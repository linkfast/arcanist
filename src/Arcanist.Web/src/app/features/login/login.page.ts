import { Edition } from '../../core/server/edition';
import { Brand } from '../../shared/brand';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { AuthService, LoginChallenge } from '../../core/auth/auth.service';
import { passkeysUsable } from '../../core/auth/webauthn';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { LanguageSelect } from '../../shared/language-select';
import { SecondFactorSetup } from '../../shared/second-factor-setup';
import { ThemeSelect } from '../../shared/theme-select';

@Component({
  selector: 'app-login-page',
  imports: [
    Brand,
    FormsModule,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmButtonImports,
    HlmAlertImports,
    TranslatePipe,
    LanguageSelect,
    SecondFactorSetup,
    ThemeSelect,
  ],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-muted/40 p-4">
      <section hlmCard class="w-full max-w-sm">
        <div hlmCardHeader>
          <h1 hlmCardTitle class="mb-2"><app-brand [edition]="edition.label()" /></h1>
          <p hlmCardDescription>{{ subtitle() | t }}</p>
        </div>
        @if (challenge(); as current) {
          <div hlmCardContent class="grid gap-4">
            @if (current.code === 'second_factor_setup_required') {
              <app-second-factor-setup [pairing]="pairing()!" (finished)="enter()" />
            } @else {
              @if (error()) {
                <div hlmAlert variant="destructive">
                  <p hlmAlertDescription>{{ error() }}</p>
                </div>
              }
              <form class="grid gap-4" (ngSubmit)="verify(current)">
                <hlm-field>
                  <label hlmFieldLabel for="code">{{ 'secondFactor.codeLabel' | t }}</label>
                  <input
                    hlmInput
                    id="code"
                    name="code"
                    autocomplete="one-time-code"
                    autofocus
                    [(ngModel)]="code"
                  />
                  <p hlmFieldDescription>{{ 'secondFactor.codeHelp' | t }}</p>
                </hlm-field>
                <button hlmBtn type="submit" class="w-full" [disabled]="busy() || !code.trim()">
                  {{ 'secondFactor.verify' | t }}
                </button>
              </form>
              @if (current.methods.includes('PASSKEY')) {
                @if (passkeyHere()) {
                  <button
                    hlmBtn
                    variant="outline"
                    type="button"
                    class="w-full"
                    [disabled]="busy()"
                    (click)="usePasskey(current)"
                  >
                    {{ 'secondFactor.usePasskey' | t }}
                  </button>
                } @else {
                  <p class="text-xs text-muted-foreground">
                    {{
                      'secondFactor.passkeyElsewhere'
                        | t: { domains: current.passkeyDomains.join(', ') }
                    }}
                  </p>
                }
              }
            }
            <button hlmBtn variant="ghost" type="button" class="w-full" (click)="startOver()">
              {{ 'secondFactor.startOver' | t }}
            </button>
          </div>
        } @else {
          <form hlmCardContent (ngSubmit)="submit()" class="grid gap-4">
            @if (restored()) {
              <div hlmAlert>
                <p hlmAlertTitle>{{ 'login.restoredTitle' | t }}</p>
                <p hlmAlertDescription>{{ 'login.restored' | t }}</p>
              </div>
            }
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
            <button
              hlmBtn
              type="submit"
              class="w-full"
              [disabled]="busy() || !userName || !password"
            >
              {{ 'login.submit' | t }}
            </button>
          </form>
        }
        <div hlmCardFooter class="justify-end gap-2">
          <app-theme-select />
          <app-language-select />
        </div>
      </section>
    </main>
  `,
})
export class LoginPage {
  protected readonly edition = inject(Edition);

  constructor() {
    // The edition under the wordmark, before anyone signs in.
    void this.edition.refresh();
  }
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18n);

  readonly returnUrl = input<string>();
  /** The server was just restored from a backup in the setup wizard. */
  readonly restored = input<string>();

  protected userName = '';
  protected password = '';
  protected code = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** The password was right, and the sign-in goes on with this. */
  protected readonly challenge = signal<LoginChallenge | null>(null);

  protected readonly pairing = computed(() => {
    const current = this.challenge();
    return current ? this.auth.pairingAtLogin(current.challenge) : null;
  });
  protected readonly subtitle = computed(() => {
    const current = this.challenge();
    return !current
      ? 'login.subtitle'
      : current.code === 'second_factor_setup_required'
        ? 'secondFactor.setupHelp'
        : 'secondFactor.verifyHelp';
  });
  /** Passkeys belong to a domain: theirs may be another address of this server. */
  protected readonly passkeyHere = computed(
    () =>
      passkeysUsable() &&
      (this.challenge()?.passkeyDomains.includes(window.location.hostname) ?? false),
  );

  protected async submit(): Promise<void> {
    await this.run(async () => {
      const challenge = await this.auth.login(this.userName, this.password);
      this.password = '';
      if (challenge) this.challenge.set(challenge);
      else await this.enter();
    });
    this.password = '';
  }

  protected async verify(current: LoginChallenge): Promise<void> {
    await this.run(async () => {
      await this.auth.loginWithCode(current.challenge, this.code);
      await this.enter();
    });
    this.code = '';
  }

  protected async usePasskey(current: LoginChallenge): Promise<void> {
    await this.run(async () => {
      await this.auth.loginWithPasskey(current.challenge);
      await this.enter();
    });
  }

  protected async enter(): Promise<void> {
    const target = this.returnUrl() || '/';
    // A web player or the Jam's client that sent someone here to sign in: back to that page of this server.
    if (/^\/players\/[^/\\]/.test(target)) window.location.assign(target);
    else await this.router.navigateByUrl(target);
  }

  protected startOver(): void {
    this.challenge.set(null);
    this.code = '';
    this.error.set(null);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      const code = ApiError.from(error).code;
      this.error.set(this.i18n.error(code));
      if (code === 'second_factor_expired') this.challenge.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}
