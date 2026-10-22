import { Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError, SetupRestoreSource, SetupStoredBackup } from '../../core/api/rest';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { HostHelp } from '../../shared/host-help';
import { SetupService } from '../../core/setup/setup.service';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { BytesPipe, DateTimePipe } from '../../shared/format';
import {
  S3ConnectionDraft,
  S3ConnectionFields,
  emptyS3Connection,
  s3ConnectionInput,
  s3ConnectionReady,
} from '../../shared/s3-connection';

type Snapshot = SetupStoredBackup['snapshots'][number];

/**
 * Restoring a whole server instead of setting it up: where its backup is, which snapshot, its password and whether
 * its music comes back too. Place it inside a dialog.
 */
@Component({
  selector: 'app-setup-restore',
  imports: [
    FormsModule,
    HlmAlertImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    TranslatePipe,
    HostHelp,
    BytesPipe,
    DateTimePipe,
    FolderPicker,
    S3ConnectionFields,
  ],
  host: { class: 'grid w-full min-w-0 grid-cols-[minmax(0,1fr)] gap-5' },
  template: `
    @if (picking()) {
      <hlm-dialog-header>
        <h3 hlmDialogTitle>{{ 'folderPicker.title' | t }}</h3>
      </hlm-dialog-header>
      <app-folder-picker
        [load]="loadDirectories()"
        [create]="createDirectory()"
        [start]="path || null"
        (picked)="pickFolder($event)"
        (cancelled)="picking.set(false)"
      />
    } @else {
      <hlm-dialog-header>
        <h3 hlmDialogTitle>{{ 'setup.restore.title' | t }}</h3>
        <p hlmDialogDescription>{{ 'setup.restore.description' | t }}</p>
      </hlm-dialog-header>

      <hlm-radio-group
        class="grid gap-2 sm:grid-cols-2"
        name="kind"
        [ngModel]="kind"
        (ngModelChange)="setKind($event)"
      >
        <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
          <hlm-radio value="FOLDER" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
          <span class="font-medium">{{ 'backups.destinations.kinds.FOLDER' | t }}</span>
        </label>
        <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
          <hlm-radio value="S3" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
          <span class="font-medium">{{ 'backups.destinations.kinds.S3' | t }}</span>
        </label>
      </hlm-radio-group>

      @if (kind === 'FOLDER') {
        <hlm-field>
          <label hlmFieldLabel for="restore-path">{{ 'setup.restore.folder' | t }}</label>
          <div class="flex gap-2">
            <input
              hlmInput
              id="restore-path"
              name="path"
              class="min-w-0 flex-1 font-mono text-xs"
              [ngModel]="path"
              (ngModelChange)="path = $event; reset()"
            />
            <button hlmBtn variant="outline" type="button" (click)="picking.set(true)">
              {{ 'backups.destinations.choose' | t }}
            </button>
          </div>
          <p hlmFieldDescription>{{ 'setup.restore.folderHelp' | t }}</p>
        </hlm-field>
      } @else {
        <app-s3-connection fieldId="restore-s3" [value]="s3" (valueChange)="s3 = $event; reset()" />
      }

      <div>
        <button
          hlmBtn
          variant="outline"
          type="button"
          [disabled]="busy() || !sourceReady()"
          (click)="browse()"
        >
          @if (busy() && backups() === null) {
            <hlm-spinner class="size-4" />
          }
          {{ 'setup.restore.search' | t }}
        </button>
      </div>

      @if (backups(); as list) {
        @if (list.length === 0) {
          <p class="text-sm text-muted-foreground">{{ 'setup.restore.none' | t }}</p>
        } @else {
          <div class="grid max-h-72 gap-3 overflow-y-auto">
            @for (backup of list; track backup.folder) {
              <section class="grid gap-2 rounded-lg border p-3">
                <p class="text-sm font-medium">{{ backup.name }}</p>
                <p class="text-xs text-muted-foreground">
                  {{
                    'backups.restore.madeBy'
                      | t: { machine: backup.machine, version: backup.arcanistVersion }
                  }}
                </p>
                <ul class="grid gap-1">
                  @for (snapshot of serverSnapshots(backup); track snapshot.name) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-left text-sm hover:bg-accent"
                        [class.border-primary]="isChosen(backup, snapshot)"
                        [class.bg-accent]="isChosen(backup, snapshot)"
                        (click)="choose(backup, snapshot)"
                      >
                        <span class="font-medium">{{ snapshot.createdAt | dateTime }}</span>
                        <span class="text-xs text-muted-foreground">
                          {{ 'backups.restore.files' | t: { count: snapshot.files } }} ·
                          {{ snapshot.bytes | bytes }}
                        </span>
                      </button>
                    </li>
                  }
                </ul>
              </section>
            }
          </div>
        }
      }

      @if (snapshot()) {
        <hlm-field>
          <label hlmFieldLabel for="restore-password">{{ 'setup.restore.password' | t }}</label>
          <div class="flex gap-2">
            <input
              hlmInput
              id="restore-password"
              name="password"
              type="password"
              autocomplete="off"
              class="min-w-0 flex-1"
              [ngModel]="password"
              (ngModelChange)="password = $event; checked.set(null)"
            />
            <button
              hlmBtn
              variant="outline"
              type="button"
              [disabled]="busy() || !password"
              (click)="check()"
            >
              {{ 'setup.restore.check' | t }}
            </button>
          </div>
          <p hlmFieldDescription>{{ 'setup.restore.passwordHelp' | t }}</p>
          @if (checked() === true) {
            <p class="text-sm text-green-600 dark:text-green-400">
              {{ 'setup.restore.passwordOk' | t }}
            </p>
          } @else if (checked() === false) {
            <p class="text-sm text-destructive">{{ 'setup.restore.passwordWrong' | t }}</p>
          }
        </hlm-field>

        <label class="flex items-start gap-3 text-sm">
          <hlm-checkbox
            class="mt-0.5"
            [checked]="restoreFiles()"
            (checkedChange)="restoreFiles.set($event)"
          />
          <span class="grid gap-0.5">
            {{ 'setup.restore.files' | t }}
            <span class="text-xs text-muted-foreground">{{ 'setup.restore.filesHelp' | t }}</span>
          </span>
        </label>

        <div hlmAlert>
          <p hlmAlertDescription>{{ 'setup.restore.supervisor' | t }}</p>
          <app-host-help class="mt-1" topic="restart" />
        </div>
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
        <button
          hlmBtn
          type="button"
          [disabled]="busy() || !snapshot() || !password || checked() === false"
          (click)="start()"
        >
          @if (busy() && snapshot()) {
            <hlm-spinner class="size-4" />
          }
          {{ 'setup.restore.start' | t }}
        </button>
      </hlm-dialog-footer>
    }
  `,
})
export class SetupRestoreDialog {
  private readonly i18n = inject(I18n);
  private readonly setup = inject(SetupService);

