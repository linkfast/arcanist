//! Analyzes files for DJ software and prints the JSON, how long it took against the audio's duration, and its
//! size: `cargo run --release --example dj -- <file>...` (`QUIET=1` prints only the measurements).

use std::fs::File;
use std::path::Path;
use std::time::Instant;

use arcanist_audio::{DjAnalyzer, ReaderSource, decode};

fn main() {
    let quiet = std::env::var_os("QUIET").is_some();
    for path in std::env::args().skip(1) {
        let file = File::open(&path).expect("readable file");
        let length = file.metadata().ok().map(|m| m.len());
        let extension = Path::new(&path).extension().and_then(|e| e.to_str()).map(str::to_owned);
        let started = Instant::now();
        let result = decode(ReaderSource::new(file, length), extension.as_deref(), None, DjAnalyzer::new, |a, s| a.push(s))
            .map_err(|e| format!("{e:?}"))
            .and_then(|a| a.finish());
        let elapsed = started.elapsed().as_secs_f64();
        match result {
            Ok(analysis) => {
                let json = serde_json::to_string(&analysis).unwrap();
                if !quiet {
                    println!("{}", serde_json::to_string_pretty(&analysis).unwrap());
                }
                let tempo = analysis.tempo.as_ref().map_or("none".to_owned(), |t| format!("{} BPM{}", t.bpm, if t.stable { "" } else { " (varying)" }));
                eprintln!(
                    "{path}: {:.1} s in {elapsed:.2} s ({:.3}x realtime), {} bytes of JSON, {tempo}, {} sections",
                    analysis.duration_seconds,
                    elapsed / analysis.duration_seconds,
                    json.len(),
                    analysis.sections.len()
                );
            }
            Err(e) => eprintln!("{path}: {e}"),
        }
    }
}
