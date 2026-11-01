import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { TranslatePipe } from '../core/i18n/i18n';

/** A section of a page with a menu of its own: its URL is the page's plus the id. */
export interface PanelSection {
  id: string;
  /** Keys of the translations. */
  label: string;
  hint: string;
  /** An icon the page provides. */
  icon: string;
}

/** Where the layout has the menu beside the content (Tailwind's `lg`). */
const wideQuery = '(min-width: 1024px)';

/** Whether the screen fits the menu beside the content, following it as it changes. */
export function wideScreen() {
  const query = window.matchMedia(wideQuery);
  const wide = signal(query.matches);
  const update = (event: MediaQueryListEvent) => wide.set(event.matches);
  query.addEventListener('change', update);
  inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  return wide.asReadonly();
}

/**
 * The section a URL asks for. An unknown one is the first; none is the first too, except on a small screen, where the
 * page starts at the menu.
 */
export function activeSection(
  sections: readonly PanelSection[],
  id: string | undefined,
  wide: boolean,
): PanelSection | null {
  return sections.find((s) => s.id === id) ?? (id || wide ? (sections[0] ?? null) : null);
}

/**
 * A page in sections, one in view at a time and each with its own URL: the menu beside the content or, on a small
 * screen, the menu and then the section with a way back. Each entry of the menu has `data-tour="section-<id>"`.
 */
@Component({
  selector: 'app-section-layout',
  imports: [RouterLink, NgIcon, TranslatePipe],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  template: `
    <header class="mb-6">
      @if (active(); as section) {
        <a
          class="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground lg:hidden"
          [routerLink]="base()"
        >
          <ng-icon name="lucideChevronLeft" />
          {{ heading() | t }}
        </a>
        <h1 class="text-2xl font-semibold lg:hidden">{{ section.label | t }}</h1>
      }
      <h1 class="text-2xl font-semibold" [class.hidden]="active()" [class.lg:block]="active()">
        {{ heading() | t }}
      </h1>
      <p
        class="text-sm text-muted-foreground"
        [class.hidden]="active()"
        [class.lg:block]="active()"
      >
        {{ subtitle() }}
      </p>
    </header>

    <div class="lg:grid lg:grid-cols-[13rem_minmax(0,1fr)] lg:items-start lg:gap-8">
      <nav
        class="lg:sticky lg:top-6"
        [class.hidden]="active()"
        [class.lg:block]="active()"
        [attr.aria-label]="heading() | t"
      >
        <ul class="grid gap-1">
          @for (section of sections(); track section.id) {
            <li>
              <a
                class="flex items-center gap-3 rounded-lg border p-3 text-sm hover:bg-accent lg:border-0 lg:px-3 lg:py-2"
                [class.lg:bg-accent]="section.id === active()?.id"
                [class.lg:font-medium]="section.id === active()?.id"
                [attr.aria-current]="section.id === active()?.id ? 'page' : null"
                [attr.data-tour]="'section-' + section.id"
                [routerLink]="[base(), section.id]"
              >
                <ng-icon [name]="section.icon" class="shrink-0" />
                <span class="grid min-w-0 flex-1 gap-0.5">
                  <span>{{ section.label | t }}</span>
                  <span class="text-xs text-muted-foreground lg:hidden">{{
                    section.hint | t
                  }}</span>
                </span>
                <!-- In a span: an icon's own display wins over the utility. -->
                <span class="shrink-0 text-muted-foreground lg:hidden">
                  <ng-icon name="lucideChevronRight" />
                </span>
              </a>
            </li>
          }
        </ul>
      </nav>

      @if (active()) {
        <div class="grid min-w-0 gap-6">
          <ng-content />
        </div>
      }
    </div>
  `,
})
export class SectionLayout {
  /** The key of the page's title. */
  readonly heading = input.required<string>();
  readonly subtitle = input('');
  /** The page's URL, as the panel's router writes it (`/settings`). */
  readonly base = input.required<string>();
  readonly sections = input.required<readonly PanelSection[]>();
  readonly active = input.required<PanelSection | null>();
}
