import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { TranslatePipe } from '../../core/i18n/i18n';
import type { PeerRulesInput } from '../../graphql/generated/graphql';

/** What a peer may do, as the form edits it: empty texts are "no limit". */
export interface RulesForm {
  allowDownload: boolean;
  compressedOnly: boolean;
  maxBitRate: number;
  bandwidthKbps: string;
  maxStreams: string;
}

export const BitRates = [96, 128, 160, 192, 256, 320];

export function rulesForm(rules?: {
  allowDownload: boolean;
  maxBitRate?: number | null;
  bandwidthKbps?: number | null;
  maxStreams?: number | null;
}): RulesForm {
  return {
    allowDownload: rules?.allowDownload ?? false,
    compressedOnly: rules?.maxBitRate != null,
    maxBitRate: rules?.maxBitRate ?? 192,
    bandwidthKbps: rules?.bandwidthKbps?.toString() ?? '',
    maxStreams: rules?.maxStreams?.toString() ?? '',
  };
}

export function rulesInput(form: RulesForm): PeerRulesInput {
  const limit = (text: string) => {
    const value = Number(text.trim());
    return text.trim() && Number.isInteger(value) && value > 0 ? value : null;
  };
  return {
    allowDownload: form.allowDownload,
    maxBitRate: form.compressedOnly ? form.maxBitRate : null,
    bandwidthKbps: limit(form.bandwidthKbps),
    maxStreams: limit(form.maxStreams),
  };
}

/** The rules of a peer: downloads, "compressed only" with a bit rate, bandwidth and simultaneous streams. */
@Component({
  selector: 'app-peer-rules',
  imports: [
    FormsModule,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  template: `
    <div class="grid gap-3">
      <label class="flex items-start justify-between gap-3 text-sm">
        <span>
          {{ 'network.rules.allowDownload' | t }}
          <span class="block text-xs text-muted-foreground">{{
            'network.rules.allowDownloadHelp' | t
          }}</span>
        </span>
        <hlm-switch
          [checked]="form().allowDownload"
          (checkedChange)="form().allowDownload = $event"
        />
      </label>
      <label class="flex items-start justify-between gap-3 text-sm">
        <span>
          {{ 'network.rules.compressedOnly' | t }}
          <span class="block text-xs text-muted-foreground">{{
            'network.rules.compressedOnlyHelp' | t
          }}</span>
        </span>
        <hlm-switch
          [checked]="form().compressedOnly"
          (checkedChange)="form().compressedOnly = $event"
        />
      </label>
      @if (form().compressedOnly) {
        <hlm-field>
          <label hlmFieldLabel [for]="id() + '-bitrate'">{{
            'network.rules.maxBitRate' | t
          }}</label>
          <hlm-native-select
            [id]="id() + '-bitrate'"
            [value]="form().maxBitRate.toString()"
            (change)="form().maxBitRate = +$any($event.target).value"
          >
            @for (rate of bitRates; track rate) {
              <option hlmNativeSelectOption [value]="rate">{{ rate }} kbit/s</option>
            }
          </hlm-native-select>
        </hlm-field>
      }
      <div class="grid gap-3 sm:grid-cols-2">
        <hlm-field>
          <label hlmFieldLabel [for]="id() + '-bandwidth'">{{
            'network.rules.bandwidth' | t
          }}</label>
          <input
            hlmInput
            [id]="id() + '-bandwidth'"
            inputmode="numeric"
            [placeholder]="'network.noLimit' | t"
            [(ngModel)]="form().bandwidthKbps"
          />
        </hlm-field>
        <hlm-field>
          <label hlmFieldLabel [for]="id() + '-streams'">{{
            'network.rules.maxStreams' | t
          }}</label>
          <input
            hlmInput
            [id]="id() + '-streams'"
            inputmode="numeric"
            [placeholder]="'network.noLimit' | t"
            [(ngModel)]="form().maxStreams"
          />
        </hlm-field>
      </div>
    </div>
  `,
})
export class PeerRules {
  readonly form = input.required<RulesForm>();
  /** Prefix for the ids of the fields, unique in the page. */
  readonly id = input('rules');
  protected readonly bitRates = BitRates;
}
