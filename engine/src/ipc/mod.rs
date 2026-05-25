mod message;
mod pipe;

pub use message::{Event, Request, Response};
pub use pipe::{EventSink, NamedPipeServer};
