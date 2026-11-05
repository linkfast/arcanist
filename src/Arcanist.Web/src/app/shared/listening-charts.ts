import { Component, computed, inject, input } from '@angular/core';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type {
  ListeningStatsFieldsFragment,
  StatsTrackFieldsFragment,
} from '../graphql/generated/graphql';

/** What was played in a period, of one user or the whole server: timeline, top lists, genres, hours and days. */
@Component({
  selector: 'app-listening-charts',
  imports: [TranslatePipe],
  template: `
    @let l = listening();
    @if (l.plays === 0) {
      <p class="text-sm text-muted-foreground">{{ 'stats.nothing' | t }}</p>
    } @else {
      <div class="grid gap-2">
        <p class="text-xs text-muted-foreground">
          {{ (l.bucket === 'DAY' ? 'stats.timeline' : 'stats.timelineMonths') | t }}
        </p>
        <div class="flex h-32 items-end gap-1" role="img" [attr.aria-label]="'stats.timeline' | t">
          @for (bucket of l.timeline; track bucket.start) {
            <div
              class="min-w-1 flex-1 rounded-t bg-primary/80"
              [style.height.%]="(bucket.plays / maxPlays()) * 100"
              [title]="bucket.start + ' · ' + bucket.plays"
            ></div>
          }
        </div>
      </div>

      <div class="grid gap-6 lg:grid-cols-3">
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.topTracks' | t }}</h3>
          <ol class="grid gap-1 text-sm">
            @for (item of l.topTracks; track item.track.id) {
              <li class="flex justify-between gap-2">
                <span class="min-w-0 truncate">
                  {{ item.track.title }}
                  <span class="text-muted-foreground">· {{ artists(item.track) }}</span>
                </span>
                <span class="shrink-0 text-muted-foreground">{{ item.plays }}</span>
              </li>
            }
          </ol>
        </div>
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.topArtists' | t }}</h3>
          <ol class="grid gap-1 text-sm">
            @for (item of l.topArtists; track item.artist.id) {
              <li class="flex justify-between gap-2">
                <span class="min-w-0 truncate">{{ item.artist.name }}</span>
                <span class="shrink-0 text-muted-foreground">{{ item.plays }}</span>
              </li>
            }
          </ol>
        </div>
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.topAlbums' | t }}</h3>
          <ol class="grid gap-1 text-sm">
            @for (item of l.topAlbums; track item.album.id) {
              <li class="flex items-center justify-between gap-2">
                <span class="flex min-w-0 items-center gap-2">
                  @if (item.album.artworkId) {
                    <img
                      class="size-8 shrink-0 rounded object-cover"
                      alt=""
                      [src]="'/rest/v1/artwork/' + item.album.artworkId + '?size=64'"
                    />
                  }
                  <span class="truncate">
                    {{ item.album.title }}
                    <span class="text-muted-foreground">· {{ item.album.artist.name }}</span>
                  </span>
                </span>
                <span class="shrink-0 text-muted-foreground">{{ item.plays }}</span>
              </li>
            }
          </ol>
        </div>
      </div>

      <div class="grid gap-6 lg:grid-cols-3">
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.topGenres' | t }}</h3>
          <ol class="grid gap-1 text-sm">
            @for (item of l.topGenres; track item.genre) {
              <li class="flex items-center gap-2">
                <span class="w-28 truncate">{{ item.genre }}</span>
                <div class="h-2 flex-1 rounded bg-muted">
                  <div
                    class="h-2 rounded bg-primary/70"
                    [style.width.%]="(item.plays / l.topGenres[0].plays) * 100"
                  ></div>
                </div>
                <span class="w-8 text-right text-muted-foreground">{{ item.plays }}</span>
              </li>
            } @empty {
              <li class="text-muted-foreground">{{ 'stats.noGenres' | t }}</li>
            }
          </ol>
        </div>
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.byHour' | t }}</h3>
          <div class="flex h-24 items-end gap-px" role="img" [attr.aria-label]="'stats.byHour' | t">
            @for (plays of l.playsByHour; track $index) {
              <div
                class="min-w-0.5 flex-1 rounded-t bg-primary/70"
                [style.height.%]="(plays / maxOf(l.playsByHour)) * 100"
                [title]="$index + ':00 · ' + plays"
              ></div>
            }
          </div>
          <div class="flex justify-between text-[10px] text-muted-foreground">
            <span>0</span><span>6</span><span>12</span><span>18</span><span>23</span>
          </div>
        </div>
        <div class="grid content-start gap-2">
          <h3 class="text-sm font-medium">{{ 'stats.byWeekday' | t }}</h3>
          <ol class="grid gap-1 text-sm">
            @for (day of weekdays(); track day.index) {
              <li class="flex items-center gap-2">
                <span class="w-10 text-muted-foreground">{{ day.name }}</span>
                <div class="h-2 flex-1 rounded bg-muted">
                  <div
                    class="h-2 rounded bg-primary/70"
                    [style.width.%]="(l.playsByWeekday[day.index] / maxOf(l.playsByWeekday)) * 100"
                  ></div>
                </div>
                <span class="w-8 text-right text-muted-foreground">{{
                  l.playsByWeekday[day.index]
                }}</span>
              </li>
            }
          </ol>
        </div>
      </div>
    }
  `,
  host: { class: 'contents' },
})
export class ListeningCharts {
  private readonly i18n = inject(I18n);

  readonly listening = input.required<ListeningStatsFieldsFragment>();

  protected readonly maxPlays = computed(() =>
    Math.max(1, ...this.listening().timeline.map((b) => b.plays)),
  );

  /** Monday first, as most of the world counts weeks. */
  protected readonly weekdays = computed(() => {
    const names = new Intl.DateTimeFormat(this.i18n.language(), { weekday: 'short' });
    return [1, 2, 3, 4, 5, 6, 0].map((index) => ({
      index,
      // 2023-01-01 was a Sunday.
      name: names.format(new Date(2023, 0, 1 + index)),
    }));
  });

  protected maxOf(values: readonly number[]): number {
    return Math.max(1, ...values);
  }

  protected artists(track: StatsTrackFieldsFragment): string {
    return track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }
}
