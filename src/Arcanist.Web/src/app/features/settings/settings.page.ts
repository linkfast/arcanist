import { Component } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/i18n';
import { FfmpegCard } from './ffmpeg-card';
import { OnlineServicesCard } from './online-services-card';
import { RatingModeCard } from './rating-mode-card';

/** Server-wide settings: ratings, online services and media tools. */
@Component({
  selector: 'app-settings-page',
  imports: [TranslatePipe, FfmpegCard, OnlineServicesCard, RatingModeCard],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'settings.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'settings.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <app-rating-mode-card />
      <app-online-services-card />
      <app-ffmpeg-card />
    </div>
  `,
})
export class SettingsPage {}
