import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  StreamingDefaultsFieldsFragment as Defaults,
  TranscodeFormat,
} from '../../graphql/generated/graphql';
import { SetStreamingDefaultsMutation, StreamingDefaultsQuery } from '../../graphql/operations';

type Client = 'desktop' | 'web' | 'mobile';

interface Quality {
  format: TranscodeFormat | null;
  maxBitRate: number | null;
}

/** The qualities offered, from the original down: what Ultimate's apps can play. */
const Qualities: Quality[] = [
  { format: null, maxBitRate: null },
  { format: 'FLAC', maxBitRate: null },
  ...[256, 192, 160, 128, 96].map((rate) => ({
    format: 'OPUS' as TranscodeFormat,
    maxBitRate: rate,
  })),
  ...[256, 192, 128].map((rate) => ({ format: 'AAC' as TranscodeFormat, maxBitRate: rate })),
  ...[320, 256, 192, 128].map((rate) => ({ format: 'MP3' as TranscodeFormat, maxBitRate: rate })),
  // Only with an ffmpeg that encodes it (libfdk_aac, or macOS): otherwise the server sends AAC.
  ...[96, 64, 48].map((rate) => ({ format: 'HE_AAC' as TranscodeFormat, maxBitRate: rate })),
];

const key = (quality: Quality): string =>
  `${quality.format ?? 'ORIGINAL'}:${quality.maxBitRate ?? ''}`;

/**
 * What each kind of Ultimate client (desktop, web, mobile) plays by default: the apps take it as their default, and
 * the server applies it to their streams when they ask for no quality of their own.
 */
@Component({
  selector: 'app-client-quality-card',
  imports: [FormsModule, HlmButtonImports, HlmCardImports, HlmNativeSelectImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'transcoding.clients.title' | t }}</h2>
        <p hlmCardDescription>{{ 'transcoding.clients.description' | t }}</p>
      </div>
      @if (loaded()) {
        <div hlmCardContent class="grid gap-4 text-sm">
          @for (client of clients; track client) {
            <label class="grid gap-1 sm:grid-cols-[12rem_1fr] sm:items-center">
              <span class="grid">
                <span class="font-medium">{{ 'transcoding.clients.' + client + '.name' | t }}</span>
                <span class="text-xs text-muted-foreground">{{
                  'transcoding.clients.' + client + '.help' | t
                }}</span>
              </span>
              <select hlmNativeSelect [name]="client" [(ngModel)]="chosen[client]">
                @for (quality of qualities; track label(quality)) {
                  <option [value]="label(quality)">{{ describe(quality) }}</option>
                }
              </select>
            </label>
          }
          <p class="text-xs text-muted-foreground">{{ 'transcoding.clients.note' | t }}</p>
          <div>
            <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
              {{ 'common.save' | t }}
            </button>
          </div>
        </div>
      }
    </section>
  `,
})
export class ClientQualityCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly clients: Client[] = ['desktop', 'web', 'mobile'];
  protected readonly qualities = Qualities;
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly chosen: Record<Client, string> = { desktop: '', web: '', mobile: '' };
  /** This server's ffmpeg encodes HE-AAC (most builds do not). */
  protected readonly heAac = signal(false);

  async ngOnInit(): Promise<void> {
    const result = await this.gql.query(StreamingDefaultsQuery);
    this.heAac.set(result.serverInfo.transcodingFormats.includes('HE_AAC'));
    this.show(result.streamingDefaults);
  }

  protected label(quality: Quality): string {
    return key(quality);
  }

  protected describe(quality: Quality): string {
    if (!quality.format) return this.i18n.t('transcoding.clients.original');
    if (quality.format === 'FLAC') return this.i18n.t('transcoding.clients.flac');
    const name =
      quality.format === 'OPUS' ? 'Opus' : quality.format === 'HE_AAC' ? 'HE-AAC' : quality.format;
    const missing =
      quality.format === 'HE_AAC' && !this.heAac()
        ? ` (${this.i18n.t('transcoding.clients.heAacMissing')})`
        : '';
    return `${name} ${quality.maxBitRate} kbit/s${missing}`;
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      const input = Object.fromEntries(
        this.clients.map((client) => [client, this.parse(this.chosen[client])]),
      ) as Record<Client, Quality>;
      const result = await this.gql.mutate(SetStreamingDefaultsMutation, { input });
      this.show(result.setStreamingDefaults);
      toast.success(this.i18n.t('transcoding.clients.saved'));
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  private parse(value: string): Quality {
    const [format, rate] = value.split(':');
    return {
      format: format === 'ORIGINAL' ? null : (format as TranscodeFormat),
      maxBitRate: rate ? Number(rate) : null,
    };
  }

  private show(defaults: Defaults): void {
    for (const client of this.clients) {
      const current = {
        format: defaults[client].format ?? null,
        maxBitRate: defaults[client].maxBitRate ?? null,
      };
      // A value set through the API that the list does not offer is shown as the nearest one it does.
      this.chosen[client] = Qualities.some((q) => key(q) === key(current))
        ? key(current)
        : key(Qualities.find((q) => q.format === current.format) ?? Qualities[0]);
    }
    this.loaded.set(true);
  }
}
