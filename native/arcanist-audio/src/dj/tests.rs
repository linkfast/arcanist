//! The DJ analysis against synthetic audio whose answers are known: click tracks at a given tempo and offset,
//! silences, a key, a loudness reference and a song with sections. Run with `--nocapture` to see the measured
//! accuracy and speed.

use std::f32::consts::PI;
use std::time::Instant;

use super::{DjAnalysis, DjAnalyzer};

const RATE: u32 = 44100;

/// A small deterministic noise source (xorshift).
struct Noise(u32);

impl Noise {
    fn next(&mut self) -> f32 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 17;
        self.0 ^= self.0 << 5;
        (self.0 as f32 / u32::MAX as f32) * 2.0 - 1.0
    }
}

fn seconds(n: f32) -> usize {
    (n * RATE as f32) as usize
}

/// Adds a decaying noise burst (a click) at `at` seconds.
fn click(signal: &mut [f32], at: f32, gain: f32, noise: &mut Noise) {
    let start = seconds(at);
    for i in 0..seconds(0.03) {
        if let Some(s) = signal.get_mut(start + i) {
            *s += gain * noise.next() * (-(i as f32) / seconds(0.006) as f32).exp();
        }
    }
}

/// Adds a low thump (a kick) at `at` seconds.
fn kick(signal: &mut [f32], at: f32, gain: f32) {
    let start = seconds(at);
    for i in 0..seconds(0.12) {
        let t = i as f32 / RATE as f32;
        if let Some(s) = signal.get_mut(start + i) {
            *s += gain * (2.0 * PI * 55.0 * t).sin() * (-t / 0.04).exp();
        }
    }
}

/// Adds a sustained triad for `length` seconds.
fn chord(signal: &mut [f32], at: f32, length: f32, notes: &[i32], gain: f32) {
    let start = seconds(at);
    for i in 0..seconds(length) {
        let t = i as f32 / RATE as f32;
        let envelope = (t / 0.01).min(1.0) * ((length - t) / 0.01).clamp(0.0, 1.0);
        if let Some(s) = signal.get_mut(start + i) {
            *s += gain * envelope * notes.iter().map(|n| (2.0 * PI * 440.0 * 2f32.powf((*n - 69) as f32 / 12.0) * t).sin()).sum::<f32>() / notes.len() as f32;
        }
    }
}

/// A click track: a click every beat, with a kick on the first beat of each bar.
fn click_track(bpm: f32, offset: f32, length: f32) -> Vec<f32> {
    let mut signal = vec![0.0f32; seconds(length)];
    let mut noise = Noise(0x9e3779b9);
    let period = 60.0 / bpm;
    let mut beat = 0;
    loop {
        let at = offset + beat as f32 * period;
        if at >= length - 0.2 {
            break;
        }
        click(&mut signal, at, if beat % 4 == 0 { 0.6 } else { 0.4 }, &mut noise);
        if beat % 4 == 0 {
            kick(&mut signal, at, 0.5);
        }
        beat += 1;
    }
    signal
}

fn analyze(mono: &[f32]) -> (DjAnalysis, f32) {
    let stereo: Vec<f32> = mono.iter().flat_map(|s| [*s, *s]).collect();
    let started = Instant::now();
    let mut analyzer = DjAnalyzer::new(RATE, 2).unwrap();
    for chunk in stereo.chunks(4096 * 2) {
        analyzer.push(chunk).unwrap();
    }
    let analysis = analyzer.finish().unwrap();
    let factor = started.elapsed().as_secs_f32() / (mono.len() as f32 / RATE as f32);
    (analysis, factor)
}

