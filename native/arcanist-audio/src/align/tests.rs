//! Synthetic songs: a melody of harmonic notes with drum hits, rendered as a function of time so the same song
//! can be rendered faster, quieter or at another sample rate. Interruptions are filtered noise ("dialogue").

use super::*;

const TRACK_RATE: u32 = 44100;
const VIDEO_RATE: u32 = 48000;

struct Song {
    notes: Vec<(f32, f32, f32)>,
}

impl Song {
    fn new(seed: u32, seconds: f32) -> Song {
        let mut state = seed.wrapping_mul(2654435761).max(1);
        let mut random = move || {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            state as f32 / u32::MAX as f32
        };
        let scale = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16];
        let mut notes = Vec::new();
        let mut t = 0.0;
        while t < seconds {
            let length = 0.2 + 0.25 * random();
            let pitch = 48 + scale[(random() * scale.len() as f32) as usize % scale.len()];
            let frequency = 440.0 * 2f32.powf((pitch as f32 - 69.0) / 12.0);
            notes.push((t, length, frequency));
            t += length;
        }
        Song { notes }
    }

    fn sample(&self, t: f32) -> f32 {
        let index = self.notes.partition_point(|n| n.0 <= t);
        let mut value = 0.0;
        if index > 0 {
            let (start, length, frequency) = self.notes[index - 1];
            let local = t - start;
            let envelope = (local * 40.0).min(1.0) * (-(local / length) * 2.0).exp();
            for harmonic in 1..5 {
                value += envelope * (2.0 * std::f32::consts::PI * frequency * harmonic as f32 * t).sin() / harmonic as f32;
            }
            // A drum hit at each note.
            if local < 0.03 {
                value += 0.6 * (local * 9871.0).sin() * (1.0 - local / 0.03);
            }
        }
        0.3 * value
    }
}

fn noise(seed: u32, t: f32) -> f32 {
    let mut x = (t * 16000.0) as u32 ^ seed.wrapping_mul(747796405);
    x ^= x >> 16;
    x = x.wrapping_mul(2246822507);
    x ^= x >> 13;
    let slow = (2.0 * std::f32::consts::PI * 3.0 * t).sin().abs();
    0.2 * slow * (x as f32 / u32::MAX as f32 - 0.5)
}

fn features(rate: u32, seconds: f32, mut sample: impl FnMut(f32) -> f32) -> Features {
    let mut extractor = FeatureExtractor::new(rate, 2).unwrap();
    let frames = (seconds * rate as f32) as usize;
    let mut buffer = Vec::with_capacity(8192);
    for i in 0..frames {
        let v = sample(i as f32 / rate as f32);
        buffer.push(v);
        buffer.push(v);
        if buffer.len() == 8192 {
            extractor.push(&buffer).unwrap();
            buffer.clear();
        }
    }
    extractor.push(&buffer).unwrap();
    extractor.finish().unwrap()
}

fn track_segments(alignment: &Alignment) -> Vec<&Segment> {
    alignment.segments.iter().filter(|s| s.source == Source::Track).collect()
}

fn near(actual: u64, expected: f32, tolerance_ms: f32) -> bool {
    (actual as f32 - expected * 1000.0).abs() <= tolerance_ms
}

