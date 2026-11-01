import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUpCircle } from '@ng-icons/lucide';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { AuthService } from '../core/auth/auth.service';
import { Gql } from '../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import { TourService } from '../core/tour/tour.service';
import type { ServerUpdateFieldsFragment as ServerUpdate } from '../graphql/generated/graphql';
import { ServerUpdateQuery } from '../graphql/operations';
import { UpdateDetails } from '../shared/update-details';

const snoozeKey = 'arcanist.update.snooze';
const day = 24 * 60 * 60 * 1000;

/** The version reminded about later, and until when (per browser). */
interface Snooze {
  version: string;
  until: number;
}

function snoozed(version: string): boolean {
  try {
    const snooze = JSON.parse(localStorage.getItem(snoozeKey) ?? 'null') as Snooze | null;
    return !!snooze && snooze.version === version && snooze.until > Date.now();
  } catch {
    return false;
  }
}

function snooze(version: string): void {
  try {
    localStorage.setItem(snoozeKey, JSON.stringify({ version, until: Date.now() + day }));
  } catch {
    // Without storage it asks again next time: nothing is lost.
  }
}

/**
 * Tells administrators, once they open the panel, that a newer Arcanist is out: what it changes and how this server
 * gets it. It waits for a tour to end, not to cover it. Closing it hides it until the panel opens again; only «Remind
 * me tomorrow» keeps it away for a day (for that version).
 */
@Component({
  selector: 'app-update-notice',
  imports: [
    BrnDialogContent,
    HlmDialogImports,
    HlmButtonImports,
    NgIcon,
    TranslatePipe,
    UpdateDetails,
  ],
  providers: [provideIcons({ lucideArrowUpCircle })],
  template: `
    <hlm-dialog [state]="visible() ? 'open' : 'closed'" (closed)="update.set(null)">
      <hlm-dialog-content class="sm:max-w-xl" *brnDialogContent="let ctx">
        @if (update(); as u) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle class="flex items-center gap-2">
              <ng-icon name="lucideArrowUpCircle" class="shrink-0 text-primary" />
              {{ 'updates.notice.title' | t: { version: u.latest?.version ?? '' } }}
            </h3>
            <p hlmDialogDescription>{{ summary() }}</p>
          </hlm-dialog-header>
          <p class="rounded-md border border-primary/30 bg-primary/5 p-3 text-sm">
            {{ 'updates.notice.recommend' | t }}
          </p>
          <app-update-details [update]="u" />
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="later()">
              {{ 'updates.notice.later' | t }}
            </button>
            <a hlmBtn variant="secondary" [href]="u.latest?.url" target="_blank" rel="noopener">
              {{ 'updates.notice.open' | t }}
            </a>
          </hlm-dialog-footer>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class UpdateNotice implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly auth = inject(AuthService);
  private readonly tour = inject(TourService);

  protected readonly update = signal<ServerUpdate | null>(null);
  protected readonly visible = computed(() => !!this.update() && !this.tour.active());

  /** «You have X. Released on …, N versions on.» The date with Intl: Angular's pipe only knows locales registered with it. */
  protected readonly summary = computed(() => {
    const update = this.update();
    if (!update?.latest) return '';
    const date = new Intl.DateTimeFormat(this.i18n.language(), { dateStyle: 'long' }).format(
      new Date(update.latest.publishedAt),
    );
    const parts = { current: update.currentVersion, date };
    return update.versionsBehind > 1
      ? this.i18n.t('updates.notice.currentBehind', { ...parts, count: update.versionsBehind })
      : this.i18n.t('updates.notice.current', parts);
  });

  async ngOnInit(): Promise<void> {
    if (!this.auth.isAdmin()) return;
    try {
      const update = (await this.gql.query(ServerUpdateQuery)).serverUpdate;
      if (update.updateAvailable && update.latest && !snoozed(update.latest.version))
        this.update.set(update);
    } catch {
      // A notice is not worth an error.
    }
  }

  protected later(): void {
    const version = this.update()?.latest?.version;
    if (version) snooze(version);
    this.update.set(null);
  }
}
