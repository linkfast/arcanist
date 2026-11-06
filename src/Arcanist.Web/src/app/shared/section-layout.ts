import { Component, input } from '@angular/core';
import { TranslatePipe } from '../core/i18n/i18n';

/** A section of a page, listed under it in the side menu: its URL is the page's plus the id. */
export interface PanelSection {
  id: string;
  /** Keys of the translations. */
  label: string;
  hint: string;
  /** An icon the shell provides. */
  icon: string;
}

function settingsSection(id: string, icon: string): PanelSection {
  return { id, icon, label: `settings.sections.${id}`, hint: `settings.sections.${id}Hint` };
}

function accountSection(id: string, icon: string): PanelSection {
  return { id, icon, label: `account.sections.${id}`, hint: `account.sections.${id}Hint` };
}

/**
 * Server-wide settings, by what one comes to do: how the server behaves for everyone, what leaves it, its security, what
 * its users may share, the server itself and its maintenance. Those who manage settings without being administrators
 * see the first two.
 */
export function settingsSections(admin: boolean): PanelSection[] {
  return [
    settingsSection('general', 'lucideSlidersHorizontal'),
    settingsSection('privacy', 'lucideShieldCheck'),
    ...(admin
      ? [
          settingsSection('security', 'lucideKeyRound'),
          settingsSection('sharing', 'lucideShare2'),
          settingsSection('server', 'lucideServer'),
          settingsSection('maintenance', 'lucideWrench'),
        ]
      : []),
  ];
}

/** What is each person's own on this server. */
export function accountSections(ultimateCloud: boolean): PanelSection[] {
  return [
    accountSection('security', 'lucideLock'),
    accountSection('apps', 'lucideSmartphone'),
    ...(ultimateCloud ? [accountSection('cloud', 'lucideCloud')] : []),
    accountSection('preferences', 'lucideSettings2'),
  ];
}

/** The section a URL asks for; an unknown one (or one this user does not see) is the first. */
export function activeSection(
  sections: readonly PanelSection[],
  id: string | undefined,
): PanelSection | null {
  return sections.find((s) => s.id === id) ?? sections[0] ?? null;
}

/** Whether a URL of the panel is that page or one below it. */
export function isWithin(url: string, path: string): boolean {
  const page = url.split(/[?#]/)[0];
  return page === path || page.startsWith(path + '/');
}

/** A section's page: what it is, and its cards in a column that stays readable on wide screens. */
@Component({
  selector: 'app-section-layout',
  imports: [TranslatePipe],
  template: `
    @if (section(); as current) {
      <header class="mb-6">
        <p class="text-sm text-muted-foreground">{{ heading() | t }}</p>
        <h1 class="text-2xl font-semibold">{{ current.label | t }}</h1>
        <p class="text-sm text-muted-foreground">{{ current.hint | t }}</p>
      </header>
      <div class="grid max-w-3xl min-w-0 gap-6">
        <ng-content />
      </div>
    }
  `,
})
export class SectionLayout {
  /** The key of the page's title. */
  readonly heading = input.required<string>();
  readonly section = input.required<PanelSection | null>();
}
