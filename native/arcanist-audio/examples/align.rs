//! Aligns the soundtrack of a music video with a track and prints the segments:
//! `cargo run --release --example align -- <video> <track>`. Symphonia reads the audio of MP4 and MKV files;
//! for other codecs extract it first (`ffmpeg -i clip.webm -vn clip.wav`).

use std::fs::File;
use std::path::Path;
use std::time::Instant;

use arcanist_audio::{Aligner, FeatureExtractor, Features, ReaderSource, Source, decode};

fn features(path: &str) -> Features {
    let file = File::open(path).expect("readable file");
    let length = file.metadata().ok().map(|m| m.len());
    let extension = Path::new(path).extension().and_then(|e| e.to_str()).map(str::to_owned);
    decode(ReaderSource::new(file, length), extension.as_deref(), None, FeatureExtractor::new, |e, s| e.push(s))
        .map_err(|e| format!("{e:?}"))
        .and_then(|e| e.finish())
        .unwrap_or_else(|e| panic!("{path}: {e}"))
}

fn main() {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let [video, track] = arguments.as_slice() else {
        eprintln!("usage: align <video> <track>");
        std::process::exit(2);
    };
    let started = Instant::now();
    let (video, track) = (features(video), features(track));
    let extracted = started.elapsed().as_secs_f64();
    let mut aligner = Aligner::new();
    aligner.set(0, video).unwrap();
    aligner.set(1, track).unwrap();
    let alignment = aligner.finish().unwrap();
    let aligned = started.elapsed().as_secs_f64() - extracted;

    let time = |ms: u64| format!("{}:{:06.3}", ms / 60000, (ms % 60000) as f64 / 1000.0);
    for segment in &alignment.segments {
        match segment.source {
            Source::Track => println!(
                "{} – {}  track from {} (rate {:.5}, similarity {:.2})",
                time(segment.video_start_ms),
                time(segment.video_end_ms),
                time(segment.track_start_ms.unwrap_or(0)),
                segment.rate.unwrap_or(1.0),
                segment.similarity.unwrap_or(0.0)
            ),
            Source::Video => println!("{} – {}  video", time(segment.video_start_ms), time(segment.video_end_ms)),
        }
    }
    eprintln!(
        "matched {}, coverage {:.0} %, video gain {:+.1} dB; features {extracted:.2} s, alignment {aligned:.3} s",
        alignment.matched,
        alignment.coverage * 100.0,
        alignment.video_gain_db
    );
}
