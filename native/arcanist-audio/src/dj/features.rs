//! Per-frame features of the mono, decimated signal: one short-time spectrum every `HOP` samples gives the onset
//! envelope, bass energy, chroma, MFCCs and, through a harmonic/percussive split, a vocal likelihood. Fine
//! energy blocks (`MICRO` samples) keep the timing that the frames are too coarse for: attacks and silences.

use std::collections::VecDeque;
use std::sync::Arc;

use rustfft::num_complex::Complex32;
use rustfft::{Fft, FftPlanner};

pub const FFT_SIZE: usize = 1024;
pub const HOP: usize = 256;
/// Samples per fine energy block: about 1.5 ms at 22 kHz.
pub const MICRO: usize = 32;
/// Frames on each side of the median that separates harmonic from percussive energy.
const HPSS_HALF: usize = 8;
const MEL_BANDS: usize = 26;
pub const MFCC: usize = 13;

/// What the analysis keeps of every frame (about 30 floats), so long tracks fit in little memory.
#[derive(Default)]
pub struct Frames {
    /// Spectral flux of log-compressed magnitudes: how much energy appears from one frame to the next.
    pub flux: Vec<f32>,
    /// Energy below 150 Hz.
    pub bass: Vec<f32>,
    pub chroma: Vec<[f32; 12]>,
    pub mfcc: Vec<[f32; MFCC]>,
    /// 0–1: how likely the frame holds a voice (harmonic, tonal energy in the voice's band).
    pub vocal: Vec<f32>,
    /// 0–1: the share of harmonic (sustained) energy in 100–4000 Hz.
    pub harmonic: Vec<f32>,
    /// Harmonic energy in the voice's band, for its syllabic modulation.
    pub voice_energy: Vec<f32>,
}

pub struct FeatureExtractor {
    rate: f32,
    fft: Arc<dyn Fft<f32>>,
    window: Vec<f32>,
    buffer: Vec<Complex32>,
    scratch: Vec<Complex32>,
    /// Decimated samples not yet consumed by a frame.
    pending: Vec<f32>,
    previous_log: Vec<f32>,
    chroma_bins: Vec<(usize, usize)>,
    mel: Vec<Vec<(usize, f32)>>,
    /// Bins the harmonic/percussive split looks at (up to ~4 kHz) and the bands it measures.
    hpss_bins: usize,
    voice_band: (usize, usize),
    harmonic_band: (usize, usize),
    /// The last frames' low magnitudes, for the median over time.
    recent: VecDeque<Vec<f32>>,
    /// Frames waiting for their harmonic/percussive split (it needs `HPSS_HALF` frames after them).
    split_done: usize,
    pub frames: Frames,
    micro_energy: f32,
    micro_count: usize,
    /// Mean square of each `MICRO`-sample block.
    pub micro: Vec<f32>,
    max_frames: usize,
}

impl FeatureExtractor {
    pub fn new(rate: f32, max_seconds: f32) -> Self {
        let fft = FftPlanner::new().plan_fft_forward(FFT_SIZE);
        let window = (0..FFT_SIZE).map(|i| 0.5 - 0.5 * (2.0 * std::f32::consts::PI * i as f32 / FFT_SIZE as f32).cos()).collect();
        let bin_hz = rate / FFT_SIZE as f32;
        let bin = |hz: f32| ((hz / bin_hz).round() as usize).min(FFT_SIZE / 2);

        // Pitch classes from 110 Hz to 5 kHz: below that the bins are wider than a semitone.
        let chroma_bins = (bin(110.0)..=bin(5000.0))
            .map(|b| {
                let midi = 69.0 + 12.0 * (b as f32 * bin_hz / 440.0).log2();
                (b, (midi.round() as i32).rem_euclid(12) as usize)
            })
            .collect();

        Self {
            rate,
            scratch: vec![Complex32::new(0.0, 0.0); fft.get_inplace_scratch_len()],
            fft,
            window,
            buffer: vec![Complex32::new(0.0, 0.0); FFT_SIZE],
            pending: Vec::with_capacity(FFT_SIZE * 2),
            previous_log: Vec::new(),
            chroma_bins,
            mel: mel_filters(rate, MEL_BANDS, 30.0, (rate / 2.0).min(8000.0)),
            hpss_bins: bin(4100.0),
            voice_band: (bin(300.0), bin(3400.0)),
            harmonic_band: (bin(100.0), bin(4000.0)),
            recent: VecDeque::with_capacity(2 * HPSS_HALF + 1),
            split_done: 0,
            frames: Frames::default(),
            micro_energy: 0.0,
            micro_count: 0,
            micro: Vec::new(),
            max_frames: (max_seconds * rate / HOP as f32) as usize,
        }
    }

