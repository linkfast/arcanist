import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { Gql } from '../core/graphql/gql.service';
import { TranslatePipe } from '../core/i18n/i18n';
import type {
  StoragePartKind,
  StorageUsageQuery as StorageUsageResult,
} from '../graphql/generated/graphql';
import { StorageUsageQuery } from '../graphql/operations';
import { BytesPipe, CountPipe, RelativeTimePipe } from './format';

type Usage = StorageUsageResult['storageUsage'];

/** The colour of each part in the bar and its legend. */
const PartColors: Record<StoragePartKind, string> = {
  DATABASE: 'bg-sky-500',
  ARTWORK: 'bg-violet-500',
  THUMBNAILS: 'bg-fuchsia-400',
  TEMPORARY: 'bg-amber-500',
  LOGS: 'bg-slate-400',
  TOOLS: 'bg-emerald-500',
  PLAYERS: 'bg-rose-500',
  EXTENSIONS: 'bg-teal-500',
  TRANSCODES: 'bg-lime-500',
  REMOTE_CACHE: 'bg-indigo-400',
  OTHER: 'bg-zinc-500',
};

/** How many tables are shown before "show all". */
const FirstTables = 12;

/**
 * How much space Arcanist and its music take. Compact on the Home page (with a link to the details); with
 * <c>detail</c>, on the Database page: what the catalog holds, each part, each library and each table.
 */
@Component({
  selector: 'app-storage-usage',
  imports: [
    RouterLink,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    RelativeTimePipe,
  ],
  providers: [provideIcons({ lucideRefreshCw })],
  template: `
    <div class="grid grid-cols-[minmax(0,1fr)] gap-6">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'storage.title' | t }}</h2>
          <p hlmCardDescription>
            @if (usage(); as current) {
              {{ 'storage.measured' | t: { when: (current.measuredAt | relativeTime) } }}
            } @else {
              {{ 'storage.measuring' | t }}
            }
          </p>
          <div hlmCardAction class="flex gap-2">
            @if (!detail()) {
              <a hlmBtn variant="outline" size="sm" routerLink="/database">{{
                'storage.details' | t
              }}</a>
            }
            <button
              hlmBtn
              variant="ghost"
              size="icon"
              type="button"
              [attr.aria-label]="'storage.refresh' | t"
              [disabled]="loading()"
              (click)="load(true)"
            >
              <ng-icon name="lucideRefreshCw" [class.animate-spin]="loading()" />
            </button>
          </div>
        </div>
        <div hlmCardContent class="grid gap-4">
          @if (usage(); as current) {
            <div class="grid gap-4 sm:grid-cols-3">
              <div>
                <p class="text-xs text-muted-foreground">{{ 'storage.arcanist' | t }}</p>
                <p class="text-2xl font-semibold">{{ current.arcanistBytes | bytes }}</p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">{{ 'storage.music' | t }}</p>
                <p class="text-2xl font-semibold">{{ current.musicBytes | bytes }}</p>
              </div>
              @if (current.disk; as disk) {
                <div>
                  <p class="text-xs text-muted-foreground">{{ 'storage.free' | t }}</p>
                  <p class="text-2xl font-semibold">{{ disk.freeBytes | bytes }}</p>
                  <p class="text-xs text-muted-foreground">
                    {{ 'storage.ofDisk' | t: { total: (disk.totalBytes | bytes) } }}
                  </p>
                </div>
              }
            </div>

            <!-- Arcanist's own data, part by part. -->
            <div
              class="flex h-3 w-full overflow-hidden rounded-full bg-muted"
              role="img"
              [attr.aria-label]="'storage.arcanist' | t"
            >
              @for (part of parts(); track part.kind) {
                <div
                  [class]="color(part.kind)"
                  [style.width.%]="share(part.bytes)"
                  [title]="('storage.parts.' + part.kind | t) + ': ' + (part.bytes | bytes)"
                ></div>
              }
            </div>
            <ul class="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2 lg:grid-cols-3">
              @for (part of parts(); track part.kind) {
                <li class="flex items-center gap-2">
                  <span class="size-2.5 shrink-0 rounded-full" [class]="color(part.kind)"></span>
                  <span class="min-w-0 truncate">{{ 'storage.parts.' + part.kind | t }}</span>
                  <span class="ml-auto text-muted-foreground">{{ part.bytes | bytes }}</span>
                </li>
              }
            </ul>
            @if (current.database.provider === 'POSTGRES') {
              <p class="text-xs text-muted-foreground">{{ 'storage.postgresElsewhere' | t }}</p>
            }
            @if (detail()) {
              <p class="text-xs text-muted-foreground">
                {{ 'storage.dataDirectory' | t }}
                <code class="break-all">{{ current.dataDirectory }}</code>
              </p>
            }
          } @else {
            <hlm-skeleton class="h-24 w-full" />
          }
        </div>
      </section>

      @if (detail() && usage(); as current) {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'storage.catalog.title' | t }}</h2>
            <p hlmCardDescription>{{ 'storage.catalog.description' | t }}</p>
          </div>
          <dl hlmCardContent class="grid grid-cols-2 gap-4 sm:grid-cols-5">
            @for (count of counts(); track count.key) {
              <div>
                <dt class="text-xs text-muted-foreground">
                  {{ 'storage.catalog.' + count.key | t }}
                </dt>
                <dd class="text-xl font-semibold">{{ count.value | count }}</dd>
              </div>
            }
          </dl>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'storage.libraries.title' | t }}</h2>
            <p hlmCardDescription>{{ 'storage.libraries.description' | t }}</p>
          </div>
          <div hlmCardContent>
            <table class="w-full text-sm">
              <thead class="text-left text-xs text-muted-foreground">
                <tr>
                  <th class="pb-2 font-normal">{{ 'storage.libraries.name' | t }}</th>
                  <th class="pb-2 font-normal">{{ 'storage.libraries.where' | t }}</th>
                  <th class="pb-2 text-right font-normal">{{ 'storage.libraries.files' | t }}</th>
                  <th class="pb-2 text-right font-normal">{{ 'storage.libraries.size' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (library of current.libraries; track library.id) {
                  <tr class="border-t">
                    <td class="max-w-0 truncate py-1.5 pr-2">{{ library.name }}</td>
                    <td class="py-1.5 pr-2">
                      <span hlmBadge variant="outline">{{
                        'storage.libraries.kinds.' + library.kind | t
                      }}</span>
                    </td>
                    <td class="py-1.5 text-right">{{ library.files | count }}</td>
                    <td class="py-1.5 text-right">{{ library.bytes | bytes }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="text-muted-foreground">
                      {{ 'storage.libraries.none' | t }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'storage.tables.title' | t }}</h2>
            <p hlmCardDescription>
              {{ current.database.provider === 'SQLITE' ? 'SQLite' : 'PostgreSQL' }}
              @if (current.database.version) {
                {{ current.database.version }}
              }
              @if (
                current.database.sizeBytes !== null && current.database.sizeBytes !== undefined
              ) {
                · {{ current.database.sizeBytes | bytes }}
              }
              · {{ 'storage.tables.rows' | t: { rows: (totalRows() | count) } }}
            </p>
          </div>
          <div hlmCardContent class="grid gap-2">
            <ul class="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2 lg:grid-cols-3">
              @for (table of tables(); track table.name) {
                <li class="flex items-center gap-2">
                  <code class="min-w-0 truncate text-xs">{{ table.name }}</code>
                  <span class="ml-auto text-muted-foreground">{{ table.rows | count }}</span>
                </li>
              }
            </ul>
            @if (current.database.tables.length > firstTables) {
              <button
                hlmBtn
                variant="ghost"
                size="sm"
                type="button"
                class="justify-self-start"
                (click)="allTables.set(!allTables())"
              >
                {{
                  (allTables() ? 'storage.tables.fewer' : 'storage.tables.all')
                    | t: { count: current.database.tables.length }
                }}
              </button>
            }
          </div>
        </section>
      }
    </div>
  `,
})
export class StorageUsageCard implements OnInit {
  private readonly gql = inject(Gql);