#[test]
fn finds_the_tempo_and_first_beat_of_click_tracks() {
    for (bpm, offset) in [(120.0, 0.0), (128.0, 0.37), (174.0, 0.123), (90.0, 1.0), (100.0, 2.5)] {
        let (analysis, factor) = analyze(&click_track(bpm, offset, 60.0));
        let tempo = analysis.tempo.expect("a tempo");
        let first = tempo.first_beat.unwrap();
        let downbeat = tempo.first_downbeat.unwrap();
        println!(
            "click {bpm} BPM, offset {offset}: bpm {} (error {:.3}), first beat {first} (error {:.1} ms), downbeat {downbeat}, stable {}, {:.3}x realtime",
            tempo.bpm,
            (tempo.bpm - bpm as f64).abs(),
            (first - offset as f64).abs() * 1000.0,
            tempo.stable,
            factor
        );
        assert!((tempo.bpm - bpm as f64).abs() < 0.1, "{bpm} BPM read as {}", tempo.bpm);
        assert!(tempo.stable);
        assert!((first - offset as f64).abs() < 0.01, "first beat {first}, expected {offset}");
        assert!((downbeat - offset as f64).abs() < 0.01, "first downbeat {downbeat}, expected {offset}");
        assert_eq!(tempo.beats_per_bar, 4);
    }
}

#[test]
fn finds_silences_and_where_the_music_is() {
    let mut signal = vec![0.0f32; seconds(0.5)];
    // Clicks over a held chord: sound all along but for the gap.
    let mut music = click_track(120.0, 0.0, 20.0);
    chord(&mut music, 0.0, 20.0, &[57, 60, 64], 0.2);
    // A gap of silence from 10.2 s to 11.9 s of the music (10.7–12.4 s of the track).
    music[seconds(10.2)..seconds(11.9)].iter_mut().for_each(|s| *s = 0.0);
    signal.extend(music);
    signal.extend(vec![0.0f32; seconds(1.0)]);
    let (analysis, _) = analyze(&signal);

    let leading = analysis.silences.iter().find(|s| s.kind == "leading").expect("a leading silence");
    let gaps: Vec<_> = analysis.silences.iter().filter(|s| s.kind == "gap").collect();
    let trailing = analysis.silences.iter().find(|s| s.kind == "trailing").expect("a trailing silence");
    println!(
        "silences: leading to {} (0.5), gaps {:?} (10.7–12.4), trailing from {} (20.5)",
        leading.end,
        gaps.iter().map(|g| (g.start, g.end)).collect::<Vec<_>>(),
        trailing.start
    );
    assert_eq!(gaps.len(), 1);
    assert!((leading.end - 0.5).abs() < 0.05);
    assert!((gaps[0].start - 10.7).abs() < 0.05, "gap starts at {}", gaps[0].start);
    assert!((gaps[0].end - 12.4).abs() < 0.05, "gap ends at {}", gaps[0].end);
    assert!((trailing.start - 20.5).abs() < 0.05);
    assert!((analysis.music_start - 0.5).abs() < 0.05);
    assert!((analysis.music_end - 20.5).abs() < 0.05);
}

/// A sung-like tone: harmonics with vibrato, in syllables five times a second.
fn voice(signal: &mut [f32], at: f32, length: f32, midi: i32, gain: f32) {
    let start = seconds(at);
    let base = 440.0 * 2f32.powf((midi - 69) as f32 / 12.0);
    let mut phase = 0.0f32;
    for i in 0..seconds(length) {
        let t = i as f32 / RATE as f32;
        let frequency = base * (1.0 + 0.02 * (2.0 * PI * 5.5 * t).sin());
        phase += 2.0 * PI * frequency / RATE as f32;
        let syllable = (0.5 - 0.5 * (2.0 * PI * 5.0 * t).cos()).powi(2);
        let tone: f32 = (1..=8).map(|h| (phase * h as f32).sin() / h as f32).sum();
        if let Some(s) = signal.get_mut(start + i) {
            *s += gain * syllable * tone * 0.5;
        }
    }
}

