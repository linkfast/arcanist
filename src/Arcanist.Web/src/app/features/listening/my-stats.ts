import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { ListeningStatsFieldsFragment, StatsPeriod } from '../../graphql/generated/graphql';
import { MyListeningStatsQuery } from '../../graphql/operations';
import { ListeningCharts } from '../../shared/listening-charts';

/** The user's own listening over a period. */
@Component({
  selector: 'app-my-stats',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmCardImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    ListeningCharts,
    TranslatePipe,
  ],
  template: `
    <div class="mb-4 flex flex-wrap items-center gap-2" data-tour="listening-filters">
      <select
        hlmNativeSelect
        class="max-w-56"
        [attr.aria-label]="'stats.libraryFilter' | t"
        [ngModel]="library()"
        (ngModelChange)="showLibrary($event)"
      >
        <option value="">{{ 'stats.allLibraries' | t }}</option>
        @for (option of libraries(); track option.id) {
          <option [value]="option.id">{{ option.name }}</option>
        }
      </select>
      <div class="flex flex-wrap gap-1" role="tablist">
        @for (option of periods; track option) {
          <button
            hlmBtn
            size="sm"
            type="button"
            role="tab"
            [attr.aria-selected]="period() === option"
            [variant]="period() === option ? 'default' : 'ghost'"
            (click)="show(option)"
          >
            {{ 'stats.periods.' + option | t }}
          </button>
        }
      </div>
    </div>

    @if (error()) {
      <p class="text-sm text-destructive">{{ error() }}</p>
    } @else if (stats(); as s) {
      <section hlmCard>
        <div hlmCardContent class="grid gap-6">
          <dl class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
            @for (figure of figures(); track figure.label) {
              <div class="rounded-lg border p-3">
                <dt class="text-xs text-muted-foreground">{{ figure.label | t }}</dt>
                <dd class="text-xl font-semibold">{{ figure.value }}</dd>
                @if (figure.note) {
                  <p class="text-xs text-muted-foreground">{{ figure.note }}</p>
                }
              </div>
            }
          </dl>
          <app-listening-charts [listening]="s" />
        </div>
      </section>
    } @else {
      <div class="flex justify-center py-12"><hlm-spinner /></div>
    }
  `,
})
export class MyStats implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly periods: StatsPeriod[] = ['WEEK', 'MONTH', 'YEAR', 'ALL'];
  protected readonly period = signal<StatsPeriod>('MONTH');
  protected readonly library = signal('');
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);
  protected readonly stats = signal<ListeningStatsFieldsFragment | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly figures = computed(() => {
    const l = this.stats();
    if (!l) return [];
    const count = new Intl.NumberFormat(this.i18n.language(), { maximumFractionDigits: 1 });
    const hours = count.format(l.listenedMs / 3_600_000);
    const heard = l.plays + l.skips;
    return [
      { label: 'stats.plays', value: count.format(l.plays) },
      { label: 'stats.listened', value: this.i18n.t('stats.hours', { hours }) },
      { label: 'stats.distinctTracks', value: count.format(l.distinctTracks) },
      {
        label: 'stats.skips',
        value: count.format(l.skips),
        note: heard
          ? this.i18n.t('stats.skipRate', { rate: Math.round((l.skips / heard) * 100) })
          : undefined,
      },
      {
        label: 'stats.activeDays',
        value: count.format(l.activeDays),
        note: l.activeDays
          ? this.i18n.t('stats.perDay', { plays: count.format(l.plays / l.activeDays) })
          : undefined,
      },
    ];
  });

  ngOnInit(): Promise<void> {
    return this.load();
  }

  protected show(period: StatsPeriod): Promise<void> {
    this.period.set(period);
    return this.load();
  }

  protected showLibrary(id: string): Promise<void> {
    this.library.set(id);
    return this.load();
  }

  private async load(): Promise<void> {
    this.stats.set(null);
    this.error.set(null);
    try {
      const result = await this.gql.query(MyListeningStatsQuery, {
        period: this.period(),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        libraryId: this.library() || null,
      });
      this.libraries.set(result.libraries);
      this.stats.set(result.listeningStats);
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    }
  }
}
