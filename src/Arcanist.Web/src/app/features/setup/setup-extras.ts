import { signal } from '@angular/core';
import { ApiError } from '../../core/api/rest';
import { Gql } from '../../core/graphql/gql.service';
import type { P2PRelayMode, Permission } from '../../graphql/generated/graphql';
import {
  CreateUserMutation,
  InstallExtensionFromUrlMutation,
  InstallFrontendFromUrlMutation,
  SetDefaultFrontendMutation,
  SetExtensionEnabledMutation,
  SetP2PSettingsMutation,
} from '../../graphql/operations';

/** A user the wizard creates besides the administrator. */
export interface DraftUser {
  userName: string;
  displayName: string;
  password: string;
  admin: boolean;
  download: boolean;
  /** Names of the libraries not visible to everyone that the user may see. */
  libraries: string[];
}

/** The built-in extensions the wizard offers (Ultimate Cloud follows the privacy step). */
export const WIZARD_EXTENSIONS = ['dlna', 'subsonic', 'mstream', 'ai', 'server-audio'] as const;
export type WizardExtension = (typeof WIZARD_EXTENSIONS)[number];

/** Those that make the most of Ultimate, the official clients: offered next to them. */
export const ULTIMATE_EXTENSIONS: readonly WizardExtension[] = ['ai', 'server-audio'];

/** Where to learn about Ultimate and Ultimate Cloud; the wizard shows no link while it is empty. */
export const ULTIMATE_URL = '';

export const USER_NAME = /^[\p{L}\p{N}._-]{3,64}$/u;

/**
 * What the wizard's last steps collect: more users, the P2P network, extensions and a web player. They are not part
 * of the setup request: once the server is set up (or, running the wizard again, at the end) they are applied with
 * the administrator's session, through the same operations as the panel.
 */
export class SetupExtrasDraft {
  readonly users = signal<DraftUser[]>([]);

  p2pEnabled = false;
  p2pName = '';
  p2pRelay: P2PRelayMode = 'N0';
  p2pRelayUrl = '';
  /** Limits already set (running the wizard again), kept as they are. */
  p2pLimits: { bandwidthKbps: number | null; maxStreams: number | null } = {
    bandwidthKbps: null,
    maxStreams: null,
  };
  /** The P2P settings when the wizard started: unchanged, nothing is sent. */
  p2pInitial: string | null = null;

  readonly extensions: Record<WizardExtension, boolean> = {
    dlna: false,
    subsonic: false,
    mstream: false,
    // AI enhancers (the AI DJ's analyses) start on; the administrator can switch them off.
    ai: true,
    'server-audio': false,
  };
  extensionsInitial: Record<WizardExtension, boolean> = {
    dlna: false,
    subsonic: false,
    mstream: false,
    ai: false,
    'server-audio': false,
  };
  readonly extensionUrls = signal<string[]>([]);
  playerUrl = '';
  playerDefault = true;

  addUser(): void {
    this.users.update((users) => [
      ...users,
      { userName: '', displayName: '', password: '', admin: false, download: true, libraries: [] },
    ]);
  }

  updateUser(index: number, change: Partial<DraftUser>): void {
    this.users.update((users) => users.map((u, i) => (i === index ? { ...u, ...change } : u)));
  }

  removeUser(index: number): void {
    this.users.update((users) => users.filter((_, i) => i !== index));
  }

  p2pKey(): string {
    return JSON.stringify([
      this.p2pEnabled,
      this.p2pName.trim(),
      this.p2pRelay,
      this.p2pRelayUrl.trim(),
    ]);
  }

  /** What the step about users must fix before going on, as a translation key; null when nothing. */
  usersProblem(adminName: string, taken: string[] = []): string | null {
    const names = new Set([adminName, ...taken].map((n) => n.trim().toLowerCase()));
    for (const user of this.users()) {
      const name = user.userName.trim();
      if (!USER_NAME.test(name)) return 'setup.users.invalidName';
      if (names.has(name.toLowerCase())) return 'setup.users.duplicate';
      names.add(name.toLowerCase());
      if (user.password.length < 8) return 'setup.users.shortPassword';
    }
    return null;
  }
}

/** Something the wizard could not apply, to show at the end. */
export interface ExtraFailure {
  what: string;
  code: string;
}

/**
 * Applies the users, the P2P network, the extensions and the player with the signed-in administrator's session.
 * Each part goes on even when another fails; the failures are returned.
 */
export async function applySetupExtras(
  gql: Gql,
  draft: SetupExtrasDraft,
  libraryIds: Map<string, string>,
): Promise<ExtraFailure[]> {
  const failures: ExtraFailure[] = [];
  const attempt = async (what: string, action: () => Promise<unknown>) => {
    try {
      await action();
    } catch (error) {
      failures.push({ what, code: ApiError.from(error).code });
    }
  };

  for (const user of draft.users()) {
    const permissions: Permission[] = user.download ? ['DOWNLOAD'] : [];
    await attempt(user.userName.trim(), () =>
      gql.mutate(CreateUserMutation, {
        input: {
          userName: user.userName.trim(),
          password: user.password,
          displayName: user.displayName.trim() || null,
          role: user.admin ? 'ADMIN' : 'USER',
          permissions: user.admin ? [] : permissions,
          libraryIds: user.admin
            ? []
            : user.libraries.map((name) => libraryIds.get(name)).filter((id): id is string => !!id),
        },
      }),
    );
  }

  if (draft.p2pKey() !== draft.p2pInitial && (draft.p2pEnabled || draft.p2pInitial !== null)) {
    await attempt('p2p', () =>
      gql.mutate(SetP2PSettingsMutation, {
        input: {
          enabled: draft.p2pEnabled,
          relay: draft.p2pRelay,
          relayUrl: draft.p2pRelay === 'CUSTOM' ? draft.p2pRelayUrl.trim() : null,
          name: draft.p2pName.trim() || null,
          bandwidthKbps: draft.p2pLimits.bandwidthKbps,
          maxStreams: draft.p2pLimits.maxStreams,
        },
      }),
    );
  }

  for (const id of WIZARD_EXTENSIONS) {
    if (draft.extensions[id] !== draft.extensionsInitial[id])
      await attempt(id, () =>
        gql.mutate(SetExtensionEnabledMutation, { id, enabled: draft.extensions[id] }),
      );
  }

  for (const url of draft
    .extensionUrls()
    .map((u) => u.trim())
    .filter(Boolean)) {
    await attempt(url, async () => {
      const installed = (await gql.mutate(InstallExtensionFromUrlMutation, { url }))
        .installExtensionFromUrl;
      await gql.mutate(SetExtensionEnabledMutation, { id: installed.id, enabled: true });
    });
  }

  const player = draft.playerUrl.trim();
  if (player) {
    await attempt(player, async () => {
      const installed = (await gql.mutate(InstallFrontendFromUrlMutation, { url: player }))
        .installFrontendFromUrl;
      if (draft.playerDefault) await gql.mutate(SetDefaultFrontendMutation, { id: installed.id });
    });
  }

  return failures;
}
