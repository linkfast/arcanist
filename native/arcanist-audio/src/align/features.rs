//! What the aligner needs from each side, extracted while the audio streams in: full-length Chromaprint
//! sub-fingerprints (the coarse match), a 100 Hz onset envelope (the fine offset) and the momentary loudness
//! every 100 ms (to match the levels of both sides).

use ebur128::{EbuR128, Mode};
use rusty_chromaprint::{Configuration, Fingerprinter};

use crate::dsp::Decimator;

/// Envelope frames per second.
pub const ENVELOPE_RATE: f32 = 100.0;
/// Loudness blocks per second.
pub const LOUDNESS_RATE: f32 = 10.0;
/// Longer audio is aligned on its first 30 minutes.
const MAX_SECONDS: u64 = 1800;
/// Loudness of digital silence, and the floor below which a block says nothing about the level.
pub const SILENCE_LUFS: f32 = -70.0;

pub struct Features {
    pub prints: Vec<u32>,
    /// Seconds covered by each sub-fingerprint.
    pub item_seconds: f32,
    pub envelope: Vec<f32>,
    pub loudness: Vec<f32>,
    pub duration_ms: u64,
}

pub struct FeatureExtractor {
    rate: u32,
    channels: u32,
    frames: u64,
    limit: u64,
    fingerprinter: Fingerprinter,
    item_seconds: f32,
    pcm16: Vec<i16>,
    decimator: Decimator,
    previous: f32,
    block_energy: f32,
    block_fill: f32,
    block_size: f32,
    envelope: Vec<f32>,
    meter: EbuR128,
    pending: Vec<f32>,
    loudness_block: usize,
    loudness_seen: u32,
    loudness: Vec<f32>,
}

impl FeatureExtractor {
    pub fn new(rate: u32, channels: u32) -> Result<Self, String> {
        if rate == 0 || channels == 0 {
            return Err("invalid audio format".into());
        }
        let config = Configuration::preset_test2();
        let mut fingerprinter = Fingerprinter::new(&config);
        fingerprinter.start(rate, channels).map_err(|e| format!("{e:?}"))?;
        let decimator = Decimator::new(rate);
        Ok(FeatureExtractor {
            rate,
            channels,
            frames: 0,
            limit: MAX_SECONDS * rate as u64,
            fingerprinter,
            item_seconds: config.item_duration_in_seconds(),
            pcm16: Vec::new(),
            block_size: decimator.output_rate() / ENVELOPE_RATE,
            decimator,
            previous: 0.0,
            block_energy: 0.0,
            block_fill: 0.0,
            envelope: Vec::new(),
            meter: EbuR128::new(channels, rate, Mode::M).map_err(|e| e.to_string())?,
            pending: Vec::new(),
            loudness_block: (rate as f32 / LOUDNESS_RATE) as usize,
            loudness_seen: 0,
            loudness: Vec::new(),
        })
    }

    /// Adds interleaved samples in [-1, 1]. A trailing partial frame is ignored.
    pub fn push(&mut self, samples: &[f32]) -> Result<(), String> {
        let channels = self.channels as usize;
        if self.frames >= self.limit {
            return Ok(());
        }
        let available = (samples.len() / channels) as u64;
        let take = available.min(self.limit - self.frames) as usize * channels;
        let samples = &samples[..take];

        self.pcm16.clear();
        self.pcm16.extend(samples.iter().map(|s| (s.clamp(-1.0, 1.0) * 32767.0) as i16));
        self.fingerprinter.consume(&self.pcm16);

        for frame in samples.chunks_exact(channels) {
            let mono = frame.iter().sum::<f32>() / channels as f32;
            if let Some(sample) = self.decimator.push(mono) {
                // Energy of the first difference: a cheap high-pass that makes attacks stand out.
                let difference = sample - self.previous;
                self.previous = sample;
                self.block_energy += difference * difference;
                self.block_fill += 1.0;
                if self.block_fill >= self.block_size {
                    self.envelope.push((self.block_energy / self.block_fill + 1e-9).ln());
                    self.block_energy = 0.0;
                    self.block_fill -= self.block_size;
                }
            }
        }

        self.pending.extend_from_slice(samples);
        let block = self.loudness_block * channels;
        let mut offset = 0;
        while self.pending.len() - offset >= block {
            self.meter.add_frames_f32(&self.pending[offset..offset + block]).map_err(|e| e.to_string())?;
            offset += block;
            self.loudness_seen += 1;
            let momentary = if self.loudness_seen >= 4 { self.meter.loudness_momentary().unwrap_or(f64::NEG_INFINITY) } else { f64::NEG_INFINITY };
            self.loudness.push(if momentary.is_finite() { (momentary as f32).max(SILENCE_LUFS) } else { SILENCE_LUFS });
        }
        self.pending.drain(..offset);

        self.frames += (take / channels) as u64;
        Ok(())
    }

    pub fn finish(mut self) -> Result<Features, String> {
        if self.frames == 0 {
            return Err("no audio".into());
        }
        self.fingerprinter.finish();
        // Onset strength: how much the log energy rises from one frame to the next.
        let mut onsets = Vec::with_capacity(self.envelope.len());
        for i in 0..self.envelope.len() {
            let rise = if i == 0 { 0.0 } else { self.envelope[i] - self.envelope[i - 1] };
            onsets.push(rise.max(0.0));
        }
        Ok(Features {
            prints: self.fingerprinter.fingerprint().to_vec(),
            item_seconds: self.item_seconds,
            envelope: onsets,
            loudness: self.loudness,
            duration_ms: self.frames * 1000 / self.rate as u64,
        })
    }
}
