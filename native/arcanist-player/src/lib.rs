//! Server audio for Arcanist: plays music on an audio device of the server. Symphonia decodes, rubato converts the
//! rate to the device's, cpal plays it; the next item is decoded right after the current one, with no gap.
//! Exposed to .NET through a small C ABI; status and lists are JSON.
//!
//! Memory: strings returned through `out` parameters belong to the caller, who releases them with
//! `arcanist_player_free`. A handle from `arcanist_player_open` is released with `arcanist_player_close`. Panics
//! never cross the boundary; they become `STATUS_PANIC`.

mod engine;
pub mod output;
mod track;

use std::ffi::{CStr, CString, c_char, c_void};
use std::io::{self, Read, Seek, SeekFrom};
use std::panic::{AssertUnwindSafe, catch_unwind};

use symphonia::core::io::MediaSource;

pub use engine::{Engine, Item, ItemError, Shared, Status};
pub use output::{DeviceInfo, Output, devices};
pub use track::{Format, map_channels};

pub const STATUS_OK: i32 = 0;
/// No device, or it could not be opened.
pub const STATUS_DEVICE: i32 = 1;
/// The item heard is not the one being decoded any more: play it again from that position.
pub const STATUS_RETRY: i32 = 2;
pub const STATUS_PANIC: i32 = 3;
pub const STATUS_ARGUMENT: i32 = 4;

/// Reads the media through the caller, on the player's thread. `read` returns the bytes read (0 at the end) or -1;
/// `seek` takes an origin of 0 (start), 1 (current) or 2 (end) and returns the new position or -1. `length` is -1
/// if unknown; `seekable` is 0 for a stream that cannot seek (a conversion piped from ffmpeg). `release` is called
/// once, when the player is done with the source.
#[repr(C)]
pub struct PlayerSource {
    pub context: *mut c_void,
    pub read: extern "C" fn(*mut c_void, *mut u8, i32) -> i64,
    pub seek: extern "C" fn(*mut c_void, i64, i32) -> i64,
    pub length: i64,
    pub seekable: i32,
    pub release: extern "C" fn(*mut c_void),
}

struct CallbackSource {
    context: *mut c_void,
    read: extern "C" fn(*mut c_void, *mut u8, i32) -> i64,
    seek: extern "C" fn(*mut c_void, i64, i32) -> i64,
    length: Option<u64>,
    seekable: bool,
    release: extern "C" fn(*mut c_void),
}

// The callbacks are used from the player's thread only, one call at a time.
unsafe impl Send for CallbackSource {}
unsafe impl Sync for CallbackSource {}

impl Read for CallbackSource {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let length = buf.len().min(i32::MAX as usize) as i32;
        match (self.read)(self.context, buf.as_mut_ptr(), length) {
            n if n >= 0 => Ok(n as usize),
            _ => Err(io::Error::other("read failed")),
        }
    }
}

impl Seek for CallbackSource {
    fn seek(&mut self, pos: SeekFrom) -> io::Result<u64> {
        let (offset, origin) = match pos {
            SeekFrom::Start(offset) => (offset as i64, 0),
            SeekFrom::Current(offset) => (offset, 1),
            SeekFrom::End(offset) => (offset, 2),
        };
        match (self.seek)(self.context, offset, origin) {
            n if n >= 0 => Ok(n as u64),
            _ => Err(io::Error::other("seek failed")),
        }
    }
}

impl MediaSource for CallbackSource {
    fn is_seekable(&self) -> bool {
        self.seekable
    }

    fn byte_len(&self) -> Option<u64> {
        self.length
    }
}

impl Drop for CallbackSource {
    fn drop(&mut self) {
        (self.release)(self.context);
    }
}

/// A player on an open device.
pub struct Player {
    engine: Engine,
    // Dropped after the engine: the device keeps taking silence until then.
    _output: Output,
}

fn guard(f: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(f)).unwrap_or(STATUS_PANIC)
}

unsafe fn text(value: *const c_char) -> Option<String> {
    if value.is_null() {
        return None;
    }
    unsafe { CStr::from_ptr(value) }.to_str().ok().map(str::to_owned).filter(|s| !s.is_empty())
}

unsafe fn give(out: *mut *mut c_char, value: &str) {
    if !out.is_null() {
        unsafe { *out = CString::new(value).unwrap_or_default().into_raw() };
    }
}

unsafe fn item(source: *const PlayerSource, extension: *const c_char, token: u64, start_ms: u64, end_ms: u64) -> Option<Item> {
    let source = unsafe { source.as_ref() }?;
    let source = CallbackSource {
        context: source.context,
        read: source.read,
        seek: source.seek,
        length: u64::try_from(source.length).ok(),
        seekable: source.seekable != 0,
        release: source.release,
    };
    Some(Item { source: Box::new(source), extension: unsafe { text(extension) }, token, start_ms, end_ms })
}

