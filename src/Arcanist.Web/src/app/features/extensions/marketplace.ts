import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBadgeCheck, lucidePuzzle, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
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
} from '../../graphql/operations';
import { BytesPipe } from '../../shared/format';

type Marketplace = ExtensionMarketplaceQuery['extensionMarketplace'];
type Entry = Marketplace['extensions'][number];

/**
 * The extension catalog (github.com/linkfast/arcanist-extensions): what there is, what is installed here, and
 * installing or updating with one click. Community extensions ask for confirmation first, as any package.
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
  providers: [provideIcons({ lucideBadgeCheck, lucidePuzzle, lucideRefreshCw, lucideSearch })],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'extensions.marketplace.title' | t }}</h2>
        <p hlmCardDescription>{{ 'extensions.marketplace.description' | t }}</p>
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
            @if (market.extensions.length > 6) {
              <div class="relative">
                <ng-icon
                  name="lucideSearch"
                  class="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-muted-foreground"
                />
                <input
                  hlmInput
                  class="w-full pl-8"
                  type="search"
                  [placeholder]="'extensions.marketplace.search' | t"
                  [ngModel]="query()"
                  (ngModelChange)="query.set($event)"
                />
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
                        <ng-icon name="lucidePuzzle" size="20" />
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
                  {{ 'extensions.marketplace.none' | t }}
                </li>
              }
            </ul>
          }
        } @else {
          <hlm-spinner class="size-5" />
        }
      </div>
    </section>

    <hlm-alert-dialog [state]="confirming() ? 'open' : 'closed'" (closed)="confirming.set(null)">
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ 'extensions.install.confirmTitle' | t }}</h3>
          <p hlmAlertDialogDescription>{{ 'extensions.marketplace.communityTrust' | t }}</p>
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
  /** An extension was installed (or updated) from the catalog. */
  readonly installed = output<Extension>();

  protected readonly marketplace = signal<Marketplace | null>(null);
  protected readonly loading = signal(false);
  protected readonly installing = signal<string | null>(null);
  protected readonly confirming = signal<Entry | null>(null);
  protected readonly query = signal('');
  protected readonly brokenIcons = signal(new Set<string>());

  protected readonly shown = computed(() => {
    const words = this.query().trim().toLowerCase().split(/\s+/).filter(Boolean);
    return (this.marketplace()?.extensions ?? []).filter((entry) => {
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
      const result = await this.gql.query(MarketplaceQuery, {
        language: this.i18n.language(),
        refresh,
      });
      this.marketplace.set(result.extensionMarketplace);
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
