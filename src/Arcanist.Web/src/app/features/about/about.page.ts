import { Component, OnInit, inject, signal } from '@angular/core';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Gql } from '../../core/graphql/gql.service';
import { TranslatePipe } from '../../core/i18n/i18n';
import type { ServerInfoQuery as ServerInfoResult } from '../../graphql/generated/graphql';
import { ServerInfoQuery } from '../../graphql/operations';

/** The server, not anyone's account: its version, its license and where its source code and APIs are. */
@Component({
  selector: 'app-about-page',
  imports: [HlmCardImports, HlmSpinnerImports, TranslatePipe],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'nav.about' | t }}</h1>
    </header>
    <section hlmCard class="max-w-3xl" data-tour="about-server">
      <div hlmCardContent class="grid gap-1 text-sm">
        @if (server(); as info) {
          <p>Arcanist {{ info.version }} · API v{{ info.apiVersion }}</p>
          <p class="text-muted-foreground">{{ 'overview.madeBy' | t }}</p>
          <p class="text-muted-foreground">{{ 'overview.license' | t }}</p>
          @if (info.sourceUrl) {
            <a class="underline" [href]="info.sourceUrl" target="_blank" rel="noopener">{{
              'overview.sourceCode' | t
            }}</a>
          }
          <p class="mt-2 flex flex-wrap gap-3">
            <a class="underline" href="/rest/docs" target="_blank" rel="noopener">REST API</a>
            <a class="underline" href="/graphql/ui" target="_blank" rel="noopener">GraphQL</a>
          </p>
        } @else {
          <hlm-spinner class="size-5 justify-self-center" />
        }
      </div>
    </section>
  `,
})
export class AboutPage implements OnInit {
  private readonly gql = inject(Gql);

  protected readonly server = signal<ServerInfoResult['serverInfo'] | null>(null);

  async ngOnInit(): Promise<void> {
    this.server.set((await this.gql.query(ServerInfoQuery)).serverInfo);
  }
}
