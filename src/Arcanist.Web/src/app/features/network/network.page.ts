import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus, lucideShieldOff } from '@ng-icons/lucide';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import { I18n, TranslatePipe } from '../../core/i18n/i18n';
import type {
  NetworkPeerFieldsFragment as Peer,
  NetworkQuery as NetworkResult,
  P2PRelayMode,
  P2PStatusFieldsFragment as Status,
  ReceivedShareFieldsFragment as Share,
} from '../../graphql/generated/graphql';
import {
  AcceptShareMutation,
  CopyReceivedShareMutation,
  CreateDeviceInvitationMutation,
  CreateShareInvitationMutation,
  NetworkQuery,
  RevokeNetworkPeerMutation,
  SetP2PSettingsMutation,
  SetReceivedSharePlaysMutation,
  UpdateNetworkPeerMutation,
} from '../../graphql/operations';
import { RemoteLibrariesCard } from './remote-libraries-card';
import { BytesPipe, CountPipe, DateTimePipe, RelativeTimePipe } from '../../shared/format';
import { Invitation } from '../../shared/invitation';
import { PeerRules, RulesForm, rulesForm, rulesInput } from './peer-rules';

type Activity = NetworkResult['p2pActivity'][number];
type Library = NetworkResult['libraries'][number];

/** How often the page refreshes while open: traffic and connections change by themselves. */
const REFRESH_MS = 5000;

interface SettingsForm {
  enabled: boolean;
  name: string;
  relay: P2PRelayMode;
  relayUrl: string;
  bandwidthKbps: string;
  maxStreams: string;
}

interface ShareForm {
  name: string;
  libraryIds: string[];
  rules: RulesForm;
  expiresOn: string;
}

interface EditForm {
  peer: Peer;
  name: string;
  libraryIds: string[];
  rules: RulesForm;
  expiresOn: string;
}

/**
 * The P2P network: its settings and status, the servers this one shares libraries with, every paired device, the
 * shares received from other servers and the activity log. Administrators only.
 */
