use anyhow::{anyhow, Result};
use etherparse::{Ipv4HeaderSlice, PacketBuilder, SlicedPacket, TcpHeaderSlice, TransportSlice};
use parking_lot::Mutex;
use std::collections::HashMap;
use std::net::Ipv4Addr;
use std::sync::Arc;
use std::thread;
use std::time::Duration;
use tokio::sync::oneshot;
use tracing::{debug, info, warn};
use windivert::address::WinDivertAddress;
use windivert::packet::WinDivertPacket;
use windivert::prelude::*;
use windivert::WinDivert;

pub type ConnId = (Ipv4Addr, u16, Ipv4Addr, u16);

pub struct PendingConn {
    pub fake_payload: Vec<u8>,
    pub syn_seq: Option<u32>,
    pub syn_ack_seq: Option<u32>,
    pub fake_sent: bool,
    pub completion: Option<oneshot::Sender<CompletionMsg>>,
}

#[derive(Debug)]
pub enum CompletionMsg {
    FakeDataAckRecv,
    UnexpectedClose(String),
}

type ConnTable = Arc<Mutex<HashMap<ConnId, PendingConn>>>;

pub struct Injector {
    connections: ConnTable,
    stop: Arc<Mutex<bool>>,
}

impl Injector {
    pub fn spawn(filter: String) -> Result<Self> {
        let connections: ConnTable = Arc::new(Mutex::new(HashMap::new()));
        let stop = Arc::new(Mutex::new(false));
        let conns_t = connections.clone();
        let stop_t = stop.clone();
        thread::Builder::new()
            .name("windivert-injector".into())
            .stack_size(8 * 1024 * 1024)
            .spawn(move || {
                if let Err(e) = run_loop(&filter, conns_t, stop_t) {
                    warn!(error = %e, "windivert loop ended");
                }
            })?;
        Ok(Self { connections, stop })
    }

    pub fn register(&self, id: ConnId, fake_payload: Vec<u8>) -> oneshot::Receiver<CompletionMsg> {
        let (tx, rx) = oneshot::channel();
        let mut map = self.connections.lock();
        map.insert(
            id,
            PendingConn {
                fake_payload,
                syn_seq: None,
                syn_ack_seq: None,
                fake_sent: false,
                completion: Some(tx),
            },
        );
        rx
    }

    pub fn unregister(&self, id: &ConnId) {
        self.connections.lock().remove(id);
    }
}

impl Drop for Injector {
    fn drop(&mut self) {
        *self.stop.lock() = true;
    }
}

fn run_loop(filter: &str, conns: ConnTable, stop: Arc<Mutex<bool>>) -> Result<()> {
    // Sniff handle: passive observation, kernel still processes packets normally.
    let sniff_flags = WinDivertFlags::new().set_sniff().set_recv_only();
    let sniff = WinDivert::network(filter, 0, sniff_flags)
        .map_err(|e| anyhow!("WinDivert sniff open failed: {e:?}"))?;

    // Inject handle: send-only, never matches any traffic.
    let inject_flags = WinDivertFlags::new().set_send_only();
    let inject = WinDivert::network("false", 0, inject_flags)
        .map_err(|e| anyhow!("WinDivert inject open failed: {e:?}"))?;

    info!("WinDivert sniff + inject handles opened");

    let mut buf = vec![0u8; 65_535];
    loop {
        if *stop.lock() {
            return Ok(());
        }
        let pkt = match sniff.recv(Some(&mut buf)) {
            Ok(p) => p,
            Err(e) => {
                warn!(error = ?e, "windivert sniff recv failed");
                thread::sleep(Duration::from_millis(50));
                continue;
            }
        };
        let inbound = !pkt.address.outbound();
        let len = pkt.data.as_ref().len();
        let bytes = &buf[..len];

        if let Some(fake_pkt) = process(bytes, inbound, &conns) {
            let mut addr: WinDivertAddress<windivert::layer::NetworkLayer> =
                unsafe { WinDivertAddress::<windivert::layer::NetworkLayer>::new() };
            addr.set_outbound(true);
            let injectable = WinDivertPacket {
                address: addr,
                data: std::borrow::Cow::Owned(fake_pkt),
            };
            if let Err(e) = inject.send(&injectable) {
                warn!(error = ?e, "fake inject send failed");
            } else {
                debug!("fake ClientHello injected");
            }
        }
    }
}

