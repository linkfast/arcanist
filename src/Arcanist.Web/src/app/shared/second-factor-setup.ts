import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFingerprint, lucideSmartphone } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../core/api/rest';
import type { SecondFactorPairing } from '../core/auth/auth.service';
import { passkeysUsable } from '../core/auth/webauthn';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import { qrPath } from './invitation';

/** One-time codes shown only now: they can be copied or downloaded, and going on takes saying they were saved. */
@Component({
  selector: 'app-recovery-codes',
  imports: [FormsModule, HlmButtonImports, TranslatePipe],
  template: `
    <div class="grid gap-3">
      <p class="font-medium">{{ 'secondFactor.codesTitle' | t }}</p>
      <p class="text-sm text-muted-foreground">{{ 'secondFactor.codesHelp' | t }}</p>
      <ul
        class="grid grid-cols-1 gap-1 rounded-md border bg-muted/50 p-3 font-mono text-sm sm:grid-cols-2"
      >
        @for (code of codes(); track code) {
          <li>{{ code }}</li>
        }
      </ul>
      <div class="flex flex-wrap gap-2">
        <button hlmBtn variant="outline" size="sm" type="button" (click)="copy()">
          {{ 'common.copy' | t }}
        </button>
        <button hlmBtn variant="outline" size="sm" type="button" (click)="download()">
          {{ 'secondFactor.download' | t }}
        </button>
      </div>
      <label class="flex items-center gap-2 text-sm">
        <input type="checkbox" name="saved" [(ngModel)]="saved" />
        {{ 'secondFactor.saved' | t }}
      </label>
      <button
        hlmBtn
        type="button"
        class="justify-self-end"
        [disabled]="!saved"
        (click)="done.emit()"
      >
        {{ 'secondFactor.continue' | t }}
      </button>
    </div>
  `,
})
export class RecoveryCodesView {
  private readonly i18n = inject(I18n);

  readonly codes = input.required<readonly string[]>();
  readonly done = output<void>();

  protected saved = false;

  protected async copy(): Promise<void> {
    await navigator.clipboard.writeText(this.codes().join('\n'));
    toast.success(this.i18n.t('common.copied'));
  }

  protected download(): void {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(
      new Blob([this.codes().join('\n') + '\n'], { type: 'text/plain' }),
    );
    link.download = 'arcanist-recovery-codes.txt';
    link.click();
    URL.revokeObjectURL(link.href);
  }
}

type Phase = 'choose' | 'app' | 'passkey' | 'codes';

/**
 * Pairing a second factor: an authenticator app (its QR code, its key, its first code) or a passkey, then the recovery
 * codes when it is the user's first. The same at the sign-in, in the setup wizard and in My account.
 */
@Component({
  selector: 'app-second-factor-setup',
  imports: [
    FormsModule,
    NgIcon,
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    TranslatePipe,
    RecoveryCodesView,
  ],
  providers: [provideIcons({ lucideSmartphone, lucideFingerprint })],
  template: `
    @switch (phase()) {
      @case ('choose') {
        <div class="grid gap-3">
          <button
            type="button"
            class="flex items-start gap-3 rounded-lg border p-3 text-left text-sm hover:bg-accent"
            [disabled]="busy()"
            (click)="chooseApp()"
          >
            <ng-icon name="lucideSmartphone" size="20" class="mt-0.5 shrink-0" />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'secondFactor.appOption' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'secondFactor.appOptionHelp' | t
              }}</span>
            </span>
          </button>
          @if (canUsePasskeys) {
            <button
              type="button"
              class="flex items-start gap-3 rounded-lg border p-3 text-left text-sm hover:bg-accent"
              [disabled]="busy()"
              (click)="phase.set('passkey')"
            >
              <ng-icon name="lucideFingerprint" size="20" class="mt-0.5 shrink-0" />
              <span class="grid gap-0.5">
                <span class="font-medium">{{ 'secondFactor.passkeyOption' | t }}</span>
                <span class="text-xs text-muted-foreground">{{
                  'secondFactor.passkeyOptionHelp' | t: { domain: domain }
                }}</span>
              </span>
            </button>
          } @else {
            <p class="text-xs text-muted-foreground">{{ 'secondFactor.passkeyUnavailable' | t }}</p>
          }
        </div>
      }
      @case ('app') {
        <form class="grid gap-3" (ngSubmit)="confirmApp()">
          @if (totp(); as setup) {
            <p class="text-sm">{{ 'secondFactor.scan' | t }}</p>
            @if (qr(); as code) {
              <svg
                class="size-44 justify-self-center rounded-md bg-white p-2"
                role="img"
                [attr.aria-label]="'secondFactor.scan' | t"
                [attr.viewBox]="'0 0 ' + code.size + ' ' + code.size"
                shape-rendering="crispEdges"
              >
                <path [attr.d]="code.path" fill="#000" />
              </svg>
            }
            <p class="text-xs text-muted-foreground">{{ 'secondFactor.manual' | t }}</p>
            <div class="flex items-center gap-2">
              <code class="min-w-0 flex-1 rounded bg-muted px-2 py-1 font-mono text-xs break-all">{{
                setup.secret
              }}</code>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                (click)="copyKey(setup.secret)"
              >
                {{ 'common.copy' | t }}
              </button>
            </div>
            <hlm-field>
              <label hlmFieldLabel for="pair-code">{{ 'secondFactor.confirmCode' | t }}</label>
              <input
                hlmInput
                id="pair-code"
                name="code"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="7"
                [(ngModel)]="code"
              />
            </hlm-field>
          }
          @if (error()) {
            <p class="text-sm text-destructive" role="alert">{{ error() }}</p>
          }
          <div class="flex justify-end gap-2">
            <button hlmBtn variant="outline" type="button" (click)="back()">
              {{ 'common.back' | t }}
            </button>
            <button hlmBtn type="submit" [disabled]="busy() || code.trim().length < 6">
              {{ 'secondFactor.confirm' | t }}
            </button>
          </div>
        </form>
      }
      @case ('passkey') {
        <form class="grid gap-3" (ngSubmit)="createPasskey()">
          <hlm-field>
            <label hlmFieldLabel for="pair-name">{{ 'secondFactor.passkeyName' | t }}</label>
            <input
              hlmInput
              id="pair-name"
              name="name"
              maxlength="100"
              [placeholder]="'secondFactor.passkeyNamePlaceholder' | t"
              [(ngModel)]="name"
            />
          </hlm-field>
          @if (error()) {
            <p class="text-sm text-destructive" role="alert">{{ error() }}</p>
          }
          <div class="flex justify-end gap-2">
            <button hlmBtn variant="outline" type="button" (click)="back()">
              {{ 'common.back' | t }}
            </button>
            <button hlmBtn type="submit" [disabled]="busy()">
              {{ 'secondFactor.passkeyCreate' | t }}
            </button>
          </div>
        </form>
      }
      @case ('codes') {
        <app-recovery-codes [codes]="codes()" (done)="finished.emit()" />
      }
    }
  `,
})
export class SecondFactorSetup {
  private readonly i18n = inject(I18n);

