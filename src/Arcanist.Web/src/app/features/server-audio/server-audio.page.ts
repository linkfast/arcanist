import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePause,
  lucidePlay,
  lucideSkipBack,
  lucideSkipForward,
  lucideSquare,
  lucideVolume2,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { HostHelp } from '../../shared/host-help';
import type {
  DlnaAlbumSearchQuery as AlbumSearchResult,
  DlnaRendererAction,
  DlnaRendererStateFieldsFragment as RendererState,
  ServerAudioStatusFieldsFragment as Status,
} from '../../graphql/generated/graphql';
import {
  ControlDlnaRendererMutation,
  DlnaAlbumSearchQuery,
  DlnaAlbumTracksQuery,
  DlnaRendererQuery,
  PlayOnDlnaRendererMutation,
  ServerAudioQuery,
  SetDlnaRendererVolumeMutation,
  SetExtensionEnabledMutation,
  SetServerAudioSettingsMutation,
  TestServerAudioMutation,
} from '../../graphql/operations';

type Album = AlbumSearchResult['albums']['items'][number];

/** The server's renderer in "play to". */
const Server = 'server';

/**
 * Server audio: the server plays on an audio device attached to it (speakers, a USB DAC, HDMI). Administrators choose
 * the device and its name; it is a device of "play to" and of Ultimate Connect, which this page also controls.
 */
