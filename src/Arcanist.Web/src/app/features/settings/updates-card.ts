import { Component, OnInit, inject, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { ServerUpdateFieldsFragment as ServerUpdate } from '../../graphql/generated/graphql';
import {
  CheckServerUpdateMutation,
  ServerUpdateQuery,
  SetServerUpdateSettingsMutation,
} from '../../graphql/operations';
import { RelativeTimePipe } from '../../shared/format';

/** Checking GitHub for newer versions of Arcanist: on or off, and when it last looked. Administrators only. */
@Component({
  selector: 'app-updates-card',
  imports: [
    HlmCardImports,
    HlmButtonImports,
    HlmSwitchImports,
    TranslatePipe,
    RelativeTimePipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'updates.card.title' | t }}</h2>
        <p hlmCardDescription>{{ 'updates.card.description' | t }}</p>
      </div>
      @if (status(); as s) {
        <div hlmCardContent class="grid gap-3 text-sm">
          <label class="flex items-center gap-3">
            <hlm-switch
              [checked]="s.enabled"
              [disabled]="busy()"
              (checkedChange)="setEnabled($event)"
            />
            {{ 'updates.card.enabled' | t }}
          </label>
          <p>
            {{ 'updates.card.current' | t: { version: s.currentVersion } }}
            @if (s.enabled) {
              —
              @if (s.updateAvailable && s.latest) {
                <a
                  class="font-medium underline"
                  [href]="s.latest.url"
                  target="_blank"
                  rel="noopener"
                  >{{ 'updates.card.available' | t: { version: s.latest.version } }}</a
                >
              } @else if (s.checkedAt) {
                {{ 'updates.card.upToDate' | t }}
              }
            }
          </p>
          @if (s.enabled && s.checkedAt) {
            <p class="text-xs text-muted-foreground">
              {{ 'updates.card.checked' | t: { when: (s.checkedAt | relativeTime) } }}
              @if (s.errorCode) {
                · <span class="text-destructive">{{ i18n.error(s.errorCode) }}</span>
              }
            </p>
          }
          @if (s.enabled) {
            <div>
              <button
                hlmBtn
                variant="outline"
                size="sm"
                type="button"
                [disabled]="busy()"
                (click)="check()"
              >
                {{ 'updates.card.check' | t }}
              </button>
            </div>
          }
        </div>
      }
    </section>
  `,
})
export class UpdatesCard implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly status = signal<ServerUpdate | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.status.set((await this.gql.query(ServerUpdateQuery)).serverUpdate);
  }

  protected check(): Promise<void> {
    return this.run(
      async () => (await this.gql.mutate(CheckServerUpdateMutation)).checkServerUpdate,
    );
  }

  protected setEnabled(enabled: boolean): Promise<void> {
    return this.run(
      async () =>
        (await this.gql.mutate(SetServerUpdateSettingsMutation, { input: { enabled } }))
          .setServerUpdateSettings,
    );
  }

  private async run(change: () => Promise<ServerUpdate>): Promise<void> {
    this.busy.set(true);
    try {
      this.status.set(await change());
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
