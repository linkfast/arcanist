import { PrivacyChoice, PrivacyLevel } from '../../shared/privacy-level';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideFolderPlus, lucidePlus, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import {
  ApiError,
  DatabaseProvider,
  ExternalServicesMode,
  OnlineServicesChoice,
  RatingMode,
  SetupFfmpegCheck,
  SetupFfmpegDownload,
  SetupStatus,
} from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, Language, TranslatePipe } from '../../core/i18n/i18n';
import { SetupService } from '../../core/setup/setup.service';
import type {
  MstreamImportJobFieldsFragment as MstreamImportJob,
  ScanJobFieldsFragment as ScanJob,
} from '../../graphql/generated/graphql';
import {
  CreateLibraryMutation,
  CreateServerDirectoryMutation,
  FfmpegQuery,
  InstallFfmpegMutation,
  LibrariesQuery,
  MstreamImportsQuery,
  ReconfigureServerMutation,
  ScanJobsQuery,
  ServerConfigurationQuery,
  ServerDirectoriesQuery,
  SetupExtrasQuery,
  AnalyzeMstreamFolderMutation,
  AnalyzeMstreamServerMutation,
  ImportMstreamMutation,
} from '../../graphql/operations';
import {
  DlnaBrowseApi,
  DlnaImportChoice,
  DlnaImportForm,
  startDlnaImport,
} from '../import/dlna-import';

import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import {
  ExtraFailure,
  SetupExtrasDraft,
  WIZARD_EXTENSIONS,
  applySetupExtras,
} from './setup-extras';
import { SetupUltimateStep } from './setup-ultimate';
import { SetupAddonsStep, SetupP2PStep, SetupUsersStep } from './setup-steps';
import { SetupRestoreDialog, SetupRestoring } from './setup-restore';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { BytesPipe, CountPipe } from '../../shared/format';
import {
  MstreamImportDraft,
  MstreamImportForm,
  MstreamSourceInput,
  NewLibrary,
} from '../../shared/mstream-import';

type Step =
  | 'code'
  | 'welcome'
  | 'database'
  | 'admin'
  | 'import'
  | 'libraries'
  | 'users'
  | 'media'
  | 'ratings'
  | 'privacy'
  | 'p2p'
  | 'ultimate'
  | 'addons'
  | 'review'
  | 'progress';

interface DraftLibrary {
  name: string;
  folders: string[];
  visibleToAll: boolean;
  /** The mStream library it was made for, and the folder chosen for it there. */
  mstream?: { vpath: string; folder: string };
}

/** A library the server already has, shown when the wizard runs again. */
interface ExistingLibrary {
  name: string;
  visibleToAll: boolean;
  locations: string[];
}

/** How often the last screen refreshes the first scans and the import. */
const PROGRESS_MS = 1500;

