import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { SharingFieldsFragment as Sharing } from '../../graphql/generated/graphql';
import { SetSharingSettingsMutation, SharingQuery } from '../../graphql/operations';

/**
 * Sharing outside the server: public playlist links (anyone with one sees the playlist and hears 30-second
 * previews) and guests without an account in Jukebox Jams. Both off until an administrator turns them on; users also
 * need the SHARE_LINKS permission. Administrators only.
 */
@Component({
  selector: 'app-sharing-card',
  imports: [
    HlmCardImports,
    HlmButtonImports,
    HlmInputImports,
    HlmSwitchImports,
    FormsModule,
    TranslatePipe,
  ],
  template: `
    <section hlmCard data-tour="settings-sharing">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'sharing.card.title' | t }}</h2>
        <p hlmCardDescription>{{ 'sharing.card.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="grid gap-4 text-sm">
          <label class="flex items-start gap-3">
            <hlm-switch
              class="mt-0.5"
              [checked]="s.publicLinks"
              [disabled]="busy()"
              (checkedChange)="save({ publicLinks: $event })"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'sharing.card.publicLinks' | t }}</span>
              <span class="text-muted-foreground">{{ 'sharing.card.publicLinksHelp' | t }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3">
            <hlm-switch
              class="mt-0.5"
              [checked]="s.jamGuests"
              [disabled]="busy()"
              (checkedChange)="save({ jamGuests: $event })"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'sharing.card.jamGuests' | t }}</span>
              <span class="text-muted-foreground">{{ 'sharing.card.jamGuestsHelp' | t }}</span>
            </span>
          </label>
          @if (s.jamGuests) {
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="grid gap-1">
                <span>{{ 'sharing.card.guestAddLimit' | t }}</span>
                <input
                  hlmInput
                  type="number"
                  min="1"
                  max="100"
                  [ngModel]="s.guestAddLimit"
                  (change)="save({ guestAddLimit: +$any($event.target).value })"
                  [disabled]="busy()"
                />
              </label>
              <label class="grid gap-1">
                <span>{{ 'sharing.card.maxGuests' | t }}</span>
                <input
                  hlmInput
                  type="number"
                  min="1"
                  max="1000"
                  [ngModel]="s.maxGuests"
                  (change)="save({ maxGuests: +$any($event.target).value })"
                  [disabled]="busy()"
                />
              </label>
            </div>
          }
          <p class="text-xs text-muted-foreground">{{ 'sharing.card.permission' | t }}</p>
        </div>
      }
    </section>
  `,
})
export class SharingCard implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly status = signal<Sharing | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.status.set((await this.gql.query(SharingQuery)).sharing);
  }

  protected async save(
    change: Partial<Omit<Sharing, 'canShareLinks' | '__typename'>>,
  ): Promise<void> {
    const current = this.status();
    if (!current) return;
    this.busy.set(true);
    try {
      const input = {
        publicLinks: current.publicLinks,
        jamGuests: current.jamGuests,
        guestAddLimit: current.guestAddLimit,
        maxGuests: current.maxGuests,
        ...change,
      };
      this.status.set(
        (await this.gql.mutate(SetSharingSettingsMutation, { input })).setSharingSettings,
      );
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.status.set({ ...current });
    } finally {
      this.busy.set(false);
    }
  }
}
