import { Component, computed, effect, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideBrainCircuit,
  lucideCast,
  lucideChartColumn,
  lucideCloud,
  lucideCopy,
  lucideDatabase,
  lucideExternalLink,
  lucideHouse,
  lucideInbox,
  lucideLibrary,
  lucideLogOut,
  lucideMusic,
  lucideNetwork,
  lucideSpeaker,
  lucidePuzzle,
  lucideSettings,
  lucideUserCog,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { AuthService } from '../core/auth/auth.service';
import { EnabledExtensions } from '../core/extensions/enabled-extensions';
import { TranslatePipe } from '../core/i18n/i18n';
import { LanguageSelect } from '../shared/language-select';
import { ThemeSelect } from '../shared/theme-select';

/** Layout of the signed-in panel: navigation on the side (on top on small screens) and the page content. */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgIcon,
    HlmButtonImports,
    TranslatePipe,
    LanguageSelect,
    ThemeSelect,
  ],
  providers: [
    provideIcons({
      lucideArchive,
      lucideCast,
      lucideChartColumn,
      lucideCloud,
      lucideCopy,
      lucideDatabase,
      lucideHouse,
      lucideInbox,
      lucidePuzzle,
      lucideLibrary,
      lucideUsers,
      lucideUserCog,
      lucideLogOut,
      lucideMusic,
      lucideNetwork,
      lucideSpeaker,
      lucideBrainCircuit,
      lucideSettings,
      lucideExternalLink,
    }),
  ],
  template: `
    <div class="flex min-h-screen flex-col md:flex-row">
      <aside
        class="flex shrink-0 flex-col gap-1 border-b bg-muted/40 p-3 md:w-60 md:border-r md:border-b-0"
      >
        <div class="px-2 pb-3 pt-1">
          <p class="text-lg font-semibold">Arcanist</p>
          <p class="truncate text-xs text-muted-foreground">
            {{ auth.user()?.displayName || auth.user()?.userName }}
          </p>
        </div>

        <nav class="flex flex-row flex-wrap gap-1 md:flex-col">
          @for (item of navigation(); track item.path) {
            <a
              hlmBtn
              variant="ghost"
              class="justify-start gap-2"
              [routerLink]="item.path"
              routerLinkActive="bg-accent text-accent-foreground"
              [routerLinkActiveOptions]="{ exact: item.path === '/' }"
            >
              <ng-icon [name]="item.icon" />
              {{ item.label | t }}
            </a>
          }
        </nav>

        <div
          class="flex flex-row flex-wrap items-center gap-2 pt-2 md:mt-auto md:flex-col md:items-stretch md:pt-6"
        >
          <a
            hlmBtn
            variant="ghost"
            class="justify-start gap-2"
            href="/"
            target="_blank"
            rel="noopener"
          >
            <ng-icon name="lucideExternalLink" />
            {{ 'nav.player' | t }}
          </a>
          <app-language-select />
          <app-theme-select />
          <button
            hlmBtn
            variant="ghost"
            class="justify-start gap-2"
            type="button"
            (click)="auth.logout()"
          >
            <ng-icon name="lucideLogOut" />
            {{ 'nav.logout' | t }}
          </button>
        </div>
      </aside>

      <main class="min-w-0 flex-1 p-4 md:p-8">
        <router-outlet />
      </main>
    </div>
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly extensions = inject(EnabledExtensions);

  constructor() {
    // Pages of extensions show while those are on; only administrators manage them.
    effect(() => {
      if (this.auth.isAdmin()) void this.extensions.refresh();
    });
  }

  protected readonly navigation = computed(() =>
    [
      { path: '/', label: 'nav.overview', icon: 'lucideHouse', visible: true },
      { path: '/libraries', label: 'nav.libraries', icon: 'lucideLibrary', visible: true },
      {
        path: '/duplicates',
        label: 'nav.duplicates',
        icon: 'lucideCopy',
        visible: this.auth.can('MANAGE_LIBRARIES'),
      },
      {
        path: '/import',
        label: 'nav.import',
        icon: 'lucideInbox',
        visible: this.auth.can('MANAGE_LIBRARIES'),
      },
      {
        path: '/stats',
        label: 'nav.stats',
        icon: 'lucideChartColumn',
        visible: this.auth.can('VIEW_ALL_STATS'),
      },
      {
        path: '/users',
        label: 'nav.users',
        icon: 'lucideUsers',
        visible: this.auth.can('MANAGE_USERS'),
      },
      {
        path: '/settings',
        label: 'nav.settings',
        icon: 'lucideSettings',
        visible: this.auth.can('MANAGE_SETTINGS'),
      },
      {
        path: '/extensions',
        label: 'nav.extensions',
        icon: 'lucidePuzzle',
        visible: this.auth.isAdmin(),
      },
      {
        path: '/database',
        label: 'nav.database',
        icon: 'lucideDatabase',
        visible: this.auth.isAdmin(),
      },
      {
        path: '/backups',
        label: 'nav.backups',
        icon: 'lucideArchive',
        visible: this.auth.isAdmin(),
      },
      {
        path: '/network',
        label: 'nav.network',
        icon: 'lucideNetwork',
        visible: this.auth.isAdmin(),
      },
      {
        path: '/cloud',
        label: 'nav.cloud',
        icon: 'lucideCloud',
        visible: this.auth.isAdmin() && this.extensions.has('ultimate-cloud'),
      },
      { path: '/dlna', label: 'nav.dlna', icon: 'lucideCast', visible: this.auth.isAdmin() },
      {
        path: '/ai',
        label: 'nav.ai',
        icon: 'lucideBrainCircuit',
        visible: this.auth.isAdmin() && this.extensions.has('ai'),
      },
      {
        path: '/server-audio',
        label: 'nav.serverAudio',
        icon: 'lucideSpeaker',
        visible: this.auth.isAdmin() && this.extensions.has('server-audio'),
      },
      {
        path: '/mstream',
        label: 'nav.mstream',
        icon: 'lucideMusic',
        visible: this.auth.isAdmin() && this.extensions.has('mstream'),
      },
      { path: '/account', label: 'nav.account', icon: 'lucideUserCog', visible: true },
    ].filter((item) => item.visible),
  );
}
