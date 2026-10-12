//! Signal-processing building blocks shared by the tempo and key estimators.

use std::sync::Arc;

use rustfft::num_complex::Complex32;
use rustfft::{Fft, FftPlanner};

/// Sample rate the tempo and key estimators work at. Enough for onsets and pitches up to ~5 kHz, and small
/// enough to keep ten minutes of audio in memory.
pub const ANALYSIS_RATE: f32 = 11025.0;

/// Downmixes interleaved frames to mono and decimates them to roughly [`ANALYSIS_RATE`] with a windowed-sinc
/// low-pass filter, so content above the new Nyquist frequency does not fold back into the pitch range.
pub struct Decimator {
    factor: usize,
    taps: Vec<f32>,
    /// The last `taps.len()` samples, written twice so that the newest window is always one contiguous slice:
    /// the filter becomes a plain dot product the compiler vectorizes.
    history: Vec<f32>,
    position: usize,
    phase: usize,
    output_rate: f32,
}

impl Decimator {
    pub fn new(input_rate: u32) -> Self {
        let factor = ((input_rate as f32 / ANALYSIS_RATE).round() as usize).max(1);
        let taps = if factor == 1 { vec![1.0] } else { lowpass(63, 0.45 / factor as f32) };
        Decimator {
            factor,
            history: vec![0.0; taps.len() * 2],
            taps,
            position: 0,
            phase: 0,
            output_rate: input_rate as f32 / factor as f32,
        }
    }

    pub fn output_rate(&self) -> f32 {
        self.output_rate
    }

    /// Feeds one mono sample; returns a decimated sample every `factor` inputs.
    #[inline]
    pub fn push(&mut self, sample: f32) -> Option<f32> {
        let len = self.taps.len();
        self.history[self.position] = sample;
        self.history[self.position + len] = sample;
        self.position = (self.position + 1) % len;
        self.phase += 1;
        if self.phase < self.factor {
            return None;
        }
        self.phase = 0;

        let window = &self.history[self.position..self.position + len];
        Some(window.iter().zip(&self.taps).map(|(sample, tap)| sample * tap).sum())
    }
}

/// Windowed-sinc low-pass filter with `cutoff` as a fraction of the sample rate.
fn lowpass(length: usize, cutoff: f32) -> Vec<f32> {
    let middle = (length - 1) as f32 / 2.0;
    let mut taps: Vec<f32> = (0..length)
        .map(|i| {
            let x = i as f32 - middle;
            let sinc = if x == 0.0 { 2.0 * cutoff } else { (2.0 * std::f32::consts::PI * cutoff * x).sin() / (std::f32::consts::PI * x) };
            let window = 0.54 - 0.46 * (2.0 * std::f32::consts::PI * i as f32 / (length - 1) as f32).cos();
            sinc * window
        })
        .collect();
    let sum: f32 = taps.iter().sum();
    taps.iter_mut().for_each(|t| *t /= sum);
    taps
}

/// Short-time Fourier transform producing magnitude spectra of Hann-windowed frames.
pub struct Stft {
    size: usize,
    hop: usize,
    window: Vec<f32>,
    fft: Arc<dyn Fft<f32>>,
}

impl Stft {
    pub fn new(size: usize, hop: usize) -> Self {
        let window = (0..size)
            .map(|i| 0.5 - 0.5 * (2.0 * std::f32::consts::PI * i as f32 / size as f32).cos())
            .collect();
        Stft { size, hop, window, fft: FftPlanner::new().plan_fft_forward(size) }
    }

    pub fn size(&self) -> usize {
        self.size
    }

    /// Calls `frame` with the magnitude spectrum (the first `size / 2 + 1` bins) of each frame.
    pub fn magnitudes(&self, signal: &[f32], mut frame: impl FnMut(&[f32])) {
        if signal.len() < self.size {
            return;
        }
        let mut buffer = vec![Complex32::new(0.0, 0.0); self.size];
        let mut scratch = vec![Complex32::new(0.0, 0.0); self.fft.get_inplace_scratch_len()];
        let mut magnitudes = vec![0.0f32; self.size / 2 + 1];

        let mut start = 0;
        while start + self.size <= signal.len() {
            for (i, value) in buffer.iter_mut().enumerate() {
                *value = Complex32::new(signal[start + i] * self.window[i], 0.0);
            }
            self.fft.process_with_scratch(&mut buffer, &mut scratch);
            for (magnitude, value) in magnitudes.iter_mut().zip(&buffer) {
                *magnitude = value.norm();
            }
            frame(&magnitudes);
            start += self.hop;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decimator_keeps_low_frequencies_and_removes_high_ones() {
        let rate = 44100;
        let amplitude = |frequency: f32| {
            let mut decimator = Decimator::new(rate);
            let output: Vec<f32> = (0..rate as usize)
                .filter_map(|i| decimator.push((2.0 * std::f32::consts::PI * frequency * i as f32 / rate as f32).sin()))
                .collect();
            output[1000..].iter().fold(0.0f32, |max, s| max.max(s.abs()))
        };

        assert!(amplitude(440.0) > 0.95);
        assert!(amplitude(9000.0) < 0.05);
    }
}
