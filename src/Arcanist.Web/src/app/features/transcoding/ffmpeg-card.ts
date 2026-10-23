import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { FfmpegStatusFieldsFragment } from '../../graphql/generated/graphql';
import {
  FfmpegQuery,
  InstallFfmpegMutation,
  RemoveDownloadedFfmpegMutation,
  SetFfmpegPathMutation,
} from '../../graphql/operations';
import { BytesPipe } from '../../shared/format';

/** Which ffmpeg Arcanist uses, a different path, or a download into the data directory. */
@Component({
  selector: 'app-ffmpeg-card',
  imports: [
    FormsModule,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmFieldImports,
    HlmInputImports,
    HlmAlertImports,
    HlmProgressImports,
    TranslatePipe,
    BytesPipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>ffmpeg</h2>
        <p hlmCardDescription>{{ 'settings.ffmpeg.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="space-y-4">
          <div class="flex flex-wrap items-center gap-2 text-sm">
            <span hlmBadge [variant]="s.available ? 'default' : 'destructive'">
              {{ (s.available ? 'settings.ffmpeg.available' : 'settings.ffmpeg.missing') | t }}
            </span>
            @if (s.available) {
              <span class="min-w-0 break-all text-muted-foreground">
                {{ s.version }} · {{ 'settings.ffmpeg.source.' + s.source | t }} · {{ s.path }}
              </span>
            }
          </div>

          @if (s.available && s.outdated && s.source !== 'DOWNLOADED') {
            <div hlmAlert>
              <p hlmAlertTitle>{{ 'settings.ffmpeg.outdatedTitle' | t }}</p>
              <p hlmAlertDescription>
                {{
                  (s.canDownload
                    ? 'settings.ffmpeg.outdated'
                    : 'settings.ffmpeg.outdatedNoDownload'
                  ) | t: { version: s.version ?? '', recommended: s.recommendedVersion }
                }}
              </p>
            </div>
          }

          @if (s.configuredPath && !s.configuredPathWorks) {
            <div hlmAlert variant="destructive">
              <p hlmAlertDescription class="break-all">
                {{ 'settings.ffmpeg.configuredBroken' | t: { path: s.configuredPath } }}
              </p>
            </div>
          }

          @if (auth.isAdmin()) {
            <div hlmField>
              <label hlmFieldLabel for="ffmpeg-path">{{ 'settings.ffmpeg.path' | t }}</label>
              <div class="flex flex-wrap gap-2">
                <input
                  hlmInput
                  id="ffmpeg-path"
                  class="min-w-0 flex-1"
                  [(ngModel)]="path"
                  [placeholder]="'settings.ffmpeg.automatic' | t"
                />
                <button
                  hlmBtn
                  variant="outline"
                  type="button"
                  [disabled]="busy()"
                  (click)="savePath()"
                >
                  {{ 'common.save' | t }}
                </button>
              </div>
              <p hlmFieldDescription>{{ 'settings.ffmpeg.pathHelp' | t }}</p>
            </div>

            @if (downloading()) {
              <div class="space-y-2">
                <p class="text-sm">
                  {{
                    'settings.ffmpeg.downloading'
                      | t
                        : {
                            received: (s.download.bytesReceived | bytes),
                            total: (s.download.totalBytes | bytes),
                          }
                  }}
                </p>
                <hlm-progress [value]="downloadPercent()" class="h-2">
                  <hlm-progress-indicator />
                </hlm-progress>
              </div>
            } @else if (s.canDownload) {
              <div class="flex flex-wrap items-center gap-3">
                <button
                  hlmBtn
                  [variant]="s.available ? 'outline' : 'default'"
                  type="button"
                  [disabled]="busy()"
                  (click)="install()"
                >
                  {{
                    (s.source === 'DOWNLOADED'
                      ? 'settings.ffmpeg.update'
                      : 'settings.ffmpeg.download'
                    ) | t
                  }}
                </button>
                <p class="text-xs text-muted-foreground">
                  {{ 'settings.ffmpeg.downloadHelp' | t }}
                </p>
              </div>
            }
            @if (s.downloaded && !downloading()) {
              <div class="flex flex-wrap items-center gap-3">
                @if (confirmingRemove()) {
                  <span class="text-sm">{{ 'settings.ffmpeg.removeConfirm' | t }}</span>
                  <button
                    hlmBtn
                    variant="destructive"
                    size="sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="remove()"
                  >
                    {{ 'settings.ffmpeg.remove' | t }}
                  </button>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    type="button"
                    (click)="confirmingRemove.set(false)"
                  >
                    {{ 'common.cancel' | t }}
                  </button>
                } @else {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="confirmingRemove.set(true)"
                  >
                    {{ 'settings.ffmpeg.remove' | t }}
                  </button>
                  <p class="text-xs text-muted-foreground">
                    {{ 'settings.ffmpeg.removeHelp' | t }}
                  </p>
                }
              </div>
            }
            @if (s.download.state === 'FAILED' && s.download.error) {
              <p class="text-sm text-destructive">{{ i18n.error(s.download.error) }}</p>
            }
            @if (s.download.state === 'COMPLETED') {
              <p class="text-sm">
                {{ 'settings.ffmpeg.installed' | t: { version: s.download.version ?? '' } }}
              </p>
            }
          }

          @if (error()) {
            <p class="text-sm text-destructive">{{ error() }}</p>
          }
        </div>
      }
    </section>
  `,
})
export class FfmpegCard implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly status = signal<FfmpegStatusFieldsFragment | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected path = '';

  protected readonly downloading = computed(() =>
    ['DOWNLOADING', 'INSTALLING'].includes(this.status()?.download.state ?? ''),
  );
  protected readonly downloadPercent = computed(() => {
    const download = this.status()?.download;
    return download?.totalBytes
      ? Math.round((download.bytesReceived / download.totalBytes) * 100)
      : null;
  });

  private poll: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  async ngOnInit(): Promise<void> {
    await this.refresh();
    this.path = this.status()?.configuredPath ?? '';
  }

  protected async savePath(): Promise<void> {
    await this.run(async () => {
      const result = await this.gql.mutate(SetFfmpegPathMutation, {
        path: this.path.trim() || null,
      });
      this.status.set(result.setFfmpegPath);
    });
  }

  protected readonly confirmingRemove = signal(false);

  /** Deletes the downloaded build; the system's (or the configured one) takes its place. */
  protected async remove(): Promise<void> {
    this.confirmingRemove.set(false);
    await this.run(async () => {
      this.status.set(
        (await this.gql.mutate(RemoveDownloadedFfmpegMutation, {})).removeDownloadedFfmpeg,
      );
    });
  }

  protected async install(): Promise<void> {
    await this.run(async () => {
      const result = await this.gql.mutate(InstallFfmpegMutation, {});
      this.status.set(result.installFfmpeg);
      this.startPolling();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(this.i18n.error(apiError.fields['path']?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  private async refresh(): Promise<void> {
    this.status.set((await this.gql.query(FfmpegQuery)).ffmpeg);
    if (!this.downloading()) this.stopPolling();
  }

  /** The download runs on the server; poll it once a second while it does. */
  private startPolling(): void {
    this.stopPolling();
    this.poll = setInterval(() => void this.refresh(), 1000);
  }

  private stopPolling(): void {
    if (this.poll) clearInterval(this.poll);
    this.poll = null;
  }
}
