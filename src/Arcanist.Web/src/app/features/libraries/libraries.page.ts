import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFolderPlus,
  lucideFolderTree,
  lucidePencil,
  lucidePlus,
  lucideRefreshCw,
  lucideTrash2,
  lucideX,
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
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  CloudSyncStateFieldsFragment as CloudSyncState,
  LibrariesQuery as LibrariesResult,
  ScanJobFieldsFragment,
} from '../../graphql/generated/graphql';
import {
  AddLibrarySourceMutation,
  CancelScanMutation,
  CloudSyncsQuery,
  CreateLibraryMutation,
  DeleteLibraryMutation,
  LibrariesQuery,
  RemoveLibrarySourceMutation,
  ScanJobUpdatedSubscription,
  ScanJobsQuery,
  CreateServerDirectoryMutation,
  ServerDirectoriesQuery,
  StartScanMutation,
  UpdateLibraryMutation,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { OrganizeDialog } from './organize-dialog';
import { CloudSyncPanel } from './cloud-sync';
import { S3LibraryForm } from './s3-library';
import { CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';

type Library = LibrariesResult['libraries'][number];
type ScanJob = ScanJobFieldsFragment;

type IntervalUnit = 'MINUTES' | 'HOURS' | 'DAYS';

const UnitMinutes: Record<IntervalUnit, number> = { MINUTES: 1, HOURS: 60, DAYS: 60 * 24 };

interface LibraryForm {
  id: string | null;
  name: string;
  visibleToAll: boolean;
  /** The interval in `intervalUnit`s; null to scan only by hand. */
  interval: number | null;
  intervalUnit: IntervalUnit;
  watchForChanges: boolean;
  /** How changes are noticed: a folder is watched, Ultimate Cloud tells; null when they cannot be. */
  watchKind: 'FOLDER' | 'CLOUD' | null;
  folders: string[];
}

/** The largest unit that expresses `minutes` exactly. */
function splitInterval(minutes: number | null | undefined): {
  interval: number | null;
  intervalUnit: IntervalUnit;
} {
  if (!minutes) return { interval: null, intervalUnit: 'HOURS' };
  if (minutes % UnitMinutes.DAYS === 0)
    return { interval: minutes / UnitMinutes.DAYS, intervalUnit: 'DAYS' };
  if (minutes % UnitMinutes.HOURS === 0)
    return { interval: minutes / UnitMinutes.HOURS, intervalUnit: 'HOURS' };
  return { interval: minutes, intervalUnit: 'MINUTES' };
}

@Component({
  selector: 'app-libraries-page',
  imports: [
    FormsModule,
    NgIcon,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSwitchImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSpinnerImports,
    HlmEmptyImports,
    TranslatePipe,
    FolderPicker,
    OrganizeDialog,
    CloudSyncPanel,
    S3LibraryForm,
    DateTimePipe,
    RelativeTimePipe,
    CountPipe,
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucidePencil,
      lucideTrash2,
      lucideFolderPlus,
      lucideFolderTree,
      lucideRefreshCw,
      lucideX,
    }),
  ],
  templateUrl: './libraries.page.html',
})
export class LibrariesPage implements OnInit {
  /** A library another server shares through the P2P network, or kept in Ultimate Cloud: read-only here. */
  protected isShared(library: { sources?: { kind: string }[] | null }): boolean {
    return (library.sources ?? []).some((s) => s.kind === 'PEER' || s.kind === 'ULTIMATE_CLOUD');
  }

  /**
   * A folder of this server can be added: a local library has one, so only one whose folder was removed takes
   * another (libraries in a bucket, shared or in Ultimate Cloud never have none).
   */
  protected takesFolders(library: { sources?: { kind: string }[] | null }): boolean {
    return (library.sources ?? []).length === 0;
  }

  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly libraries = signal<Library[] | null>(null);
  /** How each library syncs with Ultimate Cloud (administrators only). */
  protected readonly cloudSyncs = signal<Record<string, CloudSyncState>>({});
  /** The linked account has the Cloud plan. */
  protected readonly cloudSyncAvailable = signal(false);
  /** The linked account has the Cloud plan's storage on other clouds (S3). */
  protected readonly s3Available = signal(false);
  protected readonly s3Open = signal(false);

