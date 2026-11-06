import { Component, OnInit, inject, signal } from '@angular/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { TrackCopiesQuery } from '../../graphql/generated/graphql';
import { DeleteTrackFileMutation, TrackCopiesQuery as Query } from '../../graphql/operations';
import { BytesPipe, DurationPipe } from '../../shared/format';

type Song = TrackCopiesQuery['trackCopies']['items'][number];
type File = Song['track']['files'][number];

/**
 * Songs with several files of their own in a library (a FLAC and an MP3, say), to keep the ones wanted. What Arcanist
 * converts to play a song is not a file of it, so it never shows here.
 */
@Component({
  selector: 'app-track-copies',
  imports: [
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    TranslatePipe,
    BytesPipe,
    DurationPipe,
  ],
  template: `
    <div class="grid gap-4">
      <p class="text-sm text-muted-foreground">{{ 'duplicates.copies.subtitle' | t }}</p>

      @if (songs(); as list) {
        @if (list.length) {
          <p class="text-sm font-medium">{{ 'duplicates.copies.count' | t: { count: total() } }}</p>
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
            <ul hlmCardContent class="grid gap-2 sm:grid-cols-2">
              @for (file of own(song); track file.id) {
                <li class="grid min-w-0 content-start gap-1 rounded-lg border p-3 text-xs">
                  <p class="flex flex-wrap items-center gap-1">
                    <span class="font-medium">{{ quality(file) }}</span>
                    <span class="text-muted-foreground">{{ file.size | bytes }}</span>
                    @if (best(song) === file.id) {
                      <span hlmBadge variant="secondary">{{ 'duplicates.albums.best' | t }}</span>
                    }
                  </p>
                  <p class="break-all text-muted-foreground">{{ file.path }}</p>
                  @if (song.deletableFileIds.includes(file.id)) {
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      type="button"
                      class="mt-1 justify-self-start"
                      [disabled]="busy() === file.id"
                      (click)="confirming.set({ song, file })"
                    >
                      {{ 'duplicates.copies.delete' | t }}
                    </button>
                  } @else {
                    <p class="text-muted-foreground">{{ 'duplicates.libraries.readOnly' | t }}</p>
                  }
                </li>
              }
            </ul>
          </section>
        } @empty {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'duplicates.copies.none' | t }}</h2>
              <p hlmCardDescription>{{ 'duplicates.copies.noneHelp' | t }}</p>
            </div>
          </section>
        }
        @if (cursor()) {
          <button
            hlmBtn
            variant="outline"
            type="button"
            class="justify-self-center"
            (click)="load(cursor())"
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
            <h3 hlmAlertDialogTitle>{{ 'duplicates.copies.confirmTitle' | t }}</h3>
            <p hlmAlertDialogDescription>
              {{ 'duplicates.copies.confirmBody' | t: { quality: quality(c.file) } }}
            </p>
            <p class="break-all text-sm text-muted-foreground">{{ c.file.path }}</p>
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="confirming.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmAlertDialogAction type="button" (click)="delete(c.song, c.file)">
              {{ 'duplicates.copies.delete' | t }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class TrackCopies implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly songs = signal<Song[] | null>(null);
  protected readonly total = signal(0);
  protected readonly cursor = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly confirming = signal<{ song: Song; file: File } | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load(null);
  }

  /** Its own files: one it only plays is another track's, to be decided there. */
  protected own(song: Song): File[] {
    return song.track.files.filter((f) => !f.shared && f.cueTrack === 0);
  }

  protected artists(song: Song): string {
    return song.track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected quality(file: File): string {
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

  /** The best of the song's files, when one is better than the rest. */
  protected best(song: Song): string | null {
    const compare = (a: File, b: File) =>
      (b.bitDepth ?? 0) - (a.bitDepth ?? 0) ||
      (b.sampleRate ?? 0) - (a.sampleRate ?? 0) ||
      (b.bitRate ?? 0) - (a.bitRate ?? 0);
    const sorted = [...this.own(song)].sort(compare);
    return sorted.length > 1 && compare(sorted[0], sorted[1]) !== 0 ? sorted[0].id : null;
  }

  protected async delete(song: Song, file: File): Promise<void> {
    this.confirming.set(null);
    this.busy.set(file.id);
    try {
      await this.gql.mutate(DeleteTrackFileMutation, { fileId: file.id });
      toast.success(this.i18n.t('duplicates.copies.deleted'));
      await this.load(null);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(null);
    }
  }

  protected async load(after: string | null): Promise<void> {
    const page = (await this.gql.query(Query, { input: { after, first: 30 } })).trackCopies;
    this.songs.update((list) => (after ? [...(list ?? []), ...page.items] : page.items));
    this.total.set(page.totalCount);
    this.cursor.set(page.nextCursor ?? null);
  }
}
