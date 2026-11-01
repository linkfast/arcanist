import { Component, computed, inject, input, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { SectionLayout, activeSection, settingsSections } from '../../shared/section-layout';
import { HostCard } from './host-card';
import { LyricsTimingCard } from './lyrics-timing-card';
import { MaintenanceCard } from './maintenance-card';
import { OnlineServicesCard } from './online-services-card';
import { OpenSourceCard } from './open-source-card';
import { PrivacyCard } from './privacy-card';
import { RatingModeCard } from './rating-mode-card';
import { SecondFactorCard } from './second-factor-card';
import { SharingCard } from './sharing-card';
import { UpdatesCard } from './updates-card';

/** Server-wide settings, one section at a time: the side menu lists them (conversion has its own page). */
@Component({
  selector: 'app-settings-page',
  imports: [
    SectionLayout,
    LyricsTimingCard,
    PrivacyCard,
    OpenSourceCard,
    MaintenanceCard,
    OnlineServicesCard,
    RatingModeCard,
    SecondFactorCard,
    SharingCard,
    UpdatesCard,
    HostCard,
  ],
  template: `
    <app-section-layout heading="settings.title" [section]="active()">
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
        @case ('security') {
          <app-second-factor-card />
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

  protected readonly active = computed(() =>
    activeSection(settingsSections(this.auth.isAdmin()), this.section()),
  );

  /** Remakes the online services card when the privacy choices change what it may turn on. */
  protected readonly privacyVersion = signal(0);
}
