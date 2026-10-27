import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { VideoAudioMode, VideoSettingsQuery as Result } from '../../graphql/generated/graphql';
import {
  LibrariesQuery,
  SetVideoSettingsMutation,
  VideoSettingsQuery,
} from '../../graphql/operations';
import { BytesPipe } from '../../shared/format';

const GB = 1_000_000_000;
const Sizes = [5, 10, 20, 50, 100, 200].map((gb) => gb * GB);
const Ages = [7, 30, 90, 365];
const Heights = [2160, 1440, 1080, 720, 480, 360];

/**
 * Music videos: whose audio plays by default (users choose their own, and players switch while playing), the cache
 * of versions made when played, and what is prepared ahead of time for adaptive streaming.
 */
@Component({
  selector: 'app-video-card',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmNativeSelectImports,
    HlmSwitchImports,
    TranslatePipe,
    BytesPipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'videos.settings.title' | t }}</h2>
        <p hlmCardDescription>{{ 'videos.settings.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="grid gap-6 text-sm">
          <p class="text-xs text-muted-foreground">
            {{
              'videos.settings.counts'
                | t: { videos: s.videos, linked: s.linked, aligned: s.aligned, pending: s.pending }
            }}
          </p>

          <label class="grid gap-1">
            <span class="font-medium">{{ 'videos.settings.defaultAudio' | t }}</span>
            <select hlmNativeSelect name="audio" [(ngModel)]="defaultAudio">
              <option value="TRACK">{{ 'videos.audio.TRACK' | t }}</option>
              <option value="VIDEO">{{ 'videos.audio.VIDEO' | t }}</option>
            </select>
            <span class="text-xs text-muted-foreground">{{
              'videos.settings.defaultAudioHelp' | t
            }}</span>
          </label>

          <div class="grid gap-2">
            <h3 class="font-medium">{{ 'videos.settings.cache' | t }}</h3>
            <div class="flex flex-wrap items-center gap-x-6 gap-y-3">
              <label class="flex items-center gap-2">
                {{ 'transcoding.cache.size' | t }}
                <select hlmNativeSelect name="size" [(ngModel)]="cacheMaxBytes">
                  @for (size of sizes; track size) {
                    <option [ngValue]="size">{{ size | bytes }}</option>
                  }
                </select>
              </label>
              <label class="flex items-center gap-2">
                {{ 'transcoding.cache.age' | t }}
                <select hlmNativeSelect name="age" [(ngModel)]="cacheMaxAgeDays">
                  @for (age of ages; track age) {
                    <option [ngValue]="age">
                      {{ 'transcoding.cache.days' | t: { count: age } }}
                    </option>
                  }
                </select>
              </label>
            </div>
            <p class="text-xs text-muted-foreground">
              {{
                'videos.settings.usage'
                  | t: { cache: (s.cacheBytes | bytes), prepared: (s.preparedBytes | bytes) }
              }}
            </p>
          </div>

          <div class="grid gap-2">
            <h3 class="font-medium">{{ 'videos.settings.prepare' | t }}</h3>
            <p class="text-xs text-muted-foreground">{{ 'videos.settings.prepareHelp' | t }}</p>
            <div class="flex flex-wrap gap-x-4 gap-y-2">
              @for (height of heights; track height) {
                <label class="flex items-center gap-2">
                  <hlm-checkbox
                    [checked]="prepareHeights().includes(height)"
                    (checkedChange)="toggleHeight(height, $event)"
                  />
                  {{ height }}p
                </label>
              }
            </div>
            <label class="flex items-center gap-3">
              <hlm-switch [(checked)]="prepareWhenPlayed" />
              {{ 'videos.settings.whenPlayed' | t }}
            </label>
            @if (libraries().length) {
              <p class="text-xs text-muted-foreground">{{ 'videos.settings.libraries' | t }}</p>
              <div class="grid gap-2 sm:grid-cols-2">
                @for (library of libraries(); track library.id) {
                  <label class="flex items-center gap-2">
                    <hlm-checkbox
                      [checked]="libraryIds().includes(library.id)"
                      (checkedChange)="toggleLibrary(library.id, $event)"
                    />
                    {{ library.name }}
                  </label>
                }
              </div>
            }
          </div>

          <div>
            <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
              {{ 'transcoding.save' | t }}
            </button>
          </div>
        </div>
      }
    </section>
  `,
})
export class VideoCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly sizes = Sizes;
  protected readonly ages = Ages;
  protected readonly heights = Heights;

  protected readonly status = signal<Result['videoSettings'] | null>(null);
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);
  protected readonly busy = signal(false);
  protected readonly prepareHeights = signal<number[]>([]);
  protected readonly libraryIds = signal<string[]>([]);
  protected defaultAudio: VideoAudioMode = 'TRACK';
  protected cacheMaxBytes = 20 * GB;
  protected cacheMaxAgeDays = 30;
  protected prepareWhenPlayed = true;

  async ngOnInit(): Promise<void> {
    try {
      const [settings, libraries] = await Promise.all([
        this.gql.query(VideoSettingsQuery),
        this.gql.query(LibrariesQuery),
      ]);
      this.libraries.set(libraries.libraries.filter((l) => !l.personal));
      this.show(settings.videoSettings);
    } catch {
      // Not shown on a server without music videos.
    }
  }

  protected toggleHeight(height: number, on: boolean): void {
    this.prepareHeights.update((list) =>
      on ? [...list, height] : list.filter((h) => h !== height),
    );
  }

  protected toggleLibrary(id: string, on: boolean): void {
    this.libraryIds.update((ids) => (on ? [...ids, id] : ids.filter((other) => other !== id)));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(SetVideoSettingsMutation, {
        input: {
          defaultAudio: this.defaultAudio,
          cacheMaxBytes: this.cacheMaxBytes,
          cacheMaxAgeDays: this.cacheMaxAgeDays,
          prepareLibraryIds: this.libraryIds(),
          prepareHeights: this.prepareHeights(),
          prepareWhenPlayed: this.prepareWhenPlayed,
        },
      });
      this.show((await this.gql.query(VideoSettingsQuery)).videoSettings);
      toast.success(this.i18n.t('transcoding.saved'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  private show(settings: Result['videoSettings']): void {
    this.status.set(settings);
    this.defaultAudio = settings.defaultAudio;
    this.cacheMaxBytes = Sizes.includes(settings.cacheMaxBytes) ? settings.cacheMaxBytes : 20 * GB;
    this.cacheMaxAgeDays = settings.cacheMaxAgeDays;
    this.prepareWhenPlayed = settings.prepareWhenPlayed;
    this.prepareHeights.set([...settings.prepareHeights]);
    this.libraryIds.set([...settings.prepareLibraryIds]);
  }
}
