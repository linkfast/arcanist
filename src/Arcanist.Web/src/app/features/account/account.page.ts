import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MySessionsQuery as SessionsResult,
  ServerInfoQuery as ServerInfoResult,
} from '../../graphql/generated/graphql';
import {
  ChangeMyPasswordMutation,
  MySessionsQuery,
  RevokeMySessionMutation,
  ServerInfoQuery,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';

type Session = SessionsResult['mySessions'][number];

@Component({
  selector: 'app-account-page',
  imports: [
    FormsModule,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmFieldImports,
    HlmInputImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'account.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">
        {{ auth.user()?.userName }} · {{ 'users.roles.' + (auth.user()?.role ?? 'USER') | t }}
      </p>
    </header>

    <div class="grid gap-6 xl:grid-cols-2">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'account.changePassword' | t }}</h2>
          <p hlmCardDescription>{{ 'account.changePasswordHelp' | t }}</p>
        </div>
        <form hlmCardContent class="grid gap-4" (ngSubmit)="changePassword()">
          <hlm-field>
            <label hlmFieldLabel for="current">{{ 'account.currentPassword' | t }}</label>
            <input
              hlmInput
              id="current"
              name="current"
              type="password"
              autocomplete="current-password"
              [(ngModel)]="currentPassword"
            />
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel for="next">{{ 'account.newPassword' | t }}</label>
            <input
              hlmInput
              id="next"
              name="next"
              type="password"
              autocomplete="new-password"
              [(ngModel)]="newPassword"
            />
            <p hlmFieldDescription>{{ 'setup.admin.passwordHelp' | t }}</p>
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel for="confirm">{{ 'common.confirmPassword' | t }}</label>
            <input
              hlmInput
              id="confirm"
              name="confirm"
              type="password"
              autocomplete="new-password"
              [(ngModel)]="confirmation"
            />
          </hlm-field>
          @if (passwordError()) {
            <p class="text-sm text-destructive">{{ passwordError() }}</p>
          }
          <button
            hlmBtn
            type="submit"
            class="justify-self-start"
            [disabled]="
              busy() || !currentPassword || newPassword.length < 8 || newPassword !== confirmation
            "
          >
            {{ 'account.changePassword' | t }}
          </button>
        </form>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'account.sessions' | t }}</h2>
          <p hlmCardDescription>{{ 'account.sessionsHelp' | t }}</p>
        </div>
        <ul hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-3">
          @for (session of sessions(); track session.id) {
            <li class="flex min-w-0 items-start justify-between gap-3 rounded-md border p-3">
              <div class="min-w-0 text-sm">
                <p class="font-medium">
                  {{ session.deviceName || ('account.unknownDevice' | t) }}
                  @if (session.isCurrent) {
                    <span hlmBadge variant="secondary" class="ml-1">{{
                      'account.thisDevice' | t
                    }}</span>
                  }
                </p>
                <p class="truncate text-xs text-muted-foreground" [title]="session.userAgent ?? ''">
                  {{ session.userAgent }}
                </p>
                <p class="text-xs text-muted-foreground">
                  {{ session.ipAddress }} · {{ 'account.lastSeen' | t }}
                  <span [title]="session.lastSeenAt | dateTime">{{
                    session.lastSeenAt | relativeTime
                  }}</span>
                </p>
              </div>
              @if (!session.isCurrent) {
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="busy()"
                  (click)="revoke(session)"
                >
                  {{ 'account.signOut' | t }}
                </button>
              }
            </li>
          }
        </ul>
      </section>

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'account.about' | t }}</h2>
        </div>
        <div hlmCardContent class="grid gap-1 text-sm">
          @if (server(); as info) {
            <p>Arcanist {{ info.version }} · API v{{ info.apiVersion }}</p>
            <p class="text-muted-foreground">{{ 'overview.license' | t }}</p>
            @if (info.sourceUrl) {
              <a class="underline" [href]="info.sourceUrl" target="_blank" rel="noopener">{{
                'overview.sourceCode' | t
              }}</a>
            }
            <p class="mt-2 flex flex-wrap gap-3">
              <a class="underline" href="/rest/docs" target="_blank" rel="noopener">REST API</a>
              <a class="underline" href="/graphql/ui" target="_blank" rel="noopener">GraphQL</a>
            </p>
          }
        </div>
      </section>
    </div>
  `,
})
export class AccountPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly sessions = signal<Session[]>([]);
  protected readonly server = signal<ServerInfoResult['serverInfo'] | null>(null);
  protected readonly busy = signal(false);
  protected readonly passwordError = signal<string | null>(null);

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmation = '';

  async ngOnInit(): Promise<void> {
    const [sessions, server] = await Promise.all([
      this.gql.query(MySessionsQuery),
      this.gql.query(ServerInfoQuery),
    ]);
    this.sessions.set(sessions.mySessions);
    this.server.set(server.serverInfo);
  }

  protected async changePassword(): Promise<void> {
    this.busy.set(true);
    this.passwordError.set(null);
    try {
      await this.gql.mutate(ChangeMyPasswordMutation, {
        input: { currentPassword: this.currentPassword, newPassword: this.newPassword },
      });
      this.currentPassword = this.newPassword = this.confirmation = '';
      toast.success(this.i18n.t('account.passwordChanged'));
      this.sessions.set((await this.gql.query(MySessionsQuery)).mySessions);
    } catch (error) {
      const apiError = ApiError.from(error);
      this.passwordError.set(
        this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code),
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(session: Session): Promise<void> {
    this.busy.set(true);
    try {
      await this.gql.mutate(RevokeMySessionMutation, { id: session.id });
      this.sessions.update((sessions) => sessions.filter((s) => s.id !== session.id));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
