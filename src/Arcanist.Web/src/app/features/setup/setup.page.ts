import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
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
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import {
  ApiError,
  DatabaseProvider,
  OnlineServicesChoice,
  RatingMode,
  SetupFfmpegCheck,
  SetupStatus,
} from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { I18n, Language, TranslatePipe } from '../../core/i18n/i18n';
import { SetupService } from '../../core/setup/setup.service';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';

type Step =
  | 'code'
  | 'welcome'
  | 'database'
  | 'admin'
  | 'libraries'
  | 'media'
  | 'ratings'
  | 'privacy'
  | 'review';

interface DraftLibrary {
  name: string;
  folders: string[];
  visibleToAll: boolean;
}

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
    HlmAlertImports,
    HlmBadgeImports,
    HlmDialogImports,
    HlmSpinnerImports,
    TranslatePipe,
    FolderPicker,
  ],
  providers: [provideIcons({ lucideCheck, lucideFolderPlus, lucidePlus, lucideTrash2, lucideX })],
  templateUrl: './setup.page.html',
})
export class SetupPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly setup = inject(SetupService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly status = signal<SetupStatus | null>(null);
  protected readonly step = signal<Step>('welcome');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  protected readonly steps = computed<Step[]>(() => {
    const status = this.status();
    const steps: Step[] = [
      'welcome',
      'database',
      'admin',
      'libraries',
      'media',
      'ratings',
      'privacy',
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
  protected shareStatistics: 'yes' | 'no' | null = null;
  /** Opens the mStream import once the server is set up. */
  protected importMstream = false;
  protected ffmpegPath = '';
  protected ratingMode: RatingMode = 'STARS';
  /** Off until the user turns each on: they contact other servers. */
  protected readonly onlineServices: OnlineServicesChoice = {
    musicBrainz: false,
    acoustId: false,
    coverArtArchive: false,
    lyrics: false,
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

  // Folder picker
  protected readonly pickingFor = signal<number | null>(null);
  protected readonly loadDirectories: DirectoryLoader = (path) => this.setup.directories(path);
  protected readonly createDirectory: DirectoryCreator = (parent, name) =>
    this.setup.createDirectory(parent, name);

  async ngOnInit(): Promise<void> {
    const status = await this.setup.loadStatus();
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
    try {
      this.ffmpegDetected.set(await this.setup.checkFfmpeg(null));
    } catch {
      this.ffmpegDetected.set({ available: false, path: null, version: null, source: null });
    }
  }

  protected async checkFfmpeg(): Promise<boolean> {
    const path = this.ffmpegPath.trim();
    if (!path) {
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
        if (i !== index || library.folders.includes(path)) return library;
        // Name the library after its first folder unless the user already named it.
        const name = library.name || path.split(/[\\/]/).filter(Boolean).pop() || '';
        return { ...library, name, folders: [...library.folders, path] };
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

  protected async finish(): Promise<void> {
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
      });

      await this.setup.loadStatus();
      await this.auth.login(this.userName.trim(), this.password);
      await this.router.navigateByUrl(this.importMstream ? '/mstream' : '/libraries');
    } catch (error) {
      const apiError = ApiError.from(error);
      this.fieldErrors.set(apiError.fields);
      this.error.set(this.i18n.error(apiError.code));
      // Show the first step with a problem.
      const step = (['database', 'admin', 'libraries', 'media'] as Step[]).find((s) =>
        Object.keys(apiError.fields).some((field) =>
          field.startsWith(s === 'media' ? 'ffmpegPath' : s),
        ),
      );
      if (step) this.step.set(step);
    } finally {
      this.busy.set(false);
    }
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
      case 'privacy':
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
