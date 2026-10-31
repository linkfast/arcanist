/** A text in the panel's languages (tours carry their own words, in their own chunk). */
export interface TourText {
  es: string;
  en: string;
}

/** A step of a tour: what it says and, if anything, what it points at. */
export interface TourStep {
  /**
   * A CSS selector of what the step points at (usually `[data-tour="…"]`); the first visible match. Without one, or
   * when nothing matches (the user cannot see that part), the step shows in the middle of the screen.
   */
  target?: string;
  title: TourText;
  body: TourText;
}

/** A section's tour: shown the first time the section opens (unless tours are off) and from its «?» button. */
export interface Tour {
  /** The section (see `tourKeyFor`). */
  id: string;
  steps: TourStep[];
}

/**
 * The section of the panel a URL is, for its tour: the first part of the path (the overview for `/`), with the pages
 * of installed extensions as one section and the lyrics review with or without a song.
 */
export function tourKeyFor(url: string): string {
  const path = url.split(/[?#]/)[0].replace(/^\/+/, '');
  const [first, second] = path.split('/');
  if (!first) return 'overview';
  if (first === 'extensions' && second) return 'extension-page';
  if (first === 'lyrics') return 'lyrics-review';
  return first;
}