@Component({
  selector: 'app-setup-page',
  imports: [
    FormsModule,
    NgIcon,
    BrnDialogContent,
    HlmCardImports,
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    HlmRadioGroupImports,
    HlmSwitchImports,
    HlmCheckboxImports,
    SetupUsersStep,
    SetupP2PStep,
    SetupAddonsStep,
    SetupUltimateStep,
    SetupRestoreDialog,
    SetupRestoring,
    PrivacyLevel,
    DlnaImportForm,
    HlmAlertImports,
    HlmBadgeImports,
    HlmDialogImports,
    HlmSpinnerImports,
    HlmProgressImports,
    TranslatePipe,
    FolderPicker,
    MstreamImportForm,
    CountPipe,
    BytesPipe,
  ],
  providers: [provideIcons({ lucideCheck, lucideFolderPlus, lucidePlus, lucideTrash2, lucideX })],
  templateUrl: './setup.page.html',
})
export class SetupPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly setup = inject(SetupService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly gql = inject(Gql);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Run again on a server already set up (from Settings): only its answers (language, libraries to add, ffmpeg,
   * ratings, online services and anonymous statistics); the database, the account and the users stay.
   */
  protected readonly reconfigure = inject(ActivatedRoute).snapshot.data['reconfigure'] === true;

  protected readonly status = signal<SetupStatus | null>(null);
  protected readonly step = signal<Step>('welcome');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]>>({});
  /** Restoring a server from a backup instead of setting it up: the dialog, then the wait while it restarts. */
  protected readonly restoreOpen = signal(false);
  protected readonly restoring = signal(false);

  protected restoreStarted(): void {
    this.restoreOpen.set(false);
    this.restoring.set(true);
  }

  protected readonly steps = computed<Step[]>(() => {
    if (this.reconfigure)
      return [
        'welcome',
        'import',
        'libraries',
        'users',
        'media',
        'ratings',
        'privacy',
        'p2p',
        'ultimate',
        'addons',
        'review',
      ];
    const status = this.status();
    const steps: Step[] = [
      'welcome',
      'database',
      'admin',
      'import',
      'libraries',
      'users',
      'media',
      'ratings',
      'privacy',
      'p2p',
      'ultimate',
      'addons',
      'review',
    ];
    return status?.requiresCode ? ['code', ...steps] : steps;
  });
  protected readonly stepIndex = computed(() => this.steps().indexOf(this.step()));

  // Answers
  protected code = '';
  protected provider: DatabaseProvider = 'SQLITE';
  protected connectionString = '';
  protected readonly databaseTest = signal<{ success: boolean; error: string | null } | null>(null);
  protected userName = '';
  protected displayName = '';
  protected password = '';
  protected passwordConfirmation = '';
  protected readonly libraries = signal<DraftLibrary[]>([
    { name: '', folders: [], visibleToAll: true },
  ]);
  /** Running the wizard again: the libraries the server already has (changed in Libraries, not here). */
  protected readonly existingLibraries = signal<ExistingLibrary[]>([]);
  /** Users, P2P network, extensions and player: applied once signed in, with the panel's operations. */
  protected readonly extras = new SetupExtrasDraft();
  protected readonly existingUsers = signal<
    { userName: string; displayName?: string | null; role: string }[]
  >([]);
  /** What could not be applied at the end. */
  protected readonly extrasFailures = signal<ExtraFailure[]>([]);
  /** No statistics leave the server. */
  protected noTelemetry = false;
  protected externalServices: ExternalServicesMode = 'ALL';

  /**
   * A privacy level (or a combination in between): without outside services the online services go off, and back
   * on (as the wizard starts) when they are allowed again; without telemetry, no anonymous statistics.
   */
  protected setPrivacy(choice: PrivacyChoice): void {
    const wasAllowed = this.externalServices === 'ALL';
    this.noTelemetry = choice.noTelemetry;
    this.externalServices = choice.externalServices;
    if (this.externalServices !== 'ALL')
      for (const key of this.onlineServiceKeys) this.onlineServices[key] = false;
    else if (!wasAllowed) for (const key of this.onlineServiceKeys) this.onlineServices[key] = true;
    if (this.statisticsForced()) this.shareStatistics = 'no';
  }

  /** Anonymous statistics cannot be sent with these choices. */
  protected statisticsForced(): boolean {
    return this.noTelemetry || this.externalServices === 'NONE';
  }

  /** Libraries not visible to everyone, by name: new users see them only when given. */
  protected restrictedLibraries(): string[] {
    return [
      ...this.existingLibraries()
        .filter((l) => !l.visibleToAll)
        .map((l) => l.name),
      ...this.libraries()
        .filter((l) => !l.visibleToAll && l.folders.length > 0 && l.name.trim())
        .map((l) => l.name.trim()),
    ];
  }

  /** The privacy choices, for the review step. */
  protected privacySummary(): string {
    const parts: string[] = [];
    if (this.noTelemetry) parts.push(this.i18n.t('setup.privacy.noTelemetry'));
    if (this.externalServices === 'NONE') parts.push(this.i18n.t('setup.privacy.noExternal'));
    if (this.externalServices === 'ULTIMATE_CLOUD_ONLY')
      parts.push(this.i18n.t('setup.privacy.cloudOnly'));
    return parts.join(' · ');
  }

  /** Libraries with a folder, which the wizard will create. */
  protected newLibraryCount(): number {
    return this.libraries().filter((l) => l.folders.length > 0).length;
  }

  protected usersSummary(): string {
    return this.extras
      .users()
      .map((u) => u.userName.trim())
      .join(', ');
  }

  /** The P2P, extension and player choices, for the review step. */
  protected addonsSummary(): string {
    const parts = WIZARD_EXTENSIONS.filter((id) => this.extras.extensions[id]).map((id) =>
      this.i18n.t('setup.addons.extensions.' + id + '.name'),
    );
    const urls = this.extras.extensionUrls().filter((u) => u.trim()).length;
    if (urls) parts.push(this.i18n.t('setup.review.extensionUrls', { count: urls }));
    if (this.extras.playerUrl.trim()) parts.push(this.i18n.t('setup.review.player'));
    return parts.join(', ');
  }
  protected shareStatistics: 'yes' | 'no' | null = 'yes';
  /** Another server to bring music, users and their data from. */
  protected migrateFrom: 'none' | 'mstream' | 'dlna' = 'none';
  /** The copy chosen from a DLNA server; it starts once the server is set up (or at the end, running the wizard again). */
  protected readonly dlnaChoice = signal<DlnaImportChoice | null>(null);
  protected readonly dlnaPicking = signal(false);
  /** A DLNA copy was started at the end: it goes on in Import. */
  protected readonly dlnaStarted = signal(false);
  /** Running the wizard again: the folders of the libraries the server has, for imports to go into. */
  protected readonly existingSources = signal<{ id: string; label: string; local: boolean }[]>([]);
  protected localSources(): { id: string; label: string }[] {
    return this.existingSources().filter((s) => s.local);
  }

  /** Before there is an account, media servers are read through the setup API. */
  protected readonly setupDlnaApi: DlnaBrowseApi = {
    search: () => this.setup.dlnaServers(),
    describe: (location) => this.setup.dlnaServer(location),
    browse: (location, folderId) => this.setup.dlnaBrowse(location, folderId),
  };

  protected dlnaFolders(choice: DlnaImportChoice): string {
    return choice.folders.map((f) => f.title).join(', ');
  }

  /** Where the copy goes: the new library's folder, or the chosen library folder. */
  protected dlnaPlace(choice: DlnaImportChoice): string {
    return (
      choice.newLibraryPath ??
      this.existingSources().find((s) => s.id === choice.sourceId)?.label ??
      ''
    );
  }

  protected chooseDlna(choice: DlnaImportChoice): void {
    this.dlnaChoice.set(choice);
    this.dlnaPicking.set(false);
  }

  /** The users the mStream import brings (one named like the administrator is the administrator). */
  protected importedUsers(): { userName: string; displayName?: string | null; role: string }[] {
    if (this.migrateFrom !== 'mstream' || !this.mstream.importUsers) return [];
    const admin = this.userName.trim().toLowerCase();
    return (this.mstream.analysis()?.users ?? [])
      .filter((u) => u.userName.toLowerCase() !== admin && !u.exists)
      .map((u) => ({
        userName: u.userName,
        displayName: 'mStream',
        role: u.isAdmin ? 'ADMIN' : 'USER',
      }));
  }
  protected readonly mstream = new MstreamImportDraft();
  protected readonly analyzeMstream = async (source: MstreamSourceInput) =>
    !this.reconfigure
      ? this.setup.analyzeMstream(source)
      : source.server
        ? (await this.gql.mutate(AnalyzeMstreamServerMutation, { input: source.server }))
            .analyzeMstreamServer
        : (await this.gql.mutate(AnalyzeMstreamFolderMutation, { folder: source.folder ?? '' }))
            .analyzeMstreamFolder;
  protected ffmpegPath = '';
  protected ratingMode: RatingMode = 'STAR';
  /** On unless the user turns them off (or the privacy level does): each says what it sends. */
  protected readonly onlineServices: OnlineServicesChoice = {
    musicBrainz: true,
    acoustId: true,
    coverArtArchive: true,
    lyrics: true,
  };
  protected readonly onlineServiceKeys = [
    'musicBrainz',
    'acoustId',
    'coverArtArchive',
    'lyrics',
  ] as const;

  /** Names of the online services turned on, for the review step. */
  protected enabledOnlineServices(): string {
    return this.onlineServiceKeys
      .filter((key) => this.onlineServices[key])
      .map((key) => this.i18n.t(`setup.privacy.services.${key}.name`))
      .join(', ');
  }

  /** What the server finds by itself, shown when the step opens. */
  protected readonly ffmpegDetected = signal<SetupFfmpegCheck | null>(null);
  /** Result of checking the path the user typed. */
  protected readonly ffmpegCheck = signal<SetupFfmpegCheck | null>(null);
  /** Whether ffmpeg can be downloaded here, and how its download goes (null until known). */
  protected readonly ffmpegDownload = signal<SetupFfmpegDownload | null>(null);
  protected readonly ffmpegDownloading = computed(() =>
    ['DOWNLOADING', 'INSTALLING'].includes(this.ffmpegDownload()?.progress.state ?? ''),
  );
  protected readonly ffmpegDownloadPercent = computed(() => {
    const progress = this.ffmpegDownload()?.progress;
    return progress?.totalBytes
      ? Math.round((progress.bytesReceived / progress.totalBytes) * 100)
      : 0;
  });

  // Last screen: the first scans and the import, once the server is set up
  protected readonly scanJobs = signal<ScanJob[]>([]);
  protected readonly libraryNames = signal<Record<string, string>>({});
  protected readonly importJob = signal<MstreamImportJob | null>(null);
  protected importing = false;
  private readonly progressLoaded = signal(false);
  protected readonly finished = computed(
    () =>
      this.progressLoaded() &&
      this.scanJobs().every((job) => job.status !== 'QUEUED' && job.status !== 'RUNNING') &&
      (!this.importing ||
        this.importJob()?.status === 'COMPLETED' ||
        this.importJob()?.status === 'FAILED'),
  );

  // Folder picker
  protected readonly pickingFor = signal<number | null>(null);
  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    this.reconfigure
      ? (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories
      : this.setup.directories(path);
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    this.reconfigure
      ? (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
          .createServerDirectory
      : this.setup.createDirectory(parent, name);

  async ngOnInit(): Promise<void> {
    if (this.reconfigure) {
      // The answers as they are now.
      const current = (await this.gql.query(ServerConfigurationQuery)).serverConfiguration;
      this.ratingMode = current.ratingMode;
      this.shareStatistics = current.shareAnonymousStatistics ? 'yes' : 'no';
      this.ffmpegPath = current.ffmpegPath ?? '';
      const { __typename, ...online } = current.onlineServices as typeof current.onlineServices & {
        __typename?: string;
      };
      void __typename;
      Object.assign(this.onlineServices, online);
      this.noTelemetry = current.privacy.noTelemetry;
      this.externalServices = current.privacy.externalServices;
      const extras = await this.gql.query(SetupExtrasQuery);
      this.existingUsers.set(extras.users);
      const p2p = extras.p2pStatus;
      this.extras.p2pEnabled = p2p.enabled;
      this.extras.p2pName = p2p.customName ?? '';
      this.extras.p2pRelay = p2p.relay;
      this.extras.p2pRelayUrl = p2p.relayUrl ?? '';
      this.extras.p2pLimits = {
        bandwidthKbps: p2p.bandwidthKbps ?? null,
        maxStreams: p2p.maxStreams ?? null,
      };
      this.extras.p2pInitial = this.extras.p2pKey();
      for (const id of WIZARD_EXTENSIONS)
        this.extras.extensions[id] = extras.extensions.some((e) => e.id === id && e.enabled);
      this.extras.extensionsInitial = { ...this.extras.extensions };
      const existing = (await this.gql.query(LibrariesQuery)).libraries.filter((l) => !l.personal);
      this.existingSources.set(
        existing.flatMap((l) =>
          (l.sources ?? []).map((s) => ({
            id: s.id,
            label: `${l.name} — ${s.location}`,
            local: s.kind === 'LOCAL',
          })),
        ),
      );
      this.existingLibraries.set(
        existing.map((l) => ({
          name: l.name,
          visibleToAll: l.visibleToAll,
          locations: (l.sources ?? []).map((s) => s.location),
        })),
      );
      // Only new libraries are added here: none to start with when there are some.
      if (existing.length > 0) this.libraries.set([]);
      this.status.set({ requiresCode: false } as SetupStatus);
      this.step.set('welcome');
      return;
    }
    const status = await this.setup.loadStatus();
    if (status.completed && status.migrating) {
      // Set up, with the migration still running: its progress, until it ends.
      if (!this.auth.isAuthenticated() && !(await this.auth.refresh())) {
        await this.router.navigate(['/login'], { queryParams: { returnUrl: '/setup' } });
        return;
      }
      this.status.set(status);
      this.importing = true;
      this.step.set('progress');
      await this.followProgress();
      return;
    }
    this.status.set(status);
    this.step.set(status.requiresCode ? 'code' : 'welcome');
  }

  protected setLanguage(language: string): void {
    this.i18n.setLanguage(language as Language);
  }

  protected back(): void {
    this.error.set(null);
    this.step.set(this.steps()[Math.max(0, this.stepIndex() - 1)]);
  }

  protected async next(): Promise<void> {
    this.error.set(null);
    if (!(await this.validate(this.step()))) return;
    this.step.set(this.steps()[this.stepIndex() + 1]);
    if (this.step() === 'media' && !this.ffmpegDetected()) await this.detectFfmpeg();
  }

  private async detectFfmpeg(): Promise<void> {
    if (this.reconfigure) {
      try {
        const ffmpeg = (await this.gql.query(FfmpegQuery)).ffmpeg;
        this.ffmpegDetected.set({
          available: ffmpeg.available,
          path: ffmpeg.path ?? null,
          version: ffmpeg.version ?? null,
          source: ffmpeg.source ?? null,
        } as SetupFfmpegCheck);
        this.ffmpegDownload.set(this.fromStatus(ffmpeg));
      } catch {
        this.ffmpegDetected.set({ available: false, path: null, version: null, source: null });
      }
      return;
    }
    try {
      this.ffmpegDetected.set(await this.setup.checkFfmpeg(null));
      this.ffmpegDownload.set(await this.setup.ffmpegDownload());
    } catch {
      this.ffmpegDetected.set({ available: false, path: null, version: null, source: null });
    }
  }

  /** Downloads ffmpeg into the server's data folder and follows the download until it ends. */
  protected async downloadFfmpeg(): Promise<void> {
    this.error.set(null);
    try {
      this.ffmpegDownload.set(
        this.reconfigure
          ? this.fromStatus((await this.gql.mutate(InstallFfmpegMutation, {})).installFfmpeg)
          : await this.setup.installFfmpeg(),
      );
      while (this.ffmpegDownloading()) {
        await new Promise((resolve) => setTimeout(resolve, 1000));
        this.ffmpegDownload.set(
          this.reconfigure
            ? this.fromStatus((await this.gql.query(FfmpegQuery)).ffmpeg)
            : await this.setup.ffmpegDownload(),
        );
      }
      const progress = this.ffmpegDownload()?.progress;
      if (progress?.state === 'FAILED') {
        this.error.set(this.i18n.error(progress.error ?? 'ffmpeg_download_failed'));
        return;
      }
      // Found from now on in the data folder, as any other ffmpeg would be.
      this.ffmpegPath = '';
      this.ffmpegCheck.set(null);
      this.ffmpegDetected.set(null);
      await this.detectFfmpeg();
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    }
  }

  private fromStatus(ffmpeg: {
    canDownload: boolean;
    download: { state: string; bytesReceived: number; totalBytes?: number | null };
  }): SetupFfmpegDownload {
    return {
      canDownload: ffmpeg.canDownload,
      progress: {
        state: ffmpeg.download.state as SetupFfmpegDownload['progress']['state'],
        bytesReceived: ffmpeg.download.bytesReceived,
        totalBytes: ffmpeg.download.totalBytes ?? null,
        version: null,
        error: null,
      },
    };
  }

  protected async checkFfmpeg(): Promise<boolean> {
    const path = this.ffmpegPath.trim();
    // Reconfiguring, the server checks it when the answers are saved.
    if (!path || this.reconfigure) {
      this.ffmpegCheck.set(null);
      return true;
    }
    this.busy.set(true);
    try {
      const check = await this.setup.checkFfmpeg(path);
      this.ffmpegCheck.set(check);
      return check.available;
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected goTo(step: Step): void {
    this.error.set(null);
    this.step.set(step);
  }

  protected async testDatabase(): Promise<void> {
    this.busy.set(true);
    this.databaseTest.set(null);
    try {
      this.databaseTest.set(
        await this.setup.testDatabase(this.provider, this.connectionString || null),
      );
    } catch (error) {
      this.databaseTest.set({ success: false, error: this.i18n.error(ApiError.from(error).code) });
    } finally {
      this.busy.set(false);
    }
  }

  protected addLibrary(): void {
    this.libraries.update((libraries) => [
      ...libraries,
      { name: '', folders: [], visibleToAll: true },
    ]);
  }

  protected removeLibrary(index: number): void {
    this.libraries.update((libraries) => libraries.filter((_, i) => i !== index));
  }

  protected addFolder(path: string): void {
    const index = this.pickingFor();
    this.pickingFor.set(null);
    if (index === null) return;

    this.libraries.update((libraries) =>
      libraries.map((library, i) => {
        if (i !== index) return library;
        // A library has one folder: choosing another replaces it. The library is named after its folder unless the
        // user already named it.
        const name = library.name || path.split(/[\\/]/).filter(Boolean).pop() || '';
        return { ...library, name, folders: [path] };
      }),
    );
  }

  protected removeFolder(index: number, folder: string): void {
    this.libraries.update((libraries) =>
      libraries.map((library, i) =>
        i === index
          ? { ...library, folders: library.folders.filter((f) => f !== folder) }
          : library,
      ),
    );
  }

  protected updateLibrary(index: number, change: Partial<DraftLibrary>): void {
    this.libraries.update((libraries) =>
      libraries.map((library, i) => (i === index ? { ...library, ...change } : library)),
    );
  }

  protected fieldError(prefix: string): string | null {
    const code = Object.entries(this.fieldErrors()).find(([field]) =>
      field.startsWith(prefix),
    )?.[1]?.[0];
    return code ? this.i18n.error(code) : null;
  }

  /** The mStream libraries that come along, each with the folder of a new library it goes to. */
  protected mstreamLibraries(): { vpath: string; folder: string; copyFiles: boolean }[] {
    if (this.migrateFrom !== 'mstream') return [];
    const folders = this.libraries().flatMap((library) => library.folders);
    return this.mstream
      .choices()
      .filter((c) => c.target === NewLibrary && folders.includes(c.path.trim()))
      .map((c) => ({ vpath: c.vpath, folder: c.path.trim(), copyFiles: this.mstream.copies(c) }));
  }

  /**
   * Turns the mStream libraries chosen into libraries of the next step: they keep what the user changed there,
   * those no longer chosen go, and a folder already in a library is not added again.
   */
  private mergeMstreamLibraries(): void {
    const chosen =
      this.migrateFrom === 'mstream'
        ? this.mstream.choices().filter((c) => c.target === NewLibrary && c.path.trim())
        : [];
    this.libraries.update((libraries) => {
      const kept = libraries
        .filter((l) => !l.mstream || chosen.some((c) => c.vpath === l.mstream!.vpath))
        .map((l) => {
          const choice = l.mstream && chosen.find((c) => c.vpath === l.mstream!.vpath);
          if (!choice || choice.path.trim() === l.mstream!.folder) return l;
          const folder = choice.path.trim();
          return {
            ...l,
            folders: [folder],
            mstream: { vpath: choice.vpath, folder },
          };
        });
      const added = chosen
        .filter(
          (c) =>
            !kept.some((l) => l.mstream?.vpath === c.vpath || l.folders.includes(c.path.trim())),
        )
        .map((c) => ({
          name: c.name.trim(),
          folders: [c.path.trim()],
          // With their users, each sees the libraries they had in mStream.
          visibleToAll: !this.mstream.importUsers,
          mstream: { vpath: c.vpath, folder: c.path.trim() },
        }));
      const result = [
        ...kept.filter((l) => l.mstream || l.name.trim() || l.folders.length > 0),
        ...added,
      ];
      return result.length > 0 ? result : [{ name: '', folders: [], visibleToAll: true }];
    });
  }

  /** Saves the answers again, adds the new libraries and goes back to the panel. */
  private async finishReconfigure(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      await this.gql.mutate(ReconfigureServerMutation, {
        input: {
          defaultLanguage: this.i18n.language(),
          ratingMode: this.ratingMode,
          onlineServices: { ...this.onlineServices },
          shareAnonymousStatistics: this.shareStatistics === 'yes',
          ffmpegPath: this.ffmpegPath.trim() || null,
          privacy: { noTelemetry: this.noTelemetry, externalServices: this.externalServices },
        },
      });
      for (const library of this.libraries().filter((l) => l.folders.length > 0)) {
        await this.gql.mutate(CreateLibraryMutation, {
          input: {
            name: library.name.trim(),
            visibleToAll: library.visibleToAll,
            scanIntervalMinutes: null,
            watchForChanges: false,
            sources: library.folders.map((location) => ({ location })),
          },
        });
      }
      await this.applyExtras();
      const imported = await this.startMstream();
      await this.startDlna();
      toast.success(this.i18n.t('setup.reconfigured'));
      // Imports go on by themselves: their progress is in Import.
      if (imported || this.dlnaStarted()) {
        toast.info(this.i18n.t('setup.importContinues'));
        await this.router.navigateByUrl('/import');
      } else await this.router.navigateByUrl('/settings');
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      this.error.set(this.i18n.error(apiError.code));
      if (Object.keys(apiError.fields).some((field) => field === 'path')) this.step.set('media');
    } finally {
      this.busy.set(false);
    }
  }

  protected async finish(): Promise<void> {
    if (this.reconfigure) return this.finishReconfigure();
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      await this.setup.complete({
        database: this.status()?.database.lockedByEnvironment
          ? null
          : {
              provider: this.provider,
              connectionString: this.provider === 'POSTGRES' ? this.connectionString : null,
            },
        admin: {
          userName: this.userName.trim(),
          password: this.password,
          displayName: this.displayName.trim() || null,
        },
        libraries: this.libraries()
          .filter((library) => library.folders.length > 0)
          .map((library) => ({
            name: library.name.trim(),
            folders: library.folders,
            visibleToAll: library.visibleToAll,
          })),
        shareAnonymousStatistics: this.shareStatistics === 'yes',
        language: this.i18n.language(),
        ffmpegPath: this.ffmpegPath.trim() || null,
        onlineServices: { ...this.onlineServices },
        ratingMode: this.ratingMode,
        privacy: { noTelemetry: this.noTelemetry, externalServices: this.externalServices },
        mstream:
          this.migrateFrom === 'mstream' && this.mstream.analyzed
            ? {
                folder: this.mstream.analyzed.folder,
                server: this.mstream.analyzed.server,
                libraries: this.mstreamLibraries(),
                importUsers: this.mstream.importUsers,
              }
            : null,
      });

      await this.setup.loadStatus();
      await this.auth.login(this.userName.trim(), this.password);
      await this.applyExtras();
      await this.startDlna();
      this.importing = this.migrateFrom === 'mstream';
      if (!this.importing && !this.libraries().some((library) => library.folders.length > 0)) {
        await this.router.navigateByUrl(this.dlnaStarted() ? '/import' : '/libraries');
        return;
      }
      this.step.set('progress');
      await this.followProgress();
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      this.error.set(this.i18n.error(apiError.code));
      // Show the first step with a problem.
      const prefixes: Partial<Record<Step, string>> = {
        database: 'database',
        admin: 'admin',
        import: 'mstream',
        libraries: 'libraries',
        media: 'ffmpegPath',
      };
      const step = (Object.keys(prefixes) as Step[]).find((s) =>
        Object.keys(apiError.fields).some((field) => field.startsWith(prefixes[s]!)),
      );
      if (step) this.step.set(step);
    } finally {
      this.busy.set(false);
    }
  }

  /** Users, P2P network, extensions and player, with the administrator's session; what fails is shown, not fatal. */
  private async applyExtras(): Promise<void> {
    const libraries = (await this.gql.query(LibrariesQuery)).libraries;
    const failures = await applySetupExtras(
      this.gql,
      this.extras,
      new Map(libraries.map((l) => [l.name, l.id])),
    );
    this.extrasFailures.set(failures);
    for (const failure of failures)
      toast.error(
        this.i18n.t('setup.extrasFailed', {
          what: failure.what,
          error: this.i18n.error(failure.code),
        }),
      );
  }

  /** Starts the chosen DLNA copy; it does not hold the wizard: it goes on in Import. */
  private async startDlna(): Promise<void> {
    const choice = this.dlnaChoice();
    if (this.migrateFrom !== 'dlna' || !choice) return;
    try {
      await startDlnaImport(this.gql, choice);
      this.dlnaStarted.set(true);
    } catch (error) {
      const code = ApiError.from(error).code;
      this.extrasFailures.update((list) => [...list, { what: choice.serverName, code }]);
      toast.error(
        this.i18n.t('setup.extrasFailed', {
          what: choice.serverName,
          error: this.i18n.error(code),
        }),
      );
    }
  }

  /** Running the wizard again: starts the chosen mStream import; it goes on in Import. */
  private async startMstream(): Promise<boolean> {
    if (this.migrateFrom !== 'mstream' || !this.mstream.analyzed) return false;
    try {
      await this.gql.mutate(ImportMstreamMutation, {
        input: {
          folder: this.mstream.analyzed.folder,
          server: this.mstream.analyzed.server,
          importUsers: this.mstream.importUsers,
          libraries: this.mstream.libraryInputs(),
        },
      });
      return true;
    } catch (error) {
      toast.error(
        this.i18n.t('setup.extrasFailed', {
          what: 'mStream',
          error: this.i18n.error(ApiError.from(error).code),
        }),
      );
      return false;
    }
  }

  protected async openPanel(): Promise<void> {
    await this.router.navigateByUrl('/libraries');
  }

  protected scanPercent(job: ScanJob): number | null {
    const progress = job.progress;
    if (!progress || progress.phase !== 'READING' || progress.filesToRead === 0) return null;
    return Math.round((progress.filesRead / progress.filesToRead) * 100);
  }

  /** Follows the first scans and the import until they are over, or the user leaves. */
  private async followProgress(): Promise<void> {
    const libraries = (await this.gql.query(LibrariesQuery)).libraries;
    this.libraryNames.set(Object.fromEntries(libraries.map((l) => [l.id, l.name])));
    const refresh = async () => {
      try {
        const [scans, imports] = await Promise.all([
          this.gql.query(ScanJobsQuery, { limit: 50 }),
          this.importing ? this.gql.query(MstreamImportsQuery) : null,
        ]);
        this.scanJobs.set(scans.scanJobs);
        this.importJob.set(imports?.mstreamImports[0] ?? null);
        this.progressLoaded.set(true);
      } catch {
        // Retried on the next tick.
      }
      if (this.finished()) clearInterval(timer);
    };
    const timer = setInterval(() => void refresh(), PROGRESS_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    await refresh();
  }

  private async validate(step: Step): Promise<boolean> {
    switch (step) {
      case 'code':
        this.busy.set(true);
        try {
          if (await this.setup.verifyCode(this.code)) return true;
          this.error.set(this.i18n.error('setup_code_invalid'));
          return false;
        } catch (error) {
          this.error.set(this.i18n.error(ApiError.from(error).code));
          return false;
        } finally {
          this.busy.set(false);
        }
      case 'database':
        if (this.status()?.database.lockedByEnvironment || this.provider === 'SQLITE') return true;
        if (!this.connectionString.trim()) {
          this.error.set(this.i18n.t('setup.database.connectionStringRequired'));
          return false;
        }
        await this.testDatabase();
        if (!this.databaseTest()?.success) {
          this.error.set(this.i18n.error('connection_failed'));
          return false;
        }
        return true;
      case 'admin':
        if (this.userName.trim().length < 3) {
          this.error.set(this.i18n.t('setup.admin.userNameTooShort'));
          return false;
        }
        if (this.password.length < 8) {
          this.error.set(this.i18n.t('setup.admin.passwordTooShort'));
          return false;
        }
        if (this.password !== this.passwordConfirmation) {
          this.error.set(this.i18n.t('setup.admin.passwordsDiffer'));
          return false;
        }
        return true;
      case 'import':
        if (this.migrateFrom === 'mstream') {
          if (!this.mstream.analysis()) {
            this.error.set(this.i18n.t('setup.import.analyzeFirst'));
            return false;
          }
          if (!this.mstream.complete()) {
            this.error.set(this.i18n.t('setup.import.incomplete'));
            return false;
          }
        }
        if (this.migrateFrom === 'dlna' && !this.dlnaChoice()) {
          this.error.set(this.i18n.t('setup.import.dlnaChoose'));
          return false;
        }
        // Running the wizard again, the import makes its own libraries.
        if (!this.reconfigure) this.mergeMstreamLibraries();
        return true;
      case 'libraries':
        if (
          this.libraries().some((library) => library.folders.length > 0 && !library.name.trim())
        ) {
          this.error.set(this.i18n.t('setup.libraries.nameRequired'));
          return false;
        }
        return true;
      case 'media':
        if (!(await this.checkFfmpeg())) {
          this.error.set(this.i18n.error('not_ffmpeg'));
          return false;
        }
        return true;
      case 'users': {
        const problem = this.extras.usersProblem(
          this.reconfigure ? (this.auth.user()?.userName ?? '') : this.userName,
          [...this.existingUsers(), ...this.importedUsers()].map((u) => u.userName),
        );
        if (problem) {
          this.error.set(this.i18n.t(problem));
          return false;
        }
        return true;
      }
      case 'p2p':
        if (
          this.extras.p2pEnabled &&
          this.extras.p2pRelay === 'CUSTOM' &&
          !/^https?:\/\//.test(this.extras.p2pRelayUrl.trim())
        ) {
          this.error.set(this.i18n.t('setup.p2p.relayUrlRequired'));
          return false;
        }
        return true;
      case 'privacy':
        if (this.statisticsForced()) this.shareStatistics = 'no';
        if (this.shareStatistics === null) {
          this.error.set(this.i18n.t('setup.privacy.choose'));
          return false;
        }
        return true;
      default:
        return true;
    }
  }
}
