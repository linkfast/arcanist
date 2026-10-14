//! Swift and Kotlin bindings for the mobile apps (feature `mobile`), generated with uniffi:
//!
//! ```text
//! cargo run --features bindgen --bin uniffi-bindgen -- generate --library <built library> --language kotlin --out-dir <dir>
//! ```
//!
//! An app keeps one [`ArcanistP2p`] with its own secret key, reads the invitation the server shows as a QR code
//! with [`parse_invitation`], opens a local port with [`ArcanistP2p::connect`] and then talks HTTP to
//! `http://127.0.0.1:<port>` as if it were the server (pairing first, see `docs/developers/p2p.md`).

use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::{Config, Error, Node, Relay, Ticket};

#[derive(Debug, uniffi::Error)]
#[uniffi(flat_error)]
pub enum P2pError {
    /// A short code: `unreachable`, `invalid_ticket`, `invalid_invitation`, `closed`…
    Failed(String),
}

impl std::fmt::Display for P2pError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let P2pError::Failed(code) = self;
        f.write_str(code)
    }
}

impl From<Error> for P2pError {
    fn from(error: Error) -> Self {
        P2pError::Failed(error.0)
    }
}

/// What an Arcanist invitation (`arcanist://p2p?...`) holds.
#[derive(Debug, Clone, uniffi::Record)]
pub struct Invitation {
    /// `device` to pair a device with a user, `share` for another server.
    pub kind: String,
    /// The server's node, as the JSON [`ArcanistP2p::connect`] takes.
    pub ticket: String,
    /// The one-time token to send when pairing.
    pub token: String,
    /// The server's name, to show.
    pub name: String,
}

/// Reads an invitation link.
#[uniffi::export]
pub fn parse_invitation(uri: String) -> Result<Invitation, P2pError> {
    let invalid = || P2pError::Failed("invalid_invitation".to_owned());
    let query = uri.strip_prefix("arcanist://p2p?").ok_or_else(invalid)?;
    let (mut kind, mut ticket, mut token, mut name) = (None, None, None, String::new());
    for pair in query.split('&') {
        let (key, value) = pair.split_once('=').ok_or_else(invalid)?;
        let value = percent_decode(value).ok_or_else(invalid)?;
        match key {
            "kind" => kind = Some(value),
            "node" => {
                let json = String::from_utf8(base64url_decode(&value).ok_or_else(invalid)?).map_err(|_| invalid())?;
                serde_json::from_str::<Ticket>(&json).map_err(|_| invalid())?;
                ticket = Some(json);
            }
            "token" => token = Some(value),
            "name" => name = value,
            _ => {}
        }
    }
    Ok(Invitation { kind: kind.ok_or_else(invalid)?, ticket: ticket.ok_or_else(invalid)?, token: token.ok_or_else(invalid)?, name })
}

/// A new secret key for [`ArcanistP2p::new`]. Keep it: it is the device's identity once paired.
#[uniffi::export]
pub fn generate_secret_key() -> Vec<u8> {
    iroh::SecretKey::generate().to_bytes().to_vec()
}

/// A P2P node for an app: it only connects, it never serves.
#[derive(uniffi::Object)]
pub struct ArcanistP2p {
    runtime: tokio::runtime::Runtime,
    node: Mutex<Option<Node>>,
}

#[uniffi::export]
impl ArcanistP2p {
    /// `relay` is `n0` (the default), `custom` (with `relay_url`) or `direct`.
    #[uniffi::constructor]
    pub fn new(secret_key: Vec<u8>, relay: String, relay_url: Option<String>) -> Result<Arc<Self>, P2pError> {
        let key: [u8; 32] = secret_key.try_into().map_err(|_| P2pError::Failed("invalid_secret_key".to_owned()))?;
        let relay = match relay.as_str() {
            "" | "n0" => Relay::N0,
            "custom" => Relay::Custom,
            "direct" => Relay::Direct,
            _ => return Err(P2pError::Failed("invalid_relay".to_owned())),
        };
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()
            .map_err(|_| P2pError::Failed("runtime_failed".to_owned()))?;
        let node = runtime.block_on(Node::start(Config { relay, relay_url, ..Config::default() }, key))?;
        Ok(Arc::new(Self { runtime, node: Mutex::new(Some(node)) }))
    }

    /// This device's node id, which the server shows among the paired devices.
    pub fn node_id(&self) -> Result<String, P2pError> {
        self.with_node(|node| Ok(node.id()))
    }

    /// Opens a local port to the server in `ticket` (from [`parse_invitation`], or kept from an earlier one).
    pub fn connect(&self, ticket: String, timeout_ms: u32) -> Result<u16, P2pError> {
        let ticket: Ticket =
            serde_json::from_str(&ticket).map_err(|_| P2pError::Failed("invalid_ticket".to_owned()))?;
        self.with_node(|node| Ok(self.runtime.block_on(node.connect(&ticket, Duration::from_millis(timeout_ms.into())))?))
    }

    pub fn disconnect(&self, port: u16) -> Result<bool, P2pError> {
        self.with_node(|node| {
            let _runtime = self.runtime.enter();
            Ok(node.disconnect(port))
        })
    }

    /// Closes every port and the node. The object cannot be used afterwards.
    pub fn close(&self) {
        if let Some(node) = self.node.lock().unwrap().take() {
            self.runtime.block_on(node.shutdown());
        }
    }
}

impl ArcanistP2p {
    fn with_node<T>(&self, body: impl FnOnce(&Node) -> Result<T, P2pError>) -> Result<T, P2pError> {
        match self.node.lock().unwrap().as_ref() {
            Some(node) => body(node),
            None => Err(P2pError::Failed("closed".to_owned())),
        }
    }
}

fn percent_decode(text: &str) -> Option<String> {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        match bytes[i] {
            b'%' => {
                let hex = std::str::from_utf8(bytes.get(i + 1..i + 3)?).ok()?;
                out.push(u8::from_str_radix(hex, 16).ok()?);
                i += 3;
            }
            b'+' => {
                out.push(b' ');
                i += 1;
            }
            byte => {
                out.push(byte);
                i += 1;
            }
        }
    }
    String::from_utf8(out).ok()
}

fn base64url_decode(text: &str) -> Option<Vec<u8>> {
    let value = |c: u8| -> Option<u32> {
        Some(match c {
            b'A'..=b'Z' => c - b'A',
            b'a'..=b'z' => c - b'a' + 26,
            b'0'..=b'9' => c - b'0' + 52,
            b'-' => 62,
            b'_' => 63,
            _ => return None,
        } as u32)
    };
    let text = text.trim_end_matches('=').as_bytes();
    let mut out = Vec::with_capacity(text.len() * 3 / 4);
    for chunk in text.chunks(4) {
        let mut buffer = 0u32;
        for (i, &c) in chunk.iter().enumerate() {
            buffer |= value(c)? << (18 - 6 * i);
        }
        let bytes = buffer.to_be_bytes();
        out.extend_from_slice(&bytes[1..chunk.len()]);
    }
    Some(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_an_invitation() {
        // {"id":"abc"} in base64url, without padding.
        let invitation =
            parse_invitation("arcanist://p2p?kind=device&node=eyJpZCI6ImFiYyJ9&token=t0k&name=Casa%20de%20Ana".to_owned())
                .unwrap();
        assert_eq!(invitation.kind, "device");
        assert_eq!(invitation.ticket, r#"{"id":"abc"}"#);
        assert_eq!(invitation.token, "t0k");
        assert_eq!(invitation.name, "Casa de Ana");
        assert!(parse_invitation("https://example.com".to_owned()).is_err());
    }
}