    /// Seconds per frame and per fine block.
    pub fn frame_seconds(&self) -> f32 {
        HOP as f32 / self.rate
    }

    pub fn micro_seconds(&self) -> f32 {
        MICRO as f32 / self.rate
    }

    pub fn full(&self) -> bool {
        self.frames.flux.len() >= self.max_frames
    }

    pub fn push(&mut self, sample: f32) {
        if self.full() {
            return;
        }
        self.micro_energy += sample * sample;
        self.micro_count += 1;
        if self.micro_count == MICRO {
            self.micro.push(self.micro_energy / MICRO as f32);
            self.micro_energy = 0.0;
            self.micro_count = 0;
        }
        self.pending.push(sample);
        if self.pending.len() >= FFT_SIZE {
            self.frame();
            self.pending.drain(..HOP);
        }
    }

    fn frame(&mut self) {
        for (i, value) in self.buffer.iter_mut().enumerate() {
            *value = Complex32::new(self.pending[i] * self.window[i], 0.0);
        }
        self.fft.process_with_scratch(&mut self.buffer, &mut self.scratch);
        let magnitudes: Vec<f32> = self.buffer[..FFT_SIZE / 2 + 1].iter().map(|c| c.norm()).collect();

        let log: Vec<f32> = magnitudes.iter().map(|m| (1.0 + 100.0 * m).ln()).collect();
        let flux = if self.previous_log.is_empty() { 0.0 } else { log.iter().zip(&self.previous_log).map(|(c, p)| (c - p).max(0.0)).sum() };
        self.previous_log = log;
        self.frames.flux.push(flux);

        let bass_top = ((150.0 / (self.rate / FFT_SIZE as f32)) as usize).max(1);
        self.frames.bass.push(magnitudes[1..=bass_top].iter().map(|m| m * m).sum());

        let mut chroma = [0.0f32; 12];
        for &(bin, class) in &self.chroma_bins {
            chroma[class] += magnitudes[bin] * magnitudes[bin];
        }
        self.frames.chroma.push(chroma);

        let mut mel = [0.0f32; MEL_BANDS];
        for (band, filter) in self.mel.iter().enumerate() {
            mel[band] = filter.iter().map(|&(bin, weight)| magnitudes[bin] * magnitudes[bin] * weight).sum::<f32>();
        }
        let log_mel: Vec<f32> = mel.iter().map(|e| (e + 1e-6).ln()).collect();
        let mut mfcc = [0.0f32; MFCC];
        for (k, value) in mfcc.iter_mut().enumerate() {
            *value = log_mel
                .iter()
                .enumerate()
                .map(|(n, e)| e * (std::f32::consts::PI * k as f32 * (n as f32 + 0.5) / MEL_BANDS as f32).cos())
                .sum();
        }
        self.frames.mfcc.push(mfcc);

        if self.recent.len() == 2 * HPSS_HALF + 1 {
            self.recent.pop_front();
        }
        self.recent.push_back(magnitudes[..self.hpss_bins].to_vec());
        // A frame with `HPSS_HALF` frames after it has all its neighbours now.
        while self.frames.flux.len() - 1 - self.split_done >= HPSS_HALF {
            let center = self.split_done + self.recent.len() - self.frames.flux.len();
            self.split(center);
        }
    }

