//! Consumes decoded PCM once and feeds every analysis from it.

use ebur128::{EbuR128, Mode};
use rusty_chromaprint::{Configuration, FingerprintCompressor, Fingerprinter};
use serde::Serialize;

use crate::dsp::Decimator;
use crate::{key, tempo};

/// Bumped whenever results would change for the same audio, so the server re-analyzes stored files.
pub const ANALYZER_VERSION: u32 = 1;

pub const FLAG_LOUDNESS: u32 = 1;
pub const FLAG_FINGERPRINT: u32 = 2;
pub const FLAG_WAVEFORM: u32 = 4;
pub const FLAG_TEMPO: u32 = 8;
pub const FLAG_KEY: u32 = 16;
pub const FLAG_ALL: u32 = 31;

/// AcoustID fingerprints cover the first two minutes, like fpcalc.
const FINGERPRINT_SECONDS: u64 = 120;
/// Tempo and key look at the first ten minutes; long mixes add memory, not accuracy.
const TONAL_SECONDS: f32 = 600.0;
/// Points in the waveform overview: one byte each.
const WAVEFORM_POINTS: usize = 1000;

/// Loudness histogram: 0.1 LU bins from −70 LUFS, as libebur128 keeps it. Enough to combine the tracks of an
/// album into an album loudness without decoding them again.
const HISTOGRAM_BINS: usize = 1000;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Analysis {
    pub analyzer_version: u32,
    pub duration_ms: u64,
    pub sample_rate: u32,
    pub channels: u32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub loudness: Option<Loudness>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fingerprint: Option<Fingerprint>,
    /// Base64 of [`WAVEFORM_POINTS`] bytes, each the peak amplitude of its slice (0–255).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub waveform: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tempo: Option<tempo::Tempo>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub key: Option<key::Key>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Loudness {
    /// EBU R128 integrated loudness, LUFS.
    pub integrated: f64,
    /// Loudness range, LU.
    pub range: f64,
    /// Highest true peak over all channels, linear (1.0 = full scale).
    pub true_peak: f64,
    /// Base64 of little-endian (u16 bin, u32 count) pairs for non-empty bins of the block-loudness histogram.
    pub histogram: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Fingerprint {
    /// What AcoustID expects: Chromaprint's compressed form, base64url without padding (as fpcalc prints it).
    pub compressed: String,
    /// The raw sub-fingerprints, base64 of little-endian u32, for local comparisons.
    pub raw: String,
    pub duration_ms: u64,
}

pub struct Analyzer {
    flags: u32,
    rate: u32,
    channels: u32,
    frames: u64,

    loudness: Option<EbuR128>,
    pending: Vec<f32>,
    block_frames: usize,
    blocks_seen: u32,
    histogram: Vec<u32>,

    fingerprinter: Option<Fingerprinter>,
    fingerprint_config: Configuration,
    fingerprint_frames: u64,
    pcm16: Vec<i16>,

    decimator: Decimator,
    tonal: Vec<f32>,
    tonal_limit: usize,

    peak_frames: u64,
    peak_current: f32,
    peaks: Vec<f32>,
}

impl Analyzer {
    pub fn new(rate: u32, channels: u32, flags: u32) -> Result<Self, String> {
        if rate == 0 || channels == 0 {
            return Err("invalid audio format".into());
        }

        let loudness = if flags & FLAG_LOUDNESS != 0 {
            Some(EbuR128::new(channels, rate, Mode::I | Mode::LRA | Mode::TRUE_PEAK).map_err(|e| e.to_string())?)
        } else {
            None
        };

        let fingerprint_config = Configuration::preset_test2();
        let fingerprinter = if flags & FLAG_FINGERPRINT != 0 {
            let mut fingerprinter = Fingerprinter::new(&fingerprint_config);
            fingerprinter.start(rate, channels).map_err(|e| format!("{e:?}"))?;
            Some(fingerprinter)
        } else {
            None
        };

        let decimator = Decimator::new(rate);
        let tonal_limit = if flags & (FLAG_TEMPO | FLAG_KEY) != 0 { (decimator.output_rate() * TONAL_SECONDS) as usize } else { 0 };

        Ok(Analyzer {
            flags,
            rate,
            channels,
            frames: 0,
            loudness,
            pending: Vec::new(),
            block_frames: (rate / 10) as usize,
            blocks_seen: 0,
            histogram: vec![0; HISTOGRAM_BINS],
            fingerprinter,
            fingerprint_config,
            fingerprint_frames: 0,
            pcm16: Vec::new(),
            decimator,
            tonal: Vec::new(),
            tonal_limit,
            // A peak every 10 ms; reduced to WAVEFORM_POINTS at the end, when the duration is known.
            peak_frames: (rate / 100).max(1) as u64,
            peak_current: 0.0,
            peaks: Vec::new(),
        })
    }

    pub fn format(&self) -> (u32, u32) {
        (self.rate, self.channels)
    }

    /// Adds interleaved samples in [-1, 1]. A trailing partial frame is ignored.
    pub fn push(&mut self, samples: &[f32]) -> Result<(), String> {
        let channels = self.channels as usize;
        let samples = &samples[..samples.len() - samples.len() % channels];
        let frame_count = (samples.len() / channels) as u64;

        if self.loudness.is_some() {
            self.push_loudness(samples)?;
        }

        if let Some(fingerprinter) = self.fingerprinter.as_mut() {
            let wanted = FINGERPRINT_SECONDS * self.rate as u64;
            if self.fingerprint_frames < wanted {
                let take = (wanted - self.fingerprint_frames).min(frame_count) as usize * channels;
                self.pcm16.clear();
                self.pcm16.extend(samples[..take].iter().map(|s| (s.clamp(-1.0, 1.0) * 32767.0) as i16));
                fingerprinter.consume(&self.pcm16);
                self.fingerprint_frames += (take / channels) as u64;
            }
        }

        let need_tonal = self.tonal.len() < self.tonal_limit;
        let need_waveform = self.flags & FLAG_WAVEFORM != 0;
        if need_tonal || need_waveform {
            for frame in samples.chunks_exact(channels) {
                let mono = frame.iter().sum::<f32>() / channels as f32;
                if need_tonal && self.tonal.len() < self.tonal_limit {
                    if let Some(sample) = self.decimator.push(mono) {
                        self.tonal.push(sample);
                    }
                }
                if need_waveform {
                    let peak = frame.iter().fold(0.0f32, |max, s| max.max(s.abs()));
                    self.peak_current = self.peak_current.max(peak);
                    self.peak_frames -= 1;
                    if self.peak_frames == 0 {
                        self.peaks.push(self.peak_current);
                        self.peak_current = 0.0;
                        self.peak_frames = (self.rate / 100).max(1) as u64;
                    }
                }
            }
        }

        self.frames += frame_count;
        Ok(())
    }

    /// Adds frames to the loudness meter in 100 ms steps, reading the momentary loudness after each: those are
    /// exactly the overlapping 400 ms gating blocks of BS.1770, which go into the histogram.
    fn push_loudness(&mut self, samples: &[f32]) -> Result<(), String> {
        let channels = self.channels as usize;
        let block = self.block_frames * channels;
        self.pending.extend_from_slice(samples);

        let meter = self.loudness.as_mut().unwrap();
        let mut offset = 0;
        while self.pending.len() - offset >= block {
            meter.add_frames_f32(&self.pending[offset..offset + block]).map_err(|e| e.to_string())?;
            offset += block;
            self.blocks_seen += 1;
            // The first full 400 ms window ends with the fourth step.
            if self.blocks_seen >= 4 {
                if let Ok(momentary) = meter.loudness_momentary() {
                    if momentary.is_finite() && momentary >= -70.0 {
                        let bin = (((momentary + 70.0) * 10.0) as usize).min(HISTOGRAM_BINS - 1);
                        self.histogram[bin] += 1;
                    }
                }
            }
        }
        self.pending.drain(..offset);
        Ok(())
    }

    pub fn finish(mut self) -> Result<Analysis, String> {
        if self.frames == 0 {
            return Err("no audio".into());
        }

        let loudness = match self.loudness.as_mut() {
            Some(meter) => {
                if !self.pending.is_empty() {
                    meter.add_frames_f32(&self.pending).map_err(|e| e.to_string())?;
                }
                let integrated = meter.loudness_global().map_err(|e| e.to_string())?;
                let range = meter.loudness_range().map_err(|e| e.to_string())?;
                let true_peak = (0..self.channels).filter_map(|c| meter.true_peak(c).ok()).fold(0.0, f64::max);
                Some(Loudness {
                    // Digital silence has no loudness; report the floor instead of -inf.
                    integrated: if integrated.is_finite() { integrated } else { -70.0 },
                    range: if range.is_finite() { range } else { 0.0 },
                    true_peak,
                    histogram: encode_histogram(&self.histogram),
                })
            }
            None => None,
        };

        let fingerprint = match self.fingerprinter.as_mut() {
            Some(fingerprinter) => {
                fingerprinter.finish();
                let raw = fingerprinter.fingerprint();
                (!raw.is_empty()).then(|| Fingerprint {
                    compressed: base64(&FingerprintCompressor::from(&self.fingerprint_config).compress(raw), true),
                    raw: base64(&raw.iter().flat_map(|v| v.to_le_bytes()).collect::<Vec<_>>(), false),
                    duration_ms: self.fingerprint_frames * 1000 / self.rate as u64,
                })
            }
            None => None,
        };

        let waveform = (self.flags & FLAG_WAVEFORM != 0).then(|| {
            if self.peak_current > 0.0 {
                self.peaks.push(self.peak_current);
            }
            base64(&reduce_peaks(&self.peaks, WAVEFORM_POINTS), false)
        });

        let rate = self.decimator.output_rate();
        let tempo = if self.flags & FLAG_TEMPO != 0 { tempo::estimate(&self.tonal, rate) } else { None };
        let key = if self.flags & FLAG_KEY != 0 { key::estimate(&self.tonal, rate) } else { None };

        Ok(Analysis {
            analyzer_version: ANALYZER_VERSION,
            duration_ms: self.frames * 1000 / self.rate as u64,
            sample_rate: self.rate,
            channels: self.channels,
            loudness,
            fingerprint,
            waveform,
            tempo,
            key,
        })
    }
}

/// Reduces the 10 ms peaks to `points` values (max of each slice), scaled to bytes.
fn reduce_peaks(peaks: &[f32], points: usize) -> Vec<u8> {
    if peaks.is_empty() {
        return vec![0; points];
    }
    (0..points)
        .map(|i| {
            let from = i * peaks.len() / points;
            let to = ((i + 1) * peaks.len() / points).max(from + 1).min(peaks.len());
            let peak = peaks[from.min(peaks.len() - 1)..to].iter().fold(0.0f32, |m, v| m.max(*v));
            (peak.clamp(0.0, 1.0) * 255.0).round() as u8
        })
        .collect()
}

fn encode_histogram(histogram: &[u32]) -> String {
    let mut bytes = Vec::new();
    for (bin, count) in histogram.iter().enumerate().filter(|(_, c)| **c > 0) {
        bytes.extend_from_slice(&(bin as u16).to_le_bytes());
        bytes.extend_from_slice(&count.to_le_bytes());
    }
    base64(&bytes, false)
}

pub fn base64(bytes: &[u8], url_safe: bool) -> String {
    let alphabet: &[u8; 64] = if url_safe {
        b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    } else {
        b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    };
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let n = (chunk[0] as u32) << 16 | (*chunk.get(1).unwrap_or(&0) as u32) << 8 | *chunk.get(2).unwrap_or(&0) as u32;
        let digits = chunk.len() + 1;
        for i in 0..4 {
            if i < digits {
                out.push(alphabet[(n >> (18 - 6 * i) & 63) as usize] as char);
            } else if !url_safe {
                out.push('=');
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sine(frequency: f32, amplitude: f32, seconds: f32, rate: u32, channels: u32) -> Vec<f32> {
        (0..(seconds * rate as f32) as usize)
            .flat_map(|i| {
                let v = amplitude * (2.0 * std::f32::consts::PI * frequency * i as f32 / rate as f32).sin();
                std::iter::repeat_n(v, channels as usize)
            })
            .collect()
    }

    #[test]
    fn measures_loudness_of_a_reference_tone() {
        // A 1 kHz stereo sine at -20 dBFS reads close to -20 LUFS (K-weighting is ~0 dB at 1 kHz).
        let mut analyzer = Analyzer::new(48000, 2, FLAG_LOUDNESS).unwrap();
        for chunk in sine(1000.0, 0.1, 20.0, 48000, 2).chunks(4801) {
            analyzer.push(chunk).unwrap();
        }
        let loudness = analyzer.finish().unwrap().loudness.unwrap();
        assert!((loudness.integrated + 20.0).abs() < 0.5, "{}", loudness.integrated);
        assert!((loudness.true_peak - 0.1).abs() < 0.01);
        assert!(!loudness.histogram.is_empty());
    }

    #[test]
    fn fingerprints_and_waveform_cover_the_audio() {
        let mut analyzer = Analyzer::new(44100, 2, FLAG_ALL).unwrap();
        analyzer.push(&sine(440.0, 0.5, 30.0, 44100, 2)).unwrap();
        let analysis = analyzer.finish().unwrap();

        assert_eq!(analysis.duration_ms, 30_000);
        let fingerprint = analysis.fingerprint.unwrap();
        assert_eq!(fingerprint.duration_ms, 30_000);
        assert!(!fingerprint.compressed.contains('=') && !fingerprint.compressed.contains('+'));
        assert_eq!(analysis.waveform.unwrap().len(), WAVEFORM_POINTS.div_ceil(3) * 4);
    }

    #[test]
    fn base64_matches_rfc_4648() {
        assert_eq!(base64(b"foobar", false), "Zm9vYmFy");
        assert_eq!(base64(b"fooba", false), "Zm9vYmE=");
        assert_eq!(base64(b"foob", true), "Zm9vYg");
        assert_eq!(base64(&[0xfb, 0xff], true), "-_8");
    }
}
