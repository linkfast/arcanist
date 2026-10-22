import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLink, lucideRefreshCw, lucideServer, lucideWaypoints } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../core/api/rest';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type {
  ArcanistServersQuery,
  ArcanistSourceAnalysisFieldsFragment as Analysis,
} from '../graphql/generated/graphql';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from './folder-picker';
import { BytesPipe, CountPipe } from './format';

export type ArcanistServer = ArcanistServersQuery['arcanistServers'][number];

/** Where the other Arcanist is, as the API takes it. */
export interface ArcanistSourceInput {
  url: string | null;
  nodeId: string | null;
  link: string | null;
  code: string | null;
}

/** How the form finds and reads the other server: the panel's API, or the setup wizard's before there is an account. */
export interface ArcanistImportApi {
  servers(): Promise<ArcanistServer[]>;
  analyze(source: ArcanistSourceInput): Promise<Analysis>;
}

export const NewLibraryTarget = 'new';

/** What becomes of a library of the other server. */
export interface ArcanistLibraryDraft {
  sourceLibraryId: string;
  sourceName: string;
  files: number;
  bytes: number;
  visibleToAll: boolean;
  include: boolean;
  /** A library of this server (its id), or a new one. */
  target: string;
  newName: string;
  newPath: string;
}

/** The answers, kept by the page that shows the form (the wizard keeps them across steps and reloads). */
export class ArcanistImportDraft {
  how: 'found' | 'address' | 'link' = 'found';
  /** The server chosen in the list: its address, or its P2P node. */
  foundUrl: string | null = null;
  foundNode: string | null = null;
  url = '';
  code = '';
  link = '';

  readonly analysis = signal<Analysis | null>(null);
  /** The source read, so the migration uses it even if the form changes afterwards. */
  analyzed: ArcanistSourceInput | null = null;
  readonly libraries = signal<ArcanistLibraryDraft[]>([]);
  /** Every library goes to the same place. */
  sameTarget = false;
  readonly users = signal<string[]>([]);
  copyPasswords = true;
  permissions = true;
  userData = true;
  settings = true;
  extensions = true;

  /** Something is brought, and every library has where to go. */
  readonly complete = computed(() => {
    const included = this.libraries().filter((l) => l.include);
    return (
      (included.length > 0 || this.users().length > 0 || this.settings || this.extensions) &&
      included.every((l) => l.target !== NewLibraryTarget || l.newPath.trim())
    );
  });

  source(): ArcanistSourceInput {
    if (this.how === 'link') return { url: null, nodeId: null, link: this.link.trim(), code: null };
    if (this.how === 'found')
      return { url: this.foundUrl, nodeId: this.foundNode, link: null, code: this.code.trim() };
    return { url: this.url.trim(), nodeId: null, link: null, code: this.code.trim() };
  }

  /** Takes what was read: every library and user, each library to a library of the same name when there is one. */
  read(analysis: Analysis, source: ArcanistSourceInput, setup: boolean): void {
    this.analysis.set(analysis);
    this.analyzed = source;
    this.libraries.set(
      analysis.libraries.map((l) => ({
        sourceLibraryId: l.id,
        sourceName: l.name,
        files: l.files,
        bytes: l.bytes,
        visibleToAll: l.visibleToAll,
        include: true,
        target: !setup && l.sameName ? l.sameName : NewLibraryTarget,
        newName: l.name,
        newPath: '',
      })),
    );
    this.users.set(analysis.users.map((u) => u.id));
  }

  forget(): void {
    this.analysis.set(null);
    this.analyzed = null;
    this.libraries.set([]);
    this.users.set([]);
  }

  update(index: number, change: Partial<ArcanistLibraryDraft>): void {
    this.libraries.update((list) =>
      list.map((l, i) =>
        i === index || (this.sameTarget && !('include' in change)) ? { ...l, ...change } : l,
      ),
    );
  }

