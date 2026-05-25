use anyhow::Result;
use serde_json::{json, Value};
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing::{info, warn};

use crate::ipc::{EventSink, NamedPipeServer, Request, Response};
use crate::spoof::Spoofer;
use crate::xray::XrayService;

use super::state::{EngineConfig, State};

#[derive(Clone)]
pub struct Engine {
    state: Arc<RwLock<State>>,
    spoofer: Arc<Spoofer>,
    xray: Arc<XrayService>,
}

impl Engine {
    pub fn new() -> Self {
        let state = State::new();
        let spoofer = Arc::new(Spoofer::new(state.clone()));
        let xray = Arc::new(XrayService::new());
        Self { state, spoofer, xray }
    }

    pub async fn serve(self, pipe_name: &str) -> Result<()> {
        let server = NamedPipeServer::bind(pipe_name)?;
        info!("listening on pipe");
        let this = self;
        server
            .run(move |req, sink| {
                let this = this.clone();
                async move { this.handle(req, sink).await }
            })
            .await
    }

    async fn handle(self, req: Request, sink: EventSink) -> Response {
        let id = req.id;
        let result = match req.method.as_str() {
            "ping" => Ok(json!({ "pong": true })),
            "version" => Ok(json!({ "engine": env!("CARGO_PKG_VERSION") })),
            "status" => self.status().await,
            "start" => self.start(req.params, sink).await,
            "stop" => self.stop().await,
            "xray_version" => self.xray.version(req.params).await,
            "xray_status" => self.xray.status().await,
            "xray_start" => self.xray.start(req.params).await,
            "xray_stop" => self.xray.stop().await,
            other => Err(anyhow::anyhow!("unknown method `{other}`")),
        };
        match result {
            Ok(v) => Response::ok(id, v),
            Err(e) => {
                warn!(error = %e, "rpc error");
                Response::err(id, e.to_string())
            }
        }
    }

    async fn status(&self) -> Result<Value> {
        let s = self.state.read().await;
        let uptime_ms = s
            .started_at
            .map(|t| t.elapsed().as_millis() as u64)
            .unwrap_or(0);
        Ok(json!({
            "running": s.running,
            "uptime_ms": uptime_ms,
            "connections": s.stats.active_connections,
        }))
    }

    async fn start(&self, params: Value, sink: EventSink) -> Result<Value> {
        let cfg: EngineConfig = serde_json::from_value(params)?;
        let iface = self.spoofer.start(cfg, sink).await?;
        Ok(json!({ "interface_ipv4": iface }))
    }

    async fn stop(&self) -> Result<Value> {
        self.spoofer.stop().await?;
        Ok(json!({}))
    }
}
