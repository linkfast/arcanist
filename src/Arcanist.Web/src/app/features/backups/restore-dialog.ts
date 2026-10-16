import { Component, WritableSignal, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
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
  BackupDestinationFieldsFragment as Destination,
  BackupSnapshotContentsQuery,
  RestoreJobFieldsFragment as RestoreJob,
  StoredBackupsQuery,
} from '../../graphql/generated/graphql';
import {
  BackupSnapshotContentsQuery as SnapshotContentsQuery,
  StartRestoreMutation,
  StoredBackupsQuery as StoredQuery,
} from '../../graphql/operations';
import { BytesPipe, DateTimePipe } from '../../shared/format';
import type { BackupOptions } from './backup-form';

type StoredBackup = StoredBackupsQuery['storedBackups'][number];
type Snapshot = StoredBackup['snapshots'][number];
type Contents = BackupSnapshotContentsQuery['backupSnapshotContents'];

type Step = 'destination' | 'snapshot' | 'what' | 'where' | 'users' | 'confirm';
type FilesMode = 'ALL' | 'LIBRARIES' | 'ALBUMS' | 'PLAYLISTS';

/** Brings files and user data back from a snapshot, step by step, without overwriting anything. */
@Component({
  selector: 'app-restore-dialog',
  imports: [
    FormsModule,
    HlmAlertImports,
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
    BytesPipe,
    DateTimePipe,
  ],
  host: { class: 'grid w-full min-w-0 grid-cols-[minmax(0,1fr)] gap-5' },
  template: `
    <hlm-dialog-header>
      <p class="text-xs text-muted-foreground">
        {{
          'backups.restore.stepOf'
            | t: { current: steps().indexOf(step()) + 1, total: steps().length }
        }}
      </p>
      <h3 hlmDialogTitle>{{ 'backups.restore.steps.' + step() + '.title' | t }}</h3>
      <p hlmDialogDescription>{{ 'backups.restore.steps.' + step() + '.description' | t }}</p>
    </hlm-dialog-header>

    @switch (step()) {
      @case ('destination') {
        <hlm-radio-group
          class="grid gap-2"
          name="destination"
          [ngModel]="destinationId()"
          (ngModelChange)="chooseDestination($event)"
        >
          @for (destination of destinations(); track destination.id) {
            <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio [value]="destination.id" class="mt-0.5"
                ><hlm-radio-indicator indicator
              /></hlm-radio>
              <span class="grid min-w-0 gap-0.5">
                <span class="font-medium">{{ destination.name }}</span>
                <span class="truncate text-xs text-muted-foreground" [title]="destination.location">
                  {{ 'backups.destinations.kinds.' + destination.kind | t }} ·
                  {{ destination.location }}
                </span>
              </span>
            </label>
          }
        </hlm-radio-group>
      }

      @case ('snapshot') {
        @if (stored() === null) {
          <div class="flex justify-center py-8"><hlm-spinner /></div>
        } @else {
          <div class="grid max-h-[50vh] gap-3 overflow-y-auto">
            @for (backup of stored(); track backup.folder) {
              <section class="grid gap-2 rounded-lg border p-3">
                <div class="flex flex-wrap items-center gap-2 text-sm">
                  <span class="font-medium">{{ backup.name }}</span>
                  <span hlmBadge variant="outline">{{ scopeName(backup.scope) }}</span>
                </div>
                <p class="text-xs text-muted-foreground">
                  {{
                    'backups.restore.madeBy'
                      | t: { machine: backup.machine, version: backup.arcanistVersion }
                  }}
                </p>
                <ul class="grid gap-1">
                  @for (snapshot of newestFirst(backup); track snapshot.name) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-left text-sm hover:bg-accent"
                        [class.border-primary]="isChosenSnapshot(backup, snapshot)"
                        [class.bg-accent]="isChosenSnapshot(backup, snapshot)"
                        (click)="chooseSnapshot(backup, snapshot)"
                      >
                        <span class="font-medium">{{ snapshot.createdAt | dateTime }}</span>
                        <span class="text-xs text-muted-foreground">
                          {{ 'backups.restore.files' | t: { count: snapshot.files } }} ·
                          {{ snapshot.bytes | bytes }}
                        </span>
                        <span class="ml-auto flex gap-1">
                          @if (snapshot.userData) {
                            <span hlmBadge variant="secondary">{{
                              'backups.restore.userDataBadge' | t
                            }}</span>
                          }
                          @if (snapshot.server) {
                            <span hlmBadge variant="secondary">{{
                              'backups.restore.serverBadge' | t
                            }}</span>
                          }
                        </span>
                      </button>
                    </li>
                  } @empty {
                    <li class="text-xs text-muted-foreground">
                      {{ 'backups.restore.noSnapshots' | t }}
                    </li>
                  }
                </ul>
              </section>
            } @empty {
              <p class="text-sm text-muted-foreground">{{ 'backups.restore.noBackups' | t }}</p>
            }
          </div>
        }
      }

      @case ('what') {
        @if (contents() === null) {
          <div class="flex justify-center py-8"><hlm-spinner /></div>
        } @else {
          <div class="grid gap-3">
            <label class="flex items-start gap-3 text-sm">
              <hlm-checkbox
                class="mt-0.5"
                [checked]="restoreFiles()"
                (checkedChange)="restoreFiles.set($event)"
              />
              <span class="grid gap-0.5">
                {{ 'backups.restore.what.files' | t }}
                <span class="text-xs text-muted-foreground">{{
                  'backups.restore.what.filesHelp' | t
                }}</span>
              </span>
            </label>
            @if (snapshot()?.userData) {
              <label class="flex items-start gap-3 text-sm">
                <hlm-checkbox
                  class="mt-0.5"
                  [checked]="restoreUserData()"
                  (checkedChange)="restoreUserData.set($event)"
                />
                <span class="grid gap-0.5">
                  {{ 'backups.restore.what.userData' | t }}
                  <span class="text-xs text-muted-foreground">{{
                    'backups.restore.what.userDataHelp' | t
                  }}</span>
                </span>
              </label>
            }
          </div>

          @if (restoreFiles()) {
            <div class="grid gap-3 rounded-lg border p-3">
              <select
                hlmNativeSelect
                name="filesMode"
                [attr.aria-label]="'backups.restore.what.which' | t"
                [ngModel]="filesMode()"
                (ngModelChange)="filesMode.set($event)"
              >
                @for (mode of filesModes; track mode) {
                  <option hlmNativeSelectOption [value]="mode">
                    {{ 'backups.restore.what.modes.' + mode | t }}
                  </option>
                }
              </select>

              @switch (filesMode()) {
                @case ('LIBRARIES') {
                  <div class="grid max-h-64 gap-2 overflow-y-auto">
                    @for (library of contents()!.libraries; track library.folder) {
                      <label class="flex items-center gap-2 text-sm">
                        <hlm-checkbox
                          [checked]="chosenLibraries().has(library.folder)"
                          (checkedChange)="toggleIn(chosenLibraries, library.folder, $event)"
                        />
                        <span class="min-w-0 truncate">
                          {{ library.name }}
                          <span class="text-xs text-muted-foreground"
                            >· {{ 'backups.restore.files' | t: { count: library.files } }} ·
                            {{ library.bytes | bytes }}</span
                          >
                        </span>
                      </label>
                    } @empty {
                      <p class="text-sm text-muted-foreground">
                        {{ 'backups.restore.what.none' | t }}
                      </p>
                    }
                  </div>
                }
                @case ('ALBUMS') {
                  <input
                    hlmInput
                    name="albumFilter"
                    autocomplete="off"
                    [placeholder]="'backups.restore.what.albumFilter' | t"
                    [ngModel]="albumFilter()"
                    (ngModelChange)="albumFilter.set($event)"
                  />
                  <p class="text-xs text-muted-foreground">
                    {{ 'backups.restore.what.chosen' | t: { count: chosenAlbums().size } }}
                  </p>
                  <div class="grid max-h-64 gap-2 overflow-y-auto">
                    @for (album of filteredAlbums(); track album.id) {
                      <label class="flex items-center gap-2 text-sm">
                        <hlm-checkbox
                          [checked]="chosenAlbums().has(album.id)"
                          (checkedChange)="toggleIn(chosenAlbums, album.id, $event)"
                        />
                        <span class="min-w-0 truncate">
                          {{ album.artist }} — {{ album.title }}
                          @if (album.year) {
                            <span class="text-muted-foreground">({{ album.year }})</span>
                          }
                        </span>
                      </label>
                    } @empty {
                      <p class="text-sm text-muted-foreground">
                        {{ 'backups.restore.what.none' | t }}
                      </p>
                    }
                  </div>
                }
                @case ('PLAYLISTS') {
                  <p class="text-xs text-muted-foreground">
                    {{ 'backups.restore.what.playlistsHelp' | t }}
                  </p>
                  <div class="grid max-h-64 gap-2 overflow-y-auto">
                    @for (playlist of contents()!.playlists; track $index) {
                      <label class="flex items-center gap-2 text-sm">
                        <hlm-checkbox
                          [checked]="chosenPlaylists().has($index)"
                          (checkedChange)="toggleIn(chosenPlaylists, $index, $event)"
                        />
                        <span class="min-w-0 truncate">
                          {{ playlist.name }}
                          <span class="text-xs text-muted-foreground"
                            >· {{ playlist.owner }} ·
                            {{ 'backups.form.tracks' | t: { count: playlist.tracks } }}</span
                          >
                        </span>
                      </label>
                    } @empty {
                      <p class="text-sm text-muted-foreground">
                        {{ 'backups.restore.what.none' | t }}
                      </p>
                    }
                  </div>
                }
              }
            </div>
          }
        }
      }

      @case ('where') {
        <hlm-radio-group
          class="grid gap-2"
          name="target"
          [ngModel]="target()"
          (ngModelChange)="target.set($event)"
        >
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio value="ORIGINAL" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'backups.restore.where.original' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'backups.restore.where.originalHelp' | t
              }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio value="OTHER" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'backups.restore.where.other' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'backups.restore.where.otherHelp' | t
              }}</span>
            </span>
          </label>
        </hlm-radio-group>

        @if (target() === 'ORIGINAL') {
          @if (missingLibraries().length > 0) {
            <div
              class="grid gap-1 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300"
            >
              <p>{{ 'backups.restore.where.missing' | t }}</p>
              <ul class="list-disc pl-4">
                @for (library of missingLibraries(); track library.folder) {
                  <li>{{ library.name }}</li>
                }
              </ul>
            </div>
          }
        } @else {
          <hlm-field>
            <label hlmFieldLabel for="restore-target">{{
              'backups.restore.where.library' | t
            }}</label>
            <select
              hlmNativeSelect
              id="restore-target"
              name="targetSourceId"
              [ngModel]="targetSourceId()"
              (ngModelChange)="targetSourceId.set($event)"
            >
              @for (option of targets(); track option.id) {
                <option hlmNativeSelectOption [value]="option.id">{{ option.label }}</option>
              }
            </select>
            @if (targets().length === 0) {
              <p class="text-sm text-muted-foreground">
                {{ 'backups.restore.where.noTargets' | t }}
              </p>
            }
          </hlm-field>
        }
      }

      @case ('users') {
        <div class="grid max-h-[50vh] gap-2 overflow-y-auto">
          @for (user of contents()?.users ?? []; track user.userName) {
            <div class="grid items-center gap-2 rounded-lg border p-3 text-sm sm:grid-cols-2">
              <span class="grid gap-0.5">
                <span class="font-medium">{{ user.displayName || user.userName }}</span>
                <span class="text-xs text-muted-foreground">
                  {{
                    'backups.restore.users.counts'
                      | t: { ratings: user.ratings, plays: user.plays, playlists: user.playlists }
                  }}
                </span>
              </span>
              <select
                hlmNativeSelect
                [name]="'user-' + user.userName"
                [attr.aria-label]="'backups.restore.users.to' | t: { name: user.userName }"
                [ngModel]="userMap()[user.userName] ?? ''"
                (ngModelChange)="mapUser(user.userName, $event)"
              >
                <option hlmNativeSelectOption value="">
                  {{ 'backups.restore.users.skip' | t }}
                </option>
                @for (local of users(); track local.id) {
                  <option hlmNativeSelectOption [value]="local.id">
                    {{ local.displayName || local.userName }} ({{ local.userName }})
                  </option>
                }
              </select>
            </div>
          }
        </div>
      }

      @case ('confirm') {
        <ul class="grid gap-1 text-sm">
          <li>
            <span class="text-muted-foreground">{{ 'backups.restore.confirm.from' | t }}</span>
            {{ chosenBackup()?.name }} · {{ snapshot()?.createdAt | dateTime }}
          </li>
          <li>
            <span class="text-muted-foreground">{{ 'backups.restore.confirm.what' | t }}</span>
            {{ whatSummary() }}
          </li>
          @if (restoreFiles()) {
            <li>
              <span class="text-muted-foreground">{{ 'backups.restore.confirm.where' | t }}</span>
              {{ target() === 'ORIGINAL' ? ('backups.restore.where.original' | t) : targetLabel() }}
            </li>
          }
          @if (withUserData() && (contents()?.users?.length ?? 0) > 0) {
            <li>
              <span class="text-muted-foreground">{{ 'backups.restore.confirm.users' | t }}</span>
              {{ 'backups.restore.confirm.usersCount' | t: { count: mappedUsers() } }}
            </li>
          }
        </ul>
        <div hlmAlert>
          <p hlmAlertDescription>{{ 'backups.restore.confirm.noOverwrite' | t }}</p>
        </div>
      }
    }

    @if (error(); as message) {
      <div hlmAlert variant="destructive">
        <p hlmAlertDescription>{{ message }}</p>
      </div>
    }

    <hlm-dialog-footer>
      <button hlmBtn variant="ghost" type="button" (click)="cancelled.emit()">
        {{ 'common.cancel' | t }}
      </button>
      @if (step() !== 'destination') {
        <button hlmBtn variant="outline" type="button" [disabled]="busy()" (click)="back()">
          {{ 'common.back' | t }}
        </button>
      }
      @if (step() === 'confirm') {
        <button hlmBtn type="button" [disabled]="busy()" (click)="start()">
          @if (busy()) {
            <hlm-spinner class="size-4" />
          }
          {{ 'backups.restore.start' | t }}
        </button>
      } @else {
        <button hlmBtn type="button" [disabled]="busy() || !canContinue()" (click)="next()">
          {{ 'common.next' | t }}
        </button>
      }
    </hlm-dialog-footer>
  `,
})
export class RestoreDialog {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly destinations = input.required<Destination[]>();
  readonly options = input.required<BackupOptions>();
  readonly started = output<RestoreJob>();
  readonly cancelled = output<void>();

