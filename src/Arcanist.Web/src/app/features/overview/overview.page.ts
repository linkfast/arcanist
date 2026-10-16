import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AuthService } from '../../core/auth/auth.service';
import { Gql } from '../../core/graphql/gql.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import type {
  OverviewQuery as Overview,
  ServerInfoQuery as ServerInfo,
} from '../../graphql/generated/graphql';
import { OverviewQuery, ServerInfoQuery } from '../../graphql/operations';
import { CountPipe } from '../../shared/format';
import { AnalysisCard } from './analysis-card';
import { BackupsCard } from './backups-card';

@Component({
  selector: 'app-overview-page',
  imports: [
    RouterLink,
    HlmCardImports,
    HlmButtonImports,
    HlmSkeletonImports,
    TranslatePipe,
    CountPipe,
    AnalysisCard,
    BackupsCard,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">
        {{
          'overview.title' | t: { name: auth.user()?.displayName || auth.user()?.userName || '' }
        }}
      </h1>
      <p class="text-sm text-muted-foreground">{{ 'overview.subtitle' | t }}</p>
    </header>

    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      @for (stat of stats(); track stat.label) {
        <section hlmCard>
          <div hlmCardHeader>
            <p hlmCardDescription>{{ stat.label | t }}</p>
            @if (overview()) {
              <p hlmCardTitle class="text-3xl">{{ stat.value | count }}</p>
            } @else {
              <hlm-skeleton class="h-9 w-20" />
            }
          </div>
        </section>
      }
    </div>

    @if (overview()?.libraries?.length === 0) {
      <section hlmCard class="mt-6">
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'overview.noLibraries' | t }}</h2>
          <p hlmCardDescription>{{ 'overview.noLibrariesHelp' | t }}</p>
        </div>
        <div hlmCardFooter>
          <a hlmBtn routerLink="/libraries">{{ 'nav.libraries' | t }}</a>
        </div>
      </section>
    }

    <app-analysis-card class="mt-6 block" />

    @if (auth.isAdmin()) {
      <app-backups-card class="mt-6 block" />
    }

    <footer class="mt-10 text-xs text-muted-foreground">
      @if (server(); as info) {
        Arcanist {{ info.version }} ·
        @if (info.sourceUrl) {
          <a class="underline" [href]="info.sourceUrl" target="_blank" rel="noopener">{{
            'overview.sourceCode' | t
          }}</a>
        } @else {
          {{ 'overview.license' | t }}
        }
      }
    </footer>
  `,
})
export class OverviewPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly gql = inject(Gql);

  protected readonly overview = signal<Overview | null>(null);
  protected readonly server = signal<ServerInfo['serverInfo'] | null>(null);

  protected stats(): { label: string; value: number }[] {
    const data = this.overview();
    return [
      { label: 'overview.libraries', value: data?.libraries.length ?? 0 },
      { label: 'overview.artists', value: data?.artists.totalCount ?? 0 },
      { label: 'overview.albums', value: data?.albums.totalCount ?? 0 },
      { label: 'overview.tracks', value: data?.tracks.totalCount ?? 0 },
    ];
  }

  async ngOnInit(): Promise<void> {
    const [overview, server] = await Promise.all([
      this.gql.query(OverviewQuery),
      this.gql.query(ServerInfoQuery),
    ]);
    this.overview.set(overview);
    this.server.set(server.serverInfo);
  }
}
