import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../core/api/rest';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type { MstreamAnalysisFieldsFragment as Analysis } from '../graphql/generated/graphql';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from './folder-picker';

/** Where an mStream installation is read from: its folder on this server, or its server. */
export interface MstreamSourceInput {
  folder: string | null;
  server: { url: string; userName: string; password: string } | null;
}

/** What becomes of an mStream library: nothing, an existing library folder (its id), or a new library. */
export interface MstreamLibraryChoice {
  vpath: string;
  root: string;
  rootExists: boolean;
  /** Its audio files on the mStream server (read through its API), or null. */
  files: number | null;
  target: string;
  name: string;
  path: string;
  /** Copy the server's files into the folder (an mStream server on another machine). */
  copy: boolean;
}

export const SkipLibrary = 'skip';
export const NewLibrary = 'new';

/**
 * The answers of an mStream import, kept by the page that shows the form: the wizard keeps them while moving
 * between steps, and the panel throws them away when its dialog closes.
 */
export class MstreamImportDraft {
  kind: 'folder' | 'server' = 'folder';
  folder = '';
  url = '';
  adminUser = '';
  adminPassword = '';
  importUsers = true;

  readonly analysis = signal<Analysis | null>(null);
  readonly choices = signal<MstreamLibraryChoice[]>([]);
  /** The source analyzed, so the import uses what was read even if the form changes afterwards. */
  analyzed: MstreamSourceInput | null = null;

  /** Every new library has a name and a folder. */
  readonly complete = computed(() =>
    this.choices().every((c) => c.target !== NewLibrary || (c.name.trim() && c.path.trim())),
  );

  update(index: number, change: Partial<MstreamLibraryChoice>): void {
    this.choices.update((choices) =>
      choices.map((c, i) => (i === index ? { ...c, ...change } : c)),
    );
  }

  /** Whether the files of a library are copied from the server. */
  copies(choice: MstreamLibraryChoice): boolean {
    return this.analyzed?.server != null && choice.copy;
  }

  /** The chosen libraries, as the import mutation takes them. */
  libraryInputs(): {
    vpath: string;
    sourceId?: string;
    newLibraryName?: string;
    newLibraryPath?: string;
    copyFiles: boolean;
  }[] {
    return this.choices()
      .filter((c) => c.target !== SkipLibrary)
      .map((c) =>
        c.target === NewLibrary
          ? {
              vpath: c.vpath,
              newLibraryName: c.name.trim(),
              newLibraryPath: c.path.trim(),
              copyFiles: this.copies(c),
            }
          : { vpath: c.vpath, sourceId: c.target, copyFiles: this.copies(c) },
      );
  }
}

/**
 * Reads an mStream installation and lets the administrator decide what each library becomes and whether users
 * come along. Used by the setup wizard (which has no libraries yet) and by the import dialog of the panel.
 */
