import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../core/api/rest';
import { AuthService, SecondFactorAnswer } from '../core/auth/auth.service';
import { ReauthService } from '../core/auth/reauth.service';
import { passkeysUsable } from '../core/auth/webauthn';
import { I18n, TranslatePipe } from '../core/i18n/i18n';

/** Password confirmation for sensitive actions, opened by ReauthService. */
@Component({
  selector: 'app-reauth-dialog',
  imports: [
    FormsModule,
    BrnDialogContent,
    HlmDialogImports,
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    TranslatePipe,
  ],
  template: `
    <hlm-dialog [state]="reauth.open() ? 'open' : 'closed'" (closed)="cancel()">
      <hlm-dialog-content *brnDialogContent="let ctx" class="sm:max-w-sm">
        <form (ngSubmit)="confirm()" class="grid gap-4">
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'reauth.title' | t }}</h3>
            <p hlmDialogDescription>{{ 'reauth.description' | t }}</p>
          </hlm-dialog-header>
          <hlm-field>
            <label hlmFieldLabel for="reauth-password">{{ 'common.password' | t }}</label>
            <input
              hlmInput
              id="reauth-password"
              name="password"
              type="password"
              autocomplete="current-password"
              [(ngModel)]="password"
              required
            />
            @if (error() && !factor()) {
              <hlm-field-error>{{ error() }}</hlm-field-error>
            }
          </hlm-field>
          @if (factor(); as asked) {
            <hlm-field>
              <label hlmFieldLabel for="reauth-code">{{ 'secondFactor.codeLabel' | t }}</label>
              <input
                hlmInput
                id="reauth-code"
                name="code"
                autocomplete="one-time-code"
                [(ngModel)]="code"
              />
              <p hlmFieldDescription>{{ 'secondFactor.codeHelp' | t }}</p>
              @if (error()) {
                <hlm-field-error>{{ error() }}</hlm-field-error>
              }
            </hlm-field>
            @if (canUsePasskey(asked)) {
              <button
                hlmBtn
                variant="outline"
                type="button"
                [disabled]="busy()"
                (click)="usePasskey()"
              >
                {{ 'secondFactor.usePasskey' | t }}
              </button>
            }
          }
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="cancel()">
              {{ 'common.cancel' | t }}
            </button>
            <button
              hlmBtn
              type="submit"
              [disabled]="busy() || !password || (factor() && !code.trim())"
            >
              {{ 'reauth.confirm' | t }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ReauthDialog {
  protected readonly reauth = inject(ReauthService);
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(I18n);

  protected password = '';
  protected code = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** The password was right, and the server asks for the second factor too. */
  protected readonly factor = signal<SecondFactorAnswer | null>(null);

  protected canUsePasskey(asked: SecondFactorAnswer): boolean {
    return passkeysUsable() && asked.passkeyDomains.includes(window.location.hostname);
  }

  protected confirm(): Promise<void> {
    return this.run(() => this.auth.reauthenticate(this.password, this.code.trim() || undefined));
  }

  protected usePasskey(): Promise<void> {
    return this.run(() => this.auth.reauthenticateWithPasskey(this.password));
  }

  private async run(attempt: () => Promise<boolean | SecondFactorAnswer>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const answer = await attempt();
      if (answer === true) {
        this.reset();
        this.reauth.settle(true);
      } else if (answer === false) {
        this.factor.set(null);
        this.code = '';
        this.error.set(this.i18n.error('invalid_credentials'));
      } else {
        this.factor.set(answer);
      }
    } catch (error) {
      this.code = '';
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected cancel(): void {
    if (!this.reauth.open()) return;
    this.reset();
    this.reauth.settle(false);
  }

  private reset(): void {
    this.password = '';
    this.code = '';
    this.factor.set(null);
    this.error.set(null);
  }
}
