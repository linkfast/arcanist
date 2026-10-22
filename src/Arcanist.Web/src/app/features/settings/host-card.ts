import { Component, OnInit, inject } from '@angular/core';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { TranslatePipe } from '../../core/i18n/i18n';
import { Host } from '../../core/server/host';

/** Where and how the server runs: how it was installed, its system, user and data folder. Administrators only. */
@Component({
  selector: 'app-host-card',
  imports: [HlmCardImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'host.card.title' | t }}</h2>
        <p hlmCardDescription>{{ 'host.card.description' | t }}</p>
      </div>
      @if (host.current(); as current) {
        <dl hlmCardContent class="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-sm">
          <dt class="text-muted-foreground">{{ 'host.card.kind' | t }}</dt>
          <dd>{{ 'host.kinds.' + current.kind | t }}</dd>
          <dt class="text-muted-foreground">{{ 'host.card.system' | t }}</dt>
          <dd>{{ systemName(current.os) }} · {{ current.architecture }}</dd>
          <dt class="text-muted-foreground">{{ 'host.card.container' | t }}</dt>
          <dd>{{ (current.container ? 'host.card.yes' : 'host.card.no') | t }}</dd>
          <dt class="text-muted-foreground">{{ 'host.card.supervised' | t }}</dt>
          <dd>{{ (current.supervised ? 'host.card.yes' : 'host.card.no') | t }}</dd>
          <dt class="text-muted-foreground">{{ 'host.card.user' | t }}</dt>
          <dd class="font-mono text-xs">{{ current.user }}</dd>
          <dt class="text-muted-foreground">{{ 'host.card.data' | t }}</dt>
          <dd class="font-mono text-xs">{{ current.dataDirectory }}</dd>
        </dl>
      }
    </section>
  `,
})
export class HostCard implements OnInit {
  protected readonly host = inject(Host);

  ngOnInit(): void {
    void this.host.load();
  }

  protected systemName(os: string): string {
    return { WINDOWS: 'Windows', MAC_OS: 'macOS', LINUX: 'Linux' }[os] ?? os;
  }
}
