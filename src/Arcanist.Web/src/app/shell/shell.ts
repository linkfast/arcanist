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
  lucideHistory,
  lucideInfo,
  lucideKeyRound,
  lucideLock,
  lucideServer,
  lucideSettings2,
  lucideShare2,
  lucideShieldCheck,
  lucideSlidersHorizontal,
  lucideSmartphone,
  lucideWrench,
  lucideCircleHelp,
  lucideChevronDown,
  lucideCloud,
  lucideClapperboard,
  lucideMicVocal,
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
import {
  PanelSection,
  accountSections,
  isWithin,
  settingsSections,
} from '../shared/section-layout';
import { LanguageSelect } from '../shared/language-select';
import { Brand } from '../shared/brand';
import { ThemeSelect } from '../shared/theme-select';
import { TourService } from '../core/tour/tour.service';
import { TourOverlay } from '../shared/tour-overlay';
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
    TourOverlay,
  ],
  providers: [
    provideIcons({
      lucideArchive,
      lucideAudioWaveform,
      lucideCast,
      lucideChartColumn,
      lucideGift,
      lucideHistory,
      lucideInfo,
      lucideKeyRound,
      lucideLock,
      lucideServer,
      lucideSettings2,
      lucideShare2,
      lucideShieldCheck,
      lucideSlidersHorizontal,
      lucideSmartphone,
      lucideWrench,
      lucideCircleHelp,
      lucideChevronDown,
      lucideCloud,
      lucideClapperboard,
      lucideMicVocal,
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

        <nav class="flex flex-col gap-1" data-tour="nav">
          @for (item of navigation(); track item.path) {
            @if (groups()[item.path]; as children) {
              <!-- A page with others under it: Extensions, and the sections of Settings and My account. -->
              <div class="flex items-center">
                <a
                  hlmBtn
                  variant="ghost"
                  class="flex-1 justify-start gap-2"
                  [routerLink]="item.path"
                  routerLinkActive="bg-accent text-accent-foreground"
                  [routerLinkActiveOptions]="{ exact: true }"
                  (click)="reopen(item.path)"
                >
                  <ng-icon [name]="item.icon" />
                  {{ item.label | t }}
                </a>
                @if (children.length > 0) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon"
                    type="button"
                    class="shrink-0"
                    [attr.aria-expanded]="isOpen(item.path)"
                    [attr.aria-label]="'nav.toggleGroup' | t: { name: item.label | t }"
                    (click)="toggle(item.path)"
                  >
                    <ng-icon
                      name="lucideChevronDown"
                      class="transition-transform"
                      [class.-rotate-90]="!isOpen(item.path)"
                    />
                  </button>
                }
              </div>
              @if (isOpen(item.path)) {
                <div class="ml-4 flex flex-col gap-1 border-l pl-2">
                  @for (page of children; track page.path) {
                    <a
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      class="justify-start gap-2"
                      [routerLink]="page.path"
                      routerLinkActive="bg-accent text-accent-foreground"
                      ariaCurrentWhenActive="page"
                      [attr.data-tour]="page.tour"
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

      <main class="relative min-w-0 flex-1 px-3 pb-4 pt-11 sm:px-6 sm:pb-6 md:px-8 md:pb-8">
        <!-- This section's tour, whenever the user wants it. -->
        <button
          hlmBtn
          variant="ghost"
          size="icon"
          type="button"
          class="absolute right-1 top-1 z-10 text-muted-foreground sm:right-3 md:right-5"
          data-tour="help"
          [attr.title]="'tour.help' | t"
          [attr.aria-label]="'tour.help' | t"
          (click)="tour.start(url())"
        >
          <ng-icon name="lucideCircleHelp" />
        </button>
        <router-outlet />
      </main>
      <app-tour-overlay />
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

  protected readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly edition = inject(Edition);
  protected readonly tour = inject(TourService);

  /** Each section's tour, the first time it opens (once the page has drawn). */
  private readonly tourOnNavigation = effect(() => {
    const url = this.url();
    untracked(() => setTimeout(() => void this.tour.maybeStart(url), 600));
  });

  /** The menu on phones (beside the page on wider screens); any navigation closes it. */
  protected readonly menuOpen = signal(false);

  private readonly closeOnNavigation = effect(() => {
    this.url();
    untracked(() => {
      this.menuOpen.set(false);
      void this.edition.refresh();
    });
  });

  /** The pages that manage extensions, under Extensions: built-in ones while they are on, then installed ones. */
  private readonly extensionPages = computed<MenuPage[]>(() => {
    if (!this.auth.isAdmin()) return [];
    this.i18n.language();
    const builtIn = [
      {
        path: '/dlna',
        label: this.i18n.t('nav.dlna'),
        icon: 'lucideCast',
        iconUrl: null,
        tour: null,
        visible: true,
      },
      {
        path: '/server-audio',
        label: this.i18n.t('nav.serverAudio'),
        icon: 'lucideSpeaker',
        iconUrl: null,
        tour: null,
        visible: this.extensions.has('server-audio'),
      },
      {
        path: '/mstream',
        label: this.i18n.t('nav.mstream'),
        icon: 'lucideMusic',
        iconUrl: null,
        tour: null,
        visible: this.extensions.has('mstream'),
      },
    ].filter((page) => page.visible);
    const installed = this.extensions.pages().map((extension) => ({
      path: `/extensions/${extension.id}`,
      label: extension.name,
      icon: 'lucidePuzzle',
      iconUrl: extension.iconUrl ?? null,
      tour: null,
      visible: true,
    }));
    return [...builtIn, ...installed];
  });

  /** The pages with others under them in the menu, and those others. */
  protected readonly groups = computed<Record<string, MenuPage[]>>(() => {
    this.i18n.language();
    const sections = (path: string, list: PanelSection[]): MenuPage[] =>
      list.map((section) => ({
        path: `${path}/${section.id}`,
        label: this.i18n.t(section.label),
        icon: section.icon,
        iconUrl: null,
        tour: `section-${section.id}`,
      }));
    return {
      '/settings': sections('/settings', settingsSections(this.auth.isAdmin())),
      '/extensions': this.extensionPages(),
      '/account': sections('/account', accountSections(this.extensions.has('ultimate-cloud'))),
    };
  });

  /** The group that has the page in view. */
  private readonly currentGroup = computed(() => {
    const url = this.url();
    const inGroup = Object.entries(this.groups()).find(
      ([path, pages]) => isWithin(url, path) || pages.some((page) => isWithin(url, page.path)),
    );
    return inGroup?.[0] ?? null;
  });

  /** Which groups stay open wherever one is, as the user left them. Extensions, unless they closed it. */
  private readonly keptOpen = signal(readOpenGroups());

  /** The group in view, if the user closed it: it opens again on coming back. */
  private readonly closedHere = signal<string | null>(null);

  private readonly forgetClosed = effect(() => {
    this.currentGroup();
    untracked(() => this.closedHere.set(null));
  });

  /** The last groups open below the fold of a long menu: the page in view stays in sight. */
  private readonly showCurrentPage = effect(() => {
    this.url();
    setTimeout(() =>
      document
        .querySelector('aside nav [aria-current="page"]')
        ?.scrollIntoView({ block: 'nearest' }),
    );
  });

  /** A group is open while its page is in view, or if it was left open with its arrow. */
  protected isOpen(path: string): boolean {
    if (this.closedHere() === path) return false;
    return this.currentGroup() === path || (this.keptOpen()[path] ?? path === '/extensions');
  }

  protected reopen(path: string): void {
    if (this.closedHere() === path) this.closedHere.set(null);
  }

  protected toggle(path: string): void {
    const open = !this.isOpen(path);
    this.closedHere.set(!open && this.currentGroup() === path ? path : null);
    this.keptOpen.update((groups) => ({ ...groups, [path]: open }));
    try {
      localStorage.setItem(OPEN_KEY, JSON.stringify(this.keptOpen()));
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
        path: '/lyrics/review',
        label: 'nav.lyricsReview',
        icon: 'lucideMicVocal',
        visible: this.auth.can('EDIT_METADATA'),
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
      { path: '/listening', label: 'nav.listening', icon: 'lucideHistory', visible: true },
      { path: '/wishes', label: 'nav.wishes', icon: 'lucideGift', visible: true },
      { path: '/account', label: 'nav.account', icon: 'lucideUserCog', visible: true },
    ].filter((item) => item.visible),
  );
}

const OPEN_KEY = 'arcanist.nav.groups';

/** A page under another in the side menu. */
interface MenuPage {
  path: string;
  label: string;
  icon: string;
  iconUrl: string | null;
  /** What its tour points at. */
  tour: string | null;
}

function readOpenGroups(): Record<string, boolean> {
  try {
    return JSON.parse(localStorage.getItem(OPEN_KEY) ?? '{}') as Record<string, boolean>;
  } catch {
    return {};
  }
}
