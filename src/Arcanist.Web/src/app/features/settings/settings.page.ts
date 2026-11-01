import { Component, computed, inject, input, signal } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import {
  lucideServer,
  lucideShare2,
  lucideShieldCheck,
  lucideSlidersHorizontal,
  lucideWrench,
} from '@ng-icons/lucide';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import {
  PanelSection,
  SectionLayout,
  activeSection,
  wideScreen,
} from '../../shared/section-layout';
import { HostCard } from './host-card';
import { LyricsTimingCard } from './lyrics-timing-card';
import { MaintenanceCard } from './maintenance-card';
import { OnlineServicesCard } from './online-services-card';
import { OpenSourceCard } from './open-source-card';
import { PrivacyCard } from './privacy-card';
import { RatingModeCard } from './rating-mode-card';
import { SharingCard } from './sharing-card';
import { UpdatesCard } from './updates-card';

function section(id: string, icon: string): PanelSection {
  return { id, icon, label: `settings.sections.${id}`, hint: `settings.sections.${id}Hint` };
}

/**
 * Server-wide settings in sections, by what one comes to do: how the server behaves for everyone, what leaves it, what
 * its users may share, the server itself and its maintenance (conversion has its own page). Those who manage settings
 * without being administrators see the first two.
 */
@Component({
  selector: 'app-settings-page',
  imports: [
    SectionLayout,
    TranslatePipe,
    LyricsTimingCard,
    PrivacyCard,
    OpenSourceCard,
    MaintenanceCard,
    OnlineServicesCard,
    RatingModeCard,
    SharingCard,
    UpdatesCard,
    HostCard,
  ],
  providers: [
    provideIcons({
      lucideSlidersHorizontal,
      lucideShieldCheck,
      lucideShare2,
      lucideServer,
      lucideWrench,
    }),
  ],
  template: `
    <app-section-layout
      heading="settings.title"
      [subtitle]="'settings.subtitle' | t"
      base="/settings"
      [sections]="sections()"
      [active]="active()"
    >
      @switch (active()?.id) {
        @case ('general') {
          <app-rating-mode-card />
          @if (auth.isAdmin()) {
            <app-lyrics-timing-card />
          }
        }
        @case ('privacy') {
          <app-privacy-card (changed)="privacyVersion.update((v) => v + 1)" />
          @for (version of [privacyVersion()]; track version) {
            <app-online-services-card />
          }
        }
        @case ('sharing') {
          <app-sharing-card />
        }
        @case ('server') {
          <app-updates-card />
          <app-host-card />
          <app-open-source-card />
        }
        @case ('maintenance') {
          <app-maintenance-card />
        }
      }
    </app-section-layout>
  `,
})
export class SettingsPage {
  protected readonly auth = inject(AuthService);

  /** From the URL (`/settings/<section>`). */
  readonly section = input<string>();

  private readonly wide = wideScreen();

  protected readonly sections = computed<PanelSection[]>(() => [
    section('general', 'lucideSlidersHorizontal'),
    section('privacy', 'lucideShieldCheck'),
    ...(this.auth.isAdmin()
      ? [
          section('sharing', 'lucideShare2'),
          section('server', 'lucideServer'),
          section('maintenance', 'lucideWrench'),
        ]
      : []),
  ]);

  protected readonly active = computed(() =>
    activeSection(this.sections(), this.section(), this.wide()),
  );

  /** Remakes the online services card when the privacy choices change what it may turn on. */
  protected readonly privacyVersion = signal(0);
}
