import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  TranscodeFormat,
  TranscodingFieldsFragment as Transcoding,
} from '../../graphql/generated/graphql';
import {
  ClearTranscodeCacheMutation,
  LibrariesQuery,
  SetTranscodingSettingsMutation,
  TranscodingQuery,
} from '../../graphql/operations';
import { BytesPipe, CountPipe } from '../../shared/format';
import { FfmpegGuide } from './ffmpeg-card';

/** The versions offered: what players usually ask for. */
const Targets: { format: TranscodeFormat; bitRate: number; label: string }[] = [
  { format: 'OPUS', bitRate: 96, label: 'Opus 96 kbit/s' },
  { format: 'OPUS', bitRate: 128, label: 'Opus 128 kbit/s' },
  { format: 'OPUS', bitRate: 192, label: 'Opus 192 kbit/s' },
  { format: 'MP3', bitRate: 128, label: 'MP3 128 kbit/s' },
  { format: 'MP3', bitRate: 192, label: 'MP3 192 kbit/s' },
  { format: 'MP3', bitRate: 320, label: 'MP3 320 kbit/s' },
  { format: 'AAC', bitRate: 256, label: 'AAC 256 kbit/s' },
  // Only with an ffmpeg that encodes HE-AAC (libfdk_aac, or macOS's aac_at).
  { format: 'HE_AAC', bitRate: 64, label: 'HE-AAC 64 kbit/s' },
  { format: 'HE_AAC', bitRate: 48, label: 'HE-AAC 48 kbit/s' },
];

/** Decimal gigabytes, as sizes are shown everywhere in the panel. */
const GB = 1_000_000_000;
const Sizes = [0.5, 1, 2, 5, 10, 20, 50].map((gb) => gb * GB);
const Ages = [7, 30, 90, 365];

/**
 * Converted audio: the cache of whole conversions, and the versions made ahead of time (passive transcoding), with
 * what each costs and gives.
 */
