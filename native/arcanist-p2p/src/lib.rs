//! Peer-to-peer tunnels for Arcanist, over iroh.
//!
//! A node does two things:
//!
//! - **Serve.** Every stream another node opens is carried to the server's own HTTP port on loopback, preceded by a
//!   preamble that names the remote node. The server reads that preamble to know who is asking; it alone decides what
//!   each node may do (pairing, shares, limits). Nothing here interprets HTTP.
//! - **Connect.** A local port that tunnels each TCP connection to a remote node, so the server (or an app) talks
//!   HTTP to another Arcanist as if it were local.
//! - **mStream's quick connect.** When given its secret, the node also speaks the mStream apps' tunnel
//!   (`mstream/tunnel/2`): the first stream must carry that secret, and every stream after it goes to the HTTP port
//!   like Arcanist's own, with a preamble that marks it as mStream's.
//!
//! The C ABI for .NET is in [`ffi`]; the Swift and Kotlin bindings for the mobile apps, in `mobile` (feature
//! `mobile`).

use std::collections::HashMap;
use std::net::{Ipv4Addr, SocketAddr};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use iroh::endpoint::{Connection, presets};
use iroh::protocol::{AcceptError, ProtocolHandler, Router};
use iroh::{Endpoint, EndpointAddr, EndpointId, RelayMode, RelayUrl, SecretKey, TransportAddr, Watcher};
use serde::{Deserialize, Serialize};
use tokio::io::AsyncWriteExt;
use tokio::net::{TcpListener, TcpStream};
use tokio::task::JoinHandle;

pub mod ffi;
#[cfg(feature = "mobile")]
mod mobile;

#[cfg(feature = "mobile")]
uniffi::setup_scaffolding!();

/// The protocol spoken between Arcanist nodes: plain byte tunnels to the HTTP port.
pub const ALPN: &[u8] = b"arcanist/tunnel/1";

/// Starts every tunnelled TCP connection to the server's HTTP port, followed by the server's secret and the remote
/// node's id (32 bytes each): 80 bytes in all.
pub const PREAMBLE_MAGIC: &[u8; 16] = b"ARCANIST-P2P/1\r\n";
pub const PREAMBLE_LENGTH: usize = 16 + 32 + 32;

/// The mStream apps' tunnel (their pairing code, `mstr1:`, names this node and the connect secret).
pub const MSTREAM_ALPN: &[u8] = b"mstream/tunnel/2";

/// Starts the preamble of a connection that came through mStream's tunnel; the rest is as in [`PREAMBLE_MAGIC`].
pub const MSTREAM_PREAMBLE_MAGIC: &[u8; 16] = b"ARCANIST-MST/1\r\n";

/// How a node reaches others when there is no direct path.
#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Relay {
    /// n0's public relays, and n0's address lookup so a node id alone is enough to connect.
    #[default]
    N0,
    /// The administrator's own relay, with n0's address lookup.
    Custom,
    /// Direct connections only, with n0's address lookup.
    Direct,
    /// Nothing outside: direct connections to the addresses in a ticket only. For tests and closed networks.
    Local,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Config {
    pub relay: Relay,
    /// The relay for [`Relay::Custom`].
    pub relay_url: Option<String>,
    /// The server's HTTP port on loopback, where served streams go. None for a node that only connects (an app).
    pub forward_port: Option<u16>,
    /// The server's secret for the preamble, in hex (32 bytes).
    pub preamble_secret: Option<String>,
    /// The UDP port to bind; any when None.
    pub bind_port: Option<u16>,
    /// The secret of mStream's quick connect, in hex (32 bytes): with it (and a forward port), the node also accepts
    /// the mStream apps' tunnel.
    pub mstream_secret: Option<String>,
}

/// How to reach a node: its id and, when known, its relay and direct addresses. Serialized as JSON in tickets.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct Ticket {
    pub id: String,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub relays: Vec<String>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub addrs: Vec<String>,
}

impl Ticket {
    pub fn from_addr(addr: &EndpointAddr) -> Self {
        Self {
            id: addr.id.to_string(),
            relays: addr.relay_urls().map(|u| u.to_string()).collect(),
            addrs: addr.ip_addrs().map(|a| a.to_string()).collect(),
        }
    }

