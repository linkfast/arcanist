import { Component, OnInit, inject, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { Edition } from '../../core/server/edition';
import type { OpenSourceModeFieldsFragment as Mode } from '../../graphql/generated/graphql';
import { OpenSourceModeQuery, SetOpenSourceModeMutation } from '../../graphql/operations';

/**
 * The 100% open source mode: while on, closed source extensions, players and tools are neither installed nor run.
 * Lists the closed source parts installed here, and says the edition the server's mark shows.
 */
@Component({
  selector: 'app-open-source-card',
  imports: [HlmBadgeImports, HlmCardImports, HlmSwitchImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.openSource.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.openSource.description' | t }}</p>
      </div>
      @if (mode(); as m) {
        <div hlmCardContent class="grid gap-4 text-sm">
          <label class="flex items-start gap-3">
            <hlm-switch
              class="mt-0.5"
              [checked]="m.enabled"
              [disabled]="busy() || m.setByEnvironment"
              (checkedChange)="set($event)"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'settings.openSource.switch' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'settings.openSource.help' | t
              }}</span>
              @if (m.setByEnvironment) {
                <span class="text-xs text-muted-foreground">{{
                  'settings.openSource.byEnvironment' | t
                }}</span>
              }
            </span>
          </label>
          <p class="flex flex-wrap items-center gap-2">
            {{ 'settings.openSource.edition' | t }}
            <span hlmBadge variant="outline">{{
              'settings.openSource.editions.' + m.edition | t
            }}</span>
          </p>
          @if (m.components.length > 0) {
            <div class="grid gap-2">
              <p class="font-medium">{{ 'settings.openSource.closed' | t }}</p>
              <ul class="grid divide-y rounded-md border">
                @for (component of m.components; track component.kind + component.id) {
                  <li class="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
                    <span class="min-w-0">
                      <span class="block truncate">{{ component.name }}</span>
                      <span class="block text-xs text-muted-foreground">
                        {{ 'settings.openSource.kinds.' + component.kind | t }}
                        @if (component.license) {
                          · {{ component.license }}
                        }
                      </span>
                    </span>
                    <span hlmBadge [variant]="component.active ? 'secondary' : 'outline'">
                      {{
                        (component.active
                          ? 'settings.openSource.active'
                          : m.enabled
                            ? 'settings.openSource.blocked'
                            : 'settings.openSource.inactive'
                        ) | t
                      }}
                    </span>
                  </li>
                }
              </ul>
              @if (!m.enabled) {
                <p class="text-xs text-muted-foreground">{{ 'settings.openSource.offNote' | t }}</p>
              }
            </div>
          } @else {
            <p class="text-xs text-muted-foreground">{{ 'settings.openSource.noneClosed' | t }}</p>
          }
        </div>
      }
    </section>
  `,
})
export class OpenSourceCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly edition = inject(Edition);
  private readonly extensions = inject(EnabledExtensions);

  protected readonly mode = signal<Mode | null>(null);
  protected readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.mode.set((await this.gql.query(OpenSourceModeQuery)).openSourceMode);
  }

  protected async set(enabled: boolean): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(SetOpenSourceModeMutation, { input: { enabled } });
      this.mode.set(result.setOpenSourceMode);
      await Promise.all([this.edition.refresh(), this.extensions.refresh()]);
      toast.success(this.i18n.t(enabled ? 'settings.openSource.on' : 'settings.openSource.off'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
      this.mode.set((await this.gql.query(OpenSourceModeQuery)).openSourceMode);
    } finally {
      this.busy.set(false);
    }
  }
}
