import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  CreateInvitationMutation as Created,
  InvitationsQuery as InvitationsResult,
} from '../../graphql/generated/graphql';
import {
  CreateInvitationMutation,
  InvitationsQuery,
  RevokeInvitationMutation,
} from '../../graphql/operations';
import { RelativeTimePipe } from '../../shared/format';
import { Invitation } from '../../shared/invitation';

type Item = InvitationsResult['invitations'][number];

/**
 * Invitations to this server (permission INVITE): a link and a QR code with the server's public URL, which add it to
 * Ultimate Desktop or the mobile apps and, when the server allows it, let someone ask for an account. Single use, a
 * week long; the code is only shown on making it.
 */
@Component({
  selector: 'app-my-invitations',
  imports: [
    FormsModule,
    BrnDialogContent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    TranslatePipe,
    RelativeTimePipe,
    Invitation,
  ],
  template: `
    <section hlmCard data-tour="account-invitations">
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'invitations.title' | t }}</h2>
        <p hlmCardDescription>{{ 'invitations.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-4">
        @if (loaded() && !canInvite()) {
          <p class="text-sm text-muted-foreground">{{ 'invitations.needsPublicUrl' | t }}</p>
        } @else {
          <form class="flex flex-wrap items-end gap-2" (ngSubmit)="create()">
            <hlm-field class="min-w-0 flex-1">
              <label hlmFieldLabel for="invitation-note">{{ 'invitations.note' | t }}</label>
              <input
                hlmInput
                id="invitation-note"
                name="note"
                maxlength="200"
                [placeholder]="'invitations.notePlaceholder' | t"
                [(ngModel)]="note"
              />
            </hlm-field>
            <button hlmBtn type="submit" [disabled]="busy() || !loaded()">
              {{ 'invitations.create' | t }}
            </button>
          </form>
          <p class="text-xs text-muted-foreground">
            {{ (signup() ? 'invitations.signupOn' : 'invitations.signupOff') | t }}
          </p>
        }
        @if (items().length) {
          <ul class="grid grid-cols-[minmax(0,1fr)] gap-2">
            @for (item of items(); track item.id) {
              <li class="flex items-center justify-between gap-3 rounded-md border p-3 text-sm">
                <div class="grid min-w-0 gap-0.5">
                  <p class="flex flex-wrap items-center gap-2">
                    <span class="font-medium">{{ item.note || ('invitations.untitled' | t) }}</span>
                    <span hlmBadge [variant]="item.state === 'PENDING' ? 'secondary' : 'outline'">
                      {{ 'invitations.state.' + item.state | t }}
                    </span>
                  </p>
                  <p class="text-xs text-muted-foreground">
                    @if (item.usedBy) {
                      {{ 'invitations.usedBy' | t: { name: item.usedBy } }}
                    } @else if (item.state === 'PENDING') {
                      {{ 'invitation.expires' | t: { when: (item.expiresAt | relativeTime) } }}
                    } @else {
                      {{ 'invitations.made' | t: { when: (item.createdAt | relativeTime) } }}
                    }
                  </p>
                </div>
                @if (item.state === 'PENDING') {
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="revoke(item)"
                  >
                    {{ 'invitations.revoke' | t }}
                  </button>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <hlm-dialog [state]="created() ? 'open' : 'closed'" (closed)="created.set(null)">
      <hlm-dialog-content *brnDialogContent="let ctx" class="w-[min(28rem,calc(100vw-2rem))]">
        @if (created(); as invitation) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'invitations.created' | t }}</h3>
            <p hlmDialogDescription>
              {{ (invitation.signupAllowed ? 'invitations.howWithSignup' : 'invitations.how') | t }}
            </p>
          </hlm-dialog-header>
          <app-invitation [link]="invitation.url" [expiresAt]="invitation.expiresAt" />
          <div class="grid gap-1 text-center text-sm">
            <p class="text-muted-foreground">{{ 'invitations.orType' | t }}</p>
            <p class="break-all font-mono">{{ invitation.serverUrl }}</p>
            <p class="font-mono text-lg tracking-widest">{{ invitation.code }}</p>
          </div>
          <p class="text-xs text-muted-foreground">{{ 'invitations.once' | t }}</p>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MyInvitations implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected note = '';
  protected readonly items = signal<Item[]>([]);
  protected readonly canInvite = signal(false);
  protected readonly signup = signal(false);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly created = signal<Created['createInvitation'] | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      const result = await this.gql.query(InvitationsQuery, { all: false });
      this.items.set(result.invitations);
      this.canInvite.set(result.sharing.canInvite);
      this.signup.set(result.sharing.invitationSignup);
    } catch {
      this.items.set([]);
    } finally {
      this.loaded.set(true);
    }
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(CreateInvitationMutation, {
        input: { note: this.note.trim() || null },
      });
      this.created.set(result.createInvitation);
      this.note = '';
      await this.load();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(item: Item): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(RevokeInvitationMutation, { id: item.id });
      toast.success(this.i18n.t('invitations.revoked'));
      await this.load();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
