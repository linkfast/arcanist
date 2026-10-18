import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  AiStatusFieldsFragment as Status,
  AiTrackSearchQuery as SearchResult,
  TrackAnalysisFieldsFragment as Analysis,
} from '../../graphql/generated/graphql';
import {
  AiStatusQuery,
  AiTrackSearchQuery,
  PrioritizeTrackAnalysisMutation,
  ReanalyzeTracksMutation,
  SetAiSettingsMutation,
  SetExtensionEnabledMutation,
  TrackAnalysisQuery,
} from '../../graphql/operations';
import { AnalysisView } from './analysis-view';

type Song = SearchResult['tracks']['items'][number];

/**
 * AI: the analysis for DJ software that clients with AI assistance (the AI DJ of ULTIMATE) use. Its progress over
 * the library, its settings, and an inspector that draws a song's analysis and plays any moment of it.
 */
@Component({
  selector: 'app-ai-page',
  imports: [
    FormsModule,
    AnalysisView,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'ai.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'ai.subtitle' | t }}</p>
    </header>

    @if (status(); as current) {
      <div class="grid gap-6">
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'ai.analysis.title' | t }}</h2>
            <p hlmCardDescription>{{ 'ai.analysis.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-4">
            <label class="flex items-center gap-2 text-sm font-medium">
              <hlm-switch
                [checked]="current.enabled"
                [disabled]="busy()"
                (checkedChange)="setEnabled($event)"
              />
              {{ (current.enabled ? 'ai.analysis.on' : 'ai.analysis.off') | t }}
            </label>
            <p class="flex flex-wrap items-center gap-2 text-sm">
              <span hlmBadge [variant]="current.activity === 'RUNNING' ? 'default' : 'secondary'">{{
                'ai.activity.' + current.activity | t
              }}</span>
              @if (current.songsPerMinute > 0) {
                <span class="text-muted-foreground">{{
                  'ai.analysis.speed' | t: { count: current.songsPerMinute }
                }}</span>
              }
            </p>
            <dl class="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
              <div>
                <dt class="text-muted-foreground">{{ 'ai.analysis.analyzed' | t }}</dt>
                <dd class="text-lg font-semibold tabular-nums">{{ current.progress.analyzed }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'ai.analysis.pending' | t }}</dt>
                <dd class="text-lg font-semibold tabular-nums">{{ current.progress.pending }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'ai.analysis.failed' | t }}</dt>
                <dd class="text-lg font-semibold tabular-nums">{{ current.progress.failed }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'ai.analysis.version' | t }}</dt>
                <dd class="text-lg font-semibold tabular-nums">
                  {{ current.progress.analysisVersion || '—' }}
                </dd>
              </div>
            </dl>
            @if (current.error) {
              <div hlmAlert variant="destructive">
                <p hlmAlertDescription>
                  <code class="break-all text-xs">{{ current.error }}</code>
                </p>
              </div>
            }
            <div>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                [disabled]="busy() || !current.enabled"
                (click)="reanalyze()"
              >
                {{
                  (confirmReanalyze() ? 'ai.analysis.reanalyzeConfirm' : 'ai.analysis.reanalyze')
                    | t
                }}
              </button>
            </div>
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'ai.performance.title' | t }}</h2>
            <p hlmCardDescription>{{ 'ai.performance.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-4">
            <div hlmField class="max-w-xs">
              <label hlmFieldLabel for="ai-threads">{{ 'ai.performance.threads' | t }}</label>
              <select hlmNativeSelect id="ai-threads" [(ngModel)]="threads">
                @for (n of threadOptions(); track n) {
                  <option [ngValue]="n">{{ n }}</option>
                }
              </select>
            </div>
            <label class="flex items-center gap-2 text-sm"
              ><hlm-switch [(checked)]="pauseWhileScanning" />
              {{ 'ai.performance.pauseWhileScanning' | t }}</label
            >
            <label class="flex items-center gap-2 text-sm"
              ><hlm-switch [(checked)]="pauseWhileStreaming" />
              {{ 'ai.performance.pauseWhileStreaming' | t }}</label
            >
            <label class="flex items-center gap-2 text-sm"
              ><hlm-switch [(checked)]="onDemandSources" />
              {{ 'ai.performance.onDemandSources' | t }}</label
            >
            <label class="flex items-center gap-2 text-sm"
              ><hlm-switch [(checked)]="shareServerStats" />
              {{ 'ai.performance.shareServerStats' | t }}</label
            >
            <div>
              <button hlmBtn size="sm" [disabled]="busy()" (click)="save()">
                {{ 'common.save' | t }}
              </button>
            </div>
          </div>
        </section>

        @if (current.enabled) {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'ai.inspector.title' | t }}</h2>
              <p hlmCardDescription>{{ 'ai.inspector.description' | t }}</p>
            </div>
            <div hlmCardContent class="grid gap-4">
              <form class="flex gap-2" (ngSubmit)="search()">
                <input
                  hlmInput
                  name="search"
                  class="max-w-sm"
                  [(ngModel)]="query"
                  [placeholder]="'ai.inspector.search' | t"
                />
                <button hlmBtn variant="outline" type="submit">
                  {{ 'ai.inspector.searchButton' | t }}
                </button>
              </form>
              @if (songs().length) {
                <ul class="grid gap-1 text-sm">
                  @for (song of songs(); track song.id) {
                    <li>
                      <button
                        class="w-full rounded-md px-2 py-1 text-left hover:bg-accent"
                        (click)="inspect(song)"
                      >
                        <span class="font-medium">{{ song.title }}</span>
                        <span class="text-muted-foreground">
                          · {{ artistsOf(song) }} · {{ song.album.title }}</span
                        >
                      </button>
                    </li>
                  }
                </ul>
              }

              @if (selected(); as song) {
                <div class="grid gap-3">
                  <h3 class="font-medium">
                    {{ song.title }}
                    <span class="text-muted-foreground">· {{ artistsOf(song) }}</span>
                  </h3>
                  @if (analysis(); as a) {
                    @if (a.status === 'READY') {
                      <app-analysis-view [analysis]="a" (picked)="listen($event)" />
                      <p class="text-xs text-muted-foreground">{{ 'ai.inspector.legend' | t }}</p>
                      <dl class="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                        <div>
                          <dt class="text-muted-foreground">{{ 'ai.inspector.tempo' | t }}</dt>
                          <dd>
                            @if (a.tempo; as tempo) {
                              {{ tempo.bpm }} BPM ·
                              {{
                                (tempo.stable ? 'ai.inspector.stable' : 'ai.inspector.varying') | t
                              }}
                            } @else {
                              —
                            }
                          </dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">{{ 'ai.inspector.key' | t }}</dt>
                          <dd>
                            {{
                              a.key
                                ? a.key.tonic +
                                  ' ' +
                                  ('ai.mode.' + a.key.mode | t) +
                                  ' · ' +
                                  a.key.camelot
                                : '—'
                            }}
                          </dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">{{ 'ai.inspector.loudness' | t }}</dt>
                          <dd>
                            {{
                              a.loudness
                                ? a.loudness.integratedLufs +
                                  ' LUFS · ' +
                                  a.loudness.truePeakDb +
                                  ' dBTP'
                                : '—'
                            }}
                          </dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">{{ 'ai.inspector.music' | t }}</dt>
                          <dd class="tabular-nums">
                            {{ time(a.musicStart) }}–{{ time(a.musicEnd) }}
                          </dd>
                        </div>
                      </dl>
                      @if (a.stale) {
                        <p class="text-sm text-muted-foreground">{{ 'ai.inspector.stale' | t }}</p>
                      }
                    } @else if (a.status === 'PENDING') {
                      <p class="flex flex-wrap items-center gap-3 text-sm">
                        {{ 'ai.inspector.pending' | t: { position: a.position ?? 0 } }}
                        <button
                          hlmBtn
                          size="sm"
                          variant="outline"
                          [disabled]="busy()"
                          (click)="prioritize()"
                        >
                          {{ 'ai.inspector.prioritize' | t }}
                        </button>
                      </p>
                    } @else {
                      <p class="text-sm text-destructive">{{ 'ai.inspector.failed' | t }}</p>
                    }
                  }
                </div>
              }
            </div>
          </section>
        }
      </div>
    }
  `,
})
export class AiPage implements OnInit, OnDestroy {
  private readonly gql = inject(Gql);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly enabledExtensions = inject(EnabledExtensions);
  private readonly i18n = inject(I18n);

  protected readonly status = signal<Status | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmReanalyze = signal(false);
  protected readonly threadOptions = signal<number[]>([1]);
  protected readonly songs = signal<Song[]>([]);
  protected readonly selected = signal<Song | null>(null);
  protected readonly analysis = signal<Analysis | null>(null);
  protected threads = 1;
  protected pauseWhileScanning = true;
  protected pauseWhileStreaming = false;
  protected onDemandSources = true;
  protected shareServerStats = true;
  protected query = '';

  private poll: ReturnType<typeof setInterval> | undefined;
  private audio: HTMLAudioElement | null = null;

  async ngOnInit(): Promise<void> {
    const status = (await this.gql.query(AiStatusQuery)).aiStatus;
    this.apply(status, true);
    // Follows the progress (and a song waiting for its analysis) while the page is open.
    this.poll = setInterval(() => void this.refresh(), 3000);
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
    this.audio?.pause();
  }

  private apply(status: Status, settings: boolean): void {
    this.status.set(status);
    this.threadOptions.set(Array.from({ length: status.maxThreads }, (_, i) => i + 1));
    if (settings) {
      this.threads = status.settings.threads;
      this.pauseWhileScanning = status.settings.pauseWhileScanning;
      this.pauseWhileStreaming = status.settings.pauseWhileStreaming;
      this.onDemandSources = status.settings.onDemandSources;
      this.shareServerStats = status.settings.shareServerStats;
    }
  }

  private async refresh(): Promise<void> {
    try {
      this.apply((await this.gql.query(AiStatusQuery)).aiStatus, false);
      const song = this.selected();
      if (song && this.analysis()?.status === 'PENDING') await this.load(song.id);
    } catch {
      clearInterval(this.poll);
    }
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(SetExtensionEnabledMutation, { id: 'ai', enabled });
      await this.enabledExtensions.refresh();
      // Off, the page leaves the menu: it is switched on again from Extensions.
      if (!enabled) {
        await this.router.navigateByUrl('/extensions');
        return;
      }
      this.apply((await this.gql.query(AiStatusQuery)).aiStatus, false);
    });
  }

  protected async save(): Promise<void> {
    await this.run(async () => {
      const status = (
        await this.gql.mutate(SetAiSettingsMutation, {
          input: {
            threads: this.threads,
            pauseWhileScanning: this.pauseWhileScanning,
            pauseWhileStreaming: this.pauseWhileStreaming,
            onDemandSources: this.onDemandSources,
            shareServerStats: this.shareServerStats,
          },
        })
      ).setAiSettings;
      this.apply(status, true);
      toast.success(this.i18n.t('ai.performance.saved'));
    });
  }

  /** Asks to confirm with a second click within a few seconds. */
  protected async reanalyze(): Promise<void> {
    if (!this.confirmReanalyze()) {
      this.confirmReanalyze.set(true);
      setTimeout(() => this.confirmReanalyze.set(false), 4000);
      return;
    }
    this.confirmReanalyze.set(false);
    await this.run(async () => {
      this.apply((await this.gql.mutate(ReanalyzeTracksMutation)).reanalyzeTracks, false);
      toast.success(this.i18n.t('ai.analysis.reanalyzing'));
    });
  }

  protected async search(): Promise<void> {
    const text = this.query.trim();
    if (!text) return;
    await this.run(async () =>
      this.songs.set((await this.gql.query(AiTrackSearchQuery, { search: text })).tracks.items),
    );
  }

  protected async inspect(song: Song): Promise<void> {
    this.selected.set(song);
    this.analysis.set(null);
    await this.run(() => this.load(song.id));
  }

  private async load(trackId: string): Promise<void> {
    this.analysis.set((await this.gql.query(TrackAnalysisQuery, { trackId })).trackAnalysis);
  }

  protected async prioritize(): Promise<void> {
    const song = this.selected();
    if (!song) return;
    await this.run(async () => {
      this.analysis.set(
        (await this.gql.mutate(PrioritizeTrackAnalysisMutation, { trackId: song.id }))
          .prioritizeTrackAnalysis,
      );
    });
  }

  /** Plays four seconds from the moment clicked (a window of decoded audio). */
  protected async listen(seconds: number): Promise<void> {
    const song = this.selected();
    if (!song) return;
    try {
      const start = Math.max(0, Math.min(seconds, song.durationMs / 1000 - 0.5));
      const wav = await firstValueFrom(
        this.http.get(
          `/rest/v1/tracks/${song.id}/audio-window?start=${start.toFixed(3)}&duration=4`,
          { responseType: 'blob' },
        ),
      );
      this.audio?.pause();
      this.audio = new Audio(URL.createObjectURL(wav));
      await this.audio.play();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(apiError.code));
    }
  }

  protected artistsOf(song: Song): string {
    return song.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected time(seconds: number | null | undefined): string {
    if (seconds == null) return '—';
    const whole = Math.floor(seconds);
    return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}
