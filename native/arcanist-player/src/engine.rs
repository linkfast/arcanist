//! The player: a decoding thread fills a buffer of samples at the device's format, and the device takes them from
//! it (`render`). Each piece of music starts at a known frame of that buffer, so the position heard and the change
//! from one track to the next are exact; the next track is decoded right after the current one, with no gap.

use std::collections::VecDeque;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicUsize, Ordering};
use std::sync::mpsc::{Receiver, Sender, TryRecvError, channel};
use std::sync::{Arc, Condvar, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

use serde::Serialize;
use symphonia::core::io::MediaSource;

use crate::track::{Format, Track};

/// What to play: a source, its extension (a hint for the format) and the part of it (a track of a cue sheet).
pub struct Item {
    pub source: Box<dyn MediaSource>,
    pub extension: Option<String>,
    /// The caller's id for it, reported in the status.
    pub token: u64,
    pub start_ms: u64,
    /// 0: to the end of the file.
    pub end_ms: u64,
}

enum Command {
    /// Plays an item now, from a position, dropping what was playing and what was next.
    Play(Item, u64),
    /// The item after the current one (replacing the one queued, if it has not started yet).
    Queue(Item),
    ClearQueue,
    Seek(u64),
    Stop,
    Quit,
}

#[derive(Serialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ItemError {
    pub token: u64,
    pub error: String,
}

/// What the device is doing.
#[derive(Serialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Status {
    /// idle, buffering, playing or paused.
    pub state: &'static str,
    /// The item heard, if any.
    pub token: Option<u64>,
    /// Its position, in milliseconds from the start of its part.
    pub position_ms: u64,
    /// The item being decoded (the next one, once the current has been decoded), if any.
    pub decoding: Option<u64>,
    /// The item queued after the one being decoded, if any.
    pub queued: Option<u64>,
    /// Items that could not be played since the last status, in order.
    pub errors: Vec<ItemError>,
    /// The device failed (unplugged, taken by another program).
    pub device_error: Option<String>,
}

struct Marker {
    /// The frame of the buffer where the item starts (or restarts, after a seek).
    frame: u64,
    token: u64,
    position_ms: u64,
}

struct State {
    samples: VecDeque<f32>,
    /// Frames ever pushed, and frames ever played: the buffer holds the difference.
    pushed: u64,
    played: u64,
    markers: VecDeque<Marker>,
    decoding: Option<u64>,
    queued: Option<u64>,
    errors: Vec<ItemError>,
    device_error: Option<String>,
    /// The gain applied, moving towards the one wanted, so volume changes do not click.
    gain: f32,
}

/// What the decoding thread and the device share.
pub struct Shared {
    pub format: Format,
    capacity: usize,
    state: Mutex<State>,
    space: Condvar,
    paused: AtomicBool,
    volume: AtomicU32,
    muted: AtomicBool,
    /// Commands sent and not handled yet: until then the player is about to do something, not idle.
    requested: AtomicUsize,
}

impl Shared {
    /// Fills `out` (interleaved, at the device's format) with what is next, or silence.
    pub fn render(&self, out: &mut [f32]) {
        let channels = self.format.channels;
        let target = if self.muted.load(Ordering::Relaxed) { 0.0 } else { f32::from_bits(self.volume.load(Ordering::Relaxed)) };
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        let available = if self.paused.load(Ordering::Relaxed) { 0 } else { state.samples.len() / channels };
        let frames = available.min(out.len() / channels);
        // About 20 ms to reach a new volume.
        let step = 1.0 / (self.format.rate as f32 * 0.02);
        let mut gain = state.gain;
        for frame in 0..frames {
            gain = if gain < target { (gain + step).min(target) } else { (gain - step).max(target) };
            for channel in 0..channels {
                let sample = state.samples.pop_front().unwrap_or(0.0);
                out[frame * channels + channel] = sample * gain;
            }
        }
        state.gain = if frames > 0 { gain } else { target };
        out[frames * channels..].fill(0.0);
        state.played += frames as u64;
        // Markers before the one heard are done with.
        while state.markers.len() > 1 && state.markers[1].frame <= state.played {
            state.markers.pop_front();
        }
        drop(state);
        if frames > 0 {
            self.space.notify_all();
        }
    }

    pub fn set_device_error(&self, error: String) {
        self.state.lock().unwrap_or_else(|e| e.into_inner()).device_error = Some(error);
    }

    fn status(&self) -> Status {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        let heard = state.markers.iter().rev().find(|m| m.frame <= state.played);
        let pending = state.pushed > state.played;
        let (token, position_ms) = match heard {
            Some(marker) if pending || state.decoding == Some(marker.token) => {
                (Some(marker.token), marker.position_ms + (state.played - marker.frame) * 1000 / self.format.rate as u64)
            }
            _ => (None, 0),
        };
        let state_name = if token.is_none() && state.decoding.is_none() && self.requested.load(Ordering::Acquire) == 0 {
            "idle"
        } else if self.paused.load(Ordering::Relaxed) {
            "paused"
        } else if !pending {
            "buffering"
        } else {
            "playing"
        };
        Status {
            state: state_name,
            token,
            position_ms,
            decoding: state.decoding,
            queued: state.queued,
            errors: std::mem::take(&mut state.errors),
            device_error: state.device_error.clone(),
        }
    }

