//! The JS transport types used by the mechanically adapted upstream wrapper.
pub use serde_json::{from_value, to_value, Value as JsValue};

#[derive(Debug)]
pub struct JsError(String);
impl JsError {
    pub fn new(message: &str) -> Self {
        Self(message.to_owned())
    }
}
impl std::fmt::Display for JsError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        self.0.fmt(f)
    }
}
impl From<serde_json::Error> for JsError {
    fn from(error: serde_json::Error) -> Self {
        Self(error.to_string())
    }
}
