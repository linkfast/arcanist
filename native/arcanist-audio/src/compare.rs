//! Compares two raw Chromaprint fingerprints, allowing for a small time offset between them (a different
//! encoder delay, a few seconds of silence trimmed).

/// Up to ~10 seconds of offset, in sub-fingerprints (each covers ~0.124 s).
const MAX_OFFSET: isize = 80;

/// 0 means unrelated audio (half the bits differ, as for random data), 1 identical audio.
pub fn similarity(a: &[u32], b: &[u32]) -> f64 {
    let shorter = a.len().min(b.len());
    if shorter == 0 {
        return 0.0;
    }
    // Every alignment considered must overlap on at least half of the shorter fingerprint.
    let min_overlap = (shorter / 2).max(1);

    let mut best = 0.0f64;
    for offset in -MAX_OFFSET..=MAX_OFFSET {
        let (a_start, b_start) = if offset >= 0 { (offset as usize, 0) } else { (0, (-offset) as usize) };
        if a_start >= a.len() || b_start >= b.len() {
            continue;
        }
        let overlap = (a.len() - a_start).min(b.len() - b_start);
        if overlap < min_overlap {
            continue;
        }
        let differing: u32 = a[a_start..a_start + overlap]
            .iter()
            .zip(&b[b_start..b_start + overlap])
            .map(|(x, y)| (x ^ y).count_ones())
            .sum();
        let error_rate = differing as f64 / (overlap as f64 * 32.0);
        best = best.max(1.0 - 2.0 * error_rate);
    }
    best.clamp(0.0, 1.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pseudo_random(seed: u32, length: usize) -> Vec<u32> {
        let mut state = seed;
        (0..length)
            .map(|_| {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                state
            })
            .collect()
    }

    #[test]
    fn identical_and_shifted_fingerprints_match() {
        let a = pseudo_random(7, 900);
        assert_eq!(similarity(&a, &a), 1.0);
        assert_eq!(similarity(&a[20..], &a), 1.0);
    }

    #[test]
    fn unrelated_fingerprints_do_not() {
        assert!(similarity(&pseudo_random(1, 900), &pseudo_random(2, 900)) < 0.2);
        assert_eq!(similarity(&[], &pseudo_random(2, 10)), 0.0);
    }
}
