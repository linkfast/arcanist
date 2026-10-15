import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { EnabledExtensions } from '../../core/extensions/enabled-extensions';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  Capability,
  UltimateCloudCollection,
  UltimateCloudMetricsQuery as MetricsResult,
  UltimateCloudServicesInput,
  UltimateCloudStatusFieldsFragment as Status,
} from '../../graphql/generated/graphql';
import {
  AddUltimateCloudLibraryMutation,
  CancelUltimateCloudLinkMutation,
  RefreshUltimateCloudMutation,
  SetExtensionEnabledMutation,
  SetUltimateCloudServicesMutation,
  StartUltimateCloudLinkMutation,
  UltimateCloudMetricsQuery,
  UltimateCloudQuery,
  UnlinkUltimateCloudMutation,
} from '../../graphql/operations';
import { BytesPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { qrPath } from '../../shared/invitation';

type Metrics = MetricsResult['ultimateCloudMetrics'];
type Service = keyof UltimateCloudServicesInput;

const SERVICES: Service[] = [
  'personalPlays',
  'sharedPlays',
  'albumPlaythroughs',
  'starTracks',
  'metrics',
];
const PERIODS = ['ThisMonth', 'ThisYear', 'AllTime'] as const;

const ALL_CAPABILITIES: Capability[] = [
  'ULTIMATE_CLOUD_SOURCE',
  'CLOUD_SYNC',
  'THIRD_PARTY_STORAGE',
  'CLOUD_CONNECT',
];

/**
 * Linking the server to an Ultimate Cloud account with a code approved on Ultimate Cloud's site, its plan and the
 * features it enables. Administrators only.
 */
@Component({
  selector: 'app-cloud-page',
  imports: [
    RouterLink,
    BrnAlertDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmProgressImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    BytesPipe,
    DateTimePipe,
    RelativeTimePipe,
  ],
  templateUrl: './cloud.page.html',
})
export class CloudPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);
  private readonly destroyRef = inject(DestroyRef);
  private readonly enabledExtensions = inject(EnabledExtensions);

  protected readonly status = signal<Status | null>(null);
  protected readonly metrics = signal<Metrics | null>(null);
  protected readonly services = SERVICES;
  protected readonly periods = PERIODS;
  protected readonly busy = signal(false);
  protected readonly confirmingUnlink = signal(false);
  protected readonly capabilities = ALL_CAPABILITIES;
  /** Cloud Drop is no library to add (it comes in through a drop folder): it is listed only when one already exists. */
  protected collections(): UltimateCloudCollection[] {
    return this.libraryOf('CLOUD_DROP') ? ['LIBRARY', 'CLOUD_DROP'] : ['LIBRARY'];
  }

  protected readonly qr = computed(() => {
    const pending = this.status()?.pending;
    return pending ? qrPath(pending.verificationUriComplete ?? pending.verificationUri) : null;
  });

  protected readonly usedPercent = computed(() => {
    const plan = this.status()?.plan;
    return plan && plan.limitBytes > 0
      ? Math.min(100, Math.round((plan.usedBytes * 100) / plan.limitBytes))
      : null;
  });

  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    this.destroyRef.onDestroy(() => clearInterval(this.timer));
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected libraryOf(collection: UltimateCloudCollection) {
    return this.status()?.libraries.find((l) => l.collection === collection) ?? null;
  }

  protected async addLibrary(collection: UltimateCloudCollection): Promise<void> {
    await this.run(async () => {
      this.show(
        (
          await this.gql.mutate(AddUltimateCloudLibraryMutation, {
            input: { collection, name: this.defaultName(collection) },
          })
        ).addUltimateCloudLibrary,
      );
      toast.success(this.i18n.t('cloud.libraries.added'));
    });
  }

  /** "Ultimate Cloud de Ana" / "Ana's Ultimate Cloud", in the administrator's language; null lets the server name it. */
  private defaultName(collection: UltimateCloudCollection): string | null {
    const owner = this.status()?.ownerFirstName;
    return owner ? this.i18n.t('cloud.libraries.' + collection + '.libraryName', { owner }) : null;
  }

  protected enabled(capability: Capability): boolean {
    return this.status()?.capabilities.includes(capability) ?? false;
  }

  private async load(): Promise<void> {
    try {
      this.show((await this.gql.query(UltimateCloudQuery)).ultimateCloud);
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    }
  }

  /** The account's statistics, when linked and shown; quietly absent when Ultimate Cloud cannot answer. */
  private async loadMetrics(): Promise<void> {
    const status = this.status();
    if (!status?.enabled || status.state !== 'LINKED' || !status.plan || !status.services.metrics) {
      this.metrics.set(null);
      return;
    }
    try {
      this.metrics.set((await this.gql.query(UltimateCloudMetricsQuery)).ultimateCloudMetrics);
    } catch {
      this.metrics.set(null);
    }
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(SetExtensionEnabledMutation, { id: 'ultimate-cloud', enabled });
      await this.enabledExtensions.refresh();
      await this.load();
    });
  }

  protected async setService(service: Service, on: boolean): Promise<void> {
    const current = this.status()?.services;
    if (!current) return;
    const input: UltimateCloudServicesInput = {
      personalPlays: current.personalPlays,
      sharedPlays: current.sharedPlays,
      albumPlaythroughs: current.albumPlaythroughs,
      starTracks: current.starTracks,
      metrics: current.metrics,
      [service]: on,
    };
    await this.run(async () =>
      this.show(
        (await this.gql.mutate(SetUltimateCloudServicesMutation, { input }))
          .setUltimateCloudServices,
      ),
    );
  }

  /** The top of a kind (song, artist, album, genre) for a period, as one line. */
  protected top(
    kind: 'Song' | 'Artist' | 'Album' | 'Genre',
    period: (typeof PERIODS)[number],
  ): string | null {
    const metrics = this.metrics() as Record<string, unknown> | null;
    const value = metrics?.['top' + kind + period] as
      { title?: string; artist?: string; genre?: string; playCount: number } | null | undefined;
    if (!value) return null;
    const name = kind === 'Artist' ? value.artist : kind === 'Genre' ? value.genre : value.title;
    const by = kind === 'Song' || kind === 'Album' ? value.artist : null;
    return by
      ? this.i18n.t('cloud.metrics.entryBy', { name: name ?? '—', by, plays: value.playCount })
      : this.i18n.t('cloud.metrics.entry', { name: name ?? '—', plays: value.playCount });
  }

  /**
   * Follows a pending code until it is approved, refused or expires, and then until the plan is read (the server
   * reads it right after the approval).
   */
  private show(status: Status): void {
    const wasPending = this.status()?.state === 'PENDING';
    const before = this.status();
    this.status.set(status);
    if (
      before?.state !== status.state ||
      !before?.plan !== !status.plan ||
      before?.services.metrics !== status.services.metrics ||
      before?.enabled !== status.enabled
    )
      void this.loadMetrics();
    const follow =
      status.state === 'PENDING' ||
      (status.state === 'LINKED' && !status.plan && !status.errorCode);
    if (follow && !this.timer) {
      this.timer = setInterval(() => this.load(), 2000);
    } else if (!follow && this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
    if (wasPending && status.state === 'LINKED') toast.success(this.i18n.t('cloud.linked'));
  }

  protected async start(): Promise<void> {
    await this.run(async () =>
      this.show((await this.gql.mutate(StartUltimateCloudLinkMutation)).startUltimateCloudLink),
    );
  }

  protected async cancel(): Promise<void> {
    await this.run(async () =>
      this.show((await this.gql.mutate(CancelUltimateCloudLinkMutation)).cancelUltimateCloudLink),
    );
  }

  protected async refresh(): Promise<void> {
    await this.run(async () =>
      this.show((await this.gql.mutate(RefreshUltimateCloudMutation)).refreshUltimateCloud),
    );
  }

  protected async unlink(): Promise<void> {
    this.confirmingUnlink.set(false);
    await this.run(async () => {
      this.show((await this.gql.mutate(UnlinkUltimateCloudMutation)).unlinkUltimateCloud);
      toast.success(this.i18n.t('cloud.unlinked'));
    });
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
