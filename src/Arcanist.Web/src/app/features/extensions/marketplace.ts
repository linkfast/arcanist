import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBadgeCheck,
  lucideBookOpen,
  lucideExternalLink,
  lucideMonitorPlay,
  lucidePuzzle,
  lucideRefreshCw,
  lucideSearch,
} from '@ng-icons/lucide';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  ExtensionFieldsFragment as Extension,
  ExtensionMarketplaceQuery,
} from '../../graphql/generated/graphql';
import {
  ExtensionMarketplaceQuery as MarketplaceQuery,
  InstallMarketplaceExtensionMutation,
  InstallMarketplacePlayerMutation,
  PlayerMarketplaceQuery,
} from '../../graphql/operations';
import { BytesPipe } from '../../shared/format';

type Entry = ExtensionMarketplaceQuery['extensionMarketplace']['extensions'][number] & {
  /** A web player served at /. */
  isDefault?: boolean;
};
type Pricing = NonNullable<Entry['pricing']>;

/** What either catalog (extensions or web players) says, the same way. */
interface Marketplace {
  available: boolean;
  errorCode?: string | null;
  extensions: Entry[];
}

/** Extensions, or web players (Ultimate Web Player and those anyone publishes). */
export type MarketplaceKind = 'extensions' | 'players';

/**
 * The catalog (github.com/linkfast/arcanist-extensions) of extensions or of web players: what there is, what is
 * installed here, and installing or updating with one click. Community entries ask for confirmation first, as any
 * package.
 */
