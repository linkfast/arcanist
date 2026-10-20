import { Component, inject, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideCloud,
  lucideExternalLink,
  lucideMonitorSmartphone,
  lucideSparkles,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import { SetupExtrasDraft, ULTIMATE_EXTENSIONS, ULTIMATE_URL } from './setup-extras';

/**
 * Ultimate, the official clients, and Ultimate Cloud: what they add to this server, with the extensions that make
 * the most of them (the AI one on from the start). Nothing here is required.
 */
@Component({
  selector: 'app-setup-ultimate',
  imports: [NgIcon, HlmButtonImports, HlmSwitchImports, TranslatePipe],
  providers: [
    provideIcons({
      lucideCheck,
      lucideCloud,
      lucideExternalLink,
      lucideMonitorSmartphone,
      lucideSparkles,
    }),
  ],
  host: { class: 'grid grid-cols-[minmax(0,1fr)] gap-5' },
  template: `
    <p class="text-sm">{{ 'setup.ultimate.intro' | t }}</p>

    <div class="grid gap-4" [class.md:grid-cols-2]="cloud()">
      <section
        class="grid content-start gap-3 rounded-xl border border-primary/30 bg-primary/5 p-5"
      >
        <div class="flex items-center gap-3">
          <span
            class="flex size-10 items-center justify-center rounded-lg bg-primary text-primary-foreground"
          >
            <ng-icon name="lucideMonitorSmartphone" size="20" />
          </span>
          <div>
            <p class="font-semibold">Ultimate Desktop</p>
            <p class="text-xs text-muted-foreground">{{ 'setup.ultimate.desktop.tagline' | t }}</p>
          </div>
        </div>
        <ul class="grid gap-1.5 text-sm">
          @for (line of lines('setup.ultimate.desktop.features'); track line) {
            <li class="flex gap-2">
              <ng-icon name="lucideCheck" class="mt-0.5 shrink-0 text-primary" />
              <span>{{ line }}</span>
            </li>
          }
        </ul>
        <p class="text-xs text-muted-foreground">{{ 'setup.ultimate.desktop.free' | t }}</p>
      </section>

      @if (cloud()) {
        <section class="grid content-start gap-3 rounded-xl border p-5">
          <div class="flex items-center gap-3">
            <span class="flex size-10 items-center justify-center rounded-lg bg-muted">
              <ng-icon name="lucideCloud" size="20" />
            </span>
            <div>
              <p class="font-semibold">Ultimate Cloud</p>
              <p class="text-xs text-muted-foreground">{{ 'setup.ultimate.cloud.tagline' | t }}</p>
            </div>
          </div>
          <ul class="grid gap-1.5 text-sm">
            @for (line of lines('setup.ultimate.cloud.features'); track line) {
              <li class="flex gap-2">
                <ng-icon name="lucideCheck" class="mt-0.5 shrink-0 text-primary" />
                <span>{{ line }}</span>
              </li>
            }
          </ul>
          <p class="text-xs text-muted-foreground">{{ 'setup.ultimate.cloud.plans' | t }}</p>
        </section>
      }
    </div>

    <section class="grid gap-3">
      <div class="flex items-center gap-2">
        <ng-icon name="lucideSparkles" class="text-primary" />
        <p class="text-sm font-medium">{{ 'setup.ultimate.enhancers' | t }}</p>
      </div>
      @for (id of enhancers; track id) {
        <label class="flex items-start justify-between gap-3 rounded-lg border p-3 text-sm">
          <span class="grid gap-0.5">
            <span class="font-medium">{{ 'setup.addons.extensions.' + id + '.name' | t }}</span>
            <span class="text-muted-foreground">{{
              'setup.addons.extensions.' + id + '.help' | t
            }}</span>
          </span>
          <hlm-switch
            [checked]="draft().extensions[id]"
            (checkedChange)="draft().extensions[id] = $event"
            [aria-label]="'setup.addons.extensions.' + id + '.name' | t"
          />
        </label>
      }
    </section>

    @if (url) {
      <a
        hlmBtn
        variant="outline"
        class="justify-self-start gap-1"
        target="_blank"
        rel="noopener"
        [href]="url"
      >
        {{ 'setup.addons.ultimate.learnMore' | t }} <ng-icon name="lucideExternalLink" />
      </a>
    }
  `,
})
export class SetupUltimateStep {
  readonly draft = input.required<SetupExtrasDraft>();
  /** Ultimate Cloud may be offered (the administrator did not turn off every outside service). */
  readonly cloud = input(true);
  private readonly i18n = inject(I18n);

  /** A list kept as one text (" · " between items), as its items. */
  protected lines(key: string): string[] {
    return this.i18n.t(key).split(' · ');
  }

  protected readonly enhancers = ULTIMATE_EXTENSIONS;
  protected readonly url = ULTIMATE_URL;
}
