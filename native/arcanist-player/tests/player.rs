//! The player without a sound card: the tests take the samples as a device would (`render`), so decoding, the
//! queue, positions, seeking and rate conversion are checked on any machine.

use std::io::Cursor;
use std::thread::sleep;
use std::time::{Duration, Instant};

use arcanist_player::{Engine, Format, Item, Status, map_channels};
use symphonia::core::io::ReadOnlySource;

const DEVICE: Format = Format { rate: 48_000, channels: 2 };

/// A 16-bit WAV of a sine, `ms` long.
fn wav(rate: u32, channels: u16, ms: u64, hz: f32) -> Vec<u8> {
    let frames = (rate as u64 * ms / 1000) as usize;
    let data_len = frames * channels as usize * 2;
    let mut out = Vec::with_capacity(44 + data_len);
    out.extend_from_slice(b"RIFF");
    out.extend_from_slice(&(36 + data_len as u32).to_le_bytes());
    out.extend_from_slice(b"WAVEfmt ");
    out.extend_from_slice(&16u32.to_le_bytes());
    out.extend_from_slice(&1u16.to_le_bytes());
    out.extend_from_slice(&channels.to_le_bytes());
    out.extend_from_slice(&rate.to_le_bytes());
    out.extend_from_slice(&(rate * channels as u32 * 2).to_le_bytes());
    out.extend_from_slice(&(channels * 2).to_le_bytes());
    out.extend_from_slice(&16u16.to_le_bytes());
    out.extend_from_slice(b"data");
    out.extend_from_slice(&(data_len as u32).to_le_bytes());
    for frame in 0..frames {
        let value = ((frame as f32 * hz * std::f32::consts::TAU / rate as f32).sin() * 0.5 * i16::MAX as f32) as i16;
        for _ in 0..channels {
            out.extend_from_slice(&value.to_le_bytes());
        }
    }
    out
}

fn item(bytes: Vec<u8>, token: u64) -> Item {
    Item { source: Box::new(Cursor::new(bytes)), extension: Some("wav".into()), token, start_ms: 0, end_ms: 0 }
}

/// Takes `frames` frames from the player as the device would, waiting for the decoder when it runs dry.
/// Returns the frames that carried sound and the statuses seen along the way.
fn render(engine: &Engine, frames: usize) -> (usize, Vec<Status>) {
    let shared = engine.shared();
    let mut buffer = vec![0.0f32; 480 * DEVICE.channels];
    let (mut taken, mut sounding, mut statuses) = (0, 0, Vec::new());
    let deadline = Instant::now() + Duration::from_secs(20);
    while taken < frames && Instant::now() < deadline {
        let status = engine.status();
        let state = status.state;
        statuses.push(status);
        if state == "buffering" {
            sleep(Duration::from_millis(2));
            continue;
        }
        if state == "idle" {
            break;
        }
        shared.render(&mut buffer);
        taken += 480;
        sounding += buffer.chunks(DEVICE.channels).filter(|f| f.iter().any(|s| *s != 0.0)).count();
    }
    (sounding, statuses)
}

fn wait_idle(engine: &Engine) -> (usize, Vec<Status>) {
    render(engine, usize::MAX)
}

#[test]
fn plays_an_item_and_reports_where_it_is() {
    let engine = Engine::new(DEVICE, 300);
    engine.play(item(wav(48_000, 2, 1000, 440.0), 7), 0);
    let (sounding, statuses) = wait_idle(&engine);
    // A second of sound (the sine is silent at a few samples).
    assert!((47_000..=48_000).contains(&sounding), "{sounding}");
    let positions: Vec<u64> = statuses.iter().filter(|s| s.token == Some(7)).map(|s| s.position_ms).collect();
    assert!(positions.windows(2).all(|w| w[0] <= w[1]));
    assert!(*positions.last().unwrap() >= 980);
    assert_eq!("idle", engine.status().state);
}

#[test]
fn the_next_item_follows_without_a_gap() {
    let engine = Engine::new(DEVICE, 300);
    engine.play(item(wav(48_000, 2, 500, 440.0), 1), 0);
    engine.queue(item(wav(48_000, 2, 500, 660.0), 2));
    let (sounding, statuses) = wait_idle(&engine);
    assert!((47_000..=48_000).contains(&sounding), "{sounding}");
    let tokens: Vec<u64> = statuses.iter().filter_map(|s| s.token).collect();
    let first_two = tokens.iter().position(|t| *t == 2).expect("the second item was heard");
    assert!(tokens[..first_two].iter().all(|t| *t == 1));
    assert!(tokens[first_two..].iter().all(|t| *t == 2));
}