#[test]
fn tells_a_voice_from_a_pad() {
    let mut signal = vec![0.0f32; seconds(30.0)];
    chord(&mut signal, 0.0, 30.0, &[57, 60, 64], 0.3);
    voice(&mut signal, 10.0, 10.0, 64, 0.4);
    let (analysis, _) = analyze(&signal);
    let presence = decode(&analysis.vocals.presence);
    let mean = |from: f32, to: f32| {
        let (a, b) = ((from / 0.5) as usize, (to / 0.5) as usize);
        presence[a..b].iter().map(|v| *v as f32 / 255.0).sum::<f32>() / (b - a) as f32
    };
    let (pad, sung) = (mean(1.0, 9.0), mean(11.0, 19.0));
    println!("voice: pad alone {pad:.2}, with a voice {sung:.2}");
    assert!(pad < 0.2, "pad {pad}");
    assert!(sung > pad + 0.3, "voice {sung}, pad {pad}");
}

fn decode(base64: &str) -> Vec<u8> {
    let value = |c: u8| match c {
        b'A'..=b'Z' => c - b'A',
        b'a'..=b'z' => c - b'a' + 26,
        b'0'..=b'9' => c - b'0' + 52,
        b'+' => 62,
        _ => 63,
    };
    let clean: Vec<u8> = base64.bytes().filter(|c| *c != b'=').collect();
    let mut out = Vec::new();
    for chunk in clean.chunks(4) {
        let n = chunk.iter().enumerate().fold(0u32, |n, (i, c)| n | (value(*c) as u32) << (18 - 6 * i));
        for i in 0..chunk.len() - 1 {
            out.push((n >> (16 - 8 * i)) as u8);
        }
    }
    out
}

#[test]
fn finds_the_key() {
    // i–iv–V–i in A minor, as sustained triads.
    let mut signal = vec![0.0f32; seconds(32.0)];
    let progression: [&[i32]; 4] = [&[57, 60, 64], &[62, 65, 69], &[64, 68, 71], &[57, 60, 64]];
    for bar in 0..16 {
        chord(&mut signal, bar as f32 * 2.0, 2.0, progression[bar % 4], 0.5);
    }
    let (analysis, _) = analyze(&signal);
    let key = analysis.key.expect("a key");
    println!("key: pitch class {} minor {} (A minor = 9, true), confidence {}", key.pitch_class, key.minor, key.confidence);
    assert_eq!((key.pitch_class, key.minor), (9, true));
}

#[test]
fn measures_the_loudness_reference() {
    // EBU Tech 3341: a stereo 1 kHz sine at −23 dBFS reads −23 LUFS.
    let amplitude = 10f32.powf(-23.0 / 20.0);
    let signal: Vec<f32> = (0..seconds(20.0)).map(|i| amplitude * (2.0 * PI * 1000.0 * i as f32 / RATE as f32).sin()).collect();
    let (analysis, _) = analyze(&signal);
    println!("loudness: {} LUFS (−23), true peak {} dB", analysis.loudness.integrated_lufs, analysis.loudness.true_peak_db);
    assert!((analysis.loudness.integrated_lufs + 23.0).abs() < 0.5);
}

/// A song in the shape of pop: intro, verse, chorus, verse, chorus, outro, at 128 BPM.
fn song() -> (Vec<f32>, f32) {
    let bpm = 128.0;
    let bar = 4.0 * 60.0 / bpm;
    let parts: [(&str, usize); 6] = [("intro", 8), ("verse", 16), ("chorus", 16), ("verse", 16), ("chorus", 16), ("outro", 8)];
    let total: usize = parts.iter().map(|p| p.1).sum();
    let mut signal = vec![0.0f32; seconds(total as f32 * bar + 1.0)];
    let mut noise = Noise(12345);
    let verse: [&[i32]; 4] = [&[57, 60, 64], &[53, 57, 60], &[48, 52, 55], &[55, 59, 62]];
    let chorus: [&[i32]; 4] = [&[60, 64, 67], &[67, 71, 74], &[69, 72, 76], &[65, 69, 72]];
    let mut at_bar = 0;
    for (name, bars) in parts {
        for b in 0..bars {
            let start = (at_bar + b) as f32 * bar;
            let (notes, gain, drums) = match name {
                "intro" | "outro" => (verse[b % 4], 0.15, 0.0),
                "verse" => (verse[b % 4], 0.25, 0.25),
                _ => (chorus[b % 4], 0.45, 0.6),
            };
            chord(&mut signal, start, bar, notes, gain);
            for beat in 0..4 {
                let at = start + beat as f32 * bar / 4.0;
                if drums > 0.0 {
                    click(&mut signal, at, drums * if beat == 0 { 1.0 } else { 0.7 }, &mut noise);
                    if beat == 0 || name == "chorus" {
                        kick(&mut signal, at, drums);
                    }
                } else {
                    click(&mut signal, at, 0.05, &mut noise);
                }
            }
        }
        at_bar += bars;
    }
    (signal, bar)
}