#[test]
fn intro_cut_repeat_and_outro_keep_the_videos_audio() {
    let song = Song::new(7, 70.0);
    let track = features(TRACK_RATE, 60.0, |t| song.sample(t));
    // 6 s intro, the song from 0 to 40 s, 5 s of dialogue, the song again from 25 to 55 s, 4 s outro; the
    // video is 6 dB quieter.
    let video = features(VIDEO_RATE, 85.0, |t| {
        let gain = 0.5;
        if t < 6.0 {
            noise(1, t)
        } else if t < 46.0 {
            gain * song.sample(t - 6.0)
        } else if t < 51.0 {
            noise(2, t)
        } else if t < 81.0 {
            gain * song.sample(t - 51.0 + 25.0)
        } else {
            noise(3, t)
        }
    });

    let alignment = align(&video, &track);
    assert!(alignment.matched, "{alignment:#?}");
    let segments = track_segments(&alignment);
    assert_eq!(segments.len(), 2, "{alignment:#?}");

    let first = segments[0];
    assert!(near(first.video_start_ms, 6.0, 400.0), "{first:?}");
    assert!(near(first.video_end_ms, 46.0, 400.0), "{first:?}");
    let expected_track = first.video_start_ms as f32 / 1000.0 - 6.0;
    assert!(near(first.track_start_ms.unwrap(), expected_track, 30.0), "{first:?}");

    let second = segments[1];
    assert!(near(second.video_start_ms, 51.0, 400.0), "{second:?}");
    assert!(near(second.video_end_ms, 81.0, 400.0), "{second:?}");
    let expected_track = second.video_start_ms as f32 / 1000.0 - 26.0;
    assert!(near(second.track_start_ms.unwrap(), expected_track, 30.0), "{second:?}");

    assert!((alignment.video_gain_db - 6.0).abs() < 1.0, "{}", alignment.video_gain_db);
    assert!((alignment.coverage - 70.0 / 85.0).abs() < 0.03, "{}", alignment.coverage);
    // The segments cover the whole video, in order.
    assert_eq!(alignment.segments.first().unwrap().video_start_ms, 0);
    assert_eq!(alignment.segments.last().unwrap().video_end_ms, alignment.video_duration_ms);
    for pair in alignment.segments.windows(2) {
        assert_eq!(pair[0].video_end_ms, pair[1].video_start_ms);
    }
}

#[test]
fn a_video_running_slightly_fast_gets_its_rate() {
    let song = Song::new(11, 130.0);
    let track = features(TRACK_RATE, 120.0, |t| song.sample(t));
    // The whole song after a 3 s intro, at 1.001× (a 25 fps edit of a 24.975 fps master).
    let video = features(VIDEO_RATE, 118.0, |t| if t < 3.0 { noise(4, t) } else { song.sample((t - 3.0) * 1.001) });

    let alignment = align(&video, &track);
    let segments = track_segments(&alignment);
    assert_eq!(segments.len(), 1, "{alignment:#?}");
    let segment = segments[0];
    let rate = segment.rate.unwrap();
    assert!((rate - 1.001).abs() < 0.0003, "{rate}");
    let start = segment.video_start_ms as f32 / 1000.0;
    assert!(near(segment.track_start_ms.unwrap(), (start - 3.0) * 1.001, 40.0), "{segment:?}");
}

#[test]
fn a_different_performance_does_not_match() {
    let studio = Song::new(21, 70.0);
    let live = Song::new(22, 70.0);
    let track = features(TRACK_RATE, 60.0, |t| studio.sample(t));
    let video = features(VIDEO_RATE, 60.0, |t| live.sample(t));

    let alignment = align(&video, &track);
    assert!(!alignment.matched, "{alignment:#?}");
    assert_eq!(alignment.segments.len(), 1);
    assert_eq!(alignment.segments[0].source, Source::Video);
    assert_eq!(alignment.coverage, 0.0);
}

#[test]
fn the_aligner_takes_both_sides_through_its_api() {
    let song = Song::new(31, 40.0);
    let mut aligner = Aligner::new();
    assert!(aligner.push(0, &[0.0; 4]).is_err());
    aligner.begin(0, VIDEO_RATE, 1).unwrap();
    aligner.push(0, &(0..VIDEO_RATE * 30).map(|i| song.sample(i as f32 / VIDEO_RATE as f32)).collect::<Vec<_>>()).unwrap();
    aligner.set(1, features(TRACK_RATE, 30.0, |t| song.sample(t))).unwrap();
    let alignment = aligner.finish().unwrap();
    assert!(alignment.matched);
    assert!(alignment.coverage > 0.9, "{alignment:#?}");
    assert!(Aligner::new().finish().is_err());
}
