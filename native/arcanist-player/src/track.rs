//! One piece of music being decoded: Symphonia's reader and decoder, the part to play (a track of a cue sheet is a
//! range of its file), seeking, and the conversion to the device's channels and rate.

use std::io;

use rubato::{FftFixedIn, Resampler};
use symphonia::core::codecs::audio::{AudioDecoder, AudioDecoderOptions};
use symphonia::core::errors::Error;
use symphonia::core::formats::probe::Hint;
use symphonia::core::formats::{FormatOptions, FormatReader, SeekMode, SeekTo, TrackType};
use symphonia::core::io::{MediaSource, MediaSourceStream};
use symphonia::core::meta::MetadataOptions;
use symphonia::core::units::Time;

/// Corrupt packets are skipped, as players do, unless the stream is mostly corrupt.
const MAX_DECODE_ERRORS: u32 = 50;
/// Input frames the resampler takes at a time.
const RESAMPLE_CHUNK: usize = 1024;

/// What the device plays: its rate and its number of channels.
#[derive(Clone, Copy, Debug)]
pub struct Format {
    pub rate: u32,
    pub channels: usize,
}

pub struct Track {
    format: Box<dyn FormatReader>,
    decoder: Box<dyn AudioDecoder>,
    track_id: u32,
    rate: u32,
    out: Format,
    /// Where the part to play starts and ends in the file, in milliseconds (0 = the end of the file).
    start_ms: u64,
    end_ms: u64,
    /// Input frames decoded from the start of the file (after a seek, from where it landed).
    position: u64,
    /// Input frames still to drop before the wanted position (a seek lands a little before it).
    skip: u64,
    resampler: Option<Resample>,
    decoded: Vec<f32>,
    errors: u32,
    ended: bool,
}

impl Track {
    /// Opens a source and places it at `position_ms` of the part from `start_ms` to `end_ms` (0 = to the end).
    pub fn open(source: Box<dyn MediaSource>, extension: Option<&str>, start_ms: u64, end_ms: u64, position_ms: u64, out: Format) -> Result<Track, String> {
        let stream = MediaSourceStream::new(source, Default::default());
        let mut hint = Hint::new();
        if let Some(extension) = extension {
            hint.with_extension(extension);
        }
        let format = symphonia::default::get_probe()
            .probe(&hint, stream, FormatOptions::default(), MetadataOptions::default())
            .map_err(|e| format!("unsupported: {e}"))?;
        let track = format.default_track(TrackType::Audio).ok_or("unsupported: no audio track")?;
        let track_id = track.id;
        let params = track.codec_params.as_ref().and_then(|p| p.audio()).ok_or("unsupported: no audio parameters")?.clone();
        let rate = params.sample_rate.ok_or("unsupported: no sample rate")?;
        let decoder = symphonia::default::get_codecs()
            .make_audio_decoder(&params, &AudioDecoderOptions::default())
            .map_err(|e| format!("unsupported: {e}"))?;

        let mut track = Track {
            format,
            decoder,
            track_id,
            rate,
            out,
            start_ms,
            end_ms,
            position: 0,
            skip: 0,
            resampler: None,
            decoded: Vec::new(),
            errors: 0,
            ended: false,
        };
        track.reset_resampler()?;
        let from = start_ms + position_ms;
        if from > 0 {
            track.seek_to(from)?;
        }
        Ok(track)
    }

    /// Moves to `position_ms` of the part (0 is its start).
    pub fn seek(&mut self, position_ms: u64) -> Result<(), String> {
        self.ended = false;
        self.reset_resampler()?;
        self.seek_to(self.start_ms + position_ms)
    }

    fn seek_to(&mut self, ms: u64) -> Result<(), String> {
        let target = ms * self.rate as u64 / 1000;
        let time = Time::try_from_secs_f64(ms as f64 / 1000.0).ok_or("invalid position")?;
        match self.format.seek(SeekMode::Accurate, SeekTo::Time { time, track_id: Some(self.track_id) }) {
            Ok(seeked) => {
                self.decoder.reset();
                let time_base = self.format.tracks().iter().find(|t| t.id == self.track_id).and_then(|t| t.time_base);
                let frames = |ts: i64| match time_base {
                    Some(tb) => (ts as f64 * tb.numer.get() as f64 / tb.denom.get() as f64 * self.rate as f64).round().max(0.0) as u64,
                    None => ts.max(0) as u64,
                };
                self.position = frames(seeked.actual_ts.get());
                self.skip = target.saturating_sub(self.position);
                Ok(())
            }
            // Streams that cannot seek (a conversion piped from ffmpeg): forward by decoding and dropping.
            Err(_) if target >= self.position => {
                self.skip = target - self.position;
                Ok(())
            }
            Err(e) => Err(format!("cannot seek: {e}")),
        }
    }

    fn reset_resampler(&mut self) -> Result<(), String> {
        self.resampler = if self.rate == self.out.rate {
            None
        } else {
            Some(Resample::new(self.rate, self.out.rate, self.out.channels)?)
        };
        Ok(())
    }