  readonly loadDirectories = input.required<DirectoryLoader>();
  readonly createDirectory = input<DirectoryCreator | null>(null);
  readonly started = output<void>();
  readonly cancelled = output<void>();

  protected kind: 'FOLDER' | 'S3' = 'FOLDER';
  protected path = '';
  protected s3: S3ConnectionDraft = emptyS3Connection();
  protected password = '';

  protected readonly picking = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly backups = signal<SetupStoredBackup[] | null>(null);
  protected readonly backup = signal<SetupStoredBackup | null>(null);
  protected readonly snapshot = signal<Snapshot | null>(null);
  /** Whether the password opened the snapshot; null until checked. */
  protected readonly checked = signal<boolean | null>(null);
  protected readonly restoreFiles = signal(true);

  protected setKind(kind: 'FOLDER' | 'S3'): void {
    this.kind = kind;
    this.reset();
  }

  protected pickFolder(path: string): void {
    this.path = path;
    this.picking.set(false);
    this.reset();
  }

  /** The source changed: what was found there no longer counts. */
  protected reset(): void {
    this.backups.set(null);
    this.backup.set(null);
    this.snapshot.set(null);
    this.checked.set(null);
    this.error.set(null);
  }

  protected sourceReady(): boolean {
    return this.kind === 'FOLDER' ? !!this.path.trim() : s3ConnectionReady(this.s3);
  }

