import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
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
  LibrariesQuery,
  MergeDuplicateMutation,
  RescanDuplicatesMutation,
  ShareDuplicateFileMutation,
} from '../../graphql/operations';
import { DurationPipe } from '../../shared/format';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { LibraryDuplicates } from './library-duplicates';
import { TrackCopies } from './track-copies';

type Pair = Candidates['duplicateCandidates'][number];
type DuplicateFile = DuplicateTrack['files'][number];

/** A file of a pair being chosen to play on both albums. */
interface Sharing {
  pair: Pair;
  file: DuplicateFile;
  owner: DuplicateTrack;
  other: DuplicateTrack;
}

const PageSize = 100;

/**
 * Songs that may be the same. On two albums: keep both copies, or one file for both albums. On one album: keep one
 * (merging the copies) or keep both.
 */
@Component({
  selector: 'app-duplicates-page',
  imports: [
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmSkeletonImports,
    HlmTabsImports,
    TranslatePipe,
    DurationPipe,
    LibraryDuplicates,
    TrackCopies,
  ],
  template: `
    <header class="mb-4">
      <h1 class="text-2xl font-semibold">{{ 'duplicates.title' | t }}</h1>
    </header>

    <hlm-tabs class="mb-4" [tab]="tab()" (tabActivated)="tab.set($any($event))">
      <hlm-tabs-list data-tour="duplicates-tabs">
        <button hlmTabsTrigger="albums">
          {{ 'duplicates.tabs.albums' | t }}
          @if (albumPairs().length) {
            <span class="ml-1 text-xs opacity-70">{{ albumPairs().length }}</span>
          }
        </button>
        <button hlmTabsTrigger="possible">
          {{ 'duplicates.tabs.possible' | t }}
          @if (samePairs().length) {
            <span class="ml-1 text-xs opacity-70">{{ samePairs().length }}</span>
          }
        </button>
        <button hlmTabsTrigger="copies">{{ 'duplicates.tabs.copies' | t }}</button>
        <button hlmTabsTrigger="libraries">{{ 'duplicates.tabs.libraries' | t }}</button>
      </hlm-tabs-list>
    </hlm-tabs>

    @if (tab() === 'copies') {
      <app-track-copies />
    } @else if (tab() === 'libraries') {
      <app-library-duplicates [libraries]="libraries()" />
    } @else {
      <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
        <p class="min-w-0 flex-1 text-sm text-muted-foreground">
          {{ (tab() === 'albums' ? 'duplicates.albums.subtitle' : 'duplicates.subtitle') | t }}
        </p>
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          data-tour="duplicates-rescan"
          [disabled]="rescanning()"
          (click)="rescan()"
        >
          {{ 'duplicates.rescan' | t }}
        </button>
      </div>
      @if (pairs()) {
        <div class="grid gap-4" data-tour="duplicates-list">
          @for (pair of tab() === 'albums' ? albumPairs() : samePairs(); track pair.id) {
            <section hlmCard>
              <div hlmCardHeader>
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <span hlmBadge variant="secondary">
                    @if (pair.similarity > 0) {
                      {{ 'duplicates.similarity' | t: { percent: percent(pair.similarity) } }}
                    } @else {
                      {{ 'duplicates.byName' | t }}
                    }
                  </span>
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    type="button"
                    [disabled]="busy() === pair.id"
                    (click)="dismiss(pair)"
                  >
                    {{
                      (tab() === 'albums' ? 'duplicates.albums.keepBoth' : 'duplicates.keepBoth')
                        | t
                    }}
                  </button>
                </div>
              </div>
              <div hlmCardContent class="grid gap-3">
                <div class="grid gap-3 sm:grid-cols-2">
                  @for (track of [pair.trackA, pair.trackB]; track track.id) {
                    <div class="grid min-w-0 content-start gap-2 rounded-lg border p-3 text-sm">
                      <div class="min-w-0">
                        <p class="truncate font-medium">{{ track.album.title }}</p>
                        <p class="truncate text-muted-foreground">
                          {{ track.title }} · {{ artists(track) }}
                        </p>
                      </div>
                      <p class="text-xs text-muted-foreground">
                        @if (track.trackNumber) {
                          {{ 'duplicates.number' | t: { number: track.trackNumber } }} ·
                        }
                        {{ track.durationMs | duration }}
                      </p>
                      @if (libraryNames(track); as names) {
                        <p class="text-xs text-muted-foreground">{{ names }}</p>
                      }
                      <ul class="grid gap-2">
                        @for (file of track.files; track file.id) {
                          <li class="grid min-w-0 gap-1 rounded-md bg-muted/50 p-2 text-xs">
                            <p class="flex flex-wrap items-center gap-1">
                              <span class="font-medium">{{ quality(file) }}</span>
                              @if (tab() === 'albums' && best(pair) === file.id) {
                                <span hlmBadge variant="secondary">{{
                                  'duplicates.albums.best' | t
                                }}</span>
                              }
                              @if (file.shared) {
                                <span hlmBadge variant="outline">{{
                                  'duplicates.albums.sharedBadge' | t
                                }}</span>
                              }
                            </p>
                            <p class="break-all text-muted-foreground" [title]="file.path">
                              {{ file.path }}
                            </p>
                            @if (tab() === 'albums' && pair.shareableFileIds.includes(file.id)) {
                              <button
                                hlmBtn
                                variant="outline"
                                size="sm"
                                type="button"
                                class="mt-1 h-auto justify-self-start whitespace-normal py-1 text-left"
                                [disabled]="busy() === pair.id"
                                (click)="askShare(pair, track, file)"
                              >
                                {{ 'duplicates.albums.useThis' | t }}
                              </button>
                            }
                          </li>
                        }
                      </ul>
                      @if (tab() !== 'albums') {
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
                      }
                    </div>
                  }
                </div>
                @if (tab() === 'albums' && pair.shareBlocked) {
                  <p class="text-xs text-muted-foreground">
                    {{ 'duplicates.albums.blocked.' + pair.shareBlocked | t }}
                  </p>
                }
              </div>
            </section>
          } @empty {
            <section hlmCard>
              <div hlmCardHeader>
                <h2 hlmCardTitle>
                  {{ (tab() === 'albums' ? 'duplicates.albums.none' : 'duplicates.none') | t }}
                </h2>
                <p hlmCardDescription>
                  {{
                    (tab() === 'albums' ? 'duplicates.albums.noneHelp' : 'duplicates.noneHelp') | t
                  }}
                </p>
              </div>
            </section>
          }
          @if (hasMore()) {
            <button
              hlmBtn
              variant="outline"
              type="button"
              class="justify-self-center"
              (click)="load(true)"
            >
              {{ 'duplicates.libraries.more' | t }}
            </button>
          }
        </div>
      } @else {
        <hlm-skeleton class="h-40 w-full" />
      }
    }

    <hlm-alert-dialog [state]="sharing() ? 'open' : 'closed'" (closed)="sharing.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (sharing(); as choice) {
          <hlm-alert-dialog-header>
            <h3 hlmAlertDialogTitle>{{ 'duplicates.albums.confirmTitle' | t }}</h3>
            <p hlmAlertDialogDescription>
              {{
                'duplicates.albums.confirmBody'
                  | t
                    : {
                        quality: quality(choice.file),
                        album: choice.owner.album.title,
                        other: choice.other.album.title,
                      }
              }}
            </p>
            <p class="text-sm text-muted-foreground">
              {{ 'duplicates.albums.confirmHelp' | t: { other: choice.other.album.title } }}
            </p>
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="sharing.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmAlertDialogAction type="button" (click)="share(choice)">
              {{ 'duplicates.albums.confirm' | t }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class DuplicatesPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly pairs = signal<Pair[] | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly rescanning = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly sharing = signal<Sharing | null>(null);
  protected readonly tab = signal<'albums' | 'possible' | 'copies' | 'libraries'>('albums');
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);

  protected readonly albumPairs = computed(() =>
    (this.pairs() ?? []).filter((p) => p.trackA.album.id !== p.trackB.album.id),
  );
  protected readonly samePairs = computed(() =>
    (this.pairs() ?? []).filter((p) => p.trackA.album.id === p.trackB.album.id),
  );

  async ngOnInit(): Promise<void> {
    const [, libraries] = await Promise.all([this.load(), this.gql.query(LibrariesQuery)]);
    this.libraries.set(libraries.libraries.filter((l) => !l.personal));
  }

  /** The libraries a track's copies are in (for pairs whose tracks live in different libraries). */
  protected libraryNames(track: DuplicateTrack): string {
    const ids = new Set(track.files.map((f) => f.libraryId));
    return this.libraries()
      .filter((l) => ids.has(l.id))
      .map((l) => l.name)
      .join(' · ');
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

  protected quality(file: DuplicateFile): string {
    return [
      file.format,
      file.bitDepth && file.sampleRate
        ? `${file.bitDepth} bit / ${file.sampleRate / 1000} kHz`
        : null,
      file.bitRate ? `${file.bitRate} kbit/s` : null,
    ]
      .filter(Boolean)
      .join(' · ');
  }

  /** The file to suggest keeping: the highest quality of those that can stay. */
  protected best(pair: Pair): string | null {
    const score = (f: DuplicateFile) => [f.bitDepth ?? 0, f.sampleRate ?? 0, f.bitRate ?? 0];
    const files = [...pair.trackA.files, ...pair.trackB.files].filter((f) =>
      pair.shareableFileIds.includes(f.id),
    );
    const compare = (a: DuplicateFile, b: DuplicateFile) => {
      const [x, y] = [score(a), score(b)];
      return y[0] - x[0] || y[1] - x[1] || y[2] - x[2];
    };
    const sorted = files.sort(compare);
    // With the same quality there is nothing to suggest.
    return sorted.length > 1 && compare(sorted[0], sorted[1]) !== 0 ? sorted[0].id : null;
  }

  protected askShare(pair: Pair, owner: DuplicateTrack, file: DuplicateFile): void {
    const other = owner.id === pair.trackA.id ? pair.trackB : pair.trackA;
    this.sharing.set({ pair, file, owner, other });
  }

  protected async share(choice: Sharing): Promise<void> {
    this.sharing.set(null);
    await this.run(choice.pair, async () => {
      await this.gql.mutate(ShareDuplicateFileMutation, {
        id: choice.pair.id,
        fileId: choice.file.id,
      });
      toast.success(this.i18n.t('duplicates.albums.shared'));
    });
  }

  protected async rescan(): Promise<void> {
    this.rescanning.set(true);
    try {
      await this.gql.mutate(RescanDuplicatesMutation);
      toast.info(this.i18n.t('duplicates.rescanStarted'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.rescanning.set(false);
    }
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

  protected async load(more = false): Promise<void> {
    const loaded = more ? (this.pairs() ?? []) : [];
    const page = (await this.gql.query(DuplicateCandidatesQuery, { skip: loaded.length }))
      .duplicateCandidates;
    this.pairs.set([...loaded, ...page]);
    this.hasMore.set(page.length === PageSize);
  }
}
