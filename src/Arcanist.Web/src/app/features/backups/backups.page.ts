import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideHistory,
  lucidePencil,
  lucidePlay,
  lucidePlus,
  lucideTrash2,
} from '@ng-icons/lucide';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  BackupDestinationFieldsFragment as Destination,
  BackupFieldsFragment as Backup,
  BackupGuestFieldsFragment as Guest,
  BackupJobFieldsFragment as BackupJob,
  RestoreJobFieldsFragment as RestoreJob,
} from '../../graphql/generated/graphql';
import {
  BackupJobUpdatedSubscription,
  BackupOptionsQuery,
  BackupsPageQuery,
  CancelBackupRunMutation,
  CancelRestoreMutation,
  DeleteBackupDestinationMutation,
  DeleteBackupMutation,
  RestoreJobUpdatedSubscription,
  RunBackupMutation,
  TestBackupDestinationMutation,
} from '../../graphql/operations';
import { BytesPipe, CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { BackupForm, BackupOptions } from './backup-form';
import {
  backupRunActive,
  backupRunVariant,
  restoreActive,
  restorePercent,
  restoreVariant,
} from './backup-jobs';
import { BackupRuns } from './backup-runs';
import { DestinationForm } from './destination-form';
import { GuestInvitation } from './guest-invitation';
import { RestoreDialog } from './restore-dialog';
import { scheduleSummary } from './schedule';

/** Backups: what is copied where and when, restoring from them, and the servers that keep theirs here. */
@Component({
  selector: 'app-backups-page',
  imports: [
    RouterLink,
    NgIcon,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmEmptyImports,
    HlmProgressImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    DateTimePipe,
    RelativeTimePipe,
    BackupForm,
    BackupRuns,
    DestinationForm,
    GuestInvitation,
    RestoreDialog,
  ],
  providers: [provideIcons({ lucideHistory, lucidePencil, lucidePlay, lucidePlus, lucideTrash2 })],
  templateUrl: './backups.page.html',
})
export class BackupsPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly destroyRef = inject(DestroyRef);
  private readonly extensions = inject(EnabledExtensions);

  protected readonly loaded = signal(false);
  protected readonly destinations = signal<Destination[]>([]);
  protected readonly backups = signal<Backup[]>([]);
  protected readonly restores = signal<RestoreJob[]>([]);
  protected readonly guests = signal<Guest[]>([]);
  protected readonly s3Available = signal(false);
  /** Buckets are offered while the Ultimate Cloud extension is on, as for libraries in S3. */
  protected readonly s3Offered = computed(() => this.extensions.has('ultimate-cloud'));
  /** The latest run of each backup, updated live. */
  protected readonly runs = signal<Record<string, BackupJob>>({});
  protected readonly busy = signal<string | null>(null);

  /** What backups can cover and who restored data can go to; loaded when a dialog needs it. */
  protected readonly options = signal<BackupOptions | null>(null);

  // Dialogs
  protected readonly editing = signal<{ backup: Backup | null } | null>(null);
  protected readonly history = signal<Backup | null>(null);
  protected readonly deleting = signal<Backup | null>(null);
  protected readonly addingDestination = signal(false);
  protected readonly deletingDestination = signal<Destination | null>(null);
  protected readonly restoring = signal(false);
  protected readonly inviting = signal(false);

  protected readonly runVariant = backupRunVariant;
  protected readonly runActive = backupRunActive;
  protected readonly restoreVariant = restoreVariant;
  protected readonly restoreActive = restoreActive;
  protected readonly restorePercent = restorePercent;

  async ngOnInit(): Promise<void> {
    await this.load();
    this.gql
      .subscribe(BackupJobUpdatedSubscription)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ backupJobUpdated: job }) => {
        const before = this.runs()[job.backupId];
        this.runs.update((runs) => ({ ...runs, [job.backupId]: job }));
        // A run that ends moves the next one, and may have failed.
        if (backupRunActive(before?.status) && !backupRunActive(job.status)) {
          if (job.status === 'COMPLETED')
            toast.success(this.i18n.t('backups.finishedToast', { name: job.backupName }));
          else if (job.status !== 'CANCELLED')
            toast.error(this.i18n.t('backups.failedToast', { name: job.backupName }));
          void this.load();
        }
      });
    this.gql
      .subscribe(RestoreJobUpdatedSubscription)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ restoreJobUpdated: job }) => {
        this.restores.update((list) =>
          list.some((r) => r.id === job.id)
            ? list.map((r) => (r.id === job.id ? job : r))
            : [job, ...list],
        );
      });
  }

  private async load(): Promise<void> {
    try {
      const result = await this.gql.query(BackupsPageQuery);
      this.destinations.set(result.backupDestinations);
      this.backups.set(result.backups);
      this.restores.set(result.restoreJobs);
      this.guests.set(result.backupGuests);
      this.s3Available.set(result.serverInfo.capabilities.includes('THIRD_PARTY_STORAGE'));
      const runs: Record<string, BackupJob> = {};
      for (const backup of result.backups) if (backup.lastRun) runs[backup.id] = backup.lastRun;
      this.runs.set(runs);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.loaded.set(true);
    }
  }

  private async loadOptions(): Promise<BackupOptions | null> {
    try {
      const options = (await this.gql.query(BackupOptionsQuery)) as BackupOptions;
      this.options.set(options);
      return options;
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      return null;
    }
  }

  // Backups

  protected schedule(backup: Backup): string {
    return scheduleSummary(this.i18n, backup);
  }

  protected items(backup: Backup): string {
    return backup.items.map((item) => item.name).join(', ');
  }

  protected runOf(backup: Backup): BackupJob | null {
    return this.runs()[backup.id] ?? backup.lastRun ?? null;
  }

  protected runPercent(job: BackupJob): number | null {
    if (job.files <= 0) return null;
    const done = job.copied + job.unchanged + job.skipped + job.failed;
    return Math.min(100, Math.round((done * 100) / job.files));
  }

  protected async openCreate(): Promise<void> {
    if (this.destinations().length === 0) return;
    if (await this.loadOptions()) this.editing.set({ backup: null });
  }

  protected async openEdit(backup: Backup): Promise<void> {
    if (await this.loadOptions()) this.editing.set({ backup });
  }

  protected async saved(): Promise<void> {
    this.editing.set(null);
    await this.load();
  }

  protected async run(backup: Backup): Promise<void> {
    await this.act(backup.id, async () => {
      const job = (await this.gql.mutate(RunBackupMutation, { id: backup.id })).runBackup;
      this.runs.update((runs) => ({ ...runs, [backup.id]: job }));
      toast.success(this.i18n.t('backups.runQueued', { name: backup.name }));
    });
  }

  protected async cancelRun(job: BackupJob): Promise<void> {
    await this.act(job.id, async () => {
      const updated = (await this.gql.mutate(CancelBackupRunMutation, { id: job.id }))
        .cancelBackupRun;
      this.runs.update((runs) => ({ ...runs, [updated.backupId]: updated }));
    });
  }

  protected async confirmDelete(): Promise<void> {
    const backup = this.deleting();
    this.deleting.set(null);
    if (!backup) return;
    await this.act(backup.id, async () => {
      await this.gql.mutate(DeleteBackupMutation, { id: backup.id });
      toast.success(this.i18n.t('backups.deleted'));
      await this.load();
    });
  }

  // Destinations

  protected async destinationCreated(): Promise<void> {
    this.addingDestination.set(false);
    await this.load();
  }

  protected async test(destination: Destination): Promise<void> {
    await this.act(destination.id, async () => {
      const result = (await this.gql.mutate(TestBackupDestinationMutation, { id: destination.id }))
        .testBackupDestination;
      if (result.success)
        toast.success(this.i18n.t('backups.destinations.testOk', { name: destination.name }));
      else toast.error(this.i18n.error(result.errorCode));
    });
  }

  protected async confirmDeleteDestination(): Promise<void> {
    const destination = this.deletingDestination();
    this.deletingDestination.set(null);
    if (!destination) return;
    await this.act(destination.id, async () => {
      await this.gql.mutate(DeleteBackupDestinationMutation, { id: destination.id });
      toast.success(this.i18n.t('backups.destinations.deleted'));
      await this.load();
    });
  }

  // Restores

  protected async openRestore(): Promise<void> {
    if (await this.loadOptions()) this.restoring.set(true);
  }

  protected restoreStarted(job: RestoreJob): void {
    this.restoring.set(false);
    this.restores.update((list) => [job, ...list.filter((r) => r.id !== job.id)]);
    toast.success(this.i18n.t('backups.restore.startedToast'));
  }

  protected async cancelRestore(job: RestoreJob): Promise<void> {
    await this.act(job.id, async () => {
      const updated = (await this.gql.mutate(CancelRestoreMutation, { id: job.id })).cancelRestore;
      this.restores.update((list) => list.map((r) => (r.id === updated.id ? updated : r)));
    });
  }

  // Guests

  protected async invitationClosed(): Promise<void> {
    this.inviting.set(false);
    await this.load();
  }

  private async act(id: string, action: () => Promise<void>): Promise<void> {
    this.busy.set(id);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(null);
    }
  }
}
