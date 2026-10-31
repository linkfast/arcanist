import { Component, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCloud, lucideRefreshCw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  CloudSyncMode,
  CloudSyncStateFieldsFragment as SyncState,
} from '../../graphql/generated/graphql';

/** An account a library can sync with: the server's (no userId) or the administrator's own. */
export interface CloudAccount {
  id: string;
  name?: string | null;
  ownerFirstName?: string | null;
  userId?: string | null;
}
import {
  SetLibraryCloudSyncMutation,
  SyncLibraryWithCloudMutation,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';

/**
 * How a library syncs with the Ultimate Cloud account: the mode, where downloads go, and how the last run went.
 * Administrators only; the Cloud plan is needed to turn it on.
 */
@Component({
  selector: 'app-cloud-sync',
  imports: [
    NgIcon,
    RouterLink,
    HlmButtonImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSpinnerImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  providers: [provideIcons({ lucideCloud, lucideRefreshCw })],
  host: { class: 'block' },
  template: `
    <div class="grid gap-2 rounded-md border p-3 text-sm">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <span class="flex items-center gap-2 font-medium">
          <ng-icon name="lucideCloud" class="text-muted-foreground" />
          {{ 'cloudSync.title' | t }}
        </span>
        <hlm-native-select
          size="sm"
          class="w-56"
          [value]="mode()"
          [disabled]="busy() || (!available() && mode() === 'OFF') || !!plusMissing()"
          (change)="setMode($any($event.target).value)"
        >
          @for (option of modes; track option) {
            <option hlmNativeSelectOption [value]="option">
              {{ 'cloudSync.mode.' + option | t }}
            </option>
          }
        </hlm-native-select>
      </div>

      @if (accounts().length > 1) {
        <label class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          {{ 'cloudSync.account' | t }}
          <hlm-native-select
            size="sm"
            [value]="account() ?? ''"
            [disabled]="busy()"
            (change)="setAccount($any($event.target).value)"
          >
            @for (option of accounts(); track option.id) {
              <option hlmNativeSelectOption [value]="option.userId ?? ''">
                {{ accountName(option) }}
              </option>
            }
          </hlm-native-select>
        </label>
      }

      @if (plusMissing(); as plus) {
        <p class="text-xs text-muted-foreground">
          {{ 'cloudSync.needsPlus' | t }}
          <a
            class="underline underline-offset-2"
            routerLink="/extensions"
            [queryParams]="{ install: plus }"
            >{{ 'cloud.plus.install' | t }}</a
          >
        </p>
      } @else if (!available() && mode() === 'OFF') {
        <p class="text-xs text-muted-foreground">{{ 'cloudSync.needsPlan' | t }}</p>
      } @else if (mode() !== 'OFF') {
        @if ((mode() === 'DOWNLOAD' || mode() === 'BOTH') && sources().length > 1) {
          <label class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            {{ 'cloudSync.downloadTo' | t }}
            <hlm-native-select
              size="sm"
              [value]="state()?.downloadSourceId ?? sources()[0].id"
              (change)="setMode(mode(), $any($event.target).value)"
            >
              @for (source of sources(); track source.id) {
                <option hlmNativeSelectOption [value]="source.id">{{ source.location }}</option>
              }
            </hlm-native-select>
          </label>
        }

        @if (state(); as current) {
          @if (current.running; as run) {
            <p class="flex items-center gap-2 text-xs text-muted-foreground">
              <hlm-spinner class="size-3" />
              {{ 'cloudSync.phase.' + run.phase | t }}
              @if (run.total > 0) {
                ({{ run.done }} / {{ run.total }})
              }
            </p>
            @if (run.total > 0) {
              <hlm-progress [value]="(run.done * 100) / run.total"
                ><hlm-progress-indicator
              /></hlm-progress>
            }
          } @else if (current.requested) {
            <p class="flex items-center gap-2 text-xs text-muted-foreground">
              <hlm-spinner class="size-3" />{{ 'cloudSync.queued' | t }}
            </p>
          }
          <p class="text-xs text-muted-foreground">
            @if (current.lastRunAt) {
              <span [title]="current.lastRunAt | dateTime">{{
                'cloudSync.lastRun' | t: { when: (current.lastRunAt | relativeTime) }
              }}</span>
              ·
            }
            {{
              'cloudSync.counts'
                | t
                  : {
                      matched: current.matched,
                      uploaded: current.uploaded,
                      downloaded: current.downloaded,
                      localOnly: current.localOnly,
                    }
            }}
          </p>
          @if (current.errorCode) {
            <p class="text-xs text-destructive">{{ i18n.error(current.errorCode) }}</p>
          }
          @if (current.failures.length > 0) {
            <details class="text-xs">
              <summary class="cursor-pointer text-destructive">
                {{ 'cloudSync.failures' | t: { count: current.failed } }}
              </summary>
              <ul class="mt-1 grid gap-0.5 pl-4">
                @for (failure of current.failures; track $index) {
                  <li>
                    {{ failure.title }}
                    <span class="text-muted-foreground">— {{ reason(failure.reason) }}</span>
                  </li>
                }
              </ul>
            </details>
          }
        }
        <div class="flex flex-wrap items-center justify-between gap-2">
          <p class="text-xs text-muted-foreground">{{ 'cloudSync.nothingDeleted' | t }}</p>
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            class="gap-1"
            [disabled]="busy() || !!state()?.running || !!state()?.requested"
            (click)="syncNow()"
          >
            <ng-icon name="lucideRefreshCw" />
            {{ 'cloudSync.now' | t }}
          </button>
        </div>
      }
    </div>
  `,
})
export class CloudSyncPanel {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly libraryId = input.required<string>();
  readonly sources = input.required<{ id: string; location: string }[]>();
  readonly state = input<SyncState | undefined>();
  /** The account has the Cloud plan. */
  readonly available = input(false);
  /** Ultimate Cloud Plus's id while it is not running: syncing is its, so nothing syncs. */
  readonly plusMissing = input<string | null>(null);
  readonly accounts = input<CloudAccount[]>([]);
  /** Chosen while the sync is off, for when it is turned on. */
  private readonly chosen = signal<string | null | undefined>(undefined);
  readonly changed = output<SyncState>();

  protected readonly modes: CloudSyncMode[] = ['OFF', 'UPLOAD', 'DOWNLOAD', 'BOTH'];
  protected readonly busy = signal(false);

  protected mode(): CloudSyncMode {
    return this.state()?.mode ?? 'OFF';
  }

  /** The account's user (null: the server's): the one it syncs with, or the first linked. */
  protected account(): string | null {
    const state = this.state();
    if (state && state.mode !== 'OFF') return state.accountUserId ?? null;
    const chosen = this.chosen();
    return chosen !== undefined ? chosen : (this.accounts()[0]?.userId ?? null);
  }

  /** "Ultimate Cloud de Ana" for the server's account, "Mi Ultimate Cloud (ana)" for the administrator's own. */
  protected accountName(account: CloudAccount): string {
    const name = account.name ?? account.id;
    return account.userId
      ? this.i18n.t('cloudSync.ownAccount', { name })
      : account.ownerFirstName
        ? this.i18n.t('cloudSync.serverAccount', { owner: account.ownerFirstName })
        : name;
  }

  protected async setAccount(value: string): Promise<void> {
    const user = value || null;
    if (this.mode() === 'OFF') {
      this.chosen.set(user);
      return;
    }
    await this.setMode(this.mode(), undefined, user);
  }

  protected reason(code: string): string {
    return code === 'tool:hash_changed'
      ? this.i18n.t('cloudSync.cannotStrip')
      : code.startsWith('tool:')
        ? this.i18n.t('cloudSync.toolError')
        : this.i18n.error(code);
  }

  protected async setMode(
    mode: CloudSyncMode,
    downloadSourceId?: string,
    accountUserId: string | null = this.account(),
  ): Promise<void> {
    await this.run(
      async () =>
        (
          await this.gql.mutate(SetLibraryCloudSyncMutation, {
            libraryId: this.libraryId(),
            input: {
              mode,
              downloadSourceId: downloadSourceId ?? this.state()?.downloadSourceId ?? null,
              accountUserId,
            },
          })
        ).setLibraryCloudSync,
    );
  }

  protected async syncNow(): Promise<void> {
    await this.run(
      async () =>
        (await this.gql.mutate(SyncLibraryWithCloudMutation, { libraryId: this.libraryId() }))
          .syncLibraryWithCloud,
    );
  }

  private async run(action: () => Promise<SyncState>): Promise<void> {
    this.busy.set(true);
    try {
      this.changed.emit(await action());
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}