#[test]
fn another_rate_is_converted_to_the_device_s() {
    let engine = Engine::new(DEVICE, 300);
    engine.play(item(wav(44_100, 1, 1000, 440.0), 3), 0);
    let (sounding, _) = wait_idle(&engine);
    // A second at 48 kHz, give or take the resampler's edges.
    assert!((47_000..=48_600).contains(&sounding), "{sounding}");
}

#[test]
fn seeking_moves_what_is_heard() {
    let engine = Engine::new(DEVICE, 300);
    engine.play(item(wav(48_000, 2, 2000, 440.0), 4), 0);
    render(&engine, 4_800);
    assert!(engine.seek(4, 1500));
    // The seek lands once the decoder takes the command.
    let deadline = Instant::now() + Duration::from_secs(5);
    while engine.status().position_ms < 1500 && Instant::now() < deadline {
        sleep(Duration::from_millis(2));
    }
    let status = engine.status();
    assert_eq!(Some(4), status.token);
    assert!((1500..1550).contains(&status.position_ms), "{}", status.position_ms);
    let (sounding, _) = wait_idle(&engine);
    assert!((23_000..=24_200).contains(&sounding), "{sounding}");
    assert!(!engine.seek(4, 0), "nothing is being decoded any more");
}

#[test]
fn a_cue_track_plays_its_part_of_the_file() {
    let engine = Engine::new(DEVICE, 300);
    let mut part = item(wav(48_000, 2, 2000, 440.0), 5);
    (part.start_ms, part.end_ms) = (500, 1000);
    engine.play(part, 0);
    let (sounding, statuses) = wait_idle(&engine);
    assert!((23_500..=24_000).contains(&sounding), "{sounding}");
    assert!(statuses.iter().filter(|s| s.token == Some(5)).all(|s| s.position_ms <= 500));
}

#[test]
fn what_cannot_be_played_is_reported_and_skipped() {
    let engine = Engine::new(DEVICE, 300);
    let broken = Item { source: Box::new(ReadOnlySource::new(Cursor::new(b"not audio at all".to_vec()))), extension: None, token: 8, start_ms: 0, end_ms: 0 };
    engine.play(broken, 0);
    engine.queue(item(wav(48_000, 2, 300, 440.0), 9));
    let (sounding, statuses) = wait_idle(&engine);
    let errors: Vec<_> = statuses.iter().flat_map(|s| s.errors.clone()).collect();
    assert_eq!(vec![8], errors.iter().map(|e| e.token).collect::<Vec<_>>());
    // Queued before the first failed or after: either way it plays.
    assert!(sounding > 14_000 || statuses.iter().any(|s| s.token == Some(9)), "{sounding}");
}

#[test]
fn pausing_and_the_volume() {
    let engine = Engine::new(DEVICE, 300);
    engine.play(item(wav(48_000, 2, 1000, 440.0), 6), 0);
    render(&engine, 9_600);
    let before = engine.status().position_ms;
    engine.set_paused(true);
    let shared = engine.shared();
    let mut buffer = vec![1.0f32; 960];
    shared.render(&mut buffer);
    assert!(buffer.iter().all(|s| *s == 0.0));
    assert_eq!("paused", engine.status().state);
    assert_eq!(before, engine.status().position_ms);

    engine.set_paused(false);
    engine.set_volume(0.5, true);
    // Muting fades out in about 20 ms, then it is silent.
    let mut fade = vec![0.0f32; 4_800 * 2];
    shared.render(&mut fade);
    shared.render(&mut buffer);
    assert!(buffer.iter().all(|s| *s == 0.0));
    engine.stop();
    sleep(Duration::from_millis(50));
    assert_eq!("idle", engine.status().state);
}

#[test]
fn channels_are_fitted_to_the_device() {
    assert_eq!(vec![0.5, 0.5, 0.25, 0.25], map_channels(&[0.5, 0.25], 1, 2));
    assert_eq!(vec![0.5, 0.25], map_channels(&[0.75, 0.25, 0.5, 0.0], 2, 1));
    assert_eq!(vec![1.0, 2.0], map_channels(&[1.0, 2.0, 3.0, 4.0, 5.0, 6.0], 6, 2));
    assert_eq!(vec![1.0, 2.0, 0.0, 0.0], map_channels(&[1.0, 2.0], 2, 4));
}
