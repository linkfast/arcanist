import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  DatabaseStatusFieldsFragment as Status,
  PreviousSqliteAction,
} from '../../graphql/generated/graphql';
import {
  CreateServerDirectoryMutation,
  DatabaseQuery,
  MigrateToPostgresMutation,
  ResolvePreviousSqliteMutation,
  ServerDirectoriesQuery,
  TestPostgresConnectionMutation,
} from '../../graphql/operations';
import { DirectoryCreator, DirectoryLoader, FolderPicker } from '../../shared/folder-picker';
import { BytesPipe, CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';

/** What anyone can know while the server is in maintenance (REST, also during it). */
interface MaintenanceState {
  active: boolean;
  phase: string | null;
  percent: number | null;
}

/**
 * The server's database: what it is, moving from SQLite to PostgreSQL (with the server in maintenance), and what to
 * do with the SQLite file afterwards. Administrators only.
 */
@Component({
  selector: 'app-database-page',
  imports: [
    FormsModule,
    BrnAlertDialogContent,
    BrnDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmProgressImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    DateTimePipe,
    RelativeTimePipe,
    FolderPicker,
  ],
  templateUrl: './database.page.html',
})
export class DatabasePage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly status = signal<Status | null>(null);
  protected readonly maintenance = signal<MaintenanceState | null>(null);
  protected readonly busy = signal(false);
  protected readonly test = signal<{ success: boolean; error: string | null } | null>(null);
  protected readonly confirming = signal(false);
  protected readonly picking = signal(false);
  protected connectionString = '';
  protected action: PreviousSqliteAction = 'KEEP';
  protected folder = '';

  protected readonly migration = computed(() => this.status()?.migration ?? null);
  protected readonly percent = computed(() => {
    const migration = this.migration();
    const live = this.maintenance();
    if (live?.active) return live.percent;
    return migration && migration.rows > 0
      ? Math.round((migration.copied * 100) / migration.rows)
      : null;
  });

  /** The migration's phase as the maintenance state names it, before maintenance starts or once it ended. */
  protected readonly phaseKey = computed(() => {
    switch (this.migration()?.phase) {
      case 'CREATING_SCHEMA':
        return 'schema';
      case 'COPYING':
        return 'copying';
      case 'VERIFYING':
        return 'verifying';
      case 'SWITCHING':
        return 'switching';
      default:
        return 'pausing';
    }
  });

  protected readonly loadDirectories: DirectoryLoader = async (path) =>
    (await this.gql.query(ServerDirectoriesQuery, { path })).serverDirectories;
  protected readonly createDirectory: DirectoryCreator = async (parent, name) =>
    (await this.gql.mutate(CreateServerDirectoryMutation, { input: { parent, name } }))
      .createServerDirectory;

  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    this.destroyRef.onDestroy(() => clearInterval(this.timer));
  }

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  /** The maintenance state first (it answers during maintenance), then the details when it is over. */
  private async refresh(): Promise<void> {
    try {
      const state = await firstValueFrom(this.http.get<MaintenanceState>('/rest/v1/maintenance'));
      this.maintenance.set(state);
      if (state.active) {
        this.follow();
        return;
      }
      this.status.set((await this.gql.query(DatabaseQuery)).database);
      if (this.migration()?.phase === 'COMPLETED' || this.migration()?.phase === 'FAILED')
        clearInterval(this.timer);
      else if (this.migration()) this.follow();
    } catch {
      // Retried on the next tick while following.
    }
  }

  private follow(): void {
    if (this.timer) return;
    this.timer = setInterval(async () => {
      await this.refresh();
      const phase = this.migration()?.phase;
      if (!this.maintenance()?.active && (phase === 'COMPLETED' || phase === 'FAILED')) {
        clearInterval(this.timer);
        this.timer = undefined;
        if (phase === 'COMPLETED') toast.success(this.i18n.t('database.migrate.done'));
      }
    }, 1000);
  }

  protected async testConnection(): Promise<void> {
    this.busy.set(true);
    this.test.set(null);
    try {
      this.test.set(
        (
          await this.gql.mutate(TestPostgresConnectionMutation, {
            connectionString: this.connectionString.trim(),
          })
        ).testPostgresConnection,
      );
    } catch (error) {
      this.test.set({ success: false, error: this.i18n.error(ApiError.from(error).code) });
    } finally {
      this.busy.set(false);
    }
  }

  protected async migrate(): Promise<void> {
    this.confirming.set(false);
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(MigrateToPostgresMutation, {
        connectionString: this.connectionString.trim(),
      });
      this.status.set(result.migrateToPostgres);
      this.connectionString = '';
      this.test.set(null);
      this.follow();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  protected picked(path: string): void {
    this.folder = path;
    this.picking.set(false);
  }

  protected async resolve(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(ResolvePreviousSqliteMutation, {
        input: { action: this.action, folder: this.action === 'MOVE' ? this.folder.trim() : null },
      });
      this.status.set(result.resolvePreviousSqlite);
      toast.success(this.i18n.t('database.previous.done.' + this.action));
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}
