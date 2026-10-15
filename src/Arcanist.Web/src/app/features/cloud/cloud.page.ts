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
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  Capability,
  UltimateCloudCollection,
  UltimateCloudStatusFieldsFragment as Status,
} from '../../graphql/generated/graphql';
import {
  AddUltimateCloudLibraryMutation,
  CancelUltimateCloudLinkMutation,
  RefreshUltimateCloudMutation,
  StartUltimateCloudLinkMutation,
  UltimateCloudQuery,
  UnlinkUltimateCloudMutation,
} from '../../graphql/operations';
import { BytesPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { qrPath } from '../../shared/invitation';

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

  protected readonly status = signal<Status | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmingUnlink = signal(false);
  protected readonly capabilities = ALL_CAPABILITIES;
  protected readonly collections: UltimateCloudCollection[] = ['LIBRARY', 'CLOUD_DROP'];

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

  /**
   * Follows a pending code until it is approved, refused or expires, and then until the plan is read (the server
   * reads it right after the approval).
   */
  private show(status: Status): void {
    const wasPending = this.status()?.state === 'PENDING';
    this.status.set(status);
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