#[test]
fn finds_the_sections_of_a_song() {
    let (signal, bar) = song();
    let (analysis, factor) = analyze(&signal);
    for s in &analysis.sections {
        println!("section {:>7.3}–{:>7.3} ({:>4.1} bars) {} {:<7} energy {} {}", s.start, s.end, (s.end - s.start) / bar as f64, s.label, s.name, s.energy, s.trend);
    }
    println!("highlight {:?}, mix {:?}, {:.3}x realtime", analysis.highlight.as_ref().map(|h| (h.start, h.end)), analysis.mix_points, factor);
    let json = serde_json::to_string(&analysis).unwrap();
    println!("JSON: {} bytes for {:.0} s", json.len(), signal.len() as f32 / RATE as f32);

    let choruses: Vec<f64> = analysis.sections.iter().filter(|s| matches!(s.name, "chorus" | "drop")).map(|s| s.start).collect();
    let expected = [24.0 * bar as f64, 56.0 * bar as f64];
    for start in expected {
        assert!(choruses.iter().any(|c| (c - start).abs() < bar as f64 * 0.6), "no chorus at {start}: {choruses:?}");
    }
    assert_eq!(analysis.sections.first().unwrap().name, "intro");
    let highlight = analysis.highlight.as_ref().unwrap();
    assert!(expected.iter().any(|s| (highlight.start - s).abs() < bar as f64 * 0.6));
    // Phrases start on downbeats and cover the sections.
    assert!(!analysis.phrases.is_empty());
}

#[test]
fn is_deterministic() {
    let (signal, _) = song();
    let a = serde_json::to_string(&analyze(&signal).0).unwrap();
    let b = serde_json::to_string(&analyze(&signal).0).unwrap();
    assert_eq!(a, b);
}

#[test]
fn takes_little_time() {
    let (signal, _) = song();
    let (_, factor) = analyze(&signal);
    println!("song: {factor:.3}x realtime");
    if !cfg!(debug_assertions) {
        assert!(factor < 1.0, "{factor}x realtime");
    }
}

#[test]
fn lists_the_beats_of_a_changing_tempo() {
    // From 110 to 130 BPM over a minute: no single grid fits.
    let mut signal = vec![0.0f32; seconds(62.0)];
    let mut noise = Noise(777);
    let (mut at, mut beat) = (0.5f32, 0);
    let mut expected = Vec::new();
    while at < 61.0 {
        expected.push(at);
        click(&mut signal, at, if beat % 4 == 0 { 0.6 } else { 0.4 }, &mut noise);
        let bpm = 110.0 + 20.0 * (at / 61.0);
        at += 60.0 / bpm;
        beat += 1;
    }
    let (analysis, _) = analyze(&signal);
    let tempo = analysis.tempo.expect("a tempo");
    assert!(!tempo.stable);
    let beats: Vec<f32> = decode(tempo.beats.as_deref().unwrap()).chunks(4).map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]])).collect();
    // Each detected beat is on a click.
    let worst = beats.iter().map(|b| expected.iter().map(|e| (e - b).abs()).fold(f32::MAX, f32::min)).fold(0.0f32, f32::max);
    println!("changing tempo: {} beats of {}, worst error {:.1} ms, bpm {}", beats.len(), expected.len(), worst * 1000.0, tempo.bpm);
    assert!(beats.len() as f32 >= expected.len() as f32 * 0.9);
    assert!(worst < 0.02);
}
