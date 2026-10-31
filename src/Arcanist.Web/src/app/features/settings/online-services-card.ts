import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  OnlineService,
  OnlineServicesFieldsFragment as Status,
} from '../../graphql/generated/graphql';
import {
  OnlineServicesQuery,
  PrivacyQuery,
  SetAcoustIdKeyMutation,
  SetOnlineServicesMutation,
} from '../../graphql/operations';
import { CountPipe, RelativeTimePipe } from '../../shared/format';

type SettingKey = 'musicBrainz' | 'acoustId' | 'coverArtArchive' | 'lyrics';

const SETTING_OF: Record<OnlineService, SettingKey> = {
  MUSIC_BRAINZ: 'musicBrainz',
  ACOUST_ID: 'acoustId',
  COVER_ART_ARCHIVE: 'coverArtArchive',
  LYRICS: 'lyrics',
};

/** Online services: one switch each, what they have found, and the optional AcoustID key. */
@Component({
  selector: 'app-online-services-card',
  imports: [
    FormsModule,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSwitchImports,
    TranslatePipe,
    CountPipe,
    RelativeTimePipe,
  ],
  template: `
    <section hlmCard data-tour="settings-online">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.online.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.online.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="grid gap-4">
          @if (restricted()) {
            <p class="text-sm text-muted-foreground">{{ 'settings.online.restricted' | t }}</p>
          }
          @for (service of s.services; track service.service) {
            <div class="grid gap-1 rounded-lg border p-3">
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="font-medium">
                    {{ 'settings.online.service.' + service.service + '.name' | t }}
                  </p>
                  <p class="text-sm text-muted-foreground">
                    {{ 'settings.online.service.' + service.service + '.help' | t }}
                  </p>
                </div>
                <hlm-switch
                  [checked]="service.enabled"
                  [disabled]="busy() || restricted()"
                  (checkedChange)="toggle(service.service, $event)"
                  [aria-label]="'settings.online.service.' + service.service + '.name' | t"
                />
              </div>
              @if (service.enabled) {
                <p
                  class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground"
                >
                  @if (!service.ready) {
                    <span hlmBadge variant="destructive">{{ 'settings.online.needsKey' | t }}</span>
                  }
                  <span>{{ 'settings.online.found' | t: { count: (service.found | count) } }}</span>
                  <span>{{
                    'settings.online.notFound' | t: { count: (service.notFound | count) }
                  }}</span>
                  <span>{{
                    'settings.online.pending' | t: { count: (service.pending | count) }
                  }}</span>
                  @if (service.lastRequestAt) {
                    <span>{{
                      'settings.online.lastRequest'
                        | t: { when: (service.lastRequestAt | relativeTime) }
                    }}</span>
                  }
                  @if (service.lastError) {
                    <span class="text-destructive">{{
                      'settings.online.error.' + service.lastError | t
                    }}</span>
                  }
                </p>
              }
            </div>
          }

          <div hlmField>
            <label hlmFieldLabel for="acoustid-key">{{ 'settings.online.acoustIdKey' | t }}</label>
            <div class="flex flex-wrap gap-2">
              <input
                hlmInput
                id="acoustid-key"
                class="min-w-0 flex-1"
                [(ngModel)]="acoustIdKey"
                [placeholder]="
                  (s.hasOwnAcoustIdKey
                    ? 'settings.online.keySaved'
                    : 'settings.online.keyPlaceholder'
                  ) | t
                "
              />
              <button
                hlmBtn
                variant="outline"
                type="button"
                [disabled]="busy()"
                (click)="saveKey()"
              >
                {{ 'common.save' | t }}
              </button>
            </div>
            <p hlmFieldDescription>
              {{
                ((s.hasAcoustIdKey && !s.hasOwnAcoustIdKey) || s.hasOwnAcoustIdKey
                  ? 'settings.online.acoustIdKeyHelp'
                  : 'settings.online.acoustIdKeyNeeded'
                ) | t
              }}
            </p>
          </div>

          @if (error()) {
            <p class="text-sm text-destructive">{{ error() }}</p>
          }
        </div>
      }
    </section>
  `,
})
export class OnlineServicesCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly status = signal<Status | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected acoustIdKey = '';

  /** The privacy choices keep the online services off. */
  protected readonly restricted = signal(false);

  async ngOnInit(): Promise<void> {
    this.status.set((await this.gql.query(OnlineServicesQuery)).onlineServices);
    this.restricted.set((await this.gql.query(PrivacyQuery)).privacy.externalServices !== 'ALL');
  }

  protected async toggle(service: OnlineService, enabled: boolean): Promise<void> {
    const settings = this.status()?.settings;
    if (!settings) return;
    const input = {
      musicBrainz: settings.musicBrainz,
      acoustId: settings.acoustId,
      coverArtArchive: settings.coverArtArchive,
      lyrics: settings.lyrics,
      [SETTING_OF[service]]: enabled,
    };
    await this.run(async () => {
      this.status.set(
        (await this.gql.mutate(SetOnlineServicesMutation, { input })).setOnlineServices,
      );
    });
  }

  protected async saveKey(): Promise<void> {
    await this.run(async () => {
      const key = this.acoustIdKey.trim() || null;
      this.status.set(
        (await this.gql.mutate(SetAcoustIdKeyMutation, { input: { key } })).setAcoustIdKey,
      );
      this.acoustIdKey = '';
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
