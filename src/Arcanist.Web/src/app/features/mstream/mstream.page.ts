import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MstreamFeature,
  MstreamStatusFieldsFragment as Status,
  RatingMode,
} from '../../graphql/generated/graphql';
import { MstreamQuery, SetMstreamSettingsMutation } from '../../graphql/operations';

/** The apps' own functions Arcanist offers, in the order the page lists them. */
const Features: MstreamFeature[] = ['WAVEFORM', 'STATS', 'SIMILAR', 'SCAN_STATUS'];

/** mStream functions Arcanist has no equivalent for: shown so administrators know, never offered to the apps. */
const Unavailable = [
  'federation',
  'torrents',
  'backups',
  'discoveryCatalog',
  'youtube',
  'discoveryPlugins',
];

/** How the mStream extension behaves with the mStream apps. Shown in the menu while the extension is on. */
@Component({
  selector: 'app-mstream-page',
  imports: [
    FormsModule,
    RouterLink,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmNativeSelectImports,
    HlmSwitchImports,
    TranslatePipe,
  ],
  template: `
    <header class="mb-6">
      <h1 class="text-2xl font-semibold">{{ 'mstreamPage.title' | t }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'mstreamPage.subtitle' | t }}</p>
    </header>

    @if (status(); as current) {
      <div class="grid gap-6">
        @if (!current.enabled) {
          <p class="text-sm text-muted-foreground">
            {{ 'mstreamPage.off' | t }}
            <a routerLink="/extensions" class="underline">{{ 'nav.extensions' | t }}</a>
          </p>
        }

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstreamPage.ratings.title' | t }}</h2>
            <p hlmCardDescription>{{ 'mstreamPage.ratings.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-3">
            <hlm-field>
              <label hlmFieldLabel for="starThreshold">{{ 'mstreamPage.ratings.threshold' | t }}</label>
              <select
                hlmNativeSelect
                id="starThreshold"
                name="starThreshold"
                class="w-56"
                [(ngModel)]="starThreshold"
              >
                @for (stars of [1, 2, 3, 4, 5]; track stars) {
                  <option hlmNativeSelectOption [ngValue]="stars">
                    {{ 'mstreamPage.ratings.from' | t: { stars } }}
                  </option>
                }
              </select>
              <p hlmFieldDescription>
                {{
                  (starThreshold === 1
                    ? 'mstreamPage.ratings.anyHelp'
                    : 'mstreamPage.ratings.thresholdHelp'
                  ) | t: { stars: starThreshold }
                }}
              </p>
            </hlm-field>
            <p class="text-xs text-muted-foreground">
              {{
                (ratingMode() === 'STAR'
                  ? 'mstreamPage.ratings.singleStarNow'
                  : 'mstreamPage.ratings.fiveStarsNow'
                ) | t
              }}
            </p>
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstreamPage.signIn.title' | t }}</h2>
            <p hlmCardDescription>{{ 'mstreamPage.signIn.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-3">
            <label class="flex items-center gap-2 text-sm">
              <hlm-switch [checked]="passwordSignIn" (checkedChange)="passwordSignIn = $event" />
              {{ 'mstreamPage.signIn.password' | t }}
            </label>
            @if (passwordSignIn) {
              <p
                class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-300"
              >
                {{ 'mstreamPage.signIn.warning' | t }}
              </p>
            }
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstreamPage.features.title' | t }}</h2>
            <p hlmCardDescription>{{ 'mstreamPage.features.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-3">
            @for (feature of features; track feature) {
              <label class="flex items-start gap-3 text-sm">
                <hlm-switch
                  class="mt-0.5"
                  [checked]="enabled().includes(feature)"
                  (checkedChange)="toggle(feature, $event)"
                />
                <span class="grid gap-0.5">
                  <span class="font-medium">{{ 'mstreamPage.features.' + feature + '.name' | t }}</span>
                  <span class="text-xs text-muted-foreground">{{
                    'mstreamPage.features.' + feature + '.help' | t
                  }}</span>
                </span>
              </label>
            }
          </div>
        </section>

        <div class="flex justify-end">
          <button hlmBtn type="button" [disabled]="busy()" (click)="save()">
            {{ 'common.save' | t }}
          </button>
        </div>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'mstreamPage.unavailable.title' | t }}</h2>
            <p hlmCardDescription>{{ 'mstreamPage.unavailable.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid gap-3">
            @for (feature of unavailable; track feature) {
              <div class="grid gap-0.5 text-sm text-muted-foreground opacity-70">
                <span class="flex items-center gap-2 font-medium">
                  {{ 'mstreamPage.unavailable.' + feature + '.name' | t }}
                  <span hlmBadge variant="outline">{{ 'mstreamPage.unavailable.badge' | t }}</span>
                </span>
                <span class="text-xs">{{ 'mstreamPage.unavailable.' + feature + '.help' | t }}</span>
              </div>
            }
          </div>
        </section>
      </div>
    }
  `,
})
export class MstreamPage implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected readonly features = Features;
  protected readonly unavailable = Unavailable;
  protected readonly status = signal<Status | null>(null);
  protected readonly ratingMode = signal<RatingMode | null>(null);
  protected readonly enabled = signal<MstreamFeature[]>([]);
  protected readonly busy = signal(false);
  protected starThreshold = 1;
  protected passwordSignIn = false;

  async ngOnInit(): Promise<void> {
    try {
      const result = await this.gql.query(MstreamQuery);
      this.ratingMode.set(result.serverInfo.ratingMode);
      this.apply(result.mstream);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  protected toggle(feature: MstreamFeature, on: boolean): void {
    this.enabled.update((features) =>
      on ? [...features, feature] : features.filter((f) => f !== feature),
    );
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(SetMstreamSettingsMutation, {
        input: {
          starThreshold: Number(this.starThreshold),
          passwordSignIn: this.passwordSignIn,
          features: this.enabled(),
        },
      });
      this.apply(result.setMstreamSettings);
      toast.success(this.i18n.t('mstreamPage.saved'));
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }

  private apply(status: Status): void {
    this.status.set(status);
    this.starThreshold = status.starThreshold;
    this.passwordSignIn = status.passwordSignIn;
    this.enabled.set([...status.features]);
  }
}
