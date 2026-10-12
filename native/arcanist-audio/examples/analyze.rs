//! Analyzes files from the command line: `cargo run --release --example analyze -- <file>...`

use std::fs::File;
use std::time::Instant;

fn main() {
    for path in std::env::args().skip(1) {
        let file = File::open(&path).expect("cannot open file");
        let length = file.metadata().ok().map(|m| m.len());
        let extension = std::path::Path::new(&path).extension().and_then(|e| e.to_str()).map(str::to_owned);
        let started = Instant::now();
        let result = arcanist_audio::analyze(arcanist_audio::ReaderSource::new(file, length), extension.as_deref(), std::env::var("FLAGS").ok().and_then(|f| f.parse().ok()).unwrap_or(arcanist_audio::FLAG_ALL), None);
        let elapsed = started.elapsed();
        match result {
            Ok(mut analysis) => {
                analysis.waveform = analysis.waveform.map(|w| format!("{}…", &w[..16]));
                if let Some(f) = analysis.fingerprint.as_mut() {
                    f.raw = format!("{} chars", f.raw.len());
                    f.compressed = format!("{}…", &f.compressed[..24]);
                }
                if let Some(l) = analysis.loudness.as_mut() {
                    l.histogram = format!("{} chars", l.histogram.len());
                }
                println!("{path} ({:.0} ms, {:.0}x realtime)\n{}", elapsed.as_secs_f64() * 1000.0,
                    analysis.duration_ms as f64 / 1000.0 / elapsed.as_secs_f64(), serde_json::to_string_pretty(&analysis).unwrap());
            }
            Err(e) => println!("{path}: {e:?}"),
        }
    }
}
