import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { Gql } from '../../core/graphql/gql.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import type { LyricsTimingQuery as Result } from '../../graphql/generated/graphql';
import { LyricsTimingQuery } from '../../graphql/operations';
import { CountPipe } from '../../shared/format';

/**
 * Timed lyrics: how many there are and, when plain lyrics are left, the official extension that times them on this
 * server (installed from the marketplace on demand).
 */
@Component({
  selector: 'app-lyrics-timing-card',
  imports: [HlmCardImports, HlmButtonImports, RouterLink, TranslatePipe, CountPipe],
  template: `
    @if (status(); as s) {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'settings.lyricsTiming.title' | t }}</h2>
          <p hlmCardDescription>{{ 'settings.lyricsTiming.description' | t }}</p>
        </div>
        <div hlmCardContent class="grid gap-3 text-sm">
          <p>
            {{
              'settings.lyricsTiming.counts'
                | t
                  : {
                      timed: (s.timed | count),
                      all: (s.withLyrics | count),
                      aligned: (s.aligned | count),
                    }
            }}
          </p>
          @if (s.untimed + s.waiting > 0) {
            <p class="text-muted-foreground">
              {{ 'settings.lyricsTiming.left' | t: { count: (s.untimed + s.waiting | count) } }}
            </p>
          }
          @switch (s.extension) {
            @case ('NOT_INSTALLED') {
              @if (s.untimed + s.waiting > 0) {
                <div class="grid gap-2 rounded-lg border p-3">
                  <p>{{ 'settings.lyricsTiming.offer' | t }}</p>
                  <div>
                    <a
                      hlmBtn
                      size="sm"
                      routerLink="/extensions"
                      [queryParams]="{ install: s.extensionId }"
                      >{{ 'settings.lyricsTiming.install' | t }}</a
                    >
                  </div>
                </div>
              }
            }
            @case ('INSTALLED') {
              <p class="text-muted-foreground">
                {{ 'settings.lyricsTiming.installed' | t }}
                <a class="underline" routerLink="/extensions">{{ 'nav.extensions' | t }}</a>
              </p>
            }
            @default {
              <div>
                <a
                  hlmBtn
                  size="sm"
                  variant="outline"
                  [routerLink]="['/extensions', s.extensionId]"
                  >{{ 'settings.lyricsTiming.open' | t }}</a
                >
              </div>
            }
          }
        </div>
      </section>
    }
  `,
})
export class LyricsTimingCard implements OnInit {
  private readonly gql = inject(Gql);

  protected readonly status = signal<Result['lyricsTiming'] | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.status.set((await this.gql.query(LyricsTimingQuery)).lyricsTiming);
    } catch {
      // Not shown without it.
    }
  }
}
