//! Events along the track: silences, fades, onsets, the energy and voice curves, and the points worth scratching.

use super::timing::{Timing, db};

/// Below this a 50 ms window is silent.
const SILENCE_DB: f32 = -50.0;
const MIN_SILENCE: f32 = 0.3;

pub struct Silence {
    pub start: f32,
    pub end: f32,
    pub kind: &'static str,
}

/// Silences of at least 300 ms: 10 ms blocks whose 50 ms neighbourhood stays below −50 dBFS.
pub fn silences(t: &Timing) -> Vec<Silence> {
    let duration = t.duration();
    let step = 0.01;
    let blocks = (duration / step) as usize;
    let level: Vec<f32> = (0..blocks).map(|i| db(t.power(i as f32 * step, (i + 1) as f32 * step))).collect();
    let silent: Vec<bool> = (0..blocks)
        .map(|i| {
            let (from, to) = (i.saturating_sub(2), (i + 3).min(blocks));
            level[i] < SILENCE_DB && db(t.power(from as f32 * step, to as f32 * step)) < SILENCE_DB
        })
        .collect();

    let mut out = Vec::new();
    let mut i = 0;
    while i < blocks {
        if !silent[i] {
            i += 1;
            continue;
        }
        let start = i;
        while i < blocks && silent[i] {
            i += 1;
        }
        let (from, to) = (start as f32 * step, if i == blocks { duration } else { i as f32 * step });
        if to - from >= MIN_SILENCE {
            let kind = if from < 0.05 { "leading" } else if to > duration - 0.05 { "trailing" } else { "gap" };
            out.push(Silence { start: from, end: to, kind });
        }
    }
    out
}

/// Where the music starts and ends: after a leading silence and before a trailing one.
pub fn music_range(silences: &[Silence], duration: f32) -> (f32, f32) {
    let start = silences.iter().find(|s| s.kind == "leading").map_or(0.0, |s| s.end);
    let end = silences.iter().find(|s| s.kind == "trailing").map_or(duration, |s| s.start);
    if end > start { (start, end) } else { (0.0, duration) }
}

/// Loudness every `step` seconds, in dBFS.
pub fn energy_db(t: &Timing, step: f32) -> Vec<f32> {
    let count = (t.duration() / step).ceil() as usize;
    (0..count).map(|i| db(t.power(i as f32 * step, (i + 1) as f32 * step))).collect()
}

/// A dBFS level as a byte: −60 dBFS and below is 0, full scale 255.
pub fn level_byte(db: f32) -> u8 {
    (((db + 60.0) / 60.0).clamp(0.0, 1.0) * 255.0).round() as u8
}

/// The voice likelihood averaged every `step` seconds.
pub fn vocal_curve(t: &Timing, vocal: &[f32], step: f32) -> Vec<f32> {
    let count = (t.duration() / step).ceil() as usize;
    (0..count)
        .map(|i| {
            let (from, to) = (t.frame_of(i as f32 * step), t.frame_of((i + 1) as f32 * step).min(vocal.len()));
            if to > from { vocal[from..to].iter().sum::<f32>() / (to - from) as f32 } else { 0.0 }
        })
        .collect()
}

/// Where a fade in ends and a fade out starts: the music rises to (or falls from) its body level, at least 12 dB
/// away, over more than a second. `None` when it starts or ends at full level.
pub fn fades(energy: &[f32], step: f32, music: (f32, f32)) -> (Option<f32>, Option<f32>) {
    let index = |t: f32| ((t / step) as usize).min(energy.len().saturating_sub(1));
    let (first, last) = (index(music.0), index(music.1));
    if last <= first + 20 {
        return (None, None);
    }
    let mut body: Vec<f32> = energy[first..=last].iter().copied().filter(|e| *e > -60.0).collect();
    if body.is_empty() {
        return (None, None);
    }
    body.sort_by(|a, b| a.total_cmp(b));
    let level = body[body.len() / 2];
    let edge = |from: usize, to: usize| energy[from.min(to)..=to.max(from)].iter().sum::<f32>() / (to.abs_diff(from) + 1) as f32;

    let fade_in = (first..=last.min(first + (20.0 / step) as usize))
        .find(|&i| energy[i] >= level - 3.0)
        .filter(|&i| (i - first) as f32 * step >= 1.0 && edge(first, first + 2) <= level - 12.0)
        .map(|i| i as f32 * step);
    let fade_out = (last.saturating_sub((30.0 / step) as usize).max(first)..=last)
        .rev()
        .find(|&i| energy[i] >= level - 3.0)
        .filter(|&i| (last - i) as f32 * step >= 2.0 && edge(last.saturating_sub(2), last) <= level - 12.0)
        .map(|i| i as f32 * step);
    (fade_in, fade_out)
}

