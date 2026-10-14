import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFolderPlus,
  lucideImport,
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
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  DropFolderFieldsFragment as DropFolder,
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
  ],
  providers: [
    provideIcons({ lucidePlus, lucidePencil, lucideTrash2, lucideFolderPlus, lucideImport }),
  ],
  templateUrl: './import.page.html',
})
export class ImportPage implements OnInit {
  protected readonly auth = inject(AuthService);
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

  /** Every library location, to choose where a drop folder imports to. */
  protected readonly targets = computed(() =>
    this.libraries().flatMap((library) =>
      (library.sources ?? []).map((source) => ({
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

  constructor() {
    const timer = setInterval(() => {
      void this.loadQueue();
      if (this.migrations().some((m) => m.status === 'WAITING' || m.status === 'RUNNING'))
        void this.loadMigrations();
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.loadQueue();
    if (this.auth.isAdmin()) await Promise.all([this.loadFolders(), this.loadMigrations()]);
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
      path: form.path.trim(),
      librarySourceId: form.librarySourceId,
      enabled: form.enabled,
      duplicatePolicy: form.duplicatePolicy,
      convertTo: form.convertTo || null,
      convertBitRate:
        form.convertTo && form.convertTo !== 'FLAC' && form.convertBitRate
          ? Number(form.convertBitRate)
          : null,
      convertLossy: form.convertTo !== '' && form.convertTo !== 'FLAC' && form.convertLossy,
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
