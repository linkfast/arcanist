import { Component, computed, inject, input } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import qrcode from 'qrcode-generator';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import { DateTimePipe, RelativeTimePipe } from './format';

/** The dark modules of a QR code as one SVG path, so it is drawn without injecting markup. */
export function qrPath(text: string): { size: number; path: string } {
  const qr = qrcode(0, 'M');
  qr.addData(text, 'Byte');
  qr.make();
  const size = qr.getModuleCount();
  let path = '';
  for (let row = 0; row < size; row++)
    for (let col = 0; col < size; col++) if (qr.isDark(row, col)) path += `M${col} ${row}h1v1h-1z`;
  return { size, path };
}

/**
 * An invitation of the P2P network: its QR code, for an app to scan, and the link, to copy and send (to another
 * server, or to a device that cannot scan).
 */
@Component({
  selector: 'app-invitation',
  imports: [HlmButtonImports, HlmInputImports, TranslatePipe, DateTimePipe, RelativeTimePipe],
  template: `
    <div class="grid justify-items-center gap-3">
      @if (showQr()) {
        <svg
          class="size-64 rounded-md bg-white p-2"
          role="img"
          [attr.aria-label]="'invitation.qr' | t"
          [attr.viewBox]="'0 0 ' + qr().size + ' ' + qr().size"
          shape-rendering="crispEdges"
        >
          <path [attr.d]="qr().path" fill="#000" />
        </svg>
      }
      <div class="flex w-full gap-2">
        <input
          hlmInput
          readonly
          class="min-w-0 flex-1 font-mono text-xs"
          [attr.aria-label]="'invitation.link' | t"
          [value]="link()"
        />
        <button hlmBtn variant="outline" type="button" (click)="copy()">
          {{ 'invitation.copy' | t }}
        </button>
      </div>
      <p class="text-xs text-muted-foreground" [title]="expiresAt() | dateTime">
        {{ 'invitation.expires' | t: { when: (expiresAt() | relativeTime) } }}
      </p>
    </div>
  `,
})
export class Invitation {
  private readonly i18n = inject(I18n);

  readonly link = input.required<string>();
  readonly expiresAt = input.required<string>();
  /** Off for invitations meant for other servers, which are pasted, not scanned. */
  readonly showQr = input(true);

  protected readonly qr = computed(() => qrPath(this.link()));

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link());
      toast.success(this.i18n.t('invitation.copied'));
    } catch {
      toast.error(this.i18n.t('invitation.copyFailed'));
    }
  }
}
