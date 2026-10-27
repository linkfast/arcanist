import {
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  AiTrackSearchQuery as SearchResult,
  MusicVideoQuery,
  VideoAudioMode,
} from '../../graphql/generated/graphql';
import {
  AiTrackSearchQuery,
  DeleteMusicVideoMutation,
  LinkMusicVideoMutation,
  MusicVideoQuery as VideoQuery,
  RealignMusicVideoMutation,
  SetMusicVideoLiveMutation,
  UnlockMusicVideoMutation,
} from '../../graphql/operations';
import { DurationPipe } from '../../shared/format';
import { VideoTimeline } from './video-timeline';

type Video = MusicVideoQuery['musicVideo'];
type Song = SearchResult['tracks']['items'][number];

/**
 * A music video: plays it with its song's audio or its own (switching keeps the position: both have the same
 * timeline), shows where each audio plays, and lets managers link it to a song, mark it live, align it again or
 * delete it.
 */
@Component({
  selector: 'app-video-detail',
  imports: [
    FormsModule,
    HlmBadgeImports,
    HlmButtonImports,
    HlmInputImports,
    HlmSwitchImports,
    TranslatePipe,
    DurationPipe,
    VideoTimeline,
  ],
  template: `
    @if (video(); as v) {
      <div class="grid gap-4">
        <video
          #player
          class="aspect-video w-full rounded-lg bg-black"
          controls
          preload="metadata"
          [src]="source()"
          (timeupdate)="position.set(player.currentTime * 1000)"
          (loadedmetadata)="resume()"
        ></video>

        <div class="flex flex-wrap items-center gap-2">
          <span class="text-sm text-muted-foreground">{{ 'videos.audio.label' | t }}</span>
          <button
            hlmBtn
            size="sm"
            type="button"
            [variant]="audio() === 'TRACK' ? 'default' : 'outline'"
            [disabled]="v.sync !== 'READY'"
            (click)="switchAudio('TRACK')"
          >
            {{ 'videos.audio.TRACK' | t }}
          </button>
          <button
            hlmBtn
            size="sm"
            type="button"
            [variant]="audio() === 'VIDEO' ? 'default' : 'outline'"
            (click)="switchAudio('VIDEO')"
          >
            {{ 'videos.audio.VIDEO' | t }}
          </button>
          <span hlmBadge variant="secondary">{{ 'videos.sync.' + v.sync | t }}</span>
          @if (v.coverage !== null && v.coverage !== undefined) {
            <span class="text-xs text-muted-foreground">
              {{ 'videos.coverage' | t: { percent: percent(v.coverage) } }}
            </span>
          }
        </div>

        @if (v.segments?.length) {
          <div class="grid gap-1">
            <app-video-timeline
              [segments]="v.segments!"
              [durationMs]="v.durationMs"
              [positionMs]="position()"
              (seek)="seekTo($event)"
            />
            <p class="text-xs text-muted-foreground">{{ 'videos.timelineHelp' | t }}</p>
          </div>
        }

        <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt class="text-muted-foreground">{{ 'videos.duration' | t }}</dt>
          <dd>{{ v.durationMs | duration }}</dd>
          @if (v.width && v.height) {
            <dt class="text-muted-foreground">{{ 'videos.size' | t }}</dt>
            <dd>{{ v.width }}×{{ v.height }}</dd>
          }
          @if (v.path) {
            <dt class="text-muted-foreground">{{ 'videos.path' | t }}</dt>
            <dd class="font-mono text-xs">{{ v.path }}</dd>
          }
          <dt class="text-muted-foreground">{{ 'videos.link.label' | t }}</dt>
          <dd>
            @if (v.linkMethod) {
              {{ 'videos.link.' + v.linkMethod | t }}
            } @else {
              {{ 'videos.link.none' | t }}
            }
            @if (v.linkLocked) {
              <span hlmBadge variant="outline" class="ml-1">{{ 'videos.link.locked' | t }}</span>
            }
          </dd>
        </dl>

        @if (v.canManage) {
          <div class="grid gap-3 rounded-lg border p-3">
            <label class="flex items-center gap-2 text-sm">
              <hlm-switch [checked]="v.isLive" (checkedChange)="setLive($event)" />
              {{ 'videos.live' | t }}
            </label>

            <form class="flex gap-2" (ngSubmit)="search()">
              <input
                hlmInput
                name="search"
                class="min-w-0 flex-1"
                [(ngModel)]="query"
                [placeholder]="'videos.link.search' | t"
              />
              <button hlmBtn variant="outline" type="submit">{{ 'videos.link.find' | t }}</button>
            </form>
            @if (songs().length) {
              <ul class="grid gap-1 text-sm">
                @for (song of songs(); track song.id) {
                  <li>
                    <button
                      type="button"
                      class="w-full rounded-md px-2 py-1 text-left hover:bg-accent"
                      (click)="link(song.id)"
                    >
                      <span class="font-medium">{{ song.title }}</span>
                      <span class="text-muted-foreground">
                        · {{ artistsOf(song) }} · {{ song.album.title }} ·
                        {{ song.durationMs | duration }}</span
                      >
                    </button>
                  </li>
                }
              </ul>
            }

            <div class="flex flex-wrap gap-2">
              @if (v.trackId) {
                <button hlmBtn size="sm" variant="outline" type="button" (click)="link(null)">
                  {{ 'videos.link.remove' | t }}
                </button>
              }
              @if (v.linkLocked) {
                <button hlmBtn size="sm" variant="outline" type="button" (click)="unlock()">
                  {{ 'videos.link.automatic' | t }}
                </button>
              }
              @if (v.trackId) {
                <button hlmBtn size="sm" variant="outline" type="button" (click)="realign()">
                  {{ 'videos.realign' | t }}
                </button>
              }
              @if (canDelete()) {
                <button hlmBtn size="sm" variant="destructive" type="button" (click)="remove()">
                  {{ 'videos.delete' | t }}
                </button>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class VideoDetail implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly auth = inject(AuthService);

  readonly videoId = input.required<string>();
  readonly changed = output<void>();
  readonly deleted = output<void>();

  private readonly player = viewChild<ElementRef<HTMLVideoElement>>('player');

  protected readonly video = signal<Video | null>(null);
  protected readonly audio = signal<VideoAudioMode>('TRACK');
  protected readonly position = signal(0);
  protected readonly songs = signal<Song[]>([]);
  protected query = '';
  private resumeAt: { time: number; playing: boolean } | null = null;

  protected readonly canDelete = computed(() => this.auth.can('DELETE_TRACKS'));

  protected readonly source = computed(() => {
    const v = this.video();
    if (!v) return '';
    return this.audio() === 'TRACK' ? v.trackAudioUrl : v.videoAudioUrl;
  });

  async ngOnInit(): Promise<void> {
    await this.load();
    const v = this.video();
    if (v) this.audio.set(v.sync === 'READY' ? v.audioMode : 'VIDEO');
  }

  protected percent(value: number): number {
    return Math.round(value * 100);
  }

  /** Same timeline in both: the new source starts where the old one was, playing if it was. */
  protected switchAudio(mode: VideoAudioMode): void {
    const element = this.player()?.nativeElement;
    if (element) this.resumeAt = { time: element.currentTime, playing: !element.paused };
    this.audio.set(mode);
  }

  protected resume(): void {
    const element = this.player()?.nativeElement;
    const resume = this.resumeAt;
    if (!element || !resume) return;
    this.resumeAt = null;
    // A version being made as it streams cannot seek yet: it starts there instead (t, in seconds).
    const seekable =
      element.seekable.length > 0 &&
      element.seekable.end(element.seekable.length - 1) >= resume.time;
    if (seekable) element.currentTime = resume.time;
    else if (resume.time > 1) {
      const url = new URL(element.src);
      url.searchParams.set('t', resume.time.toFixed(1));
      element.src = url.href;
    }
    if (resume.playing) void element.play();
  }

  protected seekTo(ms: number): void {
    const element = this.player()?.nativeElement;
    if (element) element.currentTime = ms / 1000;
  }

  protected artistsOf(song: Song): string {
    return song.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected async search(): Promise<void> {
    if (!this.query.trim()) return;
    const result = await this.gql.query(AiTrackSearchQuery, { search: this.query.trim() });
    this.songs.set(result.tracks.items);
  }

  protected link(trackId: string | null): Promise<void> {
    return this.run(() => this.gql.mutate(LinkMusicVideoMutation, { id: this.videoId(), trackId }));
  }

  protected unlock(): Promise<void> {
    return this.run(() => this.gql.mutate(UnlockMusicVideoMutation, { id: this.videoId() }));
  }

  protected setLive(live: boolean): Promise<void> {
    return this.run(() => this.gql.mutate(SetMusicVideoLiveMutation, { id: this.videoId(), live }));
  }

  protected realign(): Promise<void> {
    return this.run(async () => {
      await this.gql.mutate(RealignMusicVideoMutation, { id: this.videoId() });
      toast.success(this.i18n.t('videos.realigning'));
    });
  }

  protected async remove(): Promise<void> {
    if (!confirm(this.i18n.t('videos.deleteConfirm'))) return;
    try {
      await this.gql.mutate(DeleteMusicVideoMutation, { id: this.videoId() });
      this.deleted.emit();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    try {
      await action();
      this.songs.set([]);
      this.query = '';
      await this.load();
      this.changed.emit();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  private async load(): Promise<void> {
    this.video.set((await this.gql.query(VideoQuery, { id: this.videoId() })).musicVideo);
  }
}