  readonly pairing = input.required<SecondFactorPairing>();
  /** Goes straight to one way of pairing, instead of offering both. */
  readonly only = input<'app' | 'passkey' | null>(null);
  readonly finished = output<void>();
  readonly cancelled = output<void>();

  protected readonly canUsePasskeys = passkeysUsable();
  protected readonly domain = window.location.hostname;

  protected readonly phase = signal<Phase>('choose');
  protected readonly totp = signal<{ secret: string; uri: string } | null>(null);
  protected readonly qr = computed(() => {
    const setup = this.totp();
    return setup ? qrPath(setup.uri) : null;
  });
  protected readonly codes = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected code = '';
  protected name = '';

  ngOnInit(): void {
    if (this.only() === 'app') void this.chooseApp();
    if (this.only() === 'passkey') this.phase.set('passkey');
  }

  protected back(): void {
    this.error.set(null);
    if (this.only()) this.cancelled.emit();
    else this.phase.set('choose');
  }

  protected async copyKey(secret: string): Promise<void> {
    await navigator.clipboard.writeText(secret);
    toast.success(this.i18n.t('common.copied'));
  }

  protected async chooseApp(): Promise<void> {
    await this.run(async () => {
      this.totp.set(await this.pairing().startTotp());
      this.code = '';
      this.phase.set('app');
    });
  }

  protected async confirmApp(): Promise<void> {
    await this.run(() =>
      this.pairing()
        .confirmTotp(this.code)
        .then((codes) => this.paired(codes)),
    );
  }

  protected async createPasskey(): Promise<void> {
    await this.run(() =>
      this.pairing()
        .registerPasskey(this.name.trim())
        .then((codes) => this.paired(codes)),
    );
  }

  private paired(codes: readonly string[]): void {
    if (codes.length === 0) {
      this.finished.emit();
      return;
    }
    this.codes.set(codes);
    this.phase.set('codes');
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Whether the server will ask everyone for a second factor, as the setup wizard offers it: on and recommended. */
@Component({
  selector: 'app-require-second-factor',
  imports: [HlmBadgeImports, HlmSwitchImports, TranslatePipe],
  template: `
    <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
      <hlm-switch class="mt-0.5" [checked]="value()" (checkedChange)="value.set($event)" />
      <span class="grid gap-0.5">
        <span class="flex flex-wrap items-center gap-2 font-medium">
          {{ 'secondFactor.wizard.switch' | t }}
          <span hlmBadge variant="secondary">{{ 'setup.database.recommended' | t }}</span>
        </span>
        <span class="text-xs text-muted-foreground">{{ 'secondFactor.wizard.help' | t }}</span>
      </span>
    </label>
  `,
})
export class RequireSecondFactor {
  readonly value = model(true);
}
