//! Key estimation: an average chromagram correlated with the Krumhansl–Kessler key profiles.

use crate::dsp::Stft;

/// Krumhansl–Kessler probe-tone ratings, starting at the tonic.
const MAJOR: [f32; 12] = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
const MINOR: [f32; 12] = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

#[derive(Debug, Clone, Copy, serde::Serialize)]
pub struct Key {
    /// Pitch class of the tonic: 0 = C, 1 = C♯/D♭ … 11 = B.
    pub tonic: u8,
    pub minor: bool,
    /// 0–1: margin over the next best key. Low values mean an ambiguous or atonal piece.
    pub confidence: f32,
}

pub fn estimate(signal: &[f32], rate: f32) -> Option<Key> {
    let stft = Stft::new(8192, 4096);
    let bin_hz = rate / stft.size() as f32;

    // Bins from C2 (65 Hz) to C7 (2093 Hz), each assigned to its nearest pitch class.
    let classes: Vec<(usize, usize)> = (1..stft.size() / 2)
        .filter_map(|bin| {
            let frequency = bin as f32 * bin_hz;
            (65.0..=2093.0).contains(&frequency).then(|| {
                let midi = 69.0 + 12.0 * (frequency / 440.0).log2();
                (bin, (midi.round() as i32).rem_euclid(12) as usize)
            })
        })
        .collect();

    let mut chroma = [0.0f32; 12];
    stft.magnitudes(signal, |magnitudes| {
        let mut frame = [0.0f32; 12];
        for &(bin, class) in &classes {
            frame[class] += magnitudes[bin];
        }
        // Every frame counts the same, so a few loud passages do not decide the key on their own.
        let max = frame.iter().cloned().fold(0.0, f32::max);
        if max > 1e-3 {
            chroma.iter_mut().zip(frame).for_each(|(total, value)| *total += value / max);
        }
    });

    if chroma.iter().sum::<f32>() <= f32::EPSILON {
        return None;
    }

    let mut scores: Vec<(f32, u8, bool)> = (0..12u8)
        .flat_map(|tonic| [false, true].map(|minor| {
            let profile = if minor { &MINOR } else { &MAJOR };
            let rotated: Vec<f32> = (0..12).map(|i| profile[(i + 12 - tonic as usize) % 12]).collect();
            (correlation(&chroma, &rotated), tonic, minor)
        }))
        .collect();
    scores.sort_by(|a, b| b.0.total_cmp(&a.0));

    let (best, tonic, minor) = scores[0];
    let confidence = ((best - scores[1].0) * 5.0).clamp(0.0, 1.0);
    Some(Key { tonic, minor, confidence })
}

fn correlation(a: &[f32], b: &[f32]) -> f32 {
    let mean_a = a.iter().sum::<f32>() / a.len() as f32;
    let mean_b = b.iter().sum::<f32>() / b.len() as f32;
    let (mut covariance, mut variance_a, mut variance_b) = (0.0, 0.0, 0.0);
    for (x, y) in a.iter().zip(b) {
        covariance += (x - mean_a) * (y - mean_b);
        variance_a += (x - mean_a) * (x - mean_a);
        variance_b += (y - mean_b) * (y - mean_b);
    }
    covariance / (variance_a * variance_b).sqrt().max(f32::EPSILON)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::dsp::ANALYSIS_RATE;

    /// Repeats the I–IV–V–I progression of a key as sustained triads.
    fn progression(tonic_midi: i32, minor: bool) -> Vec<f32> {
        let third = if minor { 3 } else { 4 };
        let chords = [
            [0, third, 7],
            [5, 5 + if minor { 3 } else { 4 }, 12],
            [7, 11, 14], // V is major in both (harmonic minor)
            [0, third, 7],
        ];
        let chord_samples = ANALYSIS_RATE as usize;
        let mut out = Vec::new();
        for _ in 0..6 {
            for chord in chords {
                for i in 0..chord_samples {
                    let t = i as f32 / ANALYSIS_RATE;
                    out.push(chord.iter().map(|interval| {
                        let frequency = 440.0 * 2f32.powf((tonic_midi + interval - 69) as f32 / 12.0);
                        (2.0 * std::f32::consts::PI * frequency * t).sin() / 3.0
                    }).sum());
                }
            }
        }
        out
    }

    #[test]
    fn finds_major_and_minor_keys() {
        let cases = [(60, false, 0), (67, false, 7), (57, true, 9), (62, true, 2)];
        for (midi, minor, tonic) in cases {
            let key = estimate(&progression(midi, minor), ANALYSIS_RATE).unwrap();
            assert_eq!((key.tonic, key.minor), (tonic, minor), "midi {midi} minor {minor}");
        }
    }
}
