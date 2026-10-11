import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToasterImports } from '@spartan-ng/helm/sonner';
import { ReauthDialog } from './shared/reauth-dialog';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToasterImports, ReauthDialog],
  template: `
    <router-outlet />
    <hlm-toaster position="bottom-right" />
    <app-reauth-dialog />
  `,
})
export class App {}