  protected readonly filesModes: FilesMode[] = ['ALL', 'LIBRARIES', 'ALBUMS', 'PLAYLISTS'];

  protected readonly step = signal<Step>('destination');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly destinationId = signal<string | null>(null);
  protected readonly stored = signal<StoredBackup[] | null>(null);
  protected readonly chosenBackup = signal<StoredBackup | null>(null);
  protected readonly snapshot = signal<Snapshot | null>(null);
  protected readonly contents = signal<Contents | null>(null);

  protected readonly restoreFiles = signal(true);
  protected readonly restoreUserData = signal(true);
  protected readonly filesMode = signal<FilesMode>('ALL');
  protected readonly chosenLibraries = signal(new Set<string>());
  protected readonly chosenAlbums = signal(new Set<string>());
  protected readonly chosenPlaylists = signal(new Set<number>());
  protected readonly albumFilter = signal('');

  protected readonly target = signal<'ORIGINAL' | 'OTHER'>('ORIGINAL');
  protected readonly targetSourceId = signal<string | null>(null);
  /** For each user of the snapshot, the user of this server who receives their data ('' for nobody). */
  protected readonly userMap = signal<Record<string, string>>({});

  protected readonly users = computed(() => this.options().users);

  /** Library folders restored files can go to: on this server's disks or in S3. */
  protected readonly targets = computed(() =>
    this.options().libraries.flatMap((library) =>
      (library.sources ?? [])
        .filter((source) => source.kind === 'LOCAL' || source.kind === 'S3')
        .map((source) => ({ id: source.id, label: `${library.name} — ${source.location}` })),
    ),
  );

