import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { AuthService } from '../core/auth/auth.service';
import { Gql } from '../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import type { ServerUpdateFieldsFragment as ServerUpdate } from '../graphql/generated/graphql';
import { ServerUpdateQuery } from '../graphql/operations';
import { ReleaseNotes } from '../shared/release-notes';

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
 * Tells administrators, once they open the panel, that a newer Arcanist is out: what changed, and how this server is
 * updated. "Remind me tomorrow" keeps it away for a day (for that version); going to the release page too.
 */
@Component({
  selector: 'app-update-notice',
  imports: [
    BrnDialogContent,
    HlmDialogImports,
    HlmButtonImports,
    TranslatePipe,
    ReleaseNotes,
    DatePipe,
  ],
  template: `
    <hlm-dialog [state]="update() ? 'open' : 'closed'" (closed)="later()">
      <hlm-dialog-content class="sm:max-w-lg" *brnDialogContent="let ctx">
        @if (update(); as u) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>
              {{ 'updates.notice.title' | t: { version: u.latest?.version ?? '' } }}
            </h3>
            <p hlmDialogDescription>
              {{
                'updates.notice.current'
                  | t
                    : {
                        current: u.currentVersion,
                        date:
                          (u.latest?.publishedAt
                            | date: 'longDate' : undefined : i18n.language()) ?? '',
                      }
              }}
            </p>
          </hlm-dialog-header>
          @if (u.latest?.notes) {
            <div class="max-h-72 overflow-y-auto rounded-md border p-3">
              <app-release-notes [markdown]="u.latest!.notes" />
            </div>
          }
          <p class="text-sm text-muted-foreground">
            {{ 'updates.howTo.' + u.installKind | t }}
          </p>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="later()">
              {{ 'updates.notice.later' | t }}
            </button>
            <button hlmBtn type="button" (click)="open(u)">
              {{ 'updates.notice.open' | t }}
            </button>
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

  protected readonly update = signal<ServerUpdate | null>(null);

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

  protected open(update: ServerUpdate): void {
    if (update.latest) window.open(update.latest.url, '_blank', 'noopener');
    this.later();
  }
}
