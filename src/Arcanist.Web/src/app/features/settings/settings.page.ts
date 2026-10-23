import { LyricsTimingCard } from './lyrics-timing-card';
import { Component, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import { HostCard } from './host-card';
import { MaintenanceCard } from './maintenance-card';
import { OnlineServicesCard } from './online-services-card';
import { OpenSourceCard } from './open-source-card';
import { PrivacyCard } from './privacy-card';
import { RatingModeCard } from './rating-mode-card';
import { UpdatesCard } from './updates-card';

/** Server-wide settings: ratings, privacy, online services, updates and maintenance (conversion has its own page). */
@Component({
  selector: 'app-settings-page',
  imports: [
    LyricsTimingCard,
    TranslatePipe,
    PrivacyCard,
    OpenSourceCard,
    MaintenanceCard,
    OnlineServicesCard,
    RatingModeCard,
    UpdatesCard,
    HostCard,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'settings.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'settings.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <app-rating-mode-card />
      <app-privacy-card (changed)="privacyVersion.update((v) => v + 1)" />
      @for (version of [privacyVersion()]; track version) {
        <app-online-services-card />
      }
      @if (auth.isAdmin()) {
        <app-lyrics-timing-card />
        <app-open-source-card />
        <app-updates-card />
        <app-host-card />
        <app-maintenance-card />
      }
    </div>
  `,
})
export class SettingsPage {
  protected readonly auth = inject(AuthService);
  /** Remakes the online services card when the privacy choices change what it may turn on. */
  protected readonly privacyVersion = signal(0);
}
