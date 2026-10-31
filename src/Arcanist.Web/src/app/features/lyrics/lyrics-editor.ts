import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronDown,
  lucideCirclePlay,
  lucideCrosshair,
  lucideMinus,
  lucidePause,
  lucidePlay,
  lucidePlus,
  lucideTrash2,
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { LyricsReviewQuery } from '../../graphql/generated/graphql';
import {
  AcceptLyricsDraftMutation,
  DiscardLyricsDraftMutation,
  LyricsReviewQuery as ReviewQuery,
  SaveLyricsDraftMutation,
} from '../../graphql/operations';

type Song = LyricsReviewQuery['track'];

/** A line in the editor: its time, its words and how sure the model was (null once edited). */
interface Line {
  id: number;
  timeMs: number;
  text: string;
  confidence: number | null;
}

/** Below this the model was unsure: the line is marked to look at first. */
const UNCERTAIN = 0.6;
/** How long after the last change the draft saves itself. */
const AUTOSAVE_MS = 2000;

let nextId = 1;

/**
 * Reviewing the lyrics a model recognized in a song, line by line, while it plays: the line being sung is lit and
 * kept in view, lines the model was unsure of are marked (and «Next doubtful» jumps to them), a line's time is set
 * by pressing «Mark» (or Ctrl+Enter) when it starts, Enter splits a line and Backspace at its start joins it to the
 * one before. Changes save themselves; accepting makes them the song's lyrics.
 */
