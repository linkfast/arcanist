import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { RouterLink } from '@angular/router';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  RemoteLibrarySettingsFieldsFragment as Settings,
  RemoteTranscoding,
} from '../../graphql/generated/graphql';
import {
  ClearRemoteLibraryCacheMutation,
  RemoteLibrarySettingsQuery,
  SetRemoteLibrarySettingsMutation,
} from '../../graphql/operations';
import { BytesPipe, CountPipe } from '../../shared/format';

const GB = 1_000_000_000;
const Sizes = [1, 2, 5, 10, 20, 50, 100, 200].map((gb) => gb * GB);
const Ages = [7, 30, 60, 180, 365];

/** How music other servers share is played here: its cache (where it is converted is chosen in Transcoding). */
@Component({
  selector: 'app-remote-libraries-card',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmCardImports,
    HlmNativeSelectImports,
    RouterLink,
    HlmSwitchImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'remoteLibraries.title' | t }}</h2>
        <p hlmCardDescription>{{ 'remoteLibraries.description' | t }}</p>
      </div>
      @if (settings(); as s) {
        <div hlmCardContent class="grid gap-5 text-sm">
          <div class="grid gap-2">
            <label class="flex items-center gap-3">
              <hlm-switch [(checked)]="cacheEnabled" />
              {{ 'remoteLibraries.cache' | t }}
            </label>
            <p class="text-xs text-muted-foreground">{{ 'remoteLibraries.cacheHelp' | t }}</p>
            @if (cacheEnabled) {
              <div class="flex flex-wrap items-center gap-x-6 gap-y-3">
                <label class="flex items-center gap-2">
                  {{ 'remoteLibraries.size' | t }}
                  <select hlmNativeSelect [(ngModel)]="cacheMaxBytes">
                    @for (size of sizes; track size) {
                      <option [ngValue]="size">{{ size | bytes }}</option>
                    }
                  </select>
                </label>
                <label class="flex items-center gap-2">
                  {{ 'remoteLibraries.age' | t }}
                  <select hlmNativeSelect [(ngModel)]="cacheMaxAgeDays">
                    @for (age of ages; track age) {
                      <option [ngValue]="age">
                        {{ 'transcoding.cache.days' | t: { count: age } }}
                      </option>
                    }
                  </select>
                </label>
              </div>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="text-xs text-muted-foreground">
                  {{
                    'remoteLibraries.usage'
                      | t: { bytes: (s.cacheBytes | bytes), count: (s.cacheFiles | count) }
                  }}
                </p>
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="busy() || s.cacheFiles === 0"
                  (click)="clear()"
                >
                  {{ 'remoteLibraries.clear' | t }}
                </button>
              </div>
            }
          </div>

          <p class="text-xs text-muted-foreground">
            {{ 'remoteLibraries.transcodingMoved' | t }}
            <a class="underline" routerLink="/transcoding">{{ 'nav.transcoding' | t }}</a>
          </p>

          <div>
            <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
              {{ 'remoteLibraries.save' | t }}
            </button>
          </div>
        </div>
      }
    </section>
  `,
})
export class RemoteLibrariesCard implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly sizes = Sizes;
  protected readonly ages = Ages;
  protected readonly settings = signal<Settings | null>(null);
  protected readonly busy = signal(false);

  protected cacheEnabled = true;
  protected cacheMaxBytes = 10 * GB;
  protected cacheMaxAgeDays = 60;
  /** Kept as it is: it is chosen in Transcoding. */
  protected transcoding: RemoteTranscoding = 'AUTO';

  async ngOnInit(): Promise<void> {
    this.show((await this.gql.query(RemoteLibrarySettingsQuery)).remoteLibrarySettings);
  }

  protected save(): Promise<void> {
    return this.run(async () => {
      const result = await this.gql.mutate(SetRemoteLibrarySettingsMutation, {
        input: {
          cacheEnabled: this.cacheEnabled,
          cacheMaxBytes: this.cacheMaxBytes,
          cacheMaxAgeDays: this.cacheMaxAgeDays,
          transcoding: this.transcoding,
        },
      });
      this.show(result.setRemoteLibrarySettings);
      toast.success(this.i18n.t('remoteLibraries.saved'));
    });
  }

  protected clear(): Promise<void> {
    return this.run(async () => {
      const result = await this.gql.mutate(ClearRemoteLibraryCacheMutation);
      this.settings.set(result.clearRemoteLibraryCache);
      toast.success(this.i18n.t('remoteLibraries.cleared'));
    });
  }

  private show(settings: Settings): void {
    this.settings.set(settings);
    this.cacheEnabled = settings.cacheEnabled;
    this.cacheMaxBytes = Sizes.reduce((best, size) =>
      Math.abs(size - settings.cacheMaxBytes) < Math.abs(best - settings.cacheMaxBytes)
        ? size
        : best,
    );
    this.cacheMaxAgeDays = Ages.includes(settings.cacheMaxAgeDays) ? settings.cacheMaxAgeDays : 60;
    this.transcoding = settings.transcoding;
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
