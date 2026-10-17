//! The audio devices of the system, through cpal: WASAPI on Windows, ALSA (and PipeWire or PulseAudio behind it) on
//! Linux, CoreAudio on macOS. A device's stream lives on a thread of its own (some systems do not let it move
//! between threads) and takes its samples from the player's buffer.

use std::sync::Arc;
use std::sync::mpsc::{Sender, channel};
use std::thread::JoinHandle;

use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use cpal::{FromSample, SampleFormat, SizedSample};
use serde::Serialize;

use crate::engine::{Engine, Shared};
use crate::track::Format;

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct DeviceInfo {
    /// Its name, which identifies it (cpal has no other stable id).
    pub id: String,
    pub name: String,
    pub is_default: bool,
    pub sample_rate: Option<u32>,
    pub channels: Option<u16>,
}

/// The output devices, the system's default first.
pub fn devices() -> Result<Vec<DeviceInfo>, String> {
    let host = cpal::default_host();
    let default = host.default_output_device().and_then(|d| d.name().ok());
    let mut list: Vec<DeviceInfo> = host
        .output_devices()
        .map_err(|e| e.to_string())?
        .filter_map(|device| {
            let name = device.name().ok()?;
            let config = device.default_output_config().ok();
            Some(DeviceInfo {
                id: name.clone(),
                is_default: default.as_deref() == Some(name.as_str()),
                name,
                sample_rate: config.as_ref().map(|c| c.sample_rate().0),
                channels: config.as_ref().map(|c| c.channels()),
            })
        })
        .collect();
    list.sort_by_key(|d| !d.is_default);
    Ok(list)
}

/// An open device: dropping it closes the stream.
pub struct Output {
    stop: Option<Sender<()>>,
    thread: Option<JoinHandle<()>>,
    pub device: String,
}

impl Drop for Output {
    fn drop(&mut self) {
        drop(self.stop.take());
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

/// Opens a device by name (the default when `None` or not found) and a player on it, buffering `buffer_ms` ahead.
pub fn open(name: Option<String>, buffer_ms: u32) -> Result<(Output, Engine), String> {
    let (format_tx, format_rx) = channel::<Result<(Format, String), String>>();
    let (shared_tx, shared_rx) = channel::<Arc<Shared>>();
    let (ready_tx, ready_rx) = channel::<Result<(), String>>();
    let (stop, stop_rx) = channel::<()>();
    let thread = std::thread::Builder::new()
        .name("arcanist-output".into())
        .spawn(move || {
            let host = cpal::default_host();
            let device = name
                .as_deref()
                .and_then(|wanted| host.output_devices().ok()?.find(|d| d.name().ok().as_deref() == Some(wanted)))
                .or_else(|| host.default_output_device());
            let Some(device) = device else {
                let _ = format_tx.send(Err("no audio output device".into()));
                return;
            };
            let config = match device.default_output_config() {
                Ok(config) => config,
                Err(e) => {
                    let _ = format_tx.send(Err(e.to_string()));
                    return;
                }
            };
            let format = Format { rate: config.sample_rate().0, channels: config.channels() as usize };
            let _ = format_tx.send(Ok((format, device.name().unwrap_or_default())));
            let Ok(shared) = shared_rx.recv() else { return };
            let stream_config = config.config();
            let stream = match config.sample_format() {
                SampleFormat::F32 => build::<f32>(&device, &stream_config, shared),
                SampleFormat::I16 => build::<i16>(&device, &stream_config, shared),
                SampleFormat::U16 => build::<u16>(&device, &stream_config, shared),
                SampleFormat::I32 => build::<i32>(&device, &stream_config, shared),
                SampleFormat::I8 => build::<i8>(&device, &stream_config, shared),
                SampleFormat::U8 => build::<u8>(&device, &stream_config, shared),
                SampleFormat::F64 => build::<f64>(&device, &stream_config, shared),
                other => Err(format!("unsupported sample format {other}")),
            };
            match stream.and_then(|s| s.play().map(|_| s).map_err(|e| e.to_string())) {
                Ok(stream) => {
                    let _ = ready_tx.send(Ok(()));
                    // Until the player is closed.
                    let _ = stop_rx.recv();
                    drop(stream);
                }
                Err(e) => {
                    let _ = ready_tx.send(Err(e));
                }
            }
        })
        .map_err(|e| e.to_string())?;

    let fail = |thread: JoinHandle<()>, error: String| {
        let _ = thread.join();
        Err(error)
    };
    let (format, device) = match format_rx.recv() {
        Ok(Ok(found)) => found,
        Ok(Err(e)) => return fail(thread, e),
        Err(_) => return fail(thread, "the audio thread stopped".into()),
    };
    let engine = Engine::new(format, buffer_ms);
    let _ = shared_tx.send(engine.shared());
    match ready_rx.recv() {
        Ok(Ok(())) => Ok((Output { stop: Some(stop), thread: Some(thread), device }, engine)),
        Ok(Err(e)) => fail(thread, e),
        Err(_) => fail(thread, "the audio thread stopped".into()),
    }
}

fn build<T: SizedSample + FromSample<f32>>(device: &cpal::Device, config: &cpal::StreamConfig, shared: Arc<Shared>) -> Result<cpal::Stream, String> {
    let errors = shared.clone();
    let mut buffer: Vec<f32> = Vec::new();
    device
        .build_output_stream(
            config,
            move |data: &mut [T], _| {
                buffer.resize(data.len(), 0.0);
                shared.render(&mut buffer);
                for (out, sample) in data.iter_mut().zip(buffer.iter()) {
                    *out = T::from_sample(*sample);
                }
            },
            move |error| errors.set_device_error(error.to_string()),
            None,
        )
        .map_err(|e| e.to_string())
}
