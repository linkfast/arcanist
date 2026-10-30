import { Component, computed, input, output } from '@angular/core';
import type { MusicVideoQuery } from '../../graphql/generated/graphql';

type Segment = NonNullable<MusicVideoQuery['musicVideo']['segments']>[number];

/**
 * Where a music video plays its song's audio (TRACK) and where its own (VIDEO), along its length, with the playing
 * position and, under the stretches, where each line of its lyrics starts. Clicking seeks there.
 */
@Component({
  selector: 'app-video-timeline',
  template: `
    <svg
      class="h-8 w-full cursor-pointer select-none"
      [attr.viewBox]="'0 0 1000 32'"
      preserveAspectRatio="none"
      role="img"
      (click)="click($event)"
    >
      @for (segment of bars(); track segment.x) {
        <rect
          [attr.x]="segment.x"
          y="6"
          [attr.width]="segment.width"
          height="20"
          rx="2"
          [class]="segment.track ? 'fill-primary' : 'fill-muted-foreground/30'"
        />
      }
      @for (mark of marks(); track $index) {
        <rect [attr.x]="mark" y="28" width="2" height="4" class="fill-foreground/60" />
      }
      <rect [attr.x]="cursor()" y="0" width="3" height="32" class="fill-foreground" />
    </svg>
  `,
})
export class VideoTimeline {
  readonly segments = input.required<Segment[]>();
  readonly durationMs = input.required<number>();
  readonly positionMs = input(0);
  /** Where the lyrics' lines start (ms of the video). */
  readonly lyricsMs = input<number[]>([]);
  readonly seek = output<number>();

  protected readonly bars = computed(() => {
    const total = Math.max(1, this.durationMs());
    return this.segments().map((s) => ({
      x: (s.videoStartMs / total) * 1000,
      width: Math.max(1, ((s.videoEndMs - s.videoStartMs) / total) * 1000 - 2),
      track: s.source === 'TRACK',
    }));
  });

  protected readonly marks = computed(() => {
    const total = Math.max(1, this.durationMs());
    return this.lyricsMs().map((ms) => (Math.min(ms, total) / total) * 998);
  });

  protected readonly cursor = computed(
    () => (Math.min(this.positionMs(), this.durationMs()) / Math.max(1, this.durationMs())) * 997,
  );

  protected click(event: MouseEvent): void {
    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    this.seek.emit(((event.clientX - box.left) / box.width) * this.durationMs());
  }
}
