//! Two nodes on this machine, without relays or address lookup: one serves a fake HTTP port, the other connects.

use std::net::Ipv4Addr;
use std::time::Duration;

use arcanist_p2p::{Config, Node, PREAMBLE_LENGTH, PREAMBLE_MAGIC, Relay, Ticket};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

const SECRET: &str = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

fn key(seed: u8) -> [u8; 32] {
    [seed; 32]
}

/// A ticket with only the node's loopback address.
fn local_ticket(node: &Node) -> Ticket {
    let mut ticket = node.info().ticket;
    let port = node.endpoint().bound_sockets().iter().find(|a| a.is_ipv4()).unwrap().port();
    ticket.addrs = vec![format!("127.0.0.1:{port}")];
    ticket
}

/// Echoes in upper case after reading the preamble, which it hands back through the channel.
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

async fn local_node(seed: u8, forward_port: Option<u16>) -> Node {
    Node::start(
        Config {
            relay: Relay::Local,
            forward_port,
            preamble_secret: forward_port.map(|_| SECRET.to_owned()),
            ..Config::default()
        },
        key(seed),
    )
    .await
    .unwrap()
}

#[tokio::test(flavor = "multi_thread")]
async fn a_connected_port_reaches_the_served_port_with_the_preamble() {
    let (http_port, mut preambles) = fake_server().await;
    let server = local_node(1, Some(http_port)).await;
    let client = local_node(2, None).await;

    let port = client.connect(&local_ticket(&server), Duration::from_secs(10)).await.unwrap();

    // Two TCP connections through the same tunnel, each its own stream.
    for message in [&b"hello"[..], &b"again"[..]] {
        let mut tcp = TcpStream::connect((Ipv4Addr::LOCALHOST, port)).await.unwrap();
        tcp.write_all(message).await.unwrap();
        let mut answer = vec![0u8; message.len()];
        tcp.read_exact(&mut answer).await.unwrap();
        assert_eq!(answer, message.to_ascii_uppercase());

        let preamble = preambles.recv().await.unwrap();
        assert_eq!(&preamble[..16], PREAMBLE_MAGIC);
        assert_eq!(hex(&preamble[16..48]), SECRET);
        assert_eq!(&preamble[48..], client.endpoint().id().as_bytes());
    }

    assert!(server.info().connected.contains(&client.id()));
    assert_eq!(client.info().tunnels[0].id, server.id());

    assert!(client.disconnect(port));
    assert!(TcpStream::connect((Ipv4Addr::LOCALHOST, port)).await.is_err() || {
        // The listener may linger an instant after the abort.
        tokio::time::sleep(Duration::from_millis(200)).await;
        TcpStream::connect((Ipv4Addr::LOCALHOST, port)).await.is_err()
    });

    client.shutdown().await;
    server.shutdown().await;
}

#[tokio::test(flavor = "multi_thread")]
async fn a_node_that_cannot_be_reached_fails_and_a_ticket_round_trips() {
    let client = local_node(3, None).await;
    let gone = local_node(4, None).await;
    let ticket = local_ticket(&gone);
    gone.shutdown().await;

    let json = serde_json::to_string(&ticket).unwrap();
    assert_eq!(serde_json::from_str::<Ticket>(&json).unwrap(), ticket);
    assert_eq!(ticket.to_addr().unwrap().id.to_string(), ticket.id);

    let error = client.connect(&ticket, Duration::from_secs(2)).await.unwrap_err();
    assert_eq!(error.0, "unreachable");
    assert_eq!(client.connect(&local_ticket(&client), Duration::from_secs(2)).await.unwrap_err().0, "own_ticket");
    client.shutdown().await;
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}
