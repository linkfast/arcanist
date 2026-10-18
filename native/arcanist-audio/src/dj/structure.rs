//! The track's form: sections found where a bar-level self-similarity matrix changes (a checkerboard novelty
//! kernel, as in Foote's segmentation), labelled by what they resemble and named by energy, repetition and place;
//! phrases, the highlight and where to mix in and out.

use super::beats::Grid;
use super::features::MFCC;
use super::timing::{Timing, db};

/// A section lasts at least this many bars.
const MIN_BARS: usize = 8;
/// Bars on each side of the novelty kernel.
const KERNEL: usize = 4;
/// Sections this similar get the same label.
const SAME_LABEL: f32 = 0.82;

pub struct Section {
    pub start: f32,
    pub end: f32,
    pub label: char,
    pub name: &'static str,
    /// 0–1: its loudness against the loudest section.
    pub energy: f32,
    pub trend: &'static str,
    pub occurrence: usize,
    pub vocal: f32,
    /// The bar it starts at (in the grid's bars), when there is a grid.
    pub first_bar: Option<usize>,
    pub bars: usize,
}

pub struct Phrase {
    pub start: f32,
    pub bars: usize,
}

pub struct Structure {
    pub sections: Vec<Section>,
    pub phrases: Vec<Phrase>,
    pub highlight: Option<(f32, f32, f32)>,
    pub intro_end: f32,
    pub outro_start: f32,
    pub mix_in: Vec<f32>,
    pub mix_out: Vec<f32>,
}

