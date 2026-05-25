use anyhow::Result;
use std::future::Future;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::windows::named_pipe::{NamedPipeServer as RawPipe, ServerOptions};
use tokio::sync::mpsc;
use tracing::{debug, info};

use super::message::{Request, Response};

/// Sink the request handler can clone and push asynchronous events into.
/// Each pushed `String` MUST be one valid line of JSON without a trailing
/// newline (the writer task appends it).
pub type EventSink = mpsc::Sender<String>;

pub struct NamedPipeServer {
    path: String,
}

impl NamedPipeServer {
    pub fn bind(name: &str) -> Result<Self> {
        let path = format!(r"\\.\pipe\{name}");
        Ok(Self { path })
    }

    pub async fn run<F, Fut>(self, handler: F) -> Result<()>
    where
        F: Fn(Request, EventSink) -> Fut + Send + Sync + Clone + 'static,
        Fut: Future<Output = Response> + Send + 'static,
    {
        loop {
            let server: RawPipe = ServerOptions::new()
                .first_pipe_instance(false)
                .create(&self.path)?;
            info!(pipe = %self.path, "awaiting client");
            server.connect().await?;
            debug!("client connected");

            let handler = handler.clone();
            tokio::spawn(async move {
                if let Err(e) = serve_session(server, handler).await {
                    tracing::warn!(error = %e, "session ended");
                }
            });
        }
    }
}

async fn serve_session<F, Fut>(stream: RawPipe, handler: F) -> Result<()>
where
    F: Fn(Request, EventSink) -> Fut + Send + Sync + Clone + 'static,
    Fut: Future<Output = Response> + Send + 'static,
{
    let (rx, mut tx) = tokio::io::split(stream);
    let mut reader = BufReader::new(rx);

    // mpsc fan-in. Responses AND events both go through the same writer.
    let (out_tx, mut out_rx) = mpsc::channel::<String>(256);

    let writer = tokio::spawn(async move {
        while let Some(mut line) = out_rx.recv().await {
            line.push('\n');
            if tx.write_all(line.as_bytes()).await.is_err() {
                break;
            }
            let _ = tx.flush().await;
        }
    });

    let mut line = String::new();
    loop {
        line.clear();
        let n = reader.read_line(&mut line).await?;
        if n == 0 {
            break;
        }
        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }
        let req: Request = match serde_json::from_str(trimmed) {
            Ok(v) => v,
            Err(e) => {
                let resp = Response::err(0, format!("malformed request: {e}"));
                let _ = out_tx.send(serde_json::to_string(&resp)?).await;
                continue;
            }
        };
        let sink = out_tx.clone();
        let handler = handler.clone();
        let out_tx2 = out_tx.clone();
        tokio::spawn(async move {
            let resp = handler(req, sink).await;
            if let Ok(s) = serde_json::to_string(&resp) {
                let _ = out_tx2.send(s).await;
            }
        });
    }

    drop(out_tx);
    let _ = writer.await;
    Ok(())
}
