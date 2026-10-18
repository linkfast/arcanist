//! Analysis for DJ software: beat grid, phrases and sections, energy and voice, onsets, silences and fades,
//! loudness and key, the highlight, and where to mix and scratch. One decode, mono at ~22 kHz for the timing
//! and spectral features (plus ~11 kHz for the key), the original signal for loudness.

mod beats;
mod events;
mod features;
mod structure;
mod timing;

#[cfg(test)]
mod tests;

use ebur128::{EbuR128, Mode};
use serde::Serialize;

use crate::analyzer::base64;
use crate::dsp::Decimator;
use crate::key;

use features::FeatureExtractor;
use timing::{Timing, db};

/// Bumped whenever results would change for the same audio: the server re-analyzes, serving the old result
/// (marked stale) meanwhile.
pub const DJ_ANALYZER_VERSION: u32 = 1;

const FEATURE_RATE: f32 = 22050.0;
/// Longer audio (a DJ mix) is analyzed for its first 30 minutes; loudness covers all of it.
const MAX_SECONDS: f32 = 1800.0;
/// The key looks at the first 10 minutes.
const KEY_SECONDS: f32 = 600.0;
const ENERGY_STEP: f32 = 0.1;
const VOCAL_STEP: f32 = 0.5;

pub struct DjAnalyzer {
    rate: u32,
    channels: u32,
    frames: u64,
    loudness: EbuR128,
    decimator: Decimator,
    features: FeatureExtractor,
    key_decimator: Decimator,
    key_signal: Vec<f32>,
    key_limit: usize,
}

impl DjAnalyzer {
    pub fn new(rate: u32, channels: u32) -> Result<Self, String> {
        if rate == 0 || channels == 0 {
            return Err("invalid audio format".into());
        }
        let decimator = Decimator::to_rate(rate, FEATURE_RATE);
        let key_decimator = Decimator::new(rate);
        Ok(DjAnalyzer {
            rate,
            channels,
            frames: 0,
            loudness: EbuR128::new(channels, rate, Mode::I | Mode::LRA | Mode::TRUE_PEAK).map_err(|e| e.to_string())?,
            features: FeatureExtractor::new(decimator.output_rate(), MAX_SECONDS),
            decimator,
            key_limit: (key_decimator.output_rate() * KEY_SECONDS) as usize,
            key_decimator,
            key_signal: Vec::new(),
        })
    }

    /// Adds interleaved samples in [-1, 1]. A trailing partial frame is ignored.
    pub fn push(&mut self, samples: &[f32]) -> Result<(), String> {
        let channels = self.channels as usize;
        let samples = &samples[..samples.len() - samples.len() % channels];
        self.loudness.add_frames_f32(samples).map_err(|e| e.to_string())?;
        for frame in samples.chunks_exact(channels) {
            let mono = frame.iter().sum::<f32>() / channels as f32;
            if let Some(sample) = self.decimator.push(mono) {
                self.features.push(sample);
            }
            if self.key_signal.len() < self.key_limit {
                if let Some(sample) = self.key_decimator.push(mono) {
                    self.key_signal.push(sample);
                }
            }
        }
        self.frames += (samples.len() / channels) as u64;
        Ok(())
    }