pub fn analyze(t: &Timing, grid: Option<&Grid>, chroma: &[[f32; 12]], mfcc: &[[f32; MFCC]], vocal: &[f32], music: (f32, f32)) -> Structure {
    // The units of form: bars from the grid, or two-second blocks without a beat.
    let mut bars: Vec<(f32, f32)> = match grid {
        Some(g) if g.downbeats.len() >= 2 => {
            let starts = g.downbeat_times();
            let bar = starts[1] - starts[0];
            starts.iter().enumerate().map(|(i, s)| (*s, starts.get(i + 1).copied().unwrap_or(s + bar).min(music.1))).filter(|(s, e)| e > s).collect()
        }
        _ => {
            let count = ((music.1 - music.0) / 2.0).ceil().max(1.0) as usize;
            (0..count).map(|i| (music.0 + 2.0 * i as f32, (music.0 + 2.0 * (i + 1) as f32).min(music.1))).collect()
        }
    };
    if bars.is_empty() {
        bars.push(music);
    }
    let gridded = grid.is_some_and(|g| g.downbeats.len() >= 2);

    // Bar features: harmony (chroma) and timbre (MFCCs), each normalized so both count alike.
    let vectors = bar_vectors(t, &bars, chroma, mfcc);
    let n = bars.len();
    let similarity = |a: usize, b: usize| vectors[a].iter().zip(&vectors[b]).map(|(x, y)| x * y).sum::<f32>();
    let level: Vec<f32> = bars.iter().map(|(s, e)| db(t.power(*s, *e))).collect();

    // Novelty (Foote's checkerboard kernel): how much the bars before and after a boundary resemble their own side
    // more than each other, plus jumps in loudness.
    let block = |from: usize, to: usize, other: (usize, usize)| -> f32 {
        let mut sum = 0.0;
        for a in from..to {
            for b in other.0..other.1 {
                sum += similarity(a, b);
            }
        }
        sum / ((to - from) * (other.1 - other.0)).max(1) as f32
    };
    let mut novelty = vec![0.0f32; n];
    for (i, value) in novelty.iter_mut().enumerate().skip(2) {
        // The same number of bars on each side (at the ends, fewer), never just one: a lone bar resembles itself.
        let side = KERNEL.min(i).min(n - i);
        if side < 2 {
            continue;
        }
        let (past, future) = ((i - side, i), (i, i + side));
        let within = (block(past.0, past.1, past) + block(future.0, future.1, future)) / 2.0;
        let across = block(past.0, past.1, future);
        *value = (within - across).max(0.0) + 0.05 * (level[i] - level[i - 1]).abs();
    }

    // Boundaries: the strongest novelty peaks, at least MIN_BARS apart, preferring bars on a four-bar phrase.
    let mean = novelty.iter().sum::<f32>() / n as f32;
    let deviation = (novelty.iter().map(|v| (v - mean) * (v - mean)).sum::<f32>() / n as f32).sqrt();
    let mut candidates: Vec<(usize, f32)> = (1..n)
        .filter(|&i| novelty[i] > (mean + 0.25 * deviation).max(0.1) && novelty[i] >= novelty[i - 1] && novelty.get(i + 1).is_none_or(|v| novelty[i] >= *v))
        .map(|i| {
            // A nearby bar on the four-bar phrase with almost the same novelty is the boundary instead.
            let aligned = [i.saturating_sub(1), i + 1, i.saturating_sub(2), i + 2]
                .into_iter()
                .filter(|&j| j > 0 && j < n && j % 4 == 0 && novelty[j] >= 0.8 * novelty[i])
                .next();
            let at = if gridded && i % 4 != 0 { aligned.unwrap_or(i) } else { i };
            (at, novelty[i])
        })
        .collect();
    candidates.sort_by(|a, b| b.1.total_cmp(&a.1).then(a.0.cmp(&b.0)));
    let mut boundaries = vec![0usize];
    for (i, _) in candidates {
        if i >= MIN_BARS && n - i >= MIN_BARS.min(n) && boundaries.iter().all(|b| b.abs_diff(i) >= MIN_BARS) {
            boundaries.push(i);
        }
    }
    boundaries.sort_unstable();

    let mut sections: Vec<Section> = boundaries
        .iter()
        .enumerate()
        .map(|(k, &from)| {
            let to = boundaries.get(k + 1).copied().unwrap_or(n);
            let (start, end) = (bars[from].0, bars[to - 1].1);
            Section {
                start,
                end,
                label: ' ',
                name: "section",
                energy: t.power(start, end),
                trend: "steady",
                occurrence: 0,
                vocal: mean_over(t, vocal, start, end),
                first_bar: gridded.then_some(from),
                bars: to - from,
            }
        })
        .collect();

    // A beatless intro before the first downbeat is a section of its own.
    if gridded && bars[0].0 - music.0 > 4.0 {
        sections.insert(
            0,
            Section {
                start: music.0,
                end: bars[0].0,
                label: ' ',
                name: "section",
                energy: t.power(music.0, bars[0].0),
                trend: "steady",
                occurrence: 0,
                vocal: mean_over(t, vocal, music.0, bars[0].0),
                first_bar: None,
                bars: 0,
            },
        );
    }

    // Labels: a section resembling an earlier one gets its letter.
    let span_vector = |s: &Section| -> Vec<f32> {
        let members: Vec<usize> = (0..n).filter(|&i| bars[i].0 >= s.start - 0.01 && bars[i].1 <= s.end + 0.01).collect();
        let mut v = vec![0.0f32; vectors[0].len()];
        for &i in &members {
            v.iter_mut().zip(&vectors[i]).for_each(|(a, b)| *a += b);
        }
        let norm = v.iter().map(|x| x * x).sum::<f32>().sqrt();
        if norm > f32::EPSILON { v.iter().map(|x| x / norm).collect() } else { v }
    };
    let means: Vec<Vec<f32>> = sections.iter().map(&span_vector).collect();
    let mut next = b'A';
    for k in 0..sections.len() {
        let earlier = (0..k)
            .map(|j| (j, means[j].iter().zip(&means[k]).map(|(a, b)| a * b).sum::<f32>()))
            .filter(|(_, s)| *s >= SAME_LABEL)
            .max_by(|a, b| a.1.total_cmp(&b.1));
        sections[k].label = match earlier {
            Some((j, _)) => sections[j].label,
            None => {
                let label = next as char;
                next = next.saturating_add(1).min(b'Z');
                label
            }
        };
        sections[k].occurrence = sections[..=k].iter().filter(|s| s.label == sections[k].label).count();
    }

    // Energy (against the loudest section) and its trend within each section.
    let loudest = sections.iter().map(|s| s.energy).fold(0.0f32, f32::max).max(1e-12);
    for s in &mut sections {
        let rise = db(t.power(s.end - (s.end - s.start).min(4.0), s.end)) - db(t.power(s.start, s.start + (s.end - s.start).min(4.0)));
        s.trend = if rise > 3.0 { "rising" } else if rise < -3.0 { "falling" } else { "steady" };
        s.energy = (s.energy / loudest).sqrt().clamp(0.0, 1.0);
    }
    name_sections(&mut sections);

    let phrases = phrases(&sections, grid);
    let highlight = highlight(&sections, grid);
    let (intro_end, outro_start, mix_in, mix_out) = mix_points(t, &sections, &phrases, vocal, music, grid);
    Structure { sections, phrases, highlight, intro_end, outro_start, mix_in, mix_out }
}

