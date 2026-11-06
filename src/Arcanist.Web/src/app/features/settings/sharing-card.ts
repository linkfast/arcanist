import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
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
    HlmSpinnerImports,
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
          @if ((s.publicLinks || s.jamGuests) && !s.publicUrl && isLocal) {
            <p
              class="rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-amber-800 dark:text-amber-300"
              role="alert"
            >
              {{ 'sharing.card.publicUrlNeeded' | t: { origin } }}
            </p>
          }
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
          <label class="flex items-start gap-3">
            <hlm-switch
              class="mt-0.5"
              [checked]="s.jamDeletePlaylist"
              [disabled]="busy()"
              (checkedChange)="save({ jamDeletePlaylist: $event })"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'sharing.card.jamDeletePlaylist' | t }}</span>
              <span class="text-muted-foreground">{{
                'sharing.card.jamDeletePlaylistHelp' | t
              }}</span>
            </span>
          </label>
          <p class="text-xs text-muted-foreground">{{ 'sharing.card.permission' | t }}</p>
          <form class="grid gap-1 border-t pt-4" (ngSubmit)="savePublicUrl()">
            <label class="font-medium" for="sharing-public-url">{{
              'sharing.card.publicUrl' | t
            }}</label>
            <span class="text-muted-foreground">{{ 'sharing.card.publicUrlHelp' | t }}</span>
            <div class="mt-1 flex flex-wrap gap-2">
              <input
                hlmInput
                id="sharing-public-url"
                name="publicUrl"
                type="url"
                class="min-w-0 flex-1"
                [placeholder]="'sharing.card.publicUrlPlaceholder' | t"
                [(ngModel)]="publicUrl"
                [disabled]="busy() || s.publicUrlSetByEnvironment"
              />
              @if (!s.publicUrlSetByEnvironment) {
                <button
                  hlmBtn
                  type="submit"
                  [disabled]="busy() || publicUrl.trim() === (s.publicUrl ?? '')"
                >
                  {{ 'common.save' | t }}
                </button>
              }
            </div>
            @if (s.publicUrlSetByEnvironment) {
              <span class="text-xs text-muted-foreground">{{
                'sharing.card.publicUrlEnvironment' | t
              }}</span>
            } @else if (!s.publicUrl && !isLocal) {
              <button
                hlmBtn
                variant="link"
                size="sm"
                type="button"
                class="justify-self-start px-0"
                (click)="publicUrl = origin"
              >
                {{ 'sharing.card.publicUrlFromHere' | t }} ({{ origin }})
              </button>
            }
          </form>
        </div>
      } @else {
        <div hlmCardContent class="flex justify-center"><hlm-spinner class="size-5" /></div>
      }
    </section>
  `,
})
export class SharingCard implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly status = signal<Sharing | null>(null);
  protected readonly busy = signal(false);
  /** The public URL being edited. */
  protected publicUrl = '';
  protected readonly origin = window.location.origin;
  /** The panel was opened by an IP or localhost: offering that address as the public one would not help. */
  protected readonly isLocal =
    /^(localhost|\[.*\]|[\d.]+)$/i.test(window.location.hostname) ||
    window.location.hostname.endsWith('.local');

  async ngOnInit(): Promise<void> {
    this.show((await this.gql.query(SharingQuery)).sharing);
  }

  protected async savePublicUrl(): Promise<void> {
    if (await this.save({ publicUrl: this.publicUrl.trim() }))
      toast.success(this.i18n.t('sharing.card.publicUrlSaved'));
  }

  protected async save(
    change: Partial<
      Omit<Sharing, 'canShareLinks' | 'publicUrlSetByEnvironment' | 'publicUrl' | '__typename'>
    > & { publicUrl?: string },
  ): Promise<boolean> {
    const current = this.status();
    if (!current) return false;
    this.busy.set(true);
    try {
      // Without publicUrl the server keeps the one there is.
      const input = {
        publicLinks: current.publicLinks,
        jamGuests: current.jamGuests,
        guestAddLimit: current.guestAddLimit,
        maxGuests: current.maxGuests,
        jamDeletePlaylist: current.jamDeletePlaylist,
        ...change,
      };
      this.show((await this.gql.mutate(SetSharingSettingsMutation, { input })).setSharingSettings);
      if (change.publicUrl === undefined) toast.success(this.i18n.t('common.saved'));
      return true;
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.status.set({ ...current });
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private show(status: Sharing): void {
    this.status.set(status);
    this.publicUrl = status.publicUrl ?? '';
  }
}
