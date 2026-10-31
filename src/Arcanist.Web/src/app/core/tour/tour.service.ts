import { Injectable, computed, signal } from '@angular/core';
import { tourKeyFor, type Tour } from './tour';

const STORAGE_KEY = 'arcanist.tours';

interface Stored {
  /** No tour starts by itself (the «?» buttons still show them). */
  disabled: boolean;
  /** The sections whose tour was seen or skipped. */
  seen: string[];
}

/**
 * The panel's tours: each section's starts by itself the first time it opens, unless it was seen or skipped, or
 * tours are off for this browser; the «?» button of each section shows it again. What was seen and whether they are
 * off is kept per browser (localStorage); Mi cuenta turns them back on.
 */
@Injectable({ providedIn: 'root' })
export class TourService {
  private readonly stored = signal<Stored>(read());
  private tours: Promise<Record<string, Tour>> | null = null;

  /** The tour being shown and its step. */
  readonly active = signal<{ tour: Tour; index: number } | null>(null);
  readonly disabled = computed(() => this.stored().disabled);

  /** The tours (in a chunk of their own: their words are in both languages). */
  private load(): Promise<Record<string, Tour>> {
    this.tours ??= import('./tours').then((m) => m.TOURS);
    return this.tours;
  }

  /** On opening a section: another section's tour closes (not marked seen), and this one's starts if it is due. */
  async maybeStart(url: string): Promise<void> {
    const key = tourKeyFor(url);
    if (this.active() && this.active()!.tour.id !== key) this.active.set(null);
    const stored = this.stored();
    if (stored.disabled || stored.seen.includes(key) || this.active()) return;
    const tour = (await this.load())[key];
    // The section may have changed while the tours loaded.
    if (tour && tourKeyFor(location.pathname.replace(/^\/admin/, '')) === key)
      this.active.set({ tour, index: 0 });
  }

  /** The «?» button: the section's tour, whatever was seen. */
  async start(url: string): Promise<void> {
    const tour = (await this.load())[tourKeyFor(url)];
    if (tour) this.active.set({ tour, index: 0 });
  }

  next(): void {
    const active = this.active();
    if (!active) return;
    if (active.index + 1 < active.tour.steps.length)
      this.active.set({ ...active, index: active.index + 1 });
    else this.finish();
  }

  back(): void {
    const active = this.active();
    if (active && active.index > 0) this.active.set({ ...active, index: active.index - 1 });
  }

  /** Done or skipped: this section's tour does not start by itself again. */
  finish(): void {
    const active = this.active();
    this.active.set(null);
    if (active)
      this.save({ ...this.stored(), seen: [...new Set([...this.stored().seen, active.tour.id])] });
  }

  /** No tour starts by itself any more (in this browser). */
  disableAll(): void {
    this.finish();
    this.save({ ...this.stored(), disabled: true });
  }

  /** Tours start by themselves again; with `fromScratch`, also those already seen. */
  enable(fromScratch = false): void {
    this.save({ disabled: false, seen: fromScratch ? [] : this.stored().seen });
  }

  private save(value: Stored): void {
    this.stored.set(value);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    } catch {
      // Without storage it holds while the page is open.
    }
  }
}

function read(): Stored {
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as Partial<Stored> | null;
    return {
      disabled: value?.disabled === true,
      seen: Array.isArray(value?.seen) ? value.seen : [],
    };
  } catch {
    return { disabled: false, seen: [] };
  }
}