/// Names by energy, repetition and place: the chorus is the loudest repeated section, a drop a chorus that comes
/// in with a jump in energy and little voice, the intro and outro the quieter ends.
fn name_sections(sections: &mut [Section]) {
    let count = sections.len();
    if count == 0 {
        return;
    }
    let repeats = |label: char, all: &[Section]| all.iter().filter(|s| s.label == label).count();
    let mut label_energy: Vec<(char, f32)> = Vec::new();
    for s in sections.iter() {
        if repeats(s.label, sections) >= 2 && !label_energy.iter().any(|(l, _)| *l == s.label) {
            let mean = sections.iter().filter(|x| x.label == s.label).map(|x| x.energy).sum::<f32>() / repeats(s.label, sections) as f32;
            label_energy.push((s.label, mean));
        }
    }
    let chorus = label_energy.iter().max_by(|a, b| a.1.total_cmp(&b.1)).filter(|(_, e)| *e >= 0.6).map(|(l, _)| *l);

    for i in 0..count {
        let s = &sections[i];
        let previous_energy = if i > 0 { sections[i - 1].energy } else { 0.0 };
        let name = if Some(s.label) == chorus {
            if s.energy - previous_energy >= 0.25 && s.vocal < 0.3 { "drop" } else { "chorus" }
        } else if i == 0 && count > 1 && s.energy < 0.75 {
            "intro"
        } else if i == count - 1 && count > 1 && (s.energy < 0.75 || s.trend == "falling") {
            "outro"
        } else if chorus.is_none() && s.energy >= 0.9 && s.energy - previous_energy >= 0.3 && s.vocal < 0.3 && i > 0 {
            "drop"
        } else if repeats(s.label, sections) >= 2 {
            "verse"
        } else if i > 0 && i < count - 1 && s.energy < 0.55 && previous_energy >= 0.7 {
            "break"
        } else if i * 2 >= count && i < count - 1 {
            "bridge"
        } else {
            "section"
        };
        sections[i].name = name;
    }
    // The section leading into a chorus or drop while getting louder builds up to it.
    for i in 0..count.saturating_sub(1) {
        if matches!(sections[i + 1].name, "chorus" | "drop") && sections[i].trend == "rising" && !matches!(sections[i].name, "chorus" | "drop" | "intro") {
            sections[i].name = "build";
        }
    }
}

/// Phrases of 16 bars (8, or what is left, at the end of a section), from each section's first bar.
fn phrases(sections: &[Section], grid: Option<&Grid>) -> Vec<Phrase> {
    let Some(grid) = grid else { return Vec::new() };
    let downbeats = grid.downbeat_times();
    let mut out = Vec::new();
    for s in sections {
        let Some(first) = s.first_bar else { continue };
        let mut bar = first;
        let end = first + s.bars;
        while bar < end {
            let left = end - bar;
            let size = if left >= 16 { 16 } else if left >= 8 { 8 } else { left };
            out.push(Phrase { start: downbeats[bar], bars: size });
            bar += size;
        }
    }
    out
}

/// The most representative part: the loudest drop or chorus (the first of the loudest), eight bars of it.
fn highlight(sections: &[Section], grid: Option<&Grid>) -> Option<(f32, f32, f32)> {
    let named = sections.iter().filter(|s| matches!(s.name, "drop" | "chorus"));
    let best = named
        .clone()
        .max_by(|a, b| a.energy.total_cmp(&b.energy).then(b.start.total_cmp(&a.start)))
        .or_else(|| sections.iter().max_by(|a, b| a.energy.total_cmp(&b.energy).then(b.start.total_cmp(&a.start))))?;
    let length = match grid {
        Some(g) => 8.0 * g.beats_per_bar as f32 * 60.0 / g.bpm,
        None => 15.0,
    };
    let mean = sections.iter().map(|s| s.energy).sum::<f32>() / sections.len() as f32;
    let confidence = if named.count() > 0 { (0.5 + (best.energy - mean)).clamp(0.3, 1.0) } else { 0.3 };
    Some((best.start, (best.start + length).min(best.end), confidence))
}