    /// Drops what is buffered: what was heard stops at once.
    fn flush(&self, state: &mut State) {
        state.samples.clear();
        state.played = state.pushed;
        state.markers.clear();
        state.gain = 0.0;
        self.space.notify_all();
    }

    fn free(&self, state: &State) -> usize {
        self.capacity.saturating_sub(state.samples.len())
    }
}

pub struct Engine {
    shared: Arc<Shared>,
    commands: Sender<Command>,
    thread: Option<JoinHandle<()>>,
}

impl Engine {
    /// A player for a device of that format, buffering about `buffer_ms` ahead.
    pub fn new(format: Format, buffer_ms: u32) -> Engine {
        let capacity = (format.rate as usize * format.channels * buffer_ms.max(100) as usize / 1000).max(format.channels * 4096);
        let shared = Arc::new(Shared {
            format,
            capacity,
            state: Mutex::new(State {
                samples: VecDeque::with_capacity(capacity),
                pushed: 0,
                played: 0,
                markers: VecDeque::new(),
                decoding: None,
                queued: None,
                errors: Vec::new(),
                device_error: None,
                gain: 1.0,
            }),
            space: Condvar::new(),
            paused: AtomicBool::new(false),
            volume: AtomicU32::new(1.0f32.to_bits()),
            muted: AtomicBool::new(false),
            requested: AtomicUsize::new(0),
        });
        let (commands, receiver) = channel();
        let decoding = shared.clone();
        let thread = std::thread::Builder::new()
            .name("arcanist-player".into())
            .spawn(move || Decoder { shared: decoding, commands: receiver, peeked: None, current: None, next: None, pending: Vec::new() }.run())
            .expect("the decoding thread starts");
        Engine { shared, commands, thread: Some(thread) }
    }

    pub fn shared(&self) -> Arc<Shared> {
        self.shared.clone()
    }

    fn send(&self, command: Command) {
        self.shared.requested.fetch_add(1, Ordering::AcqRel);
        if self.commands.send(command).is_err() {
            self.shared.requested.fetch_sub(1, Ordering::AcqRel);
        }
    }

    /// Waits (a little) for the commands sent to be handled.
    fn settle(&self) {
        let deadline = std::time::Instant::now() + Duration::from_secs(2);
        while self.shared.requested.load(Ordering::Acquire) > 0 && std::time::Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(1));
        }
    }

    pub fn play(&self, item: Item, position_ms: u64) {
        self.send(Command::Play(item, position_ms));
    }

    pub fn queue(&self, item: Item) {
        self.send(Command::Queue(item));
    }

    pub fn clear_queue(&self) {
        self.send(Command::ClearQueue);
    }

    /// Seeks the item heard; false when it is not the one being decoded any more (the next one has started
    /// decoding): play it again from that position instead.
    pub fn seek(&self, token: u64, position_ms: u64) -> bool {
        self.settle();
        let state = self.shared.state.lock().unwrap_or_else(|e| e.into_inner());
        if state.decoding != Some(token) {
            return false;
        }
        drop(state);
        self.send(Command::Seek(position_ms));
        true
    }

    pub fn stop(&self) {
        self.send(Command::Stop);
    }

    pub fn set_paused(&self, paused: bool) {
        self.shared.paused.store(paused, Ordering::Relaxed);
    }

    pub fn set_volume(&self, volume: f32, muted: bool) {
        self.shared.volume.store(volume.clamp(0.0, 1.0).to_bits(), Ordering::Relaxed);
        self.shared.muted.store(muted, Ordering::Relaxed);
    }

    pub fn status(&self) -> Status {
        self.shared.status()
    }
}

