//! mStream's quick connect: an mStream app dials `mstream/tunnel/2`, proves the connect secret on its first stream, and
//! every stream after it reaches the HTTP port marked as mStream's. Two nodes on this machine, nothing outside.

use std::net::Ipv4Addr;
use std::time::Duration;

use arcanist_p2p::{Config, MSTREAM_ALPN, MSTREAM_PREAMBLE_MAGIC, Node, PREAMBLE_LENGTH, Relay, Ticket};
use iroh::endpoint::Connection;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;

const PREAMBLE: &str = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
const CONNECT: [u8; 32] = [7; 32];

/// Answers in upper case after reading the preamble, which it hands back through the channel.
async fn fake_server() -> (u16, tokio::sync::mpsc::UnboundedReceiver<Vec<u8>>) {
    let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let (sender, receiver) = tokio::sync::mpsc::unbounded_channel();
    tokio::spawn(async move {
        loop {
            let (mut socket, _) = listener.accept().await.unwrap();
            let sender = sender.clone();
            tokio::spawn(async move {
                let mut preamble = vec![0u8; PREAMBLE_LENGTH];
                socket.read_exact(&mut preamble).await.unwrap();
                sender.send(preamble).unwrap();
                let mut buffer = [0u8; 1024];
                loop {
                    let read = socket.read(&mut buffer).await.unwrap();
                    if read == 0 {
                        break;
                    }
                    socket.write_all(&buffer[..read].to_ascii_uppercase()).await.unwrap();
                }
            });
        }
    });
    (port, receiver)
}

async fn node(seed: u8, forward_port: Option<u16>, mstream: bool) -> Node {
    Node::start(
        Config {
            relay: Relay::Local,
            forward_port,
            preamble_secret: forward_port.map(|_| PREAMBLE.to_owned()),
            mstream_secret: mstream.then(|| CONNECT.iter().map(|b| format!("{b:02x}")).collect()),
            ..Config::default()
        },
        [seed; 32],
    )
    .await
    .unwrap()
}

/// What an mStream app does after scanning the code: dial the tunnel and send the secret on the first stream.
async fn dial(app: &Node, server: &Node, secret: &[u8]) -> (Connection, Vec<u8>) {
    let mut ticket: Ticket = server.info().ticket;
    let port = server.endpoint().bound_sockets().iter().find(|a| a.is_ipv4()).unwrap().port();
    ticket.addrs = vec![format!("127.0.0.1:{port}")];
    let connection = app.endpoint().connect(ticket.to_addr().unwrap(), MSTREAM_ALPN).await.unwrap();
    let (mut send, mut recv) = connection.open_bi().await.unwrap();
    send.write_all(secret).await.unwrap();
    send.finish().unwrap();
    let answer = tokio::time::timeout(Duration::from_secs(10), recv.read_to_end(16)).await.unwrap().unwrap_or_default();
    (connection, answer)
}

#[tokio::test(flavor = "multi_thread")]
async fn an_mstream_app_with_the_secret_reaches_the_http_port() {
    let (http_port, mut preambles) = fake_server().await;
    let server = node(1, Some(http_port), true).await;
    let app = node(2, None, false).await;

    // The address is also given as iroh's own ticket, which the pairing code carries.
    let endpoint_ticket = server.info().endpoint_ticket;
    assert!(endpoint_ticket.starts_with("endpoint"));
    let parsed: iroh_tickets::endpoint::EndpointTicket = endpoint_ticket.parse().unwrap();
    assert_eq!(parsed.endpoint_addr().id, server.endpoint().id());

    let (connection, answer) = dial(&app, &server, &CONNECT).await;
    assert_eq!(answer, b"OK");

    // One stream, one HTTP connection.
    let (mut send, mut recv) = connection.open_bi().await.unwrap();
    send.write_all(b"get /api").await.unwrap();
    let mut reply = [0u8; 8];
    recv.read_exact(&mut reply).await.unwrap();
    assert_eq!(&reply, b"GET /API");
    let preamble = preambles.recv().await.unwrap();
    assert_eq!(&preamble[..16], MSTREAM_PREAMBLE_MAGIC);
    assert_eq!(&preamble[48..], app.endpoint().id().as_bytes());

    app.shutdown().await;
    server.shutdown().await;
}

#[tokio::test(flavor = "multi_thread")]
async fn a_wrong_secret_is_refused_and_a_node_without_one_does_not_speak_mstream() {
    let (http_port, _) = fake_server().await;
    let server = node(1, Some(http_port), true).await;
    let app = node(2, None, false).await;
    let (_, answer) = dial(&app, &server, &[8; 32]).await;
    assert_eq!(answer, b"NO");
    app.shutdown().await;
    server.shutdown().await;

    let plain = node(3, Some(http_port), false).await;
    let app = node(4, None, false).await;
    let port = plain.endpoint().bound_sockets().iter().find(|a| a.is_ipv4()).unwrap().port();
    let mut ticket = plain.info().ticket;
    ticket.addrs = vec![format!("127.0.0.1:{port}")];
    assert!(app.endpoint().connect(ticket.to_addr().unwrap(), MSTREAM_ALPN).await.is_err());
    app.shutdown().await;
    plain.shutdown().await;
}
