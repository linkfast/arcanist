import { Component, OnInit, computed, inject, output, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError, ExternalServicesMode } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import {
  PrivacyQuery,
  SetAnonymousStatisticsMutation,
  SetPrivacyMutation,
} from '../../graphql/operations';

interface State {
  noTelemetry: boolean;
  externalServices: ExternalServicesMode;
  anonymous: boolean;
}

/**
 * Privacy: no telemetry, no outside services (or none but Ultimate Cloud), and the anonymous statistics they allow.
 * What a choice leaves out is turned off by the server, and stays off until the choice is relaxed.
 */
@Component({
  selector: 'app-privacy-card',
  imports: [HlmCardImports, HlmCheckboxImports, HlmSwitchImports, TranslatePipe],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'settings.privacy.title' | t }}</h2>
        <p hlmCardDescription>{{ 'settings.privacy.description' | t }}</p>
      </div>
      @if (state(); as s) {
        <div hlmCardContent class="grid gap-3">
          <label class="flex items-start gap-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="s.noTelemetry"
              [disabled]="busy()"
              (checkedChange)="save({ noTelemetry: $event })"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.noTelemetry' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.noTelemetryHelp' | t
              }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="s.externalServices === 'NONE'"
              [disabled]="busy()"
              (checkedChange)="external('NONE', $event)"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.noExternal' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.noExternalHelp' | t
              }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="s.externalServices === 'ULTIMATE_CLOUD_ONLY'"
              [disabled]="busy()"
              (checkedChange)="external('ULTIMATE_CLOUD_ONLY', $event)"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.cloudOnly' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.cloudOnlyHelp' | t
              }}</span>
            </span>
          </label>
          <p class="text-xs text-muted-foreground">{{ 'setup.privacy.streamingUnaffected' | t }}</p>

          @if (cloudOn()) {
            <div class="grid gap-1 border-t pt-3">
              <label class="flex items-center gap-2 text-sm">
                <hlm-switch
                  [checked]="s.anonymous"
                  [disabled]="busy() || s.noTelemetry || s.externalServices === 'NONE'"
                  (checkedChange)="setAnonymous($event)"
                />
                {{ 'settings.statistics.share' | t }}
              </label>
              <p class="text-xs text-muted-foreground">{{ 'settings.statistics.help' | t }}</p>
            </div>
          }
        </div>
      }
    </section>
  `,
})
export class PrivacyCard implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly extensions = inject(EnabledExtensions);

  /** The choices changed what other cards show (online services, Ultimate Cloud). */
  readonly changed = output<void>();

  protected readonly state = signal<State | null>(null);
  protected readonly busy = signal(false);
  protected readonly cloudOn = computed(() => this.extensions.has('ultimate-cloud'));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    const result = await this.gql.query(PrivacyQuery);
    this.state.set({ ...result.privacy, anonymous: result.anonymousStatistics });
  }

  /** One of the two "no outside services" choices, or neither. */
  protected async external(mode: Exclude<ExternalServicesMode, 'ALL'>, on: boolean): Promise<void> {
    const current = this.state()!.externalServices;
    await this.save({ externalServices: on ? mode : current === mode ? 'ALL' : current });
  }

  protected async save(change: Partial<Omit<State, 'anonymous'>>): Promise<void> {
    const current = this.state()!;
    await this.run(async () => {
      await this.gql.mutate(SetPrivacyMutation, {
        input: {
          noTelemetry: change.noTelemetry ?? current.noTelemetry,
          externalServices: change.externalServices ?? current.externalServices,
        },
      });
      await this.extensions.refresh();
      toast.success(this.i18n.t('settings.privacy.saved'));
    });
  }

  protected async setAnonymous(on: boolean): Promise<void> {
    await this.run(() => this.gql.mutate(SetAnonymousStatisticsMutation, { enabled: on }));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      // The server decides what stays on: show it as it is now.
      await this.load();
      this.busy.set(false);
      this.changed.emit();
    }
  }
}