@Component({
  selector: 'app-network-page',
  imports: [
    RemoteLibrariesCard,
    FormsModule,
    NgTemplateOutlet,
    NgIcon,
    BrnDialogContent,
    BrnAlertDialogContent,
    HlmAlertImports,
    HlmAlertDialogImports,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
    HlmSwitchImports,
    TranslatePipe,
    BytesPipe,
    CountPipe,
    DateTimePipe,
    RelativeTimePipe,
    Invitation,
    PeerRules,
  ],
  providers: [provideIcons({ lucidePencil, lucidePlus, lucideShieldOff })],
  templateUrl: './network.page.html',
})
export class NetworkPage implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly gql = inject(Gql);

  protected readonly relays: P2PRelayMode[] = ['N0', 'CUSTOM', 'DIRECT'];

  protected readonly status = signal<Status | null>(null);
  protected readonly peers = signal<Peer[]>([]);
  protected readonly shares = signal<Share[]>([]);
  protected readonly activity = signal<Activity[]>([]);
  protected readonly libraries = signal<Library[]>([]);
  protected readonly busy = signal(false);
  protected settings: SettingsForm | null = null;

  protected readonly servers = computed(() => this.peers().filter((p) => p.kind === 'SERVER'));
  protected readonly devices = computed(() => this.peers().filter((p) => p.kind === 'DEVICE'));
  /** Folders of this server's own libraries, where a share can be copied. */
  protected readonly copyTargets = computed(() =>
    this.libraries().flatMap((library) =>
      (library.sources ?? [])
        .filter((s) => s.kind === 'LOCAL')
        .map((s) => ({ id: s.id, label: `${library.name} — ${s.location}` })),
    ),
  );

  /** Libraries that can be shared: not ones received from another server. */
  protected readonly shareable = computed(() =>
    this.libraries().filter((l) => (l.sources ?? []).every((s) => s.kind === 'LOCAL')),
  );

  // Dialogs
  protected readonly sharing = signal<ShareForm | null>(null);
  protected readonly editing = signal<EditForm | null>(null);
  protected readonly pairing = signal<{ name: string } | null>(null);
  protected readonly accepting = signal<{ link: string } | null>(null);
  protected readonly copying = signal<{ share: Share; sourceId: string } | null>(null);
  protected readonly revoking = signal<Peer | null>(null);
  protected readonly invitation = signal<{ link: string; expiresAt: string; qr: boolean } | null>(
    null,
  );

  constructor() {
    const timer = setInterval(() => void this.load(false), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.load(true);
  }

  private async load(resetForm: boolean): Promise<void> {
    try {
      const result = await this.gql.query(NetworkQuery);
      this.status.set(result.p2pStatus);
      this.peers.set(result.networkPeers);
      this.shares.set(result.receivedShares);
      this.activity.set(result.p2pActivity);
      this.libraries.set(result.libraries);
      if (resetForm || !this.settings) this.settings = this.formFor(result.p2pStatus);
    } catch {
      // Retried on the next tick.
    }
  }

  private formFor(status: Status): SettingsForm {
    return {
      enabled: status.enabled,
      name: status.customName ?? '',
      relay: status.relay,
      relayUrl: status.relayUrl ?? '',
      bandwidthKbps: status.bandwidthKbps?.toString() ?? '',
      maxStreams: status.maxStreams?.toString() ?? '',
    };
  }

  protected libraryName(id: string): string {
    return this.libraries().find((l) => l.id === id)?.name ?? '';
  }

  protected peerLibraries(peer: Peer): string {
    return peer.libraryIds.map((id) => this.libraryName(id)).join(', ');
  }

  protected rulesSummary(rules: Peer['rules']): string {
    const parts = [
      this.i18n.t(rules.allowDownload ? 'network.rules.downloads' : 'network.rules.streamOnly'),
    ];
    if (rules.maxBitRate) parts.push(this.i18n.t('network.rules.upTo', { rate: rules.maxBitRate }));
    if (rules.bandwidthKbps)
      parts.push(this.i18n.t('network.rules.bandwidthOf', { rate: rules.bandwidthKbps }));
    if (rules.maxStreams)
      parts.push(this.i18n.t('network.rules.streamsAtOnce', { count: rules.maxStreams }));
    if (rules.countPlays) parts.push(this.i18n.t('network.rules.countsPlays'));
    return parts.join(' · ');
  }

  protected async setSharePlays(share: { id: string }, sharePlays: boolean): Promise<void> {
    await this.run(async () => {
      await this.gql.mutate(SetReceivedSharePlaysMutation, {
        input: { shareId: share.id, sharePlays },
      });
    });
  }

  protected async saveSettings(): Promise<void> {
    const form = this.settings;
    if (!form) return;
    await this.run(async () => {
      const status = await this.gql.mutate(SetP2PSettingsMutation, {
        input: {
          enabled: form.enabled,
          name: form.name.trim() || null,
          relay: form.relay,
          relayUrl: form.relay === 'CUSTOM' ? form.relayUrl.trim() : null,
          bandwidthKbps: limit(form.bandwidthKbps),
          maxStreams: limit(form.maxStreams),
        },
      });
      this.status.set(status.setP2pSettings);
      this.settings = this.formFor(status.setP2pSettings);
      toast.success(this.i18n.t('network.saved'));
      // The node takes a moment to start or stop.
      setTimeout(() => void this.load(false), 1500);
    });
  }

  // Sharing with another server

  protected openShare(): void {
    this.sharing.set({ name: '', libraryIds: [], rules: rulesForm(), expiresOn: '' });
  }

  protected toggle(list: string[], id: string, on: boolean): string[] {
    return on ? [...list.filter((l) => l !== id), id] : list.filter((l) => l !== id);
  }

  protected async createShare(): Promise<void> {
    const form = this.sharing();
    if (!form) return;
    await this.run(async () => {
      const result = await this.gql.mutate(CreateShareInvitationMutation, {
        input: {
          name: form.name.trim(),
          libraryIds: form.libraryIds,
          rules: rulesInput(form.rules),
          accessExpiresAt: endOfDay(form.expiresOn),
        },
      });
      this.sharing.set(null);
      this.invitation.set({ ...result.createShareInvitation, qr: false });
    });
  }

  protected openEdit(peer: Peer): void {
    this.editing.set({
      peer,
      name: peer.name,
      libraryIds: [...peer.libraryIds],
      rules: rulesForm(peer.rules),
      expiresOn: peer.expiresAt?.slice(0, 10) ?? '',
    });
  }

  protected async saveEdit(): Promise<void> {
    const form = this.editing();
    if (!form) return;
    await this.run(async () => {
      const result = await this.gql.mutate(UpdateNetworkPeerMutation, {
        input: {
          id: form.peer.id,
          name: form.name.trim(),
          rules: rulesInput(form.rules),
          libraryIds: form.peer.kind === 'SERVER' ? form.libraryIds : null,
          expiresAt: endOfDay(form.expiresOn),
        },
      });
      this.replace(result.updateNetworkPeer);
      this.editing.set(null);
      toast.success(this.i18n.t('network.saved'));
    });
  }

  protected async revoke(): Promise<void> {
    const peer = this.revoking();
    if (!peer) return;
    await this.run(async () => {
      const result = await this.gql.mutate(RevokeNetworkPeerMutation, { id: peer.id });
      this.replace(result.revokeNetworkPeer);
      this.revoking.set(null);
    });
  }

  // Devices

  protected async createDeviceInvitation(): Promise<void> {
    const form = this.pairing();
    if (!form) return;
    await this.run(async () => {
      const result = await this.gql.mutate(CreateDeviceInvitationMutation, {
        input: { name: form.name.trim() || null },
      });
      this.pairing.set(null);
      this.invitation.set({ ...result.createDeviceInvitation, qr: true });
    });
  }

  // Shares received

  protected async acceptShare(): Promise<void> {
    const form = this.accepting();
    if (!form) return;
    await this.run(async () => {
      await this.gql.mutate(AcceptShareMutation, { link: form.link.trim() });
      this.accepting.set(null);
      toast.success(this.i18n.t('network.received.accepted'));
      await this.load(false);
    });
  }

  protected openCopy(share: Share): void {
    this.copying.set({ share, sourceId: this.copyTargets()[0]?.id ?? '' });
  }

  protected async copyShare(): Promise<void> {
    const form = this.copying();
    if (!form) return;
    await this.run(async () => {
      const result = await this.gql.mutate(CopyReceivedShareMutation, {
        input: { shareId: form.share.id, targetSourceId: form.sourceId },
      });
      this.shares.update((shares) =>
        shares.map((s) => (s.id === form.share.id ? result.copyReceivedShare : s)),
      );
      this.copying.set(null);
      toast.success(this.i18n.t('network.received.copyStarted'));
    });
  }

  private replace(peer: Peer): void {
    this.peers.update((peers) => peers.map((p) => (p.id === peer.id ? peer : p)));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      const apiError = ApiError.from(error);
      toast.error(this.i18n.error(Object.values(apiError.fields)[0]?.[0] ?? apiError.code));
    } finally {
      this.busy.set(false);
    }
  }
}

function limit(text: string): number | null {
  const value = Number(text.trim());
  return text.trim() && Number.isInteger(value) && value > 0 ? value : null;
}

/** The end of a chosen day, in the browser's time zone, as the API takes dates. */
function endOfDay(date: string): string | null {
  return date ? new Date(`${date}T23:59:59`).toISOString() : null;
}
