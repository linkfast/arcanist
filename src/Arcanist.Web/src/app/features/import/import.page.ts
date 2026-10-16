import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFolderPlus,
  lucideImport,
  lucideServer,
  lucidePencil,
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
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  DlnaImportJobFieldsFragment as DlnaImportJob,
  DropFolderFieldsFragment as DropFolder,
  DropFolderKind,
  DropFolderMode,
  DuplicatePolicy,
  ImportItemFieldsFragment as ImportItem,
  ImportQueueQuery,
  ImportStatus,
  LibrariesQuery as LibrariesResult,
  MstreamImportJobFieldsFragment as MstreamImportJob,
  TranscodeFormat,
} from '../../graphql/generated/graphql';
import {
  AcceptImportItemMutation,
  CancelDlnaImportMutation,
  DlnaImportsQuery,
  RetryDlnaImportMutation,
  CreateDropFolderMutation,
  DeleteDropFolderMutation,
  DiscardImportItemMutation,
  DropFoldersQuery,
  AnalyzeMstreamFolderMutation,
  AnalyzeMstreamServerMutation,
  IgnoreImportItemMutation,
  ImportMstreamMutation,
  MstreamImportsQuery,
  ImportQueueQuery as ImportQueue,
  LibrariesQuery,
  UltimateCloudAccountsQuery,
  RetryImportItemMutation,
  CreateServerDirectoryMutation,
  ServerDirectoriesQuery,
  UpdateDropFolderMutation,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { BytesPipe, DateTimePipe, DurationPipe, RelativeTimePipe } from '../../shared/format';
import {
  MstreamImportDraft,
  MstreamImportForm,
  MstreamSourceInput,
} from '../../shared/mstream-import';
import { DlnaImportForm } from './dlna-import';

type Library = LibrariesResult['libraries'][number];
type Summary = ImportQueueQuery['importSummary'];

interface FolderForm {
  id: string | null;
  path: string;
  librarySourceId: string;
  enabled: boolean;
  duplicatePolicy: DuplicatePolicy;
  convertTo: TranscodeFormat | '';
  convertBitRate: number | null;
  convertLossy: boolean;
  mode: DropFolderMode;
  kind: DropFolderKind;
  uploadToCloud: boolean;
  /** The account whose library receives the uploads. */
  uploadAccount: string | null;
}

interface TagsForm {
  title: string;
  artist: string;
  album: string;
  albumArtist: string;
  year: number | null;
  trackNumber: number | null;
  discNumber: number | null;
  genre: string;
}

/** How often the queue is refreshed while the page is open: files arrive on their own. */
const REFRESH_MS = 5000;

/** Drop folders, and the files found in them that wait for a decision. */
@Component({
  selector: 'app-import-page',
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
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSwitchImports,
    HlmSpinnerImports,
    HlmEmptyImports,
    TranslatePipe,
    FolderPicker,
    BytesPipe,
    DateTimePipe,
    DurationPipe,
    RelativeTimePipe,
    MstreamImportForm,
    DlnaImportForm,
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucidePencil,
      lucideTrash2,
      lucideFolderPlus,
      lucideImport,
      lucideServer,
    }),
  ],
  templateUrl: './import.page.html',
})
export class ImportPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly extensions = inject(EnabledExtensions);
  /** The Ultimate Cloud accounts linked to this server: where Cloud Drop's songs can be uploaded. */
  protected readonly cloudAccounts = signal<
    { id: string; name?: string | null; ownerFirstName?: string | null }[]
  >([]);
  /** The Ultimate Cloud extension is on: without it Cloud Drop is not offered. */
  protected readonly cloudOn = computed(() => this.extensions.has('ultimate-cloud'));

  /** A Cloud Drop folder with Ultimate Cloud off, or one into a disabled library: it can only be deleted. */
  protected folderDisabled(folder: DropFolder): boolean {
    if (folder.kind === 'ULTIMATE_CLOUD_DROP' && !this.cloudOn()) return true;
    return this.libraries().some(
      (library) =>
        library.disabled && (library.sources ?? []).some((s) => s.id === folder.librarySourceId),
    );
  }
  private readonly gql = inject(Gql);
  protected readonly i18n = inject(I18n);

  protected readonly statuses: ImportStatus[] = [
    'NEEDS_REVIEW',
    'DUPLICATE',
    'FAILED',
    'IGNORED',
    'IMPORTED',
  ];
  protected readonly policies: DuplicatePolicy[] = ['SKIP', 'KEEP_BEST', 'ADD_VERSION'];
  protected readonly formats: TranscodeFormat[] = ['FLAC', 'OPUS', 'MP3', 'AAC'];

  protected readonly status = signal<ImportStatus>('NEEDS_REVIEW');
  protected readonly summary = signal<Summary | null>(null);
  protected readonly items = signal<ImportItem[] | null>(null);
  protected readonly folders = signal<DropFolder[] | null>(null);
  protected readonly libraries = signal<Library[]>([]);
  protected readonly busy = signal<string | null>(null);

  /**
   * The library locations a drop folder can import to: those Arcanist writes to (on this server or in S3), never
   * Ultimate Cloud's or other servers' libraries, which are read only.
   */
  protected readonly targets = computed(() =>
    this.libraries()
      .filter((library) => !library.disabled)
      .flatMap((library) =>
        (library.sources ?? [])
          .filter((source) => source.kind === 'LOCAL' || source.kind === 'S3')
          .map((source) => ({
            id: source.id,
            label:
              (library.sources?.length ?? 0) > 1
                ? `${library.name} — ${source.location}`
                : library.name,
          })),
      ),
  );

  // Drop folder dialog
  protected readonly form = signal<FolderForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly picking = signal(false);
  protected readonly deleting = signal<DropFolder | null>(null);
  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  // Review dialog
  protected readonly reviewing = signal<ImportItem | null>(null);
  protected tags: TagsForm = this.emptyTags();

  // Migrations from other servers
  protected readonly migrations = signal<MstreamImportJob[]>([]);
  /** The mStream import dialog, with its answers; null while closed. */
  protected readonly migrating = signal<MstreamImportDraft | null>(null);
  protected readonly migrationSources = signal<{ id: string; label: string }[]>([]);
  protected readonly startingMigration = signal(false);
  protected readonly analyzeMstream = async (source: MstreamSourceInput) =>
    source.server
      ? (await this.gql.mutate(AnalyzeMstreamServerMutation, { input: source.server }))
          .analyzeMstreamServer
      : (await this.gql.mutate(AnalyzeMstreamFolderMutation, { folder: source.folder ?? '' }))
          .analyzeMstreamFolder;

  // Copies from DLNA media servers
  protected readonly dlnaImports = signal<DlnaImportJob[]>([]);
  /** Library folders on this server's disks, where copies can go. */
  protected readonly localTargets = computed(() =>
    this.libraries().flatMap((library) =>
      (library.sources ?? [])
        .filter((source) => source.kind === 'LOCAL')
        .map((source) => ({ id: source.id, label: `${library.name} — ${source.location}` })),
    ),
  );
  protected readonly dlnaOpen = signal(false);

  constructor() {
    const timer = setInterval(() => {
      void this.loadQueue();
      if (this.migrations().some((m) => m.status === 'WAITING' || m.status === 'RUNNING'))
        void this.loadMigrations();
    }, REFRESH_MS);
    // Copies move fast: their progress is refreshed more often.
    const dlnaTimer = setInterval(() => {
      if (this.dlnaImports().some((i) => this.dlnaActive(i))) void this.loadDlnaImports();
    }, 1500);
    inject(DestroyRef).onDestroy(() => clearInterval(dlnaTimer));
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.loadQueue();
    if (this.auth.isAdmin())
      await Promise.all([this.loadFolders(), this.loadMigrations(), this.loadDlnaImports()]);
  }

  private async loadDlnaImports(): Promise<void> {
    this.dlnaImports.set((await this.gql.query(DlnaImportsQuery)).dlnaImports);
  }

  protected dlnaActive(job: DlnaImportJob): boolean {
    return job.status === 'QUEUED' || job.status === 'LISTING' || job.status === 'COPYING';
  }

  protected dlnaPercent(job: DlnaImportJob): number | null {
    return job.found > 0
      ? Math.round(((job.copied + job.duplicates + job.failed) * 100) / job.found)
      : null;
  }

  protected async openDlna(): Promise<void> {
    await this.loadLibraries();
    this.dlnaOpen.set(true);
  }

  protected async dlnaStarted(job: DlnaImportJob): Promise<void> {
    this.dlnaOpen.set(false);
    this.dlnaImports.update((list) => [job, ...list.filter((j) => j.id !== job.id)]);
    toast.success(this.i18n.t('dlnaImport.startedToast'));
    await this.loadLibraries();
  }

  protected async cancelDlna(job: DlnaImportJob): Promise<void> {
    await this.dlnaAction(
      job,
      async () =>
        (await this.gql.mutate(CancelDlnaImportMutation, { id: job.id })).cancelDlnaImport,
    );
  }

  protected async retryDlna(job: DlnaImportJob): Promise<void> {
    await this.dlnaAction(
      job,
      async () => (await this.gql.mutate(RetryDlnaImportMutation, { id: job.id })).retryDlnaImport,
    );
  }

  private async dlnaAction(
    job: DlnaImportJob,
    action: () => Promise<DlnaImportJob>,
  ): Promise<void> {
    this.busy.set(job.id);
    try {
      const updated = await action();
      this.dlnaImports.update((list) => list.map((j) => (j.id === updated.id ? updated : j)));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(null);
    }
  }

  private async loadMigrations(): Promise<void> {
    this.migrations.set((await this.gql.query(MstreamImportsQuery)).mstreamImports);
  }

  protected async openMstream(): Promise<void> {
    await this.loadLibraries();
    this.migrationSources.set(
      this.libraries().flatMap((library) =>
        (library.sources ?? []).map((source) => ({
          id: source.id,
          label: `${library.name} — ${source.location}`,
        })),
      ),
    );
    this.migrating.set(new MstreamImportDraft());
  }

  protected async startMstream(): Promise<void> {
    const draft = this.migrating();
    if (!draft?.analyzed) return;
    this.startingMigration.set(true);
    try {
      const result = await this.gql.mutate(ImportMstreamMutation, {
        input: {
          folder: draft.analyzed.folder,
          server: draft.analyzed.server,
          importUsers: draft.importUsers,
          libraries: draft.libraryInputs(),
        },
      });
      this.migrations.update((list) => [result.importMstream, ...list]);
      this.migrating.set(null);
      toast.success(this.i18n.t('mstream.started'));
      // New libraries are scanned now.
      await this.loadLibraries();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.startingMigration.set(false);
    }
  }

  protected count(status: ImportStatus): number {
    const summary = this.summary();
    if (!summary) return 0;
    switch (status) {
      case 'NEEDS_REVIEW':
        return summary.needsReview;
      case 'DUPLICATE':
        return summary.duplicate;
      case 'FAILED':
        return summary.failed;
      case 'IGNORED':
        return summary.ignored;
      case 'IMPORTED':
        return summary.imported;
      default:
        return summary.pending;
    }
  }

  protected async show(status: ImportStatus): Promise<void> {
    this.status.set(status);
    this.items.set(null);
    await this.loadQueue();
  }

  protected targetLabel(sourceId: string): string {
    return this.targets().find((t) => t.id === sourceId)?.label ?? '';
  }

  protected describe(item: ImportItem): string {
    const details = item.details;
    if (!details) return '';
    return [details.title, details.artist ?? details.albumArtist, details.album]
      .filter(Boolean)
      .join(' · ');
  }

  protected quality(item: ImportItem): string {
    const details = item.details;
    if (!details) return '';
    const rate = details.sampleRate ? `${details.sampleRate / 1000} kHz` : null;
    const depth = details.bitDepth ? `${details.bitDepth} bit` : null;
    const bitRate = details.bitRate ? `${details.bitRate} kbit/s` : null;
    return [details.format, depth, rate, depth ? null : bitRate].filter(Boolean).join(' · ');
  }

  // Items

  protected openReview(item: ImportItem): void {
    const details = item.details;
    this.tags = {
      title: details?.title ?? '',
      artist: details?.artist ?? '',
      album: details?.album ?? '',
      albumArtist: details?.albumArtist ?? '',
      year: details?.year ?? null,
      trackNumber: details?.trackNumber ?? null,
      discNumber: details?.discNumber ?? null,
      genre: details?.genre ?? '',
    };
    this.reviewing.set(item);
  }

  protected async saveReview(): Promise<void> {
    const item = this.reviewing();
    if (!item) return;
    const text = (value: string) => value.trim() || null;
    const number = (value: number | null) => (value ? Number(value) : null);
    const ok = await this.act(item, () =>
      this.gql.mutate(AcceptImportItemMutation, {
        id: item.id,
        tags: {
          title: text(this.tags.title),
          artist: text(this.tags.artist),
          album: text(this.tags.album),
          albumArtist: text(this.tags.albumArtist),
          year: number(this.tags.year),
          trackNumber: number(this.tags.trackNumber),
          discNumber: number(this.tags.discNumber),
          genre: text(this.tags.genre),
        },
      }),
    );
    if (ok) this.reviewing.set(null);
  }

  protected accept(item: ImportItem): Promise<boolean> {
    return this.act(item, () => this.gql.mutate(AcceptImportItemMutation, { id: item.id }));
  }

  protected retry(item: ImportItem): Promise<boolean> {
    return this.act(item, () => this.gql.mutate(RetryImportItemMutation, { id: item.id }));
  }

  protected ignore(item: ImportItem): Promise<boolean> {
    return this.act(item, () => this.gql.mutate(IgnoreImportItemMutation, { id: item.id }));
  }

  protected discard(item: ImportItem): Promise<boolean> {
    return this.act(item, () => this.gql.mutate(DiscardImportItemMutation, { id: item.id }));
  }

  private async act(item: ImportItem, action: () => Promise<unknown>): Promise<boolean> {
    this.busy.set(item.id);
    try {
      await action();
      await this.loadQueue();
      return true;
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      return false;
    } finally {
      this.busy.set(null);
    }
  }

  // Drop folders

  protected openCreate(): void {
    this.formError.set(null);
    this.form.set({
      id: null,
      path: '',
      librarySourceId: this.targets()[0]?.id ?? '',
      enabled: true,
      duplicatePolicy: 'SKIP',
      convertTo: '',
      convertBitRate: null,
      convertLossy: false,
      mode: 'MOVE',
      kind: 'FOLDER',
      uploadToCloud: false,
      uploadAccount: this.cloudAccounts()[0]?.id ?? null,
    });
  }

  protected openEdit(folder: DropFolder): void {
    this.formError.set(null);
    this.form.set({
      id: folder.id,
      path: folder.path,
      librarySourceId: folder.librarySourceId,
      enabled: folder.enabled,
      duplicatePolicy: folder.duplicatePolicy,
      convertTo: folder.convertTo ?? '',
      convertBitRate: folder.convertBitRate ?? null,
      convertLossy: folder.convertLossy,
      mode: folder.mode,
      kind: folder.kind,
      uploadToCloud: folder.uploadToCloud,
      uploadAccount: folder.uploadAccount ?? this.cloudAccounts()[0]?.id ?? null,
    });
  }

  protected updateForm(change: Partial<FolderForm>): void {
    this.form.update((form) => (form ? { ...form, ...change } : form));
  }

  protected picked(path: string): void {
    this.picking.set(false);
    this.updateForm({ path });
  }

  protected async saveForm(): Promise<void> {
    const form = this.form();
    if (!form) return;
    this.formError.set(null);
    this.busy.set('form');
    const input = {
      path: form.kind === 'FOLDER' ? form.path.trim() : null,
      kind: form.kind,
      uploadToCloud: form.kind === 'ULTIMATE_CLOUD_DROP' && form.uploadToCloud,
      uploadAccount:
        form.kind === 'ULTIMATE_CLOUD_DROP' && form.uploadToCloud ? form.uploadAccount : null,
      librarySourceId: form.librarySourceId,
      enabled: form.enabled,
      duplicatePolicy: form.duplicatePolicy,
      convertTo: form.convertTo || null,
      convertBitRate:
        form.convertTo && form.convertTo !== 'FLAC' && form.convertBitRate
          ? Number(form.convertBitRate)
          : null,
      convertLossy: form.convertTo !== '' && form.convertTo !== 'FLAC' && form.convertLossy,
      mode: form.mode,
    };
    try {
      if (form.id) await this.gql.mutate(UpdateDropFolderMutation, { id: form.id, input });
      else await this.gql.mutate(CreateDropFolderMutation, { input });
      this.form.set(null);
      toast.success(this.i18n.t('import.folders.saved'));
      await this.loadFolders();
    } catch (error) {
      const apiError = ApiError.from(error);
      this.formError.set(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(null);
    }
  }

  protected async confirmDelete(): Promise<void> {
    const folder = this.deleting();
    this.deleting.set(null);
    if (!folder) return;
    try {
      await this.gql.mutate(DeleteDropFolderMutation, { id: folder.id });
      await this.loadFolders();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  private async loadFolders(): Promise<void> {
    const [folders, libraries] = await Promise.all([
      this.gql.query(DropFoldersQuery),
      this.gql.query(LibrariesQuery),
    ]);
    this.libraries.set(libraries.libraries);
    this.folders.set(folders.dropFolders);
    try {
      this.cloudAccounts.set(
        (await this.gql.query(UltimateCloudAccountsQuery)).ultimateCloudAccounts,
      );
    } catch {
      this.cloudAccounts.set([]);
    }
  }

  /** "Giancarlo's Ultimate Cloud": an account's library, as Ultimate Cloud libraries are named here. */
  protected cloudLibraryLabel(accountId: string | null | undefined): string {
    const account =
      this.cloudAccounts().find((a) => a.id === accountId) ??
      (accountId ? null : this.cloudAccounts()[0]);
    if (!account) return this.i18n.t('import.folders.unlinkedAccount');
    const owner = account.ownerFirstName ?? account.name;
    return owner
      ? this.i18n.t('cloud.libraries.LIBRARY.libraryName', { owner })
      : this.i18n.t('import.folders.kinds.ULTIMATE_CLOUD_DROP');
  }

  /** An account a folder uploads to that is not linked any more. */
  protected accountGone(accountId: string): boolean {
    return !this.cloudAccounts().some((a) => a.id === accountId);
  }

  /** "Giancarlo's Cloud Drop": the linked account's. */
  protected cloudDropLabel(): string {
    const account = this.cloudAccounts()[0];
    const owner = account?.ownerFirstName ?? account?.name;
    return owner
      ? this.i18n.t('cloud.libraries.CLOUD_DROP.libraryName', { owner })
      : this.i18n.t('import.folders.kinds.ULTIMATE_CLOUD_DROP');
  }

  private async loadLibraries(): Promise<void> {
    this.libraries.set((await this.gql.query(LibrariesQuery)).libraries);
  }

  private async loadQueue(): Promise<void> {
    try {
      const result = await this.gql.query(ImportQueue, { status: this.status() });
      this.summary.set(result.importSummary);
      this.items.set(result.importItems);
    } catch {
      // A refresh that fails (the server restarting) is retried on the next tick.
    }
  }

  private emptyTags(): TagsForm {
    return {
      title: '',
      artist: '',
      album: '',
      albumArtist: '',
      year: null,
      trackNumber: null,
      discNumber: null,
      genre: '',
    };
  }
}