@Component({
  selector: 'app-lyrics-editor',
  imports: [FormsModule, NgIcon, HlmBadgeImports, HlmButtonImports, TranslatePipe],
  providers: [
    provideIcons({
      lucideChevronDown,
      lucideCirclePlay,
      lucideCrosshair,
      lucideMinus,
      lucidePause,
      lucidePlay,
      lucidePlus,
      lucideTrash2,
      lucideTriangleAlert,
    }),
  ],
  host: { '(document:keydown)': 'globalKey($event)' },
  template: `
    @if (song(); as s) {
      <div class="grid gap-4">
        <div class="flex items-center gap-3" data-tour="lyrics-song">
          @if (s.artworkId) {
            <img
              class="size-14 shrink-0 rounded-md object-cover"
              [src]="'/rest/v1/artwork/' + s.artworkId + '?size=128'"
              alt=""
            />
          }
          <div class="grid min-w-0 flex-1">
            <span class="truncate font-semibold">{{ s.title }}</span>
            <span class="truncate text-sm text-muted-foreground"
              >{{ artists() }} · {{ s.album.title }}</span
            >
          </div>
          <span class="text-xs text-muted-foreground">{{ saveState() | t }}</span>
        </div>

        <!-- The player: everything follows its clock. -->
        <div class="grid gap-2 rounded-lg border p-3" data-tour="lyrics-player">
          <audio
            #audio
            [src]="s.streamUrl"
            preload="auto"
            (play)="playing.set(true)"
            (pause)="playing.set(false)"
            (timeupdate)="tick()"
            (loadedmetadata)="durationMs.set(audio.duration * 1000)"
          ></audio>
          <div class="flex items-center gap-3">
            <button
              hlmBtn
              size="icon"
              type="button"
              [attr.aria-label]="(playing() ? 'lyricsReview.pause' : 'lyricsReview.play') | t"
              (click)="toggle()"
            >
              <ng-icon [name]="playing() ? 'lucidePause' : 'lucidePlay'" />
            </button>
            <span class="w-24 shrink-0 font-mono text-sm tabular-nums">
              {{ clock(position()) }}
            </span>
            <div class="relative min-w-0 flex-1">
              <input
                type="range"
                class="w-full accent-primary"
                min="0"
                [max]="durationMs()"
                step="100"
                [value]="position()"
                [attr.aria-label]="'lyricsReview.seek' | t"
                (input)="seek(+$any($event.target).value)"
              />
              <!-- Where each line starts; the doubtful ones in amber. -->
              <div class="pointer-events-none absolute inset-x-0 -bottom-1 h-1.5 overflow-hidden">
                @for (line of lines(); track line.id) {
                  <span
                    class="absolute h-1.5 w-0.5"
                    [class]="isUncertain(line) ? 'bg-amber-500' : 'bg-muted-foreground/50'"
                    [style.left.%]="Math.min(99.5, (line.timeMs / Math.max(1, durationMs())) * 100)"
                  ></span>
                }
              </div>
            </div>
            <span
              class="hidden w-16 shrink-0 text-right font-mono text-xs text-muted-foreground sm:inline"
            >
              {{ clock(durationMs()) }}
            </span>
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-2" data-tour="lyrics-toolbar">
          @if (uncertainCount() > 0) {
            <button hlmBtn size="sm" variant="outline" type="button" (click)="nextDoubtful()">
              <ng-icon name="lucideTriangleAlert" class="text-amber-500" />
              {{ 'lyricsReview.nextDoubtful' | t: { count: uncertainCount() } }}
            </button>
          }
          <button
            hlmBtn
            size="sm"
            variant="outline"
            type="button"
            (click)="showHelp.set(!showHelp())"
          >
            {{ 'lyricsReview.shortcuts' | t }}
            <ng-icon name="lucideChevronDown" />
          </button>
          <span class="flex-1"></span>
          <button hlmBtn size="sm" variant="ghost" type="button" (click)="discard()">
            {{ 'lyricsReview.discard' | t }}
          </button>
          <button
            hlmBtn
            size="sm"
            type="button"
            [disabled]="busy()"
            (click)="accept()"
            data-tour="lyrics-accept"
          >
            {{ 'lyricsReview.accept' | t }}
          </button>
        </div>
        @if (showHelp()) {
          <ul
            class="grid gap-1 rounded-lg bg-muted/50 p-3 text-xs text-muted-foreground sm:grid-cols-2"
          >
            @for (key of shortcutKeys; track key) {
              <li>{{ 'lyricsReview.keys.' + key | t }}</li>
            }
          </ul>
        }

        <!-- The lines. -->
        <ol class="grid gap-1" data-tour="lyrics-lines">
          @for (line of lines(); track line.id; let i = $index) {
            <li
              #row
              class="group flex items-center gap-1 rounded-md border px-1 py-1 transition-colors"
              [class.border-primary]="i === current()"
              [class.bg-accent]="i === current()"
              [class.ring-1]="i === current()"
              [class.ring-primary]="i === current()"
              [class.border-amber-500/60]="i !== current() && isUncertain(line)"
              [class.bg-amber-500/5]="i !== current() && isUncertain(line)"
            >
              <button
                type="button"
                class="w-20 shrink-0 rounded px-1 py-1 text-left font-mono text-xs tabular-nums hover:bg-accent"
                [class.text-destructive]="outOfOrder(i)"
                [attr.title]="
                  (outOfOrder(i) ? 'lyricsReview.outOfOrder' : 'lyricsReview.playFromHere') | t
                "
                (click)="playFrom(line)"
              >
                {{ clock(line.timeMs) }}
              </button>
              <div class="hidden shrink-0 sm:flex">
                <button
                  hlmBtn
                  size="icon-sm"
                  variant="ghost"
                  type="button"
                  [attr.aria-label]="'lyricsReview.earlier' | t"
                  (click)="nudge(i, -100)"
                >
                  <ng-icon name="lucideMinus" />
                </button>
                <button
                  hlmBtn
                  size="icon-sm"
                  variant="ghost"
                  type="button"
                  [attr.aria-label]="'lyricsReview.later' | t"
                  (click)="nudge(i, 100)"
                >
                  <ng-icon name="lucidePlus" />
                </button>
              </div>
              <button
                hlmBtn
                size="icon-sm"
                variant="ghost"
                type="button"
                [attr.title]="'lyricsReview.mark' | t"
                [attr.aria-label]="'lyricsReview.mark' | t"
                (click)="mark(i)"
              >
                <ng-icon name="lucideCrosshair" />
              </button>
              <input
                #text
                class="min-w-0 flex-1 rounded bg-transparent px-2 py-1 text-sm outline-none focus:bg-background focus:ring-2 focus:ring-ring"
                [class.italic]="!line.text"
                [placeholder]="'lyricsReview.break' | t"
                [value]="line.text"
                (input)="edit(i, $any($event.target).value)"
                (keydown)="lineKey($event, i)"
              />
              @if (isUncertain(line)) {
                <span
                  class="hidden shrink-0 text-xs text-amber-600 sm:inline dark:text-amber-400"
                  [attr.title]="'lyricsReview.uncertain' | t"
                  >{{ percent(line.confidence) }} %</span
                >
              }
              <button
                hlmBtn
                size="icon-sm"
                variant="ghost"
                type="button"
                class="opacity-60 group-hover:opacity-100"
                [attr.aria-label]="'lyricsReview.remove' | t"
                (click)="remove(i)"
              >
                <ng-icon name="lucideTrash2" />
              </button>
            </li>
          }
        </ol>
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          class="justify-self-start"
          (click)="add()"
        >
          <ng-icon name="lucidePlus" /> {{ 'lyricsReview.addLine' | t }}
        </button>
      </div>
    }
  `,
})
export class LyricsEditor implements OnDestroy {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  readonly trackId = input.required<string>();
  /** Accepted or discarded: the page moves on. */
  readonly done = output<void>();

