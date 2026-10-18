import { Component, computed, input, output } from '@angular/core';
import type { TrackAnalysisFieldsFragment as Analysis } from '../../graphql/generated/graphql';

/** One byte per value, from base64. */
export function bytes(base64: string | null | undefined): Uint8Array {
  if (!base64) return new Uint8Array();
  const text = atob(base64);
  const out = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) out[i] = text.charCodeAt(i);
  return out;
}

/** Float32 little-endian values, from base64. */
export function floats(base64: string | null | undefined): number[] {
  const raw = bytes(base64);
  const view = new DataView(raw.buffer);
  return Array.from({ length: Math.floor(raw.length / 4) }, (_, i) => view.getFloat32(i * 4, true));
}

const WIDTH = 1000;
const SECTION_COLORS: Record<string, string> = {
  intro: '#64748b',
  verse: '#0ea5e9',
  build: '#f59e0b',
  chorus: '#a855f7',
  drop: '#ef4444',
  break: '#14b8a6',
  bridge: '#84cc16',
  outro: '#64748b',
  section: '#94a3b8',
};

/**
 * A song's analysis drawn along its duration: sections (coloured, with their names), the energy as a waveform,
 * beats and downbeats, the voice, silences, the highlight, mix points and scratch points. A click anywhere asks to
 * hear that moment.
 */
@Component({
  selector: 'app-analysis-view',
  template: `
    <svg
      [attr.viewBox]="'0 0 ' + width + ' 170'"
      class="w-full cursor-crosshair select-none rounded-md border bg-muted/30"
      preserveAspectRatio="none"
      role="img"
      (click)="pick($event)"
    >
      <!-- Sections -->
      @for (s of sections(); track s.start) {
        <rect
          [attr.x]="x(s.start)"
          y="0"
          [attr.width]="x(s.end) - x(s.start)"
          height="18"
          [attr.fill]="s.color"
          opacity="0.85"
        />
        <text [attr.x]="x(s.start) + 3" y="13" font-size="10" fill="white">{{ s.label }}</text>
      }
      <!-- Highlight -->
      @if (analysis().highlight; as h) {
        <rect
          [attr.x]="x(h.start)"
          y="20"
          [attr.width]="x(h.end) - x(h.start)"
          height="110"
          fill="#facc15"
          opacity="0.15"
        />
      }
      <!-- Silences -->
      @for (s of analysis().silences ?? []; track s.start) {
        <rect
          [attr.x]="x(s.start)"
          y="20"
          [attr.width]="x(s.end) - x(s.start)"
          height="110"
          fill="currentColor"
          opacity="0.08"
        />
      }
      <!-- Energy -->
      <path [attr.d]="energy()" fill="#6366f1" opacity="0.55" />
      <!-- Beats and downbeats -->
      @for (b of beats(); track $index) {
        <line
          [attr.x1]="x(b.t)"
          [attr.x2]="x(b.t)"
          [attr.y1]="b.down ? 20 : 112"
          y2="130"
          [attr.stroke]="b.down ? '#0f172a' : '#64748b'"
          [attr.stroke-width]="b.down ? 0.8 : 0.4"
          opacity="0.6"
        />
      }
      <!-- Voice -->
      @for (v of vocals(); track $index) {
        <rect
          [attr.x]="x(v.t)"
          y="134"
          [attr.width]="x(v.step) + 0.5"
          height="10"
          fill="#ec4899"
          [attr.opacity]="v.value"
        />
      }
      <!-- Mix points: in (green) and out (red) -->
      @for (t of analysis().mixPoints?.mixIn ?? []; track t) {
        <polygon [attr.points]="arrow(t, true)" fill="#16a34a" />
      }
      @for (t of analysis().mixPoints?.mixOut ?? []; track t) {
        <polygon [attr.points]="arrow(t, false)" fill="#dc2626" />
      }
      <!-- Scratch points -->
      @for (c of analysis().scratchCues ?? []; track c.t) {
        <circle
          [attr.cx]="x(c.t)"
          cy="160"
          r="3.5"
          [attr.fill]="c.kind === 'vocal' ? '#ec4899' : c.kind === 'stab' ? '#a855f7' : '#0ea5e9'"
        >
          <title>{{ c.kind }} {{ c.t }}s</title>
        </circle>
      }
    </svg>
  `,
})
export class AnalysisView {
  readonly analysis = input.required<Analysis>();
  /** Seconds where the user clicked. */
  readonly picked = output<number>();

  protected readonly width = WIDTH;
  private readonly duration = computed(() =>
    Math.max(0.001, this.analysis().durationSeconds ?? 0.001),
  );

  protected x(seconds: number): number {
    return (seconds / this.duration()) * WIDTH;
  }

  protected readonly sections = computed(() =>
    (this.analysis().sections ?? []).map((s) => ({
      ...s,
      color: SECTION_COLORS[s.name] ?? SECTION_COLORS['section'],
      label: `${s.label} ${s.name}`,
    })),
  );

  protected readonly energy = computed(() => {
    const e = this.analysis().energy;
    const values = bytes(e?.values);
    if (!e || values.length === 0) return '';
    const points = Array.from(
      values,
      (v, i) => `${this.x(i * e.stepSeconds).toFixed(1)},${(130 - (v / 255) * 105).toFixed(1)}`,
    );
    return `M0,130 L${points.join(' L')} L${this.x(values.length * e.stepSeconds).toFixed(1)},130 Z`;
  });

  protected readonly beats = computed(() => {
    const tempo = this.analysis().tempo;
    if (!tempo) return [];
    const end = this.analysis().musicEnd ?? this.duration();
    let beats: number[];
    let downbeats: Set<number>;
    if (tempo.stable && tempo.firstBeat != null) {
      const step = 60 / tempo.bpm;
      beats = [];
      for (let t = tempo.firstBeat; t <= end; t += step) beats.push(t);
      const first = tempo.firstDownbeat ?? tempo.firstBeat;
      const phase = Math.round((first - tempo.firstBeat) / step);
      downbeats = new Set(
        beats.filter((_, i) => (i - phase) % tempo.beatsPerBar === 0 && i >= phase),
      );
    } else {
      beats = floats(tempo.beats);
      downbeats = new Set(floats(tempo.downbeats));
    }
    return beats.map((t) => ({ t, down: downbeats.has(t) }));
  });

  protected readonly vocals = computed(() => {
    const v = this.analysis().vocals;
    return Array.from(bytes(v?.presence), (value, i) => ({
      t: i * (v?.stepSeconds ?? 0.5),
      step: v?.stepSeconds ?? 0.5,
      value: value / 255,
    }));
  });

  protected arrow(t: number, inward: boolean): string {
    const x = this.x(t);
    return inward ? `${x},148 ${x + 6},154 ${x},160` : `${x},148 ${x - 6},154 ${x},160`;
  }

  protected pick(event: MouseEvent): void {
    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    this.picked.emit(((event.clientX - box.left) / box.width) * this.duration());
  }
}