@Component({
  selector: 'app-mstream-import',
  imports: [
    FormsModule,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    FolderPicker,
  ],
  template: `
    @if (picking() !== null) {
      <div class="grid gap-2 rounded-lg border p-3">
        <p class="text-sm font-medium">{{ 'folderPicker.title' | t }}</p>
        <app-folder-picker
          [load]="loadDirectories()"
          [create]="createDirectory()"
          (picked)="picked($event)"
          (cancelled)="picking.set(null)"
        />
      </div>
    } @else {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-5">
        <form class="grid gap-4" (ngSubmit)="analyze()">
          <hlm-radio-group class="grid gap-2 sm:grid-cols-2" name="kind" [(ngModel)]="draft().kind">
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

          @if (draft().kind === 'folder') {
            <hlm-field>
              <label hlmFieldLabel for="mstream-folder">{{
                'mstream.source.folderLabel' | t
              }}</label>
              <div class="flex gap-2">
                <input
                  hlmInput
                  id="mstream-folder"
                  name="folder"
                  class="min-w-0 flex-1"
                  [(ngModel)]="draft().folder"
                />
                <button hlmBtn variant="outline" type="button" (click)="picking.set('folder')">
                  {{ 'mstream.browse' | t }}
                </button>
              </div>
            </hlm-field>
          } @else {
            <div class="grid gap-3 sm:grid-cols-3">
              <hlm-field class="sm:col-span-3">
                <label hlmFieldLabel for="mstream-url">{{ 'mstream.source.url' | t }}</label>
                <input
                  hlmInput
                  id="mstream-url"
                  name="url"
                  type="url"
                  placeholder="http://192.168.1.20:3000"
                  [(ngModel)]="draft().url"
                />
              </hlm-field>
              <hlm-field>
                <label hlmFieldLabel for="mstream-user">{{ 'mstream.source.adminUser' | t }}</label>
                <input
                  hlmInput
                  id="mstream-user"
                  name="mstreamUser"
                  autocomplete="off"
                  [(ngModel)]="draft().adminUser"
                />
              </hlm-field>
              <hlm-field class="sm:col-span-2">
                <label hlmFieldLabel for="mstream-password">{{ 'common.password' | t }}</label>
                <input
                  hlmInput
                  id="mstream-password"
                  name="mstreamPassword"
                  type="password"
                  autocomplete="off"
                  [(ngModel)]="draft().adminPassword"
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
            [variant]="draft().analysis() ? 'outline' : 'default'"
            type="submit"
            class="justify-self-start"
            [disabled]="busy() || !canAnalyze()"
          >
            @if (busy()) {
              <hlm-spinner class="size-4" />
            }
            {{ (draft().analysis() ? 'mstream.analyzeAgain' : 'mstream.analyze') | t }}
          </button>
        </form>

        @if (draft().analysis(); as found) {
          <section class="grid gap-3">
            <div>
              <h3 class="text-sm font-medium">{{ 'mstream.libraries.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">
                @if (found.version) {
                  {{ 'mstream.version' | t: { version: found.version } }}
                }
                {{ (setup() ? 'mstream.libraries.helpSetup' : 'mstream.libraries.help') | t }}
              </p>
            </div>
            @for (choice of draft().choices(); track choice.vpath; let index = $index) {
              <div class="grid gap-3 rounded-md border p-3">
                <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
                  <span class="min-w-0">
                    <span class="font-medium">{{ choice.vpath }}</span>
                    <span class="ml-2 text-xs break-all text-muted-foreground">{{
                      choice.root
                    }}</span>
                    @if (!choice.rootExists) {
                      <span hlmBadge variant="outline" class="ml-2">{{
                        'mstream.libraries.notHere' | t
                      }}</span>
                    }
                    @if (choice.files !== null) {
                      <span class="ml-2 text-xs text-muted-foreground">{{
                        'mstream.libraries.files' | t: { count: choice.files }
                      }}</span>
                    }
                  </span>
                  <hlm-native-select
                    [value]="choice.target"
                    (change)="draft().update(index, { target: $any($event.target).value })"
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
                @if (draft().analyzed?.server && choice.target !== 'skip') {
                  <label class="flex items-start gap-2 text-sm">
                    <hlm-switch
                      class="mt-0.5"
                      [checked]="choice.copy"
                      (checkedChange)="draft().update(index, { copy: $event })"
                    />
                    <span>
                      {{ 'mstream.libraries.copy' | t }}
                      <span class="block text-xs text-muted-foreground">{{
                        (choice.copy
                          ? 'mstream.libraries.copyHelp'
                          : choice.rootExists
                            ? 'mstream.libraries.noCopyHere'
                            : 'mstream.libraries.noCopyEmpty'
                        ) | t
                      }}</span>
                    </span>
                  </label>
                }
                @if (choice.target === 'new') {
                  <div class="grid gap-2 sm:grid-cols-[12rem_minmax(0,1fr)]">
                    <input
                      hlmInput
                      [attr.aria-label]="'mstream.libraries.name' | t"
                      [placeholder]="'mstream.libraries.name' | t"
                      [ngModel]="choice.name"
                      (ngModelChange)="draft().update(index, { name: $event })"
                    />
                    <div class="flex gap-2">
                      <input
                        hlmInput
                        class="min-w-0 flex-1"
                        [attr.aria-label]="'mstream.libraries.path' | t"
                        [placeholder]="'mstream.libraries.path' | t"
                        [ngModel]="choice.path"
                        (ngModelChange)="draft().update(index, { path: $event })"
                      />
                      <button hlmBtn variant="outline" type="button" (click)="picking.set(index)">
                        {{ 'mstream.browse' | t }}
                      </button>
                    </div>
                  </div>
                }
              </div>
            } @empty {
              <p class="text-sm text-muted-foreground">{{ 'mstream.libraries.none' | t }}</p>
            }
          </section>

          <section class="grid gap-3">
            <div>
              <h3 class="text-sm font-medium">{{ 'mstream.users.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">
                {{ (found.version ? 'mstream.users.helpFolder' : 'mstream.users.helpServer') | t }}
              </p>
            </div>
            <ul class="flex flex-wrap gap-2">
              @for (user of found.users; track user.userName) {
                <li class="flex items-center gap-1 rounded-md border px-2 py-1 text-sm">
                  {{ user.userName }}
                  @if (user.isAdmin) {
                    <span hlmBadge variant="secondary">{{ 'users.roles.ADMIN' | t }}</span>
                  }
                  @if (isYou(user.userName)) {
                    <span hlmBadge variant="outline">{{ 'mstream.users.you' | t }}</span>
                  } @else if (user.exists) {
                    <span hlmBadge variant="outline">{{ 'mstream.users.exists' | t }}</span>
                  } @else if (!user.canSignIn) {
                    <span hlmBadge variant="destructive">{{ 'mstream.users.noPassword' | t }}</span>
                  }
                </li>
              } @empty {
                <li class="text-sm text-muted-foreground">{{ 'mstream.users.none' | t }}</li>
              }
            </ul>
            <p class="text-sm text-muted-foreground">
              {{
                (found.version ? 'mstream.users.data' : 'mstream.users.dataServer')
                  | t
                    : {
                        playlists: found.playlists,
                        rated: found.ratedTracks,
                        played: found.playedTracks,
                      }
              }}
            </p>
            @if (setup()) {
              <p class="text-xs text-muted-foreground">{{ 'mstream.users.sameName' | t }}</p>
            }
            <label class="flex items-center gap-2 text-sm">
              <hlm-switch
                [checked]="draft().importUsers"
                (checkedChange)="draft().importUsers = $event"
              />
              {{ 'mstream.users.import' | t }}
            </label>
          </section>
        }
      </div>
    }
  `,
})
export class MstreamImportForm {
  private readonly i18n = inject(I18n);

