use anyhow::{anyhow, Context, Result};
use serde::Deserialize;
use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use std::process::Stdio;
use tokio::fs;
use tokio::process::{Child, Command};
use tokio::sync::Mutex;
use tracing::{info, warn};

#[derive(Default)]
pub struct XrayService {
    child: Mutex<Option<Child>>,
}

#[derive(Debug, Deserialize)]
struct XrayPaths {
    xray_exe: PathBuf,
}

#[derive(Debug, Deserialize)]
struct XrayStartRequest {
    xray_exe: PathBuf,
    config_path: PathBuf,
    profile: XrayProfile,
}

#[derive(Debug, Deserialize)]
#[allow(dead_code)]
struct XrayProfile {
    name: String,
    protocol: String,
    mode: String,
    address: String,
    port: u16,
    user_id: String,
    security: String,
    transport: String,
    server_name: String,
    raw_uri: String,
}

impl XrayService {
    pub fn new() -> Self {
        Self::default()
    }

    pub async fn version(&self, params: Value) -> Result<Value> {
        let paths: XrayPaths = serde_json::from_value(params)?;
        let out = run_capture(&paths.xray_exe, paths.xray_exe.parent(), ["version"]).await?;
        let line = out.lines().next().unwrap_or("xray ready").to_string();
        Ok(json!({ "version": line }))
    }

    pub async fn status(&self) -> Result<Value> {
        let guard = self.child.lock().await;
        Ok(json!({ "running": guard.is_some() }))
    }

    pub async fn start(&self, params: Value) -> Result<Value> {
        let req: XrayStartRequest = serde_json::from_value(params)?;
        // Tunnel and SystemProxy both run the same xray inbounds (SOCKS5 + HTTP);
        // the C# host layers tun2socks / Windows proxy registry on top.
        {
            let guard = self.child.lock().await;
            if guard.is_some() {
                return Ok(json!({ "running": true }));
            }
        }

        let config = build_proxy_config(&req.profile)?;
        if let Some(parent) = req.config_path.parent() {
            fs::create_dir_all(parent).await?;
        }
        fs::write(&req.config_path, config).await?;

        let config_arg = req.config_path.to_string_lossy().to_string();
        run_capture(
            &req.xray_exe,
            req.xray_exe.parent(),
            ["run", "-test", "-c", config_arg.as_str()],
        )
        .await?;

        let child = Command::new(&req.xray_exe)
            .arg("run")
            .arg("-c")
            .arg(&req.config_path)
            .current_dir(req.xray_exe.parent().unwrap_or_else(|| Path::new(".")))
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .with_context(|| format!("failed to start xray: {}", req.xray_exe.display()))?;

        let pid = child.id().unwrap_or(0);
        info!(pid, profile = %req.profile.name, "xray started");
        let mut guard = self.child.lock().await;
        *guard = Some(child);
        Ok(json!({ "running": true, "pid": pid, "socks": "127.0.0.1:20882", "http": "127.0.0.1:20883" }))
    }

    pub async fn stop(&self) -> Result<Value> {
        let mut guard = self.child.lock().await;
        if let Some(mut child) = guard.take() {
            if let Err(e) = child.kill().await {
                warn!(error = %e, "failed to stop xray");
            }
        }
        Ok(json!({}))
    }
}

async fn run_capture<const N: usize>(exe: &Path, cwd: Option<&Path>, args: [&str; N]) -> Result<String> {
    let mut cmd = Command::new(exe);
    cmd.args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some(cwd) = cwd {
        cmd.current_dir(cwd);
    }

    let out = cmd.output().await?;
    let stdout = String::from_utf8_lossy(&out.stdout).to_string();
    let stderr = String::from_utf8_lossy(&out.stderr).to_string();
    if !out.status.success() {
        return Err(anyhow!(if stderr.trim().is_empty() { stdout } else { stderr }));
    }
    Ok(if stdout.trim().is_empty() { stderr } else { stdout })
}

fn build_proxy_config(profile: &XrayProfile) -> Result<String> {
    let outbound = build_outbound(profile)?;
    let config = json!({
        "log": { "loglevel": "warning" },
        "inbounds": [
            {
                "tag": "socks-in",
                "listen": "127.0.0.1",
                "port": 20882,
                "protocol": "socks",
                "settings": { "udp": true, "auth": "noauth" }
            },
            {
                "tag": "http-in",
                "listen": "127.0.0.1",
                "port": 20883,
                "protocol": "http"
            }
        ],
        "outbounds": [
            outbound,
            { "protocol": "freedom", "tag": "direct" },
            { "protocol": "blackhole", "tag": "block" }
        ]
    });
    Ok(serde_json::to_string_pretty(&config)?)
}