    pub fn finish(mut self) -> Result<DjAnalysis, String> {
        if self.frames == 0 {
            return Err("no audio".into());
        }
        self.features.finish();
        let frame_seconds = self.features.frame_seconds();
        features::syllabic(&mut self.features.frames, frame_seconds);
        let duration = self.frames as f32 / self.rate as f32;
        let timing = Timing {
            rate: self.decimator.output_rate(),
            frame_seconds: self.features.frame_seconds(),
            micro_seconds: self.features.micro_seconds(),
            micro: std::mem::take(&mut self.features.micro),
        };
        let f = &self.features.frames;

        let silences = events::silences(&timing);
        let music = events::music_range(&silences, timing.duration().min(duration));
        let grid = beats::grid(&timing, &f.flux, &f.bass, &f.chroma, music);
        let structure = structure::analyze(&timing, grid.as_ref(), &f.chroma, &f.mfcc, &f.vocal, music);
        let energy = events::energy_db(&timing, ENERGY_STEP);
        let (fade_in, fade_out) = events::fades(&energy, ENERGY_STEP, music);
        let onsets = events::onsets(&timing, &f.flux);
        let cues = events::scratch_cues(&timing, &onsets, &f.vocal, &f.harmonic, structure.highlight.map(|(a, b, _)| (a, b)));
        let vocals = events::vocal_curve(&timing, &f.vocal, VOCAL_STEP);

        let integrated = self.loudness.loudness_global().map_err(|e| e.to_string())?;
        let range = self.loudness.loudness_range().map_err(|e| e.to_string())?;
        let peak = (0..self.channels).filter_map(|c| self.loudness.true_peak(c).ok()).fold(0.0, f64::max);
        let key = key::estimate(&self.key_signal, self.key_decimator.output_rate());

        let tempo = grid.as_ref().map(|g| {
            let downbeats = g.downbeat_times();
            Tempo {
                bpm: round(g.bpm as f64, 2),
                confidence: round(g.confidence as f64, 2),
                stable: g.stable,
                beats_per_bar: g.beats_per_bar,
                first_beat: g.beats.first().map(|t| ms(*t)),
                first_downbeat: downbeats.first().map(|t| ms(*t)),
                beats: (!g.stable).then(|| floats(&g.beats)),
                downbeats: (!g.stable).then(|| floats(&downbeats)),
            }
        });

        Ok(DjAnalysis {
            analyzer_version: DJ_ANALYZER_VERSION,
            duration_seconds: ms(duration),
            music_start: ms(music.0),
            music_end: ms(music.1),
            tempo,
            phrases: structure.phrases.iter().map(|p| Phrase { start: ms(p.start), bars: p.bars }).collect(),
            loudness: Loudness {
                integrated_lufs: round(if integrated.is_finite() { integrated } else { -70.0 }, 1),
                range: round(if range.is_finite() { range } else { 0.0 }, 1),
                true_peak_db: round(if peak > 0.0 { 20.0 * peak.log10() } else { -70.0 }, 1),
            },
            key: key.map(|k| Key { pitch_class: k.tonic, minor: k.minor, confidence: round(k.confidence as f64, 2) }),
            energy: Curve { step_seconds: ENERGY_STEP as f64, values: base64(&energy.iter().map(|e| events::level_byte(*e)).collect::<Vec<u8>>(), false) },
            vocals: VocalCurve { step_seconds: VOCAL_STEP as f64, presence: bytes(&vocals) },
            onsets: Onsets {
                times: floats(&onsets.iter().map(|o| o.time).collect::<Vec<f32>>()),
                strengths: bytes(&onsets.iter().map(|o| o.strength).collect::<Vec<f32>>()),
            },
            silences: silences.iter().map(|s| Silence { start: ms(s.start), end: ms(s.end), kind: s.kind }).collect(),
            fades: Fades { fade_in_end: fade_in.map(ms), fade_out_start: fade_out.map(ms) },
            sections: structure
                .sections
                .iter()
                .map(|s| Section {
                    start: ms(s.start),
                    end: ms(s.end),
                    label: s.label.to_string(),
                    name: s.name,
                    energy: round(s.energy as f64, 2),
                    trend: s.trend,
                    occurrence: s.occurrence,
                })
                .collect(),
            highlight: structure.highlight.map(|(start, end, confidence)| Highlight { start: ms(start), end: ms(end), confidence: round(confidence as f64, 2) }),
            mix_points: MixPoints {
                intro_end: ms(structure.intro_end),
                outro_start: ms(structure.outro_start),
                mix_in: structure.mix_in.iter().map(|t| ms(*t)).collect(),
                mix_out: structure.mix_out.iter().map(|t| ms(*t)).collect(),
            },
            scratch_cues: cues
                .iter()
                .map(|c| ScratchCue {
                    t: ms(c.time),
                    duration: ms(c.duration),
                    kind: c.kind,
                    isolation: round(c.isolation as f64, 2),
                    strength: round(c.strength as f64, 2),
                })
                .collect(),
            peak_db: round(db(timing.micro.iter().cloned().fold(0.0f32, f32::max)) as f64, 1),
        })
    }
}

