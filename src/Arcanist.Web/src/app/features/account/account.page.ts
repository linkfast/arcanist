import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { provideIcons } from '@ng-icons/core';
import {
  lucideCloud,
  lucideInfo,
  lucideSettings2,
  lucideShieldCheck,
  lucideSmartphone,
} from '@ng-icons/lucide';
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
import { AuthService } from '../../core/auth/auth.service';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  AppPasswordFieldsFragment as AppPassword,
  MySessionsQuery as SessionsResult,
  ServerInfoQuery as ServerInfoResult,
} from '../../graphql/generated/graphql';
import {
  AppPasswordsQuery,
  ChangeMyPasswordMutation,
  CreateAppPasswordMutation,
  DeleteAppPasswordMutation,
  MySessionsQuery,
  RevokeMySessionMutation,
  ServerInfoQuery,
  SharingQuery,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { LanguageSelect } from '../../shared/language-select';
import {
  PanelSection,
  SectionLayout,
  activeSection,
  wideScreen,
} from '../../shared/section-layout';
import { ThemeSelect } from '../../shared/theme-select';
import { MyCloud } from './my-cloud';
import { MyDevices } from './my-devices';
import { MyDlna } from './my-dlna';
import { MySecondFactor } from './my-second-factor';
import { MyTours } from './my-tours';
import { MyVideos } from './my-videos';
import { describeDevice } from './user-agent';

type Session = SessionsResult['mySessions'][number];

/** What is about to be cut off, waiting for the user to confirm it. */
type Pending =
  | { kind: 'session'; session: Session }
  | { kind: 'others'; count: number }
  | { kind: 'appPassword'; password: AppPassword };

function section(id: string, icon: string): PanelSection {
  return { id, icon, label: `account.sections.${id}`, hint: `account.sections.${id}Hint` };
}

/** What is each person's own on this server, in sections: security, apps and devices, their cloud, preferences. */
@Component({
  selector: 'app-account-page',
  imports: [
    FormsModule,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmCardImports,
    HlmButtonImports,
    HlmBadgeImports,
    HlmFieldImports,
    HlmInputImports,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmSpinnerImports,
    SectionLayout,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
    LanguageSelect,
    ThemeSelect,
    MyCloud,
    MyDevices,
    MyDlna,
    MySecondFactor,
    MyVideos,
    MyTours,
  ],
  providers: [
    provideIcons({
      lucideShieldCheck,
      lucideSmartphone,
      lucideCloud,
      lucideSettings2,
      lucideInfo,
    }),
  ],
  template: `
    <app-section-layout
      heading="account.title"
      [subtitle]="
        (auth.user()?.userName ?? '') + ' · ' + ('users.roles.' + (auth.user()?.role ?? 'USER') | t)
      "
      base="/account"
      [sections]="sections()"
      [active]="active()"
    >
      @switch (active()?.id) {
        @case ('security') {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'account.password' | t }}</h2>
              <p hlmCardDescription>{{ 'account.changePasswordHelp' | t }}</p>
            </div>
            <div hlmCardContent>
              <button hlmBtn variant="outline" type="button" (click)="openPassword()">
                {{ 'account.changePassword' | t }}
              </button>
            </div>
          </section>

          <app-my-second-factor />

          <section hlmCard data-tour="account-sessions">
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'account.sessions' | t }}</h2>
              <p hlmCardDescription>{{ 'account.sessionsHelp' | t }}</p>
            </div>
            <div hlmCardContent class="grid gap-3">
              @if (sessions() === null) {
                <hlm-spinner class="size-5 justify-self-center" />
              } @else {
                <ul class="grid grid-cols-[minmax(0,1fr)] gap-3">
                  @for (session of sortedSessions(); track session.id) {
                    <li
                      class="flex min-w-0 items-start justify-between gap-3 rounded-md border p-3"
                    >
                      <div class="min-w-0 text-sm">
                        <p class="font-medium">
                          {{ device(session) }}
                          @if (session.isCurrent) {
                            <span hlmBadge variant="secondary" class="ml-1">{{
                              'account.thisDevice' | t
                            }}</span>
                          }
                        </p>
                        <p class="text-xs text-muted-foreground" [title]="session.userAgent ?? ''">
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
                          (click)="pending.set({ kind: 'session', session })"
                        >
                          {{ 'account.signOut' | t }}
                        </button>
                      }
                    </li>
                  }
                </ul>
                @if (others() > 1) {
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    type="button"
                    class="justify-self-start"
                    [disabled]="busy()"
                    (click)="pending.set({ kind: 'others', count: others() })"
                  >
                    {{ 'account.signOutOthers' | t }}
                  </button>
                }
              }
            </div>
          </section>
        }

        @case ('apps') {
          <section hlmCard data-tour="account-app-passwords">
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'account.appPasswords.title' | t }}</h2>
              <p hlmCardDescription>{{ 'account.appPasswords.description' | t }}</p>
            </div>
            <div hlmCardContent class="grid gap-4">
              <form class="flex flex-wrap items-end gap-2" (ngSubmit)="createAppPassword()">
                <hlm-field class="min-w-48 flex-1">
                  <label hlmFieldLabel for="appName">{{ 'account.appPasswords.name' | t }}</label>
                  <input
                    hlmInput
                    id="appName"
                    name="appName"
                    maxlength="100"
                    [placeholder]="'account.appPasswords.namePlaceholder' | t"
                    [(ngModel)]="appName"
                  />
                </hlm-field>
                <button hlmBtn type="submit" [disabled]="busy() || !appName.trim()">
                  {{ 'account.appPasswords.create' | t }}
                </button>
              </form>
              @if (appPasswords() === null) {
                <hlm-spinner class="size-5 justify-self-center" />
              } @else {
                <ul class="grid grid-cols-[minmax(0,1fr)] gap-2">
                  @for (password of appPasswords(); track password.id) {
                    <li
                      class="flex min-w-0 items-center justify-between gap-3 rounded-md border p-3"
                    >
                      <div class="min-w-0 text-sm">
                        <p class="truncate font-medium">{{ password.name }}</p>
                        <p class="text-xs text-muted-foreground">
                          {{ 'account.appPasswords.createdAt' | t }}
                          <span [title]="password.createdAt | dateTime">{{
                            password.createdAt | relativeTime
                          }}</span>
                          ·
                          @if (password.lastUsedAt) {
                            {{ 'account.lastSeen' | t }}
                            <span [title]="password.lastUsedAt | dateTime">{{
                              password.lastUsedAt | relativeTime
                            }}</span>
                          } @else {
                            {{ 'account.appPasswords.neverUsed' | t }}
                          }
                        </p>
                      </div>
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        type="button"
                        [disabled]="busy()"
                        (click)="pending.set({ kind: 'appPassword', password })"
                      >
                        {{ 'account.appPasswords.revoke' | t }}
                      </button>
                    </li>
                  } @empty {
                    <li class="text-sm text-muted-foreground">
                      {{ 'account.appPasswords.none' | t }}
                    </li>
                  }
                </ul>
              }
            </div>
          </section>
          <app-my-devices />
        }

        @case ('cloud') {
          <app-my-cloud />
        }

        @case ('preferences') {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'account.appearance' | t }}</h2>
              <p hlmCardDescription>{{ 'account.appearanceHelp' | t }}</p>
            </div>
            <div hlmCardContent class="flex flex-wrap gap-3">
              <app-language-select />
              <app-theme-select />
            </div>
          </section>
          <app-my-videos />
          <app-my-dlna />
          <app-my-tours />
        }

        @case ('about') {
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'account.about' | t }}</h2>
            </div>
            <div hlmCardContent class="grid gap-1 text-sm">
              @if (server(); as info) {
                <p>Arcanist {{ info.version }} · API v{{ info.apiVersion }}</p>
                <p class="text-muted-foreground">{{ 'overview.madeBy' | t }}</p>
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
              } @else {
                <hlm-spinner class="size-5 justify-self-center" />
              }
            </div>
          </section>
        }
      }
    </app-section-layout>

    <!-- Changing the password -->
    <hlm-dialog
      [state]="changingPassword() ? 'open' : 'closed'"
      (closed)="changingPassword.set(false)"
    >
      <hlm-dialog-content *brnDialogContent="let ctx" class="sm:max-w-sm">
        <form class="grid gap-4" (ngSubmit)="changePassword()">
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'account.changePassword' | t }}</h3>
            <p hlmDialogDescription>{{ 'account.changePasswordHelp' | t }}</p>
          </hlm-dialog-header>
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
            <p
              hlmFieldDescription
              [class.text-destructive]="newPassword.length > 0 && newPassword.length < 8"
            >
              {{ 'setup.admin.passwordHelp' | t }}
            </p>
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
            @if (confirmation && newPassword !== confirmation) {
              <p class="text-sm text-destructive">{{ 'account.passwordsDiffer' | t }}</p>
            }
          </hlm-field>
          @if (passwordError()) {
            <p class="text-sm text-destructive" role="alert">{{ passwordError() }}</p>
          }
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="changingPassword.set(false)">
              {{ 'common.cancel' | t }}
            </button>
            <button
              hlmBtn
              type="submit"
              [disabled]="
                busy() || !currentPassword || newPassword.length < 8 || newPassword !== confirmation
              "
            >
              {{ 'account.changePassword' | t }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- An app password just created: shown only now, so it does not close by a click outside. -->
    <hlm-alert-dialog [state]="created() ? 'open' : 'closed'" (closed)="created.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (created(); as secret) {
          <hlm-alert-dialog-header>
            <h3 hlmAlertDialogTitle>{{ 'account.appPasswords.createdTitle' | t }}</h3>
            <p hlmAlertDialogDescription>{{ 'account.appPasswords.created' | t }}</p>
          </hlm-alert-dialog-header>
          <div class="flex flex-wrap items-center gap-2">
            <code class="rounded bg-muted px-2 py-1 font-mono text-base break-all">{{
              secret
            }}</code>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="copy(secret)">
              {{ 'common.copy' | t }}
            </button>
          </div>
          <p class="text-sm text-muted-foreground">
            {{
              'account.appPasswords.howTo'
                | t: { server: serverUrl(), user: auth.user()?.userName ?? '' }
            }}
          </p>
          <hlm-alert-dialog-footer>
            <button hlmBtn type="button" (click)="created.set(null)">
              {{ 'common.done' | t }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>

    <!-- Confirming what cuts something off -->
    <hlm-alert-dialog [state]="pending() ? 'open' : 'closed'" (closed)="pending.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        @if (pending(); as what) {
          <hlm-alert-dialog-header>
            @switch (what.kind) {
              @case ('session') {
                <h3 hlmAlertDialogTitle>
                  {{ 'account.confirmSignOut' | t: { device: device(what.session) } }}
                </h3>
                <p hlmAlertDialogDescription>{{ 'account.confirmSignOutHelp' | t }}</p>
              }
              @case ('others') {
                <h3 hlmAlertDialogTitle>
                  {{ 'account.confirmOthers' | t: { count: what.count } }}
                </h3>
                <p hlmAlertDialogDescription>{{ 'account.confirmSignOutHelp' | t }}</p>
              }
              @case ('appPassword') {
                <h3 hlmAlertDialogTitle>
                  {{ 'account.appPasswords.confirm' | t: { name: what.password.name } }}
                </h3>
                <p hlmAlertDialogDescription>{{ 'account.appPasswords.confirmHelp' | t }}</p>
              }
            }
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel type="button" (click)="pending.set(null)">
              {{ 'common.cancel' | t }}
            </button>
            <button hlmAlertDialogAction type="button" (click)="confirm(what)">
              {{
                (what.kind === 'appPassword' ? 'account.appPasswords.revoke' : 'account.signOut')
                  | t
              }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class AccountPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly extensions = inject(EnabledExtensions);

  /** From the URL (`/account/<section>`). */
  readonly section = input<string>();

  private readonly wide = wideScreen();

  protected readonly sections = computed<PanelSection[]>(() => [
    section('security', 'lucideShieldCheck'),
    section('apps', 'lucideSmartphone'),
    ...(this.extensions.has('ultimate-cloud') ? [section('cloud', 'lucideCloud')] : []),
    section('preferences', 'lucideSettings2'),
    section('about', 'lucideInfo'),
  ]);

  protected readonly active = computed(() =>
    activeSection(this.sections(), this.section(), this.wide()),
  );

  /** Null until they load, so an empty list only says so when it is. */
  protected readonly sessions = signal<Session[] | null>(null);
  protected readonly appPasswords = signal<AppPassword[] | null>(null);
  protected readonly server = signal<ServerInfoResult['serverInfo'] | null>(null);
  protected readonly busy = signal(false);

  protected readonly sortedSessions = computed(() =>
    [...(this.sessions() ?? [])].sort((a, b) => Number(b.isCurrent) - Number(a.isCurrent)),
  );
  protected readonly others = computed(
    () => (this.sessions() ?? []).filter((s) => !s.isCurrent).length,
  );

  protected readonly pending = signal<Pending | null>(null);

  /** The secret of the app password just created: shown only now. */
  protected readonly created = signal<string | null>(null);
  /** The address apps connect to: the public one when the server has it. */
  protected readonly serverUrl = signal(window.location.origin);
  protected appName = '';

  protected readonly changingPassword = signal(false);
  protected readonly passwordError = signal<string | null>(null);
  protected currentPassword = '';
  protected newPassword = '';
  protected confirmation = '';

  async ngOnInit(): Promise<void> {
    const [sessions, server, passwords, sharing] = await Promise.all([
      this.gql.query(MySessionsQuery),
      this.gql.query(ServerInfoQuery),
      this.gql.query(AppPasswordsQuery),
      this.gql.query(SharingQuery),
    ]);
    this.sessions.set(sessions.mySessions);
    this.server.set(server.serverInfo);
    this.appPasswords.set(passwords.appPasswords);
    if (sharing.sharing.publicUrl) this.serverUrl.set(sharing.sharing.publicUrl);
  }

  protected device(session: Session): string {
    const described = describeDevice(session.userAgent);
    const name = session.deviceName || this.i18n.t('account.unknownDevice');
    return described ? `${name} · ${described}` : name;
  }

  protected openPassword(): void {
    this.currentPassword = this.newPassword = this.confirmation = '';
    this.passwordError.set(null);
    this.changingPassword.set(true);
  }

  protected async createAppPassword(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(CreateAppPasswordMutation, {
        name: this.appName.trim(),
      });
      this.appPasswords.update((passwords) => [
        result.createAppPassword.appPassword,
        ...(passwords ?? []),
      ]);
      this.created.set(result.createAppPassword.secret);
      this.appName = '';
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    toast.success(this.i18n.t('common.copied'));
  }

  protected async changePassword(): Promise<void> {
    this.busy.set(true);
    this.passwordError.set(null);
    try {
      await this.gql.mutate(ChangeMyPasswordMutation, {
        input: { currentPassword: this.currentPassword, newPassword: this.newPassword },
      });
      this.currentPassword = this.newPassword = this.confirmation = '';
      this.changingPassword.set(false);
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

  protected async confirm(what: Pending): Promise<void> {
    this.pending.set(null);
    this.busy.set(true);
    try {
      if (what.kind === 'appPassword') {
        await this.gql.mutate(DeleteAppPasswordMutation, { id: what.password.id });
        this.appPasswords.update((all) => all?.filter((p) => p.id !== what.password.id) ?? null);
        return;
      }
      const leaving =
        what.kind === 'session'
          ? [what.session]
          : (this.sessions() ?? []).filter((s) => !s.isCurrent);
      await Promise.all(leaving.map((s) => this.gql.mutate(RevokeMySessionMutation, { id: s.id })));
      const gone = new Set(leaving.map((s) => s.id));
      this.sessions.update((all) => all?.filter((s) => !gone.has(s.id)) ?? null);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