    /// Splits the frame at `center` of the recent window into harmonic and percussive energy (medians along time
    /// and along frequency) and scores its voice.
    fn split(&mut self, center: usize) {
        let bins = self.hpss_bins;
        let frame = &self.recent[center];
        let mut column = Vec::with_capacity(self.recent.len());
        let mut row = Vec::with_capacity(2 * HPSS_HALF + 1);
        let (mut voice_harmonic, mut voice_total, mut band_harmonic, mut band_total) = (0.0f32, 0.0f32, 0.0f32, 0.0f32);
        let (mut log_sum, mut linear_sum, mut voice_bins) = (0.0f32, 0.0f32, 0usize);
        for b in 0..bins {
            column.clear();
            column.extend(self.recent.iter().map(|f| f[b]));
            let harmonic = median(&mut column);
            row.clear();
            row.extend_from_slice(&frame[b.saturating_sub(HPSS_HALF)..(b + HPSS_HALF + 1).min(bins)]);
            let percussive = median(&mut row);
            let power = frame[b] * frame[b];
            let mask = harmonic * harmonic / (harmonic * harmonic + percussive * percussive + 1e-12);
            if (self.voice_band.0..=self.voice_band.1).contains(&b) {
                voice_harmonic += power * mask;
                voice_total += power;
                log_sum += (power + 1e-10).ln();
                linear_sum += power + 1e-10;
                voice_bins += 1;
            }
            if (self.harmonic_band.0..=self.harmonic_band.1).contains(&b) {
                band_harmonic += power * mask;
                band_total += power;
            }
        }
        let share = if voice_total > 1e-9 { voice_harmonic / voice_total } else { 0.0 };
        // Spectral flatness of the voice band: noise is flat, a voice has peaks (harmonics).
        let flatness = if voice_bins > 0 { ((log_sum / voice_bins as f32).exp() / (linear_sum / voice_bins as f32)).clamp(0.0, 1.0) } else { 1.0 };
        let loud = if band_total > 1e-9 { (voice_total / band_total).clamp(0.0, 1.0) } else { 0.0 };
        let audible = band_total > 1e-4;
        self.frames.vocal.push(if audible { (share * (1.0 - flatness) * (0.5 + loud)).clamp(0.0, 1.0) } else { 0.0 });
        self.frames.harmonic.push(if band_total > 1e-9 { band_harmonic / band_total } else { 0.0 });
        self.frames.voice_energy.push(voice_harmonic);
        self.split_done += 1;
    }

    /// Splits the frames still waiting (the last ones, with a shorter window after them).
    pub fn finish(&mut self) {
        while self.split_done < self.frames.flux.len() && !self.recent.is_empty() {
            let center = self.split_done + self.recent.len() - self.frames.flux.len();
            self.split(center);
        }
    }
}

/// A voice rises and falls with its syllables, four to eight times a second; a pad or a held synth does not. The
/// voice likelihood keeps only as much as the harmonic energy of its band moves within half a second around it.
pub fn syllabic(frames: &mut Frames, frame_seconds: f32) {
    let half = (0.25 / frame_seconds) as usize;
    let levels: Vec<f32> = frames.voice_energy.iter().map(|e| 10.0 * (e + 1e-10).log10()).collect();
    let n = levels.len().min(frames.vocal.len());
    let mut sums = vec![(0.0f64, 0.0f64); n + 1];
    for i in 0..n {
        let v = levels[i] as f64;
        sums[i + 1] = (sums[i].0 + v, sums[i].1 + v * v);
    }
    for i in 0..n {
        let (from, to) = (i.saturating_sub(half), (i + half + 1).min(n));
        let count = (to - from) as f64;
        let mean = (sums[to].0 - sums[from].0) / count;
        let deviation = ((sums[to].1 - sums[from].1) / count - mean * mean).max(0.0).sqrt() as f32;
        // Steady (under 1.5 dB of movement) is no voice; 4.5 dB or more is fully one.
        frames.vocal[i] *= ((deviation - 1.5) / 3.0).clamp(0.0, 1.0);
    }
}

fn median(values: &mut [f32]) -> f32 {
    if values.is_empty() {
        return 0.0;
    }
    let middle = values.len() / 2;
    *values.select_nth_unstable_by(middle, |a, b| a.total_cmp(b)).1
}

/// Triangular mel filters: for each band, the bins it covers and their weights.
fn mel_filters(rate: f32, bands: usize, low: f32, high: f32) -> Vec<Vec<(usize, f32)>> {
    let mel = |hz: f32| 2595.0 * (1.0 + hz / 700.0).log10();
    let hz = |mel: f32| 700.0 * (10f32.powf(mel / 2595.0) - 1.0);
    let bin_hz = rate / FFT_SIZE as f32;
    let points: Vec<f32> = (0..bands + 2).map(|i| hz(mel(low) + (mel(high) - mel(low)) * i as f32 / (bands + 1) as f32)).collect();
    (0..bands)
        .map(|band| {
            let (left, center, right) = (points[band], points[band + 1], points[band + 2]);
            ((left / bin_hz).floor() as usize..=((right / bin_hz).ceil() as usize).min(FFT_SIZE / 2))
                .filter_map(|bin| {
                    let f = bin as f32 * bin_hz;
                    let weight = if f < center { (f - left) / (center - left) } else { (right - f) / (right - center) };
                    (weight > 0.0).then_some((bin, weight))
                })
                .collect()
        })
        .collect()
}
