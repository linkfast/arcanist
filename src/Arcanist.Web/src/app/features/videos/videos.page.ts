import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { VideoClipFieldsFragment as Video } from '../../graphql/generated/graphql';
import { MusicVideosQuery } from '../../graphql/operations';
import { DurationPipe } from '../../shared/format';
import { VideoDetail } from './video-detail';

/**
 * Music videos: those the scans found in the libraries (next to their songs, or named after them) and the uploaded
 * ones, with whether they are linked to a song and play with its audio. Opening one plays it and lets managers fix
 * its link.
 */
@Component({
  selector: 'app-videos-page',
  imports: [
    FormsModule,
    BrnDialogContent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSkeletonImports,
    TranslatePipe,
    DurationPipe,
    VideoDetail,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'videos.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'videos.subtitle' | t }}</p>
    </header>

    <form class="mb-4 flex flex-wrap gap-2" (ngSubmit)="reload()">
      <input
        hlmInput
        name="search"
        class="min-w-0 flex-1 sm:max-w-sm"
        [(ngModel)]="search"
        [placeholder]="'videos.search' | t"
      />
      <select hlmNativeSelect name="linked" [(ngModel)]="linked" (ngModelChange)="reload()">
        <option value="">{{ 'videos.filters.all' | t }}</option>
        <option value="true">{{ 'videos.filters.linked' | t }}</option>
        <option value="false">{{ 'videos.filters.unlinked' | t }}</option>
      </select>
      <select hlmNativeSelect name="live" [(ngModel)]="live" (ngModelChange)="reload()">
        <option value="">{{ 'videos.filters.anyPerformance' | t }}</option>
        <option value="true">{{ 'videos.filters.live' | t }}</option>
        <option value="false">{{ 'videos.filters.studio' | t }}</option>
      </select>
      <button hlmBtn variant="outline" type="submit">{{ 'videos.find' | t }}</button>
    </form>

    @if (videos(); as list) {
      <p class="mb-3 text-sm text-muted-foreground">
        {{ 'videos.count' | t: { count: total() } }}
      </p>
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        @for (video of list; track video.id) {
          <button
            type="button"
            class="grid gap-2 overflow-hidden rounded-xl border text-left transition hover:bg-accent"
            (click)="open(video)"
          >
            <div class="relative aspect-video w-full bg-muted">
              @if (video.artworkId) {
                <img
                  class="h-full w-full object-cover"
                  [src]="'/rest/v1/artwork/' + video.artworkId + '?size=512'"
                  alt=""
                  loading="lazy"
                />
              }
              <span class="absolute bottom-2 right-2 rounded bg-black/70 px-1.5 text-xs text-white">
                {{ video.durationMs | duration }}
              </span>
            </div>
            <div class="grid min-w-0 gap-1 px-3 pb-3">
              <p class="truncate font-medium">{{ video.title }}</p>
              <p class="truncate text-sm text-muted-foreground">
                {{ video.artist ?? ('videos.unknownArtist' | t) }}
              </p>
              <div class="flex flex-wrap gap-1">
                <span hlmBadge [variant]="video.sync === 'READY' ? 'default' : 'secondary'">
                  {{ 'videos.sync.' + video.sync | t }}
                </span>
                @if (video.isLive) {
                  <span hlmBadge variant="outline">{{ 'videos.liveBadge' | t }}</span>
                }
                @if (!video.trackId) {
                  <span hlmBadge variant="outline">{{ 'videos.link.none' | t }}</span>
                }
              </div>
            </div>
          </button>
        } @empty {
          <section hlmCard class="sm:col-span-2 xl:col-span-3">
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'videos.none' | t }}</h2>
              <p hlmCardDescription>{{ 'videos.noneHelp' | t }}</p>
            </div>
          </section>
        }
      </div>
      @if (next()) {
        <div class="mt-4 flex justify-center">
          <button hlmBtn variant="outline" type="button" (click)="more()">
            {{ 'videos.more' | t }}
          </button>
        </div>
      }
    } @else {
      <hlm-skeleton class="h-40 w-full" />
    }

    <hlm-dialog [state]="selected() ? 'open' : 'closed'" (closed)="selected.set(null)">
      <hlm-dialog-content *brnDialogContent="let ctx" class="w-[min(56rem,calc(100vw-2rem))]">
        @if (selected(); as video) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ video.title }}</h3>
            <p hlmDialogDescription>{{ video.artist ?? ('videos.unknownArtist' | t) }}</p>
          </hlm-dialog-header>
          <app-video-detail
            [videoId]="video.id"
            (changed)="reload()"
            (deleted)="selected.set(null); reload()"
          />
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class VideosPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly videos = signal<Video[] | null>(null);
  protected readonly total = signal(0);
  protected readonly next = signal<string | null>(null);
  protected readonly selected = signal<Video | null>(null);
  protected search = '';
  protected linked = '';
  protected live = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected open(video: Video): void {
    this.selected.set(video);
  }

  protected async reload(): Promise<void> {
    await this.load(null);
  }

  protected async more(): Promise<void> {
    await this.load(this.next());
  }

  private async load(after: string | null): Promise<void> {
    try {
      const page = (
        await this.gql.query(MusicVideosQuery, {
          input: {
            search: this.search.trim() || null,
            linked: this.linked === '' ? null : this.linked === 'true',
            live: this.live === '' ? null : this.live === 'true',
            after,
            first: 48,
          },
        })
      ).musicVideos;
      this.videos.update((current) =>
        after && current ? [...current, ...page.items] : page.items,
      );
      this.total.set(page.totalCount);
      this.next.set(page.nextCursor ?? null);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.videos.update((current) => current ?? []);
    }
  }
}
