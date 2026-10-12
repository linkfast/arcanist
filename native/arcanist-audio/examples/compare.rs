//! Compares the fingerprints of files: `cargo run --release --example compare -- <reference> <file>...`

use std::fs::File;

fn fingerprint(path: &str) -> Vec<u32> {
    let file = File::open(path).expect("cannot open file");
    let length = file.metadata().ok().map(|m| m.len());
    let extension = std::path::Path::new(path).extension().and_then(|e| e.to_str()).map(str::to_owned);
    let analysis = arcanist_audio::analyze(arcanist_audio::ReaderSource::new(file, length), extension.as_deref(), arcanist_audio::FLAG_FINGERPRINT, None)
        .expect("cannot analyze");
    let raw = analysis.fingerprint.expect("no fingerprint").raw;
    decode_base64(&raw).chunks_exact(4).map(|c| u32::from_le_bytes([c[0], c[1], c[2], c[3]])).collect()
}

fn decode_base64(text: &str) -> Vec<u8> {
    let value = |c: u8| match c {
        b'A'..=b'Z' => c - b'A',
        b'a'..=b'z' => c - b'a' + 26,
        b'0'..=b'9' => c - b'0' + 52,
        b'+' | b'-' => 62,
        _ => 63,
    };
    let digits: Vec<u8> = text.bytes().filter(|c| *c != b'=').map(value).collect();
    digits
        .chunks(4)
        .flat_map(|chunk| {
            let n = chunk.iter().enumerate().fold(0u32, |n, (i, d)| n | (*d as u32) << (18 - 6 * i));
            [(n >> 16) as u8, (n >> 8) as u8, n as u8].into_iter().take(chunk.len() - 1)
        })
        .collect()
}

fn main() {
    let mut args = std::env::args().skip(1);
    let reference_path = args.next().expect("usage: compare <reference> <file>...");
    let reference = fingerprint(&reference_path);
    for path in args {
        println!("{path}: {:.3}", arcanist_audio::similarity(&reference, &fingerprint(&path)));
    }
}