fn build_outbound(p: &XrayProfile) -> Result<Value> {
    match p.protocol.to_lowercase().as_str() {
        "vless" => {
            let query = parse_query(&p.raw_uri);
            let mut user = json!({ "id": p.user_id, "encryption": "none" });
            if let Some(flow) = query.get("flow") {
                user["flow"] = json!(flow);
            }
            server_protocol("vless", p, user)
        }
        "vmess" => server_protocol("vmess", p, json!({ "id": p.user_id, "alterId": 0, "security": "auto" })),
        "trojan" => {
            let mut outbound = json!({
                "protocol": "trojan",
                "tag": "proxy",
                "settings": {
                    "servers": [{ "address": p.address, "port": p.port, "password": p.user_id }]
                }
            });
            add_stream_settings(&mut outbound, p);
            Ok(outbound)
        }
        "ss" => {
            let mut parts = p.user_id.splitn(2, ':');
            let first = parts.next().unwrap_or("aes-128-gcm");
            let second = parts.next();
            let (method, password) = match second {
                Some(pass) => (first, pass),
                None => ("aes-128-gcm", first),
            };
            Ok(json!({
                "protocol": "shadowsocks",
                "tag": "proxy",
                "settings": {
                    "servers": [{ "address": p.address, "port": p.port, "method": method, "password": password }]
                }
            }))
        }
        other => Err(anyhow!("protocol not wired yet: {other}")),
    }
}

fn server_protocol(protocol: &str, p: &XrayProfile, user: Value) -> Result<Value> {
    let mut outbound = json!({
        "protocol": protocol,
        "tag": "proxy",
        "settings": {
            "vnext": [{
                "address": p.address,
                "port": p.port,
                "users": [user]
            }]
        }
    });
    add_stream_settings(&mut outbound, p);
    Ok(outbound)
}

fn add_stream_settings(outbound: &mut Value, p: &XrayProfile) {
    let query = parse_query(&p.raw_uri);
    let network = if p.transport.trim().is_empty() { "tcp" } else { p.transport.as_str() };
    let security = match p.security.to_lowercase().as_str() {
        "tls" => "tls",
        "reality" => "reality",
        _ => "none",
    };
    let server_name = if p.server_name.trim().is_empty() { p.address.as_str() } else { p.server_name.as_str() };
    let mut stream = json!({ "network": network, "security": security });

    if security == "tls" {
        stream["tlsSettings"] = json!({ "serverName": server_name, "allowInsecure": false });
    } else if security == "reality" {
        stream["realitySettings"] = json!({
            "serverName": server_name,
            "fingerprint": query.get("fp").map(String::as_str).unwrap_or("chrome"),
            "publicKey": query.get("pbk").map(String::as_str).unwrap_or(""),
            "shortId": query.get("sid").map(String::as_str).unwrap_or(""),
            "spiderX": query.get("spx").map(String::as_str).unwrap_or("")
        });
    }

    if network.eq_ignore_ascii_case("ws") {
        stream["wsSettings"] = json!({
            "path": query.get("path").map(String::as_str).unwrap_or("/"),
            "headers": { "Host": query.get("host").map(String::as_str).unwrap_or(server_name) }
        });
    } else if network.eq_ignore_ascii_case("grpc") {
        stream["grpcSettings"] = json!({
            "serviceName": query.get("serviceName").map(String::as_str).unwrap_or("")
        });
    }

    outbound["streamSettings"] = stream;
}

fn parse_query(raw_uri: &str) -> std::collections::HashMap<String, String> {
    let mut map = std::collections::HashMap::new();
    let Some(query) = raw_uri.split_once('?').map(|(_, q)| q.split('#').next().unwrap_or(q)) else {
        return map;
    };
    for pair in query.split('&').filter(|p| !p.is_empty()) {
        if let Some((k, v)) = pair.split_once('=') {
            map.insert(k.to_string(), percent_decode(v));
        }
    }
    map
}

fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Ok(hex) = u8::from_str_radix(&value[i + 1..i + 3], 16) {
                out.push(hex);
                i += 3;
                continue;
            }
        }
        out.push(if bytes[i] == b'+' { b' ' } else { bytes[i] });
        i += 1;
    }
    String::from_utf8_lossy(&out).to_string()
}
