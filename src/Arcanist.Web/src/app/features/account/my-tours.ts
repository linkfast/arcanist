import { Component, inject } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { TourService } from '../../core/tour/tour.service';

/** The panel's tours: on or off in this browser, and all of them again from the start. */
@Component({
  selector: 'app-my-tours',
  imports: [HlmButtonImports, HlmCardImports, HlmSwitchImports, TranslatePipe],
  template: `
    <section hlmCard data-tour="account-tours">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'tour.card.title' | t }}</h2>
        <p hlmCardDescription>{{ 'tour.card.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid gap-3">
        <label class="flex items-center gap-2 text-sm">
          <hlm-switch
            [checked]="!tours.disabled()"
            (checkedChange)="$event ? tours.enable() : tours.disableAll()"
          />
          {{ 'tour.card.enabled' | t }}
        </label>
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          class="justify-self-start"
          (click)="again()"
        >
          {{ 'tour.card.again' | t }}
        </button>
      </div>
    </section>
  `,
})
export class MyTours {
  protected readonly tours = inject(TourService);
  private readonly i18n = inject(I18n);

  protected again(): void {
    this.tours.enable(true);
    toast.success(this.i18n.t('tour.card.againDone'));
  }
}
