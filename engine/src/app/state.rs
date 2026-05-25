use serde::{Deserialize, Serialize};
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::RwLock;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EngineConfig {
    pub listen_host: String,
    pub listen_port: u16,
    pub connect_ip: String,
    pub connect_port: u16,
    pub fake_sni: String,
}

#[derive(Debug, Default)]
pub struct Stats {
    pub active_connections: u32,
    pub bytes_in: u64,
    pub bytes_out: u64,
}

pub struct State {
    pub running: bool,
    pub started_at: Option<Instant>,
    pub config: Option<EngineConfig>,
    pub stats: Stats,
}

impl State {
    pub fn new() -> Arc<RwLock<Self>> {
        Arc::new(RwLock::new(Self {
            running: false,
            started_at: None,
            config: None,
            stats: Stats::default(),
        }))
    }
}