@Component({
  selector: 'app-server-audio-page',
  imports: [
    FormsModule,
    NgIcon,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSwitchImports,
    TranslatePipe,
    HostHelp,
  ],
  providers: [
    provideIcons({
      lucidePause,
      lucidePlay,
      lucideSkipBack,
      lucideSkipForward,
      lucideSquare,
      lucideVolume2,
    }),
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'serverAudio.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'serverAudio.subtitle' | t }}</p>
    </header>

    @if (status(); as current) {
      <div class="grid gap-6">
        <section hlmCard data-tour="server-audio-output">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'serverAudio.output.title' | t }}</h2>
            <p hlmCardDescription>{{ 'serverAudio.output.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-4">
            <label class="flex items-center gap-2 text-sm font-medium">
              <hlm-switch
                [checked]="current.enabled"
                [disabled]="busy()"
                (checkedChange)="setEnabled($event)"
              />
              {{ (current.enabled ? 'serverAudio.output.on' : 'serverAudio.output.off') | t }}
            </label>

            @if (!current.available) {
              <div hlmAlert variant="destructive">
                <h3 hlmAlertTitle>{{ 'serverAudio.unavailable.title' | t }}</h3>
                <p hlmAlertDescription>
                  {{ 'serverAudio.unavailable.description' | t }}
                  @if (current.unavailableReason) {
                    <code class="mt-1 block break-all text-xs">{{
                      current.unavailableReason
                    }}</code>
                  }
                </p>
              </div>
            } @else if (current.device) {
              <p class="flex flex-wrap items-center gap-2 text-sm">
                <span hlmBadge variant="secondary">{{ 'serverAudio.output.playsOn' | t }}</span>
                {{ current.device }}
              </p>
            } @else if (current.deviceError) {
              <div hlmAlert>
                <h3 hlmAlertTitle>{{ 'serverAudio.output.noDevice' | t }}</h3>
                <p hlmAlertDescription>
                  {{ 'serverAudio.output.noDeviceHelp' | t }}
                  <code class="mt-1 block break-all text-xs">{{ current.deviceError }}</code>
                </p>
                <app-host-help class="mt-2" topic="audio" />
              </div>
            }

            @if (current.available && current.devices.length === 0) {
              <p class="text-sm text-muted-foreground">{{ 'serverAudio.output.noDevices' | t }}</p>
              <app-host-help topic="audio" />
            }

            @if (current.connectAccounts > 0) {
              <p class="text-xs text-muted-foreground">
                {{ 'serverAudio.output.connect' | t: { count: current.connectAccounts } }}
              </p>
            }
          </div>
        </section>

        @if (current.enabled && current.available) {
          <section hlmCard data-tour="server-audio-device">
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'serverAudio.settings.title' | t }}</h2>
              <p hlmCardDescription>{{ 'serverAudio.settings.description' | t }}</p>
            </div>
            <div hlmCardContent class="grid gap-4">
              <hlm-field>
                <label hlmFieldLabel for="serverAudioDevice">{{
                  'serverAudio.settings.device' | t
                }}</label>
                <hlm-native-select
                  id="serverAudioDevice"
                  [value]="deviceId"
                  (change)="deviceId = $any($event.target).value"
                >
                  <option hlmNativeSelectOption value="">
                    {{ 'serverAudio.settings.systemDefault' | t }}
                  </option>
                  @for (device of current.devices; track device.id) {
                    <option hlmNativeSelectOption [value]="device.id">
                      {{ device.name }}
                      @if (device.isDefault) {
                        ({{ 'serverAudio.settings.default' | t }})
                      }
                    </option>
                  }
                </hlm-native-select>
                <p hlmFieldDescription>{{ 'serverAudio.settings.deviceHelp' | t }}</p>
              </hlm-field>
              <hlm-field>
                <label hlmFieldLabel for="serverAudioName">{{
                  'serverAudio.settings.name' | t
                }}</label>
                <input
                  hlmInput
                  id="serverAudioName"
                  name="serverAudioName"
                  maxlength="64"
                  [placeholder]="current.name"
                  [(ngModel)]="name"
                />
                <p hlmFieldDescription>{{ 'serverAudio.settings.nameHelp' | t }}</p>
              </hlm-field>
            </div>
            <div hlmCardFooter class="flex flex-wrap gap-2">
              <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
                {{ 'common.save' | t }}
              </button>
              <button
                hlmBtn
                variant="outline"
                type="button"
                [disabled]="busy() || !current.device || playing()?.state === 'PLAYING'"
                (click)="test()"
              >
                <ng-icon name="lucideVolume2" />
                {{ 'serverAudio.settings.test' | t }}
              </button>
            </div>
          </section>

          <section hlmCard data-tour="server-audio-playing">
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'serverAudio.playing.title' | t }}</h2>
              <p hlmCardDescription>{{ 'serverAudio.playing.description' | t }}</p>
            </div>
            <div hlmCardContent class="grid gap-3">
              @if (playing(); as state) {
                <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
                  @if (state.track; as track) {
                    <p>
                      <span class="font-medium">{{ track.title }}</span>
                      <span class="text-muted-foreground">
                        · {{ artistsOf(track) }} · {{ track.album.title }}</span
                      >
                    </p>
                  } @else {
                    <p class="text-muted-foreground">{{ 'serverAudio.playing.nothing' | t }}</p>
                  }
                  <span hlmBadge variant="secondary">{{ 'dlna.states.' + state.state | t }}</span>
                </div>
                @if (state.track) {
                  <p class="text-xs text-muted-foreground">
                    {{ time(state.positionMs) }} / {{ time(state.durationMs) }} ·
                    {{ state.queueIndex + 1 }} / {{ state.queue.length }}
                    @if (!state.startedByYou) {
                      · {{ 'serverAudio.playing.someoneElse' | t }}
                    }
                  </p>
                }
                <div class="flex flex-wrap items-center gap-1">
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'dlna.actions.PREVIOUS' | t"
                    (click)="control('PREVIOUS')"
                  >
                    <ng-icon name="lucideSkipBack" />
                  </button>
                  @if (state.state === 'PLAYING') {
                    <button
                      hlmBtn
                      variant="outline"
                      size="icon"
                      type="button"
                      [attr.aria-label]="'dlna.actions.PAUSE' | t"
                      (click)="control('PAUSE')"
                    >
                      <ng-icon name="lucidePause" />
                    </button>
                  } @else {
                    <button
                      hlmBtn
                      variant="outline"
                      size="icon"
                      type="button"
                      [attr.aria-label]="'dlna.actions.PLAY' | t"
                      [disabled]="state.queue.length === 0"
                      (click)="control('PLAY')"
                    >
                      <ng-icon name="lucidePlay" />
                    </button>
                  }
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'dlna.actions.STOP' | t"
                    (click)="control('STOP')"
                  >
                    <ng-icon name="lucideSquare" />
                  </button>
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'dlna.actions.NEXT' | t"
                    (click)="control('NEXT')"
                  >
                    <ng-icon name="lucideSkipForward" />
                  </button>
                  @if (state.volume !== null && state.volume !== undefined) {
                    <label class="ml-3 flex items-center gap-2 text-sm">
                      {{ 'dlna.renderers.volume' | t }}
                      <input
                        type="range"
                        min="0"
                        max="100"
                        [value]="state.volume"
                        (change)="setVolume($any($event.target).valueAsNumber)"
                      />
                    </label>
                  }
                </div>
              }

              <form class="flex flex-wrap items-end gap-2" (ngSubmit)="searchAlbums()">
                <hlm-field class="min-w-48 flex-1">
                  <label hlmFieldLabel for="serverAudioAlbum">{{
                    'dlna.renderers.playAlbum' | t
                  }}</label>
                  <input
                    hlmInput
                    id="serverAudioAlbum"
                    name="serverAudioAlbum"
                    [(ngModel)]="albumSearch"
                  />
                </hlm-field>
                <button hlmBtn variant="outline" type="submit" [disabled]="!albumSearch.trim()">
                  {{ 'dlna.renderers.search' | t }}
                </button>
              </form>
              <ul class="grid gap-1">
                @for (album of albums(); track album.id) {
                  <li class="flex items-center justify-between gap-2 text-sm">
                    <span class="min-w-0 truncate"
                      >{{ album.title }} · {{ album.artist.name }}</span
                    >
                    <button
                      hlmBtn
                      size="sm"
                      type="button"
                      [disabled]="busy()"
                      (click)="playAlbum(album)"
                    >
                      {{ 'dlna.actions.PLAY' | t }}
                    </button>
                  </li>
                }
              </ul>
            </div>
          </section>
        }
      </div>
    }
  `,
})
export class ServerAudioPage implements OnInit, OnDestroy {
  private readonly gql = inject(Gql);
  private readonly router = inject(Router);
  private readonly enabledExtensions = inject(EnabledExtensions);
  private readonly i18n = inject(I18n);

  protected readonly status = signal<Status | null>(null);
  protected readonly playing = signal<RendererState | null>(null);
  protected readonly albums = signal<Album[]>([]);
  protected readonly busy = signal(false);
  protected deviceId = '';
  protected name = '';
  protected albumSearch = '';

  private poll: ReturnType<typeof setInterval> | undefined;

  async ngOnInit(): Promise<void> {
    await this.load();
    // Follows what plays (and the device) while the page is open.
    this.poll = setInterval(() => void this.refresh(), 2000);
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
  }

  private async load(): Promise<void> {
    const status = (await this.gql.query(ServerAudioQuery)).serverAudio;
    this.status.set(status);
    this.deviceId = status.deviceId ?? '';
    this.name = status.customName ?? '';
    await this.loadPlaying();
  }

  private async refresh(): Promise<void> {
    try {
      const status = (await this.gql.query(ServerAudioQuery)).serverAudio;
      this.status.set(status);
      await this.loadPlaying();
    } catch {
      clearInterval(this.poll);
    }
  }

  private async loadPlaying(): Promise<void> {
    const status = this.status();
    if (!status?.enabled || !status.available) {
      this.playing.set(null);
      return;
    }
    this.playing.set((await this.gql.query(DlnaRendererQuery, { id: Server })).dlnaRenderer);
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(SetExtensionEnabledMutation, { id: 'server-audio', enabled });
      await this.enabledExtensions.refresh();
      // Off, the page leaves the menu: it is switched on again from Extensions.
      if (!enabled) {
        await this.router.navigateByUrl('/extensions');
        return;
      }
      await this.load();
    });
  }

  protected async save(): Promise<void> {
    await this.run(async () => {
      const status = (
        await this.gql.mutate(SetServerAudioSettingsMutation, {
          input: { deviceId: this.deviceId || null, name: this.name.trim() || null },
        })
      ).setServerAudioSettings;
      this.status.set(status);
      this.deviceId = status.deviceId ?? '';
      this.name = status.customName ?? '';
      toast.success(this.i18n.t('serverAudio.saved'));
    });
  }

  protected async test(): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(TestServerAudioMutation);
    });
  }

  protected async control(action: DlnaRendererAction): Promise<void> {
    await this.run(async () => {
      this.playing.set(
        (
          await this.gql.mutate(ControlDlnaRendererMutation, {
            input: { rendererId: Server, action },
          })
        ).controlDlnaRenderer,
      );
    });
  }

  protected async setVolume(volume: number): Promise<void> {
    await this.run(async () => {
      this.playing.set(
        (await this.gql.mutate(SetDlnaRendererVolumeMutation, { rendererId: Server, volume }))
          .setDlnaRendererVolume,
      );
    });
  }

  protected async searchAlbums(): Promise<void> {
    this.albums.set(
      (await this.gql.query(DlnaAlbumSearchQuery, { search: this.albumSearch.trim() })).albums
        .items,
    );
  }

  protected async playAlbum(album: Album): Promise<void> {
    await this.run(async () => {
      const tracks = (await this.gql.query(DlnaAlbumTracksQuery, { albumId: album.id })).tracks
        .items;
      this.playing.set(
        (
          await this.gql.mutate(PlayOnDlnaRendererMutation, {
            input: { rendererId: Server, trackIds: tracks.map((t) => t.id) },
          })
        ).playOnDlnaRenderer,
      );
    });
  }

  protected artistsOf(track: NonNullable<RendererState['track']>): string {
    return track.artists
      .filter((a) => a.role === 'PRIMARY')
      .map((a) => a.name)
      .join(', ');
  }

  protected time(milliseconds: number): string {
    const seconds = Math.floor(milliseconds / 1000);
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
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
