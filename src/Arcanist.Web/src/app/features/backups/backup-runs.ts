import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { BackupJobFieldsFragment as BackupJob } from '../../graphql/generated/graphql';
import { BackupRunsQuery } from '../../graphql/operations';
import { BytesPipe, CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { backupRunVariant } from './backup-jobs';

/** The latest runs of a backup: when, what they copied and what failed. */
@Component({
  selector: 'app-backup-runs',
  imports: [
    HlmBadgeImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  host: { class: 'grid w-full min-w-0 grid-cols-[minmax(0,1fr)] gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ 'backups.runs.title' | t: { name: name() } }}</h3>
      <p hlmDialogDescription>{{ 'backups.runs.description' | t }}</p>
    </hlm-dialog-header>

    @if (runs() === null) {
      <div class="flex justify-center py-8"><hlm-spinner /></div>
    } @else {
      <ul class="grid gap-3">
        @for (run of runs(); track run.id) {
          <li class="grid gap-2 rounded-lg border p-3 text-sm">
            <div class="flex flex-wrap items-center gap-2">
              <span hlmBadge [variant]="variant(run)">{{
                'backups.runStatus.' + run.status | t
              }}</span>
              <span class="text-muted-foreground">{{
                'backups.runs.trigger.' + run.trigger | t
              }}</span>
              <span
                class="ml-auto text-xs text-muted-foreground"
                [title]="run.requestedAt | dateTime"
              >
                {{ run.requestedAt | relativeTime }}
              </span>
            </div>
            <dl class="grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-3">
              @if (run.startedAt) {
                <div>
                  <dt class="text-muted-foreground">{{ 'backups.runs.started' | t }}</dt>
                  <dd>{{ run.startedAt | dateTime }}</dd>
                </div>
              }
              @if (run.finishedAt) {
                <div>
                  <dt class="text-muted-foreground">{{ 'backups.runs.finished' | t }}</dt>
                  <dd>{{ run.finishedAt | dateTime }}</dd>
                </div>
              }
              @if (run.snapshot) {
                <div>
                  <dt class="text-muted-foreground">{{ 'backups.runs.snapshot' | t }}</dt>
                  <dd class="truncate font-mono" [title]="run.snapshot">{{ run.snapshot }}</dd>
                </div>
              }
              <div>
                <dt class="text-muted-foreground">{{ 'backups.counters.files' | t }}</dt>
                <dd>{{ run.files | count }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'backups.counters.copied' | t }}</dt>
                <dd>{{ run.copied | count }} · {{ run.bytes | bytes }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'backups.counters.unchanged' | t }}</dt>
                <dd>{{ run.unchanged | count }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'backups.counters.versioned' | t }}</dt>
                <dd>{{ run.versioned | count }}</dd>
              </div>
              @if (run.skipped > 0) {
                <div>
                  <dt class="text-muted-foreground">{{ 'backups.counters.skipped' | t }}</dt>
                  <dd>{{ run.skipped | count }}</dd>
                </div>
              }
              @if (run.failed > 0) {
                <div>
                  <dt class="text-muted-foreground">{{ 'backups.counters.failed' | t }}</dt>
                  <dd class="text-destructive">{{ run.failed | count }}</dd>
                </div>
              }
            </dl>
            @if (run.errorCode) {
              <p class="text-xs text-destructive">{{ i18n.error(run.errorCode) }}</p>
            }
            @if (run.failures.length > 0) {
              <details class="text-xs">
                <summary class="cursor-pointer text-muted-foreground">
                  {{ 'backups.failures' | t: { count: run.failures.length } }}
                </summary>
                <ul class="mt-2 grid gap-1">
                  @for (failure of run.failures; track $index) {
                    <li class="grid gap-0.5 rounded bg-muted px-2 py-1">
                      <span class="truncate font-mono" [title]="failure.path">{{
                        failure.path
                      }}</span>
                      <span class="text-destructive">{{ i18n.error(failure.code) }}</span>
                    </li>
                  }
                </ul>
              </details>
            }
          </li>
        } @empty {
          <li class="text-sm text-muted-foreground">{{ 'backups.runs.empty' | t }}</li>
        }
      </ul>
    }

    <hlm-dialog-footer>
      <button hlmBtn variant="outline" type="button" (click)="closed.emit()">
        {{ 'common.close' | t }}
      </button>
    </hlm-dialog-footer>
  `,
})
export class BackupRuns implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  readonly backupId = input.required<string>();
  readonly name = input.required<string>();
  /** The backup's latest run as the page follows it live, merged into the list. */
  readonly latest = input<BackupJob | null>(null);
  readonly closed = output<void>();

  private readonly loaded = signal<BackupJob[] | null>(null);
  protected readonly runs = computed(() => {
    const runs = this.loaded();
    const latest = this.latest();
    if (!runs || !latest || latest.backupId !== this.backupId()) return runs;
    return runs.some((r) => r.id === latest.id)
      ? runs.map((r) => (r.id === latest.id ? latest : r))
      : [latest, ...runs];
  });
  protected readonly variant = (run: BackupJob) => backupRunVariant(run.status);

  async ngOnInit(): Promise<void> {
    try {
      this.loaded.set(
        (await this.gql.query(BackupRunsQuery, { backupId: this.backupId() })).backupRuns,
      );
    } catch {
      this.loaded.set([]);
    }
  }
}
