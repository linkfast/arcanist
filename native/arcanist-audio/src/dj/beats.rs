//! Tempo, beats and downbeats. The tempo comes from the autocorrelation of the onset envelope (with a preference
//! for danceable tempos), the beats from dynamic programming over that envelope (Ellis, "Beat Tracking by Dynamic
//! Programming", 2007), each moved to the attack it belongs to, and the downbeats from where harmony changes and
//! the bass hits.

use super::timing::Timing;

const MIN_BPM: f32 = 60.0;
const MAX_BPM: f32 = 200.0;
/// How strongly beats keep to the tempo: Ellis' "tightness".
const TIGHTNESS: f32 = 100.0;
/// A tempo is stable when no stretch of the track strays from it by more than this.
const STABLE_DEVIATION: f32 = 0.01;

pub struct Grid {
    pub bpm: f32,
    pub confidence: f32,
    pub stable: bool,
    pub beats_per_bar: usize,
    /// Every beat, in seconds (generated from the tempo when it is stable).
    pub beats: Vec<f32>,
    /// Indexes into `beats` of the first beat of each bar.
    pub downbeats: Vec<usize>,
}

impl Grid {
    pub fn downbeat_times(&self) -> Vec<f32> {
        self.downbeats.iter().map(|&i| self.beats[i]).collect()
    }
}

/// The onset envelope for tempo work: the flux minus its local mean (half a second each side), normalized.
pub fn envelope(flux: &[f32], frame_seconds: f32) -> Vec<f32> {
    let half = (0.5 / frame_seconds) as usize;
    let mut sums = vec![0.0f64; flux.len() + 1];
    for (i, value) in flux.iter().enumerate() {
        sums[i + 1] = sums[i] + *value as f64;
    }
    let mut out: Vec<f32> = (0..flux.len())
        .map(|i| {
            let (from, to) = (i.saturating_sub(half), (i + half + 1).min(flux.len()));
            (flux[i] - ((sums[to] - sums[from]) / (to - from) as f64) as f32).max(0.0)
        })
        .collect();
    let deviation = (out.iter().map(|v| v * v).sum::<f32>() / out.len().max(1) as f32).sqrt();
    if deviation > f32::EPSILON {
        out.iter_mut().for_each(|v| *v /= deviation);
    }
    out
}

/// The beat period in frames and how clearly it stands out, or `None` without a steady pulse.
pub fn period(envelope: &[f32], frame_seconds: f32) -> Option<(f32, f32)> {
    if envelope.len() as f32 * frame_seconds < 8.0 {
        return None;
    }
    let energy: f32 = envelope.iter().map(|v| v * v).sum();
    if energy <= f32::EPSILON {
        return None;
    }
    let lag_of = |bpm: f32| 60.0 / (bpm * frame_seconds);
    let min_lag = lag_of(MAX_BPM * 2.0).floor() as usize;
    let max_lag = (lag_of(MIN_BPM / 2.0).ceil() as usize).min(envelope.len() / 2);
    let correlation: Vec<f32> = (0..=max_lag + 1)
        .map(|lag| {
            if lag < min_lag.saturating_sub(1) || lag == 0 {
                return 0.0;
            }
            envelope.iter().zip(&envelope[lag..]).map(|(a, b)| a * b).sum::<f32>() / energy
        })
        .collect();

    // The strongest periodicity, mildly preferring tempos near 120 BPM.
    let (from, to) = (lag_of(MAX_BPM).floor() as usize, (lag_of(MIN_BPM).ceil() as usize).min(max_lag));
    let mut best = None;
    for lag in from.max(1)..=to {
        if correlation[lag] <= correlation[lag - 1] || correlation[lag] < correlation[lag + 1] {
            continue;
        }
        let bpm = 60.0 / (lag as f32 * frame_seconds);
        let weight = (-0.5 * ((bpm / 120.0).log2() / 0.9).powi(2)).exp();
        let score = correlation[lag] * weight;
        if best.is_none_or(|(_, s)| score > s) {
            best = Some((lag, score));
        }
    }
    let (mut lag, _) = best?;

    // Half or double time: settle within 70–180 BPM, towards what one dances to.
    let peak_near = |target: f32| {
        let center = target.round() as usize;
        (center.saturating_sub(1)..=(center + 1).min(max_lag)).max_by(|a, b| correlation[*a].total_cmp(&correlation[*b])).unwrap_or(center)
    };
    let bpm = 60.0 / (lag as f32 * frame_seconds);
    // As strong at double the tempo (every beat equally loud, as in drum and bass or a click track): the faster.
    let half = peak_near(lag as f32 / 2.0);
    if half >= min_lag && 60.0 / (half as f32 * frame_seconds) <= 180.0 && correlation[half] >= 0.95 * correlation[lag] {
        lag = half;
    } else if bpm < 70.0 {
        let half = peak_near(lag as f32 / 2.0);
        if 60.0 / (half as f32 * frame_seconds) <= 180.0 && correlation[half] >= 0.5 * correlation[lag] {
            lag = half;
        }
    } else if bpm > 180.0 {
        let double = peak_near(lag as f32 * 2.0);
        if double <= max_lag && 60.0 / (double as f32 * frame_seconds) >= 70.0 && correlation[double] >= 0.5 * correlation[lag] {
            lag = double;
        }
    }

    // Parabolic interpolation for a period finer than a frame.
    let (left, center, right) = (correlation[lag - 1], correlation[lag], correlation[lag + 1]);
    let denominator = left - 2.0 * center + right;
    let offset = if denominator.abs() > f32::EPSILON { (0.5 * (left - right) / denominator).clamp(-0.5, 0.5) } else { 0.0 };
    let mean = correlation[from.max(1)..=to].iter().sum::<f32>() / (to + 1 - from.max(1)) as f32;
    let confidence = ((center - mean) / (1.0 - mean).max(f32::EPSILON)).clamp(0.0, 1.0);
    Some((lag as f32 + offset, confidence))
}

