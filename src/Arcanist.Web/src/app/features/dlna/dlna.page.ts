import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown,
  lucideArrowUp,
  lucidePause,
  lucidePlay,
  lucideRefreshCw,
  lucideSkipBack,
  lucideSkipForward,
  lucideSquare,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  DlnaAlbumSearchQuery as AlbumSearchResult,
  DlnaRendererAction,
  DlnaRendererFieldsFragment as Renderer,
  DlnaRendererStateFieldsFragment as RendererState,
  DlnaSection,
  DlnaStatusFieldsFragment as Status,
} from '../../graphql/generated/graphql';
import {
  AddDlnaRendererMutation,
  ControlDlnaRendererMutation,
  DlnaAlbumSearchQuery,
  DlnaAlbumTracksQuery,
  DlnaQuery,
  DlnaRendererQuery,
  DlnaRenderersQuery,
  PlayOnDlnaRendererMutation,
  SetDlnaRecordPlaysMutation,
  SetDlnaRendererVolumeMutation,
  SetDlnaSettingsMutation,
  SetExtensionEnabledMutation,
} from '../../graphql/operations';

type Album = AlbumSearchResult['albums']['items'][number];

const AllSections: DlnaSection[] = [
  'ARTISTS',
  'ALBUMS',
  'GENRES',
  'DECADES',
  'FOLDERS',
  'PLAYLISTS',
  'RECENTLY_ADDED',
  'MIXES',
  'STARRED',
  'RATED',
];

/**
 * DLNA: the media server devices on the local network browse (what it publishes and how its tree looks) and
 * "play to" renderers.
 */
