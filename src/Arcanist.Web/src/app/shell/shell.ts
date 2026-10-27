import { Edition } from '../core/server/edition';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideAudioWaveform,
  lucideBrainCircuit,
  lucideCast,
  lucideChartColumn,
  lucideGift,
  lucideChevronDown,
  lucideCloud,
  lucideClapperboard,
  lucideCopy,
  lucideDatabase,
  lucideExternalLink,
  lucideHouse,
  lucideInbox,
  lucideLibrary,
  lucideLogOut,
  lucideMenu,
  lucideX,
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
import { I18n, TranslatePipe } from '../core/i18n/i18n';
import { LanguageSelect } from '../shared/language-select';
import { Brand } from '../shared/brand';
import { ThemeSelect } from '../shared/theme-select';
import { UpdateNotice } from './update-notice';

/** Layout of the signed-in panel: navigation on the side (on top on small screens) and the page content. */
@Component({
  selector: 'app-shell',
  imports: [
    Brand,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgIcon,
    HlmButtonImports,
    TranslatePipe,
    LanguageSelect,
    ThemeSelect,
    UpdateNotice,
  ],
  providers: [
    provideIcons({
      lucideArchive,
      lucideAudioWaveform,
      lucideCast,
      lucideChartColumn,
      lucideGift,
      lucideChevronDown,
      lucideCloud,
      lucideClapperboard,
      lucideCopy,
      lucideDatabase,
      lucideHouse,
      lucideInbox,
      lucidePuzzle,
      lucideLibrary,
      lucideUsers,
      lucideUserCog,
      lucideLogOut,
      lucideMenu,
      lucideX,
      lucideMusic,
      lucideNetwork,
      lucideSpeaker,
      lucideBrainCircuit,
      lucideSettings,
      lucideExternalLink,
    }),
  ],
  template: `
    <div class="min-h-dvh md:flex">
      <!-- Phones: a bar with the mark and the menu, which slides in from the side. -->
      <header
        class="sticky top-0 z-30 flex items-center justify-between gap-2 border-b bg-background/95 px-3 py-2 backdrop-blur md:hidden"
      >
        <a routerLink="/" class="min-w-0" [attr.aria-label]="'nav.overview' | t">
          <app-brand [compact]="true" [edition]="edition.label()" />
        </a>
        <button
          hlmBtn
          variant="ghost"
          size="icon"
          type="button"
          [attr.aria-label]="'nav.menu' | t"
          [attr.aria-expanded]="menuOpen()"
          (click)="menuOpen.set(true)"
        >
          <ng-icon name="lucideMenu" size="20" />
        </button>
      </header>
      @if (menuOpen()) {
        <div class="fixed inset-0 z-40 bg-black/50 md:hidden" (click)="menuOpen.set(false)"></div>
      }

      <aside
        class="fixed inset-y-0 left-0 z-50 flex w-72 max-w-[85vw] flex-col gap-1 overflow-y-auto overscroll-contain [scrollbar-width:thin] border-r bg-background p-3 transition-transform duration-200 md:sticky md:top-0 md:z-auto md:h-dvh md:w-60 md:max-w-none md:shrink-0 md:translate-x-0 md:bg-muted/40 md:transition-none"
        [class.-translate-x-full]="!menuOpen()"
        [attr.aria-label]="'nav.menu' | t"
      >
        <div class="flex items-start justify-between gap-2 px-2 pt-1 pb-3">
          <div class="grid min-w-0 gap-2">
            <a routerLink="/" class="w-fit"><app-brand [edition]="edition.label()" /></a>
            <p class="truncate text-xs text-muted-foreground">
              {{ auth.user()?.displayName || auth.user()?.userName }}
            </p>
          </div>
          <button
            hlmBtn
            variant="ghost"
            size="icon-sm"
            type="button"
            class="md:hidden"
            [attr.aria-label]="'common.close' | t"
            (click)="menuOpen.set(false)"
          >
            <ng-icon name="lucideX" />
          </button>
        </div>

        <nav class="flex flex-col gap-1">
          @for (item of navigation(); track item.path) {
            @if (item.path === '/extensions') {
              <!-- Extensions: their list, and below it the pages that manage each one. -->
              <div class="flex items-center">
                <a
                  hlmBtn
                  variant="ghost"
                  class="flex-1 justify-start gap-2"
                  routerLink="/extensions"
                  routerLinkActive="bg-accent text-accent-foreground"
                  [routerLinkActiveOptions]="{ exact: true }"
                  (click)="extensionsOpen.set(true)"
                >
                  <ng-icon name="lucidePuzzle" />
                  {{ 'nav.extensions' | t }}
                </a>
                @if (extensionPages().length > 0) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon"
                    type="button"
                    class="shrink-0"
                    [attr.aria-expanded]="extensionsExpanded()"
                    [attr.aria-label]="'nav.extensionsToggle' | t"
                    (click)="toggleExtensions()"
                  >
                    <ng-icon
                      name="lucideChevronDown"
                      class="transition-transform"
                      [class.-rotate-90]="!extensionsExpanded()"
                    />
                  </button>
                }
              </div>
              @if (extensionsExpanded()) {
                <div class="ml-4 flex flex-col gap-1 border-l pl-2">
                  @for (page of extensionPages(); track page.path) {
                    <a
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      class="justify-start gap-2"
                      [routerLink]="page.path"
                      routerLinkActive="bg-accent text-accent-foreground"
                    >
                      @if (page.iconUrl) {
                        <img [src]="page.iconUrl" alt="" class="size-4 rounded-sm" />
                      } @else {
                        <ng-icon [name]="page.icon" />
                      }
                      {{ page.label }}
                    </a>
                  }
                </div>
              }
            } @else {
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
          }
        </nav>

        <div class="mt-auto flex flex-col items-stretch gap-2 pt-6">
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

      <main class="min-w-0 flex-1 px-3 py-4 sm:p-6 md:p-8">
        <router-outlet />
      </main>
      <!-- In a chunk of its own: most visits have nothing to tell. -->
      @defer (on idle) {
        <app-update-notice />
      }
    </div>
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly extensions = inject(EnabledExtensions);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly edition = inject(Edition);

  /** The menu on phones (beside the page on wider screens); any navigation closes it. */
  protected readonly menuOpen = signal(false);

  private readonly closeOnNavigation = effect(() => {
    this.url();
    untracked(() => {
      this.menuOpen.set(false);
      void this.edition.refresh();
    });
  });

  /** The Extensions group, opened or closed by the user (remembered). */
  protected readonly extensionsOpen = signal(readOpen());

  /** The pages that manage extensions, under Extensions: built-in ones while they are on, then installed ones. */
  protected readonly extensionPages = computed(() => {
    if (!this.auth.isAdmin()) return [];
    this.i18n.language();
    const builtIn = [
      {
        path: '/dlna',
        label: this.i18n.t('nav.dlna'),
        icon: 'lucideCast',
        iconUrl: null,
        visible: true,
      },
      {
        path: '/server-audio',
        label: this.i18n.t('nav.serverAudio'),
        icon: 'lucideSpeaker',
        iconUrl: null,
        visible: this.extensions.has('server-audio'),
      },
      {
        path: '/mstream',
        label: this.i18n.t('nav.mstream'),
        icon: 'lucideMusic',
        iconUrl: null,
        visible: this.extensions.has('mstream'),
      },
    ].filter((page) => page.visible);
    const installed = this.extensions.pages().map((extension) => ({
      path: `/extensions/${extension.id}`,
      label: extension.name,
      icon: 'lucidePuzzle',
      iconUrl: extension.iconUrl ?? null,
      visible: true,
    }));
    return [...builtIn, ...installed];
  });

  /** Open when the user opened it, or while one of its pages is shown. */
  protected readonly extensionsExpanded = computed(
    () =>
      this.extensionsOpen() ||
      this.extensionPages().some((page) => this.url().startsWith(page.path)),
  );

  protected toggleExtensions(): void {
    const open = !this.extensionsExpanded();
    this.extensionsOpen.set(open);
    try {
      localStorage.setItem(OPEN_KEY, open ? '1' : '0');
    } catch {
      // Without storage it is remembered while the page is open.
    }
  }

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
        path: '/videos',
        label: 'nav.videos',
        icon: 'lucideClapperboard',
        visible: this.auth.can('MANAGE_LIBRARIES') || this.auth.can('EDIT_METADATA'),
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
        path: '/transcoding',
        label: 'nav.transcoding',
        icon: 'lucideAudioWaveform',
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
      {
        path: '/ai',
        label: 'nav.ai',
        icon: 'lucideBrainCircuit',
        visible: this.auth.isAdmin() && this.extensions.has('ai'),
      },
      { path: '/wishes', label: 'nav.wishes', icon: 'lucideGift', visible: true },
      { path: '/account', label: 'nav.account', icon: 'lucideUserCog', visible: true },
    ].filter((item) => item.visible),
  );
}

const OPEN_KEY = 'arcanist.nav.extensions';

function readOpen(): boolean {
  try {
    return localStorage.getItem(OPEN_KEY) !== '0';
  } catch {
    return true;
  }
}