    pub fn to_addr(&self) -> Result<EndpointAddr, Error> {
        let id: EndpointId = self.id.parse().map_err(|_| Error::new("invalid_ticket"))?;
        let mut addrs = Vec::new();
        for relay in &self.relays {
            addrs.push(TransportAddr::Relay(relay.parse::<RelayUrl>().map_err(|_| Error::new("invalid_ticket"))?));
        }
        for addr in &self.addrs {
            addrs.push(TransportAddr::Ip(addr.parse::<SocketAddr>().map_err(|_| Error::new("invalid_ticket"))?));
        }
        Ok(EndpointAddr::from_parts(id, addrs))
    }
}

/// What a node reports about itself.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Info {
    pub id: String,
    pub ticket: Ticket,
    /// The same address as iroh's own ticket (`endpoint…`), as other iroh apps (mStream's) read it.
    pub endpoint_ticket: String,
    /// The home relay, and whether the node is connected to it.
    pub relay: Option<String>,
    pub relay_connected: bool,
    /// The nodes with an open connection to this one, served or connected.
    pub connected: Vec<String>,
    /// The local ports opened by [`Node::connect`], with the node each reaches.
    pub tunnels: Vec<TunnelInfo>,
}

#[derive(Debug, Clone, Serialize)]
pub struct TunnelInfo {
    pub port: u16,
    pub id: String,
}

/// A failure, as a short code the caller can show or translate.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Error(pub String);

impl Error {
    pub fn new(code: impl Into<String>) -> Self {
        Self(code.into())
    }
}

impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for Error {}

/// Counts the open connections with each remote node.
#[derive(Debug, Default)]
struct Peers(Mutex<HashMap<EndpointId, usize>>);

impl Peers {
    fn opened(&self, id: EndpointId) {
        *self.0.lock().unwrap().entry(id).or_default() += 1;
    }

    fn closed(&self, id: EndpointId) {
        let mut peers = self.0.lock().unwrap();
        if let Some(count) = peers.get_mut(&id) {
            *count -= 1;
            if *count == 0 {
                peers.remove(&id);
            }
        }
    }

    fn ids(&self) -> Vec<String> {
        self.0.lock().unwrap().keys().map(|id| id.to_string()).collect()
    }
}

/// Carries each stream of an incoming connection to the server's HTTP port.
#[derive(Debug, Clone)]
struct Tunnel {
    port: u16,
    secret: [u8; 32],
    peers: Arc<Peers>,
}

impl ProtocolHandler for Tunnel {
    async fn accept(&self, connection: Connection) -> Result<(), AcceptError> {
        let remote = connection.remote_id();
        self.peers.opened(remote);
        forward_streams(&connection, self.port, PREAMBLE_MAGIC, self.secret, remote).await;
        self.peers.closed(remote);
        Ok(())
    }
}

/// mStream's tunnel: the first stream carries the connect secret (answered `OK` or `NO`); after it, each stream is a
/// connection to the HTTP port, as in Arcanist's own tunnel.
#[derive(Debug, Clone)]
struct MstreamTunnel {
    port: u16,
    preamble_secret: [u8; 32],
    connect_secret: [u8; 32],
    peers: Arc<Peers>,
}

impl ProtocolHandler for MstreamTunnel {
    async fn accept(&self, connection: Connection) -> Result<(), AcceptError> {
        let remote = connection.remote_id();
        let Ok((mut send, mut recv)) = connection.accept_bi().await else { return Ok(()) };
        let offered = tokio::time::timeout(Duration::from_secs(10), recv.read_to_end(64)).await;
        let allowed = matches!(&offered, Ok(Ok(bytes)) if fixed_time_eq(bytes, &self.connect_secret));
        let _ = send.write_all(if allowed { b"OK" } else { b"NO" }).await;
        let _ = send.finish();
        if !allowed {
            // Let the answer reach the app before the connection goes.
            let _ = tokio::time::timeout(Duration::from_secs(2), send.stopped()).await;
            connection.close(1u32.into(), b"secret");
            return Ok(());
        }

        self.peers.opened(remote);
        forward_streams(&connection, self.port, MSTREAM_PREAMBLE_MAGIC, self.preamble_secret, remote).await;
        self.peers.closed(remote);
        Ok(())
    }
}

