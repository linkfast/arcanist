import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { BrowserLogin, BrowserLoginCheck } from '../../core/auth/browser-login';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { Edition } from '../../core/server/edition';
import { Brand } from '../../shared/brand';
import { LanguageSelect } from '../../shared/language-select';
import { ThemeSelect } from '../../shared/theme-select';

/**
 * An app opens this page in the browser to sign in here, where passkeys work: the login is the panel's own, and the
 * page always asks before handing the app a code, even with a session already open.
 */
@Component({
  selector: 'app-authorize-page',
  imports: [
    Brand,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    LanguageSelect,
    ThemeSelect,
    TranslatePipe,
  ],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-muted/40 p-4">
      <section hlmCard class="w-full max-w-md">
        <div hlmCardHeader>
          <h1 hlmCardTitle class="mb-2"><app-brand [edition]="edition.label()" /></h1>
        </div>
        <div hlmCardContent class="grid gap-4">
          @if (error()) {
            <div hlmAlert variant="destructive">
              <p hlmAlertDescription>{{ error() }}</p>
            </div>
          }
          @switch (stage()) {
            @case ('invalid') {
              <div hlmAlert variant="destructive">
                <p hlmAlertTitle>{{ 'browserLogin.invalidTitle' | t }}</p>
                <p hlmAlertDescription>{{ 'browserLogin.invalid' | t }}</p>
              </div>
            }
            @case ('consent') {
              @if (check(); as link) {
                <h2 class="text-lg font-semibold break-words">
                  {{ 'browserLogin.title' | t: { client: link.clientName } }}
                </h2>
                <p class="text-sm">
                  {{ 'browserLogin.asUser' | t: { user: auth.user()?.userName ?? '' } }}
                </p>
                <p class="text-sm text-muted-foreground">
                  {{ destination() | t: { scheme: link.scheme ?? '' } }}
                </p>
                <p class="text-sm text-muted-foreground">{{ 'browserLogin.warning' | t }}</p>
                <div class="grid gap-2 sm:grid-cols-2">
                  <button
                    hlmBtn
                    type="button"
                    variant="outline"
                    [disabled]="busy()"
                    (click)="deny(link)"
                  >
                    {{ 'browserLogin.deny' | t }}
                  </button>
                  <button hlmBtn type="button" [disabled]="busy()" (click)="approve()">
                    {{ 'browserLogin.allow' | t }}
                  </button>
                </div>
                <button
                  hlmBtn
                  type="button"
                  variant="ghost"
                  [disabled]="busy()"
                  (click)="switchUser()"
                >
                  {{ 'browserLogin.switchUser' | t }}
                </button>
              }
            }
            @case ('approved') {
              <p class="text-sm">{{ 'browserLogin.approved' | t }}</p>
            }
            @case ('denied') {
              <p class="text-sm">{{ 'browserLogin.denied' | t }}</p>
            }
            @default {
              <p class="text-sm text-muted-foreground">{{ 'browserLogin.loading' | t }}</p>
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
export class AuthorizePage implements OnInit {
  protected readonly edition = inject(Edition);
  protected readonly auth = inject(AuthService);
  private readonly browser = inject(BrowserLogin);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18n);

  readonly clientName = input<string>();
  readonly redirectUri = input<string>();
  readonly codeChallenge = input<string>();
  readonly state = input<string>();
  readonly deviceName = input<string>();

  protected readonly stage = signal<'loading' | 'invalid' | 'consent' | 'approved' | 'denied'>(
    'loading',
  );
  protected readonly check = signal<BrowserLoginCheck | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly destination = computed(() =>
    this.check()?.target === 'APP' ? 'browserLogin.toApp' : 'browserLogin.toThisDevice',
  );

  async ngOnInit(): Promise<void> {
    void this.edition.refresh();
    // Before anyone types a password: a link that cannot be honoured says so first.
    try {
      this.check.set(await this.browser.check(this.request()));
    } catch {
      this.stage.set('invalid');
      return;
    }
    if (this.auth.isAuthenticated() || (await this.auth.refresh())) this.stage.set('consent');
    else await this.signIn();
  }

  protected async approve(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const url = await this.browser.approve(this.request());
      this.stage.set('approved');
      window.location.assign(url);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected deny(link: BrowserLoginCheck): void {
    this.stage.set('denied');
    window.location.assign(link.denyUrl);
  }

  protected async switchUser(): Promise<void> {
    const here = this.router.url;
    await this.auth.logout();
    await this.signIn(here);
  }

  private signIn(returnUrl = this.router.url): Promise<boolean> {
    return this.router.navigate(['/login'], { queryParams: { returnUrl } });
  }

  private request() {
    return {
      clientName: this.clientName(),
      redirectUri: this.redirectUri(),
      codeChallenge: this.codeChallenge(),
      state: this.state(),
      deviceName: this.deviceName(),
    };
  }
}
