import { Component, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import { FfmpegCard } from './ffmpeg-card';
import { MaintenanceCard } from './maintenance-card';
import { OnlineServicesCard } from './online-services-card';
import { PrivacyCard } from './privacy-card';
import { RatingModeCard } from './rating-mode-card';

/** Server-wide settings: ratings, online services and media tools. */
@Component({
  selector: 'app-settings-page',
  imports: [
    TranslatePipe,
    FfmpegCard,
    PrivacyCard,
    MaintenanceCard,
    OnlineServicesCard,
    RatingModeCard,
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
      <app-ffmpeg-card />
      @if (auth.isAdmin()) {
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
