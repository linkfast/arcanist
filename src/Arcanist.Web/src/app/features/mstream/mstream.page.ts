import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MstreamAnalysisFieldsFragment as Analysis,
  MstreamImportJobFieldsFragment as ImportJob,
} from '../../graphql/generated/graphql';
import {
  AnalyzeMstreamFolderMutation,
  AnalyzeMstreamServerMutation,
  CreateServerDirectoryMutation,
  ImportMstreamMutation,
  MstreamImportsQuery,
  MstreamLibrarySourcesQuery,
  ServerDirectoriesQuery,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';

/** What becomes of an mStream library: nothing, an existing library folder, or a new library. */
interface LibraryChoice {
  vpath: string;
  root: string;
  rootExists: boolean;
  target: string;
  name: string;
  path: string;
}

const Skip = 'skip';
const New = 'new';

/**
 * Imports an mStream installation, from its folder on this server or from its server's API: first reads it, then
 * the administrator decides what each library becomes and whether users come along.
 */
@Component({
  selector: 'app-mstream-page',
  imports: [
    FormsModule,
    BrnDialogContent,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    FolderPicker,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'mstream.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'mstream.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'mstream.source.title' | t }}</h2>
        </div>
        <form hlmCardContent class="grid gap-4" (ngSubmit)="analyze()">
          <hlm-radio-group class="grid gap-2 sm:grid-cols-2" name="kind" [(ngModel)]="kind">
            <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio value="folder"><hlm-radio-indicator indicator /></hlm-radio>
              <span>
                {{ 'mstream.source.folder' | t }}
                <span class="block text-xs text-muted-foreground">{{
                  'mstream.source.folderHelp' | t
                }}</span>
              </span>
            </label>
            <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio value="server"><hlm-radio-indicator indicator /></hlm-radio>
              <span>
                {{ 'mstream.source.server' | t }}
                <span class="block text-xs text-muted-foreground">{{
                  'mstream.source.serverHelp' | t
                }}</span>
              </span>
            </label>
          </hlm-radio-group>

          @if (kind === 'folder') {
            <hlm-field>
              <label hlmFieldLabel for="folder">{{ 'mstream.source.folderLabel' | t }}</label>
              <div class="flex gap-2">
                <input hlmInput id="folder" name="folder" class="flex-1" [(ngModel)]="folder" />
                <button hlmBtn variant="outline" type="button" (click)="picking.set('folder')">
                  {{ 'mstream.browse' | t }}
                </button>
              </div>
            </hlm-field>
          } @else {
            <div class="grid gap-3 sm:grid-cols-3">
              <hlm-field class="sm:col-span-3">
                <label hlmFieldLabel for="url">{{ 'mstream.source.url' | t }}</label>
                <input
                  hlmInput
                  id="url"
                  name="url"
                  type="url"
                  placeholder="http://192.168.1.20:3000"
                  [(ngModel)]="url"
                />
              </hlm-field>
              <hlm-field>
                <label hlmFieldLabel for="mstreamUser">{{ 'mstream.source.adminUser' | t }}</label>
                <input
                  hlmInput
                  id="mstreamUser"
                  name="mstreamUser"
                  autocomplete="off"
                  [(ngModel)]="adminUser"
                />
              </hlm-field>
              <hlm-field class="sm:col-span-2">
                <label hlmFieldLabel for="mstreamPassword">{{ 'common.password' | t }}</label>
                <input
                  hlmInput
                  id="mstreamPassword"
                  name="mstreamPassword"
                  type="password"
                  autocomplete="off"
                  [(ngModel)]="adminPassword"
                />
              </hlm-field>
              <p class="text-xs text-muted-foreground sm:col-span-3">
                {{ 'mstream.source.passwordHelp' | t }}
              </p>
            </div>
          }

          @if (error()) {
            <div hlmAlert variant="destructive">
              <p hlmAlertDescription>{{ error() }}</p>
            </div>
          }
          <button
            hlmBtn
            type="submit"
            class="justify-self-start"
            [disabled]="busy() || !canAnalyze()"
          >
            @if (busy()) {
              <hlm-spinner class="size-4" />
            }
            {{ 'mstream.analyze' | t }}
          </button>
        </form>
      </section>

      @if (analysis(); as found) {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstream.libraries.title' | t }}</h2>
            <p hlmCardDescription>
              @if (found.version) {
                {{ 'mstream.version' | t: { version: found.version } }}
              }
              {{ 'mstream.libraries.help' | t }}
            </p>
          </div>
          <div hlmCardContent class="grid gap-3">
            @for (choice of choices(); track choice.vpath; let index = $index) {
              <div class="grid gap-3 rounded-md border p-3">
                <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
                  <span>
                    <span class="font-medium">{{ choice.vpath }}</span>
                    <span class="ml-2 text-xs break-all text-muted-foreground">{{
                      choice.root
                    }}</span>
                    @if (!choice.rootExists) {
                      <span hlmBadge variant="outline" class="ml-2">{{
                        'mstream.libraries.notHere' | t
                      }}</span>
                    }
                  </span>
                  <hlm-native-select
                    [value]="choice.target"
                    (change)="update(index, { target: $any($event.target).value })"
                  >
                    <option hlmNativeSelectOption value="skip">
                      {{ 'mstream.libraries.skip' | t }}
                    </option>
                    <option hlmNativeSelectOption value="new">
                      {{ 'mstream.libraries.new' | t }}
                    </option>
                    @for (source of sources(); track source.id) {
                      <option hlmNativeSelectOption [value]="source.id">{{ source.label }}</option>
                    }
                  </hlm-native-select>
                </div>
                @if (choice.target === 'new') {
                  <div class="grid gap-2 sm:grid-cols-[12rem_minmax(0,1fr)]">
                    <input
                      hlmInput
                      [attr.aria-label]="'mstream.libraries.name' | t"
                      [placeholder]="'mstream.libraries.name' | t"
                      [ngModel]="choice.name"
                      (ngModelChange)="update(index, { name: $event })"
                    />
                    <div class="flex gap-2">
                      <input
                        hlmInput
                        class="flex-1"
                        [attr.aria-label]="'mstream.libraries.path' | t"
                        [placeholder]="'mstream.libraries.path' | t"
                        [ngModel]="choice.path"
                        (ngModelChange)="update(index, { path: $event })"
                      />
                      <button hlmBtn variant="outline" type="button" (click)="picking.set(index)">
                        {{ 'mstream.browse' | t }}
                      </button>
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstream.users.title' | t }}</h2>
            <p hlmCardDescription>
              {{ (found.version ? 'mstream.users.helpFolder' : 'mstream.users.helpServer') | t }}
            </p>
          </div>
          <div hlmCardContent class="grid gap-3">
            <ul class="flex flex-wrap gap-2">
              @for (user of found.users; track user.userName) {
                <li class="flex items-center gap-1 rounded-md border px-2 py-1 text-sm">
                  {{ user.userName }}
                  @if (user.isAdmin) {
                    <span hlmBadge variant="secondary">{{ 'users.roles.ADMIN' | t }}</span>
                  }
                  @if (user.exists) {
                    <span hlmBadge variant="outline">{{ 'mstream.users.exists' | t }}</span>
                  } @else if (!user.canSignIn) {
                    <span hlmBadge variant="destructive">{{ 'mstream.users.noPassword' | t }}</span>
                  }
                </li>
              } @empty {
                <li class="text-sm text-muted-foreground">{{ 'mstream.users.none' | t }}</li>
              }
            </ul>
            @if (found.version) {
              <p class="text-sm text-muted-foreground">
                {{
                  'mstream.users.data'
                    | t
                      : {
                          playlists: found.playlists,
                          rated: found.ratedTracks,
                          played: found.playedTracks,
                        }
                }}
              </p>
            }
            <label class="flex items-center gap-2 text-sm">
              <hlm-switch [checked]="importUsers" (checkedChange)="importUsers = $event" />
              {{ 'mstream.users.import' | t }}
            </label>
            <button
              hlmBtn
              type="button"
              class="justify-self-start"
              [disabled]="busy() || !canImport()"
              (click)="start()"
            >
              {{ 'mstream.import' | t }}
            </button>
          </div>
        </section>
      }

      @if (imports().length > 0) {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstream.history.title' | t }}</h2>
          </div>
          <ul hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-2">
            @for (job of imports(); track job.id) {
              <li class="grid gap-1 rounded-md border p-3 text-sm">
                <p class="flex flex-wrap items-center gap-2">
                  <span
                    hlmBadge
                    [variant]="job.status === 'FAILED' ? 'destructive' : 'secondary'"
                    >{{ 'mstream.status.' + job.status | t }}</span
                  >
                  <span class="min-w-0 break-all font-medium">{{ job.source }}</span>
                  <span class="text-xs text-muted-foreground" [title]="job.createdAt | dateTime">{{
                    job.createdAt | relativeTime
                  }}</span>
                </p>
                <p class="text-xs text-muted-foreground">
                  {{
                    'mstream.history.report'
                      | t
                        : {
                            libraries: job.librariesCreated,
                            users: job.usersCreated,
                            playlists: job.playlists,
                            ratings: job.ratings,
                            plays: job.plays,
                            unmatched: job.unmatched,
                          }
                  }}
                </p>
                @if (job.status === 'WAITING') {
                  <p class="text-xs text-muted-foreground">{{ 'mstream.history.waiting' | t }}</p>
                }
                @if (job.kind === 'SERVER') {
                  <p class="text-xs text-muted-foreground">{{ 'mstream.history.server' | t }}</p>
                }
                @if (job.errorCode) {
                  <p class="text-xs text-destructive">{{ i18n.error(job.errorCode) }}</p>
                }
              </li>
            }
          </ul>
        </section>
      }
    </div>

    <hlm-dialog [state]="picking() !== null ? 'open' : 'closed'" (closed)="picking.set(null)">
      <hlm-dialog-content
        *brnDialogContent="let ctx"
        class="w-[min(36rem,calc(100vw-2rem))] sm:max-w-xl"
      >
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'folderPicker.title' | t }}</h3>
        </hlm-dialog-header>
        <app-folder-picker
          [load]="loadDirectories"
          [create]="createDirectory"
          (picked)="picked($event)"
          (cancelled)="picking.set(null)"
        />
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MstreamPage implements OnInit, OnDestroy {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected kind: 'folder' | 'server' = 'folder';
  protected folder = '';
  protected url = '';
  protected adminUser = '';
  protected adminPassword = '';
  protected importUsers = true;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly analysis = signal<Analysis | null>(null);
  protected readonly choices = signal<LibraryChoice[]>([]);
  protected readonly sources = signal<{ id: string; label: string }[]>([]);
  protected readonly imports = signal<ImportJob[]>([]);
  /** What the folder picker fills: the mStream folder, or the path of a new library. */
  protected readonly picking = signal<'folder' | number | null>(null);

  protected readonly canImport = computed(() =>
    this.choices().every((c) => c.target !== New || (c.name.trim() && c.path.trim())),
  );

  private poll: ReturnType<typeof setInterval> | undefined;
  /** The source analyzed, so the import uses what was read even if the form changes. */
  private analyzed: {
    folder: string | null;
    server: { url: string; userName: string; password: string } | null;
  } | null = null;

  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  async ngOnInit(): Promise<void> {
    await this.loadImports();
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
  }

  protected canAnalyze(): boolean {
    return this.kind === 'folder'
      ? !!this.folder.trim()
      : !!(this.url.trim() && this.adminUser.trim() && this.adminPassword);
  }

  protected async analyze(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.analysis.set(null);
    try {
      const server =
        this.kind === 'server'
          ? { url: this.url.trim(), userName: this.adminUser.trim(), password: this.adminPassword }
          : null;
      const analysis = server
        ? (await this.gql.mutate(AnalyzeMstreamServerMutation, { input: server }))
            .analyzeMstreamServer
        : (await this.gql.mutate(AnalyzeMstreamFolderMutation, { folder: this.folder.trim() }))
            .analyzeMstreamFolder;
      this.analyzed = { folder: server ? null : this.folder.trim(), server };

      const libraries = (await this.gql.query(MstreamLibrarySourcesQuery)).libraries;
      this.sources.set(
        libraries.flatMap((library) =>
          (library.sources ?? []).map((source) => ({
            id: source.id,
            label: `${library.name} — ${source.location}`,
          })),
        ),
      );
      this.choices.set(
        analysis.libraries.map((library) => ({
          vpath: library.vpath,
          root: library.root,
          rootExists: library.rootExists,
          target: library.sourceId ?? (library.rootExists ? New : Skip),
          name: library.vpath,
          path: library.rootExists ? library.root : '',
        })),
      );
      this.analysis.set(analysis);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  protected update(index: number, change: Partial<LibraryChoice>): void {
    this.choices.update((choices) =>
      choices.map((c, i) => (i === index ? { ...c, ...change } : c)),
    );
  }

  protected picked(path: string): void {
    const target = this.picking();
    this.picking.set(null);
    if (target === 'folder') this.folder = path;
    else if (target !== null) this.update(target, { path });
  }

  protected async start(): Promise<void> {
    if (!this.analyzed) return;
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(ImportMstreamMutation, {
        input: {
          folder: this.analyzed.folder,
          server: this.analyzed.server,
          importUsers: this.importUsers,
          libraries: this.choices()
            .filter((c) => c.target !== Skip)
            .map((c) =>
              c.target === New
                ? { vpath: c.vpath, newLibraryName: c.name.trim(), newLibraryPath: c.path.trim() }
                : { vpath: c.vpath, sourceId: c.target },
            ),
        },
      });
      this.imports.update((imports) => [result.importMstream, ...imports]);
      this.analysis.set(null);
      this.adminPassword = '';
      toast.success(this.i18n.t('mstream.started'));
      this.watch();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadImports(): Promise<void> {
    this.imports.set((await this.gql.query(MstreamImportsQuery)).mstreamImports);
    this.watch();
  }

  /** Follows imports that are still running. */
  private watch(): void {
    clearInterval(this.poll);
    if (!this.imports().some((job) => job.status === 'WAITING' || job.status === 'RUNNING')) return;
    this.poll = setInterval(async () => {
      this.imports.set((await this.gql.query(MstreamImportsQuery)).mstreamImports);
      if (!this.imports().some((job) => job.status === 'WAITING' || job.status === 'RUNNING'))
        clearInterval(this.poll);
    }, 2000);
  }
}