  /** The steps this restore goes through. */
  protected readonly steps = computed<Step[]>(() => {
    const steps: Step[] = ['destination', 'snapshot', 'what'];
    if (this.restoreFiles()) steps.push('where');
    if (this.withUserData() && (this.contents()?.users.length ?? 0) > 0) steps.push('users');
    steps.push('confirm');
    return steps;
  });

  protected readonly filteredAlbums = computed(() => {
    const albums = this.contents()?.albums ?? [];
    const filter = normalize(this.albumFilter());
    if (!filter) return albums;
    return albums.filter((album) =>
      normalize(`${album.artist} ${album.title} ${album.year ?? ''}`).includes(filter),
    );
  });

  /** Libraries of the backup that this server does not have: their files cannot go back where they were. */
  protected readonly missingLibraries = computed(() => {
    const libraries = this.chosenBackup()?.libraries ?? [];
    const chosen = this.filesMode() === 'LIBRARIES' ? this.chosenLibraries() : null;
    return libraries.filter((l) => !l.sourceId && (!chosen || chosen.has(l.folder)));
  });

  protected scopeName(scope: string): string {
    const key = `backups.scopes.${scope.toUpperCase()}.name`;
    const name = this.i18n.t(key);
    return name === key ? scope : name;
  }

  /** The latest snapshot first: usually the one to restore. */
  protected newestFirst(backup: StoredBackup): Snapshot[] {
    return [...backup.snapshots].reverse();
  }

