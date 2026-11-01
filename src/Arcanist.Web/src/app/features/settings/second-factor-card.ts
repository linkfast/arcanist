import { Component, OnInit, inject, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { SecondFactorPolicyQuery as PolicyResult } from '../../graphql/generated/graphql';
import { SecondFactorPolicyQuery, SetSecondFactorPolicyMutation } from '../../graphql/operations';

type Policy = PolicyResult['secondFactorPolicy'];

/** Whether everyone signs in with a second factor: who has none pairs one at their next sign-in. */
@Component({
  selector: 'app-second-factor-card',
  imports: [HlmBadgeImports, HlmCardImports, HlmSwitchImports, TranslatePipe],
  template: `
    <section hlmCard data-tour="settings-second-factor">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'secondFactor.policy.title' | t }}</h2>
        <p hlmCardDescription>{{ 'secondFactor.policy.description' | t }}</p>
      </div>
      @if (policy(); as p) {
        <div hlmCardContent class="grid gap-3 text-sm">
          <label class="flex items-start gap-3">
            <hlm-switch
              class="mt-0.5"
              [checked]="p.required"
              [disabled]="busy() || p.setByEnvironment"
              (checkedChange)="set($event)"
            />
            <span class="grid gap-0.5">
              <span class="flex flex-wrap items-center gap-2 font-medium">
                {{ 'secondFactor.policy.switch' | t }}
                <span hlmBadge variant="secondary">{{ 'setup.database.recommended' | t }}</span>
              </span>
              <span class="text-xs text-muted-foreground">{{
                'secondFactor.policy.help' | t
              }}</span>
              @if (p.setByEnvironment) {
                <span class="text-xs text-muted-foreground">{{
                  'settings.openSource.byEnvironment' | t
                }}</span>
              }
            </span>
          </label>
          @if (p.usersWithout > 0) {
            <p class="text-xs text-muted-foreground">
              {{ 'secondFactor.policy.usersWithout' | t: { count: p.usersWithout } }}
            </p>
          }
        </div>
      }
    </section>
  `,
})
export class SecondFactorCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly policy = signal<Policy | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.policy.set((await this.gql.query(SecondFactorPolicyQuery)).secondFactorPolicy);
  }

  protected async set(required: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const { setSecondFactorPolicy } = await this.gql.mutate(SetSecondFactorPolicyMutation, {
        required,
      });
      this.policy.set(setSecondFactorPolicy);
      toast.success(this.i18n.t('common.saved'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      // The switch moved by itself: redrawn, it goes back to what the server has.
      const current = this.policy();
      this.policy.set(null);
      await new Promise((resolve) => setTimeout(resolve));
      this.policy.set(current);
    } finally {
      this.busy.set(false);
    }
  }
}
