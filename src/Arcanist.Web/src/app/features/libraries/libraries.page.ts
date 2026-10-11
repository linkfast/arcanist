import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFolderPlus,
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
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  LibrariesQuery as LibrariesResult,
  ScanJobFieldsFragment,
} from '../../graphql/generated/graphql';
import {
  AddLibrarySourceMutation,
  CancelScanMutation,
  CreateLibraryMutation,
  DeleteLibraryMutation,
  LibrariesQuery,
  RemoveLibrarySourceMutation,
  ScanJobUpdatedSubscription,
  ScanJobsQuery,
  ServerDirectoriesQuery,
  StartScanMutation,
  UpdateLibraryMutation,
} from '../../graphql/operations';
import { DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';

type Library = LibrariesResult['libraries'][number];
type ScanJob = ScanJobFieldsFragment;

interface LibraryForm {
  id: string | null;
  name: string;
  visibleToAll: boolean;
  scanIntervalHours: number | null;
  folders: string[];
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
    HlmProgressImports,
    HlmSpinnerImports,
    HlmEmptyImports,
    TranslatePipe,
    FolderPicker,
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
      lucideRefreshCw,
      lucideX,
    }),
  ],
  templateUrl: './libraries.page.html',
})
export class LibrariesPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly libraries = signal<Library[] | null>(null);
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

  protected readonly deleting = signal<Library | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();

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
    this.form.set({ id: null, name: '', visibleToAll: true, scanIntervalHours: null, folders: [] });
  }

  protected openEdit(library: Library): void {
    this.formError.set(null);
    this.form.set({
      id: library.id,
      name: library.name,
      visibleToAll: library.visibleToAll,
      scanIntervalHours: library.scanIntervalHours ?? null,
      folders: [],
    });
  }

  protected updateForm(change: Partial<LibraryForm>): void {
    this.form.update((form) => (form ? { ...form, ...change } : form));
  }

  protected addFormFolder(path: string): void {
    const trimmed = path.trim();
    if (!trimmed) return;
    this.form.update((form) => {
      if (!form || form.folders.includes(trimmed)) return form;
      const name = form.name || trimmed.split(/[\\/]/).filter(Boolean).pop() || '';
      return { ...form, name, folders: [...form.folders, trimmed] };
    });
    this.newFolder = '';
  }

  protected async saveForm(): Promise<void> {
    const form = this.form();
    if (!form) return;

    this.formError.set(null);
    this.busy.set(true);
    try {
      const interval = form.scanIntervalHours ? Number(form.scanIntervalHours) : null;
      if (form.id) {
        await this.gql.mutate(UpdateLibraryMutation, {
          id: form.id,
          input: {
            name: form.name.trim(),
            visibleToAll: form.visibleToAll,
            scanIntervalHours: interval,
          },
        });
      } else {
        await this.gql.mutate(CreateLibraryMutation, {
          input: {
            name: form.name.trim(),
            visibleToAll: form.visibleToAll,
            scanIntervalHours: interval,
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