  private source(): SetupRestoreSource {
    return this.kind === 'FOLDER'
      ? { kind: 'FOLDER', path: this.path.trim(), s3: null }
      : { kind: 'S3', path: null, s3: s3ConnectionInput(this.s3) };
  }

  protected serverSnapshots(backup: SetupStoredBackup): Snapshot[] {
    // Newest first: the latest is usually the one to restore.
    return backup.snapshots.filter((s) => s.server).reverse();
  }

  protected isChosen(backup: SetupStoredBackup, snapshot: Snapshot): boolean {
    return this.backup()?.folder === backup.folder && this.snapshot()?.name === snapshot.name;
  }

  protected choose(backup: SetupStoredBackup, snapshot: Snapshot): void {
    this.backup.set(backup);
    this.snapshot.set(snapshot);
    this.checked.set(null);
  }

  protected async browse(): Promise<void> {
    await this.run(async () => {
      const found = await this.setup.browseRestore(this.source());
      this.backups.set(found.filter((b) => b.snapshots.some((s) => s.server)));
      this.backup.set(null);
      this.snapshot.set(null);
    });
  }

  protected async check(): Promise<boolean> {
    const backup = this.backup();
    const snapshot = this.snapshot();
    if (!backup || !snapshot) return false;
    let success = false;
    await this.run(async () => {
      success = (
        await this.setup.checkRestore(this.source(), backup.folder, snapshot.name, this.password)
      ).success;
      this.checked.set(success);
    });
    return success;
  }

  protected async start(): Promise<void> {
    const backup = this.backup();
    const snapshot = this.snapshot();
    if (!backup || !snapshot || !this.password) return;
    if (this.checked() !== true && !(await this.check())) return;
    await this.run(async () => {
      await this.setup.startRestore({
        source: this.source(),
        folder: backup.folder,
        snapshot: snapshot.name,
        password: this.password,
        restoreFiles: this.restoreFiles(),
      });
      this.started.emit();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}

/** How often the server is asked whether it came back. */
const POLL_MS = 2000;
/** After this long without coming back, the server probably needs to be started by hand. */
const PATIENCE_MS = 2 * 60 * 1000;

/**
 * The server installs the backup and restarts: this waits until it answers again, set up, and goes to the login
 * page, where the backup's accounts work again.
 */
@Component({
  selector: 'app-setup-restoring',
  imports: [HlmAlertImports, HlmCardImports, HlmSpinnerImports, TranslatePipe],
  template: `
    <section hlmCard class="w-full max-w-lg">
      <div hlmCardHeader>
        <h1 hlmCardTitle class="text-xl">{{ 'setup.restore.restoring.title' | t }}</h1>
        <p hlmCardDescription>{{ 'setup.restore.restoring.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid gap-4">
        <div class="flex items-center gap-3 text-sm">
          <hlm-spinner class="size-5" />
          {{
            (serverUp()
              ? 'setup.restore.restoring.installing'
              : 'setup.restore.restoring.restarting'
            ) | t
          }}
        </div>
        @if (late()) {
          <div hlmAlert variant="destructive">
            <p hlmAlertTitle>{{ 'setup.restore.restoring.lateTitle' | t }}</p>
            <p hlmAlertDescription>{{ 'setup.restore.restoring.late' | t }}</p>
          </div>
        }
      </div>
    </section>
  `,
})
export class SetupRestoring implements OnInit {
  private readonly setup = inject(SetupService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly serverUp = signal(true);
  protected readonly late = signal(false);
  private done = false;

  ngOnInit(): void {
    const startedAt = Date.now();
    const timer = setInterval(() => void this.poll(startedAt), POLL_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  private async poll(startedAt: number): Promise<void> {
    if (this.done) return;
    this.late.set(Date.now() - startedAt > PATIENCE_MS);
    const up = await this.setup.healthy();
    this.serverUp.set(up);
    if (!up) return;
    try {
      const status = await this.setup.loadStatus();
      if (!status.completed || this.done) return;
      this.done = true;
      await this.router.navigate(['/login'], { queryParams: { restored: 1 } });
    } catch {
      // Still restarting: asked again on the next tick.
    }
  }
}
