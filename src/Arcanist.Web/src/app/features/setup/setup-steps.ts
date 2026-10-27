import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideExternalLink, lucidePlus, lucideSparkles, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { TranslatePipe } from '../../core/i18n/i18n';
import {
  SetupExtrasDraft,
  ULTIMATE_EXTENSIONS,
  ULTIMATE_URL,
  WIZARD_EXTENSIONS,
} from './setup-extras';

/** The users of the server: the administrator (always), those it has (running the wizard again) and new ones. */
@Component({
  selector: 'app-setup-users',
  imports: [
    FormsModule,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucidePlus, lucideTrash2 })],
  host: { class: 'grid grid-cols-[minmax(0,1fr)] gap-3' },
  template: `
    <p class="text-sm">{{ 'setup.users.explanation' | t }}</p>
    <ul class="grid gap-2">
      @if (adminName()) {
        <li class="flex flex-wrap items-center gap-2 rounded-lg border bg-muted/40 p-3 text-sm">
          <span class="font-medium">{{ adminName() }}</span>
          <span hlmBadge variant="secondary">{{ 'users.roles.ADMIN' | t }}</span>
        </li>
      }
      @for (user of existing(); track user.userName) {
        <li class="flex flex-wrap items-center gap-2 rounded-lg border bg-muted/40 p-3 text-sm">
          <span class="font-medium">{{ user.userName }}</span>
          @if (user.displayName && user.displayName !== user.userName) {
            <span class="text-xs text-muted-foreground">{{ user.displayName }}</span>
          }
          <span hlmBadge variant="outline">{{ 'users.roles.' + user.role | t }}</span>
          @if (user.origin) {
            <span hlmBadge variant="secondary">{{
              'setup.users.migratedFrom' | t: { server: user.origin }
            }}</span>
          }
        </li>
      }
    </ul>

    @for (user of draft().users(); track $index; let index = $index) {
      <div class="grid grid-cols-[minmax(0,1fr)] gap-3 rounded-lg border p-4">
        <div class="grid gap-3 sm:grid-cols-2">
          <hlm-field>
            <label hlmFieldLabel [for]="'user-name-' + index">{{
              'setup.users.userName' | t
            }}</label>
            <input
              hlmInput
              [id]="'user-name-' + index"
              [name]="'user-name-' + index"
              autocomplete="off"
              [ngModel]="user.userName"
              (ngModelChange)="draft().updateUser(index, { userName: $event })"
            />
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel [for]="'user-display-' + index">{{
              'setup.users.displayName' | t
            }}</label>
            <input
              hlmInput
              [id]="'user-display-' + index"
              [name]="'user-display-' + index"
              [ngModel]="user.displayName"
              (ngModelChange)="draft().updateUser(index, { displayName: $event })"
            />
          </hlm-field>
          <hlm-field>
            <label hlmFieldLabel [for]="'user-password-' + index">{{
              'setup.users.password' | t
            }}</label>
            <input
              hlmInput
              type="password"
              autocomplete="new-password"
              [id]="'user-password-' + index"
              [name]="'user-password-' + index"
              [ngModel]="user.password"
              (ngModelChange)="draft().updateUser(index, { password: $event })"
            />
          </hlm-field>
        </div>
        <div class="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
          <label class="flex items-center gap-2">
            <hlm-switch
              [checked]="user.admin"
              (checkedChange)="draft().updateUser(index, { admin: $event })"
            />
            {{ 'setup.users.admin' | t }}
          </label>
          @if (!user.admin) {
            <label class="flex items-center gap-2">
              <hlm-switch
                [checked]="user.download"
                (checkedChange)="draft().updateUser(index, { download: $event })"
              />
              {{ 'setup.users.download' | t }}
            </label>
          }
        </div>
        @if (!user.admin && restricted().length > 0) {
          <div class="grid gap-1 text-sm">
            <span class="text-muted-foreground">{{ 'setup.users.libraries' | t }}</span>
            @for (library of restricted(); track library) {
              <label class="flex items-center gap-2">
                <hlm-checkbox
                  [checked]="user.libraries.includes(library)"
                  (checkedChange)="toggleLibrary(index, library, $event)"
                />
                {{ library }}
              </label>
            }
          </div>
        }
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          type="button"
          class="justify-self-start gap-1"
          (click)="draft().removeUser(index)"
        >
          <ng-icon name="lucideTrash2" /> {{ 'common.remove' | t }}
        </button>
      </div>
    }

    <button
      hlmBtn
      variant="ghost"
      type="button"
      class="justify-self-start gap-1"
      (click)="draft().addUser()"
    >
      <ng-icon name="lucidePlus" /> {{ 'setup.users.add' | t }}
    </button>
    <p class="text-xs text-muted-foreground">{{ 'setup.users.later' | t }}</p>
  `,
})
export class SetupUsersStep {
  readonly draft = input.required<SetupExtrasDraft>();
  readonly adminName = input<string | null>(null);
  /** Users already on the server, and those an import brings (with `origin`: the server they come from). */
  readonly existing = input<
    { userName: string; displayName?: string | null; role: string; origin?: string }[]
  >([]);
  /** Libraries not visible to everyone: new users see them only when given. */
  readonly restricted = input<string[]>([]);

