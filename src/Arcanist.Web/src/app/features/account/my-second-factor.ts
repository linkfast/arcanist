import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import type { SecondFactorPairing } from '../../core/auth/auth.service';
import { createPasskey } from '../../core/auth/webauthn';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { MySecondFactorQuery as MineResult } from '../../graphql/generated/graphql';
import {
  ConfirmTotpSetupMutation,
  DisableTotpMutation,
  MySecondFactorQuery,
  PasskeyRegistrationOptionsMutation,
  RegenerateRecoveryCodesMutation,
  RegisterPasskeyMutation,
  RemovePasskeyMutation,
  RenamePasskeyMutation,
  StartTotpSetupMutation,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { RecoveryCodesView, SecondFactorSetup } from '../../shared/second-factor-setup';

type Mine = MineResult['mySecondFactor'];
type Passkey = Mine['passkeys'][number];
type Pending = { kind: 'app' } | { kind: 'passkey'; passkey: Passkey } | { kind: 'codes' };

/** The user's own second factors: the authenticator app, passkeys and recovery codes. */
@Component({
  selector: 'app-my-second-factor',
  imports: [
    FormsModule,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSpinnerImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
    SecondFactorSetup,
    RecoveryCodesView,
  ],
  template: `
    <section hlmCard data-tour="account-second-factor">
      <div hlmCardHeader>
        <h2 hlmCardTitle class="flex flex-wrap items-center gap-2">
          {{ 'secondFactor.card.title' | t }}
          @if (mine(); as m) {
            <span hlmBadge [variant]="m.enabled ? 'secondary' : 'outline'">{{
              (m.enabled ? 'secondFactor.card.on' : 'secondFactor.card.off') | t
            }}</span>
          }
        </h2>
        <p hlmCardDescription>{{ 'secondFactor.card.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid gap-4 text-sm">
        @if (mine(); as m) {
          @if (m.required && !m.enabled) {
            <p class="text-amber-700 dark:text-amber-400">{{ 'secondFactor.card.required' | t }}</p>
          }

          <div class="flex flex-wrap items-center justify-between gap-3 rounded-md border p-3">
            <div class="min-w-0">
              <p class="font-medium">{{ 'secondFactor.appOption' | t }}</p>
              <p class="text-xs text-muted-foreground">
                @if (m.totpEnabled && m.totpEnabledAt) {
                  {{ 'secondFactor.card.since' | t }}
                  <span [title]="m.totpEnabledAt | dateTime">{{
                    m.totpEnabledAt | relativeTime
                  }}</span>
                } @else {
                  {{ 'secondFactor.appOptionHelp' | t }}
                }
              </p>
            </div>
            @if (m.totpEnabled) {
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                [disabled]="busy()"
                (click)="pending.set({ kind: 'app' })"
              >
                {{ 'common.remove' | t }}
              </button>
            } @else {
              <button hlmBtn size="sm" type="button" (click)="pairingWith.set('app')">
                {{ 'secondFactor.card.setUp' | t }}
              </button>
            }
          </div>

          <div class="grid gap-2 rounded-md border p-3">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div class="min-w-0">
                <p class="font-medium">{{ 'secondFactor.card.passkeys' | t }}</p>
                <p class="text-xs text-muted-foreground">
                  {{ 'secondFactor.card.passkeysHelp' | t }}
                </p>
              </div>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                (click)="pairingWith.set('passkey')"
              >
                {{ 'secondFactor.card.addPasskey' | t }}
              </button>
            </div>
            @if (m.passkeys.length > 0) {
              <ul class="grid gap-2">
                @for (passkey of m.passkeys; track passkey.id) {
                  <li class="flex flex-wrap items-center justify-between gap-2 border-t pt-2">
                    <div class="min-w-0">
                      <p class="truncate">{{ passkey.name }}</p>
                      <p class="text-xs text-muted-foreground">
                        {{ passkey.rpId }} ·
                        @if (passkey.lastUsedAt) {
                          {{ 'account.lastSeen' | t }}
                          <span [title]="passkey.lastUsedAt | dateTime">{{
                            passkey.lastUsedAt | relativeTime
                          }}</span>
                        } @else {
                          {{ 'account.appPasswords.neverUsed' | t }}
                        }
                      </p>
                    </div>
                    <div class="flex gap-2">
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        (click)="renameName = passkey.name; renaming.set(passkey)"
                      >
                        {{ 'common.edit' | t }}
                      </button>
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        type="button"
                        [disabled]="busy()"
                        (click)="pending.set({ kind: 'passkey', passkey })"
                      >
                        {{ 'common.remove' | t }}
                      </button>
                    </div>
                  </li>
                }
              </ul>
            }
          </div>

          @if (m.enabled) {
            <div class="flex flex-wrap items-center justify-between gap-3 rounded-md border p-3">
              <div class="min-w-0">
                <p class="font-medium">{{ 'secondFactor.card.codes' | t }}</p>
                <p
                  class="text-xs"
                  [class.text-muted-foreground]="m.recoveryCodesLeft > 2"
                  [class.text-destructive]="m.recoveryCodesLeft <= 2"
                >
                  {{ 'secondFactor.card.codesLeft' | t: { count: m.recoveryCodesLeft } }}
                </p>
              </div>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                [disabled]="busy()"
                (click)="pending.set({ kind: 'codes' })"
              >
                {{ 'secondFactor.card.regenerate' | t }}
              </button>
            </div>
          }
        } @else {
          <hlm-spinner class="size-5 justify-self-center" />
        }
      </div>
    </section>

    <hlm-dialog [state]="pairingWith() ? 'open' : 'closed'" (closed)="pairingWith.set(null)">
      <hlm-dialog-content *brnDialogContent="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h3 hlmDialogTitle>{{ 'secondFactor.card.title' | t }}</h3>
        </hlm-dialog-header>
        @if (pairingWith(); as way) {
          <app-second-factor-setup
            [pairing]="pairing"
            [only]="way"
            (cancelled)="pairingWith.set(null)"
            (finished)="paired()"
          />
        }
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-dialog [state]="renaming() ? 'open' : 'closed'" (closed)="renaming.set(null)">
      <hlm-dialog-content *brnDialogContent="let ctx" class="sm:max-w-sm">
        <form class="grid gap-4" (ngSubmit)="rename()">
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'secondFactor.passkeyName' | t }}</h3>
          </hlm-dialog-header>
          <hlm-field>
            <label hlmFieldLabel for="passkey-name">{{ 'secondFactor.passkeyName' | t }}</label>
            <input
              hlmInput
              id="passkey-name"
              name="name"
              maxlength="100"
              [(ngModel)]="renameName"
            />
          </hlm-field>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="renaming.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmBtn type="submit" [disabled]="busy() || !renameName.trim()">
              {{ 'common.save' | t }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- New codes: shown only now, so they do not close by a click outside. -->
    <hlm-alert-dialog [state]="newCodes() ? 'open' : 'closed'" (closed)="newCodes.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (newCodes(); as codes) {
          <app-recovery-codes [codes]="codes" (done)="newCodes.set(null)" />
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>

    <hlm-alert-dialog [state]="pending() ? 'open' : 'closed'" (closed)="pending.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (pending(); as what) {
          <hlm-alert-dialog-header>
            @switch (what.kind) {
              @case ('app') {
                <h3 hlmAlertDialogTitle>{{ 'secondFactor.card.confirmApp' | t }}</h3>
                <p hlmAlertDialogDescription>{{ 'secondFactor.card.confirmRemoveHelp' | t }}</p>
              }
              @case ('passkey') {
                <h3 hlmAlertDialogTitle>
                  {{ 'secondFactor.card.confirmPasskey' | t: { name: what.passkey.name } }}
                </h3>
                <p hlmAlertDialogDescription>{{ 'secondFactor.card.confirmRemoveHelp' | t }}</p>
              }
              @case ('codes') {
                <h3 hlmAlertDialogTitle>{{ 'secondFactor.card.confirmCodes' | t }}</h3>
                <p hlmAlertDialogDescription>{{ 'secondFactor.card.confirmCodesHelp' | t }}</p>
              }
            }
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="pending.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmAlertDialogAction type="button" (click)="confirm(what)">
              {{ (what.kind === 'codes' ? 'secondFactor.card.regenerate' : 'common.remove') | t }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class MySecondFactor implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly mine = signal<Mine | null>(null);
  protected readonly busy = signal(false);
  protected readonly pairingWith = signal<'app' | 'passkey' | null>(null);
  protected readonly pending = signal<Pending | null>(null);
  protected readonly renaming = signal<Passkey | null>(null);
  protected renameName = '';
  protected readonly newCodes = signal<readonly string[] | null>(null);

  protected readonly pairing: SecondFactorPairing = {
    startTotp: async () => (await this.gql.mutate(StartTotpSetupMutation)).startTotpSetup,
    confirmTotp: async (code) =>
      (await this.gql.mutate(ConfirmTotpSetupMutation, { code })).confirmTotpSetup.codes,
    registerPasskey: async (name) => {
      const { passkeyRegistrationOptions } = await this.gql.mutate(
        PasskeyRegistrationOptionsMutation,
      );
      const attestation = await createPasskey(passkeyRegistrationOptions.options);
      const { registerPasskey } = await this.gql.mutate(RegisterPasskeyMutation, {
        name: name || null,
        attestation,
      });
      return registerPasskey.recoveryCodes;
    },
  };

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async paired(): Promise<void> {
    this.pairingWith.set(null);
    toast.success(this.i18n.t('common.saved'));
    await this.load();
  }

  protected async rename(): Promise<void> {
    const passkey = this.renaming();
    if (!passkey) return;
    await this.run(async () => {
      await this.gql.mutate(RenamePasskeyMutation, {
        id: passkey.id,
        name: this.renameName.trim(),
      });
      this.renaming.set(null);
    });
  }

  protected async confirm(what: Pending): Promise<void> {
    this.pending.set(null);
    await this.run(async () => {
      if (what.kind === 'app') await this.gql.mutate(DisableTotpMutation);
      if (what.kind === 'passkey')
        await this.gql.mutate(RemovePasskeyMutation, { id: what.passkey.id });
      if (what.kind === 'codes')
        this.newCodes.set(
          (await this.gql.mutate(RegenerateRecoveryCodesMutation)).regenerateRecoveryCodes.codes,
        );
    });
  }

  private async load(): Promise<void> {
    this.mine.set((await this.gql.query(MySecondFactorQuery)).mySecondFactor);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      await this.load();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
