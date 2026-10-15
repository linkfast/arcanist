import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type { NetworkPeerFieldsFragment as Peer } from '../../graphql/generated/graphql';
import {
  CreateDeviceInvitationMutation,
  MyDevicesQuery,
  RevokeNetworkPeerMutation,
} from '../../graphql/operations';
import { DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { Invitation } from '../../shared/invitation';

/**
 * The user's devices paired through the P2P network: the app scans a QR code and then reaches this server from
 * anywhere, without opening ports. Each user pairs and revokes their own.
 */
@Component({
  selector: 'app-my-devices',
  imports: [
    FormsModule,
    BrnDialogContent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    TranslatePipe,
    DateTimePipe,
    RelativeTimePipe,
    Invitation,
  ],
  template: `
    <section hlmCard>
      <div hlmCardHeader>
        <h2 hlmCardTitle>{{ 'devices.title' | t }}</h2>
        <p hlmCardDescription>{{ 'devices.description' | t }}</p>
      </div>
      <div hlmCardContent class="grid grid-cols-[minmax(0,1fr)] gap-4">
        <form class="flex flex-wrap items-end gap-2" (ngSubmit)="create()">
          <hlm-field class="min-w-0 flex-1">
            <label hlmFieldLabel for="device-name">{{ 'devices.name' | t }}</label>
            <input
              hlmInput
              id="device-name"
              name="deviceName"
              [placeholder]="'devices.namePlaceholder' | t"
              [(ngModel)]="name"
            />
          </hlm-field>
          <button hlmBtn type="submit" [disabled]="busy()">{{ 'devices.pair' | t }}</button>
        </form>
        @if (error()) {
          <p class="text-sm text-destructive">{{ error() }}</p>
        }
        <ul class="grid grid-cols-[minmax(0,1fr)] gap-2">
          @for (device of devices(); track device.id) {
            <li class="flex items-center justify-between gap-3 rounded-md border p-3 text-sm">
              <div class="grid min-w-0 gap-0.5">
                <p class="flex flex-wrap items-center gap-2">
                  <span class="font-medium">{{ device.name }}</span>
                  @if (device.revokedAt) {
                    <span hlmBadge variant="destructive">{{ 'network.revoked' | t }}</span>
                  } @else if (device.online) {
                    <span hlmBadge variant="secondary">{{ 'network.online' | t }}</span>
                  }
                </p>
                <p class="text-xs text-muted-foreground" [title]="device.createdAt | dateTime">
                  {{ 'devices.paired' | t: { when: (device.createdAt | relativeTime) } }}
                  @if (device.lastSeenAt) {
                    · {{ 'network.lastSeen' | t: { when: (device.lastSeenAt | relativeTime) } }}
                  }
                </p>
              </div>
              @if (!device.revokedAt) {
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  [disabled]="busy()"
                  (click)="revoke(device)"
                >
                  {{ 'network.revoke' | t }}
                </button>
              }
            </li>
          } @empty {
            <li class="text-sm text-muted-foreground">{{ 'devices.none' | t }}</li>
          }
        </ul>
      </div>
    </section>

    <hlm-dialog [state]="invitation() ? 'open' : 'closed'" (closed)="closed()">
      <hlm-dialog-content *brnDialogContent="let ctx" class="w-[min(28rem,calc(100vw-2rem))]">
        @if (invitation(); as created) {
          <hlm-dialog-header>
            <h3 hlmDialogTitle>{{ 'devices.pair' | t }}</h3>
            <p hlmDialogDescription>{{ 'invitation.deviceHelp' | t }}</p>
          </hlm-dialog-header>
          <app-invitation [link]="created.link" [expiresAt]="created.expiresAt" />
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MyDevices implements OnInit {
  private readonly gql = inject(Gql);
  private readonly i18n = inject(I18n);

  protected name = '';
  protected readonly devices = signal<Peer[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly invitation = signal<{ link: string; expiresAt: string } | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      this.devices.set((await this.gql.query(MyDevicesQuery)).networkPeers);
    } catch {
      this.devices.set([]);
    }
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.gql.mutate(CreateDeviceInvitationMutation, {
        input: { name: this.name.trim() || null },
      });
      this.invitation.set(result.createDeviceInvitation);
      this.name = '';
    } catch (error) {
      this.error.set(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }

  /** The dialog closed: the device may have paired meanwhile. */
  protected async closed(): Promise<void> {
    this.invitation.set(null);
    await this.load();
  }

  protected async revoke(device: Peer): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.gql.mutate(RevokeNetworkPeerMutation, { id: device.id });
      this.devices.update((list) =>
        list.map((d) => (d.id === device.id ? result.revokeNetworkPeer : d)),
      );
      toast.success(this.i18n.t('devices.revoked'));
    } catch (error) {
      toast.error(this.i18n.error(ApiError.from(error).code));
    } finally {
      this.busy.set(false);
    }
  }
}
