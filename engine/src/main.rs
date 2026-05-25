use anyhow::Result;
use clap::Parser;
use tracing::info;

mod app;
mod ipc;
mod spoof;
mod xray;

#[derive(Parser, Debug)]
#[command(name = "spoof-engine", version, about)]
struct Args {
    /// Windows named pipe name (without \\.\pipe\ prefix). Required.
    #[arg(long)]
    pipe: String,

    /// Log level filter (RUST_LOG-style).
    #[arg(long, default_value = "info")]
    log: String,
}

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    let args = Args::parse();

    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::new(&args.log))
        .with_writer(std::io::stderr)
        .init();

    info!(pipe = %args.pipe, "spoof-engine starting");
    app::Engine::new().serve(&args.pipe).await
}
