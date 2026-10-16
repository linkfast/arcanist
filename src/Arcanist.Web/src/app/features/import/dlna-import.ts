import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronRight,
  lucideFolder,
  lucideRefreshCw,
  lucideServer,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  BrowseDlnaMediaServerQuery as BrowseResult,
  DlnaImportJobFieldsFragment as DlnaImportJob,
  DlnaMediaServersQuery as ServersResult,
} from '../../graphql/generated/graphql';
import {
  BrowseDlnaMediaServerQuery,
  DlnaMediaServerQuery,
  DlnaMediaServersQuery,
  StartDlnaImportMutation,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';

type DlnaMediaServer = ServersResult['dlnaMediaServers'][number];
type DlnaFolderContents = BrowseResult['browseDlnaMediaServer'];

/** How the form reads media servers: the panel's API by default, the setup wizard's before there is an account. */
export interface DlnaBrowseApi {
  search(): Promise<DlnaMediaServer[]>;
  describe(location: string): Promise<DlnaMediaServer>;
  browse(location: string, folderId: string): Promise<DlnaFolderContents>;
}

/** What to copy, chosen now and started later (the setup wizard and running it again start it at the end). */
export interface DlnaImportChoice {
  location: string;
  serverName: string;
  folders: { id: string; title: string }[];
  sourceId: string | null;
  newLibraryName: string | null;
  newLibraryPath: string | null;
}

/** Starts a chosen copy with the administrator's session. */
export async function startDlnaImport(gql: Gql, choice: DlnaImportChoice): Promise<DlnaImportJob> {
  return (
    await gql.mutate(StartDlnaImportMutation, {
      input: {
        location: choice.location,
        folders: choice.folders,
        sourceId: choice.sourceId,
        newLibraryName: choice.newLibraryName,
        newLibraryPath: choice.newLibraryPath,
      },
    })
  ).startDlnaImport;
}

interface Crumb {
  id: string;
  title: string;
}

/**
 * Copying music from a DLNA/UPnP media server: find the server (or type its address), choose folders of its tree,
 * and the library to copy into. Place it inside a dialog; it has its own footer.
 */
@Component({
  selector: 'app-dlna-import',
  imports: [
    FormsModule,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    TranslatePipe,
    FolderPicker,
  ],
  providers: [
    provideIcons({ lucideChevronRight, lucideFolder, lucideRefreshCw, lucideServer, lucideX }),
  ],
  host: { class: 'block w-full min-w-0' },
  template: `
    @if (picking()) {
      <div class="grid gap-2 rounded-lg border p-3">
        <p class="text-sm font-medium">{{ 'folderPicker.title' | t }}</p>
        <app-folder-picker
          [load]="loadDirectories()"
          [create]="createDirectory()"
          (picked)="newPath = $event; picking.set(false)"
          (cancelled)="picking.set(false)"
        />
      </div>
    } @else if (!server()) {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-4">
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium">{{ 'dlnaImport.servers.title' | t }}</p>
          <button
            hlmBtn
            variant="ghost"
            size="sm"
            type="button"
            class="gap-1"
            [disabled]="searching()"
            (click)="search()"
          >
            <ng-icon name="lucideRefreshCw" />{{ 'dlnaImport.servers.search' | t }}
          </button>
        </div>
        @if (searching()) {
          <p class="flex items-center gap-2 text-sm text-muted-foreground">
            <hlm-spinner class="size-4" />{{ 'dlnaImport.servers.searching' | t }}
          </p>
        } @else if (servers().length === 0) {
          <p class="text-sm text-muted-foreground">{{ 'dlnaImport.servers.none' | t }}</p>
        } @else {
          <ul class="grid gap-2">
            @for (found of servers(); track found.id) {
              <li>
                <button
                  type="button"
                  class="flex w-full items-center gap-3 rounded-lg border p-3 text-left text-sm hover:bg-accent"
                  (click)="choose(found)"
                >
                  <ng-icon name="lucideServer" class="shrink-0 text-muted-foreground" />
                  <span class="grid min-w-0 gap-0.5">
                    <span class="font-medium">{{ found.name }}</span>
                    <span class="truncate text-xs text-muted-foreground">{{
                      describe(found)
                    }}</span>
                  </span>
                </button>
              </li>
            }
          </ul>
        }
        <form class="grid gap-2" (ngSubmit)="openLocation()">
          <label hlmFieldLabel for="dlna-location">{{ 'dlnaImport.servers.byAddress' | t }}</label>
          <div class="flex gap-2">
            <input
              hlmInput
              id="dlna-location"
              name="location"
              class="min-w-0 flex-1 font-mono text-xs"
              placeholder="http://192.168.1.20:9790/description.xml"
              [(ngModel)]="location"
            />
            <button hlmBtn variant="outline" type="submit" [disabled]="busy() || !location.trim()">
              {{ 'dlnaImport.servers.open' | t }}
            </button>
          </div>
          <p class="text-xs text-muted-foreground">{{ 'dlnaImport.servers.byAddressHelp' | t }}</p>
        </form>
      </div>
    } @else {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-5">
        <div class="flex items-center justify-between gap-2">
          <p class="flex min-w-0 items-center gap-2 text-sm font-medium">
            <ng-icon name="lucideServer" class="shrink-0 text-muted-foreground" />
            <span class="truncate">{{ server()!.name }}</span>
          </p>
          <button hlmBtn variant="ghost" size="sm" type="button" (click)="reset()">
            {{ 'dlnaImport.browse.change' | t }}
          </button>
        </div>

        <div class="grid gap-2">
          <nav class="flex flex-wrap items-center gap-1 text-sm" aria-label="breadcrumb">
            @for (crumb of path(); track crumb.id; let last = $last) {
              @if (last) {
                <span class="font-medium">{{ crumb.title }}</span>
              } @else {
                <button
                  type="button"
                  class="text-muted-foreground hover:underline"
                  (click)="goTo(crumb)"
                >
                  {{ crumb.title }}
                </button>
                <ng-icon name="lucideChevronRight" class="text-muted-foreground" />
              }
            }
          </nav>
          <div class="max-h-72 overflow-y-auto rounded-lg border">
            @if (busy() && !contents()) {
              <p class="flex items-center gap-2 p-3 text-sm text-muted-foreground">
                <hlm-spinner class="size-4" />{{ 'dlnaImport.browse.loading' | t }}
              </p>
            } @else if (contents(); as folder) {
              <ul class="divide-y">
                @for (child of folder.folders; track child.id) {
                  <li class="flex items-center gap-3 px-3 py-2 text-sm">
                    <hlm-checkbox
                      [checked]="isSelected(child.id)"
                      [attr.aria-label]="child.title"
                      (checkedChange)="toggle(child, $event)"
                    />
                    <button
                      type="button"
                      class="flex min-w-0 flex-1 items-center gap-2 text-left hover:underline"
                      [disabled]="busy()"
                      (click)="open(child)"
                    >
                      <ng-icon name="lucideFolder" class="shrink-0 text-muted-foreground" />
                      <span class="truncate">{{ child.title }}</span>
                    </button>
                    @if (child.childCount !== null && child.childCount !== undefined) {
                      <span class="text-xs text-muted-foreground">{{ child.childCount }}</span>
                    }
                  </li>
                } @empty {
                  <li class="p-3 text-sm text-muted-foreground">
                    {{ 'dlnaImport.browse.noFolders' | t }}
                  </li>
                }
              </ul>
            }
          </div>
          @if (contents(); as folder) {
            <div
              class="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground"
            >
              <span>
                @if (folder.tracks > 0 || folder.folders.length === 0) {
                  {{ 'dlnaImport.browse.tracksHere' | t: { count: folder.tracks } }}
                }
                @if (folder.truncated) {
                  · {{ 'dlnaImport.browse.truncated' | t }}
                }
              </span>
              @if (folder.tracks > 0 && path().length > 1 && !isSelected(folder.id)) {
                <button hlmBtn variant="outline" size="sm" type="button" (click)="selectCurrent()">
                  {{ 'dlnaImport.browse.selectThis' | t }}
                </button>
              }
            </div>
          }
          <p class="text-xs text-muted-foreground">{{ 'dlnaImport.browse.views' | t }}</p>
        </div>

        @if (selected().length > 0) {
          <div class="grid gap-2">
            <p class="text-sm font-medium">{{ 'dlnaImport.selected' | t }}</p>
            <div class="flex flex-wrap gap-2">
              @for (folder of selected(); track folder.id) {
                <span hlmBadge variant="secondary" class="gap-1">
                  {{ folder.title }}
                  <button
                    type="button"
                    [attr.aria-label]="'common.remove' | t"
                    (click)="toggle(folder, false)"
                  >
                    <ng-icon name="lucideX" size="12" />
                  </button>
                </span>
              }
            </div>
          </div>

          <div class="grid gap-3">
            <p class="text-sm font-medium">{{ 'dlnaImport.destination.title' | t }}</p>
            <hlm-radio-group class="grid gap-2 sm:grid-cols-2" name="target" [(ngModel)]="target">
              <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
                <hlm-radio value="existing" [disabled]="targets().length === 0"
                  ><hlm-radio-indicator indicator
                /></hlm-radio>
                <span>{{ 'dlnaImport.destination.existing' | t }}</span>
              </label>
              <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
                <hlm-radio value="new"><hlm-radio-indicator indicator /></hlm-radio>
                <span>{{ 'dlnaImport.destination.new' | t }}</span>
              </label>
            </hlm-radio-group>
            @if (target === 'existing') {
              <hlm-native-select [value]="sourceId" (change)="sourceId = $any($event.target).value">
                @for (option of targets(); track option.id) {
                  <option hlmNativeSelectOption [value]="option.id">{{ option.label }}</option>
                }
              </hlm-native-select>
            } @else {
              <div class="grid gap-2 sm:grid-cols-[12rem_minmax(0,1fr)]">
                <input
                  hlmInput
                  name="newName"
                  [attr.aria-label]="'dlnaImport.destination.name' | t"
                  [placeholder]="server()!.name"
                  [(ngModel)]="newName"
                />
                <div class="flex gap-2">
                  <input
                    hlmInput
                    name="newPath"
                    class="min-w-0 flex-1 font-mono text-xs"
                    [attr.aria-label]="'dlnaImport.destination.folder' | t"
                    [placeholder]="'dlnaImport.destination.folder' | t"
                    [(ngModel)]="newPath"
                  />
                  <button hlmBtn variant="outline" type="button" (click)="picking.set(true)">
                    {{ 'mstream.browse' | t }}
                  </button>
                </div>
              </div>
            }
            <ul class="list-disc space-y-1 pl-5 text-xs text-muted-foreground">
              <li>{{ 'dlnaImport.notes.originals' | t }}</li>
              <li>{{ 'dlnaImport.notes.template' | t }}</li>
              <li>{{ 'dlnaImport.notes.duplicates' | t }}</li>
            </ul>
          </div>
        }
      </div>
    }

    <hlm-dialog-footer class="mt-4">
      <button hlmBtn variant="ghost" type="button" (click)="cancelled.emit()">
        {{ 'common.cancel' | t }}
      </button>
      <button hlmBtn type="button" [disabled]="!ready() || starting()" (click)="start()">
        @if (starting()) {
          <hlm-spinner class="size-4" />
        }
        {{ (chooseOnly() ? 'dlnaImport.choose' : 'dlnaImport.start') | t }}
      </button>
    </hlm-dialog-footer>
  `,
})
export class DlnaImportForm implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  /** Library folders that can be copied into. */
  readonly targets = input.required<{ id: string; label: string }[]>();
  readonly loadDirectories = input.required<DirectoryLoader>();
  readonly createDirectory = input.required<DirectoryCreator>();
  readonly started = output<DlnaImportJob>();
  readonly cancelled = output<void>();
  /** Reads servers some other way (the setup wizard, before there is an account). */
  readonly api = input<DlnaBrowseApi | null>(null);
  /** Only choose: the copy starts later, and `chosen` says what. */
  readonly chooseOnly = input(false);
  readonly chosen = output<DlnaImportChoice>();

  protected readonly servers = signal<DlnaMediaServer[]>([]);
  protected readonly searching = signal(false);
  protected readonly server = signal<DlnaMediaServer | null>(null);
  protected readonly path = signal<Crumb[]>([]);
  protected readonly contents = signal<DlnaFolderContents | null>(null);
  protected readonly selected = signal<Crumb[]>([]);
  protected readonly busy = signal(false);
  protected readonly starting = signal(false);
  protected readonly picking = signal(false);
  protected location = '';
  protected target: 'existing' | 'new' = 'existing';
  protected sourceId = '';
  protected newName = '';
  protected newPath = '';

  protected readonly ready = computed(() => this.server() !== null && this.selected().length > 0);

  async ngOnInit(): Promise<void> {
    if (this.targets().length === 0) this.target = 'new';
    this.sourceId = this.targets()[0]?.id ?? '';
    await this.search();
  }

  protected describe(server: DlnaMediaServer): string {
    let host = server.location;
    try {
      host = new URL(server.location).host;
    } catch {
      // Shown as it is.
    }
    return [server.manufacturer, server.model, host].filter(Boolean).join(' · ');
  }

  protected async search(): Promise<void> {
    this.searching.set(true);
    try {
      this.servers.set(
        (await this.api()?.search()) ??
          (await this.gql.query(DlnaMediaServersQuery)).dlnaMediaServers,
      );
    } catch (error) {
      this.fail(error);
    } finally {
      this.searching.set(false);
    }
  }

  protected async openLocation(): Promise<void> {
    this.busy.set(true);
    try {
      const location = this.location.trim();
      const found =
        (await this.api()?.describe(location)) ??
        (await this.gql.query(DlnaMediaServerQuery, { location })).dlnaMediaServer;
      await this.choose(found);
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async choose(found: DlnaMediaServer): Promise<void> {
    this.server.set(found);
    this.selected.set([]);
    await this.goTo({ id: '0', title: found.name }, true);
  }

  protected reset(): void {
    this.server.set(null);
    this.contents.set(null);
    this.path.set([]);
    this.selected.set([]);
  }

  protected async open(folder: Crumb): Promise<void> {
    this.path.update((path) => [...path, { id: folder.id, title: folder.title }]);
    await this.load(folder.id);
  }

  protected async goTo(crumb: Crumb, root = false): Promise<void> {
    if (root) this.path.set([crumb]);
    else this.path.update((path) => path.slice(0, path.findIndex((c) => c.id === crumb.id) + 1));
    await this.load(crumb.id);
  }

  private async load(folderId: string): Promise<void> {
    const server = this.server();
    if (!server) return;
    this.busy.set(true);
    this.contents.set(null);
    try {
      this.contents.set(
        (await this.api()?.browse(server.location, folderId)) ??
          (
            await this.gql.query(BrowseDlnaMediaServerQuery, {
              location: server.location,
              folderId,
            })
          ).browseDlnaMediaServer,
      );
    } catch (error) {
      this.fail(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected isSelected(id: string): boolean {
    return this.selected().some((f) => f.id === id);
  }

  protected toggle(folder: Crumb, checked: boolean): void {
    this.selected.update((list) =>
      checked
        ? list.some((f) => f.id === folder.id)
          ? list
          : [...list, { id: folder.id, title: folder.title }]
        : list.filter((f) => f.id !== folder.id),
    );
  }

  protected selectCurrent(): void {
    const current = this.path().at(-1);
    if (current) this.toggle(current, true);
  }

  protected async start(): Promise<void> {
    const server = this.server();
    if (!server) return;
    const existing = this.target === 'existing';
    const choice: DlnaImportChoice = {
      location: server.location,
      serverName: server.name,
      folders: this.selected(),
      sourceId: existing ? this.sourceId : null,
      newLibraryName: existing ? null : this.newName.trim() || null,
      newLibraryPath: existing ? null : this.newPath.trim(),
    };
    if (this.chooseOnly()) {
      if (!existing && !choice.newLibraryPath) {
        toast.error(this.i18n.t('dlnaImport.destination.folderRequired'));
        return;
      }
      this.chosen.emit(choice);
      return;
    }
    this.starting.set(true);
    try {
      this.started.emit(await startDlnaImport(this.gql, choice));
    } catch (error) {
      this.fail(error);
    } finally {
      this.starting.set(false);
    }
  }

  private fail(error: unknown): void {
    const apiError = ApiError.from(error);
    toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
  }
}
