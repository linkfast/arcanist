import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import {
  FactoryResetMutation,
  ResetSettingsMutation,
  ResetUserDataMutation,
} from '../../graphql/operations';

type Reset = 'settings' | 'userData' | 'factory';

/**
 * Running the setup wizard again, and the resets: settings, users' data, or everything (factory). Each reset asks to
 * type a word, and the password again; music files are never touched.
 */
@Component({
  selector: 'app-maintenance-card',
  imports: [
    FormsModule,
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmInputImports,
    HlmSpinnerImports,
    TranslatePipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.maintenance.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.maintenance.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid gap-4">
        <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3">
          <span class="grid gap-0.5 text-sm">
            <span class="font-medium">{{ 'settings.maintenance.wizard' | t }}</span>
            <span class="text-xs text-muted-foreground">{{
              'settings.maintenance.wizardHelp' | t
            }}</span>
          </span>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="runWizard()">
            {{ 'settings.maintenance.wizardRun' | t }}
          </button>
        </div>

        @for (reset of resets; track reset) {
          <div
            class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-destructive/40 p-3"
          >
            <span class="grid gap-0.5 text-sm">
              <span class="font-medium">{{ 'settings.maintenance.' + reset + '.title' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'settings.maintenance.' + reset + '.help' | t
              }}</span>
            </span>
            <button
              hlmBtn
              variant="destructive"
              size="sm"
              type="button"
              [disabled]="busy()"
              (click)="open(reset)"
            >
              {{ 'settings.maintenance.' + reset + '.button' | t }}
            </button>
          </div>
        }

        @if (restarting()) {
          <p class="flex items-center gap-2 text-sm text-muted-foreground">
            <hlm-spinner class="size-4" /> {{ 'settings.maintenance.restarting' | t }}
          </p>
        }
      </div>
    </section>

    <hlm-alert-dialog [state]="confirming() ? 'open' : 'closed'" (closed)="confirming.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (confirming(); as reset) {
          <hlm-alert-dialog-header>
            <h3 hlmAlertDialogTitle>{{ 'settings.maintenance.' + reset + '.confirmTitle' | t }}</h3>
            <p hlmAlertDialogDescription>{{ 'settings.maintenance.' + reset + '.confirm' | t }}</p>
          </hlm-alert-dialog-header>
          @if (reset === 'factory') {
            <label class="flex items-start gap-2 text-sm">
              <hlm-checkbox
                class="mt-0.5"
                [checked]="removeAddons()"
                (checkedChange)="removeAddons.set($event)"
              />
              {{ 'settings.maintenance.factory.addons' | t }}
            </label>
          }
          <label class="grid gap-1 text-sm">
            {{ 'settings.maintenance.type' | t: { word: word() } }}
            <input
              hlmInput
              name="confirmWord"
              autocomplete="off"
              [ngModel]="typed()"
              (ngModelChange)="typed.set($event)"
            />
          </label>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="confirming.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button
              hlmBtn
              variant="destructive"
              type="button"
              [disabled]="busy() || typed().trim().toUpperCase() !== word()"
              (click)="run(reset)"
            >
              {{ 'settings.maintenance.' + reset + '.button' | t }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class MaintenanceCard {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);

  protected readonly resets: Reset[] = ['settings', 'userData', 'factory'];
  protected readonly confirming = signal<Reset | null>(null);
  protected readonly typed = signal('');
  protected readonly removeAddons = signal(false);
  protected readonly busy = signal(false);
  protected readonly restarting = signal(false);

  /** The word to type, in the user's language. */
  protected word(): string {
    return this.i18n.t('settings.maintenance.word').toUpperCase();
  }

  protected async runWizard(): Promise<void> {
    await this.router.navigateByUrl('/reconfigure');
  }

  protected open(reset: Reset): void {
    this.typed.set('');
    this.removeAddons.set(false);
    this.confirming.set(reset);
  }

  protected async run(reset: Reset): Promise<void> {
    this.busy.set(true);
    try {
      if (reset === 'settings') await this.gql.mutate(ResetSettingsMutation);
      else if (reset === 'userData') await this.gql.mutate(ResetUserDataMutation);
      else
        await this.gql.mutate(FactoryResetMutation, {
          input: { removeAddons: this.removeAddons() },
        });
      this.confirming.set(null);
      if (reset !== 'factory') {
        toast.success(this.i18n.t('settings.maintenance.' + reset + '.done'));
        return;
      }
      this.restarting.set(true);
      await this.waitForSetup();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  /** The server restarts empty: once it answers again as not set up, the setup wizard opens. */
  private async waitForSetup(): Promise<void> {
    for (let attempt = 0; attempt < 300; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 2000));
      try {
        const response = await fetch('/rest/v1/server-info', { cache: 'no-store' });
        if (response.ok && (await response.json()).setupCompleted === false) {
          location.assign('/admin/setup');
          return;
        }
      } catch {
        // Still restarting.
      }
    }
  }
}
