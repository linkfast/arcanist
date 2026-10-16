import { Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { TranslatePipe } from '../../core/i18n/i18n';
import { AnonymousStatisticsCard } from './anonymous-statistics-card';
import { FfmpegCard } from './ffmpeg-card';
import { MaintenanceCard } from './maintenance-card';
import { OnlineServicesCard } from './online-services-card';
import { RatingModeCard } from './rating-mode-card';

/** Server-wide settings: ratings, online services and media tools. */
@Component({
  selector: 'app-settings-page',
  imports: [
    TranslatePipe,
    AnonymousStatisticsCard,
    FfmpegCard,
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
      <app-online-services-card />
      @if (cloudOn()) {
        <app-anonymous-statistics-card />
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
  private readonly extensions = inject(EnabledExtensions);
  /** Anonymous statistics go to Ultimate Cloud: hidden with its extension off. */
  protected readonly cloudOn = computed(() => this.extensions.has('ultimate-cloud'));
}
