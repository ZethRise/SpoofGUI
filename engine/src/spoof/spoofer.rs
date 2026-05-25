use anyhow::{anyhow, Result};
use serde_json::json;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::sync::{Mutex, RwLock};
use tracing::{info, warn};

use crate::app::state::State;
use crate::app::EngineConfig;
use crate::ipc::{Event, EventSink};

use super::forwarder;
use super::injector::Injector;
use super::net_iface;

pub struct Spoofer {
    state: Arc<RwLock<State>>,
    handle: Mutex<Option<RunHandle>>,
}

struct RunHandle {
    forwarder: tokio::task::JoinHandle<()>,
    stats: tokio::task::JoinHandle<()>,
    _injector: Arc<Injector>,
}

impl Spoofer {
    pub fn new(state: Arc<RwLock<State>>) -> Self {
        Self {
            state,
            handle: Mutex::new(None),
        }
    }

    pub async fn start(&self, cfg: EngineConfig, sink: EventSink) -> Result<String> {
        let mut slot = self.handle.lock().await;
        if slot.is_some() {
            return Err(anyhow!("already running"));
        }

        let iface = net_iface::get_default_interface_ipv4(&cfg.connect_ip)?;
        info!(?cfg, %iface, "starting spoofer");

        let filter = format!(
            "tcp and ((ip.SrcAddr == {iface} and ip.DstAddr == {connect}) or (ip.SrcAddr == {connect} and ip.DstAddr == {iface}))",
            iface = iface,
            connect = cfg.connect_ip,
        );
        let injector = Arc::new(Injector::spawn(filter)?);

        {
            let mut s = self.state.write().await;
            s.running = true;
            s.started_at = Some(Instant::now());
            s.config = Some(cfg.clone());
            s.stats = Default::default();
        }

        let state_fwd = self.state.clone();
        let inj = injector.clone();
        let task = tokio::spawn(async move {
            if let Err(e) = forwarder::run(state_fwd, cfg, iface, inj).await {
                warn!(error = %e, "forwarder loop ended");
            }
        });

        let state_stats = self.state.clone();
        let stats_task = tokio::spawn(async move {
            run_stats_ticker(state_stats, sink).await;
        });

        *slot = Some(RunHandle {
            forwarder: task,
            stats: stats_task,
            _injector: injector,
        });

        Ok(iface.to_string())
    }

    pub async fn stop(&self) -> Result<()> {
        let mut slot = self.handle.lock().await;
        if let Some(h) = slot.take() {
            h.forwarder.abort();
            h.stats.abort();
            // dropping h._injector signals the WinDivert thread to exit
        }
        let mut s = self.state.write().await;
        s.running = false;
        s.started_at = None;
        s.stats = Default::default();
        Ok(())
    }
}

/// Push one `stats` event per second over the IPC pipe until the sink closes
/// or the task is aborted.
async fn run_stats_ticker(state: Arc<RwLock<State>>, sink: EventSink) {
    let mut ticker = tokio::time::interval(Duration::from_secs(1));
    ticker.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
    loop {
        ticker.tick().await;
        let snapshot = {
            let s = state.read().await;
            let uptime_ms = s
                .started_at
                .map(|t| t.elapsed().as_millis() as u64)
                .unwrap_or(0);
            json!({
                "running": s.running,
                "uptime_ms": uptime_ms,
                "connections": s.stats.active_connections,
                "bytes_in": s.stats.bytes_in,
                "bytes_out": s.stats.bytes_out,
            })
        };
        let evt = Event::new("stats", &snapshot);
        let line = match serde_json::to_string(&evt) {
            Ok(s) => s,
            Err(_) => continue,
        };
        if sink.send(line).await.is_err() {
            return; // client disconnected
        }
    }
}
