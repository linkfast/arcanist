import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { ApiError, RatingMode } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { RatingModeQuery, SetRatingModeMutation } from '../../graphql/operations';

/** How users rate tracks on this server: five stars or a single star. */
@Component({
  selector: 'app-rating-mode-card',
  imports: [FormsModule, HlmCardImports, HlmRadioGroupImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.ratings.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.ratings.description' | t }}</p>
      </div>
      @if (mode(); as current) {
        <div hlmCardContent class="grid gap-3">
          <hlm-radio-group
            class="grid gap-2 sm:grid-cols-2"
            name="ratingMode"
            [disabled]="busy()"
            [ngModel]="current"
            (ngModelChange)="change($event)"
          >
            <label class="flex items-center gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio value="STARS"><hlm-radio-indicator indicator /></hlm-radio>
              <span>★★★★★ {{ 'settings.ratings.stars' | t }}</span>
            </label>
            <label class="flex items-center gap-3 rounded-lg border p-3 text-sm">
              <hlm-radio value="STAR"><hlm-radio-indicator indicator /></hlm-radio>
              <span>★ {{ 'settings.ratings.star' | t }}</span>
            </label>
          </hlm-radio-group>
          <p class="text-xs text-muted-foreground">{{ 'settings.ratings.help' | t }}</p>
        </div>
      }
    </section>
  `,
})
export class RatingModeCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly mode = signal<RatingMode | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.mode.set((await this.gql.query(RatingModeQuery)).serverInfo.ratingMode);
  }

  protected async change(mode: RatingMode): Promise<void> {
    if (mode === this.mode()) return;
    const previous = this.mode();
    this.mode.set(mode);
    this.busy.set(true);
    try {
      await this.gql.mutate(SetRatingModeMutation, { mode });
      toast.success(this.i18n.t('settings.ratings.saved'));
    } catch (error) {
      this.mode.set(previous);
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
