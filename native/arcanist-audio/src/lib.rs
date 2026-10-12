//! Audio analysis for Arcanist: decoding (Symphonia), EBU R128 loudness, Chromaprint fingerprints, a
//! waveform overview, tempo and key. Exposed to .NET through a small C ABI; every result is JSON.
//!
//! Memory: strings returned through `out` parameters belong to the caller, who releases them with
//! `arcanist_audio_free`. Panics never cross the boundary; they become `STATUS_PANIC`.

mod analyzer;
mod compare;
mod decode;
mod dsp;
mod key;
mod tempo;

use std::ffi::{CStr, CString, c_char, c_void};
use std::io::{self, Read, Seek, SeekFrom};
use std::panic::{AssertUnwindSafe, catch_unwind};

pub use analyzer::{ANALYZER_VERSION, Analysis, Analyzer, FLAG_ALL, FLAG_FINGERPRINT, FLAG_KEY, FLAG_LOUDNESS, FLAG_TEMPO, FLAG_WAVEFORM};
pub use compare::similarity;
pub use decode::{DecodeFailure, ReaderSource, analyze};

pub const STATUS_OK: i32 = 0;
/// The format or codec is not supported: decode it elsewhere (ffmpeg) and use the PCM functions.
pub const STATUS_UNSUPPORTED: i32 = 1;
/// The stream is corrupt or empty.
pub const STATUS_INVALID: i32 = 2;
pub const STATUS_PANIC: i32 = 3;
pub const STATUS_ARGUMENT: i32 = 4;

/// Reads the media through the caller. `read` returns the bytes read (0 at the end) or -1; `seek` takes an
/// origin of 0 (start), 1 (current) or 2 (end) and returns the new position or -1. `length` is -1 if unknown.
#[repr(C)]
pub struct ArcanistSource {
    pub context: *mut c_void,
    pub read: extern "C" fn(*mut c_void, *mut u8, i32) -> i64,
    pub seek: extern "C" fn(*mut c_void, i64, i32) -> i64,
    pub length: i64,
}

struct CallbackReader {
    context: *mut c_void,
    read: extern "C" fn(*mut c_void, *mut u8, i32) -> i64,
    seek: extern "C" fn(*mut c_void, i64, i32) -> i64,
}

// The callbacks are only used from the thread that called arcanist_audio_analyze.
unsafe impl Send for CallbackReader {}
unsafe impl Sync for CallbackReader {}

impl Read for CallbackReader {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let length = buf.len().min(i32::MAX as usize) as i32;
        match (self.read)(self.context, buf.as_mut_ptr(), length) {
            n if n >= 0 => Ok(n as usize),
            _ => Err(io::Error::other("read failed")),
        }
    }
}

impl Seek for CallbackReader {
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

/// Version of the analysis algorithms; results from an older version should be recomputed.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_audio_version() -> u32 {
    ANALYZER_VERSION
}

/// Decodes and analyzes a stream: all of it, or from `start_ms` to `end_ms` when `end_ms` is not 0 (a track of a
/// cue sheet). `extension` (may be null) helps detect the format. On return, `out` holds the analysis as JSON, or
/// `{"error": "..."}`.
///
/// # Safety
/// `source` must be valid for the duration of the call; `extension` null or a NUL-terminated string; `out`
/// a valid pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_analyze(
    source: *const ArcanistSource,
    extension: *const c_char,
    flags: u32,
    start_ms: u64,
    end_ms: u64,
    out: *mut *mut c_char,
) -> i32 {
    if source.is_null() || out.is_null() || (end_ms != 0 && end_ms <= start_ms) {
        return STATUS_ARGUMENT;
    }
    let (source, extension) = unsafe {
        let extension = if extension.is_null() { None } else { CStr::from_ptr(extension).to_str().ok().map(str::to_owned) };
        (&*source, extension)
    };
    let reader = CallbackReader { context: source.context, read: source.read, seek: source.seek };
    let length = (source.length >= 0).then_some(source.length as u64);

    let range = (end_ms != 0).then_some((start_ms, end_ms));
    let result = catch_unwind(AssertUnwindSafe(|| analyze(ReaderSource::new(reader, length), extension.as_deref(), flags, range)));
    let (status, json) = match result {
        Ok(Ok(analysis)) => (STATUS_OK, to_json(&analysis)),
        Ok(Err(DecodeFailure::Unsupported(message))) => (STATUS_UNSUPPORTED, error_json(&message)),
        Ok(Err(DecodeFailure::Invalid(message))) => (STATUS_INVALID, error_json(&message)),
        Err(_) => (STATUS_PANIC, error_json("the decoder panicked")),
    };
    unsafe { *out = into_c_string(json) };
    status
}