@Component({
  selector: 'app-extension-marketplace',
  imports: [
    FormsModule,
    NgIcon,
    BrnAlertDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSpinnerImports,
    TranslatePipe,
    BytesPipe,
  ],
  providers: [
    provideIcons({
      lucideBadgeCheck,
      lucideBookOpen,
      lucideExternalLink,
      lucideMonitorPlay,
      lucidePuzzle,
      lucideRefreshCw,
      lucideSearch,
    }),
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'extensions.marketplace.' + kind() + '.title' | t }}</h2>
        <p hlmCardDescription>{{ 'extensions.marketplace.' + kind() + '.description' | t }}</p>
        <div hlmCardAction>
          <button
            hlmBtn
            variant="ghost"
            size="sm"
            type="button"
            [disabled]="loading() || busy()"
            (click)="load(true)"
          >
            <ng-icon name="lucideRefreshCw" class="mr-1.5" />
            {{ 'extensions.marketplace.refresh' | t }}
          </button>
        </div>
      </div>
      <div hlmCardContent class="grid gap-4">
        @if (marketplace(); as market) {
          @if (!market.available) {
            <div hlmAlert>
              <p hlmAlertDescription>
                {{ i18n.error(market.errorCode ?? 'marketplace_unavailable') }}
              </p>
            </div>
          } @else {
            <div class="relative">
              <ng-icon
                name="lucideSearch"
                class="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-muted-foreground"
              />
              <input
                hlmInput
                class="w-full pl-8"
                type="search"
                [attr.aria-label]="'extensions.marketplace.' + kind() + '.search' | t"
                [placeholder]="'extensions.marketplace.' + kind() + '.search' | t"
                [ngModel]="query()"
                (ngModelChange)="query.set($event)"
              />
            </div>
            @if (pricings().length > 1) {
              <div
                class="flex flex-wrap gap-1"
                role="group"
                [attr.aria-label]="'extensions.marketplace.pricingFilter' | t"
              >
                <button
                  hlmBtn
                  size="sm"
                  type="button"
                  [variant]="pricing() === null ? 'secondary' : 'ghost'"
                  [attr.aria-pressed]="pricing() === null"
                  (click)="pricing.set(null)"
                >
                  {{ 'extensions.marketplace.anyPricing' | t }}
                </button>
                @for (option of pricings(); track option) {
                  <button
                    hlmBtn
                    size="sm"
                    type="button"
                    [variant]="pricing() === option ? 'secondary' : 'ghost'"
                    [attr.aria-pressed]="pricing() === option"
                    (click)="pricing.set(pricing() === option ? null : option)"
                  >
                    {{ 'extensions.marketplace.pricing.' + option | t }}
                  </button>
                }
              </div>
            }
            @if (categories().length > 1) {
              <div
                class="flex flex-wrap gap-1"
                role="group"
                [attr.aria-label]="'extensions.marketplace.categories' | t"
              >
                <button
                  hlmBtn
                  size="sm"
                  type="button"
                  [variant]="category() === null ? 'secondary' : 'ghost'"
                  [attr.aria-pressed]="category() === null"
                  (click)="category.set(null)"
                >
                  {{ 'extensions.marketplace.allCategories' | t }}
                </button>
                @for (option of categories(); track option) {
                  <button
                    hlmBtn
                    size="sm"
                    type="button"
                    [variant]="category() === option ? 'secondary' : 'ghost'"
                    [attr.aria-pressed]="category() === option"
                    (click)="category.set(category() === option ? null : option)"
                  >
                    {{ option }}
                  </button>
                }
              </div>
            }
            <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              @for (entry of shown(); track entry.id) {
                <li class="flex min-w-0 flex-col gap-3 rounded-lg border p-4">
                  <div class="flex min-w-0 items-start gap-3">
                    @if (entry.iconUrl && !brokenIcons().has(entry.id)) {
                      <img
                        [src]="entry.iconUrl"
                        alt=""
                        class="size-10 shrink-0 rounded-md"
                        (error)="iconFailed(entry.id)"
                      />
                    } @else {
                      <span
                        class="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground"
                      >
                        <ng-icon
                          [name]="kind() === 'players' ? 'lucideMonitorPlay' : 'lucidePuzzle'"
                          size="20"
                        />
                      </span>
                    }
                    <div class="min-w-0">
                      <p class="truncate font-medium">{{ entry.name }}</p>
                      <p class="flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
                        @if (entry.official) {
                          <span hlmBadge class="gap-1"
                            ><ng-icon name="lucideBadgeCheck" size="12" />{{
                              'extensions.marketplace.official' | t
                            }}</span
                          >
                        } @else {
                          <span hlmBadge variant="secondary">{{
                            'extensions.marketplace.community' | t
                          }}</span>
                        }
                        @if (entry.pricing) {
                          <span
                            hlmBadge
                            variant="outline"
                            [class]="pricingClass[entry.pricing]"
                            [title]="'extensions.marketplace.pricingHelp.' + entry.pricing | t"
                            >{{ 'extensions.marketplace.pricing.' + entry.pricing | t }}</span
                          >
                        }
                        @if (entry.isDefault) {
                          <span hlmBadge>{{ 'extensions.players.default' | t }}</span>
                        }
                        @if (entry.author) {
                          <span class="truncate">{{ entry.author }}</span>
                        }
                      </p>
                    </div>
                  </div>
                  @if (entry.description) {
                    <p class="line-clamp-3 text-sm text-muted-foreground">
                      {{ entry.description }}
                    </p>
                  }
                  <div class="mt-auto flex flex-wrap items-center justify-between gap-2">
                    <p class="text-xs text-muted-foreground">
                      {{ entry.version }}
                      @if (entry.size) {
                        · {{ entry.size | bytes }}
                      }
                      @if (entry.homepage) {
                        ·
                        <a
                          class="underline"
                          [href]="entry.homepage"
                          target="_blank"
                          rel="noopener"
                          >{{ 'extensions.marketplace.homepage' | t }}</a
                        >
                      }
                      @if (entry.pricingUrl) {
                        ·
                        <a
                          class="underline"
                          [href]="entry.pricingUrl"
                          target="_blank"
                          rel="noopener"
                          >{{ 'extensions.marketplace.prices' | t }}</a
                        >
                      }
                    </p>
                    @if (entry.builtIn) {
                      <span hlmBadge variant="outline">{{ 'extensions.builtIn' | t }}</span>
                    } @else if (!entry.compatible) {
                      <span hlmBadge variant="outline">{{
                        'extensions.marketplace.needs'
                          | t: { version: entry.minServerVersion ?? '' }
                      }}</span>
                    } @else if (entry.installedVersion && !entry.updateAvailable) {
                      <span hlmBadge variant="outline">{{
                        'extensions.marketplace.installed' | t
                      }}</span>
                    } @else {
                      <button
                        hlmBtn
                        size="sm"
                        type="button"
                        [disabled]="busy()"
                        (click)="ask(entry)"
                      >
                        @if (installing() === entry.id) {
                          <hlm-spinner class="mr-1.5 size-4" />
                        }
                        {{
                          (entry.updateAvailable
                            ? 'extensions.marketplace.update'
                            : 'extensions.marketplace.install'
                          ) | t
                        }}
                      </button>
                    }
                  </div>
                </li>
              } @empty {
                <li class="text-sm text-muted-foreground">
                  {{ 'extensions.marketplace.' + kind() + '.none' | t }}
                </li>
              }
            </ul>
          }
        } @else {
          <hlm-spinner class="size-5" />
        }
      </div>
    </section>

    <section hlmCard class="mt-6">
      <div hlmCardHeader>
        <h2 hlmCardTitle class="flex items-center gap-2">
          <ng-icon name="lucideBookOpen" />
          {{ 'extensions.marketplace.' + kind() + '.createTitle' | t }}
        </h2>
        <p hlmCardDescription>
          {{ 'extensions.marketplace.' + kind() + '.createDescription' | t }}
        </p>
      </div>
      <div hlmCardContent class="grid gap-3 text-sm">
        <ol class="grid list-decimal gap-1.5 pl-5 text-muted-foreground">
          <li>{{ 'extensions.marketplace.' + kind() + '.write' | t }}</li>
          <li>{{ 'extensions.marketplace.' + kind() + '.release' | t }}</li>
          <li>{{ 'extensions.marketplace.' + kind() + '.propose' | t }}</li>
          <li>{{ 'extensions.marketplace.' + kind() + '.manage' | t }}</li>
        </ol>
        <div class="flex flex-wrap gap-2">
          <a
            hlmBtn
            variant="outline"
            size="sm"
            [href]="kind() === 'players' ? playerGuideUrl : guideUrl"
            target="_blank"
            rel="noopener"
          >
            {{ 'extensions.marketplace.create.guide' | t }}
            <ng-icon name="lucideExternalLink" class="ml-1.5" />
          </a>
          <a hlmBtn variant="outline" size="sm" [href]="publishUrl" target="_blank" rel="noopener">
            {{ 'extensions.marketplace.create.howToPublish' | t }}
            <ng-icon name="lucideExternalLink" class="ml-1.5" />
          </a>
          <a hlmBtn size="sm" [href]="proposeUrl" target="_blank" rel="noopener">
            {{ 'extensions.marketplace.' + kind() + '.proposeButton' | t }}
            <ng-icon name="lucideExternalLink" class="ml-1.5" />
          </a>
        </div>
      </div>
    </section>

    <hlm-alert-dialog [state]="confirming() ? 'open' : 'closed'" (closed)="confirming.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ 'extensions.install.confirmTitle' | t }}</h3>
          <p hlmAlertDialogDescription>
            {{
              (kind() === 'players'
                ? 'extensions.install.playerTrust'
                : 'extensions.marketplace.communityTrust'
              ) | t
            }}
          </p>
          <p class="text-sm font-medium">
            {{ confirming()?.name }}
            @if (confirming()?.author; as author) {
              · {{ author }}
            }
          </p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="confirming.set(null)">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="confirmed()">
            {{ 'extensions.install.confirm' | t }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ExtensionMarketplace implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  /** The page is busy with another change. */
  readonly busy = input(false);
  /** Which catalog it shows. */
  readonly kind = input<MarketplaceKind>('extensions');
  /** An extension was installed (or updated) from the catalog. */
  readonly installed = output<Extension>();
  /** A web player was installed (or updated) from the catalog. */
  readonly installedPlayer = output<void>();

  protected readonly marketplace = signal<Marketplace | null>(null);
  protected readonly loading = signal(false);
  protected readonly installing = signal<string | null>(null);
  protected readonly confirming = signal<Entry | null>(null);
  protected readonly query = signal('');
  protected readonly category = signal<string | null>(null);
  protected readonly pricing = signal<Pricing | null>(null);

  /** Colors of the pricing badges (with their dark variants). */
  protected readonly pricingClass: Record<Pricing, string> = {
    OPEN_SOURCE: 'border-emerald-600/40 text-emerald-700 dark:text-emerald-400',
    FREE: 'border-sky-600/40 text-sky-700 dark:text-sky-400',
    PAID: 'border-amber-600/40 text-amber-700 dark:text-amber-400',
    FREEMIUM: 'border-violet-600/40 text-violet-700 dark:text-violet-400',
  };

  /** The pricing models in the catalog, in a fixed order. */
  protected readonly pricings = computed(() => {
    const present = new Set((this.marketplace()?.extensions ?? []).map((e) => e.pricing));
    return (['OPEN_SOURCE', 'FREE', 'FREEMIUM', 'PAID'] as Pricing[]).filter((p) => present.has(p));
  });

  /** How to write one, and how the catalog takes proposals (issues with forms). */
  protected readonly guideUrl =
    'https://github.com/linkfast/arcanist/blob/main/docs/developers/extensions.md';
  protected readonly playerGuideUrl =
    'https://github.com/linkfast/arcanist/blob/main/docs/developers/frontends.md';
  protected readonly publishUrl =
    'https://github.com/linkfast/arcanist-extensions#proposing-and-managing-extensions';
  protected readonly proposeUrl =
    'https://github.com/linkfast/arcanist-extensions/issues/new/choose';

  /** The catalog's categories, by how many extensions have them. */
  protected readonly categories = computed(() => {
    const counts = new Map<string, number>();
    for (const entry of this.marketplace()?.extensions ?? [])
      for (const category of entry.categories)
        counts.set(category, (counts.get(category) ?? 0) + 1);
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .map(([c]) => c);
  });
  protected readonly brokenIcons = signal(new Set<string>());

  protected readonly shown = computed(() => {
    const words = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);
    const category = this.category();
    const pricing = this.pricing();
    return (this.marketplace()?.extensions ?? []).filter((entry) => {
      if (category && !entry.categories.includes(category)) return false;
      if (pricing && entry.pricing !== pricing) return false;
      const text = [entry.name, entry.description, entry.author, ...entry.categories]
        .join(' ')
        .toLowerCase();
      return words.every((word) => text.includes(word));
    });
  });

  async ngOnInit(): Promise<void> {
    if (!this.marketplace()) await this.load(false);
  }

  /** Reads the catalog; with refresh, from its source instead of the server's copy of the last hour. */
  async load(refresh: boolean): Promise<void> {
    this.loading.set(true);
    try {
      const variables = { language: this.i18n.language(), refresh };
      if (this.kind() === 'players') {
        const { playerMarketplace: market } = await this.gql.query(
          PlayerMarketplaceQuery,
          variables,
        );
        this.marketplace.set({ ...market, extensions: market.players });
      } else {
        this.marketplace.set(
          (await this.gql.query(MarketplaceQuery, variables)).extensionMarketplace,
        );
      }
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.loading.set(false);
    }
  }

  protected iconFailed(id: string): void {
    this.brokenIcons.update((ids) => new Set(ids).add(id));
  }

  /** Official extensions install with one click; the community's ask first. */
  protected ask(entry: Entry): void {
    if (entry.official) void this.install(entry);
    else this.confirming.set(entry);
  }

  protected confirmed(): void {
    const entry = this.confirming();
    this.confirming.set(null);
    if (entry) void this.install(entry);
  }

  private async install(entry: Entry): Promise<void> {
    this.installing.set(entry.id);
    try {
      if (this.kind() === 'players') {
        await this.gql.mutate(InstallMarketplacePlayerMutation, {
          input: { id: entry.id, makeDefault: false },
        });
        toast.success(this.i18n.t('extensions.marketplace.playerInstalled', { name: entry.name }));
        this.installedPlayer.emit();
        await this.load(false);
        return;
      }
      const result = await this.gql.mutate(InstallMarketplaceExtensionMutation, {
        input: { id: entry.id, enable: true },
      });
      // The page reloads everything, the catalog's buttons included.
      this.installed.emit(result.installMarketplaceExtension);
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.installing.set(null);
    }
  }
}
