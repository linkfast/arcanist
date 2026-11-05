import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { ServerStatsQuery as Result, StatsPeriod } from '../../graphql/generated/graphql';
import { LibrariesQuery, ServerStatsQuery } from '../../graphql/operations';
import { BytesPipe, CountPipe, RelativeTimePipe } from '../../shared/format';
import { ListeningCharts } from '../../shared/listening-charts';

type Stats = Result['serverStats'];

/** What the libraries hold and how the server is listened to, for administrators with VIEW_ALL_STATS. */
@Component({
  selector: 'app-stats-page',
  imports: [
    FormsModule,
    HlmNativeSelectImports,
    HlmBadgeImports,
    HlmCardImports,
    HlmButtonImports,
    HlmSpinnerImports,
    HlmTableImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    RelativeTimePipe,
    ListeningCharts,
  ],
  template: `
    <header class="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold">{{ 'stats.title' | t }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'stats.subtitle' | t }}</p>
      </div>
      <div class="flex flex-wrap items-center gap-2" data-tour="stats-filters">
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
    </header>

    @if (stats(); as s) {
      <div class="grid gap-6">
        <section hlmCard data-tour="stats-library">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'stats.library' | t }}</h2>
          </div>
          <div hlmCardContent class="grid gap-4">
            <dl class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              @for (figure of libraryFigures(); track figure.label) {
                <div class="rounded-lg border p-3">
                  <dt class="text-xs text-muted-foreground">{{ figure.label | t }}</dt>
                  <dd class="text-xl font-semibold">
                    {{
                      figure.bytes === null || figure.bytes === undefined
                        ? figure.value
                        : (figure.bytes | bytes)
                    }}
                  </dd>
                </div>
              }
            </dl>
            @if (s.library.formats.length) {
              <div class="grid gap-1 text-sm">
                <p class="text-xs text-muted-foreground">{{ 'stats.formats' | t }}</p>
                @for (format of s.library.formats; track format.format) {
                  <div class="flex items-center gap-2">
                    <span class="w-40 truncate">{{
                      format.format || ('stats.unknownFormat' | t)
                    }}</span>
                    <div class="h-2 flex-1 rounded bg-muted">
                      <div
                        class="h-2 rounded bg-primary"
                        [style.width.%]="(format.files / s.library.files) * 100"
                      ></div>
                    </div>
                    <span class="w-32 text-right text-xs text-muted-foreground">
                      {{ format.files | count }} · {{ format.sizeBytes | bytes }}
                    </span>
                  </div>
                }
              </div>
            }
          </div>
        </section>

        <section hlmCard data-tour="stats-listening">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'stats.listening' | t }}</h2>
          </div>
          <div hlmCardContent class="grid gap-6">
            <dl class="grid grid-cols-2 gap-3 sm:grid-cols-4">
              @for (figure of listeningFigures(); track figure.label) {
                <div class="rounded-lg border p-3">
                  <dt class="text-xs text-muted-foreground">{{ figure.label | t }}</dt>
                  <dd class="text-xl font-semibold">{{ figure.value }}</dd>
                  @if (figure.note) {
                    <p class="text-xs text-muted-foreground">{{ figure.note }}</p>
                  }
                </div>
              }
            </dl>

            <app-listening-charts [listening]="s.listening" />
          </div>
        </section>

        <section hlmCard data-tour="stats-users">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'stats.users' | t }}</h2>
          </div>
          <div hlmCardContent class="overflow-x-auto">
            <table hlmTable class="w-full text-sm">
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'stats.user' | t }}</th>
                  <th hlmTh class="text-right">{{ 'stats.plays' | t }}</th>
                  <th hlmTh class="text-right">{{ 'stats.listened' | t }}</th>
                  <th hlmTh class="text-right">{{ 'stats.lastPlayed' | t }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                <!-- Users of another server share its id: their names tell them apart. -->
                @for (user of s.users; track user.userId + user.userName) {
                  <tr hlmTr>
                    <td hlmTd>
                      {{ user.displayName || user.userName }}
                      @if (user.remote) {
                        <span hlmBadge variant="outline" class="ml-1">{{
                          'stats.remote' | t: { server: user.server ?? '' }
                        }}</span>
                      }
                    </td>
                    <td hlmTd class="text-right">{{ user.plays | count }}</td>
                    <td hlmTd class="text-right">
                      {{ 'stats.hours' | t: { hours: hours(user.listenedMs) } }}
                    </td>
                    <td hlmTd class="text-right text-muted-foreground">
                      {{
                        user.lastPlayedAt ? (user.lastPlayedAt | relativeTime) : ('stats.never' | t)
                      }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      </div>
    } @else {
      <div class="flex justify-center py-12"><hlm-spinner /></div>
    }
  `,
})
export class StatsPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** The library the statistics are of ("" for all of them), kept in the address (?library=). */
  protected readonly library = signal('');
  protected readonly libraries = signal<{ id: string; name: string }[]>([]);

  protected readonly listeningFigures = computed(() => {
    const s = this.stats();
    if (!s) return [];
    const count = new Intl.NumberFormat(this.i18n.language(), { maximumFractionDigits: 1 });
    const l = s.listening;
    const figures: { label: string; value: string; note?: string }[] = [
      { label: 'stats.plays', value: count.format(l.plays) },
      {
        label: 'stats.listened',
        value: this.i18n.t('stats.hours', { hours: this.hours(l.listenedMs) }),
      },
      { label: 'stats.distinctTracks', value: count.format(l.distinctTracks) },
      { label: 'stats.listeners', value: count.format(l.listeners) },
      {
        label: 'stats.skips',
        value: count.format(l.skips),
        note:
          l.plays + l.skips
            ? this.i18n.t('stats.skipRate', {
                rate: Math.round((l.skips / (l.plays + l.skips)) * 100),
              })
            : undefined,
      },
      {
        label: 'stats.activeDays',
        value: count.format(l.activeDays),
        note: l.activeDays
          ? this.i18n.t('stats.perDay', { plays: count.format(l.plays / l.activeDays) })
          : undefined,
      },
      { label: 'stats.added', value: count.format(s.addedTracks) },
    ];
    if (s.remotePlays || s.remoteListenedMs)
      figures.push({
        label: 'stats.remotePlays',
        value: count.format(s.remotePlays),
        note: this.i18n.t('stats.hours', { hours: this.hours(s.remoteListenedMs) }),
      });
    return figures;
  });

  protected async showLibrary(id: string): Promise<void> {
    this.library.set(id);
    await this.router.navigate([], { queryParams: { library: id || null }, replaceUrl: true });
    this.stats.set(null);
    await this.load();
  }

  protected readonly periods: StatsPeriod[] = ['WEEK', 'MONTH', 'YEAR', 'ALL'];
  protected readonly period = signal<StatsPeriod>('MONTH');
  protected readonly stats = signal<Stats | null>(null);

  protected readonly libraryFigures = computed(() => {
    const library = this.stats()?.library;
    if (!library) return [];
    const count = new Intl.NumberFormat(this.i18n.language());
    return [
      { label: 'stats.tracks', value: count.format(library.tracks), bytes: null as number | null },
      { label: 'stats.albums', value: count.format(library.albums) },
      { label: 'stats.artists', value: count.format(library.artists) },
      { label: 'stats.files', value: count.format(library.files) },
      {
        label: 'stats.duration',
        value: this.i18n.t('stats.hours', { hours: this.hours(library.durationMs) }),
      },
      { label: 'stats.size', value: '', bytes: library.sizeBytes },
    ];
  });

  async ngOnInit(): Promise<void> {
    this.library.set(this.route.snapshot.queryParamMap.get('library') ?? '');
    const libraries = await this.gql.query(LibrariesQuery);
    this.libraries.set(libraries.libraries.map((l) => ({ id: l.id, name: l.name })));
    await this.load();
  }

  protected async show(period: StatsPeriod): Promise<void> {
    this.period.set(period);
    this.stats.set(null);
    await this.load();
  }

  protected hours(ms: number): string {
    return new Intl.NumberFormat(this.i18n.language(), { maximumFractionDigits: 1 }).format(
      ms / 3_600_000,
    );
  }

  private async load(): Promise<void> {
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    const result = await this.gql.query(ServerStatsQuery, {
      period: this.period(),
      timeZone,
      libraryId: this.library() || null,
    });
    this.stats.set(result.serverStats);
  }
}