  /** The Database page shows everything; the Home page, the summary. */
  readonly detail = input(false);

  protected readonly usage = signal<Usage | null>(null);
  protected readonly loading = signal(false);
  protected readonly allTables = signal(false);
  protected readonly firstTables = FirstTables;

  /** The parts that take something, the biggest first. */
  protected readonly parts = computed(() =>
    [...(this.usage()?.parts ?? [])].filter((p) => p.bytes > 0).sort((a, b) => b.bytes - a.bytes),
  );

  protected readonly tables = computed(() => {
    const tables = this.usage()?.database.tables ?? [];
    return this.allTables() ? tables : tables.slice(0, FirstTables);
  });

  protected readonly totalRows = computed(() =>
    (this.usage()?.database.tables ?? []).reduce((sum, t) => sum + t.rows, 0),
  );

  protected readonly counts = computed(() => {
    const catalog = this.usage()?.catalog;
    if (!catalog) return [];
    return (
      [
        'libraries',
        'artists',
        'albums',
        'tracks',
        'files',
        'missingFiles',
        'playlists',
        'users',
        'plays',
        'ratings',
      ] as const
    ).map((key) => ({ key, value: catalog[key] }));
  });

  ngOnInit(): void {
    void this.load(false);
  }

  protected async load(refresh: boolean): Promise<void> {
    this.loading.set(true);
    try {
      this.usage.set((await this.gql.query(StorageUsageQuery, { refresh })).storageUsage);
    } catch {
      // The card keeps what it showed.
    } finally {
      this.loading.set(false);
    }
  }

  protected color(kind: StoragePartKind): string {
    return PartColors[kind];
  }

  /** A part's share of the bar; tiny parts still show a sliver. */
  protected share(bytes: number): number {
    const total = this.usage()?.arcanistBytes ?? 0;
    return total > 0 ? Math.max((bytes / total) * 100, 0.5) : 0;
  }
}