pub struct Onset {
    pub time: f32,
    /// 0–1, relative to the track's strongest.
    pub strength: f32,
}

/// Onsets: peaks of the flux above its local median, at least 50 ms apart, each moved to its attack.
pub fn onsets(t: &Timing, flux: &[f32]) -> Vec<Onset> {
    let n = flux.len();
    let mut sorted = flux.to_vec();
    sorted.sort_by(|a, b| a.total_cmp(b));
    let typical = sorted.get(n * 9 / 10).copied().unwrap_or(0.0);
    let min_gap = (0.05 / t.frame_seconds).ceil() as usize;
    let mut window = Vec::with_capacity(17);
    let mut peaks: Vec<(usize, f32)> = Vec::new();
    for k in 1..n {
        let (from, to) = (k.saturating_sub(8), (k + 9).min(n));
        window.clear();
        window.extend_from_slice(&flux[from..to]);
        let middle = window.len() / 2;
        let median = *window.select_nth_unstable_by(middle, |a, b| a.total_cmp(b)).1;
        let excess = flux[k] - median;
        let local_max = flux[k.saturating_sub(3)..(k + 4).min(n)].iter().all(|v| *v <= flux[k]);
        if local_max && excess > 0.15 * typical && excess > 0.0 {
            match peaks.last_mut() {
                Some(last) if k - last.0 < min_gap => {
                    if excess > last.1 {
                        *last = (k, excess);
                    }
                }
                _ => peaks.push((k, excess)),
            }
        }
    }
    let mut strengths: Vec<f32> = peaks.iter().map(|p| p.1).collect();
    strengths.sort_by(|a, b| a.total_cmp(b));
    let top = strengths.get(strengths.len().saturating_sub(1) * 99 / 100).copied().unwrap_or(1.0).max(f32::EPSILON);
    peaks
        .into_iter()
        .map(|(k, excess)| Onset { time: t.attack(t.frame_time(k)), strength: (excess / top).min(1.0) })
        .collect()
}

pub struct ScratchCue {
    pub time: f32,
    pub duration: f32,
    pub kind: &'static str,
    pub isolation: f32,
    pub strength: f32,
    pub score: f32,
}

/// Points worth scratching: a strong attack after at least 80 ms of something quieter, followed by 120–450 ms of
/// sustained sound (a vocal syllable, a stab). Voice and the highlight are preferred; the best 12, best first.
pub fn scratch_cues(t: &Timing, onsets: &[Onset], vocal: &[f32], harmonic: &[f32], highlight: Option<(f32, f32)>) -> Vec<ScratchCue> {
    let mut cues: Vec<ScratchCue> = Vec::new();
    for (i, onset) in onsets.iter().enumerate() {
        if onset.strength < 0.3 {
            continue;
        }
        let at = onset.time;
        let before = t.power(at - 0.08, at);
        let after = t.power(at, at + 0.12);
        if after <= 1e-9 {
            continue;
        }
        let isolation = (1.0 - before / after).clamp(0.0, 1.0);
        if isolation < 0.5 {
            continue;
        }

        // How long it lasts: until it falls 12 dB below its start, the next onset, or 450 ms.
        let next = onsets.get(i + 1).map_or(f32::MAX, |o| o.time);
        let level = db(t.power(at, at + 0.03));
        let mut end = at + 0.03;
        while end < at + 0.45 && end < next && db(t.power(end, end + 0.01)) > level - 12.0 {
            end += 0.01;
        }
        let duration = end - at;
        if duration < 0.12 {
            continue;
        }

        let (from, to) = (t.frame_of(at).min(vocal.len()), t.frame_of(end).min(vocal.len()).min(harmonic.len()));
        let mean = |values: &[f32]| if to > from { values[from..to].iter().sum::<f32>() / (to - from) as f32 } else { 0.0 };
        let (voice, sustain) = (mean(vocal), mean(harmonic));
        let kind = if voice >= 0.35 { "vocal" } else if sustain >= 0.5 { "stab" } else { "drum" };
        let inside = highlight.is_some_and(|(a, b)| at >= a && at < b);
        let score = 0.35 * onset.strength
            + 0.25 * isolation
            + 0.2 * voice.min(1.0)
            + 0.1 * if inside { 1.0 } else { 0.0 }
            + 0.1 * ((duration - 0.12) / 0.33).clamp(0.0, 1.0);
        cues.push(ScratchCue { time: at, duration, kind, isolation, strength: onset.strength, score });
    }
    cues.sort_by(|a, b| b.score.total_cmp(&a.score).then(a.time.total_cmp(&b.time)));
    cues.truncate(12);
    cues
}
