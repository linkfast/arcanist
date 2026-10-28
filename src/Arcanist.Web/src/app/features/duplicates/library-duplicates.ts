import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { LibraryDuplicatesQuery } from '../../graphql/generated/graphql';
import {
  DeleteTrackCopiesMutation,
  LibraryDuplicatesQuery as Query,
} from '../../graphql/operations';
import { BytesPipe, DurationPipe } from '../../shared/format';

type Song = LibraryDuplicatesQuery['libraryDuplicates']['items'][number];
type Library = Song['libraries'][number];

/**
 * Songs that more than one library has: each library's copies, and whether deleting them would leave anyone without
 * the song (users who listen to that library but to none of the others that have it).
 */
@Component({
  selector: 'app-library-duplicates',
  imports: [
    FormsModule,
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmNativeSelectImports,
    HlmSkeletonImports,
    TranslatePipe,
    BytesPipe,
    DurationPipe,
  ],
  template: `
    <div class="grid gap-4">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <p class="text-sm text-muted-foreground">{{ 'duplicates.libraries.subtitle' | t }}</p>
        <select
          hlmNativeSelect
          name="library"
          class="w-auto"
          [ngModel]="libraryId()"
          (ngModelChange)="filter($event)"
        >
          <option value="">{{ 'duplicates.libraries.all' | t }}</option>
          @for (library of libraries(); track library.id) {
            <option [value]="library.id">{{ library.name }}</option>
          }
        </select>
      </div>

      @if (songs(); as list) {
        @if (list.length) {
          <p class="text-sm font-medium">
            {{ 'duplicates.libraries.count' | t: { count: total() } }}
          </p>
        }
        @for (song of list; track song.track.id) {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle class="truncate">{{ song.track.title }}</h2>
              <p hlmCardDescription class="truncate">
                {{ artists(song) }} · {{ song.track.album.title }} ·
                {{ song.track.durationMs | duration }}
              </p>
            </div>
            <div hlmCardContent class="grid gap-3 sm:grid-cols-2">
              @for (library of song.libraries; track library.id) {
                <div class="grid min-w-0 content-start gap-2 rounded-lg border p-3 text-sm">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="font-medium">{{ library.name }}</span>
                    @if (library.visibleToAll) {
                      <span hlmBadge variant="secondary">{{
                        'duplicates.libraries.everyone' | t
                      }}</span>
                    }
                  </div>
                  @for (file of filesIn(song, library); track file.id) {
                    <p class="break-all font-mono text-xs text-muted-foreground">
                      {{ file.path }} · {{ describe(file) }} · {{ file.size | bytes }}
                    </p>
                  }
                  @if (library.losingUsers.length) {
                    <p class="text-xs text-amber-700 dark:text-amber-400">
                      {{ 'duplicates.libraries.losing' | t: { users: names(library) } }}
                    </p>
                  } @else {
                    <p class="text-xs text-muted-foreground">
                      {{ 'duplicates.libraries.safe' | t: { library: library.name } }}
                    </p>
                  }
                  @if (library.canDelete) {
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      type="button"
                      class="justify-self-start"
                      [disabled]="busy() === song.track.id"
                      (click)="ask(song, library)"
                    >
                      {{ 'duplicates.libraries.delete' | t: { library: library.name } }}
                    </button>
                  } @else {
                    <p class="text-xs text-muted-foreground">
                      {{ 'duplicates.libraries.readOnly' | t }}
                    </p>
                  }
                </div>
              }
            </div>
          </section>
        } @empty {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'duplicates.libraries.none' | t }}</h2>
              <p hlmCardDescription>{{ 'duplicates.libraries.noneHelp' | t }}</p>
            </div>
          </section>
        }
        @if (cursor()) {
          <button
            hlmBtn
            variant="outline"
            type="button"
            class="justify-self-center"
            (click)="more()"
          >
            {{ 'duplicates.libraries.more' | t }}
          </button>
        }
      } @else {
        <hlm-skeleton class="h-40 w-full" />
      }
    </div>

    <hlm-alert-dialog [state]="confirming() ? 'open' : 'closed'" (closed)="confirming.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (confirming(); as c) {
          <hlm-alert-dialog-header>
            <h3 hlmAlertDialogTitle>
              {{ 'duplicates.libraries.confirmTitle' | t: { library: c.library.name } }}
            </h3>
            <p hlmAlertDialogDescription>
              @if (c.library.losingUsers.length) {
                {{
                  'duplicates.libraries.confirmLosing'
                    | t: { users: names(c.library), others: others(c.song, c.library) }
                }}
              } @else {
                {{ 'duplicates.libraries.confirmSafe' | t: { others: others(c.song, c.library) } }}
              }
            </p>
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="confirming.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmAlertDialogAction type="button" (click)="delete(c.song, c.library)">
              {{ 'duplicates.libraries.delete' | t: { library: c.library.name } }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class LibraryDuplicates implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  /** The libraries to filter by (those of the server, personal ones aside). */
  readonly libraries = input<{ id: string; name: string }[]>([]);

  protected readonly songs = signal<Song[] | null>(null);
  protected readonly total = signal(0);
  protected readonly cursor = signal<string | null>(null);
  protected readonly libraryId = signal('');
  protected readonly busy = signal<string | null>(null);
  protected readonly confirming = signal<{ song: Song; library: Library } | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load(null);
  }

  protected filter(libraryId: string): void {
    this.libraryId.set(libraryId);
    this.songs.set(null);
    void this.load(null);
  }

  protected more(): Promise<void> {
    return this.load(this.cursor());
  }

  protected artists(song: Song): string {
    return song.track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected filesIn(song: Song, library: Library) {
    return song.track.files.filter((f) => f.libraryId === library.id);
  }

  protected describe(file: Song['track']['files'][number]): string {
    return [file.format, file.bitRate ? `${file.bitRate} kbit/s` : null].filter(Boolean).join(' ');
  }

  /** "Ana, Bruno and Carla", in the panel's language. */
  protected names(library: Library): string {
    return new Intl.ListFormat(this.i18n.language(), { type: 'conjunction' }).format(
      library.losingUsers.map((u) => u.name),
    );
  }

  protected others(song: Song, library: Library): string {
    return new Intl.ListFormat(this.i18n.language(), { type: 'conjunction' }).format(
      song.libraries.filter((l) => l.id !== library.id).map((l) => l.name),
    );
  }

  protected ask(song: Song, library: Library): void {
    this.confirming.set({ song, library });
  }

  protected async delete(song: Song, library: Library): Promise<void> {
    this.confirming.set(null);
    this.busy.set(song.track.id);
    try {
      await this.gql.mutate(DeleteTrackCopiesMutation, {
        id: song.track.id,
        libraryId: library.id,
      });
      toast.success(this.i18n.t('duplicates.libraries.deleted', { library: library.name }));
      // With two libraries it is no longer a duplicate; with more, the others still are.
      this.songs.update((list) =>
        (list ?? []).flatMap((s) => {
          if (s.track.id !== song.track.id) return [s];
          const left = s.libraries.filter((l) => l.id !== library.id);
          return left.length > 1 ? [{ ...s, libraries: left }] : [];
        }),
      );
      if (song.libraries.length <= 2) this.total.update((n) => Math.max(0, n - 1));
      // Who loses what changes with the libraries left: read it again.
      if (song.libraries.length > 2) await this.load(null);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(null);
    }
  }

  private async load(after: string | null): Promise<void> {
    const page = (
      await this.gql.query(Query, {
        input: { libraryId: this.libraryId() || null, after, first: 30 },
      })
    ).libraryDuplicates;
    this.songs.update((list) => (after ? [...(list ?? []), ...page.items] : page.items));
    this.total.set(page.totalCount);
    this.cursor.set(page.nextCursor ?? null);
  }
}