/// Beat frames by dynamic programming: each beat is where an onset is, about one period after the previous one.
pub fn track(envelope: &[f32], period: f32) -> Vec<usize> {
    let n = envelope.len();
    if n == 0 || period < 2.0 {
        return Vec::new();
    }
    let mut score = vec![0.0f32; n];
    let mut back = vec![usize::MAX; n];
    let (near, far) = ((period / 2.0).round() as usize, (period * 2.0).round() as usize);
    for t in 0..n {
        let mut best = 0.0f32;
        let mut from = usize::MAX;
        for gap in near.max(1)..=far {
            if gap > t {
                break;
            }
            let previous = t - gap;
            let penalty = (gap as f32 / period).ln();
            let candidate = score[previous] - TIGHTNESS * penalty * penalty;
            if from == usize::MAX || candidate > best {
                best = candidate;
                from = previous;
            }
        }
        if from != usize::MAX && best > 0.0 {
            score[t] = envelope[t] + best;
            back[t] = from;
        } else {
            score[t] = envelope[t];
        }
    }

    // The best beat within the last period, then back through the chain.
    let tail = n.saturating_sub(period.ceil() as usize);
    let mut beat = (tail..n).max_by(|a, b| score[*a].total_cmp(&score[*b])).unwrap_or(n - 1);
    let mut beats = vec![beat];
    while back[beat] != usize::MAX {
        beat = back[beat];
        beats.push(beat);
    }
    beats.reverse();
    // Beats before the music has a pulse and after it stops (the chain starts and ends anywhere): drop those on no
    // onset.
    let threshold = 0.1 * beats.iter().map(|&b| envelope[b]).sum::<f32>() / beats.len() as f32;
    let first = beats.iter().position(|&b| envelope[b] > threshold).unwrap_or(0);
    let last = beats.iter().rposition(|&b| envelope[b] > threshold).map_or(beats.len(), |i| i + 1);
    beats.truncate(last);
    beats.drain(..first.min(beats.len()));
    beats
}

/// The grid: tempo, beats (refined to their attacks) and downbeats.
pub fn grid(timing: &Timing, flux: &[f32], bass: &[f32], chroma: &[[f32; 12]], music: (f32, f32)) -> Option<Grid> {
    let env = envelope(flux, timing.frame_seconds);
    let (period_frames, confidence) = period(&env, timing.frame_seconds)?;
    let frames = track(&env, period_frames);
    if frames.len() < 8 {
        return None;
    }
    let times: Vec<f32> = frames.iter().map(|&f| timing.attack(timing.frame_time(f))).collect();

    // A line through the beats: its slope is the period. Beats far from it (a missed or extra beat) are left out
    // of a second fit.
    let mut fit = line(&times, None);
    let kept: Vec<bool> = times.iter().enumerate().map(|(i, t)| (t - (fit.0 + fit.1 * i as f32)).abs() < fit.1 * 0.25).collect();
    if kept.iter().filter(|k| **k).count() >= 8 {
        fit = line(&times, Some(&kept));
    }
    let (start, step) = fit;
    // Stable: the stretches of 16 beats keep the same period (within 1%), all but an intro or a breakdown without
    // a clear pulse (a tenth of them).
    let windows: Vec<bool> = if times.len() >= 16 {
        (0..=times.len() - 16).step_by(8).map(|from| ((line(&times[from..from + 16], None).1 - step) / step).abs() < STABLE_DEVIATION).collect()
    } else {
        Vec::new()
    };
    let stable = !windows.is_empty() && windows.iter().filter(|w| **w).count() as f32 >= windows.len() as f32 * 0.9;

    let (beats, bpm) = if stable && step > 0.0 {
        // The grid, from the first beat at or after the music starts (the fit may begin later, in an intro).
        let mut first = start;
        while first - step >= music.0 - 0.05 {
            first -= step;
        }
        while first < music.0 - 0.05 {
            first += step;
        }
        let count = ((music.1 - first) / step).floor().max(0.0) as usize + 1;
        ((0..count).map(|i| first + step * i as f32).collect::<Vec<f32>>(), 60.0 / step)
    } else {
        let mut intervals: Vec<f32> = times.windows(2).map(|w| w[1] - w[0]).collect();
        intervals.sort_by(|a, b| a.total_cmp(b));
        let median = intervals[intervals.len() / 2];
        (times, 60.0 / median)
    };

    let (beats_per_bar, phase) = downbeat_phase(timing, &beats, bass, chroma);
    let downbeats = (phase..beats.len()).step_by(beats_per_bar).collect();
    Some(Grid { bpm, confidence, stable, beats_per_bar, beats, downbeats })
}

