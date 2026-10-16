import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  BackupDestinationFieldsFragment as Destination,
  BackupFieldsFragment as Backup,
  BackupOptionsQuery,
  BackupSchedule,
  BackupScope,
  DayOfWeek,
} from '../../graphql/generated/graphql';
import {
  BackupAlbumSearchQuery,
  CreateBackupMutation,
  UpdateBackupMutation,
} from '../../graphql/operations';
import { DAYS_OF_WEEK, browserTimeZone, minutesToTime, timeToMinutes } from './schedule';

export type BackupOptions = BackupOptionsQuery;

interface Item {
  id: string;
  name: string;
}

/** Shortest password for the server's part of a backup (the server checks it too). */
const MIN_PASSWORD = 8;
const MAX_INTERVAL_HOURS = 720;

/** Creates or changes a backup: what it covers, where it goes, its password, schedule and retention. */
@Component({
  selector: 'app-backup-form',
  imports: [
    FormsModule,
    NgIcon,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucidePlus, lucideX })],
  host: { class: 'block w-full min-w-0' },
  template: `
    <form class="grid grid-cols-[minmax(0,1fr)] gap-5" (ngSubmit)="save()">
      <hlm-dialog-header>
        <h3 hlmDialogTitle>{{ (backup() ? 'backups.form.edit' : 'backups.form.create') | t }}</h3>
        <p hlmDialogDescription>{{ 'backups.form.description' | t }}</p>
      </hlm-dialog-header>

      <hlm-field>
        <label hlmFieldLabel for="backup-name">{{ 'backups.form.name' | t }}</label>
        <input
          hlmInput
          id="backup-name"
          name="name"
          [placeholder]="'backups.form.namePlaceholder' | t"
          [(ngModel)]="name"
          required
        />
        @if (fieldError('name'); as message) {
          <hlm-field-error>{{ message }}</hlm-field-error>
        }
      </hlm-field>

      <hlm-field>
        <label hlmFieldLabel for="backup-destination">{{ 'backups.form.destination' | t }}</label>
        @if (backup(); as current) {
          <p class="rounded-md bg-muted px-3 py-1.5 text-sm">
            {{ current.destination.name }}
            <span class="text-muted-foreground"
              >· {{ 'backups.destinations.kinds.' + current.destination.kind | t }}</span
            >
          </p>
          <p hlmFieldDescription>{{ 'backups.form.destinationFixed' | t }}</p>
        } @else {
          <select
            hlmNativeSelect
            id="backup-destination"
            name="destination"
            [(ngModel)]="destinationId"
          >
            @for (destination of destinations(); track destination.id) {
              <option hlmNativeSelectOption [value]="destination.id">
                {{ destination.name }} ·
                {{ 'backups.destinations.kinds.' + destination.kind | t }}
              </option>
            }
          </select>
        }
        @if (fieldError('destinationId'); as message) {
          <hlm-field-error>{{ message }}</hlm-field-error>
        }
      </hlm-field>

      <hlm-field>
        <label hlmFieldLabel for="backup-scope">{{ 'backups.form.scope' | t }}</label>
        @if (backup()) {
          <p class="rounded-md bg-muted px-3 py-1.5 text-sm">
            {{ 'backups.scopes.' + scope + '.name' | t }}
          </p>
        } @else {
          <select
            hlmNativeSelect
            id="backup-scope"
            name="scope"
            [ngModel]="scope"
            (ngModelChange)="setScope($event)"
          >
            @for (option of scopes; track option) {
              <option hlmNativeSelectOption [value]="option">
                {{ 'backups.scopes.' + option + '.name' | t }}
              </option>
            }
          </select>
        }
        <p hlmFieldDescription>{{ 'backups.scopes.' + scope + '.help' | t }}</p>
      </hlm-field>

      @switch (scope) {
        @case ('LIBRARY') {
          <fieldset class="grid gap-2">
            <legend class="mb-1 text-sm font-medium">{{ 'backups.form.libraries' | t }}</legend>
            @for (library of libraries(); track library.id) {
              <label class="flex items-center gap-2 text-sm">
                <hlm-checkbox
                  [checked]="isChosen(library.id)"
                  (checkedChange)="toggle({ id: library.id, name: library.name }, $event)"
                />
                {{ library.name }}
              </label>
            } @empty {
              <p class="text-sm text-muted-foreground">{{ 'backups.form.noLibraries' | t }}</p>
            }
          </fieldset>
        }
        @case ('ALBUM') {
          <div class="grid gap-2">
            <label hlmFieldLabel for="backup-album-search">{{ 'backups.form.albums' | t }}</label>
            @if (chosen().length > 0) {
              <div class="flex flex-wrap gap-1">
                @for (album of chosen(); track album.id) {
                  <span hlmBadge variant="secondary" class="gap-1">
                    {{ album.name }}
                    <button
                      type="button"
                      class="rounded-full hover:text-destructive"
                      [attr.aria-label]="'common.remove' | t"
                      (click)="toggle(album, false)"
                    >
                      <ng-icon name="lucideX" class="size-3" />
                    </button>
                  </span>
                }
              </div>
            }
            <input
              hlmInput
              id="backup-album-search"
              name="albumSearch"
              autocomplete="off"
              [placeholder]="'backups.form.albumSearch' | t"
              [ngModel]="albumQuery"
              (ngModelChange)="searchAlbums($event)"
            />
            @if (albumResults().length > 0) {
              <ul class="max-h-56 overflow-y-auto rounded-md border">
                @for (album of albumResults(); track album.id) {
                  <li class="flex items-center justify-between gap-2 px-3 py-1.5 text-sm">
                    <span class="min-w-0 truncate">{{ album.name }}</span>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      type="button"
                      [disabled]="isChosen(album.id)"
                      [attr.aria-label]="'common.add' | t"
                      (click)="toggle(album, true)"
                    >
                      <ng-icon name="lucidePlus" />
                    </button>
                  </li>
                }
              </ul>
            } @else if (albumQuery.trim() && !searching()) {
              <p class="text-xs text-muted-foreground">{{ 'backups.form.noAlbums' | t }}</p>
            }
          </div>
        }
        @case ('PLAYLIST') {
          <fieldset class="grid gap-2">
            <legend class="mb-1 text-sm font-medium">{{ 'backups.form.playlists' | t }}</legend>
            @for (playlist of playlists(); track playlist.id) {
              <label class="flex items-center gap-2 text-sm">
                <hlm-checkbox
                  [checked]="isChosen(playlist.id)"
                  (checkedChange)="toggle({ id: playlist.id, name: playlist.name }, $event)"
                />
                <span class="min-w-0 truncate">
                  {{ playlist.name }}
                  @if (playlist.detail) {
                    <span class="text-muted-foreground">· {{ playlist.detail }}</span>
                  }
                </span>
              </label>
            } @empty {
              <p class="text-sm text-muted-foreground">{{ 'backups.form.noPlaylists' | t }}</p>
            }
          </fieldset>
        }
      }
      @if (fieldError('scopeIds'); as message) {
        <p class="text-sm text-destructive">{{ message }}</p>
      }

      <label class="flex items-start gap-3 text-sm">
        <hlm-switch class="mt-0.5" name="includeUserData" [(ngModel)]="includeUserData" />
        <span class="grid gap-0.5">
          {{ 'backups.form.userData' | t }}
          <span class="text-xs text-muted-foreground">{{ 'backups.form.userDataHelp' | t }}</span>
        </span>
      </label>

      @if (scope === 'SERVER' || scope === 'LIBRARY') {
        <div class="grid gap-3">
          <label class="flex items-start gap-3 text-sm">
            <hlm-switch class="mt-0.5" name="includeServer" [(ngModel)]="includeServer" />
            <span class="grid gap-0.5">
              {{ 'backups.form.server' | t }}
              <span class="text-xs text-muted-foreground">{{ 'backups.form.serverHelp' | t }}</span>
            </span>
          </label>
          @if (includeServer) {
            <div class="grid gap-3 rounded-lg border p-3">
              <p
                class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300"
              >
                {{ 'backups.form.passwordWarning' | t }}
              </p>
              <div class="grid gap-3 sm:grid-cols-2">
                <hlm-field>
                  <label hlmFieldLabel for="backup-password">{{ 'common.password' | t }}</label>
                  <input
                    hlmInput
                    id="backup-password"
                    name="password"
                    type="password"
                    autocomplete="new-password"
                    [placeholder]="hasPassword() ? ('backups.form.passwordKeep' | t) : ''"
                    [(ngModel)]="password"
                  />
                </hlm-field>
                <hlm-field>
                  <label hlmFieldLabel for="backup-password-confirm">{{
                    'common.confirmPassword' | t
                  }}</label>
                  <input
                    hlmInput
                    id="backup-password-confirm"
                    name="passwordConfirmation"
                    type="password"
                    autocomplete="new-password"
                    [(ngModel)]="passwordConfirmation"
                  />
                </hlm-field>
              </div>
              <p class="text-xs text-muted-foreground">
                {{
                  (hasPassword() ? 'backups.form.passwordEditHelp' : 'backups.form.passwordHelp')
                    | t
                }}
              </p>
              @if (passwordProblem(); as problem) {
                <p class="text-sm text-destructive">{{ problem | t }}</p>
              }
              @if (fieldError('password'); as message) {
                <p class="text-sm text-destructive">{{ message }}</p>
              }
            </div>
          }
        </div>
      }

      <fieldset class="grid gap-3">
        <legend class="mb-1 text-sm font-medium">{{ 'backups.schedule.title' | t }}</legend>
        <div class="flex flex-wrap items-end gap-3">
          <select
            hlmNativeSelect
            name="schedule"
            class="w-48"
            [attr.aria-label]="'backups.schedule.title' | t"
            [(ngModel)]="schedule"
          >
            @for (option of schedules; track option) {
              <option hlmNativeSelectOption [value]="option">
                {{ 'backups.schedule.kinds.' + option | t }}
              </option>
            }
          </select>
          @if (schedule === 'HOURS') {
            <label class="flex items-center gap-2 text-sm">
              {{ 'backups.schedule.every' | t }}
              <input
                hlmInput
                name="intervalHours"
                type="number"
                min="1"
                [max]="maxInterval"
                class="w-24"
                [(ngModel)]="intervalHours"
              />
              {{ 'backups.schedule.hours' | t }}
            </label>
          }
          @if (schedule === 'WEEKLY') {
            <select
              hlmNativeSelect
              name="dayOfWeek"
              class="w-40"
              [attr.aria-label]="'backups.schedule.day' | t"
              [(ngModel)]="dayOfWeek"
            >
              @for (day of days; track day) {
                <option hlmNativeSelectOption [value]="day">
                  {{ 'backups.days.' + day | t }}
                </option>
              }
            </select>
          }
          @if (schedule === 'DAILY' || schedule === 'WEEKLY') {
            <label class="flex items-center gap-2 text-sm">
              {{ 'backups.schedule.at' | t }}
              <input hlmInput name="time" type="time" class="w-32" [(ngModel)]="time" />
            </label>
          }
        </div>
        @if (schedule !== 'MANUAL') {
          <hlm-field>
            <label hlmFieldLabel for="backup-zone">{{ 'backups.schedule.timeZone' | t }}</label>
            <input
              hlmInput
              id="backup-zone"
              name="timeZone"
              class="font-mono text-xs"
              [(ngModel)]="timeZone"
            />
            <p hlmFieldDescription>{{ 'backups.schedule.timeZoneHelp' | t }}</p>
            @if (fieldError('timeZone'); as message) {
              <hlm-field-error>{{ message }}</hlm-field-error>
            }
          </hlm-field>
        }
        @if (scheduleProblem(); as problem) {
          <p class="text-sm text-destructive">{{ problem | t }}</p>
        }
        @for (field of ['intervalHours', 'timeOfDay', 'dayOfWeek']; track field) {
          @if (fieldError(field); as message) {
            <p class="text-sm text-destructive">{{ message }}</p>
          }
        }
      </fieldset>

      <hlm-field>
        <label hlmFieldLabel for="backup-retention">{{ 'backups.form.retention' | t }}</label>
        <div class="flex items-center gap-2 text-sm">
          <input
            hlmInput
            id="backup-retention"
            name="retentionDays"
            type="number"
            min="0"
            max="36500"
            class="w-28"
            [(ngModel)]="retentionDays"
          />
          {{ 'backups.form.days' | t }}
        </div>
        <p hlmFieldDescription>
          {{
            Number(retentionDays) > 0
              ? ('backups.form.retentionHelp' | t: { days: Number(retentionDays) })
              : ('backups.form.retentionForever' | t)
          }}
        </p>
        @if (fieldError('retentionDays'); as message) {
          <hlm-field-error>{{ message }}</hlm-field-error>
        }
      </hlm-field>

      <label class="flex items-start gap-3 text-sm">
        <hlm-switch class="mt-0.5" name="enabled" [(ngModel)]="enabled" />
        <span class="grid gap-0.5">
          {{ 'backups.form.enabled' | t }}
          <span class="text-xs text-muted-foreground">{{ 'backups.form.enabledHelp' | t }}</span>
        </span>
      </label>

      @if (error(); as message) {
        <div hlmAlert variant="destructive">
          <p hlmAlertDescription>{{ message }}</p>
        </div>
      }

      <hlm-dialog-footer>
        <button hlmBtn variant="ghost" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | t }}
        </button>
        <button hlmBtn type="submit" [disabled]="busy() || !ready()">
          @if (busy()) {
            <hlm-spinner class="size-4" />
          }
          {{ (backup() ? 'common.save' : 'backups.form.createAction') | t }}
        </button>
      </hlm-dialog-footer>
    </form>
  `,
})
export class BackupForm implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  /** The backup to change; null to create one. */
  readonly backup = input<Backup | null>(null);
  readonly destinations = input.required<Destination[]>();
  readonly options = input.required<BackupOptions>();
  readonly saved = output<Backup>();
  readonly cancelled = output<void>();

  protected readonly Number = Number;
  protected readonly scopes: BackupScope[] = ['SERVER', 'LIBRARY', 'ALBUM', 'PLAYLIST'];
  protected readonly schedules: BackupSchedule[] = ['MANUAL', 'HOURS', 'DAILY', 'WEEKLY'];
  protected readonly days = DAYS_OF_WEEK;
  protected readonly maxInterval = MAX_INTERVAL_HOURS;

  protected name = '';
  protected destinationId = '';
  protected scope: BackupScope = 'SERVER';
  protected readonly chosen = signal<Item[]>([]);
  protected includeUserData = true;
  protected includeServer = true;
  protected password = '';
  protected passwordConfirmation = '';
  protected schedule: BackupSchedule = 'DAILY';
  protected intervalHours: number | null = 24;
  protected time = '03:00';
  protected dayOfWeek: DayOfWeek = 'SUNDAY';
  protected timeZone = browserTimeZone();
  protected retentionDays: number | null = 30;
  protected enabled = true;

  protected albumQuery = '';
  protected readonly albumResults = signal<Item[]>([]);
  protected readonly searching = signal(false);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly fieldErrors = signal<Record<string, string[]>>({});

  /** Libraries that can be backed up: on this server's disks or in S3, not anyone's personal one. */
  protected readonly libraries = computed(() =>
    this.options().libraries.filter(
      (library) =>
        !library.personal &&
        (library.sources ?? []).some((s) => s.kind === 'LOCAL' || s.kind === 'S3'),
    ),
  );

  /** The playlists this administrator sees, plus those the backup has that they do not. */
  protected readonly playlists = computed(() => {
    const listed = this.options().playlists.map((p) => ({
      id: p.id,
      name: p.name,
      detail: `${p.owner.name} · ${this.i18n.t('backups.form.tracks', { count: p.trackCount })}`,
    }));
    const extra = this.chosen()
      .filter((item) => !listed.some((p) => p.id === item.id))
      .map((item) => ({ ...item, detail: '' }));
    return [...listed, ...extra];
  });

  /** The backup already keeps a password for the server's part: leaving it empty keeps it. */
  protected readonly hasPassword = computed(() => !!this.backup()?.includeServer);

  ngOnInit(): void {
    const backup = this.backup();
    if (!backup) {
      this.destinationId = this.destinations()[0]?.id ?? '';
      return;
    }
    this.name = backup.name;
    this.destinationId = backup.destination.id;
    this.scope = backup.scope;
    this.chosen.set(backup.items.map((item) => ({ id: item.id, name: item.name })));
    this.includeUserData = backup.includeUserData;
    this.includeServer = backup.includeServer;
    this.schedule = backup.schedule;
    this.intervalHours = backup.intervalHours ?? 24;
    this.time = minutesToTime(backup.timeOfDay ?? 180);
    this.dayOfWeek = backup.dayOfWeek ?? 'SUNDAY';
    this.timeZone = backup.timeZone || browserTimeZone();
    this.retentionDays = backup.retentionDays;
    this.enabled = backup.enabled;
  }

  protected setScope(scope: BackupScope): void {
    this.scope = scope;
    this.chosen.set([]);
    this.albumQuery = '';
    this.albumResults.set([]);
    // Only the whole server and its libraries carry the database.
    if (scope === 'ALBUM' || scope === 'PLAYLIST') this.includeServer = false;
  }

  protected isChosen(id: string): boolean {
    return this.chosen().some((item) => item.id === id);
  }

  protected toggle(item: Item, on: boolean): void {
    this.chosen.update((list) =>
      on
        ? list.some((i) => i.id === item.id)
          ? list
          : [...list, item]
        : list.filter((i) => i.id !== item.id),
    );
  }

  protected searchAlbums(query: string): void {
    this.albumQuery = query;
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (!query.trim()) {
      this.albumResults.set([]);
      return;
    }
    this.searching.set(true);
    this.searchTimer = setTimeout(async () => {
      try {
        const result = await this.gql.query(BackupAlbumSearchQuery, { search: query.trim() });
        if (query !== this.albumQuery) return;
        this.albumResults.set(
          result.albums.items.map((album) => ({
            id: album.id,
            name:
              [album.artist.name, album.title].join(' — ') + (album.year ? ` (${album.year})` : ''),
          })),
        );
      } catch {
        this.albumResults.set([]);
      } finally {
        this.searching.set(false);
      }
    }, 300);
  }

  /** What is wrong with the password, as an i18n key; null when nothing is. */
  protected passwordProblem(): string | null {
    if (!this.includeServer || !this.carriesServer()) return null;
    if (!this.password && this.hasPassword()) return null;
    if (!this.password) return null;
    if (this.password.length < MIN_PASSWORD) return 'backups.form.passwordShort';
    if (this.password !== this.passwordConfirmation) return 'backups.form.passwordMismatch';
    return null;
  }

  protected scheduleProblem(): string | null {
    if (this.schedule === 'HOURS') {
      const hours = Number(this.intervalHours);
      if (!Number.isInteger(hours) || hours < 1 || hours > MAX_INTERVAL_HOURS)
        return 'backups.schedule.intervalInvalid';
    }
    if (
      (this.schedule === 'DAILY' || this.schedule === 'WEEKLY') &&
      timeToMinutes(this.time) === null
    )
      return 'backups.schedule.timeInvalid';
    return null;
  }

  protected ready(): boolean {
    if (!this.name.trim() || !this.destinationId) return false;
    if (this.scope !== 'SERVER' && this.chosen().length === 0) return false;
    if (this.includeServer && this.carriesServer()) {
      if (!this.password && !this.hasPassword()) return false;
      if (this.passwordProblem()) return false;
    }
    const retention = Number(this.retentionDays);
    if (!Number.isInteger(retention) || retention < 0 || retention > 36500) return false;
    return !this.scheduleProblem();
  }

  protected fieldError(field: string): string | null {
    const code = this.fieldErrors()[field]?.[0];
    return code ? this.i18n.error(code) : null;
  }

  private carriesServer(): boolean {
    return this.scope === 'SERVER' || this.scope === 'LIBRARY';
  }

  protected async save(): Promise<void> {
    if (!this.ready()) return;
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    const includeServer = this.includeServer && this.carriesServer();
    const fields = {
      name: this.name.trim(),
      scopeIds: this.scope === 'SERVER' ? [] : this.chosen().map((item) => item.id),
      includeUserData: this.includeUserData,
      includeServer,
      password: includeServer && this.password ? this.password : null,
      schedule: this.schedule,
      intervalHours: this.schedule === 'HOURS' ? Number(this.intervalHours) : null,
      timeOfDay:
        this.schedule === 'DAILY' || this.schedule === 'WEEKLY' ? timeToMinutes(this.time) : null,
      dayOfWeek: this.schedule === 'WEEKLY' ? this.dayOfWeek : null,
      timeZone: this.timeZone.trim() || browserTimeZone(),
      retentionDays: Number(this.retentionDays),
      enabled: this.enabled,
    };
    try {
      const backup = this.backup();
      const result = backup
        ? (await this.gql.mutate(UpdateBackupMutation, { id: backup.id, input: fields }))
            .updateBackup
        : (
            await this.gql.mutate(CreateBackupMutation, {
              input: { ...fields, destinationId: this.destinationId, scope: this.scope },
            })
          ).createBackup;
      toast.success(this.i18n.t(backup ? 'backups.form.saved' : 'backups.form.created'));
      this.saved.emit(result);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      this.error.set(
        Object.keys(apiError.fields).length > 0
          ? this.i18n.error('validation_failed')
          : this.i18n.error(apiError.code),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
