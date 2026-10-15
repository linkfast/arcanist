import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { LanguageSelect } from '../../shared/language-select';
import { ThemeSelect } from '../../shared/theme-select';

@Component({
  selector: 'app-login-page',
  imports: [
    FormsModule,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmButtonImports,
    HlmAlertImports,
    TranslatePipe,
    LanguageSelect,
    ThemeSelect,
  ],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-muted/40 p-4">
      <section hlmCard class="w-full max-w-sm">
        <div hlmCardHeader>
          <h1 hlmCardTitle class="text-xl">Arcanist</h1>
          <p hlmCardDescription>{{ 'login.subtitle' | t }}</p>
        </div>
        <form hlmCardContent (ngSubmit)="submit()" class="grid gap-4">
          @if (error()) {
            <div hlmAlert variant="destructive">
              <p hlmAlertDescription>{{ error() }}</p>
            </div>
          }
          <hlm-field>
            <label hlmFieldLabel for="userName">{{ 'common.userName' | t }}</label>
            <input
              hlmInput
              id="userName"
              name="userName"
              autocomplete="username"
              [(ngModel)]="userName"
              required
            />
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel for="password">{{ 'common.password' | t }}</label>
            <input
              hlmInput
              id="password"
              name="password"
              type="password"
              autocomplete="current-password"
              [(ngModel)]="password"
              required
            />
          </hlm-field>
          <button hlmBtn type="submit" class="w-full" [disabled]="busy() || !userName || !password">
            {{ 'login.submit' | t }}
          </button>
        </form>
        <div hlmCardFooter class="justify-end gap-2">
          <app-theme-select />
          <app-language-select />
        </div>
      </section>
    </main>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18n);

  readonly returnUrl = input<string>();

  protected userName = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.userName, this.password);
      await this.router.navigateByUrl(this.returnUrl() || '/');
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
      this.password = '';
    } finally {
      this.busy.set(false);
    }
  }
}
