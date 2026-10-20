import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  P2PActivityKind,
  P2PActivityLogQuery as Result,
} from '../../graphql/generated/graphql';
import { P2PActivityLogQuery } from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';

const Kinds: P2PActivityKind[] = [
  'JOINED',
  'CONNECTED',
  'STREAMED',
  'DOWNLOADED',
  'REFUSED',
  'CHANGED',
  'REVOKED',
];
const Sizes = [10, 100, 500];

/** The P2P network's activity: filtered by peer, kind or words, a page of 10, 100 or 500 at a time. */
@Component({
  selector: 'app-p2p-activity-log',
  imports: [
    FormsModule,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'network.activity.title' | t }}</h2>
        <p hlmCardDescription>{{ 'network.activity.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid gap-4">
        <div class="flex flex-wrap items-center gap-2">
          <input
            hlmInput
            type="search"
            class="min-w-48 flex-1"
            [placeholder]="'network.activity.search' | t"
            [ngModel]="search()"
            (ngModelChange)="typed($event)"
          />
          <select
            hlmNativeSelect
            [ngModel]="peerId()"
            (ngModelChange)="filter(() => peerId.set($event))"
          >
            <option value="">{{ 'network.activity.allPeers' | t }}</option>
            @for (peer of peers(); track peer.id) {
              <option [value]="peer.id">{{ peer.name }}</option>
            }
          </select>
          <select
            hlmNativeSelect
            [ngModel]="kind()"
            (ngModelChange)="filter(() => kind.set($event))"
          >
            <option value="">{{ 'network.activity.allKinds' | t }}</option>
            @for (option of kinds; track option) {
              <option [value]="option">{{ 'network.activity.kinds.' + option | t }}</option>
            }
          </select>
          <select
            hlmNativeSelect
            [ngModel]="size()"
            (ngModelChange)="filter(() => size.set(+$event))"
          >
            @for (option of sizes; track option) {
              <option [ngValue]="option">
                {{ 'network.activity.perPage' | t: { count: option } }}
              </option>
            }
          </select>
        </div>

        @if (page(); as p) {
          <ul class="grid grid-cols-[minmax(0,1fr)] gap-1 text-sm">
            @for (entry of p.items; track entry.id) {
              <li class="flex flex-wrap items-baseline gap-x-2 border-b py-1 last:border-0">
                <span
                  class="w-28 shrink-0 text-xs text-muted-foreground"
                  [title]="entry.at | dateTime"
                  >{{ entry.at | relativeTime }}</span
                >
                <span class="min-w-0">
                  {{ 'network.activity.' + entry.kind | t: { peer: entry.peerName } }}
                  @if (entry.detail && entry.kind !== 'JOINED') {
                    <span class="text-muted-foreground">
                      ·
                      {{ entry.kind === 'REFUSED' ? i18n.error(entry.detail) : entry.detail }}</span
                    >
                  }
                </span>
              </li>
            } @empty {
              <li class="text-muted-foreground">{{ 'network.activity.empty' | t }}</li>
            }
          </ul>
          @if (p.totalCount > 0) {
            <div
              class="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground"
            >
              <span>{{
                'network.activity.range'
                  | t: { from: p.offset + 1, to: p.offset + p.items.length, total: p.totalCount }
              }}</span>
              <span class="flex gap-1">
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="loading() || p.offset === 0"
                  (click)="go(-1)"
                >
                  {{ 'network.activity.previous' | t }}
                </button>
                <span class="self-center px-1">{{
                  'network.activity.page' | t: { page: pageNumber(), pages: pages() }
                }}</span>
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="loading() || p.offset + p.items.length >= p.totalCount"
                  (click)="go(1)"
                >
                  {{ 'network.activity.next' | t }}
                </button>
              </span>
            </div>
          }
        } @else {
          <hlm-spinner class="size-5" />
        }
      </div>
    </section>
  `,
})
export class P2PActivityLog implements OnInit, OnDestroy {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  /** The peers to filter by. */
  readonly peers = input<readonly { id: string; name: string }[]>([]);

  protected readonly kinds = Kinds;
  protected readonly sizes = Sizes;
  protected readonly search = signal('');
  protected readonly peerId = signal('');
  protected readonly kind = signal<P2PActivityKind | ''>('');
  protected readonly size = signal(10);
  protected readonly offset = signal(0);
  protected readonly page = signal<Result['p2pActivityLog'] | null>(null);
  protected readonly loading = signal(false);
  protected readonly pages = computed(() =>
    Math.max(1, Math.ceil((this.page()?.totalCount ?? 0) / this.size())),
  );
  protected readonly pageNumber = computed(() => Math.floor(this.offset() / this.size()) + 1);

  private typing: ReturnType<typeof setTimeout> | null = null;
  private refresh: ReturnType<typeof setInterval> | null = null;

  async ngOnInit(): Promise<void> {
    await this.load();
    // The first page follows what happens; later ones stay as they are while being read.
    this.refresh = setInterval(() => {
      if (this.offset() === 0 && !this.loading()) void this.load();
    }, 10_000);
  }

  ngOnDestroy(): void {
    if (this.typing) clearTimeout(this.typing);
    if (this.refresh) clearInterval(this.refresh);
  }

  protected typed(text: string): void {
    this.search.set(text);
    if (this.typing) clearTimeout(this.typing);
    this.typing = setTimeout(() => void this.filter(() => undefined), 300);
  }

  /** Changes a filter and goes back to the first page. */
  protected async filter(change: () => void): Promise<void> {
    change();
    this.offset.set(0);
    await this.load();
  }

  protected async go(step: number): Promise<void> {
    this.offset.set(Math.max(0, this.offset() + step * this.size()));
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.gql.query(P2PActivityLogQuery, {
        input: {
          peerId: this.peerId() || null,
          kind: this.kind() || null,
          search: this.search().trim() || null,
          offset: this.offset(),
          first: this.size(),
        },
      });
      this.page.set(result.p2pActivityLog);
    } finally {
      this.loading.set(false);
    }
  }
}