/// Carries every stream the remote node opens to the HTTP port, each after a preamble naming it.
async fn forward_streams(connection: &Connection, port: u16, magic: &'static [u8; 16], secret: [u8; 32], remote: EndpointId) {
    loop {
        let Ok((send, recv)) = connection.accept_bi().await else { break };
        tokio::spawn(async move {
            let Ok(mut tcp) = TcpStream::connect((Ipv4Addr::LOCALHOST, port)).await else { return };
            let _ = tcp.set_nodelay(true);
            let mut preamble = Vec::with_capacity(PREAMBLE_LENGTH);
            preamble.extend_from_slice(magic);
            preamble.extend_from_slice(&secret);
            preamble.extend_from_slice(remote.as_bytes());
            if tcp.write_all(&preamble).await.is_err() {
                return;
            }
            let mut stream = tokio::io::join(recv, send);
            let _ = tokio::io::copy_bidirectional(&mut tcp, &mut stream).await;
        });
    }
}

/// Compares two secrets in the same time whatever they hold.
fn fixed_time_eq(a: &[u8], b: &[u8]) -> bool {
    a.len() == b.len() && a.iter().zip(b).fold(0u8, |difference, (x, y)| difference | (x ^ y)) == 0
}

/// A local port tunnelling to a remote node.
struct Forward {
    id: EndpointId,
    task: JoinHandle<()>,
}

/// A running node.
pub struct Node {
    router: Router,
    peers: Arc<Peers>,
    forwards: Mutex<HashMap<u16, Forward>>,
}

impl Node {
    /// Binds the endpoint and, with a forward port, starts serving.
    pub async fn start(config: Config, secret_key: [u8; 32]) -> Result<Self, Error> {
        let builder = match config.relay {
            Relay::N0 => Endpoint::builder(presets::N0),
            Relay::Custom => {
                let url: RelayUrl = config
                    .relay_url
                    .as_deref()
                    .ok_or_else(|| Error::new("relay_url_required"))?
                    .parse()
                    .map_err(|_| Error::new("invalid_relay_url"))?;
                Endpoint::builder(presets::N0).relay_mode(RelayMode::custom([url]))
            }
            Relay::Direct => Endpoint::builder(presets::N0DisableRelay),
            Relay::Local => Endpoint::builder(presets::Minimal).relay_mode(RelayMode::Disabled),
        };
        let mut builder = builder.secret_key(SecretKey::from_bytes(&secret_key));
        if let Some(port) = config.bind_port {
            builder = builder
                .bind_addr(SocketAddr::from((Ipv4Addr::UNSPECIFIED, port)))
                .map_err(|_| Error::new("invalid_bind_port"))?;
        }
        let endpoint = builder.bind().await.map_err(|e| Error::new(format!("bind_failed: {e}")))?;

        let peers = Arc::new(Peers::default());
        let mut router = Router::builder(endpoint);
        if let Some(port) = config.forward_port {
            let secret = parse_secret(config.preamble_secret.as_deref())?;
            router = router.accept(ALPN, Tunnel { port, secret, peers: peers.clone() });
            if let Some(connect) = config.mstream_secret.as_deref() {
                let connect_secret = parse_secret(Some(connect)).map_err(|_| Error::new("invalid_mstream_secret"))?;
                router = router.accept(MSTREAM_ALPN, MstreamTunnel { port, preamble_secret: secret, connect_secret, peers: peers.clone() });
            }
        }
        Ok(Self { router: router.spawn(), peers, forwards: Mutex::new(HashMap::new()) })
    }

    pub fn endpoint(&self) -> &Endpoint {
        self.router.endpoint()
    }

    pub fn id(&self) -> String {
        self.endpoint().id().to_string()
    }