@Component({
  selector: 'app-transcoding-card',
  imports: [
    FormsModule,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSwitchImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
  ],
  template: `
    <section hlmCard data-tour="transcoding-conversion">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'transcoding.title' | t }}</h2>
        <p hlmCardDescription>{{ 'transcoding.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="grid gap-6 text-sm">
          @if (!s.ffmpegAvailable) {
            <div hlmAlert>
              <p hlmAlertDescription>{{ 'transcoding.noFfmpeg' | t }}</p>
            </div>
          }

          <div class="grid gap-3">
            <h3 class="font-medium">{{ 'transcoding.cache.title' | t }}</h3>
            <label class="flex items-center gap-3">
              <hlm-switch [(checked)]="cacheEnabled" />
              {{ 'transcoding.cache.enabled' | t }}
            </label>
            <p class="text-xs text-muted-foreground">{{ 'transcoding.cache.help' | t }}</p>
            @if (cacheEnabled) {
              <div class="flex flex-wrap items-center gap-x-6 gap-y-3">
                <label class="flex items-center gap-2">
                  {{ 'transcoding.cache.size' | t }}
                  <select hlmNativeSelect [(ngModel)]="cacheMaxBytes">
                    @for (size of sizes; track size) {
                      <option [ngValue]="size">{{ size | bytes }}</option>
                    }
                  </select>
                </label>
                <label class="flex items-center gap-2">
                  {{ 'transcoding.cache.age' | t }}
                  <select hlmNativeSelect [(ngModel)]="cacheMaxAgeDays">
                    @for (age of ages; track age) {
                      <option [ngValue]="age">
                        {{ 'transcoding.cache.days' | t: { count: age } }}
                      </option>
                    }
                  </select>
                </label>
              </div>
              <div class="grid gap-1.5">
                <hlm-progress [value]="cachePercent()" class="h-2">
                  <hlm-progress-indicator />
                </hlm-progress>
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <p class="text-xs text-muted-foreground">
                    {{
                      'transcoding.cache.usage'
                        | t: { bytes: (s.cacheBytes | bytes), count: (s.cacheFiles | count) }
                    }}
                  </p>
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    type="button"
                    [disabled]="busy() || s.cacheFiles === 0"
                    (click)="clearCache()"
                  >
                    {{ 'transcoding.cache.clear' | t }}
                  </button>
                </div>
              </div>
            }
          </div>

          <div class="grid gap-3">
            <h3 class="font-medium">{{ 'transcoding.pregenerate.title' | t }}</h3>
            <p class="text-xs text-muted-foreground">{{ 'transcoding.pregenerate.help' | t }}</p>
            <div class="grid gap-3 sm:grid-cols-2">
              <div class="rounded-md border p-3">
                <p class="mb-1 text-xs font-medium text-emerald-700 dark:text-emerald-400">
                  {{ 'transcoding.pregenerate.pros' | t }}
                </p>
                <ul class="grid gap-1 text-xs text-muted-foreground">
                  @for (line of list('transcoding.pregenerate.prosList'); track line) {
                    <li>+ {{ line }}</li>
                  }
                </ul>
              </div>
              <div class="rounded-md border p-3">
                <p class="mb-1 text-xs font-medium text-amber-700 dark:text-amber-400">
                  {{ 'transcoding.pregenerate.cons' | t }}
                </p>
                <ul class="grid gap-1 text-xs text-muted-foreground">
                  @for (line of list('transcoding.pregenerate.consList'); track line) {
                    <li>− {{ line }}</li>
                  }
                </ul>
              </div>
            </div>

            <fieldset class="grid gap-2">
              <legend class="mb-1 font-medium">{{ 'transcoding.pregenerate.formats' | t }}</legend>
              <div class="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                @for (target of targets(); track target.label) {
                  <label class="flex items-center gap-2">
                    <hlm-checkbox
                      [checked]="chosen(target)"
                      (checkedChange)="choose(target, $event)"
                    />
                    {{ target.label }}
                  </label>
                }
              </div>
              @if (s.ffmpegAvailable && !heAac()) {
                <p class="text-xs text-muted-foreground">
                  {{ 'transcoding.pregenerate.noHeAac' | t }}
                  <a class="underline" [href]="guide" target="_blank" rel="noopener">{{
                    'settings.ffmpeg.features.guide' | t
                  }}</a>
                </p>
              }
            </fieldset>

            @if (pregenerate().length) {
              <fieldset class="grid gap-2">
                <legend class="mb-1 font-medium">
                  {{ 'transcoding.pregenerate.libraries' | t }}
                </legend>
                <label class="flex items-center gap-2">
                  <hlm-checkbox
                    [checked]="allLibraries()"
                    (checkedChange)="allLibraries.set($event)"
                  />
                  {{ 'transcoding.pregenerate.allLibraries' | t }}
                </label>
                @if (!allLibraries()) {
                  <div class="grid gap-2 pl-6 sm:grid-cols-2">
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
                <label class="flex items-center gap-2">
                  <hlm-checkbox [(checked)]="onlyLossless" />
                  {{ 'transcoding.pregenerate.onlyLossless' | t }}
                </label>
              </fieldset>
            }

            @if (s.pregenerated.length) {
              <ul class="grid gap-2">
                @for (p of s.pregenerated; track p.format + p.bitRate) {
                  <li class="grid gap-1">
                    <div class="flex flex-wrap justify-between gap-2 text-xs">
                      <span class="font-medium">{{ labelOf(p.format, p.bitRate) }}</span>
                      <span class="text-muted-foreground">
                        {{
                          'transcoding.pregenerate.progress'
                            | t
                              : {
                                  done: (p.done | count),
                                  total: (p.total | count),
                                  bytes: (p.bytes | bytes),
                                  estimated: (p.estimatedBytes | bytes),
                                }
                        }}
                      </span>
                    </div>
                    <hlm-progress [value]="p.total ? (p.done / p.total) * 100 : 100" class="h-1.5">
                      <hlm-progress-indicator />
                    </hlm-progress>
                  </li>
                }
              </ul>
              @if (s.working) {
                <p class="text-xs text-muted-foreground">
                  {{ 'transcoding.pregenerate.working' | t }}
                </p>
              }
            } @else if (!pregenerate().length) {
              <p class="text-xs text-muted-foreground">{{ 'transcoding.pregenerate.none' | t }}</p>
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
export class TranscodingCard implements OnInit, OnDestroy {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly sizes = Sizes;
  protected readonly guide = FfmpegGuide;
  protected readonly ages = Ages;

  protected readonly status = signal<Transcoding | null>(null);
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);
  protected readonly busy = signal(false);

  protected cacheEnabled = true;
  protected cacheMaxBytes = 2 * GB;
  protected cacheMaxAgeDays = 30;
  protected onlyLossless = false;
  protected readonly pregenerate = signal<{ format: TranscodeFormat; bitRate: number }[]>([]);
  protected readonly allLibraries = signal(true);
  protected readonly libraryIds = signal<string[]>([]);

  /** This server's ffmpeg encodes HE-AAC: only then are its versions offered. */
  protected readonly heAac = computed(() => this.status()?.formats.includes('HE_AAC') ?? false);
  protected readonly targets = computed(() =>
    Targets.filter((t) => t.format !== 'HE_AAC' || this.heAac()),
  );

  protected readonly cachePercent = computed(() => {
    const s = this.status();
    return s ? Math.min(100, (s.cacheBytes / s.settings.cacheMaxBytes) * 100) : 0;
  });

  private poll: ReturnType<typeof setInterval> | null = null;

  async ngOnInit(): Promise<void> {
    const [transcoding, libraries] = await Promise.all([
      this.gql.query(TranscodingQuery),
      this.gql.query(LibrariesQuery),
    ]);
    this.libraries.set(libraries.libraries.filter((l) => !l.personal));
    this.show(transcoding.transcoding, true);
    // While versions are being made, their progress moves.
    this.poll = setInterval(() => void this.refresh(), 5000);
  }

  ngOnDestroy(): void {
    if (this.poll) clearInterval(this.poll);
  }

  protected list(key: string): string[] {
    return this.i18n.t(key).split(' · ');
  }

  protected labelOf(format: string, bitRate: number): string {
    return (
      Targets.find((t) => t.format === format && t.bitRate === bitRate)?.label ??
      `${format} ${bitRate}`
    );
  }

  protected chosen(target: { format: TranscodeFormat; bitRate: number }): boolean {
    return this.pregenerate().some(
      (t) => t.format === target.format && t.bitRate === target.bitRate,
    );
  }

  protected choose(target: { format: TranscodeFormat; bitRate: number }, on: boolean): void {
    this.pregenerate.update((list) =>
      on
        ? [...list, { format: target.format, bitRate: target.bitRate }]
        : list.filter((t) => t.format !== target.format || t.bitRate !== target.bitRate),
    );
  }

  protected toggleLibrary(id: string, on: boolean): void {
    this.libraryIds.update((ids) => (on ? [...ids, id] : ids.filter((other) => other !== id)));
  }

  protected save(): Promise<void> {
    return this.run(async () => {
      const result = await this.gql.mutate(SetTranscodingSettingsMutation, {
        input: {
          cacheEnabled: this.cacheEnabled,
          cacheMaxBytes: this.cacheMaxBytes,
          cacheMaxAgeDays: this.cacheMaxAgeDays,
          // HE-AAC versions an ffmpeg without the encoder cannot make: the server refuses them.
          pregenerate: this.pregenerate().filter((t) => t.format !== 'HE_AAC' || this.heAac()),
          pregenerateLibraries:
            this.allLibraries() || !this.libraryIds().length ? null : this.libraryIds(),
          pregenerateOnlyLossless: this.onlyLossless,
        },
      });
      this.show(result.setTranscodingSettings, true);
      toast.success(this.i18n.t('transcoding.saved'));
    });
  }

  protected clearCache(): Promise<void> {
    return this.run(async () => {
      this.show((await this.gql.mutate(ClearTranscodeCacheMutation)).clearTranscodeCache, false);
      toast.success(this.i18n.t('transcoding.cache.cleared'));
    });
  }

  private async refresh(): Promise<void> {
    if (this.busy()) return;
    try {
      this.show((await this.gql.query(TranscodingQuery)).transcoding, false);
    } catch {
      // The next poll tries again.
    }
  }

  /** Shows the server's state; with the settings too when they were just read or saved (not over the user's edits). */
  private show(status: Transcoding, settings: boolean): void {
    this.status.set(status);
    if (!settings) return;
    const s = status.settings;
    this.cacheEnabled = s.cacheEnabled;
    this.cacheMaxBytes = Sizes.reduce((best, size) =>
      Math.abs(size - s.cacheMaxBytes) < Math.abs(best - s.cacheMaxBytes) ? size : best,
    );
    this.cacheMaxAgeDays = Ages.includes(s.cacheMaxAgeDays) ? s.cacheMaxAgeDays : 30;
    this.onlyLossless = s.pregenerateOnlyLossless;
    this.pregenerate.set(s.pregenerate.map((t) => ({ format: t.format, bitRate: t.bitRate })));
    this.allLibraries.set(!s.pregenerateLibraries?.length);
    this.libraryIds.set(s.pregenerateLibraries ?? []);
  }

  private async run(change: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await change();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}