  protected toggleLibrary(index: number, library: string, on: boolean): void {
    const user = this.draft().users()[index];
    const libraries = on
      ? [...user.libraries, library]
      : user.libraries.filter((name) => name !== library);
    this.draft().updateUser(index, { libraries });
  }
}

/** The P2P network: off by default; on, devices and other servers reach this one without opening ports. */
@Component({
  selector: 'app-setup-p2p',
  imports: [
    FormsModule,
    HlmFieldImports,
    HlmInputImports,
    HlmRadioGroupImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  host: { class: 'grid grid-cols-[minmax(0,1fr)] gap-3' },
  template: `
    <p class="text-sm">{{ 'setup.p2p.explanation' | t }}</p>
    <label class="flex items-center gap-2 rounded-lg border p-3 text-sm font-medium">
      <hlm-switch [checked]="draft().p2pEnabled" (checkedChange)="draft().p2pEnabled = $event" />
      {{ 'setup.p2p.enable' | t }}
    </label>
    @if (draft().p2pEnabled) {
      <hlm-field>
        <label hlmFieldLabel for="p2p-name">{{ 'setup.p2p.name' | t }}</label>
        <input
          hlmInput
          id="p2p-name"
          name="p2pName"
          [placeholder]="'setup.p2p.namePlaceholder' | t"
          [(ngModel)]="draft().p2pName"
        />
      </hlm-field>
      <hlm-radio-group class="grid gap-2" name="p2pRelay" [(ngModel)]="draft().p2pRelay">
        @for (mode of relays; track mode) {
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-radio [value]="mode" class="mt-0.5"><hlm-radio-indicator indicator /></hlm-radio>
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.p2p.relay.' + mode + '.name' | t }}</span>
              <span class="text-muted-foreground">{{
                'setup.p2p.relay.' + mode + '.help' | t
              }}</span>
            </span>
          </label>
        }
      </hlm-radio-group>
      @if (draft().p2pRelay === 'CUSTOM') {
        <hlm-field>
          <label hlmFieldLabel for="p2p-relay">{{ 'setup.p2p.relayUrl' | t }}</label>
          <input
            hlmInput
            id="p2p-relay"
            name="p2pRelayUrl"
            placeholder="https://relay.example.com"
            [(ngModel)]="draft().p2pRelayUrl"
          />
        </hlm-field>
      }
    }
    <p class="text-xs text-muted-foreground">{{ 'setup.p2p.later' | t }}</p>
  `,
})
export class SetupP2PStep {
  readonly draft = input.required<SetupExtrasDraft>();
  protected readonly relays = ['N0', 'CUSTOM', 'DIRECT'] as const;
}

/** Built-in extensions to switch on, and extensions or a web player to install from a URL. */
@Component({
  selector: 'app-setup-addons',
  imports: [
    FormsModule,
    NgIcon,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  providers: [provideIcons({ lucideExternalLink, lucidePlus, lucideSparkles, lucideTrash2 })],
  host: { class: 'grid grid-cols-[minmax(0,1fr)] gap-4' },
  template: `
    <div class="grid gap-2">
      <p class="text-sm font-medium">{{ 'setup.addons.builtIn' | t }}</p>
      @for (id of builtIn; track id) {
        <label class="flex items-start justify-between gap-3 rounded-lg border p-3 text-sm">
          <span class="grid gap-0.5">
            <span class="font-medium">{{ 'setup.addons.extensions.' + id + '.name' | t }}</span>
            <span class="text-muted-foreground">{{
              'setup.addons.extensions.' + id + '.help' | t
            }}</span>
          </span>
          <hlm-switch
            [checked]="draft().extensions[id]"
            (checkedChange)="draft().extensions[id] = $event"
            [aria-label]="'setup.addons.extensions.' + id + '.name' | t"
          />
        </label>
      }
    </div>

    <div class="grid gap-2">
      <p class="text-sm font-medium">{{ 'setup.addons.player' | t }}</p>
      <p class="text-sm text-muted-foreground">{{ 'setup.addons.playerHelp' | t }}</p>
      <input
        hlmInput
        name="playerUrl"
        placeholder="https://…/player.zip"
        [(ngModel)]="draft().playerUrl"
      />
      @if (draft().playerUrl.trim()) {
        <label class="flex items-center gap-2 text-sm">
          <hlm-checkbox
            [checked]="draft().playerDefault"
            (checkedChange)="draft().playerDefault = $event"
          />
          {{ 'setup.addons.playerDefault' | t }}
        </label>
      }
    </div>

    <div class="grid gap-2">
      <p class="text-sm font-medium">{{ 'setup.addons.more' | t }}</p>
      @for (url of draft().extensionUrls(); track $index; let index = $index) {
        <div class="flex gap-2">
          <input
            hlmInput
            class="min-w-0 flex-1"
            [name]="'extension-url-' + index"
            placeholder="https://…/extension.zip"
            [ngModel]="url"
            (ngModelChange)="setUrl(index, $event)"
          />
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            type="button"
            [attr.aria-label]="'common.remove' | t"
            (click)="removeUrl(index)"
          >
            <ng-icon name="lucideTrash2" />
          </button>
        </div>
      }
      <button
        hlmBtn
        variant="ghost"
        type="button"
        class="justify-self-start gap-1"
        (click)="addUrl()"
      >
        <ng-icon name="lucidePlus" /> {{ 'setup.addons.addExtension' | t }}
      </button>
    </div>
    <p class="text-xs text-muted-foreground">{{ 'setup.addons.trust' | t }}</p>
  `,
})
export class SetupAddonsStep {
  readonly draft = input.required<SetupExtrasDraft>();
  /** Ultimate Cloud may be offered (the administrator did not turn off every outside service). */
  readonly cloud = input(true);
  protected readonly builtIn = WIZARD_EXTENSIONS.filter((id) => !ULTIMATE_EXTENSIONS.includes(id));
  protected readonly forUltimate = ULTIMATE_EXTENSIONS;
  protected readonly url = ULTIMATE_URL;

  protected addUrl(): void {
    this.draft().extensionUrls.update((urls) => [...urls, '']);
  }

  protected setUrl(index: number, url: string): void {
    this.draft().extensionUrls.update((urls) => urls.map((u, i) => (i === index ? url : u)));
  }

  protected removeUrl(index: number): void {
    this.draft().extensionUrls.update((urls) => urls.filter((_, i) => i !== index));
  }
}
