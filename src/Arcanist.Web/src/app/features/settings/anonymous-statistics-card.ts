import { Component, OnInit, inject, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { AnonymousStatisticsQuery, SetAnonymousStatisticsMutation } from '../../graphql/operations';

/**
 * Anonymous statistics: which songs this server plays, without saying who, for Ultimate Cloud's global rankings. Asked
 * by the setup wizard; it needs no account (unlike the account's own services, on the Ultimate Cloud page).
 */
@Component({
  selector: 'app-anonymous-statistics-card',
  imports: [HlmCardImports, HlmSwitchImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.statistics.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.statistics.description' | t }}</p>
      </div>
      @if (enabled(); as state) {
        <div hlmCardContent class="grid gap-2">
          <label class="flex items-center gap-2 text-sm">
            <hlm-switch [checked]="state.on" [disabled]="busy()" (checkedChange)="change($event)" />
            {{ 'settings.statistics.share' | t }}
          </label>
          <p class="text-xs text-muted-foreground">{{ 'settings.statistics.help' | t }}</p>
        </div>
      }
    </section>
  `,
})
export class AnonymousStatisticsCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly enabled = signal<{ on: boolean } | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.enabled.set({ on: (await this.gql.query(AnonymousStatisticsQuery)).anonymousStatistics });
  }

  protected async change(on: boolean): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(SetAnonymousStatisticsMutation, { enabled: on });
      this.enabled.set({ on });
      toast.success(this.i18n.t('settings.statistics.saved'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.enabled.set({ on: !on });
    } finally {
      this.busy.set(false);
    }
  }
}
