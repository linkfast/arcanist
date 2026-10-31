import { Component, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Gql } from '../../core/graphql/gql.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import type { LyricsDraftsQuery } from '../../graphql/generated/graphql';
import { LyricsDraftsQuery as DraftsQuery } from '../../graphql/operations';
import { DurationPipe, RelativeTimePipe } from '../../shared/format';
import { LyricsEditor } from './lyrics-editor';

type Draft = LyricsDraftsQuery['lyricsDrafts']['items'][number];

/**
 * Lyrics to review: what a model (the Lyrics Sync extension) recognized in songs without lyrics. Nobody sees them as
 * a song's lyrics until someone corrects and accepts them here. The list on the side, the editor of the one chosen.
 */
@Component({
  selector: 'app-lyrics-review-page',
  imports: [
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmSkeletonImports,
    TranslatePipe,
    DurationPipe,
    RelativeTimePipe,
    LyricsEditor,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'lyricsReview.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'lyricsReview.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6 lg:grid-cols-[18rem_1fr]">
      <aside
        class="grid content-start gap-2"
        [class.hidden]="selected()"
        [class.lg:grid]="true"
        data-tour="lyrics-list"
      >
        @if (drafts(); as list) {
          <p class="text-sm text-muted-foreground">
            {{ 'lyricsReview.count' | t: { count: total() } }}
          </p>
          @for (draft of list; track draft.trackId) {
            <a
              class="flex items-center gap-3 rounded-lg border p-2 text-left transition hover:bg-accent"
              [class.border-primary]="draft.trackId === selected()"
              [routerLink]="['/lyrics/review', draft.trackId]"
            >
              @if (draft.artworkId) {
                <img
                  class="size-10 shrink-0 rounded object-cover"
                  [src]="'/rest/v1/artwork/' + draft.artworkId + '?size=64'"
                  alt=""
                  loading="lazy"
                />
              } @else {
                <span class="size-10 shrink-0 rounded bg-muted"></span>
              }
              <span class="grid min-w-0 flex-1 text-sm">
                <span class="truncate font-medium">{{ draft.title }}</span>
                <span class="truncate text-xs text-muted-foreground"
                  >{{ draft.artist ?? '' }} · {{ draft.durationMs | duration }}</span
                >
                <span class="truncate text-xs text-muted-foreground">
                  {{ 'lyricsReview.lines' | t: { count: draft.lines } }} ·
                  {{ draft.createdAt | relativeTime }}
                </span>
              </span>
              @if (draft.uncertain > 0) {
                <span
                  hlmBadge
                  variant="outline"
                  class="shrink-0 border-amber-500/60 text-amber-600 dark:text-amber-400"
                >
                  {{ draft.uncertain }}
                </span>
              }
            </a>
          } @empty {
            <div
              class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
            >
              {{ 'lyricsReview.none' | t }}
            </div>
          }
        } @else {
          <div hlmSkeleton class="h-16"></div>
          <div hlmSkeleton class="h-16"></div>
        }
      </aside>

      <section class="min-w-0">
        @if (selected(); as id) {
          <a hlmBtn variant="ghost" size="sm" class="mb-2 lg:hidden" routerLink="/lyrics/review">
            {{ 'lyricsReview.back' | t }}
          </a>
          <app-lyrics-editor [trackId]="id" (done)="next(id)" />
        } @else {
          <div
            class="hidden rounded-lg border border-dashed p-10 text-center text-sm text-muted-foreground lg:block"
          >
            {{ 'lyricsReview.pick' | t }}
          </div>
        }
      </section>
    </div>
  `,
})
export class LyricsReviewPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly router = inject(Router);

  /** The song in the editor, from the address (the page stays the same as the song changes). */
  protected readonly selected = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => songOf(this.router.url)),
    ),
    { initialValue: songOf(this.router.url) },
  );
  protected readonly drafts = signal<Draft[] | null>(null);
  protected readonly total = signal(0);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  /** After accepting or discarding one: the list again, and the next one to review. */
  protected async next(done: string): Promise<void> {
    await this.reload();
    const following = this.drafts()?.find((d) => d.trackId !== done);
    await this.router.navigate(
      following ? ['/lyrics/review', following.trackId] : ['/lyrics/review'],
    );
  }

  private async reload(): Promise<void> {
    const result = (await this.gql.query(DraftsQuery, { first: 200 })).lyricsDrafts;
    this.drafts.set(result.items);
    this.total.set(result.totalCount);
  }
}

/** The song of a /lyrics/review/<id> address, or null for the list alone. */
function songOf(url: string): string | null {
  return /\/lyrics\/review\/([^/?#]+)/.exec(url)?.[1] ?? null;
}