  protected readonly Math = Math;
  protected readonly shortcutKeys = ['play', 'mark', 'split', 'join', 'move', 'nudge'];

  protected readonly song = signal<Song | null>(null);
  protected readonly lines = signal<Line[]>([]);
  protected readonly position = signal(0);
  protected readonly durationMs = signal(0);
  protected readonly playing = signal(false);
  protected readonly busy = signal(false);
  protected readonly showHelp = signal(false);
  private readonly dirty = signal(false);
  private readonly saving = signal(false);

  private readonly audio = viewChild<ElementRef<HTMLAudioElement>>('audio');
  private readonly rows = viewChildren<ElementRef<HTMLLIElement>>('row');
  private readonly inputs = viewChildren<ElementRef<HTMLInputElement>>('text');
  private autosave: ReturnType<typeof setTimeout> | null = null;
  private frame = 0;

  protected readonly artists = computed(
    () =>
      this.song()
        ?.artists.filter((a) => a.role === 'PRIMARY')
        .map((a) => a.name)
        .join(', ') ?? '',
  );

  /** The line being sung: the last one that started. */
  protected readonly current = computed(() => {
    const ms = this.position();
    const lines = this.lines();
    let index = -1;
    for (let i = 0; i < lines.length; i++) if (lines[i].timeMs <= ms) index = i;
    return index;
  });

  protected readonly uncertainCount = computed(
    () => this.lines().filter((l) => this.isUncertain(l)).length,
  );

  protected readonly saveState = computed(() =>
    this.saving()
      ? 'lyricsReview.saving'
      : this.dirty()
        ? 'lyricsReview.unsaved'
        : 'lyricsReview.saved',
  );