/// Least-squares line `t = a + b·i` through the beats (or the kept ones).
fn line(times: &[f32], kept: Option<&[bool]>) -> (f32, f32) {
    let points: Vec<(f64, f64)> = times
        .iter()
        .enumerate()
        .filter(|(i, _)| kept.is_none_or(|k| k[*i]))
        .map(|(i, t)| (i as f64, *t as f64))
        .collect();
    let n = points.len() as f64;
    let (sx, sy) = points.iter().fold((0.0, 0.0), |(x, y), p| (x + p.0, y + p.1));
    let (mx, my) = (sx / n, sy / n);
    let (sxy, sxx) = points.iter().fold((0.0, 0.0), |(a, b), p| (a + (p.0 - mx) * (p.1 - my), b + (p.0 - mx) * (p.0 - mx)));
    let slope = if sxx > 0.0 { sxy / sxx } else { 0.0 };
    ((my - slope * mx) as f32, slope as f32)
}

/// Beats per bar (4, or 3 when it clearly fits better) and which beat of the first bar is the downbeat: the one
/// where, bar after bar, harmony changes and the bass hits.
fn downbeat_phase(timing: &Timing, beats: &[f32], bass: &[f32], chroma: &[[f32; 12]]) -> (usize, usize) {
    if beats.len() < 8 {
        return (4, 0);
    }
    let frame = |t: f32| timing.frame_of(t).min(bass.len().saturating_sub(1));
    // Bass at each beat, and how much the harmony changes there (the mean chroma of the beat before and after).
    let span = |a: f32, b: f32| -> [f32; 12] {
        let (from, to) = (frame(a), frame(b).max(frame(a) + 1).min(chroma.len()));
        let mut sum = [0.0f32; 12];
        for c in &chroma[from.min(to)..to] {
            sum.iter_mut().zip(c).for_each(|(s, v)| *s += v);
        }
        normalize(sum)
    };
    let step = (beats[beats.len() - 1] - beats[0]) / (beats.len() - 1) as f32;
    let mut accent = Vec::with_capacity(beats.len());
    for (i, &t) in beats.iter().enumerate() {
        let b = frame(t);
        let low = bass[b.saturating_sub(2)..(b + 3).min(bass.len())].iter().cloned().fold(0.0f32, f32::max);
        let before = span(if i > 0 { beats[i - 1] } else { t - step }, t);
        let after = span(t, beats.get(i + 1).copied().unwrap_or(t + step));
        let change = 1.0 - before.iter().zip(&after).map(|(a, b)| a * b).sum::<f32>();
        accent.push((low, change));
    }
    let (bass_max, change_max) = accent.iter().fold((0.0f32, 0.0f32), |(a, b), (l, c)| (a.max(*l), b.max(*c)));
    let score: Vec<f32> = accent.iter().map(|(l, c)| l / bass_max.max(f32::EPSILON) + c / change_max.max(f32::EPSILON)).collect();

    let phases = |per_bar: usize| -> (usize, f32) {
        let means: Vec<f32> = (0..per_bar)
            .map(|phase| {
                let values: Vec<f32> = score.iter().skip(phase).step_by(per_bar).copied().collect();
                values.iter().sum::<f32>() / values.len().max(1) as f32
            })
            .collect();
        let average = means.iter().sum::<f32>() / per_bar as f32;
        let (best, value) = means.iter().enumerate().max_by(|a, b| a.1.total_cmp(b.1)).map(|(i, v)| (i, *v)).unwrap_or((0, 0.0));
        (best, value - average)
    };
    let (four, four_contrast) = phases(4);
    let (three, three_contrast) = phases(3);
    if three_contrast > four_contrast * 1.3 { (3, three) } else { (4, four) }
}

fn normalize(v: [f32; 12]) -> [f32; 12] {
    let norm = v.iter().map(|x| x * x).sum::<f32>().sqrt();
    if norm <= f32::EPSILON { v } else { v.map(|x| x / norm) }
}