@Component({
  selector: 'app-dlna-page',
  imports: [
    FormsModule,
    NgIcon,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSwitchImports,
    HlmNativeSelectImports,
    TranslatePipe,
  ],
  providers: [
    provideIcons({
      lucideArrowDown,
      lucideArrowUp,
      lucidePause,
      lucidePlay,
      lucideRefreshCw,
      lucideSkipBack,
      lucideSkipForward,
      lucideSquare,
    }),
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'dlna.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'dlna.subtitle' | t }}</p>
    </header>

    @if (status(); as current) {
      <div class="grid gap-6">
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'dlna.server.title' | t }}</h2>
            <p hlmCardDescription>{{ 'dlna.server.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-4">
            <label class="flex items-center gap-2 text-sm font-medium">
              <hlm-switch
                [checked]="current.enabled"
                [disabled]="busy()"
                (checkedChange)="setEnabled($event)"
              />
              {{ (current.enabled ? 'dlna.server.on' : 'dlna.server.off') | t }}
            </label>
            @if (current.enabled) {
              <div class="grid gap-1 text-xs text-muted-foreground">
                @for (address of current.addresses; track address) {
                  <code class="break-all">{{ address }}</code>
                } @empty {
                  <p>{{ 'dlna.server.noNetwork' | t }}</p>
                }
              </div>
            }
            @if (!current.convertsFormats) {
              <p class="text-xs text-muted-foreground">{{ 'dlna.server.noFfmpeg' | t }}</p>
            }
            <div class="grid gap-1">
              <label class="flex items-center gap-2 text-sm font-medium">
                <hlm-switch
                  [checked]="current.recordPlays"
                  [disabled]="busy()"
                  (checkedChange)="setRecordPlays($event)"
                />
                {{
                  (current.recordPlays ? 'dlna.server.recordPlays' : 'dlna.server.playsPaused') | t
                }}
              </label>
              <p class="text-xs text-muted-foreground">{{ 'dlna.server.recordPlaysHint' | t }}</p>
            </div>
          </div>
        </section>

        <!-- Everything devices see, saved together. -->
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'dlna.settings.title' | t }}</h2>
            <p hlmCardDescription>{{ 'dlna.settings.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-6">
            <hlm-field>
              <label hlmFieldLabel for="dlnaName">{{ 'dlna.server.name' | t }}</label>
              <input
                hlmInput
                id="dlnaName"
                name="dlnaName"
                maxlength="64"
                [placeholder]="current.name"
                [(ngModel)]="friendlyName"
              />
            </hlm-field>

            <div class="grid gap-2">
              <h3 class="text-sm font-medium">{{ 'dlna.libraries.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">{{ 'dlna.libraries.description' | t }}</p>
              @for (library of libraries(); track library.id) {
                <label class="flex items-center gap-2 text-sm">
                  <hlm-checkbox
                    [checked]="published().includes(library.id)"
                    (checkedChange)="toggleLibrary(library.id, $event)"
                  />
                  {{ library.name }}
                </label>
              } @empty {
                <p class="text-sm text-muted-foreground">{{ 'dlna.libraries.none' | t }}</p>
              }
            </div>

            <div class="grid gap-2">
              <h3 class="text-sm font-medium">{{ 'dlna.sections.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">{{ 'dlna.sections.description' | t }}</p>
              <ul class="grid gap-2">
                @for (section of orderedSections(); track section; let index = $index) {
                  <li
                    class="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
                  >
                    <label class="flex items-center gap-2">
                      <hlm-checkbox
                        [checked]="sections().includes(section)"
                        (checkedChange)="toggleSection(section, $event)"
                      />
                      {{ 'dlna.sections.names.' + section | t }}
                      @if (section === 'RATED' && !fiveStars()) {
                        <span class="text-xs text-muted-foreground">{{
                          'dlna.sections.onlyFiveStars' | t
                        }}</span>
                      }
                    </label>
                    @if (sections().includes(section)) {
                      <span class="flex gap-1">
                        <button
                          hlmBtn
                          variant="ghost"
                          size="icon"
                          type="button"
                          [attr.aria-label]="'dlna.sections.up' | t"
                          [disabled]="sections().indexOf(section) === 0"
                          (click)="move(section, -1)"
                        >
                          <ng-icon name="lucideArrowUp" />
                        </button>
                        <button
                          hlmBtn
                          variant="ghost"
                          size="icon"
                          type="button"
                          [attr.aria-label]="'dlna.sections.down' | t"
                          [disabled]="sections().indexOf(section) === sections().length - 1"
                          (click)="move(section, 1)"
                        >
                          <ng-icon name="lucideArrowDown" />
                        </button>
                      </span>
                    }
                  </li>
                }
              </ul>
            </div>
          </div>
          @if (sections().includes('STARRED') || sections().includes('RATED')) {
            <hlm-field class="px-6 pb-2">
              <label hlmFieldLabel for="dlnaRatingsUser">{{
                'dlna.sections.ratingsUser' | t
              }}</label>
              <hlm-native-select
                id="dlnaRatingsUser"
                [value]="ratingsUser() ?? ''"
                (change)="ratingsUser.set($any($event.target).value || null)"
              >
                @for (user of users(); track user.id) {
                  <option hlmNativeSelectOption [value]="user.id">
                    {{ user.displayName || user.userName }}
                  </option>
                }
              </hlm-native-select>
              <p hlmFieldDescription>{{ 'dlna.sections.ratingsUserHelp' | t }}</p>
            </hlm-field>
          }
          <div hlmCardFooter>
            <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
              {{ 'common.save' | t }}
            </button>
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'dlna.renderers.title' | t }}</h2>
            <p hlmCardDescription>{{ 'dlna.renderers.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-4">
            @if (!current.enabled) {
              <p class="text-sm text-muted-foreground">{{ 'dlna.renderers.off' | t }}</p>
            } @else {
              <div class="flex flex-wrap items-center gap-2">
                @for (renderer of renderers(); track renderer.id) {
                  <button
                    hlmBtn
                    size="sm"
                    type="button"
                    [variant]="selected()?.renderer?.id === renderer.id ? 'default' : 'outline'"
                    (click)="select(renderer)"
                  >
                    {{ renderer.name }}
                  </button>
                } @empty {
                  <span class="text-sm text-muted-foreground">{{ 'dlna.renderers.none' | t }}</span>
                }
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon"
                  type="button"
                  [attr.aria-label]="'dlna.renderers.refresh' | t"
                  (click)="loadRenderers()"
                >
                  <ng-icon name="lucideRefreshCw" />
                </button>
              </div>

              <form class="flex flex-wrap items-end gap-2" (ngSubmit)="addRenderer()">
                <hlm-field class="min-w-64 flex-1">
                  <label hlmFieldLabel for="rendererUrl">{{ 'dlna.renderers.addUrl' | t }}</label>
                  <input
                    hlmInput
                    id="rendererUrl"
                    name="rendererUrl"
                    type="url"
                    placeholder="http://192.168.1.30:49152/description.xml"
                    [(ngModel)]="rendererUrl"
                  />
                </hlm-field>
                <button
                  hlmBtn
                  variant="outline"
                  type="submit"
                  [disabled]="busy() || !rendererUrl.trim()"
                >
                  {{ 'common.add' | t }}
                </button>
              </form>

              @if (selected(); as playing) {
                <div class="grid gap-3 rounded-md border p-3">
                  <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
                    <span>
                      <span class="font-medium">{{ playing.renderer.name }}</span>
                      @if (playing.renderer.model) {
                        <span class="text-muted-foreground"> · {{ playing.renderer.model }}</span>
                      }
                    </span>
                    <span hlmBadge variant="secondary">{{
                      'dlna.states.' + playing.state | t
                    }}</span>
                  </div>
                  @if (playing.track; as track) {
                    <p class="text-sm">
                      <span class="font-medium">{{ track.title }}</span>
                      <span class="text-muted-foreground">
                        · {{ artistsOf(track) }} · {{ track.album.title }}</span
                      >
                    </p>
                    <p class="text-xs text-muted-foreground">
                      {{ time(playing.positionMs) }} / {{ time(playing.durationMs) }} ·
                      {{ playing.queueIndex + 1 }} / {{ playing.queue.length }}
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
                    @if (playing.state === 'PLAYING') {
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
                    @if (playing.volume !== null && playing.volume !== undefined) {
                      <label class="ml-3 flex items-center gap-2 text-sm">
                        {{ 'dlna.renderers.volume' | t }}
                        <input
                          type="range"
                          min="0"
                          max="100"
                          [value]="playing.volume"
                          (change)="setVolume($any($event.target).valueAsNumber)"
                        />
                      </label>
                    }
                  </div>

                  <form class="flex flex-wrap items-end gap-2" (ngSubmit)="searchAlbums()">
                    <hlm-field class="min-w-48 flex-1">
                      <label hlmFieldLabel for="albumSearch">{{
                        'dlna.renderers.playAlbum' | t
                      }}</label>
                      <input
                        hlmInput
                        id="albumSearch"
                        name="albumSearch"
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
              }
            }
          </div>
        </section>
      </div>
    }
  `,
})
export class DlnaPage implements OnInit, OnDestroy {
  private readonly gql = inject(Gql);
  private readonly enabledExtensions = inject(EnabledExtensions);
  private readonly i18n = inject(I18n);

  protected readonly status = signal<Status | null>(null);
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);
  protected readonly published = signal<string[]>([]);
  protected readonly users = signal<
    { id: string; userName: string; displayName?: string | null }[]
  >([]);
  /** Whose stars and ratings the StarTracks and rated folders show. */
  protected readonly ratingsUser = signal<string | null>(null);
  protected readonly fiveStars = signal(false);
  protected readonly sections = signal<DlnaSection[]>([]);
  protected readonly renderers = signal<Renderer[]>([]);
  protected readonly selected = signal<RendererState | null>(null);
  protected readonly albums = signal<Album[]>([]);
  protected readonly busy = signal(false);
  protected friendlyName = '';
  protected rendererUrl = '';
  protected albumSearch = '';

  /** The chosen sections in their order, then the others. */
  protected readonly orderedSections = computed(() => [
    ...this.sections(),
    ...AllSections.filter((s) => !this.sections().includes(s)),
  ]);

  private poll: ReturnType<typeof setInterval> | undefined;

  async ngOnInit(): Promise<void> {
    await this.load();
    if (this.status()?.enabled) await this.loadRenderers();
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
  }

  private async load(): Promise<void> {
    const result = await this.gql.query(DlnaQuery);
    this.apply(result.dlna);
    this.libraries.set(result.libraries);
    this.users.set(result.users);
    this.fiveStars.set(result.serverInfo.ratingMode === 'STARS');
  }

  private apply(status: Status): void {
    this.status.set(status);
    this.friendlyName = status.friendlyName ?? '';
    this.published.set([...status.libraryIds]);
    this.sections.set([...status.sections]);
    this.ratingsUser.set(status.ratingsUserId ?? null);
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(SetExtensionEnabledMutation, { id: 'dlna', enabled });
      await this.enabledExtensions.refresh();
      await this.load();
      if (enabled) await this.loadRenderers();
    });
  }

  protected async setRecordPlays(record: boolean): Promise<void> {
    await this.run(async () => {
      const result = await this.gql.mutate(SetDlnaRecordPlaysMutation, { record });
      this.status.update((current) =>
        current ? { ...current, recordPlays: result.setDlnaRecordPlays.recordPlays } : current,
      );
    });
  }

  protected toggleLibrary(id: string, checked: boolean): void {
    this.published.update((ids) => (checked ? [...ids, id] : ids.filter((i) => i !== id)));
  }

  protected toggleSection(section: DlnaSection, checked: boolean): void {
    this.sections.update((sections) =>
      checked ? [...sections, section] : sections.filter((s) => s !== section),
    );
  }

  protected move(section: DlnaSection, by: number): void {
    this.sections.update((sections) => {
      const list = [...sections];
      const index = list.indexOf(section);
      const target = index + by;
      if (index < 0 || target < 0 || target >= list.length) return sections;
      [list[index], list[target]] = [list[target], list[index]];
      return list;
    });
  }

  protected async save(): Promise<void> {
    await this.run(async () => {
      const result = await this.gql.mutate(SetDlnaSettingsMutation, {
        input: {
          friendlyName: this.friendlyName.trim() || null,
          // Only libraries that still exist (one may have been deleted meanwhile).
          libraryIds: this.published().filter((id) => this.libraries().some((l) => l.id === id)),
          sections: this.sections(),
          ratingsUserId: this.ratingsUser(),
        },
      });
      this.apply(result.setDlnaSettings);
      toast.success(this.i18n.t('dlna.saved'));
    });
  }

  protected async loadRenderers(): Promise<void> {
    this.renderers.set((await this.gql.query(DlnaRenderersQuery)).dlnaRenderers);
  }

  protected async addRenderer(): Promise<void> {
    await this.run(async () => {
      const added = (
        await this.gql.mutate(AddDlnaRendererMutation, { url: this.rendererUrl.trim() })
      ).addDlnaRenderer;
      this.rendererUrl = '';
      await this.loadRenderers();
      await this.select(added);
    });
  }

  protected async select(renderer: Renderer): Promise<void> {
    clearInterval(this.poll);
    await this.run(async () => {
      this.selected.set(
        (await this.gql.query(DlnaRendererQuery, { id: renderer.id })).dlnaRenderer,
      );
    });
    // Follows the renderer while the page is open.
    this.poll = setInterval(async () => {
      try {
        this.selected.set(
          (await this.gql.query(DlnaRendererQuery, { id: renderer.id })).dlnaRenderer,
        );
      } catch {
        clearInterval(this.poll);
      }
    }, 2000);
  }

  protected async control(action: DlnaRendererAction): Promise<void> {
    const renderer = this.selected()?.renderer;
    if (!renderer) return;
    await this.run(async () => {
      this.selected.set(
        (
          await this.gql.mutate(ControlDlnaRendererMutation, {
            input: { rendererId: renderer.id, action },
          })
        ).controlDlnaRenderer,
      );
    });
  }

  protected async setVolume(volume: number): Promise<void> {
    const renderer = this.selected()?.renderer;
    if (!renderer) return;
    await this.run(async () => {
      this.selected.set(
        (await this.gql.mutate(SetDlnaRendererVolumeMutation, { rendererId: renderer.id, volume }))
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
    const renderer = this.selected()?.renderer;
    if (!renderer) return;
    await this.run(async () => {
      const tracks = (await this.gql.query(DlnaAlbumTracksQuery, { albumId: album.id })).tracks
        .items;
      this.selected.set(
        (
          await this.gql.mutate(PlayOnDlnaRendererMutation, {
            input: { rendererId: renderer.id, trackIds: tracks.map((t) => t.id) },
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
