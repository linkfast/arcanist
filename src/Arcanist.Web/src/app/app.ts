import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToasterImports } from '@spartan-ng/helm/sonner';
import { Theme } from './core/theme/theme';
import { ReauthDialog } from './shared/reauth-dialog';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToasterImports, ReauthDialog],
  template: `
    <router-outlet />
    <hlm-toaster position="bottom-right" [theme]="theme.dark() ? 'dark' : 'light'" />
    <app-reauth-dialog />
  `,
})
export class App {
  protected readonly theme = inject(Theme);
}