    pub fn info(&self) -> Info {
        let endpoint = self.endpoint();
        let home = endpoint.home_relay_status().get().into_iter().next();
        Info {
            id: endpoint.id().to_string(),
            ticket: Ticket::from_addr(&endpoint.addr()),
            endpoint_ticket: iroh_tickets::endpoint::EndpointTicket::new(endpoint.addr()).to_string(),
            relay: home.as_ref().map(|h| h.url().to_string()),
            relay_connected: home.as_ref().is_some_and(|h| h.is_connected()),
            connected: self.peers.ids(),
            tunnels: self
                .forwards
                .lock()
                .unwrap()
                .iter()
                .map(|(port, f)| TunnelInfo { port: *port, id: f.id.to_string() })
                .collect(),
        }
    }

    /// Opens a local port that tunnels to the node in `ticket`. The connection is made now, so a node that cannot be
    /// reached fails here (`unreachable`); afterwards it is made again when it drops.
    pub async fn connect(&self, ticket: &Ticket, timeout: Duration) -> Result<u16, Error> {
        let addr = ticket.to_addr()?;
        let id = addr.id;
        if id == self.endpoint().id() {
            return Err(Error::new("own_ticket"));
        }
        let connection = tokio::time::timeout(timeout, self.endpoint().connect(addr.clone(), ALPN))
            .await
            .map_err(|_| Error::new("unreachable"))?
            .map_err(|_| Error::new("unreachable"))?;
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await.map_err(|_| Error::new("no_local_port"))?;
        let port = listener.local_addr().map_err(|_| Error::new("no_local_port"))?.port();

        let endpoint = self.endpoint().clone();
        let peers = self.peers.clone();
        peers.opened(id);
        let current = Arc::new(tokio::sync::Mutex::new(connection));
        let task = tokio::spawn(async move {
            let _guard = ClosedGuard { peers: peers.clone(), id };
            while let Ok((mut tcp, _)) = listener.accept().await {
                let _ = tcp.set_nodelay(true);
                let current = current.clone();
                let endpoint = endpoint.clone();
                let addr = addr.clone();
                tokio::spawn(async move {
                    let Some((send, recv)) = open_stream(&endpoint, &addr, &current).await else { return };
                    let mut stream = tokio::io::join(recv, send);
                    let _ = tokio::io::copy_bidirectional(&mut tcp, &mut stream).await;
                });
            }
        });
        self.forwards.lock().unwrap().insert(port, Forward { id, task });
        Ok(port)
    }

    /// Closes a local port opened by [`Node::connect`].
    pub fn disconnect(&self, port: u16) -> bool {
        match self.forwards.lock().unwrap().remove(&port) {
            Some(forward) => {
                forward.task.abort();
                true
            }
            None => false,
        }
    }

    /// Closes every tunnel and the endpoint, telling connected nodes.
    pub async fn shutdown(self) {
        for (_, forward) in self.forwards.lock().unwrap().drain() {
            forward.task.abort();
        }
        let _ = self.router.shutdown().await;
    }
}

struct ClosedGuard {
    peers: Arc<Peers>,
    id: EndpointId,
}

impl Drop for ClosedGuard {
    fn drop(&mut self) {
        self.peers.closed(self.id);
    }
}

/// A stream on the cached connection, connecting again once if it dropped.
async fn open_stream(
    endpoint: &Endpoint,
    addr: &EndpointAddr,
    current: &tokio::sync::Mutex<Connection>,
) -> Option<(iroh::endpoint::SendStream, iroh::endpoint::RecvStream)> {
    let mut connection = current.lock().await;
    if let Ok(stream) = connection.open_bi().await {
        return Some(stream);
    }
    let fresh = tokio::time::timeout(Duration::from_secs(30), endpoint.connect(addr.clone(), ALPN)).await.ok()?.ok()?;
    *connection = fresh;
    connection.open_bi().await.ok()
}

fn parse_secret(hex: Option<&str>) -> Result<[u8; 32], Error> {
    let hex = hex.ok_or_else(|| Error::new("preamble_secret_required"))?;
    if hex.len() != 64 {
        return Err(Error::new("invalid_preamble_secret"));
    }
    let mut secret = [0u8; 32];
    for (i, byte) in secret.iter_mut().enumerate() {
        *byte = u8::from_str_radix(&hex[i * 2..i * 2 + 2], 16).map_err(|_| Error::new("invalid_preamble_secret"))?;
    }
    Ok(secret)
}