  protected isChosenSnapshot(backup: StoredBackup, snapshot: Snapshot): boolean {
    return this.chosenBackup()?.folder === backup.folder && this.snapshot()?.name === snapshot.name;
  }

  protected chooseDestination(id: string): void {
    if (id === this.destinationId()) return;
    this.destinationId.set(id);
    this.stored.set(null);
    this.chosenBackup.set(null);
    this.snapshot.set(null);
    this.contents.set(null);
  }

  protected chooseSnapshot(backup: StoredBackup, snapshot: Snapshot): void {
    if (this.isChosenSnapshot(backup, snapshot)) return;
    this.chosenBackup.set(backup);
    this.snapshot.set(snapshot);
    this.contents.set(null);
    this.restoreUserData.set(snapshot.userData);
    this.chosenLibraries.set(new Set());
    this.chosenAlbums.set(new Set());
    this.chosenPlaylists.set(new Set());
  }

  protected toggleIn<T>(set: WritableSignal<Set<T>>, value: T, on: boolean): void {
    set.update((current) => {
      const next = new Set(current);
      if (on) next.add(value);
      else next.delete(value);
      return next;
    });
  }

  protected mapUser(userName: string, userId: string): void {
    this.userMap.update((map) => ({ ...map, [userName]: userId }));
  }