/// Starts analyzing PCM decoded elsewhere. Returns null if the format is invalid.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_audio_analyzer_new(rate: u32, channels: u32, flags: u32) -> *mut Analyzer {
    catch_unwind(|| Analyzer::new(rate, channels, flags).ok())
        .ok()
        .flatten()
        .map_or(std::ptr::null_mut(), |analyzer| Box::into_raw(Box::new(analyzer)))
}

/// Adds `count` interleaved f32 samples.
///
/// # Safety
/// `analyzer` must come from `arcanist_audio_analyzer_new` and not be finished or freed; `samples` must point
/// to `count` floats.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_analyzer_push(analyzer: *mut Analyzer, samples: *const f32, count: usize) -> i32 {
    if analyzer.is_null() || (samples.is_null() && count > 0) {
        return STATUS_ARGUMENT;
    }
    let (analyzer, samples) = unsafe {
        (&mut *analyzer, if count == 0 { &[][..] } else { std::slice::from_raw_parts(samples, count) })
    };
    match catch_unwind(AssertUnwindSafe(|| analyzer.push(samples))) {
        Ok(Ok(())) => STATUS_OK,
        Ok(Err(_)) => STATUS_INVALID,
        Err(_) => STATUS_PANIC,
    }
}

/// Finishes and releases the analyzer; `out` receives the analysis as JSON, or `{"error": "..."}`.
///
/// # Safety
/// `analyzer` must come from `arcanist_audio_analyzer_new` and is invalid afterwards; `out` a valid pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_analyzer_finish(analyzer: *mut Analyzer, out: *mut *mut c_char) -> i32 {
    if analyzer.is_null() || out.is_null() {
        return STATUS_ARGUMENT;
    }
    let analyzer = unsafe { Box::from_raw(analyzer) };
    let (status, json) = match catch_unwind(AssertUnwindSafe(|| analyzer.finish())) {
        Ok(Ok(analysis)) => (STATUS_OK, to_json(&analysis)),
        Ok(Err(message)) => (STATUS_INVALID, error_json(&message)),
        Err(_) => (STATUS_PANIC, error_json("the analyzer panicked")),
    };
    unsafe { *out = into_c_string(json) };
    status
}

/// Releases an analyzer that will not be finished.
///
/// # Safety
/// `analyzer` must come from `arcanist_audio_analyzer_new`, or be null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_analyzer_free(analyzer: *mut Analyzer) {
    if !analyzer.is_null() {
        drop(unsafe { Box::from_raw(analyzer) });
    }
}

/// Similarity of two raw fingerprints (sub-fingerprint arrays): 0 unrelated, 1 identical.
///
/// # Safety
/// `a` and `b` must point to `a_count` and `b_count` values (or be null with a count of 0).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_compare(a: *const u32, a_count: usize, b: *const u32, b_count: usize) -> f64 {
    let (a, b) = unsafe { (slice(a, a_count), slice(b, b_count)) };
    catch_unwind(|| similarity(a, b)).unwrap_or(0.0)
}

/// Releases a string returned by this library.
///
/// # Safety
/// `value` must come from this library, or be null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_audio_free(value: *mut c_char) {
    if !value.is_null() {
        drop(unsafe { CString::from_raw(value) });
    }
}

unsafe fn slice<'a>(pointer: *const u32, count: usize) -> &'a [u32] {
    if pointer.is_null() || count == 0 { &[] } else { unsafe { std::slice::from_raw_parts(pointer, count) } }
}

fn to_json(analysis: &Analysis) -> String {
    serde_json::to_string(analysis).unwrap_or_else(|e| error_json(&e.to_string()))
}

fn error_json(message: &str) -> String {
    serde_json::json!({ "error": message }).to_string()
}

fn into_c_string(value: String) -> *mut c_char {
    CString::new(value.replace('\0', "")).unwrap_or_default().into_raw()
}
