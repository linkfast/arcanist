import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucideKeyRound, lucideServer } from '@ng-icons/lucide';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  ArcanistImportJobFieldsFragment as ArcanistImportJob,
  CreateMigrationCodeMutation as CreatedCode,
  LibrariesQuery as LibrariesResult,
  MigrationCodeInfoFieldsFragment as MigrationCodeInfo,
} from '../../graphql/generated/graphql';
import {
  AnalyzeArcanistServerQuery,
  ArcanistImportsQuery,
  ArcanistServersQuery,
  CancelArcanistImportMutation,
  CreateMigrationCodeMutation,
  CreateServerDirectoryMutation,
  DeleteArcanistImportMutation,
  LibrariesQuery,
  MigrationCodesQuery,
  RetryArcanistImportMutation,
  RevokeMigrationCodeMutation,
  ServerDirectoriesQuery,
  StartArcanistImportMutation,
} from '../../graphql/operations';
import {
  ArcanistImportApi,
  ArcanistImportDraft,
  ArcanistImportForm,
} from '../../shared/arcanist-import';
import { DirectoryCreator, DirectoryLoader } from '../../shared/folder-picker';
import { BytesPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';

type Library = LibrariesResult['libraries'][number];

/** Whether a migration is still going. */
export function arcanistImportActive(job: ArcanistImportJob): boolean {
  return !['COMPLETED', 'FAILED', 'CANCELLED'].includes(job.status);
}

/** How far a migration's files got, in percent (by size). */
export function arcanistImportPercent(job: ArcanistImportJob): number | null {
  if (job.status !== 'COPYING' || job.filesTotal === 0) return null;
  return job.bytesTotal > 0
    ? Math.min(100, Math.round((job.copiedBytes * 100) / job.bytesTotal))
    : Math.round(((job.filesCopied + job.filesPresent + job.filesFailed) * 100) / job.filesTotal);
}

/** One migration from another Arcanist: its step, its numbers, its failures and actions. */
@Component({
  selector: 'app-arcanist-import-job',
  imports: [
    HlmBadgeImports,
    HlmButtonImports,
    HlmProgressImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  host: { class: 'grid gap-2 rounded-md border p-3 text-sm' },
  template: `
    @let job = this.job();
    <p class="flex flex-wrap items-center gap-2">
      <span hlmBadge variant="outline">Arcanist</span>
      @if (active()) {
        <hlm-spinner class="size-4" />
      }
      <span hlmBadge [variant]="job.status === 'FAILED' ? 'destructive' : 'secondary'">{{
        'arcanistImport.status.' + job.status | t
      }}</span>
      <span class="min-w-0 font-medium break-all">{{ job.sourceName }}</span>
      @if (job.sourceVersion) {
        <span class="text-xs text-muted-foreground">Arcanist {{ job.sourceVersion }}</span>
      }
      <span class="text-xs text-muted-foreground" [title]="job.createdAt | dateTime">{{
        job.createdAt | relativeTime
      }}</span>
    </p>
    @if (job.libraries.length > 0) {
      <p class="text-xs text-muted-foreground">
        {{ 'arcanistImport.job.into' | t: { libraries: job.libraries.join(', ') } }}
      </p>
    }
    <p class="text-xs text-muted-foreground">
      {{
        'arcanistImport.job.prepared'
          | t
            : {
                created: job.usersCreated,
                mapped: job.usersMapped,
                libraries: job.librariesCreated,
                extensions: job.extensionsInstalled,
                players: job.playersInstalled,
              }
      }}
      @if (job.settingsApplied) {
        · {{ 'arcanistImport.job.settings' | t }}
      }
    </p>
    @if (job.filesTotal > 0) {
      @if (percent(); as value) {
        <hlm-progress [value]="value"><hlm-progress-indicator /></hlm-progress>
      }
      <p class="text-xs text-muted-foreground">
        {{
          'arcanistImport.job.files'
            | t
              : {
                  copied: job.filesCopied,
                  present: job.filesPresent,
                  failed: job.filesFailed,
                  total: job.filesTotal,
                  size: (job.copiedBytes | bytes),
                  totalSize: (job.bytesTotal | bytes),
                }
        }}
      </p>
    }
    @if (job.status === 'WAITING') {
      <p class="text-xs text-muted-foreground">{{ 'arcanistImport.job.waiting' | t }}</p>
    }
    @if (job.status === 'COMPLETED') {
      <p class="text-xs text-muted-foreground">
        {{
          'arcanistImport.job.brought'
            | t
              : {
                  ratings: job.ratings,
                  plays: job.plays,
                  playlists: job.playlists,
                  wishes: job.wishes,
                  unmatched: job.unmatched,
                }
        }}
      </p>
    }
    @if (job.errorCode) {
      <p class="text-xs text-destructive">{{ i18n.error(job.errorCode) }}</p>
    }
    @if (job.failures.length > 0) {
      <details class="text-xs">
        <summary class="cursor-pointer text-muted-foreground">
          {{ 'arcanistImport.job.failures' | t: { count: job.filesFailed } }}
        </summary>
        <ul class="mt-1 grid gap-0.5 pl-4">
          @for (failure of job.failures; track $index) {
            <li class="break-all">
              {{ failure.path }}
              <span class="text-muted-foreground">— {{ i18n.error(failure.reason) }}</span>
            </li>
          }
        </ul>
      </details>
    }
    @if (actions()) {
      <div class="flex flex-wrap gap-2">
        @if (active()) {
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="busy()"
            (click)="act('cancel')"
          >
            {{ 'arcanistImport.job.cancel' | t }}
          </button>
        } @else {
          @if (job.canRetry) {
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              [disabled]="busy()"
              (click)="act('retry')"
            >
              {{ 'arcanistImport.job.retry' | t }}
            </button>
          }
          <button
            hlmBtn
            variant="ghost"
            size="sm"
            type="button"
            [title]="'import.migrate.removeHelp' | t"
            [disabled]="busy()"
            (click)="act('remove')"
          >
            {{ 'import.migrate.remove' | t }}
          </button>
        }
      </div>
    }
  `,
})
export class ArcanistImportJobView {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly job = input.required<ArcanistImportJob>();
  /** Cancel, retry and remove (the panel); the wizard only shows how it goes. */
  readonly actions = input(true);
  readonly changed = output<ArcanistImportJob | null>();

  protected readonly busy = signal(false);
  protected readonly active = computed(() => arcanistImportActive(this.job()));
  protected readonly percent = computed(() => arcanistImportPercent(this.job()));

  protected async act(action: 'cancel' | 'retry' | 'remove'): Promise<void> {
    const id = this.job().id;
    this.busy.set(true);
    try {
      if (action === 'remove') {
        await this.gql.mutate(DeleteArcanistImportMutation, { id });
        this.changed.emit(null);
      } else if (action === 'cancel')
        this.changed.emit(
          (await this.gql.mutate(CancelArcanistImportMutation, { id })).cancelArcanistImport,
        );
      else
        this.changed.emit(
          (await this.gql.mutate(RetryArcanistImportMutation, { id })).retryArcanistImport,
        );
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}

/** The panel's API for the form. */
export function panelArcanistApi(gql: Gql): ArcanistImportApi {
  return {
    servers: async () => (await gql.query(ArcanistServersQuery)).arcanistServers,
    analyze: async (input) =>
      (await gql.query(AnalyzeArcanistServerQuery, { input })).analyzeArcanistServer,
  };
}

/** The library folders of this server where a migration can copy music: local ones, not personal or disabled. */
export function arcanistTargets(libraries: Library[]): { id: string; label: string }[] {
  return libraries
    .filter((l) => !l.personal && !l.disabled && (l.sources ?? []).some((s) => s.kind === 'LOCAL'))
    .map((l) => ({ id: l.id, label: l.name }));
}

/** Starts a migration with the answers of a form. */
export async function startArcanistImport(
  gql: Gql,
  draft: ArcanistImportDraft,
): Promise<ArcanistImportJob> {
  return (
    await gql.mutate(StartArcanistImportMutation, {
      input: {
        source: draft.analyzed ?? draft.source(),
        libraries: draft.libraryInputs(),
        users: draft.users(),
        ...draft.options(),
      },
    })
  ).startArcanistImport;
}

/**
 * The Import page's part for other Arcanist servers: bring one into this server (a dialog), the migrations and how
 * they go, and the codes that let another Arcanist bring this one over.
 */
@Component({
  selector: 'app-arcanist-migration',
  imports: [
    NgIcon,
    BrnDialogContent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmSpinnerImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
    ArcanistImportForm,
  ],
  providers: [provideIcons({ lucideCopy, lucideKeyRound, lucideServer })],
  host: { class: 'contents' },
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'arcanistImport.codes.title' | t }}</h2>
        <p hlmCardDescription>{{ 'arcanistImport.codes.help' | t }}</p>
      </div>
      <div hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-4">
        <div>
          <button hlmBtn type="button" class="gap-2" [disabled]="creating()" (click)="createCode()">
            @if (creating()) {
              <hlm-spinner class="size-4" />
            } @else {
              <ng-icon name="lucideKeyRound" />
            }
            {{ 'arcanistImport.codes.create' | t }}
          </button>
        </div>
        @if (codes().length > 0) {
          <ul class="grid gap-2">
            @for (code of codes(); track code.id) {
              <li class="flex flex-wrap items-center gap-2 rounded-md border p-3 text-sm">
                <span hlmBadge [variant]="code.active ? 'secondary' : 'outline'">{{
                  (code.active
                    ? 'arcanistImport.codes.active'
                    : code.revokedAt
                      ? 'arcanistImport.codes.revoked'
                      : 'arcanistImport.codes.expired'
                  ) | t
                }}</span>
                <span class="text-xs text-muted-foreground" [title]="code.createdAt | dateTime">{{
                  'arcanistImport.codes.made' | t: { when: (code.createdAt | relativeTime) }
                }}</span>
                @if (code.active) {
                  <span class="text-xs text-muted-foreground">{{
                    'arcanistImport.codes.until' | t: { when: (code.expiresAt | dateTime) }
                  }}</span>
                }
                @if (code.lastUsedAt) {
                  <span class="text-xs text-muted-foreground">{{
                    'arcanistImport.codes.used'
                      | t: { when: (code.lastUsedAt | relativeTime), by: code.lastUsedBy ?? '?' }
                  }}</span>
                }
                @if (code.active) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    type="button"
                    class="ml-auto"
                    [disabled]="busy() === code.id"
                    (click)="revoke(code)"
                  >
                    {{ 'arcanistImport.codes.revoke' | t }}
                  </button>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <hlm-dialog [state]="created() ? 'open' : 'closed'" (closed)="created.set(null)">
      <hlm-dialog-content
        *brnDialogContent="let ctx"
        class="w-[min(36rem,calc(100vw-2rem))] sm:max-w-xl"
      >
        @if (created(); as code) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'arcanistImport.codes.createdTitle' | t }}</h3>
            <p hlmDialogDescription>{{ 'arcanistImport.codes.createdHelp' | t }}</p>
          </hlm-dialog-header>
          <div class="grid gap-4">
            <div class="flex items-center gap-2">
              <code
                class="flex-1 rounded-md border bg-muted px-3 py-2 text-center font-mono text-lg tracking-widest"
                >{{ code.code }}</code
              >
              <button
                hlmBtn
                variant="outline"
                size="icon"
                type="button"
                [attr.aria-label]="'common.copy' | t"
                (click)="copy(code.code)"
              >
                <ng-icon name="lucideCopy" />
              </button>
            </div>
            @if (code.addresses.length > 0) {
              <div class="grid gap-1 text-sm">
                <p class="font-medium">{{ 'arcanistImport.codes.addresses' | t }}</p>
                <ul class="grid gap-0.5 font-mono text-xs">
                  @for (address of code.addresses; track address) {
                    <li>{{ address }}</li>
                  }
                </ul>
              </div>
            }
            @if (code.p2PLink; as link) {
              <div class="grid gap-1 text-sm">
                <p class="font-medium">{{ 'arcanistImport.codes.link' | t }}</p>
                <div class="flex items-start gap-2">
                  <code class="flex-1 rounded-md border bg-muted p-2 font-mono text-xs break-all">{{
                    link
                  }}</code>
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'common.copy' | t"
                    (click)="copy(link)"
                  >
                    <ng-icon name="lucideCopy" />
                  </button>
                </div>
                <p class="text-xs text-muted-foreground">
                  {{ 'arcanistImport.codes.linkHelp' | t }}
                </p>
              </div>
            } @else {
              <p class="text-xs text-muted-foreground">{{ 'arcanistImport.codes.noLink' | t }}</p>
            }
            <p class="text-xs text-muted-foreground">
              {{ 'arcanistImport.codes.validUntil' | t: { when: (code.expiresAt | dateTime) } }}
            </p>
          </div>
          <hlm-dialog-footer>
            <button hlmBtn type="button" (click)="created.set(null)">
              {{ 'common.close' | t }}
            </button>
          </hlm-dialog-footer>
        }
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-dialog [state]="draft() ? 'open' : 'closed'" (closed)="draft.set(null)">
      <hlm-dialog-content
        *brnDialogContent="let ctx"
        class="w-[min(48rem,calc(100vw-2rem))] sm:max-w-3xl"
      >
        @if (draft(); as current) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'arcanistImport.title' | t }}</h3>
            <p hlmDialogDescription>{{ 'arcanistImport.subtitle' | t }}</p>
          </hlm-dialog-header>
          <app-arcanist-import
            [draft]="current"
            [api]="api"
            [targets]="targets()"
            [loadDirectories]="loadDirectories"
            [createDirectory]="createDirectory"
          />
          <hlm-dialog-footer>
            <button hlmBtn variant="ghost" type="button" (click)="draft.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button
              hlmBtn
              type="button"
              [disabled]="starting() || !current.analysis() || !current.complete()"
              (click)="start(current)"
            >
              @if (starting()) {
                <hlm-spinner class="size-4" />
              }
              {{ 'arcanistImport.start' | t }}
            </button>
          </hlm-dialog-footer>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ArcanistMigration implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  /** A migration started or changed: the page reloads its libraries and shows the job. */
  readonly started = output<ArcanistImportJob>();

  protected readonly codes = signal<MigrationCodeInfo[]>([]);
  protected readonly created = signal<CreatedCode['createMigrationCode'] | null>(null);
  protected readonly creating = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly draft = signal<ArcanistImportDraft | null>(null);
  protected readonly starting = signal(false);
  protected readonly targets = signal<{ id: string; label: string }[]>([]);
  protected readonly api = panelArcanistApi(this.gql);
  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  async ngOnInit(): Promise<void> {
    await this.loadCodes();
  }

  /** Opens the dialog to bring another Arcanist into this one. */
  async open(): Promise<void> {
    this.targets.set(arcanistTargets((await this.gql.query(LibrariesQuery)).libraries));
    this.draft.set(new ArcanistImportDraft());
  }

  protected async start(draft: ArcanistImportDraft): Promise<void> {
    this.starting.set(true);
    try {
      const job = await startArcanistImport(this.gql, draft);
      this.draft.set(null);
      toast.success(this.i18n.t('arcanistImport.startedToast'));
      this.started.emit(job);
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.starting.set(false);
    }
  }

  protected async createCode(): Promise<void> {
    this.creating.set(true);
    try {
      this.created.set((await this.gql.mutate(CreateMigrationCodeMutation)).createMigrationCode);
      await this.loadCodes();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.creating.set(false);
    }
  }

  protected async revoke(code: MigrationCodeInfo): Promise<void> {
    this.busy.set(code.id);
    try {
      await this.gql.mutate(RevokeMigrationCodeMutation, { id: code.id });
      await this.loadCodes();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(null);
    }
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(this.i18n.t('common.copied'));
    } catch {
      // The text is on screen to copy by hand.
    }
  }

  private async loadCodes(): Promise<void> {
    this.codes.set((await this.gql.query(MigrationCodesQuery)).migrationCodes);
  }
}

/** Lists the migrations from other Arcanist servers. */
export async function loadArcanistImports(gql: Gql): Promise<ArcanistImportJob[]> {
  return (await gql.query(ArcanistImportsQuery)).arcanistImports;
}