  constructor() {
    effect(() => {
      const id = this.trackId();
      untracked(() => void this.load(id));
    });
    // The line being sung stays in view while it plays.
    effect(() => {
      const index = this.current();
      if (!this.playing() || index < 0) return;
      untracked(() =>
        this.rows()[index]?.nativeElement.scrollIntoView({ block: 'nearest', behavior: 'smooth' }),
      );
    });
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frame);
    if (this.dirty()) void this.save();
  }

  protected isUncertain(line: Line): boolean {
    return line.confidence !== null && line.confidence < UNCERTAIN;
  }

  protected percent(confidence: number | null): number {
    return Math.round((confidence ?? 0) * 100);
  }

  protected outOfOrder(index: number): boolean {
    const lines = this.lines();
    return index > 0 && lines[index].timeMs < lines[index - 1].timeMs;
  }

  /** m:ss.cc */
  protected clock(ms: number): string {
    const total = Math.max(0, Math.round(ms / 10));
    const minutes = Math.floor(total / 6000);
    const seconds = Math.floor((total % 6000) / 100);
    const hundredths = total % 100;
    return `${minutes}:${String(seconds).padStart(2, '0')}.${String(hundredths).padStart(2, '0')}`;
  }

  // Playback.

  protected toggle(): void {
    const element = this.audio()?.nativeElement;
    if (!element) return;
    if (element.paused) void element.play();
    else element.pause();
  }

  protected seek(ms: number): void {
    const element = this.audio()?.nativeElement;
    if (element) element.currentTime = ms / 1000;
    this.position.set(ms);
  }

  protected playFrom(line: Line): void {
    // A little before, to hear the line come in.
    this.seek(Math.max(0, line.timeMs - 500));
    void this.audio()?.nativeElement.play();
  }

  /** Follows the clock smoothly while it plays (timeupdate alone comes four times a second). */
  protected tick(): void {
    const element = this.audio()?.nativeElement;
    if (!element) return;
    this.position.set(element.currentTime * 1000);
    if (!element.paused && !this.frame) {
      const follow = () => {
        this.frame = 0;
        if (element.paused) return;
        this.position.set(element.currentTime * 1000);
        this.frame = requestAnimationFrame(follow);
      };
      this.frame = requestAnimationFrame(follow);
    }
  }

  // Editing.

  protected edit(index: number, text: string): void {
    this.change((lines) => {
      lines[index] = { ...lines[index], text, confidence: null };
    });
  }

  /** The line starts now (where the player is). */
  protected mark(index: number): void {
    const ms = Math.round(this.position());
    this.change((lines) => {
      lines[index] = { ...lines[index], timeMs: ms };
    });
  }

  protected nudge(index: number, deltaMs: number): void {
    this.change((lines) => {
      lines[index] = { ...lines[index], timeMs: Math.max(0, lines[index].timeMs + deltaMs) };
    });
  }

  protected remove(index: number): void {
    this.change((lines) => lines.splice(index, 1));
  }

  protected add(): void {
    const last = this.lines().at(-1);
    const timeMs = this.playing() ? Math.round(this.position()) : (last?.timeMs ?? 0) + 2000;
    this.change((lines) => lines.push({ id: nextId++, timeMs, text: '', confidence: null }));
    setTimeout(() => this.focus(this.lines().length - 1, 0));
  }

  protected lineKey(event: KeyboardEvent, index: number): void {
    const field = event.target as HTMLInputElement;
    const caret = field.selectionStart ?? 0;
    const lines = this.lines();
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      // Mark this line and go to the next: syncing while listening.
      event.preventDefault();
      this.mark(index);
      if (index + 1 < lines.length) this.focus(index + 1, 0);
    } else if (event.key === 'Enter') {
      // Split at the caret: the rest starts a new line (now, if it plays; else halfway to the next).
      event.preventDefault();
      const line = lines[index];
      const next = lines[index + 1];
      const timeMs = this.playing()
        ? Math.round(this.position())
        : next
          ? Math.round((line.timeMs + next.timeMs) / 2)
          : line.timeMs + 2000;
      this.change((all) => {
        all[index] = { ...line, text: line.text.slice(0, caret).trimEnd(), confidence: null };
        all.splice(index + 1, 0, {
          id: nextId++,
          timeMs,
          text: line.text.slice(caret).trimStart(),
          confidence: null,
        });
      });
      setTimeout(() => this.focus(index + 1, 0));
    } else if (event.key === 'Backspace' && caret === 0 && field.selectionEnd === 0 && index > 0) {
      // Join to the line before.
      event.preventDefault();
      const previous = lines[index - 1];
      const joint = previous.text.length + (previous.text && lines[index].text ? 1 : 0);
      this.change((all) => {
        all[index - 1] = {
          ...previous,
          text: [previous.text, lines[index].text].filter((t) => t).join(' '),
          confidence: null,
        };
        all.splice(index, 1);
      });
      setTimeout(() => this.focus(index - 1, joint));
    } else if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      const target = index + (event.key === 'ArrowUp' ? -1 : 1);
      if (target >= 0 && target < lines.length) this.focus(target, caret);
    } else if (event.altKey && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
      event.preventDefault();
      this.nudge(index, event.key === 'ArrowLeft' ? -100 : 100);
    }
  }

  /** Ctrl+Space plays or pauses anywhere; Space too outside the lines. */
  protected globalKey(event: KeyboardEvent): void {
    const inField =
      event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
    if (event.code === 'Space' && (event.ctrlKey || !inField)) {
      if (!inField && event.target instanceof HTMLButtonElement) return;
      event.preventDefault();
      this.toggle();
    }
  }

  protected nextDoubtful(): void {
    const lines = this.lines();
    const from = Math.max(0, this.current() + 1);
    const order = [...lines.keys()].slice(from).concat([...lines.keys()].slice(0, from));
    const index = order.find((i) => this.isUncertain(lines[i]));
    if (index === undefined) return;
    this.focus(index, lines[index].text.length);
    this.playFrom(lines[index]);
  }

  private focus(index: number, caret: number): void {
    const field = this.inputs()[index]?.nativeElement;
    if (!field) return;
    field.focus();
    field.setSelectionRange(caret, caret);
    this.rows()[index]?.nativeElement.scrollIntoView({ block: 'nearest' });
  }

  private change(apply: (lines: Line[]) => unknown): void {
    const copy = [...this.lines()];
    apply(copy);
    this.lines.set(copy);
    this.dirty.set(true);
    if (this.autosave) clearTimeout(this.autosave);
    this.autosave = setTimeout(() => void this.save(), AUTOSAVE_MS);
  }

  // Server.

  private async load(trackId: string): Promise<void> {
    this.lines.set([]);
    this.dirty.set(false);
    const result = await this.gql.query(ReviewQuery, { trackId });
    this.song.set(result.track);
    this.position.set(0);
    this.durationMs.set(result.track.durationMs);
    this.lines.set(
      (result.lyricsTranscription.draft?.lines ?? []).map((l) => ({
        id: nextId++,
        timeMs: l.timeMs,
        text: l.text,
        confidence: l.confidence ?? null,
      })),
    );
  }

  private payload(): { timeMs: number; text: string }[] {
    return this.lines().map((l) => ({ timeMs: Math.max(0, Math.round(l.timeMs)), text: l.text }));
  }

  private async save(): Promise<void> {
    if (this.autosave) clearTimeout(this.autosave);
    this.autosave = null;
    if (!this.dirty() || this.saving()) return;
    this.saving.set(true);
    try {
      this.dirty.set(false);
      await this.gql.mutate(SaveLyricsDraftMutation, {
        trackId: this.trackId(),
        lines: this.payload(),
      });
    } catch (error) {
      this.dirty.set(true);
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.saving.set(false);
    }
  }

  protected async accept(): Promise<void> {
    if (!this.lines().some((l) => l.text.trim())) {
      toast.error(this.i18n.t('lyricsReview.empty'));
      return;
    }
    this.busy.set(true);
    try {
      if (this.autosave) clearTimeout(this.autosave);
      await this.gql.mutate(AcceptLyricsDraftMutation, {
        trackId: this.trackId(),
        lines: this.payload(),
      });
      this.dirty.set(false);
      this.audio()?.nativeElement.pause();
      toast.success(this.i18n.t('lyricsReview.accepted'));
      this.done.emit();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async discard(): Promise<void> {
    if (!confirm(this.i18n.t('lyricsReview.discardConfirm'))) return;
    const again = confirm(this.i18n.t('lyricsReview.discardAgain'));
    try {
      if (this.autosave) clearTimeout(this.autosave);
      this.dirty.set(false);
      await this.gql.mutate(DiscardLyricsDraftMutation, { trackId: this.trackId(), again });
      this.audio()?.nativeElement.pause();
      this.done.emit();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }
}
