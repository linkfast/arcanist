import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import { ClientQualityCard } from './client-quality-card';
import { FfmpegCard } from './ffmpeg-card';
import { RemoteTranscodingCard } from './remote-transcoding-card';
import { TranscodingCard } from './transcoding-card';
import { VideoCard } from './video-card';

/**
 * Everything about converting audio: ffmpeg, the quality each Ultimate client plays by default, the conversions
 * kept and made ahead of time, and where music shared by other servers is converted.
 */
@Component({
  selector: 'app-transcoding-page',
  imports: [
    TranslatePipe,
    ClientQualityCard,
    FfmpegCard,
    RemoteTranscodingCard,
    TranscodingCard,
    VideoCard,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'transcoding.page.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'transcoding.page.subtitle' | t }}</p>
    </header>

    <div class="grid gap-6">
      <app-ffmpeg-card />
      <app-client-quality-card />
      <app-transcoding-card />
      <app-video-card />
      @if (auth.isAdmin()) {
        <app-remote-transcoding-card />
      }
    </div>
  `,
})
export class TranscodingPage {
  protected readonly auth = inject(AuthService);
}