    /// Appends the next piece of audio to `out`, at the device's format; false at the end of the part.
    pub fn next(&mut self, out: &mut Vec<f32>) -> Result<bool, String> {
        if self.ended {
            return Ok(false);
        }
        loop {
            let packet = match self.format.next_packet() {
                Ok(Some(packet)) => packet,
                Ok(None) => return self.finish(out),
                // Truncated files end with an unexpected EOF: play what was decoded.
                Err(Error::IoError(e)) if e.kind() == io::ErrorKind::UnexpectedEof => return self.finish(out),
                Err(e) => return Err(format!("invalid: {e}")),
            };
            if packet.track_id != self.track_id {
                continue;
            }
            let buffer = match self.decoder.decode(&packet) {
                Ok(buffer) => buffer,
                Err(Error::DecodeError(_)) => {
                    self.errors += 1;
                    if self.errors > MAX_DECODE_ERRORS {
                        return Err("invalid: too many decode errors".into());
                    }
                    continue;
                }
                Err(e) => return Err(format!("invalid: {e}")),
            };
            let spec = buffer.spec();
            let channels = spec.channels().count().max(1);
            buffer.copy_to_vec_interleaved(&mut self.decoded);
            let mut frames = (self.decoded.len() / channels) as u64;
            let mut from = 0usize;

            // Before the wanted position (a seek, or the start of a cue track).
            if self.skip > 0 {
                let dropped = self.skip.min(frames);
                self.skip -= dropped;
                from = dropped as usize * channels;
                frames -= dropped;
                self.position += dropped;
            }
            // After the end of the part.
            let mut last = false;
            if self.end_ms > 0 {
                let end = self.end_ms * self.rate as u64 / 1000;
                if self.position + frames >= end {
                    frames = end.saturating_sub(self.position);
                    last = true;
                }
            }
            let to = from + frames as usize * channels;
            self.position += frames;
            if frames > 0 {
                let mapped = map_channels(&self.decoded[from..to], channels, self.out.channels);
                match self.resampler.as_mut() {
                    Some(resampler) => resampler.push(&mapped, out)?,
                    None => out.extend_from_slice(&mapped),
                }
            }
            if last {
                return self.finish(out);
            }
            if !out.is_empty() {
                return Ok(true);
            }
        }
    }

    fn finish(&mut self, out: &mut Vec<f32>) -> Result<bool, String> {
        self.ended = true;
        if let Some(resampler) = self.resampler.as_mut() {
            resampler.flush(out)?;
        }
        Ok(!out.is_empty())
    }
}

/// Fits interleaved audio to the device's channels: mono to every channel, more channels to the first two (or to
/// mono, averaged), stereo into the first two of a device with more.
pub fn map_channels(input: &[f32], from: usize, to: usize) -> Vec<f32> {
    if from == to {
        return input.to_vec();
    }
    let frames = input.len() / from;
    let mut out = vec![0.0; frames * to];
    for frame in 0..frames {
        let source = &input[frame * from..(frame + 1) * from];
        let target = &mut out[frame * to..(frame + 1) * to];
        if from == 1 {
            target.fill(source[0]);
        } else if to == 1 {
            target[0] = (source[0] + source[1]) * 0.5;
        } else {
            let shared = from.min(to);
            target[..shared].copy_from_slice(&source[..shared]);
        }
    }
    out
}

/// Converts the rate with rubato, in fixed input chunks. The output keeps the length and the timing of the input:
/// the resampler's delay is dropped at the start, and the end is flushed and cut to the right length.
struct Resample {
    inner: FftFixedIn<f32>,
    channels: usize,
    ratio: f64,
    pending: Vec<Vec<f32>>,
    /// Output frames still to drop (the resampler's delay).
    delay: usize,
    input: u64,
    output: u64,
}

impl Resample {
    fn new(from: u32, to: u32, channels: usize) -> Result<Self, String> {
        let inner = FftFixedIn::<f32>::new(from as usize, to as usize, RESAMPLE_CHUNK, 2, channels).map_err(|e| format!("resampler: {e}"))?;
        let delay = inner.output_delay();
        Ok(Resample { inner, channels, ratio: to as f64 / from as f64, pending: vec![Vec::new(); channels], delay, input: 0, output: 0 })
    }

    fn push(&mut self, interleaved: &[f32], out: &mut Vec<f32>) -> Result<(), String> {
        for frame in interleaved.chunks_exact(self.channels) {
            for (channel, sample) in frame.iter().enumerate() {
                self.pending[channel].push(*sample);
            }
        }
        self.input += (interleaved.len() / self.channels) as u64;
        while self.pending[0].len() >= self.inner.input_frames_next() {
            let needed = self.inner.input_frames_next();
            let chunk: Vec<Vec<f32>> = self.pending.iter_mut().map(|c| c.drain(..needed).collect()).collect();
            let result = self.inner.process(&chunk, None).map_err(|e| format!("resampler: {e}"))?;
            self.emit(&result, out, u64::MAX);
        }
        Ok(())
    }

    fn flush(&mut self, out: &mut Vec<f32>) -> Result<(), String> {
        let expected = (self.input as f64 * self.ratio).round() as u64;
        let chunk: Vec<Vec<f32>> = self.pending.iter_mut().map(std::mem::take).collect();
        let mut first = true;
        // The rest of the input, then silence through the resampler until all of the input has come out.
        while self.output < expected {
            let result = if first {
                self.inner.process_partial(Some(&chunk), None)
            } else {
                self.inner.process_partial(None::<&[Vec<f32>]>, None)
            }
            .map_err(|e| format!("resampler: {e}"))?;
            first = false;
            if result.first().map(|c| c.is_empty()).unwrap_or(true) {
                break;
            }
            self.emit(&result, out, expected);
        }
        Ok(())
    }

    /// Appends resampled frames, dropping the delay and anything beyond `limit` frames in all.
    fn emit(&mut self, channels: &[Vec<f32>], out: &mut Vec<f32>, limit: u64) {
        let frames = channels.first().map(|c| c.len()).unwrap_or(0);
        let skip = self.delay.min(frames);
        self.delay -= skip;
        let take = ((frames - skip) as u64).min(limit.saturating_sub(self.output)) as usize;
        out.reserve(take * channels.len());
        for frame in skip..skip + take {
            for channel in channels {
                out.push(channel[frame]);
            }
        }
        self.output += take as u64;
    }
}