  protected mappedUsers(): number {
    return Object.values(this.userMap()).filter(Boolean).length;
  }

  protected withUserData(): boolean {
    return this.restoreUserData() && !!this.snapshot()?.userData;
  }

  protected canContinue(): boolean {
    switch (this.step()) {
      case 'destination':
        return !!this.destinationId();
      case 'snapshot':
        return !!this.snapshot();
      case 'what':
        if (!this.contents()) return false;
        if (!this.restoreFiles() && !this.withUserData()) return false;
        return !this.restoreFiles() || this.filesMode() === 'ALL' || this.paths().length > 0;
      case 'where':
        return this.target() === 'ORIGINAL' || !!this.targetSourceId();
      default:
        return true;
    }
  }

  protected async next(): Promise<void> {
    if (!this.canContinue()) return;
    this.error.set(null);
    const steps = this.steps();
    const following = steps[steps.indexOf(this.step()) + 1];
    if (!following) return;
    this.step.set(following);
    if (following === 'snapshot' && this.stored() === null) await this.loadStored();
    if (following === 'what' && this.contents() === null) await this.loadContents();
    if (following === 'where' && !this.targetSourceId())
      this.targetSourceId.set(this.targets()[0]?.id ?? null);
  }

  protected back(): void {
    this.error.set(null);
    const steps = this.steps();
    const previous = steps[steps.indexOf(this.step()) - 1];
    if (previous) this.step.set(previous);
  }

