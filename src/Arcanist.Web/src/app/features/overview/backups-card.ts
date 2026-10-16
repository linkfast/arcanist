import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { Gql } from '../../core/graphql/gql.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import type { BackupsOverviewQuery } from '../../graphql/generated/graphql';
import { BackupsOverviewQuery as OverviewQuery } from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { backupRunVariant } from '../backups/backup-jobs';

type Backup = BackupsOverviewQuery['backups'][number];

/** Each backup's latest run and next one, for administrators. */
@Component({
  selector: 'app-backups-card',
  imports: [
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    @if (backups(); as list) {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'backups.overview.title' | t }}</h2>
          <p hlmCardDescription>
            {{ (list.length ? 'backups.overview.description' : 'backups.overview.none') | t }}
          </p>
          <div hlmCardAction>
            <a hlmBtn variant="outline" size="sm" routerLink="/backups">{{
              'backups.overview.open' | t
            }}</a>
          </div>
        </div>
        @if (list.length > 0) {
          <ul hlmCardContent class="grid gap-2">
            @for (backup of list; track backup.id) {
              <li class="flex flex-wrap items-center gap-2 text-sm">
                <span class="min-w-0 truncate font-medium">{{ backup.name }}</span>
                @if (backup.lastRun; as run) {
                  <span hlmBadge [variant]="variant(backup)">{{
                    'backups.runStatus.' + run.status | t
                  }}</span>
                  @if (run.finishedAt) {
                    <span
                      class="text-xs text-muted-foreground"
                      [title]="run.finishedAt | dateTime"
                      >{{ run.finishedAt | relativeTime }}</span
                    >
                  }
                } @else {
                  <span hlmBadge variant="outline">{{ 'backups.list.neverRun' | t }}</span>
                }
                <span class="ml-auto text-xs text-muted-foreground">
                  @if (!backup.enabled) {
                    {{ 'backups.list.paused' | t }}
                  } @else if (backup.nextRunAt) {
                    <span [title]="backup.nextRunAt | dateTime">{{
                      'backups.list.next' | t: { when: (backup.nextRunAt | relativeTime) }
                    }}</span>
                  } @else {
                    {{ 'backups.schedule.summary.manual' | t }}
                  }
                </span>
              </li>
            }
          </ul>
        }
      </section>
    }
  `,
})
export class BackupsCard implements OnInit {
  private readonly gql = inject(Gql);

  protected readonly backups = signal<Backup[] | null>(null);

  protected variant(backup: Backup) {
    return backup.lastRun ? backupRunVariant(backup.lastRun.status) : 'outline';
  }

  async ngOnInit(): Promise<void> {
    try {
      this.backups.set((await this.gql.query(OverviewQuery)).backups);
    } catch {
      // The overview shows without it.
    }
  }
}
