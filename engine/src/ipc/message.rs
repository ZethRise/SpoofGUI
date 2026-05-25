use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Deserialize)]
pub struct Request {
    pub id: u64,
    pub method: String,
    #[serde(default)]
    pub params: Value,
}

#[derive(Debug, Serialize)]
pub struct Response {
    pub id: u64,
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub result: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

impl Response {
    pub fn ok(id: u64, result: Value) -> Self {
        Self { id, ok: true, result: Some(result), error: None }
    }
    pub fn err(id: u64, error: String) -> Self {
        Self { id, ok: false, result: None, error: Some(error) }
    }
}

#[derive(Debug, Serialize)]
pub struct Event<'a, T: Serialize> {
    pub event: &'a str,
    pub data: T,
}

impl<'a, T: Serialize> Event<'a, T> {
    pub fn new(event: &'a str, data: T) -> Self {
        Self { event, data }
    }
}