  protected async s3Created(): Promise<void> {
    this.s3Open.set(false);
    await this.load();
  }
  /** Latest scan of each library, updated live. */
  protected readonly scans = signal<Record<string, ScanJob>>({});
  protected readonly busy = signal(false);

  protected readonly canManage = computed(() => this.auth.can('MANAGE_LIBRARIES'));
  protected readonly canScan = computed(() => this.auth.can('RUN_SCANS'));
  protected readonly canFollowScans = computed(() => this.canScan() || this.canManage());

  // Create / edit dialog
  protected readonly form = signal<LibraryForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected newFolder = '';

  // Folder picking: for the open form, or to add a source to an existing library.
  protected readonly picking = signal<{ libraryId: string | null } | null>(null);
  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  protected readonly deleting = signal<Library | null>(null);
  protected readonly organizing = signal<Library | null>(null);

  protected async closeOrganize(): Promise<void> {
    this.organizing.set(null);
    // Its template or automatic organizing may have changed.
    await this.load();
  }

  async ngOnInit(): Promise<void> {
    await this.load();
    if (this.auth.isAdmin()) {
      await this.loadCloudSyncs();
      // Syncs run in the background: follow the ones going on.
      const timer = setInterval(() => {
        if (Object.values(this.cloudSyncs()).some((s) => s.running || s.requested))
          void this.loadCloudSyncs();
      }, 2000);
      this.destroyRef.onDestroy(() => clearInterval(timer));
    }

    if (this.canFollowScans()) {
      const jobs = await this.gql.query(ScanJobsQuery, { limit: 100 });
      // Most recent first: keep the first job seen per library.
      const latest: Record<string, ScanJob> = {};
      for (const job of jobs.scanJobs) latest[job.libraryId] ??= job;
      this.scans.set(latest);

      this.gql
        .subscribe(ScanJobUpdatedSubscription)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(({ scanJobUpdated: job }) => {
          this.scans.update((scans) => ({ ...scans, [job.libraryId]: job }));
          if (job.status === 'COMPLETED') void this.load();
        });
    }
  }

  protected isRunning(job: ScanJob | undefined): boolean {
    return job?.status === 'QUEUED' || job?.status === 'RUNNING';
  }

  protected progressPercent(job: ScanJob): number | null {
    const progress = job.progress;
    if (!progress || progress.phase !== 'READING' || progress.filesToRead === 0) return null;
    return Math.round((progress.filesRead / progress.filesToRead) * 100);
  }

  protected async scan(library: Library, full: boolean): Promise<void> {
    await this.run(async () => {
      const result = await this.gql.mutate(StartScanMutation, { libraryId: library.id, full });
      this.scans.update((scans) => ({ ...scans, [library.id]: result.startScan }));
    });
  }

  protected async cancelScan(job: ScanJob): Promise<void> {
    await this.run(() => this.gql.mutate(CancelScanMutation, { id: job.id }));
  }

  protected openCreate(): void {
    this.formError.set(null);
    this.newFolder = '';
    this.form.set({
      id: null,
      name: '',
      visibleToAll: true,
      interval: null,
      intervalUnit: 'HOURS',
      watchForChanges: false,
      watchKind: 'FOLDER',
      folders: [],
    });
  }

  protected openEdit(library: Library): void {
    this.formError.set(null);
    this.form.set({
      id: library.id,
      name: library.name,
      visibleToAll: library.visibleToAll,
      ...splitInterval(library.scanIntervalMinutes),
      watchForChanges: library.watchForChanges,
      watchKind: this.watchKind(library),
      folders: [],
    });
  }

  protected readonly intervalUnits: IntervalUnit[] = ['MINUTES', 'HOURS', 'DAYS'];

  /** The interval as text, in the largest unit that expresses it exactly. */
  protected intervalLabel(minutes: number): string {
    const { interval, intervalUnit } = splitInterval(minutes);
    return this.i18n.t('libraries.every.' + intervalUnit, { count: interval ?? 0 });
  }

  private watchKind(library: Library): LibraryForm['watchKind'] {
    const kinds = (library.sources ?? []).map((s) => s.kind);
    if (kinds.every((k) => k === 'LOCAL')) return 'FOLDER';
    if (kinds.every((k) => k === 'ULTIMATE_CLOUD')) return 'CLOUD';
    return null;
  }

