import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { AuthService } from '../../core/auth/auth.service';
import { BrowserLogin, DeviceLoginDetails } from '../../core/auth/browser-login';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { Edition } from '../../core/server/edition';
import { Brand } from '../../shared/brand';
import { LanguageSelect } from '../../shared/language-select';
import { ThemeSelect } from '../../shared/theme-select';

/** A device without a browser of its own shows a code, and someone signed in here lets it in. */
@Component({
  selector: 'app-device-page',
  imports: [
    Brand,
    FormsModule,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
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
          @if (answered(); as answer) {
            <p class="text-sm">{{ 'browserLogin.device.' + answer | t }}</p>
          } @else if (device(); as found) {
            <h2 class="text-lg font-semibold break-words">
              {{ 'browserLogin.title' | t: { client: found.clientName } }}
            </h2>
            <p class="text-sm">
              {{ 'browserLogin.asUser' | t: { user: auth.user()?.userName ?? '' } }}
            </p>
            <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
              <dt class="text-muted-foreground">{{ 'browserLogin.device.code' | t }}</dt>
              <dd class="font-mono">{{ found.userCode }}</dd>
              @if (found.deviceName) {
                <dt class="text-muted-foreground">{{ 'browserLogin.device.name' | t }}</dt>
                <dd class="break-words">{{ found.deviceName }}</dd>
              }
              @if (found.ipAddress) {
                <dt class="text-muted-foreground">{{ 'browserLogin.device.address' | t }}</dt>
                <dd class="font-mono">{{ found.ipAddress }}</dd>
              }
            </dl>
            <p class="text-sm text-muted-foreground">{{ 'browserLogin.device.warning' | t }}</p>
            <div class="grid gap-2 sm:grid-cols-2">
              <button
                hlmBtn
                type="button"
                variant="outline"
                [disabled]="busy()"
                (click)="deny(found)"
              >
                {{ 'browserLogin.deny' | t }}
              </button>
              <button hlmBtn type="button" [disabled]="busy()" (click)="approve(found)">
                {{ 'browserLogin.allow' | t }}
              </button>
            </div>
          } @else {
            <p class="text-sm text-muted-foreground">{{ 'browserLogin.device.help' | t }}</p>
            <form class="grid gap-4" (ngSubmit)="lookUp()">
              <hlm-field>
                <label hlmFieldLabel for="userCode">{{ 'browserLogin.device.code' | t }}</label>
                <input
                  hlmInput
                  id="userCode"
                  name="userCode"
                  class="font-mono uppercase"
                  autocomplete="off"
                  autofocus
                  [(ngModel)]="userCode"
                />
              </hlm-field>
              <button hlmBtn type="submit" class="w-full" [disabled]="busy() || !userCode.trim()">
                {{ 'browserLogin.continue' | t }}
              </button>
            </form>
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
export class DevicePage implements OnInit {
  protected readonly edition = inject(Edition);
  protected readonly auth = inject(AuthService);
  private readonly browser = inject(BrowserLogin);
  private readonly i18n = inject(I18n);

  /** The code, when it came in the link. */
  readonly code = input<string>();

  protected userCode = '';
  protected readonly device = signal<DeviceLoginDetails | null>(null);
  protected readonly answered = signal<'approved' | 'denied' | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.edition.refresh();
    // Only filled in: going on is the person's own step, comparing it with what their device shows.
    this.userCode = this.code() ?? '';
  }

  protected lookUp(): Promise<void> {
    return this.run(async () => this.device.set(await this.browser.lookUpDevice(this.userCode)));
  }

  protected approve(found: DeviceLoginDetails): Promise<void> {
    return this.run(async () => {
      await this.browser.approveDevice(found.userCode);
      this.answered.set('approved');
    });
  }

  protected deny(found: DeviceLoginDetails): Promise<void> {
    return this.run(async () => {
      await this.browser.denyDevice(found.userCode);
      this.answered.set('denied');
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      const code = ApiError.from(error).code;
      this.error.set(this.i18n.error(code));
      if (code === 'browser_login_expired') this.device.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}
