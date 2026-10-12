import { Component } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/i18n';
import { FfmpegCard } from './ffmpeg-card';

/** Server-wide settings: media tools and, later, online services. */
@Component({
  selector: 'app-settings-page',
  imports: [TranslatePipe, FfmpegCard],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'settings.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'settings.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <app-ffmpeg-card />
    </div>
  `,
})
export class SettingsPage {}