/// Returns the fake packet bytes to inject if this packet triggers injection.
fn process(bytes: &[u8], inbound: bool, conns: &ConnTable) -> Option<Vec<u8>> {
    let parsed = SlicedPacket::from_ip(bytes).ok()?;
    let ip = match parsed.ip.as_ref()? {
        etherparse::InternetSlice::Ipv4(h, _) => h.clone(),
        _ => return None,
    };
    let tcp = match parsed.transport.as_ref()? {
        TransportSlice::Tcp(t) => t.clone(),
        _ => return None,
    };
    let src = Ipv4Addr::from(ip.source());
    let dst = Ipv4Addr::from(ip.destination());
    let sport = tcp.source_port();
    let dport = tcp.destination_port();

    let key: ConnId = if inbound {
        (dst, dport, src, sport)
    } else {
        (src, sport, dst, dport)
    };

    let mut map = conns.lock();
    let conn = map.get_mut(&key)?;

    let seq = tcp.sequence_number();
    let ack = tcp.acknowledgment_number();
    let has_syn = tcp.syn();
    let has_ack = tcp.ack();
    let has_rst = tcp.rst();
    let has_fin = tcp.fin();
    let payload_len = parsed.payload.len();

    if inbound {
        if has_syn && has_ack && !has_rst && !has_fin && payload_len == 0 {
            if let Some(syn_seq) = conn.syn_seq {
                if ack == syn_seq.wrapping_add(1) {
                    conn.syn_ack_seq = Some(seq);
                }
            }
            return None;
        }
        if has_rst {
            signal(conn, CompletionMsg::UnexpectedClose("RST from server".into()));
            return None;
        }
        if has_ack && !has_syn && !has_fin && payload_len == 0 && conn.fake_sent {
            if let Some(sas) = conn.syn_ack_seq {
                if seq == sas.wrapping_add(1) {
                    signal(conn, CompletionMsg::FakeDataAckRecv);
                }
            }
            return None;
        }
        return None;
    }

    // outbound
    if has_syn && !has_ack && !has_rst && !has_fin && payload_len == 0 {
        conn.syn_seq = Some(seq);
        return None;
    }
    if has_ack
        && !has_syn
        && !has_rst
        && !has_fin
        && payload_len == 0
        && !conn.fake_sent
    {
        let (Some(ss), Some(sas)) = (conn.syn_seq, conn.syn_ack_seq) else {
            return None;
        };
        if seq != ss.wrapping_add(1) || ack != sas.wrapping_add(1) {
            return None;
        }
        let fake_seq = ss.wrapping_add(1).wrapping_sub(conn.fake_payload.len() as u32);
        let fake_pkt = match craft_tcp(src, dst, sport, dport, fake_seq, ack, &conn.fake_payload) {
            Ok(p) => p,
            Err(e) => {
                signal(conn, CompletionMsg::UnexpectedClose(format!("craft: {e}")));
                return None;
            }
        };
        conn.fake_sent = true;
        return Some(fake_pkt);
    }
    None
}

fn signal(conn: &mut PendingConn, msg: CompletionMsg) {
    if let Some(tx) = conn.completion.take() {
        let _ = tx.send(msg);
    }
}

fn craft_tcp(
    src: Ipv4Addr,
    dst: Ipv4Addr,
    sport: u16,
    dport: u16,
    seq: u32,
    ack: u32,
    payload: &[u8],
) -> Result<Vec<u8>> {
    let builder = PacketBuilder::ipv4(src.octets(), dst.octets(), 64)
        .tcp(sport, dport, seq, 64_240)
        .ack(ack)
        .psh();
    let mut buf = Vec::with_capacity(builder.size(payload.len()));
    builder
        .write(&mut buf, payload)
        .map_err(|e| anyhow!("packet build: {e}"))?;
    Ok(buf)
}

#[allow(dead_code)]
fn _smoke(ipv4: &[u8]) -> Option<(u16, u32)> {
    let ip = Ipv4HeaderSlice::from_slice(ipv4).ok()?;
    let tcp = TcpHeaderSlice::from_slice(&ipv4[ip.slice().len()..]).ok()?;
    Some((tcp.destination_port(), tcp.sequence_number()))
}