impl Drop for Engine {
    fn drop(&mut self) {
        let _ = self.commands.send(Command::Quit);
        self.shared.space.notify_all();
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

struct Decoder {
    shared: Arc<Shared>,
    commands: Receiver<Command>,
    /// A command that arrived while waiting for room in the buffer, handled first.
    peeked: Option<Command>,
    current: Option<(Track, u64)>,
    next: Option<Item>,
    /// Decoded samples waiting for room in the buffer.
    pending: Vec<f32>,
}

impl Decoder {
    fn run(mut self) {
        loop {
            // Idle: wait for something to do. Otherwise, take the commands that arrived.
            let command = if let Some(command) = self.peeked.take() {
                Some(command)
            } else if self.current.is_none() && self.pending.is_empty() {
                match self.commands.recv() {
                    Ok(command) => Some(command),
                    Err(_) => return,
                }
            } else {
                match self.commands.try_recv() {
                    Ok(command) => Some(command),
                    Err(TryRecvError::Empty) => None,
                    Err(TryRecvError::Disconnected) => return,
                }
            };
            if let Some(command) = command {
                let quit = matches!(command, Command::Quit);
                let keep = self.handle(command);
                if !quit {
                    self.shared.requested.fetch_sub(1, Ordering::AcqRel);
                }
                if !keep {
                    return;
                }
                continue;
            }

            if !self.pending.is_empty() {
                self.push_pending();
                continue;
            }

            let Some((track, token)) = self.current.as_mut() else { continue };
            let token = *token;
            let mut chunk = Vec::new();
            match track.next(&mut chunk) {
                Ok(more) => {
                    self.pending = chunk;
                    if !more {
                        self.advance();
                    }
                }
                Err(error) => {
                    self.lock().errors.push(ItemError { token, error });
                    self.advance();
                }
            }
        }
    }

    /// Handles a command; false to quit.
    fn handle(&mut self, command: Command) -> bool {
        match command {
            Command::Play(item, position_ms) => {
                self.pending.clear();
                self.next = None;
                {
                    let mut state = self.lock();
                    self.shared.flush(&mut state);
                    state.queued = None;
                }
                self.start(item, position_ms);
            }
            Command::Queue(item) => {
                let token = item.token;
                if self.current.is_none() && self.pending.is_empty() && self.lock().decoding.is_none() {
                    // Nothing being decoded (the last item is still being heard, or nothing is): it follows on.
                    self.start(item, 0);
                } else {
                    self.next = Some(item);
                    self.lock().queued = Some(token);
                }
            }
            Command::ClearQueue => {
                self.next = None;
                self.lock().queued = None;
            }
            Command::Seek(position_ms) => {
                if let Some((track, token)) = self.current.as_mut() {
                    let token = *token;
                    self.pending.clear();
                    let result = track.seek(position_ms);
                    let mut state = self.lock();
                    self.shared.flush(&mut state);
                    match result {
                        Ok(()) => {
                            let frame = state.pushed;
                            state.markers.push_back(Marker { frame, token, position_ms });
                        }
                        Err(error) => state.errors.push(ItemError { token, error }),
                    }
                }
            }
            Command::Stop => {
                self.pending.clear();
                self.current = None;
                self.next = None;
                let mut state = self.lock();
                self.shared.flush(&mut state);
                state.decoding = None;
                state.queued = None;
            }
            Command::Quit => return false,
        }
        true
    }

    /// Opens an item and marks where it starts in the buffer.
    fn start(&mut self, item: Item, position_ms: u64) {
        let token = item.token;
        match Track::open(item.source, item.extension.as_deref(), item.start_ms, item.end_ms, position_ms, self.shared.format) {
            Ok(track) => {
                self.current = Some((track, token));
                let mut state = self.lock();
                let frame = state.pushed;
                state.markers.push_back(Marker { frame, token, position_ms });
                state.decoding = Some(token);
            }
            Err(error) => {
                self.current = None;
                // An item that cannot be played is skipped. The next one is marked as being decoded in the same
                // step, so the player never looks idle in between.
                let next = self.next.take();
                let mut state = self.lock();
                state.errors.push(ItemError { token, error });
                state.decoding = next.as_ref().map(|n| n.token);
                state.queued = None;
                drop(state);
                if let Some(next) = next {
                    self.start(next, 0);
                }
            }
        }
    }

    /// The current item has been decoded: the next one follows it in the buffer.
    fn advance(&mut self) {
        // What is left of the current item goes in before the next one starts.
        while !self.pending.is_empty() {
            if !self.push_pending() {
                return;
            }
        }
        self.current = None;
        // In one step, so the player never looks idle between two items.
        let next = self.next.take();
        {
            let mut state = self.lock();
            state.decoding = next.as_ref().map(|n| n.token);
            state.queued = None;
        }
        if let Some(next) = next {
            self.start(next, 0);
        }
    }

    /// Moves pending samples into the buffer as room allows, waiting a little for room; false when a command
    /// arrived meanwhile (the caller handles it on its next turn).
    fn push_pending(&mut self) -> bool {
        let shared = self.shared.clone();
        let mut state = shared.state.lock().unwrap_or_else(|e| e.into_inner());
        loop {
            let free = self.shared.free(&state);
            let channels = self.shared.format.channels;
            let room = free / channels * channels;
            if room > 0 {
                let count = room.min(self.pending.len());
                state.samples.extend(self.pending.drain(..count));
                state.pushed += (count / channels) as u64;
                if self.pending.is_empty() {
                    return true;
                }
            }
            let (next, _) = shared.space.wait_timeout(state, Duration::from_millis(50)).unwrap_or_else(|e| e.into_inner());
            state = next;
            match self.commands.try_recv() {
                Ok(command) => {
                    self.peeked = Some(command);
                    return false;
                }
                Err(TryRecvError::Disconnected) => {
                    self.peeked = Some(Command::Quit);
                    return false;
                }
                Err(TryRecvError::Empty) => {}
            }
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, State> {
        self.shared.state.lock().unwrap_or_else(|e| e.into_inner())
    }
}
