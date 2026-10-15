import { HttpErrorResponse } from '@angular/common/http';

// REST types used by the panel: setup and authentication only exist in REST. Everything else goes through
// GraphQL with generated types (src/app/graphql/generated).

export type UserRole = 'USER' | 'ADMIN';
export type Permission =
  | 'MANAGE_LIBRARIES'
  | 'MANAGE_USERS'
  | 'MANAGE_SETTINGS'
  | 'RUN_SCANS'
  | 'DOWNLOAD'
  | 'VIEW_ALL_STATS'
  | 'UPLOAD';
export type DatabaseProvider = 'SQLITE' | 'POSTGRES';

export interface UserAccount {
  id: string;
  userName: string;
  displayName: string | null;
  role: UserRole;
  permissions: Permission[];
  isDisabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  libraryIds: string[];
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string | null;
  sessionExpiresAt: string;
  user: UserAccount;
}

export interface SetupStatus {
  completed: boolean;
  requiresCode: boolean;
  database: {
    configured: boolean;
    lockedByEnvironment: boolean;
    provider: DatabaseProvider | null;
    defaultSqlitePath: string;
  };
  dataDirectory: string;
}

/** How users rate tracks: one to five stars, or a single star (kept as five). */
export type RatingMode = 'STARS' | 'STAR';

export interface SetupRequest {
  database: { provider: DatabaseProvider; connectionString: string | null } | null;
  admin: { userName: string; password: string; displayName: string | null };
  libraries: { name: string; folders: string[]; visibleToAll: boolean }[];
  shareAnonymousStatistics: boolean;
  language: string;
  ffmpegPath: string | null;
  onlineServices: OnlineServicesChoice;
  ratingMode: RatingMode;
  /** An mStream installation to import; each vpath goes to one of the folders of `libraries`. */
  mstream: {
    folder: string | null;
    server: { url: string; userName: string; password: string } | null;
    libraries: { vpath: string; folder: string }[];
    importUsers: boolean;
  } | null;
}

export interface OnlineServicesChoice {
  musicBrainz: boolean;
  acoustId: boolean;
  coverArtArchive: boolean;
  lyrics: boolean;
}

export interface SetupFfmpegCheck {
  available: boolean;
  path: string | null;
  version: string | null;
  source: 'CONFIGURED' | 'DOWNLOADED' | 'SYSTEM' | null;
}

export interface ServerDirectory {
  name: string;
  path: string;
  isHidden: boolean;
}

export interface ServerDirectoryListing {
  path: string | null;
  parent: string | null;
  directories: ServerDirectory[];
}

/** A failed API call: its error code and, for validation failures, the codes per field. */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly fields: Record<string, string[]> = {},
    readonly status = 0,
  ) {
    super(code);
  }

  /** Builds the error from REST problem details or a GraphQL error response. */
  static from(error: unknown): ApiError {
    if (error instanceof ApiError) return error;

    if (error instanceof HttpErrorResponse) {
      const body = error.error as { code?: string; errors?: Record<string, string[]> } | null;
      return new ApiError(
        body?.code ?? (error.status === 0 ? 'network' : 'unexpected'),
        body?.errors ?? {},
        error.status,
      );
    }

    // Apollo errors carry the GraphQL errors; Arcanist puts the code in extensions.
    const graphQLErrors = (
      error as { errors?: { extensions?: { code?: string; errors?: Record<string, string[]> } }[] }
    )?.errors;
    const first = graphQLErrors?.[0]?.extensions;
    if (first?.code) return new ApiError(first.code, first.errors ?? {});

    return new ApiError('unexpected');
  }

  /** Field error codes whose path starts with the given prefix, e.g. "admin." or "libraries[0]". */
  fieldsUnder(prefix: string): string[] {
    return Object.entries(this.fields)
      .filter(([field]) => field.startsWith(prefix))
      .flatMap(([, codes]) => codes);
  }
}
