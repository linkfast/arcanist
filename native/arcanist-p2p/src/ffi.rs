//! The C ABI used by the server (`P2P/NativeP2P.cs`).
//!
//! Every function returns 0 on success, or a non-zero status with an error code written to `error` (free it with
//! [`arcanist_p2p_free_string`]). Panics are caught here: a bug in the node never takes the server down. Calls block
//! on a runtime owned by this library, so they must not be made from inside it.

use std::ffi::{CStr, CString, c_char};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::OnceLock;
use std::time::Duration;

use crate::{Config, Error, Node, Ticket};

const OK: i32 = 0;
const FAILED: i32 = 1;
const INVALID_ARGUMENT: i32 = 2;
const PANICKED: i32 = 3;

fn runtime() -> &'static tokio::runtime::Runtime {
    static RUNTIME: OnceLock<tokio::runtime::Runtime> = OnceLock::new();
    RUNTIME.get_or_init(|| {
        tokio::runtime::Builder::new_multi_thread()
            .thread_name("arcanist-p2p")
            .worker_threads(2)
            .enable_all()
            .build()
            .expect("the P2P runtime could not start")
    })
}

/// Runs `body`, turning errors and panics into a status and an error message.
fn guarded(error: *mut *mut c_char, body: impl FnOnce() -> Result<(), (i32, Error)>) -> i32 {
    let status = match catch_unwind(AssertUnwindSafe(body)) {
        Ok(Ok(())) => return OK,
        Ok(Err((status, message))) => (status, message.0),
        Err(_) => (PANICKED, "panic".to_owned()),
    };
    if !error.is_null() {
        unsafe { *error = to_c(&status.1) };
    }
    status.0
}

fn to_c(text: &str) -> *mut c_char {
    CString::new(text.replace('\0', "")).unwrap_or_default().into_raw()
}

unsafe fn read_str<'a>(text: *const c_char) -> Result<&'a str, (i32, Error)> {
    if text.is_null() {
        return Err((INVALID_ARGUMENT, Error::new("null_argument")));
    }
    unsafe { CStr::from_ptr(text) }.to_str().map_err(|_| (INVALID_ARGUMENT, Error::new("invalid_utf8")))
}

fn failed(error: Error) -> (i32, Error) {
    (FAILED, error)
}

/// Starts a node. `config` is the JSON of [`Config`]; `secret_key` points to the node's 32-byte secret key, which
/// gives it its id. On success `node` receives a handle for the other calls, released by [`arcanist_p2p_stop`].
///
/// # Safety
/// `config` must be a NUL-terminated string, `secret_key` must point to 32 readable bytes, and `node` and `error`
/// must be writable (or null for `error`).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_start(
    config: *const c_char,
    secret_key: *const u8,
    node: *mut *mut Node,
    error: *mut *mut c_char,
) -> i32 {
    guarded(error, || {
        let config: Config = serde_json::from_str(unsafe { read_str(config)? })
            .map_err(|_| (INVALID_ARGUMENT, Error::new("invalid_config")))?;
        if secret_key.is_null() || node.is_null() {
            return Err((INVALID_ARGUMENT, Error::new("null_argument")));
        }
        let mut key = [0u8; 32];
        key.copy_from_slice(unsafe { std::slice::from_raw_parts(secret_key, 32) });
        let started = runtime().block_on(Node::start(config, key)).map_err(failed)?;
        unsafe { *node = Box::into_raw(Box::new(started)) };
        Ok(())
    })
}

/// Writes the node's [`crate::Info`] as JSON to `info`.
///
/// # Safety
/// `node` must come from [`arcanist_p2p_start`] and not be stopped; `info` and `error` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_info(node: *const Node, info: *mut *mut c_char, error: *mut *mut c_char) -> i32 {
    guarded(error, || {
        let node = unsafe { node.as_ref() }.ok_or((INVALID_ARGUMENT, Error::new("null_argument")))?;
        let _runtime = runtime().enter();
        let json = serde_json::to_string(&node.info()).map_err(|_| failed(Error::new("serialization_failed")))?;
        unsafe { *info = to_c(&json) };
        Ok(())
    })
}

/// Opens a local port that tunnels to the node in `ticket` (the JSON of [`Ticket`]), waiting up to `timeout_ms` for
/// the first connection.
///
/// # Safety
/// `node` must come from [`arcanist_p2p_start`]; `ticket` must be a NUL-terminated string; `port` and `error` must
/// be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_connect(
    node: *const Node,
    ticket: *const c_char,
    timeout_ms: u32,
    port: *mut u16,
    error: *mut *mut c_char,
) -> i32 {
    guarded(error, || {
        let node = unsafe { node.as_ref() }.ok_or((INVALID_ARGUMENT, Error::new("null_argument")))?;
        let ticket: Ticket = serde_json::from_str(unsafe { read_str(ticket)? })
            .map_err(|_| (INVALID_ARGUMENT, Error::new("invalid_ticket")))?;
        let opened = runtime()
            .block_on(node.connect(&ticket, Duration::from_millis(timeout_ms.into())))
            .map_err(failed)?;
        unsafe { *port = opened };
        Ok(())
    })
}

/// Closes a local port opened by [`arcanist_p2p_connect`]. Returns 0 when it was open.
///
/// # Safety
/// `node` must come from [`arcanist_p2p_start`].
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_disconnect(node: *const Node, port: u16) -> i32 {
    guarded(std::ptr::null_mut(), || {
        let node = unsafe { node.as_ref() }.ok_or((INVALID_ARGUMENT, Error::new("null_argument")))?;
        let _runtime = runtime().enter();
        if node.disconnect(port) { Ok(()) } else { Err(failed(Error::new("not_open"))) }
    })
}

/// Stops the node and releases it: tunnels close and connected nodes are told.
///
/// # Safety
/// `node` must come from [`arcanist_p2p_start`] and is invalid afterwards.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_stop(node: *mut Node) {
    if node.is_null() {
        return;
    }
    let node = unsafe { Box::from_raw(node) };
    let _ = catch_unwind(AssertUnwindSafe(|| {
        runtime().block_on(async {
            let _ = tokio::time::timeout(Duration::from_secs(5), node.shutdown()).await;
        })
    }));
}

/// Releases a string returned by this library.
///
/// # Safety
/// `text` must come from this library, or be null.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn arcanist_p2p_free_string(text: *mut c_char) {
    if !text.is_null() {
        drop(unsafe { CString::from_raw(text) });
    }
}

/// The library's version, to check the server found the one it was built with. Static: not to be freed.
#[unsafe(no_mangle)]
pub extern "C" fn arcanist_p2p_version() -> *const c_char {
    concat!(env!("CARGO_PKG_VERSION"), "\0").as_ptr().cast()
}
