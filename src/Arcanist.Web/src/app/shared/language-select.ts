import { Component, inject } from '@angular/core';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { I18n, Language } from '../core/i18n/i18n';

@Component({
  selector: 'app-language-select',
  imports: [HlmNativeSelectImports],
  template: `
    <hlm-native-select
      [value]="i18n.language()"
      (change)="select($event)"
      selectClass="w-36"
      aria-label="Language"
    >
      <option hlmNativeSelectOption value="es">Español</option>
      <option hlmNativeSelectOption value="en">English</option>
    </hlm-native-select>
  `,
})
export class LanguageSelect {
  protected readonly i18n = inject(I18n);

  protected select(event: Event): void {
    this.i18n.setLanguage((event.target as HTMLSelectElement).value as Language);
  }
}
