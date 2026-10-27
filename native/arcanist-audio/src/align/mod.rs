//! Aligns the soundtrack of a music video with a track of the library: where the video plays the same
//! recording (at some offset, possibly cut, repeated or slightly faster), the track's audio can replace it;
//! elsewhere (dialogue, intros, outros, scenes between parts) the video keeps its own audio.
//!
//! Both sides are reduced to full-length Chromaprint sub-fingerprints (~0.124 s each). Every 3 s window of the
//! video is compared with every offset of the track, a Viterbi pass picks a consistent path through the best
//! offsets of each window (a chorus repeated in the track does not make it jump), runs of the path become
//! segments, their edges are refined item by item and their offsets to ~10 ms with the onset envelopes, and
//! the loudness of both sides gives the gain that makes the switches seamless.

mod features;

#[cfg(test)]
mod tests;

use serde::Serialize;

pub use features::{FeatureExtractor, Features};
use features::{ENVELOPE_RATE, LOUDNESS_RATE};

/// Bumped whenever results would change for the same audio, so the server aligns again.
pub const ALIGN_VERSION: u32 = 1;

/// Window compared at each step, in sub-fingerprints (~3 s), and the step between windows (~0.5 s).
const WINDOW: usize = 24;
const STEP: usize = 4;
/// Best offsets kept per window.
const CANDIDATES: usize = 8;
/// Offsets closer than this (in sub-fingerprints) are the same match.
const CLUSTER: i64 = 4;
/// Consecutive windows whose offsets differ by at most this still follow the same segment (allows a drift).
const SAME_OFFSET: i64 = 2;
/// Bit error rate of a window with no match: a window must do better to count as the track. Unrelated audio
/// sits near 0.5; the same recording through a different encoder well below 0.25.
const NONE_COST: f32 = 0.31;
/// Viterbi penalties for jumping to another offset and for switching between the track and the video.
const JUMP_COST: f32 = 0.5;
const SWITCH_COST: f32 = 0.15;
/// Item-level thresholds when refining the edges of a segment (smoothed fraction of differing bits).
const EDGE_MATCH: f32 = 0.30;
const EDGE_MISS: f32 = 0.38;
const MIN_SEGMENT_SECONDS: f32 = 2.0;
const MAX_GAP_SECONDS: f32 = 1.0;
/// Below this share of the video playing the track, the video keeps its own audio throughout.
const MIN_COVERAGE: f64 = 0.3;
/// Seconds of onset envelope used to refine each end of a segment, and the search around the coarse offset.
const REFINE_SECONDS: f32 = 20.0;
const REFINE_LAG_SECONDS: f32 = 0.3;
/// How far the edges of a segment may move after the coarse pass, the envelope compared at each step, and the
/// correlation that still counts as the same audio.
const EDGE_SECONDS: f32 = 4.0;
const EDGE_CONTEXT_SECONDS: f32 = 0.6;
const EDGE_CORRELATION: f32 = 0.5;
/// Envelope frames of slack either way when comparing the onsets at an edge.
const EDGE_SLACK: i64 = 3;
/// Rates closer to 1 than this are treated as no drift.
const MIN_DRIFT: f64 = 0.0003;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Source {
    Track,
    Video,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Segment {
    pub video_start_ms: u64,
    pub video_end_ms: u64,
    pub source: Source,
    /// Where the track is at `video_start_ms` (track segments).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub track_start_ms: Option<u64>,
    /// Track seconds per video second (track segments): 1 unless the video runs slightly faster or slower.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rate: Option<f64>,
    /// 0 unrelated, 1 identical (track segments).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub similarity: Option<f64>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Alignment {
    pub align_version: u32,
    pub video_duration_ms: u64,
    pub track_duration_ms: u64,
    /// Whether enough of the video plays the track to use it.
    pub matched: bool,
    /// Share of the video that plays the track.
    pub coverage: f64,
    /// Gain for the video's own audio so it sounds as loud as the track around the switches.
    pub video_gain_db: f64,
    /// Cover the whole video, in order.
    pub segments: Vec<Segment>,
}

enum Side {
    Empty,
    Extracting(Box<FeatureExtractor>),
    Done(Box<Features>),
}

/// Collects both sides (0: the video, 1: the track) and aligns them.
pub struct Aligner {
    sides: [Side; 2],
}

impl Default for Aligner {
    fn default() -> Self {
        Self::new()
    }
}

impl Aligner {
    pub fn new() -> Self {
        Aligner { sides: [Side::Empty, Side::Empty] }
    }

    pub fn begin(&mut self, side: usize, rate: u32, channels: u32) -> Result<(), String> {
        let slot = self.sides.get_mut(side).ok_or("invalid side")?;
        *slot = Side::Extracting(Box::new(FeatureExtractor::new(rate, channels)?));
        Ok(())
    }

    pub fn push(&mut self, side: usize, samples: &[f32]) -> Result<(), String> {
        match self.sides.get_mut(side) {
            Some(Side::Extracting(extractor)) => extractor.push(samples),
            _ => Err("the side was not begun".into()),
        }
    }

    pub fn set(&mut self, side: usize, features: Features) -> Result<(), String> {
        let slot = self.sides.get_mut(side).ok_or("invalid side")?;
        *slot = Side::Done(Box::new(features));
        Ok(())
    }

    pub fn finish(self) -> Result<Alignment, String> {
        let [video, track] = self.sides;
        let video = done(video, "video")?;
        let track = done(track, "track")?;
        Ok(align(&video, &track))
    }
}

fn done(side: Side, name: &str) -> Result<Features, String> {
    match side {
        Side::Done(features) => Ok(*features),
        Side::Extracting(extractor) => extractor.finish().map_err(|e| format!("{name}: {e}")),
        Side::Empty => Err(format!("{name}: no audio")),
    }
}

/// A run of the coarse path: items `[start, end)` of the video, with the offsets (track item − video item) at
/// its first and last windows.
#[derive(Debug, Clone)]
struct Run {
    start: usize,
    end: usize,
    first: i64,
    last: i64,
}

pub fn align(video: &Features, track: &Features) -> Alignment {
    let item = video.item_seconds;
    let runs = coarse_runs(&video.prints, &track.prints, item);
    let runs = refine_edges(runs, &video.prints, &track.prints);

    let video_seconds = video.duration_ms as f32 / 1000.0;
    let track_seconds = track.duration_ms as f32 / 1000.0;
    let mut segments = Vec::new();
    for (r, run) in runs.iter().enumerate() {
        let start = run.start as f32 * item;
        let end = (run.end as f32 * item).min(video_seconds);
        if end - start < MIN_SEGMENT_SECONDS {
            continue;
        }
        let (offset, rate) = fine_offset(video, track, start, end, run.first as f32 * item, run.last as f32 * item);
        let track_at = |t: f32| t + offset + ((rate - 1.0) * (t - start) as f64) as f32;
        // A sub-fingerprint looks ~2 s ahead, so the items next to a cut are blurred: settle both edges with
        // the onset envelopes.
        let lower = if r == 0 { 0.0 } else { runs[r - 1].end as f32 * item };
        let upper = runs.get(r + 1).map_or(video_seconds, |next| next.start as f32 * item);
        let (mut start, mut end) = settle_edges(video, track, start, end, lower, upper, track_at);
        // Never past either end of the track.
        // Never past either end of the track.
        if track_at(start) < 0.0 {
            start -= track_at(start);
        }
        if track_at(end) > track_seconds {
            end -= (track_at(end) - track_seconds) / rate as f32;
        }
        if end - start < MIN_SEGMENT_SECONDS {
            continue;
        }
        let similarity = similarity_of(&video.prints, &track.prints, run, item, start, end);
        segments.push((start, end, track_at(start), rate, similarity));
    }

    let covered: f32 = segments.iter().map(|s| s.1 - s.0).sum();
    let coverage = if video_seconds > 0.0 { (covered / video_seconds) as f64 } else { 0.0 };
    let matched = coverage >= MIN_COVERAGE;
    let video_duration_ms = video.duration_ms;

    let mut result = Vec::new();
    let mut cursor = 0u64;
    let mut gains = Vec::new();
    if matched {
        for &(start, end, track_start, rate, similarity) in &segments {
            let start_ms = (start * 1000.0).round() as u64;
            let end_ms = ((end * 1000.0).round() as u64).min(video_duration_ms);
            if start_ms > cursor {
                result.push(video_segment(cursor, start_ms));
            }
            result.push(Segment {
                video_start_ms: start_ms,
                video_end_ms: end_ms,
                source: Source::Track,
                track_start_ms: Some((track_start.max(0.0) * 1000.0).round() as u64),
                rate: Some(rate),
                similarity: Some(similarity),
            });
            cursor = end_ms;
            collect_gains(video, track, start, end, track_start, rate, &mut gains);
        }
    }
    if cursor < video_duration_ms {
        result.push(video_segment(cursor, video_duration_ms));
    }

    Alignment {
        align_version: ALIGN_VERSION,
        video_duration_ms,
        track_duration_ms: track.duration_ms,
        matched,
        coverage: if matched { coverage.min(1.0) } else { 0.0 },
        video_gain_db: median(&mut gains).map_or(0.0, |g| (g as f64).clamp(-20.0, 20.0)),
        segments: result,
    }
}

fn video_segment(start: u64, end: u64) -> Segment {
    Segment { video_start_ms: start, video_end_ms: end, source: Source::Video, track_start_ms: None, rate: None, similarity: None }
}

/// The best offsets of a window: distinct matches (more than [`CLUSTER`] apart), lowest error first.
struct Best {
    entries: Vec<(i64, f32)>,
}

impl Best {
    fn offer(&mut self, offset: i64, error: f32) {
        if let Some(near) = self.entries.iter_mut().find(|(o, _)| (o - offset).abs() <= CLUSTER) {
            if error < near.1 {
                *near = (offset, error);
            }
            return;
        }
        if self.entries.len() < CANDIDATES {
            self.entries.push((offset, error));
        } else if let Some(worst) = self.entries.iter_mut().max_by(|a, b| a.1.total_cmp(&b.1)) {
            if error < worst.1 {
                *worst = (offset, error);
            }
        }
    }
}

/// Every window against every offset (prefix sums along each diagonal keep it linear in the pairs), then the
/// Viterbi path through the candidates, as runs of consistent offsets.
fn coarse_runs(video: &[u32], track: &[u32], item: f32) -> Vec<Run> {
    if video.len() < WINDOW || track.len() < WINDOW {
        return Vec::new();
    }
    let starts: Vec<usize> = (0..=video.len() - WINDOW).step_by(STEP).collect();
    let mut best: Vec<Best> = starts.iter().map(|_| Best { entries: Vec::with_capacity(CANDIDATES) }).collect();
    let bits = (WINDOW * 32) as f32;
    let mut prefix = Vec::with_capacity(video.len() + 1);

    for offset in -(video.len() as i64 - WINDOW as i64)..=(track.len() as i64 - WINDOW as i64) {
        // Video items i with 0 <= i + offset < track.len().
        let first = (-offset).max(0) as usize;
        let last = (track.len() as i64 - offset).min(video.len() as i64) as usize;
        if last < first + WINDOW {
            continue;
        }
        prefix.clear();
        prefix.push(0u32);
        let mut sum = 0u32;
        for i in first..last {
            sum += (video[i] ^ track[(i as i64 + offset) as usize]).count_ones();
            prefix.push(sum);
        }
        let from = starts.partition_point(|&s| s < first);
        for (k, &start) in starts.iter().enumerate().skip(from) {
            if start + WINDOW > last {
                break;
            }
            let error = (prefix[start + WINDOW - first] - prefix[start - first]) as f32 / bits;
            best[k].offer(offset, error);
        }
    }

    // Viterbi: states are each window's candidates plus "no match" (the last state).
    let states: Vec<Vec<(i64, f32)>> = best.into_iter().map(|b| b.entries.into_iter().filter(|e| e.1 < NONE_COST + JUMP_COST).collect()).collect();
    let mut cost: Vec<f32> = Vec::new();
    let mut back: Vec<Vec<usize>> = Vec::with_capacity(states.len());
    for (k, candidates) in states.iter().enumerate() {
        let mut next = Vec::with_capacity(candidates.len() + 1);
        let mut from = Vec::with_capacity(candidates.len() + 1);
        for state in 0..=candidates.len() {
            let emission = candidates.get(state).map_or(NONE_COST, |c| c.1);
            if k == 0 {
                next.push(emission);
                from.push(0);
                continue;
            }
            let previous = &states[k - 1];
            let mut chosen = (f32::INFINITY, 0);
            for (p, &total) in cost.iter().enumerate() {
                let transition = match (previous.get(p), candidates.get(state)) {
                    (None, None) => 0.0,
                    (Some(a), Some(b)) if (a.0 - b.0).abs() <= SAME_OFFSET => 0.0,
                    (Some(_), Some(_)) => JUMP_COST,
                    _ => SWITCH_COST,
                };
                if total + transition < chosen.0 {
                    chosen = (total + transition, p);
                }
            }
            next.push(chosen.0 + emission);
            from.push(chosen.1);
        }
        cost = next;
        back.push(from);
    }
    let mut path = vec![0usize; states.len()];
    let mut state = cost.iter().enumerate().min_by(|a, b| a.1.total_cmp(b.1)).map_or(0, |(i, _)| i);
    for k in (0..states.len()).rev() {
        path[k] = state;
        state = back[k][state];
    }

    // Each window decides the items at its centre (the first and last ones also their outer edges).
    let half = (WINDOW - STEP) / 2;
    let mut runs: Vec<Run> = Vec::new();
    for (k, &start) in starts.iter().enumerate() {
        let Some(&(offset, _)) = states[k].get(path[k]) else { continue };
        let from = if k == 0 { 0 } else { start + half };
        let to = if k + 1 == starts.len() { video.len() } else { start + half + STEP };
        match runs.last_mut() {
            Some(run) if run.end == from && (run.last - offset).abs() <= SAME_OFFSET => {
                run.end = to;
                run.last = offset;
            }
            _ => runs.push(Run { start: from, end: to, first: offset, last: offset }),
        }
    }

    // Close short gaps between runs that continue the same offset.
    let max_gap = (MAX_GAP_SECONDS / item).round() as usize;
    let mut merged: Vec<Run> = Vec::new();
    for run in runs {
        match merged.last_mut() {
            Some(previous) if run.start - previous.end <= max_gap && (previous.last - run.first).abs() <= SAME_OFFSET => {
                previous.end = run.end;
                previous.last = run.last;
            }
            _ => merged.push(run),
        }
    }
    merged
}

/// Moves each edge to where the items stop (or start) matching, without crossing the neighbouring runs.
fn refine_edges(mut runs: Vec<Run>, video: &[u32], track: &[u32]) -> Vec<Run> {
    let error = |i: usize, offset: i64| -> f32 {
        let j = i as i64 + offset;
        if i >= video.len() || j < 0 || j as usize >= track.len() {
            return 1.0;
        }
        (video[i] ^ track[j as usize]).count_ones() as f32 / 32.0
    };
    let smoothed = |i: usize, offset: i64| -> f32 {
        let from = i.saturating_sub(2);
        let to = (i + 3).min(video.len());
        if to <= from {
            return 1.0;
        }
        (from..to).map(|x| error(x, offset)).sum::<f32>() / (to - from) as f32
    };

    let count = runs.len();
    for r in 0..count {
        let lower = if r == 0 { 0 } else { runs[r - 1].end };
        let upper = if r + 1 < count { runs[r + 1].start } else { video.len() };
        let run = &mut runs[r];
        while run.start < run.end && smoothed(run.start, run.first) > EDGE_MISS {
            run.start += 1;
        }
        while run.start > lower && smoothed(run.start - 1, run.first) < EDGE_MATCH {
            run.start -= 1;
        }
        while run.end > run.start && smoothed(run.end - 1, run.last) > EDGE_MISS {
            run.end -= 1;
        }
        while run.end < upper && smoothed(run.end, run.last) < EDGE_MATCH {
            run.end += 1;
        }
    }
    runs.retain(|r| r.end > r.start);
    runs
}

/// Refines the coarse offsets (seconds) of a segment with the onset envelopes, at each end when the segment is
/// long enough to measure a drift. Returns the offset at `start` and the rate.
fn fine_offset(video: &Features, track: &Features, start: f32, end: f32, first: f32, last: f32) -> (f32, f64) {
    if end - start >= 2.0 * REFINE_SECONDS {
        let a = refine(video, track, start, start + REFINE_SECONDS, first).unwrap_or(first);
        let b = refine(video, track, end - REFINE_SECONDS, end, last).unwrap_or(last);
        let (ca, cb) = (start + REFINE_SECONDS / 2.0, end - REFINE_SECONDS / 2.0);
        let rate = 1.0 + ((b - a) / (cb - ca)) as f64;
        if (rate - 1.0).abs() < MIN_DRIFT || !(0.9..=1.1).contains(&rate) {
            return ((a + b) / 2.0, 1.0);
        }
        // Offset at `start`, following the line through both measurements.
        return (a - ((rate - 1.0) * (ca - start) as f64) as f32, rate);
    }
    let middle = (start + end) / 2.0;
    let half = ((end - start) / 2.0).min(REFINE_SECONDS / 2.0);
    let coarse = (first + last) / 2.0;
    (refine(video, track, middle - half, middle + half, coarse).unwrap_or(coarse), 1.0)
}

/// The offset (seconds) within ±[`REFINE_LAG_SECONDS`] of `coarse` that best correlates the onset envelopes of
/// the video between `from` and `to` with the track's.
fn refine(video: &Features, track: &Features, from: f32, to: f32, coarse: f32) -> Option<f32> {
    let v = &video.envelope;
    let t = &track.envelope;
    let a = (from * ENVELOPE_RATE).max(0.0) as usize;
    let b = ((to * ENVELOPE_RATE) as usize).min(v.len());
    if b <= a + 50 {
        return None;
    }
    let window = &v[a..b];
    let mean_v = window.iter().sum::<f32>() / window.len() as f32;
    let centred: Vec<f32> = window.iter().map(|x| x - mean_v).collect();
    let norm_v = centred.iter().map(|x| x * x).sum::<f32>().sqrt();
    if norm_v <= 1e-6 {
        return None;
    }
    let base = (coarse * ENVELOPE_RATE).round() as i64;
    let reach = (REFINE_LAG_SECONDS * ENVELOPE_RATE) as i64;
    let mut best: Option<(f32, i64)> = None;
    for lag in base - reach..=base + reach {
        let start = a as i64 + lag;
        if start < 0 || start as usize + window.len() > t.len() {
            continue;
        }
        let other = &t[start as usize..start as usize + window.len()];
        let mean_t = other.iter().sum::<f32>() / other.len() as f32;
        let mut dot = 0.0;
        let mut norm_t = 0.0;
        for (x, y) in centred.iter().zip(other) {
            let y = y - mean_t;
            dot += x * y;
            norm_t += y * y;
        }
        if norm_t <= 1e-6 {
            continue;
        }
        let correlation = dot / (norm_v * norm_t.sqrt());
        if best.is_none_or(|(c, _)| correlation > c) {
            best = Some((correlation, lag));
        }
    }
    // A weak peak means the envelopes say nothing reliable: keep the coarse offset.
    best.filter(|(c, _)| *c > 0.3).map(|(_, lag)| lag as f32 / ENVELOPE_RATE)
}

/// Moves each edge of a segment in 100 ms steps (at most [`EDGE_SECONDS`] each way, never past `lower` and
/// `upper`): inwards while the onsets just inside do not agree, then outwards while those just outside do.
fn settle_edges(video: &Features, track: &Features, start: f32, end: f32, lower: f32, upper: f32, track_at: impl Fn(f32) -> f32) -> (f32, f32) {
    let agree = |from: f32| -> bool {
        let v = (from * ENVELOPE_RATE).round() as i64;
        let t = (track_at(from) * ENVELOPE_RATE).round() as i64;
        let length = (EDGE_CONTEXT_SECONDS * ENVELOPE_RATE) as i64;
        if v < 0 || t < 0 || (v + length) as usize > video.envelope.len() || (t + length) as usize > track.envelope.len() {
            return false;
        }
        // Onsets are a frame or two wide: a few frames of slack keep an offset off by 10–30 ms from hiding the match.
        let window = &video.envelope[v as usize..(v + length) as usize];
        (-EDGE_SLACK..=EDGE_SLACK).any(|lag| {
            let start = t + lag;
            start >= 0
                && (start + length) as usize <= track.envelope.len()
                && correlation(window, &track.envelope[start as usize..(start + length) as usize]) >= EDGE_CORRELATION
        })
    };
    let steps = (EDGE_SECONDS / 0.1) as usize;
    let (mut start, mut end) = (start, end);
    for _ in 0..steps {
        if end - start <= MIN_SEGMENT_SECONDS || agree(start) {
            break;
        }
        start += 0.1;
    }
    for _ in 0..steps {
        if start - EDGE_CONTEXT_SECONDS < lower || !agree(start - EDGE_CONTEXT_SECONDS) {
            break;
        }
        start -= 0.1;
    }
    for _ in 0..steps {
        if end - start <= MIN_SEGMENT_SECONDS || agree(end - EDGE_CONTEXT_SECONDS) {
            break;
        }
        end -= 0.1;
    }
    for _ in 0..steps {
        if end + EDGE_CONTEXT_SECONDS > upper || !agree(end) {
            break;
        }
        end += 0.1;
    }
    (start, end)
}

fn correlation(a: &[f32], b: &[f32]) -> f32 {
    let mean_a = a.iter().sum::<f32>() / a.len() as f32;
    let mean_b = b.iter().sum::<f32>() / b.len() as f32;
    let (mut dot, mut norm_a, mut norm_b) = (0.0, 0.0, 0.0);
    for (x, y) in a.iter().zip(b) {
        let (x, y) = (x - mean_a, y - mean_b);
        dot += x * y;
        norm_a += x * x;
        norm_b += y * y;
    }
    if norm_a <= 1e-9 || norm_b <= 1e-9 { 0.0 } else { dot / (norm_a * norm_b).sqrt() }
}

fn similarity_of(video: &[u32], track: &[u32], run: &Run, item: f32, start: f32, end: f32) -> f64 {
    let from = ((start / item) as usize).max(run.start);
    let to = ((end / item) as usize).min(run.end).min(video.len());
    if to <= from {
        return 0.0;
    }
    let mut differing = 0u64;
    let mut count = 0u64;
    for i in from..to {
        // The offset drifts linearly from the first window to the last.
        let progress = if run.end > run.start + 1 { (i - run.start) as f64 / (run.end - run.start - 1) as f64 } else { 0.0 };
        let offset = run.first + ((run.last - run.first) as f64 * progress).round() as i64;
        let j = i as i64 + offset;
        if j < 0 || j as usize >= track.len() {
            continue;
        }
        differing += (video[i] ^ track[j as usize]).count_ones() as u64;
        count += 32;
    }
    if count == 0 { 0.0 } else { (1.0 - 2.0 * differing as f64 / count as f64).clamp(0.0, 1.0) }
}

/// Track loudness minus video loudness over the blocks where both play the same audio.
fn collect_gains(video: &Features, track: &Features, start: f32, end: f32, track_start: f32, rate: f64, gains: &mut Vec<f32>) {
    let from = (start * LOUDNESS_RATE).ceil() as usize;
    let to = ((end * LOUDNESS_RATE) as usize).min(video.loudness.len());
    for block in from..to {
        let at = block as f32 / LOUDNESS_RATE;
        let in_track = track_start + ((at - start) as f64 * rate) as f32;
        let other = (in_track * LOUDNESS_RATE).round() as usize;
        let (Some(&v), Some(&t)) = (video.loudness.get(block), track.loudness.get(other)) else { continue };
        if v > -50.0 && t > -50.0 {
            gains.push(t - v);
        }
    }
}

fn median(values: &mut [f32]) -> Option<f32> {
    if values.is_empty() {
        return None;
    }
    values.sort_by(f32::total_cmp);
    Some(values[values.len() / 2])
}