  /** Every library to the same place as the first one. */
  setSameTarget(same: boolean): void {
    this.sameTarget = same;
    const first = this.libraries()[0];
    if (same && first)
      this.libraries.update((list) =>
        list.map((l) => ({
          ...l,
          target: first.target,
          newName: first.newName,
          newPath: first.newPath,
        })),
      );
  }

  toggleUser(id: string, on: boolean): void {
    this.users.update((list) => (on ? [...new Set([...list, id])] : list.filter((u) => u !== id)));
  }

  /** The chosen libraries, as `startArcanistImport` takes them. */
  libraryInputs(): {
    sourceLibraryId: string;
    libraryId: string | null;
    newLibraryName: string | null;
    newLibraryPath: string | null;
  }[] {
    return this.libraries()
      .filter((l) => l.include)
      .map((l) =>
        l.target === NewLibraryTarget
          ? {
              sourceLibraryId: l.sourceLibraryId,
              libraryId: null,
              newLibraryName: l.newName.trim() || l.sourceName,
              newLibraryPath: l.newPath.trim(),
            }
          : {
              sourceLibraryId: l.sourceLibraryId,
              libraryId: l.target,
              newLibraryName: null,
              newLibraryPath: null,
            },
      );
  }

  options(): {
    copyPasswords: boolean;
    permissions: boolean;
    userData: boolean;
    settings: boolean;
    extensions: boolean;
  } {
    return {
      copyPasswords: this.copyPasswords,
      permissions: this.permissions,
      userData: this.userData,
      settings: this.settings,
      extensions: this.extensions,
    };
  }

  /** What the wizard keeps across a reload (the code too: the migration needs it at the end). */
  save(): unknown {
    return {
      how: this.how,
      foundUrl: this.foundUrl,
      foundNode: this.foundNode,
      url: this.url,
      code: this.code,
      link: this.link,
      analysis: this.analysis(),
      analyzed: this.analyzed,
      libraries: this.libraries(),
      sameTarget: this.sameTarget,
      users: this.users(),
      ...this.options(),
    };
  }

  restore(saved: unknown): void {
    if (!saved || typeof saved !== 'object') return;
    const s = saved as ReturnType<ArcanistImportDraft['save']> & Record<string, unknown>;
    Object.assign(this, {
      how: s['how'] ?? this.how,
      foundUrl: s['foundUrl'] ?? null,
      foundNode: s['foundNode'] ?? null,
      url: s['url'] ?? '',
      code: s['code'] ?? '',
      link: s['link'] ?? '',
      analyzed: s['analyzed'] ?? null,
      sameTarget: s['sameTarget'] ?? false,
      copyPasswords: s['copyPasswords'] ?? true,
      permissions: s['permissions'] ?? true,
      userData: s['userData'] ?? true,
      settings: s['settings'] ?? true,
      extensions: s['extensions'] ?? true,
    });
    this.analysis.set((s['analysis'] as Analysis | null) ?? null);
    this.libraries.set((s['libraries'] as ArcanistLibraryDraft[]) ?? []);
    this.users.set((s['users'] as string[]) ?? []);
  }
}

/**
 * Bringing another Arcanist over: find it (on the network, paired, by address or with its P2P link), read it with
 * its migration code, and choose its libraries (and where each goes), users and what else comes. Used by the setup
 * wizard (where every library goes to a new one) and by the Import page. Place it inside a dialog; the host puts
 * the buttons.
 */
