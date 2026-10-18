//! Converting between frames, fine energy blocks and seconds, and finding where an attack really starts.

use super::features::{FFT_SIZE, HOP};

pub struct Timing {
    pub rate: f32,
    pub frame_seconds: f32,
    pub micro_seconds: f32,
    /// Mean square of each fine block (about 1.5 ms).
    pub micro: Vec<f32>,
}

impl Timing {
    /// When the onset a frame's flux peak shows began. The flux of a Hann-windowed frame rises fastest when the new
    /// sound is three quarters of a window ahead of the frame's start, a quarter after its centre.
    pub fn frame_time(&self, frame: usize) -> f32 {
        (frame * HOP + FFT_SIZE * 3 / 4) as f32 / self.rate
    }

    /// The frame whose window is centred nearest to a time.
    pub fn frame_of(&self, seconds: f32) -> usize {
        ((seconds * self.rate - FFT_SIZE as f32 / 2.0) / HOP as f32).round().max(0.0) as usize
    }

    pub fn duration(&self) -> f32 {
        self.micro.len() as f32 * self.micro_seconds
    }

    /// Moves an estimated onset to the attack near it: the fine block, within 25 ms before and 15 ms after, where
    /// the energy jumps the most over the blocks before it. Without a clear jump it stays where it was.
    pub fn attack(&self, estimate: f32) -> f32 {
        let block = |t: f32| (t / self.micro_seconds).round().max(0.0) as usize;
        let (from, to) = (block(estimate - 0.025).max(4), block(estimate + 0.015).min(self.micro.len()));
        let mut best = None;
        for b in from..to {
            let before = self.micro[b - 4..b].iter().sum::<f32>() / 4.0;
            let rise = (self.micro[b] + 1e-10).ln() - (before + 1e-10).ln();
            if rise > std::f32::consts::LN_2 && best.is_none_or(|(_, r)| rise > r) {
                best = Some((b, rise));
            }
        }
        best.map_or(estimate, |(b, _)| b as f32 * self.micro_seconds)
    }

    /// Mean square over a span of seconds.
    pub fn power(&self, from: f32, to: f32) -> f32 {
        let (a, b) = ((from / self.micro_seconds).max(0.0) as usize, ((to / self.micro_seconds) as usize).min(self.micro.len()));
        if b <= a {
            return 0.0;
        }
        self.micro[a..b].iter().sum::<f32>() / (b - a) as f32
    }
}

/// Decibels relative to full scale of a mean square.
pub fn db(power: f32) -> f32 {
    10.0 * (power + 1e-12).log10()
}
