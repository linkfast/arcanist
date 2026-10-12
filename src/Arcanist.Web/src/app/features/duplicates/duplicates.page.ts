import { Component, OnInit, inject, signal } from '@angular/core';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { toast } from '@spartan-ng/brain/sonner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  DuplicateCandidatesQuery as Candidates,
  DuplicateTrackFieldsFragment as DuplicateTrack,
} from '../../graphql/generated/graphql';
import {
  DismissDuplicateMutation,
  DuplicateCandidatesQuery,
  MergeDuplicateMutation,
} from '../../graphql/operations';
import { DurationPipe } from '../../shared/format';

type Pair = Candidates['duplicateCandidates'][number];

/** Pairs of tracks that may be the same audio: keep one (merging the copies) or keep both. */
@Component({
  selector: 'app-duplicates-page',
  imports: [
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmSkeletonImports,
    TranslatePipe,
    DurationPipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'duplicates.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'duplicates.subtitle' | t }}</p>
    </header>

    @if (pairs(); as list) {
      <div class="grid gap-4">
        @for (pair of list; track pair.id) {
          <section hlmCard>
            <div hlmCardHeader>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <span hlmBadge variant="secondary">
                  {{ 'duplicates.similarity' | t: { percent: percent(pair.similarity) } }}
                </span>
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  type="button"
                  [disabled]="busy() === pair.id"
                  (click)="dismiss(pair)"
                >
                  {{ 'duplicates.keepBoth' | t }}
                </button>
              </div>
            </div>
            <div hlmCardContent class="grid gap-3 sm:grid-cols-2">
              @for (track of [pair.trackA, pair.trackB]; track track.id) {
                <div class="grid min-w-0 gap-2 rounded-lg border p-3 text-sm">
                  <div class="min-w-0">
                    <p class="truncate font-medium">{{ track.title }}</p>
                    <p class="truncate text-muted-foreground">
                      {{ artists(track) }} · {{ track.album.title }}
                    </p>
                  </div>
                  <p class="text-xs text-muted-foreground">
                    @if (track.trackNumber) {
                      {{ 'duplicates.number' | t: { number: track.trackNumber } }} ·
                    }
                    {{ track.durationMs | duration }} · {{ formats(track) }}
                  </p>
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    type="button"
                    class="justify-self-start"
                    [disabled]="busy() === pair.id"
                    (click)="merge(pair, track.id)"
                  >
                    {{ 'duplicates.keepThis' | t }}
                  </button>
                </div>
              }
            </div>
          </section>
        } @empty {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'duplicates.none' | t }}</h2>
              <p hlmCardDescription>{{ 'duplicates.noneHelp' | t }}</p>
            </div>
          </section>
        }
      </div>
    } @else {
      <hlm-skeleton class="h-40 w-full" />
    }
  `,
})
export class DuplicatesPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly pairs = signal<Pair[] | null>(null);
  protected readonly busy = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected percent(similarity: number): number {
    return Math.round(similarity * 100);
  }

  protected artists(track: DuplicateTrack): string {
    return track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected formats(track: DuplicateTrack): string {
    return track.files
      .map((f) => [f.format, f.bitRate ? `${f.bitRate} kbit/s` : null].filter(Boolean).join(' '))
      .join(' + ');
  }

  protected async merge(pair: Pair, keepTrackId: string): Promise<void> {
    await this.run(pair, async () => {
      await this.gql.mutate(MergeDuplicateMutation, { id: pair.id, keepTrackId });
      toast.success(this.i18n.t('duplicates.merged'));
    });
  }

  protected async dismiss(pair: Pair): Promise<void> {
    await this.run(pair, async () => {
      await this.gql.mutate(DismissDuplicateMutation, { id: pair.id });
    });
  }

  private async run(pair: Pair, action: () => Promise<void>): Promise<void> {
    this.busy.set(pair.id);
    try {
      await action();
      this.pairs.update((list) => list?.filter((p) => p.id !== pair.id) ?? null);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(null);
    }
  }

  private async load(): Promise<void> {
    this.pairs.set((await this.gql.query(DuplicateCandidatesQuery, {})).duplicateCandidates);
  }
}