  readonly draft = input.required<MstreamImportDraft>();
  /** Reads the installation: through GraphQL in the panel, through the setup API in the wizard. */
  readonly analyzeWith = input.required<(source: MstreamSourceInput) => Promise<Analysis>>();
  readonly loadDirectories = input.required<DirectoryLoader>();
  readonly createDirectory = input.required<DirectoryCreator>();
  /** Existing library folders an mStream library can map to. */
  readonly sources = input<{ id: string; label: string }[]>([]);
  /** In the wizard the new libraries join the next step, and the administrator is being created. */
  readonly setup = input(false);
  /** The administrator's user name, whose mStream namesake's data goes to them. */
  readonly adminName = input('');

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** What the folder picker fills: the mStream folder, or the folder of a new library. */
  protected readonly picking = signal<'folder' | number | null>(null);

  protected canAnalyze(): boolean {
    const draft = this.draft();
    return draft.kind === 'folder'
      ? !!draft.folder.trim()
      : !!(draft.url.trim() && draft.adminUser.trim() && draft.adminPassword);
  }

  protected isYou(userName: string): boolean {
    const admin = this.adminName().trim().toLowerCase();
    return this.setup() && !!admin && admin === userName.trim().toLowerCase();
  }

  protected async analyze(): Promise<void> {
    const draft = this.draft();
    this.busy.set(true);
    this.error.set(null);
    try {
      const source: MstreamSourceInput =
        draft.kind === 'server'
          ? {
              folder: null,
              server: {
                url: draft.url.trim(),
                userName: draft.adminUser.trim(),
                password: draft.adminPassword,
              },
            }
          : { folder: draft.folder.trim(), server: null };
      const analysis = await this.analyzeWith()(source);
      draft.analyzed = source;
      draft.choices.set(
        analysis.libraries.map((library) => ({
          vpath: library.vpath,
          root: library.root,
          rootExists: library.rootExists,
          files: library.files ?? null,
          // A server's folder that is not here: a new library whose files are copied from the server.
          target:
            library.sourceId ?? (library.rootExists || source.server ? NewLibrary : SkipLibrary),
          name: library.vpath,
          path: library.rootExists ? library.root : '',
          copy: !!source.server && !library.rootExists,
        })),
      );
      draft.analysis.set(analysis);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  protected picked(path: string): void {
    const target = this.picking();
    this.picking.set(null);
    if (target === 'folder') this.draft().folder = path;
    else if (target !== null) this.draft().update(target, { path });
  }
}