/// Where the intro ends and the outro starts, and phrase starts without voice to mix in (early) and out (late).
fn mix_points(t: &Timing, sections: &[Section], phrases: &[Phrase], vocal: &[f32], music: (f32, f32), grid: Option<&Grid>) -> (f32, f32, Vec<f32>, Vec<f32>) {
    let length = music.1 - music.0;
    let intro_end = match sections.first() {
        Some(s) if s.name == "intro" => s.end,
        _ => phrases.iter().map(|p| p.start).find(|&p| p - music.0 >= length.min(30.0) * 0.5).or(sections.first().map(|s| s.end)).unwrap_or(music.0),
    };
    let outro_start = match sections.last() {
        Some(s) if s.name == "outro" => s.start,
        _ => phrases.iter().map(|p| p.start).rev().find(|&p| music.1 - p >= length.min(30.0) * 0.5).or(sections.last().map(|s| s.start)).unwrap_or(music.1),
    };
    let bar = grid.map_or(2.0, |g| g.beats_per_bar as f32 * 60.0 / g.bpm);
    let quiet = |p: f32| mean_over(t, vocal, p, p + 8.0 * bar) < 0.35;
    let starts: Vec<f32> = if phrases.is_empty() { sections.iter().map(|s| s.start).collect() } else { phrases.iter().map(|p| p.start).collect() };
    let mut mix_in: Vec<f32> = starts.iter().copied().filter(|&p| p <= music.0 + length * 0.4 && quiet(p)).take(4).collect();
    if let Some(&first) = starts.first() {
        if !mix_in.contains(&first) {
            mix_in.insert(0, first);
            mix_in.truncate(4);
        }
    }
    let late: Vec<f32> = starts.iter().copied().filter(|&p| p >= music.0 + length * 0.6 && music.1 - p >= 8.0 * bar.min(4.0) && quiet(p)).collect();
    let mix_out = late[late.len().saturating_sub(4)..].to_vec();
    (intro_end, outro_start, mix_in, mix_out)
}

fn bar_vectors(t: &Timing, bars: &[(f32, f32)], chroma: &[[f32; 12]], mfcc: &[[f32; MFCC]]) -> Vec<Vec<f32>> {
    let frames = chroma.len().min(mfcc.len());
    let raw: Vec<([f32; 12], [f32; MFCC])> = bars
        .iter()
        .map(|(s, e)| {
            let (from, to) = (t.frame_of(*s).min(frames), t.frame_of(*e).min(frames));
            let mut c = [0.0f32; 12];
            let mut m = [0.0f32; MFCC];
            for f in from..to.max(from) {
                c.iter_mut().zip(&chroma[f]).for_each(|(a, b)| *a += b);
                m.iter_mut().zip(&mfcc[f]).for_each(|(a, b)| *a += b);
            }
            let count = (to.max(from) - from).max(1) as f32;
            (c, m.map(|v| v / count))
        })
        .collect();
    // MFCCs (without the loudness coefficient) standardized across the track, so timbre changes show.
    let dims = MFCC - 1;
    let mut mean = vec![0.0f32; dims];
    let mut deviation = vec![0.0f32; dims];
    for (_, m) in &raw {
        for d in 0..dims {
            mean[d] += m[d + 1];
        }
    }
    mean.iter_mut().for_each(|v| *v /= raw.len().max(1) as f32);
    for (_, m) in &raw {
        for d in 0..dims {
            deviation[d] += (m[d + 1] - mean[d]).powi(2);
        }
    }
    deviation.iter_mut().for_each(|v| *v = (*v / raw.len().max(1) as f32).sqrt().max(1e-6));

    raw.iter()
        .map(|(c, m)| {
            let norm = c.iter().map(|x| x * x).sum::<f32>().sqrt().max(1e-9);
            let mut v: Vec<f32> = c.iter().map(|x| x / norm).collect();
            let timbre: Vec<f32> = (0..dims).map(|d| (m[d + 1] - mean[d]) / deviation[d]).collect();
            let tnorm = timbre.iter().map(|x| x * x).sum::<f32>().sqrt().max(1e-9);
            v.extend(timbre.iter().map(|x| x / tnorm));
            let all = v.iter().map(|x| x * x).sum::<f32>().sqrt().max(1e-9);
            v.iter().map(|x| x / all).collect()
        })
        .collect()
}

fn mean_over(t: &Timing, values: &[f32], from: f32, to: f32) -> f32 {
    let (a, b) = (t.frame_of(from).min(values.len()), t.frame_of(to).min(values.len()));
    if b > a { values[a..b].iter().sum::<f32>() / (b - a) as f32 } else { 0.0 }
}
