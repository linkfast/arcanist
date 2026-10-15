import { Component, input, output } from '@angular/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { TranslatePipe } from '../core/i18n/i18n';

/** Asks before switching the Ultimate Cloud extension off: everything about it disappears until it is on again. */
@Component({
  selector: 'app-cloud-off-dialog',
  imports: [BrnAlertDialogContent, HlmAlertDialogImports, TranslatePipe],
  template: `
    <hlm-alert-dialog [state]="open() ? 'open' : 'closed'" (closed)="cancelled.emit()">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ 'cloud.extension.offTitle' | t }}</h3>
          <p hlmAlertDialogDescription>{{ 'cloud.extension.offConfirm' | t }}</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="cancelled.emit()">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="confirmed.emit()">
            {{ 'cloud.extension.off' | t }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class CloudOffDialog {
  readonly open = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
}
