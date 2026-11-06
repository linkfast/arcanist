import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { firstValueFrom } from 'rxjs';
import { ApiError } from '../../core/api/rest';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { Edition } from '../../core/server/edition';
import { Brand } from '../../shared/brand';
import { DateTimePipe } from '../../shared/format';
import { LanguageSelect } from '../../shared/language-select';
import { ThemeSelect } from '../../shared/theme-select';

interface InvitationPreview {
  serverUrl: string;
  invitedBy: string;
  expiresAt: string;
  signupAllowed: boolean;
}

/**
 * Where an invitation link opens (`/admin/join#CODE`): how to add this server to an Ultimate app, and, when the server
 * allows it, a form to ask for an account, which waits for an administrator. The code stays after the `#`, so it
 * never reaches the server's logs; it is sent in a request body.
 */
@Component({
  selector: 'app-join-page',
  imports: [
    FormsModule,
    RouterLink,
    Brand,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    LanguageSelect,
    ThemeSelect,
    TranslatePipe,
    DateTimePipe,
  ],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-muted/40 p-4">
      <section hlmCard class="w-full max-w-md">
        <div hlmCardHeader>
          <h1 hlmCardTitle class="mb-2"><app-brand [edition]="edition.label()" /></h1>
        </div>
        <div hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-4">
          @switch (stage()) {
            @case ('invalid') {
              <div hlmAlert variant="destructive">
                <p hlmAlertTitle>{{ 'join.invalidTitle' | t }}</p>
                <p hlmAlertDescription>{{ 'join.invalid' | t }}</p>
              </div>
            }
            @case ('ready') {
              @if (preview(); as invitation) {
                <h2 class="text-lg font-semibold break-words">
                  {{ 'join.title' | t: { name: invitation.invitedBy } }}
                </h2>
                <div class="grid gap-2 rounded-md border p-3 text-sm">
                  <p class="font-medium">{{ 'join.appsTitle' | t }}</p>
                  <p class="text-muted-foreground">{{ 'join.apps' | t }}</p>
                  <div class="flex gap-2">
                    <input
                      hlmInput
                      readonly
                      class="min-w-0 flex-1 font-mono text-xs"
                      [attr.aria-label]="'join.link' | t"
                      [value]="link"
                    />
                    <button hlmBtn variant="outline" type="button" (click)="copy()">
                      {{ 'invitation.copy' | t }}
                    </button>
                  </div>
                  <p class="text-xs text-muted-foreground">
                    {{ 'join.orType' | t: { url: invitation.serverUrl, code: code } }}
                  </p>
                </div>
                @if (invitation.signupAllowed) {
                  <form class="grid gap-3" (ngSubmit)="signUp()">
                    <p class="text-sm font-medium">{{ 'join.signupTitle' | t }}</p>
                    <p class="text-sm text-muted-foreground">{{ 'join.signupHelp' | t }}</p>
                    <hlm-field>
                      <label hlmFieldLabel for="join-user">{{ 'join.userName' | t }}</label>
                      <input
                        hlmInput
                        id="join-user"
                        name="userName"
                        autocomplete="username"
                        required
                        [(ngModel)]="userName"
                      />
                    </hlm-field>
                    <hlm-field>
                      <label hlmFieldLabel for="join-name">{{ 'join.displayName' | t }}</label>
                      <input
                        hlmInput
                        id="join-name"
                        name="displayName"
                        autocomplete="name"
                        [(ngModel)]="displayName"
                      />
                    </hlm-field>
                    <hlm-field>
                      <label hlmFieldLabel for="join-password">{{ 'join.password' | t }}</label>
                      <input
                        hlmInput
                        id="join-password"
                        name="password"
                        type="password"
                        autocomplete="new-password"
                        required
                        [(ngModel)]="password"
                      />
                    </hlm-field>
                    <hlm-field>
                      <label hlmFieldLabel for="join-confirm">{{ 'join.confirm' | t }}</label>
                      <input
                        hlmInput
                        id="join-confirm"
                        name="confirm"
                        type="password"
                        autocomplete="new-password"
                        required
                        [(ngModel)]="confirm"
                      />
                    </hlm-field>
                    @if (error()) {
                      <p class="text-sm text-destructive" role="alert">{{ error() }}</p>
                    }
                    <button hlmBtn type="submit" [disabled]="busy()">
                      {{ 'join.signup' | t }}
                    </button>
                    <p
                      class="text-xs text-muted-foreground"
                      [title]="invitation.expiresAt | dateTime"
                    >
                      {{ 'join.once' | t }}
                    </p>
                  </form>
                } @else {
                  <p class="text-sm text-muted-foreground">{{ 'join.noSignup' | t }}</p>
                }
                <a hlmBtn variant="ghost" routerLink="/login">{{ 'join.haveAccount' | t }}</a>
              }
            }
            @case ('done') {
              <div hlmAlert>
                <p hlmAlertTitle>{{ 'join.doneTitle' | t }}</p>
                <p hlmAlertDescription>{{ 'join.done' | t: { name: userName.trim() } }}</p>
              </div>
            }
            @default {
              <p class="text-sm text-muted-foreground">{{ 'join.loading' | t }}</p>
            }
          }
        </div>
        <div hlmCardFooter class="justify-end gap-2">
          <app-theme-select />
          <app-language-select />
        </div>
      </section>
    </main>
  `,
})
export class JoinPage implements OnInit {
  protected readonly edition = inject(Edition);
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);

  /** The code, after the `#` of the link. */
  protected readonly code = decodeURIComponent(location.hash.replace(/^#/, '')).trim();
  protected readonly link = location.href;
  protected readonly stage = signal<'loading' | 'invalid' | 'ready' | 'done'>('loading');
  protected readonly preview = signal<InvitationPreview | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected userName = '';
  protected displayName = '';
  protected password = '';
  protected confirm = '';

  async ngOnInit(): Promise<void> {
    void this.edition.refresh();
    if (!this.code) {
      this.stage.set('invalid');
      return;
    }
    try {
      this.preview.set(
        await firstValueFrom(
          this.http.post<InvitationPreview>('/rest/v1/public/invitations/check', {
            code: this.code,
          }),
        ),
      );
      this.stage.set('ready');
    } catch {
      this.stage.set('invalid');
    }
  }

  protected async signUp(): Promise<void> {
    this.error.set(null);
    if (this.password !== this.confirm) {
      this.error.set(this.i18n.t('join.mismatch'));
      return;
    }
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.http.post('/rest/v1/public/invitations/signup', {
          code: this.code,
          userName: this.userName.trim(),
          password: this.password,
          displayName: this.displayName.trim() || null,
        }),
      );
      this.password = this.confirm = '';
      this.stage.set('done');
    } catch (error) {
      const apiError = ApiError.from(error);
      const field = Object.values(apiError.fields)[0]?.[0];
      this.error.set(this.i18n.error(field ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link);
      toast.success(this.i18n.t('invitation.copied'));
    } catch {
      toast.error(this.i18n.t('invitation.copyFailed'));
    }
  }
}