/// The version of this C ABI, for the server to check it loaded the library it expects.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_version() -> u32 {
    1
}

/// The output devices, as JSON: `[{id, name, isDefault, sampleRate, channels}]`.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_devices(out: *mut *mut c_char) -> i32 {
    guard(|| match devices() {
        Ok(list) => {
            unsafe { give(out, &serde_json::to_string(&list).unwrap_or_else(|_| "[]".into())) };
            STATUS_OK
        }
        Err(error) => {
            unsafe { give(out, &error) };
            STATUS_DEVICE
        }
    })
}

/// Opens a device (by id; the system's default when null or not found). On success `handle` is the player and
/// `out` is `{device, sampleRate, channels}`; otherwise `out` is the error.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_open(device: *const c_char, buffer_ms: u32, handle: *mut *mut Player, out: *mut *mut c_char) -> i32 {
    guard(|| {
        if handle.is_null() {
            return STATUS_ARGUMENT;
        }
        match output::open(unsafe { text(device) }, buffer_ms) {
            Ok((output, engine)) => {
                let format = engine.shared().format;
                let info = serde_json::json!({ "device": output.device, "sampleRate": format.rate, "channels": format.channels });
                unsafe {
                    give(out, &info.to_string());
                    *handle = Box::into_raw(Box::new(Player { engine, _output: output }));
                }
                STATUS_OK
            }
            Err(error) => {
                unsafe { give(out, &error) };
                STATUS_DEVICE
            }
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_close(handle: *mut Player) {
    if !handle.is_null() {
        let _ = catch_unwind(AssertUnwindSafe(|| drop(unsafe { Box::from_raw(handle) })));
    }
}

fn with_player(handle: *mut Player, f: impl FnOnce(&Player) -> i32) -> i32 {
    guard(|| match unsafe { handle.as_ref() } {
        Some(player) => f(player),
        None => STATUS_ARGUMENT,
    })
}

/// Plays a source now, from `position_ms` of its part (`start_ms` to `end_ms` of the file; 0 = to the end), dropping
/// what was playing and what was queued. The source is released when the player is done with it, also on errors.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_play(
    handle: *mut Player, source: *const PlayerSource, extension: *const c_char, token: u64, start_ms: u64, end_ms: u64, position_ms: u64,
) -> i32 {
    with_player(handle, |player| match unsafe { item(source, extension, token, start_ms, end_ms) } {
        Some(item) => {
            player.engine.play(item, position_ms);
            STATUS_OK
        }
        None => STATUS_ARGUMENT,
    })
}

/// Queues a source after the current one (replacing the one queued if it has not started), with no gap.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_queue(handle: *mut Player, source: *const PlayerSource, extension: *const c_char, token: u64, start_ms: u64, end_ms: u64) -> i32 {
    with_player(handle, |player| match unsafe { item(source, extension, token, start_ms, end_ms) } {
        Some(item) => {
            player.engine.queue(item);
            STATUS_OK
        }
        None => STATUS_ARGUMENT,
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_clear_queue(handle: *mut Player) -> i32 {
    with_player(handle, |player| {
        player.engine.clear_queue();
        STATUS_OK
    })
}

/// Seeks the item heard (`token`); `STATUS_RETRY` when the next one has started decoding: play it again instead.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_seek(handle: *mut Player, token: u64, position_ms: u64) -> i32 {
    with_player(handle, |player| if player.engine.seek(token, position_ms) { STATUS_OK } else { STATUS_RETRY })
}

#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_pause(handle: *mut Player, paused: i32) -> i32 {
    with_player(handle, |player| {
        player.engine.set_paused(paused != 0);
        STATUS_OK
    })
}

/// Sets the volume (0–1) and the mute.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_volume(handle: *mut Player, volume: f32, muted: i32) -> i32 {
    with_player(handle, |player| {
        player.engine.set_volume(volume, muted != 0);
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_stop(handle: *mut Player) -> i32 {
    with_player(handle, |player| {
        player.engine.stop();
        STATUS_OK
    })
}

/// What the player is doing, as JSON (see `Status`). Errors are reported once.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_status(handle: *mut Player, out: *mut *mut c_char) -> i32 {
    with_player(handle, |player| {
        unsafe { give(out, &serde_json::to_string(&player.engine.status()).unwrap_or_default()) };
        STATUS_OK
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn arcanist_player_free(value: *mut c_char) {
    if !value.is_null() {
        drop(unsafe { CString::from_raw(value) });
    }
}
