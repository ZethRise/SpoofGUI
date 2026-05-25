use anyhow::{anyhow, Result};
use socket2::{Domain, Protocol, Socket, Type};
use std::net::{Ipv4Addr, SocketAddr, SocketAddrV4};
use std::sync::Arc;
use std::time::Duration;
use tokio::io::AsyncWriteExt;
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::RwLock;
use tokio::time::timeout;
use tracing::{debug, info, warn};

use super::client_hello;
use super::injector::{CompletionMsg, ConnId, Injector};
use crate::app::state::State;
use crate::app::EngineConfig;

/// Bind listener, accept incoming flows, set up the fake-injection per flow,
/// then bidirectionally relay once the trick has tripped the middlebox.
pub async fn run(
    state: Arc<RwLock<State>>,
    cfg: EngineConfig,
    iface_ipv4: Ipv4Addr,
    injector: Arc<Injector>,
) -> Result<()> {
    let listen_addr: SocketAddr = format!("{}:{}", cfg.listen_host, cfg.listen_port)
        .parse()
        .map_err(|e| anyhow!("bad listen addr: {e}"))?;
    let listener = TcpListener::bind(listen_addr).await?;
    info!(%listen_addr, "forwarder listening");

    let connect_ip: Ipv4Addr = cfg
        .connect_ip
        .parse()
        .map_err(|e| anyhow!("bad CONNECT_IP `{}`: {e}", cfg.connect_ip))?;
    let connect_port = cfg.connect_port;
    let fake_sni = cfg.fake_sni.into_bytes();

    loop {
        let (incoming, peer) = match listener.accept().await {
            Ok(p) => p,
            Err(e) => {
                warn!(error = %e, "accept failed");
                continue;
            }
        };
        debug!(%peer, "accepted");
        let state_t = state.clone();
        let injector_t = injector.clone();
        let fake_t = fake_sni.clone();
        tokio::spawn(async move {
            if let Err(e) = handle(state_t, injector_t, incoming, iface_ipv4, connect_ip, connect_port, fake_t).await {
                debug!(error = %e, "flow ended");
            }
        });
    }
}

async fn handle(
    state: Arc<RwLock<State>>,
    injector: Arc<Injector>,
    incoming: TcpStream,
    iface: Ipv4Addr,
    connect_ip: Ipv4Addr,
    connect_port: u16,
    fake_sni: Vec<u8>,
) -> Result<()> {
    let fake_payload = client_hello::build(&fake_sni);

    // Bind an outbound socket on the chosen interface, port = ephemeral.
    let sock = Socket::new(Domain::IPV4, Type::STREAM, Some(Protocol::TCP))?;
    sock.set_reuse_address(true).ok();
    sock.bind(&SocketAddrV4::new(iface, 0).into())?;
    let local: SocketAddrV4 = sock.local_addr()?.as_socket_ipv4().ok_or_else(|| anyhow!("no v4 local"))?;
    let src_port = local.port();

    // Register BEFORE connecting so the injector catches the very first SYN.
    let id: ConnId = (iface, src_port, connect_ip, connect_port);
    let waiter = injector.register(id, fake_payload);

    {
        let mut s = state.write().await;
        s.stats.active_connections = s.stats.active_connections.saturating_add(1);
    }

    let outgoing = match connect_bound(sock, connect_ip, connect_port).await {
        Ok(s) => s,
        Err(e) => {
            injector.unregister(&id);
            let mut s = state.write().await;
            s.stats.active_connections = s.stats.active_connections.saturating_sub(1);
            return Err(anyhow!("connect: {e}"));
        }
    };

    let dec_conn = || async {
        let mut s = state.write().await;
        s.stats.active_connections = s.stats.active_connections.saturating_sub(1);
    };

    match timeout(Duration::from_secs(2), waiter).await {
        Ok(Ok(CompletionMsg::FakeDataAckRecv)) => {}
        Ok(Ok(CompletionMsg::UnexpectedClose(why))) => {
            injector.unregister(&id);
            dec_conn().await;
            return Err(anyhow!("teardown: {why}"));
        }
        _ => {
            injector.unregister(&id);
            dec_conn().await;
            return Err(anyhow!("handshake timeout"));
        }
    }
    injector.unregister(&id);

    let r = relay(incoming, outgoing).await;
    dec_conn().await;
    r
}

/// Connect a pre-bound socket2 socket, then hand it over to tokio.
async fn connect_bound(sock: Socket, ip: Ipv4Addr, port: u16) -> Result<TcpStream> {
    let addr: SocketAddr = SocketAddrV4::new(ip, port).into();
    // socket2 connect is blocking; run on a blocking thread so we don't park
    // the tokio reactor. The handshake is fast anyway.
    let sock = tokio::task::spawn_blocking(move || -> std::io::Result<Socket> {
        sock.connect(&addr.into())?;
        Ok(sock)
    })
    .await
    .map_err(|e| anyhow!("join: {e}"))??;
    sock.set_nonblocking(true)?;
    let std_stream: std::net::TcpStream = sock.into();
    TcpStream::from_std(std_stream).map_err(|e| anyhow!("adopt: {e}"))
}

async fn relay(a: TcpStream, b: TcpStream) -> Result<()> {
    let (mut ar, mut aw) = a.into_split();
    let (mut br, mut bw) = b.into_split();
    let a2b = async move {
        let _ = tokio::io::copy(&mut ar, &mut bw).await;
        let _ = bw.shutdown().await;
    };
    let b2a = async move {
        let _ = tokio::io::copy(&mut br, &mut aw).await;
        let _ = aw.shutdown().await;
    };
    tokio::join!(a2b, b2a);
    Ok(())
}