  private async loadStored(): Promise<void> {
    const destinationId = this.destinationId();
    if (!destinationId) return;
    try {
      const result = await this.gql.query(StoredQuery, { destinationId });
      this.stored.set(result.storedBackups);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
      this.step.set('destination');
    }
  }

  private async loadContents(): Promise<void> {
    const destinationId = this.destinationId();
    const backup = this.chosenBackup();
    const snapshot = this.snapshot();
    if (!destinationId || !backup || !snapshot) return;
    try {
      const result = await this.gql.query(SnapshotContentsQuery, {
        destinationId,
        folder: backup.folder,
        snapshot: snapshot.name,
      });
      const contents = result.backupSnapshotContents;
      this.contents.set(contents);
      // Each user's data goes to the user of the same name, when this server has one.
      this.userMap.set(
        Object.fromEntries(contents.users.map((user) => [user.userName, user.userId ?? ''])),
      );
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
      this.step.set('snapshot');
    }
  }

  /** The backup's paths (or folders) to restore; empty for everything. */
  private paths(): string[] {
    const contents = this.contents();
    if (!contents) return [];
    let paths: string[];
    switch (this.filesMode()) {
      case 'LIBRARIES':
        paths = [...this.chosenLibraries()];
        break;
      case 'ALBUMS':
        paths = contents.albums
          .filter((a) => this.chosenAlbums().has(a.id))
          .flatMap((a) => a.paths);
        break;
      case 'PLAYLISTS':
        paths = contents.playlists
          .filter((_, index) => this.chosenPlaylists().has(index))
          .flatMap((p) => p.paths);
        break;
      default:
        return [];
    }
    return [...new Set(paths)];
  }

  protected whatSummary(): string {
    const parts: string[] = [];
    if (this.restoreFiles()) {
      const mode = this.filesMode();
      const count =
        mode === 'LIBRARIES'
          ? this.chosenLibraries().size
          : mode === 'ALBUMS'
            ? this.chosenAlbums().size
            : this.chosenPlaylists().size;
      parts.push(this.i18n.t('backups.restore.confirm.files.' + mode, { count }));
    }
    if (this.withUserData()) parts.push(this.i18n.t('backups.restore.what.userData'));
    return parts.join(' · ');
  }

  protected targetLabel(): string {
    return this.targets().find((t) => t.id === this.targetSourceId())?.label ?? '';
  }

  protected async start(): Promise<void> {
    const destinationId = this.destinationId();
    const backup = this.chosenBackup();
    const snapshot = this.snapshot();
    if (!destinationId || !backup || !snapshot) return;
    this.busy.set(true);
    this.error.set(null);
    const files = this.restoreFiles();
    const userData = this.withUserData();
    try {
      const result = await this.gql.mutate(StartRestoreMutation, {
        input: {
          destinationId,
          folder: backup.folder,
          snapshot: snapshot.name,
          files,
          userData,
          paths: files ? this.paths() : null,
          targetSourceId: files && this.target() === 'OTHER' ? this.targetSourceId() : null,
          users: userData
            ? (this.contents()?.users ?? []).map((user) => ({
                userName: user.userName,
                userId: this.userMap()[user.userName] || null,
              }))
            : null,
        },
      });
      this.started.emit(result.startRestore);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}

function normalize(text: string): string {
  return text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();
}