@Component({
  selector: 'app-arcanist-import',
  imports: [
    FormsModule,
    NgIcon,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    FolderPicker,
    BytesPipe,
    CountPipe,
  ],
  providers: [provideIcons({ lucideLink, lucideRefreshCw, lucideServer, lucideWaypoints })],
  host: { class: 'block w-full min-w-0' },
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
    } @else if (!draft().analysis()) {
      <form class="grid grid-cols-[minmax(0,1fr)] gap-4" (ngSubmit)="analyze()">
        <hlm-radio-group class="grid gap-2 sm:grid-cols-3" name="how" [(ngModel)]="draft().how">
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio value="found" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span>{{ 'arcanistImport.how.found' | t }}</span>
          </label>
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio value="address" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span>{{ 'arcanistImport.how.address' | t }}</span>
          </label>
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio value="link" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span>{{ 'arcanistImport.how.link' | t }}</span>
          </label>
        </hlm-radio-group>

        @switch (draft().how) {
          @case ('found') {
            <div class="grid gap-2">
              <div class="flex items-center justify-between gap-2">
                <p class="text-sm font-medium">{{ 'arcanistImport.servers.title' | t }}</p>
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  type="button"
                  class="gap-1"
                  [disabled]="searching()"
                  (click)="search()"
                >
                  <ng-icon name="lucideRefreshCw" />{{ 'arcanistImport.servers.search' | t }}
                </button>
              </div>
              @if (searching()) {
                <p class="flex items-center gap-2 text-sm text-muted-foreground">
                  <hlm-spinner class="size-4" />{{ 'arcanistImport.servers.searching' | t }}
                </p>
              } @else if (servers().length === 0) {
                <p class="text-sm text-muted-foreground">{{ 'arcanistImport.servers.none' | t }}</p>
              } @else {
                <ul class="grid max-h-56 gap-2 overflow-y-auto">
                  @for (server of servers(); track server.url ?? server.nodeId) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full items-center gap-3 rounded-lg border p-3 text-left text-sm hover:bg-accent"
                        [class.border-primary]="isChosen(server)"
                        [class.bg-accent]="isChosen(server)"
                        (click)="choose(server)"
                      >
                        <ng-icon
                          [name]="server.via === 'PAIRED' ? 'lucideWaypoints' : 'lucideServer'"
                          class="shrink-0 text-muted-foreground"
                        />
                        <span class="grid min-w-0 gap-0.5">
                          <span class="font-medium">{{ server.name }}</span>
                          <span class="truncate text-xs text-muted-foreground">{{
                            server.via === 'PAIRED'
                              ? ('arcanistImport.servers.paired' | t)
                              : server.url
                          }}</span>
                        </span>
                      </button>
                    </li>
                  }
                </ul>
              }
            </div>
          }
          @case ('address') {
            <hlm-field>
              <label hlmFieldLabel for="arcanist-url">{{ 'arcanistImport.address' | t }}</label>
              <input
                hlmInput
                id="arcanist-url"
                name="url"
                class="font-mono text-xs"
                placeholder="192.168.1.20:8484"
                autocomplete="off"
                [(ngModel)]="draft().url"
              />
              <p hlmFieldDescription>{{ 'arcanistImport.addressHelp' | t }}</p>
            </hlm-field>
          }
          @case ('link') {
            <hlm-field>
              <label hlmFieldLabel for="arcanist-link">{{ 'arcanistImport.link' | t }}</label>
              <textarea
                hlmInput
                id="arcanist-link"
                name="link"
                rows="3"
                class="min-h-20 font-mono text-xs break-all"
                placeholder="arcanist://p2p?kind=migrate&…"
                [(ngModel)]="draft().link"
              ></textarea>
              <p hlmFieldDescription>{{ 'arcanistImport.linkHelp' | t }}</p>
            </hlm-field>
          }
        }

        @if (draft().how !== 'link') {
          <hlm-field>
            <label hlmFieldLabel for="arcanist-code">{{ 'arcanistImport.code' | t }}</label>
            <input
              hlmInput
              id="arcanist-code"
              name="code"
              class="font-mono tracking-widest uppercase"
              placeholder="XXXX-XXXX-XXXX-XXXX-XXXX"
              autocomplete="off"
              [(ngModel)]="draft().code"
            />
            <p hlmFieldDescription>{{ 'arcanistImport.codeHelp' | t }}</p>
          </hlm-field>
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
          {{ 'arcanistImport.read' | t }}
        </button>
      </form>
    } @else if (draft().analysis(); as found) {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-5">
        <div
          class="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/40 p-3"
        >
          <p class="flex min-w-0 items-center gap-2 text-sm">
            <ng-icon
              [name]="found.viaP2P ? 'lucideWaypoints' : 'lucideServer'"
              class="shrink-0 text-muted-foreground"
            />
            <span class="truncate font-medium">{{ found.serverName }}</span>
            <span class="text-xs text-muted-foreground">Arcanist {{ found.version }}</span>
            @if (found.viaP2P) {
              <span hlmBadge variant="outline">P2P</span>
            }
          </p>
          <button hlmBtn variant="ghost" size="sm" type="button" (click)="draft().forget()">
            {{ 'arcanistImport.change' | t }}
          </button>
        </div>

        <section class="grid gap-3">
          <div class="flex flex-wrap items-end justify-between gap-2">
            <div>
              <h3 class="text-sm font-medium">{{ 'arcanistImport.libraries.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">
                {{
                  (setup() ? 'arcanistImport.libraries.helpSetup' : 'arcanistImport.libraries.help')
                    | t
                }}
              </p>
            </div>
            @if (found.libraries.length > 1) {
              <label class="flex items-center gap-2 text-sm">
                <hlm-switch
                  [checked]="draft().sameTarget"
                  (checkedChange)="draft().setSameTarget($event)"
                />
                {{ 'arcanistImport.libraries.same' | t }}
              </label>
            }
          </div>
          @for (library of draft().libraries(); track library.sourceLibraryId; let index = $index) {
            <div class="grid gap-3 rounded-md border p-3">
              <label class="flex flex-wrap items-center gap-3 text-sm">
                <hlm-checkbox
                  [checked]="library.include"
                  (checkedChange)="draft().update(index, { include: $event })"
                />
                <span class="font-medium">{{ library.sourceName }}</span>
                <span class="text-xs text-muted-foreground">
                  {{ 'arcanistImport.libraries.files' | t: { count: (library.files | count) } }}
                  · {{ library.bytes | bytes }}
                </span>
              </label>
              @if (library.include && (!draft().sameTarget || index === 0)) {
                @if (draft().sameTarget) {
                  <p class="text-xs text-muted-foreground">
                    {{ 'arcanistImport.libraries.sameHelp' | t }}
                  </p>
                }
                <div class="grid gap-2 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
                  @if (!setup()) {
                    <hlm-native-select
                      [value]="library.target"
                      (change)="draft().update(index, { target: $any($event.target).value })"
                    >
                      <option hlmNativeSelectOption value="new">
                        {{ 'arcanistImport.libraries.new' | t }}
                      </option>
                      @for (target of targets(); track target.id) {
                        <option hlmNativeSelectOption [value]="target.id">
                          {{ target.label }}
                        </option>
                      }
                    </hlm-native-select>
                  } @else {
                    <input
                      hlmInput
                      [attr.aria-label]="'arcanistImport.libraries.name' | t"
                      [placeholder]="'arcanistImport.libraries.name' | t"
                      [ngModel]="library.newName"
                      (ngModelChange)="draft().update(index, { newName: $event })"
                    />
                  }
                  @if (library.target === 'new') {
                    <div class="flex gap-2">
                      <input
                        hlmInput
                        class="min-w-0 flex-1 font-mono text-xs"
                        [attr.aria-label]="'arcanistImport.libraries.folder' | t"
                        [placeholder]="'arcanistImport.libraries.folder' | t"
                        [ngModel]="library.newPath"
                        (ngModelChange)="draft().update(index, { newPath: $event })"
                      />
                      <button hlmBtn variant="outline" type="button" (click)="picking.set(index)">
                        {{ 'mstream.browse' | t }}
                      </button>
                    </div>
                    @if (!setup()) {
                      <input
                        hlmInput
                        class="sm:col-span-2"
                        [attr.aria-label]="'arcanistImport.libraries.name' | t"
                        [placeholder]="'arcanistImport.libraries.name' | t"
                        [ngModel]="library.newName"
                        (ngModelChange)="draft().update(index, { newName: $event })"
                      />
                    }
                  }
                </div>
              }
            </div>
          } @empty {
            <p class="text-sm text-muted-foreground">{{ 'arcanistImport.libraries.none' | t }}</p>
          }
          <p class="text-xs text-muted-foreground">{{ 'arcanistImport.libraries.copyNote' | t }}</p>
        </section>

        <section class="grid gap-3">
          <div class="flex flex-wrap items-end justify-between gap-2">
            <div>
              <h3 class="text-sm font-medium">{{ 'arcanistImport.users.title' | t }}</h3>
              <p class="text-xs text-muted-foreground">{{ 'arcanistImport.users.help' | t }}</p>
            </div>
            @if (found.users.length > 1) {
              <div class="flex gap-1">
                <button hlmBtn variant="ghost" size="sm" type="button" (click)="allUsers(true)">
                  {{ 'arcanistImport.users.all' | t }}
                </button>
                <button hlmBtn variant="ghost" size="sm" type="button" (click)="allUsers(false)">
                  {{ 'arcanistImport.users.none' | t }}
                </button>
              </div>
            }
          </div>
          <ul class="grid max-h-60 gap-1 overflow-y-auto rounded-md border p-2">
            @for (user of found.users; track user.id) {
              <li>
                <label
                  class="flex flex-wrap items-center gap-2 rounded px-1 py-1 text-sm hover:bg-accent"
                >
                  <hlm-checkbox
                    [checked]="draft().users().includes(user.id)"
                    (checkedChange)="draft().toggleUser(user.id, $event)"
                  />
                  <span class="font-medium">{{ user.userName }}</span>
                  @if (user.displayName) {
                    <span class="text-xs text-muted-foreground">{{ user.displayName }}</span>
                  }
                  @if (user.role === 'ADMIN') {
                    <span hlmBadge variant="secondary">{{ 'users.roles.ADMIN' | t }}</span>
                  }
                  @if (sameAsAdmin(user.userName)) {
                    <span hlmBadge variant="outline">{{ 'arcanistImport.users.isAdmin' | t }}</span>
                  } @else if (user.exists) {
                    <span hlmBadge variant="outline">{{ 'arcanistImport.users.exists' | t }}</span>
                  }
                  @if (user.isDisabled) {
                    <span hlmBadge variant="outline">{{
                      'arcanistImport.users.disabled' | t
                    }}</span>
                  }
                  @if (!user.hasPassword) {
                    <span hlmBadge variant="outline">{{
                      'arcanistImport.users.noPassword' | t
                    }}</span>
                  }
                  @if (user.secondFactor) {
                    <span hlmBadge variant="outline">{{
                      'arcanistImport.users.secondFactor' | t
                    }}</span>
                  }
                </label>
              </li>
            } @empty {
              <li class="p-1 text-sm text-muted-foreground">
                {{ 'arcanistImport.users.empty' | t }}
              </li>
            }
          </ul>
        </section>

        <section class="grid gap-3">
          <h3 class="text-sm font-medium">{{ 'arcanistImport.options.title' | t }}</h3>
          @for (option of optionKeys; track option) {
            <label class="flex items-start gap-3 text-sm">
              <hlm-switch
                class="mt-0.5"
                [checked]="draft()[option]"
                (checkedChange)="draft()[option] = $event"
              />
              <span>
                {{ 'arcanistImport.options.' + option | t }}
                <span class="block text-xs text-muted-foreground">{{
                  'arcanistImport.options.' + option + 'Help' | t
                }}</span>
              </span>
            </label>
          }
          @if (draft().extensions && components().length > 0) {
            <ul class="flex flex-wrap gap-1 pl-12">
              @for (component of components(); track component.id) {
                <li>
                  <span
                    hlmBadge
                    [variant]="component.available ? 'secondary' : 'outline'"
                    [title]="
                      (component.installed
                        ? 'arcanistImport.options.installed'
                        : component.available
                          ? 'arcanistImport.options.available'
                          : 'arcanistImport.options.unavailable'
                      ) | t
                    "
                    [class.line-through]="!component.available"
                    >{{ component.name }}</span
                  >
                </li>
              }
            </ul>
          }
        </section>
      </div>
    }
  `,
})
export class ArcanistImportForm implements OnInit {
  private readonly i18n = inject(I18n);

  readonly draft = input.required<ArcanistImportDraft>();
  readonly api = input.required<ArcanistImportApi>();
  readonly loadDirectories = input.required<DirectoryLoader>();
  readonly createDirectory = input.required<DirectoryCreator>();
  /** The setup wizard: every library goes to a new one (the libraries step shows them). */
  readonly setup = input(false);
  /** Libraries of this server music can go to (the panel). */
  readonly targets = input<{ id: string; label: string }[]>([]);
  /** The administrator being created by the wizard: a user of that name is them. */
  readonly adminName = input('');

  protected readonly optionKeys = [
    'copyPasswords',
    'permissions',
    'userData',
    'settings',
    'extensions',
  ] as const;

  protected readonly servers = signal<ArcanistServer[]>([]);
  protected readonly searching = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** The library whose folder is being picked. */
  protected readonly picking = signal<number | null>(null);

  protected readonly components = computed(() => {
    const found = this.draft().analysis();
    return found
      ? [
          ...found.extensions.filter((e) => !e.builtIn || e.enabled),
          ...found.players.filter((p) => !p.builtIn),
        ]
      : [];
  });

  async ngOnInit(): Promise<void> {
    if (!this.draft().analysis() && this.draft().how === 'found') await this.search();
  }

  protected async search(): Promise<void> {
    this.searching.set(true);
    try {
      this.servers.set(await this.api().servers());
    } catch (error) {
      this.error.set(this.message(error));
    } finally {
      this.searching.set(false);
    }
  }

  protected isChosen(server: ArcanistServer): boolean {
    const draft = this.draft();
    return (server.url ?? null) === draft.foundUrl && (server.nodeId ?? null) === draft.foundNode;
  }

  protected choose(server: ArcanistServer): void {
    this.draft().foundUrl = server.url ?? null;
    this.draft().foundNode = server.nodeId ?? null;
  }

  protected canAnalyze(): boolean {
    const draft = this.draft();
    switch (draft.how) {
      case 'link':
        return draft.link.trim().length > 0;
      case 'address':
        return draft.url.trim().length > 0 && draft.code.trim().length > 0;
      default:
        return (
          (draft.foundUrl !== null || draft.foundNode !== null) && draft.code.trim().length > 0
        );
    }
  }

  protected async analyze(): Promise<void> {
    if (!this.canAnalyze()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const source = this.draft().source();
      this.draft().read(await this.api().analyze(source), source, this.setup());
    } catch (error) {
      this.error.set(this.message(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected allUsers(on: boolean): void {
    this.draft().users.set(
      on
        ? (this.draft()
            .analysis()
            ?.users.map((u) => u.id) ?? [])
        : [],
    );
  }

  protected sameAsAdmin(userName: string): boolean {
    const admin = this.adminName().trim().toLowerCase();
    return admin.length > 0 && userName.toLowerCase() === admin;
  }

  protected picked(path: string): void {
    const index = this.picking();
    this.picking.set(null);
    if (index === null) return;
    const name = path.split(/[\\/]/).filter(Boolean).pop() ?? '';
    const library = this.draft().libraries()[index];
    this.draft().update(index, {
      newPath: path,
      newName: library && library.newName.trim() ? library.newName : name,
    });
  }

  private message(error: unknown): string {
    const apiError = ApiError.from(error);
    return this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code);
  }
}
