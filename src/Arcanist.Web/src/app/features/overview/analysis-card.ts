import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { AnalysisProgressFieldsFragment } from '../../graphql/generated/graphql';
import {
  AnalysisProgressChangedSubscription,
  AnalysisProgressQuery,
  SetAnalysisEnabledMutation,
} from '../../graphql/operations';
import { CountPipe } from '../../shared/format';

/** Background audio analysis: progress, speed and the switch to turn it off. */
@Component({
  selector: 'app-analysis-card',
  imports: [
    HlmCardImports,
    HlmBadgeImports,
    HlmProgressImports,
    HlmSwitchImports,
    TranslatePipe,
    CountPipe,
  ],
  template: `
    @if (progress(); as p) {
      <section hlmCard>
        <div hlmCardHeader>
          <div class="flex items-start justify-between gap-4">
            <div class="min-w-0">
              <h2 hlmCardTitle>{{ 'analysis.title' | t }}</h2>
              <p hlmCardDescription>{{ 'analysis.description' | t }}</p>
            </div>
            @if (auth.can('MANAGE_SETTINGS')) {
              <label class="flex shrink-0 items-center gap-2 text-sm">
                {{ 'analysis.enabled' | t }}
                <hlm-switch
                  [checked]="p.enabled"
                  [disabled]="saving()"
                  (checkedChange)="setEnabled($event)"
                  [aria-label]="'analysis.enabled' | t"
                />
              </label>
            }
          </div>
        </div>
        <div hlmCardContent class="space-y-3">
          <div class="flex flex-wrap items-center gap-2 text-sm">
            <span hlmBadge [variant]="p.activity === 'RUNNING' ? 'default' : 'secondary'">
              {{ 'analysis.activity.' + p.activity | t }}
            </span>
            @if (p.activity === 'RUNNING' && p.filesPerMinute > 0) {
              <span class="text-muted-foreground">{{
                'analysis.speed' | t: { count: p.filesPerMinute }
              }}</span>
            }
          </div>

          @if (total() > 0) {
            <hlm-progress [value]="percent()" class="h-2">
              <hlm-progress-indicator />
            </hlm-progress>
          }

          <dl class="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
            <div>
              <dt class="text-muted-foreground">{{ 'analysis.analyzed' | t }}</dt>
              <dd class="font-medium">{{ p.analyzed | count }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">{{ 'analysis.pending' | t }}</dt>
              <dd class="font-medium">{{ p.pending | count }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">{{ 'analysis.needsFfmpeg' | t }}</dt>
              <dd class="font-medium">{{ p.needsFfmpeg | count }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">{{ 'analysis.failed' | t }}</dt>
              <dd class="font-medium">{{ p.failed | count }}</dd>
            </div>
          </dl>

          @if (p.activity === 'UNAVAILABLE') {
            <p class="text-sm text-destructive">{{ 'analysis.unavailable' | t }}</p>
          }
          @if (p.needsFfmpeg > 0) {
            <p class="text-sm text-muted-foreground">{{ 'analysis.needsFfmpegHelp' | t }}</p>
          }
          @if (error()) {
            <p class="text-sm text-destructive">{{ error() }}</p>
          }
        </div>
      </section>
    }
  `,
})
export class AnalysisCard implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly progress = signal<AnalysisProgressFieldsFragment | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly total = computed(() => {
    const p = this.progress();
    return p ? p.analyzed + p.pending + p.failed + p.needsFfmpeg : 0;
  });

  protected readonly percent = computed(() => {
    const p = this.progress();
    const total = this.total();
    return p && total > 0 ? Math.round(((total - p.pending) / total) * 100) : 0;
  });

  async ngOnInit(): Promise<void> {
    if (!this.auth.can('MANAGE_LIBRARIES')) return;

    this.progress.set((await this.gql.query(AnalysisProgressQuery)).analysisProgress);
    this.gql
      .subscribe(AnalysisProgressChangedSubscription)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ analysisProgressChanged }) => this.progress.set(analysisProgressChanged));
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      const result = await this.gql.mutate(SetAnalysisEnabledMutation, { enabled });
      this.progress.set(result.setAnalysisEnabled);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.saving.set(false);
    }
  }
}
