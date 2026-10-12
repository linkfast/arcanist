//! Tempo estimation: spectral-flux onset envelope, autocorrelation, and a prior around 120 BPM that settles
//! octave ambiguities (a 70 BPM ballad also fits 140) the way listeners usually do.

use crate::dsp::Stft;

const MIN_BPM: f32 = 50.0;
const MAX_BPM: f32 = 220.0;

#[derive(Debug, Clone, Copy, serde::Serialize)]
pub struct Tempo {
    pub bpm: f32,
    /// 0–1: how much the winning periodicity stands out. Low values mean no steady beat (speech, ambient).
    pub confidence: f32,
}

pub fn estimate(signal: &[f32], rate: f32) -> Option<Tempo> {
    let stft = Stft::new(1024, 128);
    let frame_rate = rate / 128.0;

    // Spectral flux on log-compressed magnitudes: how much energy appears from one frame to the next.
    let mut previous: Option<Vec<f32>> = None;
    let mut envelope = Vec::new();
    stft.magnitudes(signal, |magnitudes| {
        let current: Vec<f32> = magnitudes.iter().map(|m| (1.0 + 100.0 * m).ln()).collect();
        if let Some(prev) = &previous {
            envelope.push(current.iter().zip(prev).map(|(c, p)| (c - p).max(0.0)).sum::<f32>());
        }
        previous = Some(current);
    });

    // At least ~8 seconds of audio.
    if envelope.len() < (frame_rate * 8.0) as usize {
        return None;
    }

    // Remove the slowly varying level so only the pulses remain.
    let half = (frame_rate * 0.5) as usize;
    let mut sums = vec![0.0f32; envelope.len() + 1];
    for (i, value) in envelope.iter().enumerate() {
        sums[i + 1] = sums[i] + value;
    }
    let onsets: Vec<f32> = (0..envelope.len())
        .map(|i| {
            let (from, to) = (i.saturating_sub(half), (i + half + 1).min(envelope.len()));
            (envelope[i] - (sums[to] - sums[from]) / (to - from) as f32).max(0.0)
        })
        .collect();

    let energy: f32 = onsets.iter().map(|v| v * v).sum();
    if energy <= f32::EPSILON {
        return None;
    }

    let min_lag = (60.0 * frame_rate / MAX_BPM).floor() as usize;
    let max_lag = ((60.0 * frame_rate / MIN_BPM).ceil() as usize).min(onsets.len() / 2);
    let correlation: Vec<f32> = (0..=max_lag + 1)
        .map(|lag| {
            if lag < min_lag.saturating_sub(1) {
                return 0.0;
            }
            onsets.iter().zip(&onsets[lag..]).map(|(a, b)| a * b).sum::<f32>() / energy
        })
        .collect();

    // Weight each lag by a log-normal prior centred on 120 BPM, one octave wide.
    let score = |lag: usize| {
        let bpm = 60.0 * frame_rate / lag as f32;
        let octaves = (bpm / 120.0).log2();
        correlation[lag] * (-0.5 * octaves * octaves).exp()
    };
    let best = (min_lag.max(1)..=max_lag).max_by(|a, b| score(*a).total_cmp(&score(*b)))?;

    // Parabolic interpolation around the peak for sub-frame precision.
    let refined = if best > min_lag && best < max_lag {
        let (a, b, c) = (correlation[best - 1], correlation[best], correlation[best + 1]);
        let denominator = a - 2.0 * b + c;
        if denominator.abs() > f32::EPSILON { best as f32 + 0.5 * (a - c) / denominator } else { best as f32 }
    } else {
        best as f32
    };

    let bpm = 60.0 * frame_rate / refined;
    let mean = correlation[min_lag..=max_lag].iter().sum::<f32>() / (max_lag - min_lag + 1) as f32;
    let confidence = ((correlation[best] - mean) / (1.0 - mean).max(f32::EPSILON)).clamp(0.0, 1.0);

    Some(Tempo { bpm: (bpm * 10.0).round() / 10.0, confidence })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::dsp::ANALYSIS_RATE;

    /// Short noise bursts at the given tempo, like a hi-hat.
    fn clicks(bpm: f32, seconds: f32) -> Vec<f32> {
        let rate = ANALYSIS_RATE;
        let period = (60.0 / bpm * rate) as usize;
        let mut seed = 1u32;
        (0..(seconds * rate) as usize)
            .map(|i| {
                seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                let noise = (seed >> 8) as f32 / (1u32 << 24) as f32 - 0.5;
                let offset = i % period;
                if offset < 200 { noise * (1.0 - offset as f32 / 200.0) } else { 0.0 }
            })
            .collect()
    }

    #[test]
    fn finds_steady_tempos() {
        for bpm in [90.0, 120.0, 128.0, 174.0] {
            let tempo = estimate(&clicks(bpm, 30.0), ANALYSIS_RATE).unwrap();
            assert!((tempo.bpm - bpm).abs() < 1.0, "expected {bpm}, got {}", tempo.bpm);
            assert!(tempo.confidence > 0.3, "confidence {}", tempo.confidence);
        }
    }

    #[test]
    fn silence_has_no_tempo() {
        assert!(estimate(&vec![0.0; ANALYSIS_RATE as usize * 20], ANALYSIS_RATE).is_none());
    }
}
