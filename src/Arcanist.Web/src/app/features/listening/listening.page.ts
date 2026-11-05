import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { TranslatePipe } from '../../core/i18n/i18n';
import { MyStats } from './my-stats';
import { PlayHistory } from './play-history';

/** What each user listened to: their history, which they manage, and their statistics. */
@Component({
  selector: 'app-listening-page',
  imports: [HlmButtonImports, MyStats, PlayHistory, RouterLink, TranslatePipe],
  template: `
    <header class="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold">{{ 'listening.title' | t }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'listening.subtitle' | t }}</p>
      </div>
      <nav class="flex gap-1" data-tour="listening-tabs" [attr.aria-label]="'listening.title' | t">
        <a
          hlmBtn
          size="sm"
          routerLink="/listening/history"
          [variant]="current() === 'history' ? 'default' : 'ghost'"
          [attr.aria-current]="current() === 'history' ? 'page' : null"
        >
          {{ 'listening.history' | t }}
        </a>
        <a
          hlmBtn
          size="sm"
          routerLink="/listening/stats"
          [variant]="current() === 'stats' ? 'default' : 'ghost'"
          [attr.aria-current]="current() === 'stats' ? 'page' : null"
        >
          {{ 'listening.stats' | t }}
        </a>
      </nav>
    </header>
    @if (current() === 'stats') {
      <app-my-stats />
    } @else {
      <app-play-history />
    }
  `,
})
export class ListeningPage {
  readonly section = input<string>();

  protected readonly current = computed(() => (this.section() === 'stats' ? 'stats' : 'history'));
}
