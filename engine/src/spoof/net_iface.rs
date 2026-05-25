use anyhow::{anyhow, Result};
use std::net::{IpAddr, Ipv4Addr, SocketAddr, UdpSocket};

#[cfg(windows)]
use windows_sys::Win32::NetworkManagement::IpHelper::{
    GetAdaptersAddresses, GAA_FLAG_SKIP_ANYCAST, GAA_FLAG_SKIP_DNS_SERVER,
    GAA_FLAG_SKIP_MULTICAST, IP_ADAPTER_ADDRESSES_LH,
};
#[cfg(windows)]
use windows_sys::Win32::Networking::WinSock::{AF_INET, AF_UNSPEC, SOCKADDR_IN};

/// Returns the IPv4 of the interface that should carry traffic to `remote_ip`.
///
/// Strategy:
/// 1. Enumerate physical (non-virtual) adapters with status Up, an IPv4
///    address, and at least one gateway. Prefer those.
/// 2. Fall back to UDP routing probe (which trusts the Windows routing table
///    verbatim — picks WSL/Hyper-V adapters if they hold the default route).
pub fn get_default_interface_ipv4(remote_ip: &str) -> Result<Ipv4Addr> {
    #[cfg(windows)]
    {
        if let Some(ip) = pick_physical_adapter_ipv4() {
            return Ok(ip);
        }
    }
    udp_probe(remote_ip)
}

fn udp_probe(remote_ip: &str) -> Result<Ipv4Addr> {
    let dst: IpAddr = remote_ip
        .parse()
        .map_err(|e| anyhow!("invalid remote ip `{remote_ip}`: {e}"))?;
    let sock = UdpSocket::bind("0.0.0.0:0")?;
    sock.connect(SocketAddr::new(dst, 53))?;
    match sock.local_addr()?.ip() {
        IpAddr::V4(v4) if !v4.is_unspecified() => Ok(v4),
        other => Err(anyhow!("no IPv4 default interface (got {other})")),
    }
}

#[cfg(windows)]
fn pick_physical_adapter_ipv4() -> Option<Ipv4Addr> {
    const VIRTUAL_HINTS: &[&str] = &[
        "vethernet",
        "wsl",
        "hyper-v",
        "virtualbox",
        "vmware",
        "tap-",
        "loopback",
        "tunnel",
        "bluetooth",
    ];

    let mut size: u32 = 16 * 1024;
    let mut buf: Vec<u8> = vec![0u8; size as usize];
    loop {
        let res = unsafe {
            GetAdaptersAddresses(
                AF_UNSPEC as u32,
                GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER,
                std::ptr::null_mut(),
                buf.as_mut_ptr() as *mut IP_ADAPTER_ADDRESSES_LH,
                &mut size,
            )
        };
        const ERROR_BUFFER_OVERFLOW: u32 = 111;
        const NO_ERROR: u32 = 0;
        if res == NO_ERROR {
            break;
        }
        if res == ERROR_BUFFER_OVERFLOW {
            buf.resize(size as usize, 0);
            continue;
        }
        return None;
    }

    let mut candidates: Vec<(Ipv4Addr, u32, String)> = Vec::new();
    let mut cur = buf.as_ptr() as *const IP_ADAPTER_ADDRESSES_LH;
    while !cur.is_null() {
        let adapter = unsafe { &*cur };
        if adapter.OperStatus != 1 {
            // 1 == IfOperStatusUp
            cur = adapter.Next;
            continue;
        }

        let friendly = read_wide(adapter.FriendlyName).to_lowercase();
        let desc = read_wide(adapter.Description).to_lowercase();
        let is_virtual = VIRTUAL_HINTS
            .iter()
            .any(|h| friendly.contains(h) || desc.contains(h));

        let has_gateway = !adapter.FirstGatewayAddress.is_null();

        let mut ip_addr: Option<Ipv4Addr> = None;
        let mut unicast = adapter.FirstUnicastAddress;
        while !unicast.is_null() {
            let u = unsafe { &*unicast };
            let sa = u.Address.lpSockaddr;
            if !sa.is_null() && unsafe { (*sa).sa_family } == AF_INET {
                let sin = sa as *const SOCKADDR_IN;
                let raw = unsafe { (*sin).sin_addr.S_un.S_addr };
                let octets = raw.to_ne_bytes();
                let v4 = Ipv4Addr::new(octets[0], octets[1], octets[2], octets[3]);
                if !v4.is_loopback() && !v4.is_unspecified() {
                    ip_addr = Some(v4);
                    break;
                }
            }
            unicast = u.Next;
        }

        if let Some(ip) = ip_addr {
            if is_virtual {
                cur = adapter.Next;
                continue;
            }
            if !has_gateway {
                cur = adapter.Next;
                continue;
            }
            let metric = adapter.Ipv4Metric;
            candidates.push((ip, metric, friendly.clone()));
        }

        cur = adapter.Next;
    }

    candidates.sort_by_key(|(_, metric, _)| *metric);
    if let Some((ip, _, name)) = candidates.first() {
        tracing::info!(adapter = %name, ip = %ip, "selected outbound iface");
        return Some(*ip);
    }
    None
}

#[cfg(windows)]
fn read_wide(ptr: *const u16) -> String {
    if ptr.is_null() {
        return String::new();
    }
    let mut len = 0usize;
    unsafe {
        while *ptr.add(len) != 0 {
            len += 1;
        }
        let slice = std::slice::from_raw_parts(ptr, len);
        String::from_utf16_lossy(slice)
    }
}
