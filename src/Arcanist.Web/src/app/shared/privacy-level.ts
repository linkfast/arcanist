import { Component, computed, input, output, signal } from '@angular/core';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { ExternalServicesMode } from '../core/api/rest';
import { TranslatePipe } from '../core/i18n/i18n';

/** The two privacy choices the server keeps. */
export interface PrivacyChoice {
  noTelemetry: boolean;
  externalServices: ExternalServicesMode;
}

type Level = 'open' | 'private' | 'closed';

/**
 * The three privacy levels as a traffic light: green (everything on), amber (no telemetry: nothing about what is
 * played leaves the server, online services still work) and red (no telemetry and nothing outside: no online
 * services and no Ultimate Cloud). "Customize" shows the choices one by one, for the combinations in between.
 */
const Levels: Record<Level, PrivacyChoice> = {
  open: { noTelemetry: false, externalServices: 'ALL' },
  private: { noTelemetry: true, externalServices: 'ALL' },
  closed: { noTelemetry: true, externalServices: 'NONE' },
};

const Tones: Record<Level, { dot: string; selected: string }> = {
  open: {
    dot: 'bg-emerald-500',
    selected: 'border-emerald-500 bg-emerald-50 ring-1 ring-emerald-500 dark:bg-emerald-950/40',
  },
  private: {
    dot: 'bg-amber-500',
    selected: 'border-amber-500 bg-amber-50 ring-1 ring-amber-500 dark:bg-amber-950/40',
  },
  closed: {
    dot: 'bg-red-500',
    selected: 'border-red-500 bg-red-50 ring-1 ring-red-500 dark:bg-red-950/40',
  },
};

@Component({
  selector: 'app-privacy-level',
  imports: [HlmCheckboxImports, TranslatePipe],
  template: `
    <div class="grid gap-3">
      <div
        class="grid gap-2 sm:grid-cols-3"
        role="radiogroup"
        [attr.aria-label]="'privacyLevel.label' | t"
      >
        @for (level of levels; track level) {
          <button
            type="button"
            role="radio"
            [attr.aria-checked]="current() === level"
            [disabled]="disabled()"
            class="grid content-start gap-1.5 rounded-lg border p-3 text-left text-sm transition-colors hover:bg-muted/50 disabled:opacity-60"
            [class]="current() === level ? tones[level].selected : ''"
            (click)="pick(level)"
          >
            <span class="flex items-center gap-2 font-medium">
              <span class="size-3 shrink-0 rounded-full" [class]="tones[level].dot"></span>
              {{ 'privacyLevel.' + level + '.title' | t }}
            </span>
            <span class="text-xs text-muted-foreground">{{
              'privacyLevel.' + level + '.help' | t
            }}</span>
          </button>
        }
      </div>
      @if (current() === 'custom') {
        <p class="text-xs text-muted-foreground">{{ 'privacyLevel.custom' | t }}</p>
      }
      <button
        type="button"
        class="justify-self-start text-xs underline underline-offset-2"
        [attr.aria-expanded]="details()"
        (click)="details.set(!details())"
      >
        {{ (details() ? 'privacyLevel.hideDetails' : 'privacyLevel.showDetails') | t }}
      </button>
      @if (details()) {
        <div class="grid gap-2">
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="value().noTelemetry"
              [disabled]="disabled()"
              (checkedChange)="emit({ noTelemetry: $event })"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.noTelemetry' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.noTelemetryHelp' | t
              }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="value().externalServices === 'NONE'"
              [disabled]="disabled()"
              (checkedChange)="external('NONE', $event)"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.noExternal' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.noExternalHelp' | t
              }}</span>
            </span>
          </label>
          <label class="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <hlm-checkbox
              class="mt-0.5"
              [checked]="value().externalServices === 'ULTIMATE_CLOUD_ONLY'"
              [disabled]="disabled()"
              (checkedChange)="external('ULTIMATE_CLOUD_ONLY', $event)"
            />
            <span class="grid gap-0.5">
              <span class="font-medium">{{ 'setup.privacy.cloudOnly' | t }}</span>
              <span class="text-xs text-muted-foreground">{{
                'setup.privacy.cloudOnlyHelp' | t
              }}</span>
            </span>
          </label>
        </div>
      }
      <p class="text-xs text-muted-foreground">{{ 'setup.privacy.streamingUnaffected' | t }}</p>
    </div>
  `,
})
export class PrivacyLevel {
  readonly value = input.required<PrivacyChoice>();
  readonly disabled = input(false);
  readonly changed = output<PrivacyChoice>();

  protected readonly levels = Object.keys(Levels) as Level[];
  protected readonly tones = Tones;
  protected readonly details = signal(false);

  /** The level the choices are, or "custom" for a combination in between. */
  protected readonly current = computed<Level | 'custom'>(() => {
    const value = this.value();
    return (
      this.levels.find(
        (level) =>
          Levels[level].noTelemetry === value.noTelemetry &&
          Levels[level].externalServices === value.externalServices,
      ) ?? 'custom'
    );
  });

  protected pick(level: Level): void {
    this.changed.emit({ ...Levels[level] });
  }

  protected emit(change: Partial<PrivacyChoice>): void {
    this.changed.emit({ ...this.value(), ...change });
  }

  /** One of the two "no outside services" choices, or neither. */
  protected external(mode: Exclude<ExternalServicesMode, 'ALL'>, on: boolean): void {
    const current = this.value().externalServices;
    this.emit({ externalServices: on ? mode : current === mode ? 'ALL' : current });
  }
}
