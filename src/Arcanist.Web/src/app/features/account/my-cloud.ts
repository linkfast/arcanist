import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  MyUltimateCloudMetricsQuery as MetricsResult,
  MyUltimateCloudStatusFieldsFragment as Status,
  UltimateCloudServicesInput,
} from '../../graphql/generated/graphql';
import {
  CancelMyUltimateCloudLinkMutation,
  MyUltimateCloudMetricsQuery,
  MyUltimateCloudQuery,
  RefreshMyUltimateCloudMutation,
  SetMyUltimateCloudServicesMutation,
  StartMyUltimateCloudLinkMutation,
  UnlinkMyUltimateCloudMutation,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { qrPath } from '../../shared/invitation';

type Metrics = MetricsResult['myUltimateCloudMetrics'];
type Service = keyof UltimateCloudServicesInput;

const SERVICES: Service[] = ['personalPlays', 'albumPlaythroughs', 'starTracks', 'metrics'];

/**
 * The user's own Ultimate Cloud account: linked with a code approved on Ultimate Cloud's site, it brings their music
 * as a personal library, their plays, statistics and StarTracks, and their devices. Hidden while the extension is off.
 */
@Component({
  selector: 'app-my-cloud',
  imports: [
    BrnAlertDialogContent,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  template: `
    @if (status(); as current) {
      @if (current.enabled && current.state !== 'NOT_CONFIGURED') {
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'account.cloud.title' | t }}</h2>
            <p hlmCardDescription>{{ 'account.cloud.description' | t }}</p>
          </div>
          <div hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-4">
            @if (current.errorCode && current.state !== 'PENDING') {
              <p class="text-sm text-destructive">{{ i18n.error(current.errorCode) }}</p>
            }
            @switch (current.state) {
              @case ('UNLINKED') {
                <ul class="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
                  <li>{{ 'account.cloud.notes.library' | t }}</li>
                  <li>{{ 'account.cloud.notes.listening' | t }}</li>
                  <li>{{ 'account.cloud.notes.devices' | t }}</li>
                </ul>
                <button
                  hlmBtn
                  type="button"
                  class="justify-self-start"
                  [disabled]="busy()"
                  (click)="start()"
                >
                  {{ 'account.cloud.link' | t }}
                </button>
              }
              @case ('PENDING') {
                @if (current.pending; as pending) {
                  <div class="grid justify-items-center gap-3 text-center">
                    <p class="text-sm">{{ 'cloud.pending.instructions' | t }}</p>
                    @if (qr(); as code) {
                      <svg
                        class="size-48 rounded-md bg-white p-2"
                        role="img"
                        [attr.aria-label]="'cloud.pending.qr' | t"
                        [attr.viewBox]="'0 0 ' + code.size + ' ' + code.size"
                        shape-rendering="crispEdges"
                      >
                        <path [attr.d]="code.path" fill="#000" />
                      </svg>
                    }
                    <a
                      hlmBtn
                      variant="outline"
                      target="_blank"
                      rel="noopener"
                      [href]="pending.verificationUriComplete ?? pending.verificationUri"
                    >
                      {{ 'cloud.pending.open' | t }}
                    </a>
                    <span class="font-mono text-2xl font-semibold tracking-widest">{{
                      pending.userCode
                    }}</span>
                    <div class="flex items-center gap-2 text-sm text-muted-foreground">
                      <hlm-spinner class="size-4" />
                      <span [title]="pending.expiresAt | dateTime">{{
                        'cloud.pending.waiting' | t: { when: (pending.expiresAt | relativeTime) }
                      }}</span>
                    </div>
                    <button
                      hlmBtn
                      variant="ghost"
                      type="button"
                      [disabled]="busy()"
                      (click)="cancel()"
                    >
                      {{ 'common.cancel' | t }}
                    </button>
                  </div>
                }
              }
              @case ('LINKED') {
                <dl class="grid gap-2 text-sm sm:grid-cols-[9rem_minmax(0,1fr)]">
                  <dt class="text-muted-foreground">{{ 'cloud.account.name' | t }}</dt>
                  <dd>{{ current.account ?? '—' }}</dd>
                  @if (current.plan; as plan) {
                    <dt class="text-muted-foreground">{{ 'cloud.plan.name' | t }}</dt>
                    <dd class="flex flex-wrap items-center gap-2">
                      {{ plan.name }}
                      <span
                        hlmBadge
                        [variant]="current.tier === 'PAID' ? 'default' : 'secondary'"
                        >{{ 'cloud.tier.' + current.tier | t }}</span
                      >
                    </dd>
                    <dt class="text-muted-foreground">{{ 'cloud.plan.checked' | t }}</dt>
                    <dd [title]="plan.checkedAt | dateTime">
                      {{ plan.checkedAt | relativeTime }}
                    </dd>
                  }
                  <dt class="text-muted-foreground">{{ 'account.cloud.library' | t }}</dt>
                  <dd>
                    {{
                      (current.libraryId
                        ? 'account.cloud.libraryReady'
                        : 'account.cloud.libraryNone'
                      ) | t
                    }}
                  </dd>
                </dl>

                <ul class="grid gap-3">
                  @for (service of services; track service) {
                    <li class="flex items-start gap-3 text-sm">
                      <hlm-switch
                        class="mt-0.5"
                        [checked]="current.services[service]"
                        [disabled]="busy()"
                        (checkedChange)="setService(service, $event)"
                        [attr.aria-label]="'account.cloud.services.' + service + '.name' | t"
                      />
                      <span class="grid gap-0.5">
                        <span class="font-medium">{{
                          'account.cloud.services.' + service + '.name' | t
                        }}</span>
                        <span class="text-xs text-muted-foreground">{{
                          'account.cloud.services.' + service + '.help' | t
                        }}</span>
                      </span>
                    </li>
                  }
                </ul>

                @if (metrics(); as stats) {
                  <div class="grid gap-1 rounded-md border p-3 text-sm">
                    <span class="font-medium">{{ 'cloud.metrics.title' | t }}</span>
                    <span class="text-muted-foreground">{{
                      'cloud.metrics.totals'
                        | t: { plays: stats.totalPlays, streams: stats.totalStreams }
                    }}</span>
                    @if (stats.topSongAllTime; as song) {
                      <span>{{
                        'account.cloud.topSong'
                          | t
                            : {
                                name: song.title ?? '—',
                                by: song.artist ?? '—',
                                plays: song.playCount,
                              }
                      }}</span>
                    }
                    @if (stats.topArtistAllTime; as artist) {
                      <span>{{
                        'account.cloud.topArtist'
                          | t: { name: artist.artist, plays: artist.playCount }
                      }}</span>
                    }
                  </div>
                }

                <div class="flex flex-wrap gap-2">
                  <button
                    hlmBtn
                    variant="outline"
                    type="button"
                    [disabled]="busy()"
                    (click)="refresh()"
                  >
                    {{ 'cloud.account.refresh' | t }}
                  </button>
                  @if (current.errorCode === 'cloud_link_expired') {
                    <button hlmBtn type="button" [disabled]="busy()" (click)="start()">
                      {{ 'cloud.account.relink' | t }}
                    </button>
                  }
                  <button
                    hlmBtn
                    variant="destructive"
                    type="button"
                    [disabled]="busy()"
                    (click)="confirmingUnlink.set(true)"
                  >
                    {{ 'cloud.account.unlink' | t }}
                  </button>
                </div>
              }
            }
          </div>
        </section>
      }
    }

    <hlm-alert-dialog
      [state]="confirmingUnlink() ? 'open' : 'closed'"
      (closed)="confirmingUnlink.set(false)"
    >
      <hlm-alert-dialog-content *brnAlertDialogContent="let ctx">
        <hlm-alert-dialog-header>
          <h3 hlmAlertDialogTitle>{{ 'account.cloud.unlinkTitle' | t }}</h3>
          <p hlmAlertDialogDescription>{{ 'account.cloud.unlinkHelp' | t }}</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel type="button" (click)="confirmingUnlink.set(false)">
            {{ 'common.cancel' | t }}
          </button>
          <button hlmAlertDialogAction type="button" (click)="unlink()">
            {{ 'cloud.account.unlink' | t }}
          </button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class MyCloud implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly status = signal<Status | null>(null);
  protected readonly metrics = signal<Metrics | null>(null);
  protected readonly services = SERVICES;
  protected readonly busy = signal(false);
  protected readonly confirmingUnlink = signal(false);

  protected readonly qr = computed(() => {
    const pending = this.status()?.pending;
    return pending ? qrPath(pending.verificationUriComplete ?? pending.verificationUri) : null;
  });

  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    this.destroyRef.onDestroy(() => clearInterval(this.timer));
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      this.show((await this.gql.query(MyUltimateCloudQuery)).myUltimateCloud);
    } catch {
      // Without Ultimate Cloud the card simply does not show.
    }
  }

  private async loadMetrics(): Promise<void> {
    const status = this.status();
    if (!status?.enabled || status.state !== 'LINKED' || !status.plan || !status.services.metrics) {
      this.metrics.set(null);
      return;
    }
    try {
      this.metrics.set((await this.gql.query(MyUltimateCloudMetricsQuery)).myUltimateCloudMetrics);
    } catch {
      this.metrics.set(null);
    }
  }

  /** Follows a pending code until it is approved, and then until the plan and the library are there. */
  private show(status: Status): void {
    const before = this.status();
    this.status.set(status);
    if (
      before?.state !== status.state ||
      !before?.plan !== !status.plan ||
      before?.services.metrics !== status.services.metrics
    )
      void this.loadMetrics();
    const follow =
      status.state === 'PENDING' ||
      (status.state === 'LINKED' && (!status.plan || !status.libraryId) && !status.errorCode);
    if (follow && !this.timer) {
      this.timer = setInterval(() => this.load(), 2000);
    } else if (!follow && this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
    if (before?.state === 'PENDING' && status.state === 'LINKED')
      toast.success(this.i18n.t('cloud.linked'));
  }

  protected async start(): Promise<void> {
    // The personal library is named in the user's language once the account says whose it is; until then, generic.
    const libraryName = this.i18n.t('account.cloud.libraryName');
    await this.run(async () =>
      this.show(
        (await this.gql.mutate(StartMyUltimateCloudLinkMutation, { input: { libraryName } }))
          .startMyUltimateCloudLink,
      ),
    );
  }

  protected async cancel(): Promise<void> {
    await this.run(async () =>
      this.show(
        (await this.gql.mutate(CancelMyUltimateCloudLinkMutation)).cancelMyUltimateCloudLink,
      ),
    );
  }

  protected async refresh(): Promise<void> {
    await this.run(async () =>
      this.show((await this.gql.mutate(RefreshMyUltimateCloudMutation)).refreshMyUltimateCloud),
    );
  }

  protected async unlink(): Promise<void> {
    this.confirmingUnlink.set(false);
    await this.run(async () => {
      this.show((await this.gql.mutate(UnlinkMyUltimateCloudMutation)).unlinkMyUltimateCloud);
      toast.success(this.i18n.t('cloud.unlinked'));
    });
  }

  protected async setService(service: Service, on: boolean): Promise<void> {
    const current = this.status()?.services;
    if (!current) return;
    const input: UltimateCloudServicesInput = {
      personalPlays: current.personalPlays,
      albumPlaythroughs: current.albumPlaythroughs,
      starTracks: current.starTracks,
      metrics: current.metrics,
      [service]: on,
    };
    await this.run(async () =>
      this.show(
        (await this.gql.mutate(SetMyUltimateCloudServicesMutation, { input }))
          .setMyUltimateCloudServices,
      ),
    );
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