  protected updateForm(change: Partial<LibraryForm>): void {
    this.form.update((form) => (form ? { ...form, ...change } : form));
  }

  protected addFormFolder(path: string): void {
    const trimmed = path.trim();
    if (!trimmed) return;
    this.form.update((form) => {
      if (!form) return form;
      // One folder per library: choosing another replaces it.
      const name = form.name || trimmed.split(/[\\/]/).filter(Boolean).pop() || '';
      return { ...form, name, folders: [trimmed] };
    });
    this.newFolder = '';
  }

  protected async saveForm(): Promise<void> {
    const form = this.form();
    if (!form) return;

    this.formError.set(null);
    this.busy.set(true);
    try {
      const interval = form.interval
        ? Number(form.interval) * UnitMinutes[form.intervalUnit]
        : null;
      const watchForChanges = form.watchKind !== null && form.watchForChanges;
      if (form.id) {
        await this.gql.mutate(UpdateLibraryMutation, {
          id: form.id,
          input: {
            name: form.name.trim(),
            visibleToAll: form.visibleToAll,
            scanIntervalMinutes: interval,
            watchForChanges,
          },
        });
      } else {
        await this.gql.mutate(CreateLibraryMutation, {
          input: {
            name: form.name.trim(),
            visibleToAll: form.visibleToAll,
            scanIntervalMinutes: interval,
            watchForChanges,
            sources: form.folders.map((location) => ({ location })),
          },
        });
      }
      this.form.set(null);
      toast.success(this.i18n.t('libraries.saved'));
      await this.load();
    } catch (error) {
      this.formError.set(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  protected async picked(path: string): Promise<void> {
    const target = this.picking();
    this.picking.set(null);
    if (!target) return;

    if (target.libraryId === null) {
      this.addFormFolder(path);
      return;
    }

    await this.run(async () => {
      await this.gql.mutate(AddLibrarySourceMutation, {
        libraryId: target.libraryId!,
        input: { location: path },
      });
      toast.success(this.i18n.t('libraries.folderAdded'));
      await this.load();
    });
  }

  protected async removeSource(sourceId: string): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(RemoveLibrarySourceMutation, { id: sourceId });
      await this.load();
    });
  }

  protected async confirmDelete(): Promise<void> {
    const library = this.deleting();
    this.deleting.set(null);
    if (!library) return;

    await this.run(async () => {
      await this.gql.mutate(DeleteLibraryMutation, { id: library.id });
      toast.success(this.i18n.t('libraries.deleted', { name: library.name }));
      await this.load();
    });
  }

  private async loadCloudSyncs(): Promise<void> {
    try {
      const result = await this.gql.query(CloudSyncsQuery);
      this.cloudSyncs.set(Object.fromEntries(result.cloudSyncs.map((s) => [s.libraryId, s])));
      this.cloudSyncAvailable.set(result.serverInfo.capabilities.includes('CLOUD_SYNC'));
      this.s3Available.set(result.serverInfo.capabilities.includes('THIRD_PARTY_STORAGE'));
    } catch {
      // Retried while following; without it the libraries still show.
    }
  }

  protected cloudSyncChanged(state: CloudSyncState): void {
    this.cloudSyncs.update((syncs) => ({ ...syncs, [state.libraryId]: state }));
  }

  /** Libraries with folders on this server, which can sync with Ultimate Cloud. */
  protected canSync(library: Library): boolean {
    return (
      this.auth.isAdmin() &&
      (library.sources?.length ?? 0) > 0 &&
      (library.sources ?? []).every((s) => s.kind === 'LOCAL')
    );
  }

  private async load(): Promise<void> {
    const result = await this.gql.query(LibrariesQuery);
    this.libraries.set(result.libraries);
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      toast.error(this.describe(ApiError.from(error)));
    } finally {
      this.busy.set(false);
    }
  }

  /** The first field error when there is one (e.g. a folder that does not exist), else the general error. */
  private describe(error: ApiError): string {
    const fieldCode = Object.values(error.fields)[0]?.[0];
    return this.i18n.error(fieldCode ?? error.code);
  }
}