fn round(value: f64, decimals: i32) -> f64 {
    let factor = 10f64.powi(decimals);
    (value * factor).round() / factor
}

/// Seconds with three decimals (never below zero: an estimate a hair before the start is the start).
fn ms(seconds: f32) -> f64 {
    round(seconds.max(0.0) as f64, 3)
}

/// Float32 little endian, base64.
fn floats(values: &[f32]) -> String {
    base64(&values.iter().flat_map(|v| v.to_le_bytes()).collect::<Vec<u8>>(), false)
}

/// Values 0–1 as bytes 0–255, base64.
fn bytes(values: &[f32]) -> String {
    base64(&values.iter().map(|v| (v.clamp(0.0, 1.0) * 255.0).round() as u8).collect::<Vec<u8>>(), false)
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DjAnalysis {
    pub analyzer_version: u32,
    pub duration_seconds: f64,
    pub music_start: f64,
    pub music_end: f64,
    pub tempo: Option<Tempo>,
    pub phrases: Vec<Phrase>,
    pub loudness: Loudness,
    pub key: Option<Key>,
    pub energy: Curve,
    pub vocals: VocalCurve,
    pub onsets: Onsets,
    pub silences: Vec<Silence>,
    pub fades: Fades,
    pub sections: Vec<Section>,
    pub highlight: Option<Highlight>,
    pub mix_points: MixPoints,
    pub scratch_cues: Vec<ScratchCue>,
    /// The loudest fine block, in dBFS (for tests and diagnostics).
    #[serde(skip)]
    pub peak_db: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Tempo {
    pub bpm: f64,
    pub confidence: f64,
    pub stable: bool,
    pub beats_per_bar: usize,
    pub first_beat: Option<f64>,
    pub first_downbeat: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub beats: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub downbeats: Option<String>,
}

#[derive(Debug, Serialize)]
pub struct Phrase {
    pub start: f64,
    pub bars: usize,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Loudness {
    pub integrated_lufs: f64,
    pub range: f64,
    pub true_peak_db: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Key {
    /// 0 = C … 11 = B.
    pub pitch_class: u8,
    pub minor: bool,
    pub confidence: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Curve {
    pub step_seconds: f64,
    pub values: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VocalCurve {
    pub step_seconds: f64,
    pub presence: String,
}

#[derive(Debug, Serialize)]
pub struct Onsets {
    pub times: String,
    pub strengths: String,
}

#[derive(Debug, Serialize)]
pub struct Silence {
    pub start: f64,
    pub end: f64,
    pub kind: &'static str,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Fades {
    pub fade_in_end: Option<f64>,
    pub fade_out_start: Option<f64>,
}

#[derive(Debug, Serialize)]
pub struct Section {
    pub start: f64,
    pub end: f64,
    pub label: String,
    pub name: &'static str,
    pub energy: f64,
    pub trend: &'static str,
    pub occurrence: usize,
}

#[derive(Debug, Serialize)]
pub struct Highlight {
    pub start: f64,
    pub end: f64,
    pub confidence: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MixPoints {
    pub intro_end: f64,
    pub outro_start: f64,
    pub mix_in: Vec<f64>,
    pub mix_out: Vec<f64>,
}

#[derive(Debug, Serialize)]
pub struct ScratchCue {
    pub t: f64,
    pub duration: f64,
    pub kind: &'static str,
    pub isolation: f64,
    pub strength: f64,
}
