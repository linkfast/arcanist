import { Component, inject } from '@angular/core';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { TranslatePipe } from '../core/i18n/i18n';
import { Theme, ThemePreference } from '../core/theme/theme';

@Component({
  selector: 'app-theme-select',
  imports: [HlmNativeSelectImports, TranslatePipe],
  template: `
    <hlm-native-select
      [value]="theme.preference()"
      (change)="select($event)"
      selectClass="w-36"
      [attr.aria-label]="'theme.label' | t"
    >
      @for (preference of theme.preferences; track preference) {
        <option hlmNativeSelectOption [value]="preference">
          {{ 'theme.' + preference | t }}
        </option>
      }
    </hlm-native-select>
  `,
})
export class ThemeSelect {
  protected readonly theme = inject(Theme);

  protected select(event: Event): void {
    this.theme.set((event.target as HTMLSelectElement).value as ThemePreference);
  }
}
