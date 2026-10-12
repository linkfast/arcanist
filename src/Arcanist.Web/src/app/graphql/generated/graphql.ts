/* eslint-disable */
/** Internal type. DO NOT USE DIRECTLY. */
type Exact<T extends { [key: string]: unknown }> = { [K in keyof T]: T[K] };
/** Internal type. DO NOT USE DIRECTLY. */
export type Incremental<T> = T | { [P in keyof T]?: P extends ' $fragmentName' | '__typename' ? T[P] : never };
import type { TypedDocumentNode as DocumentNode } from '@graphql-typed-document-node/core';
export type AnalysisActivity =
  | 'DISABLED'
  | 'IDLE'
  | 'RUNNING'
  | 'UNAVAILABLE'
  | 'WAITING_FOR_SCAN';

export type ArtistRole =
  | 'COMPOSER'
  | 'CONDUCTOR'
  | 'FEATURED'
  | 'LYRICIST'
  | 'PRIMARY'
  | 'PRODUCER'
  | 'REMIXER';

export type ChangeMyPasswordInput = {
  currentPassword: string;
  newPassword: string;
};

export type CreateLibraryInput = {
  name: string;
  scanIntervalHours?: number | null | undefined;
  sources: Array<LibrarySourceInput>;
  visibleToAll: boolean;
};

export type CreateUserInput = {
  displayName?: string | null | undefined;
  libraryIds?: Array<string> | null | undefined;
  password: string;
  permissions?: Array<Permission> | null | undefined;
  role: UserRole;
  userName: string;
};

export type DropFolderInput = {
  convertBitRate?: number | null | undefined;
  convertLossy: boolean;
  convertTo?: TranscodeFormat | null | undefined;
  duplicatePolicy: DuplicatePolicy;
  enabled: boolean;
  librarySourceId: string;
  path: string;
};

export type DuplicatePolicy =
  | 'ADD_VERSION'
  | 'KEEP_BEST'
  | 'SKIP';

export type DuplicateStatus =
  | 'DISMISSED'
  | 'MERGED'
  | 'OPEN';

export type FfmpegDownloadState =
  | 'COMPLETED'
  | 'DOWNLOADING'
  | 'FAILED'
  | 'IDLE'
  | 'INSTALLING';

export type FfmpegSource =
  | 'CONFIGURED'
  | 'DOWNLOADED'
  | 'SYSTEM';

export type ImportReason =
  | 'MISSING_TAGS'
  | 'NOT_BETTER'
  | 'REPLACED_WORSE'
  | 'SAME_AUDIO'
  | 'SAME_TRACK'
  | 'TRANSCODING_FAILED'
  | 'TRANSCODING_UNAVAILABLE'
  | 'UNREADABLE'
  | 'WRITE_FAILED';

export type ImportStatus =
  | 'DISCARDED'
  | 'DUPLICATE'
  | 'FAILED'
  | 'IGNORED'
  | 'IMPORTED'
  | 'NEEDS_REVIEW'
  | 'PENDING';

export type ImportTagsInput = {
  album?: string | null | undefined;
  albumArtist?: string | null | undefined;
  artist?: string | null | undefined;
  discNumber?: number | null | undefined;
  genre?: string | null | undefined;
  title?: string | null | undefined;
  trackNumber?: number | null | undefined;
  year?: number | null | undefined;
};

export type LibrarySourceInput = {
  location: string;
};

export type LibrarySourceKind =
  | 'LOCAL';

export type OnlineService =
  | 'ACOUST_ID'
  | 'COVER_ART_ARCHIVE'
  | 'LYRICS'
  | 'MUSIC_BRAINZ';

export type OrganizeRunKind =
  | 'AUTOMATIC'
  | 'MANUAL';

export type OrganizeRunStatus =
  | 'COMPLETED'
  | 'FAILED'
  | 'RUNNING'
  | 'UNDOING'
  | 'UNDONE';

export type Permission =
  | 'DOWNLOAD'
  | 'MANAGE_LIBRARIES'
  | 'MANAGE_SETTINGS'
  | 'MANAGE_USERS'
  | 'RUN_SCANS'
  | 'VIEW_ALL_STATS';

export type ResetUserPasswordInput = {
  newPassword: string;
};

export type ScanKind =
  | 'FULL'
  | 'INCREMENTAL';

export type ScanPhase =
  | 'CLEANING_UP'
  | 'DISCOVERING'
  | 'READING';

export type ScanStatus =
  | 'CANCELLED'
  | 'COMPLETED'
  | 'FAILED'
  | 'QUEUED'
  | 'RUNNING';

export type SetAcoustIdKeyInput = {
  key?: string | null | undefined;
};

export type SetOnlineServicesInput = {
  acoustId: boolean;
  coverArtArchive: boolean;
  lyrics: boolean;
  musicBrainz: boolean;
};

export type SetUserLibrariesInput = {
  libraryIds: Array<string>;
};

export type TranscodeFormat =
  | 'AAC'
  | 'FLAC'
  | 'MP3'
  | 'OPUS';

export type UpdateLibraryInput = {
  name: string;
  scanIntervalHours?: number | null | undefined;
  visibleToAll: boolean;
};

export type UpdateLibraryOrganizationInput = {
  autoOrganize: boolean;
  template?: string | null | undefined;
};

export type UpdateUserInput = {
  displayName?: string | null | undefined;
  isDisabled: boolean;
  permissions?: Array<Permission> | null | undefined;
  role: UserRole;
};

export type UserRole =
  | 'ADMIN'
  | 'USER';

export type ScanJobFieldsFragment = { id: string, libraryId: string, kind: ScanKind, status: ScanStatus, requestedAt: string, startedAt: string | null, finishedAt: string | null, filesSeen: number, filesAdded: number, filesUpdated: number, filesMissing: number, errors: number, errorCode: string | null, progress: { phase: ScanPhase, filesFound: number, filesToRead: number, filesRead: number } | null };

export type ServerInfoQueryVariables = Exact<{ [key: string]: never; }>;


export type ServerInfoQuery = { serverInfo: { name: string, version: string, apiVersion: number, sourceUrl: string | null } };

export type OverviewQueryVariables = Exact<{ [key: string]: never; }>;


export type OverviewQuery = { libraries: Array<{ id: string }>, artists: { totalCount: number }, albums: { totalCount: number }, tracks: { totalCount: number } };

export type LibrariesQueryVariables = Exact<{ [key: string]: never; }>;


export type LibrariesQuery = { libraries: Array<{ id: string, name: string, visibleToAll: boolean, scanIntervalHours: number | null, createdAt: string, lastScanAt: string | null, sources: Array<{ id: string, kind: LibrarySourceKind, location: string }> | null, organization: { template: string, isDefaultTemplate: boolean, autoOrganize: boolean, autoOrganizeSince: string | null } | null }> };

export type CreateLibraryMutationVariables = Exact<{
  input: CreateLibraryInput;
}>;


export type CreateLibraryMutation = { createLibrary: { id: string } };

export type UpdateLibraryMutationVariables = Exact<{
  id: string;
  input: UpdateLibraryInput;
}>;


export type UpdateLibraryMutation = { updateLibrary: { id: string } };

export type DeleteLibraryMutationVariables = Exact<{
  id: string;
}>;


export type DeleteLibraryMutation = { deleteLibrary: boolean };

export type AddLibrarySourceMutationVariables = Exact<{
  libraryId: string;
  input: LibrarySourceInput;
}>;


export type AddLibrarySourceMutation = { addLibrarySource: { id: string } };

export type RemoveLibrarySourceMutationVariables = Exact<{
  id: string;
}>;


export type RemoveLibrarySourceMutation = { removeLibrarySource: { id: string } };

export type ServerDirectoriesQueryVariables = Exact<{
  path?: string | null | undefined;
}>;


export type ServerDirectoriesQuery = { serverDirectories: { path: string | null, parent: string | null, directories: Array<{ name: string, path: string, isHidden: boolean }> } };

export type ScanJobsQueryVariables = Exact<{
  libraryId?: string | null | undefined;
  limit?: number | null | undefined;
}>;


export type ScanJobsQuery = { scanJobs: Array<{ id: string, libraryId: string, kind: ScanKind, status: ScanStatus, requestedAt: string, startedAt: string | null, finishedAt: string | null, filesSeen: number, filesAdded: number, filesUpdated: number, filesMissing: number, errors: number, errorCode: string | null, progress: { phase: ScanPhase, filesFound: number, filesToRead: number, filesRead: number } | null }> };

export type StartScanMutationVariables = Exact<{
  libraryId: string;
  full: boolean;
}>;


export type StartScanMutation = { startScan: { id: string, libraryId: string, kind: ScanKind, status: ScanStatus, requestedAt: string, startedAt: string | null, finishedAt: string | null, filesSeen: number, filesAdded: number, filesUpdated: number, filesMissing: number, errors: number, errorCode: string | null, progress: { phase: ScanPhase, filesFound: number, filesToRead: number, filesRead: number } | null } };

export type CancelScanMutationVariables = Exact<{
  id: string;
}>;


export type CancelScanMutation = { cancelScan: boolean };

export type ScanJobUpdatedSubscriptionVariables = Exact<{ [key: string]: never; }>;


export type ScanJobUpdatedSubscription = { scanJobUpdated: { id: string, libraryId: string, kind: ScanKind, status: ScanStatus, requestedAt: string, startedAt: string | null, finishedAt: string | null, filesSeen: number, filesAdded: number, filesUpdated: number, filesMissing: number, errors: number, errorCode: string | null, progress: { phase: ScanPhase, filesFound: number, filesToRead: number, filesRead: number } | null } };

export type UserFieldsFragment = { id: string, userName: string, displayName: string | null, role: UserRole, permissions: Array<Permission>, isDisabled: boolean, createdAt: string, lastLoginAt: string | null, libraryIds: Array<string> };

export type UsersQueryVariables = Exact<{ [key: string]: never; }>;


export type UsersQuery = { users: Array<{ id: string, userName: string, displayName: string | null, role: UserRole, permissions: Array<Permission>, isDisabled: boolean, createdAt: string, lastLoginAt: string | null, libraryIds: Array<string> }>, libraries: Array<{ id: string, name: string, visibleToAll: boolean }> };

export type CreateUserMutationVariables = Exact<{
  input: CreateUserInput;
}>;


export type CreateUserMutation = { createUser: { id: string } };

export type UpdateUserMutationVariables = Exact<{
  id: string;
  input: UpdateUserInput;
}>;


export type UpdateUserMutation = { updateUser: { id: string } };

export type SetUserLibrariesMutationVariables = Exact<{
  id: string;
  input: SetUserLibrariesInput;
}>;


export type SetUserLibrariesMutation = { setUserLibraries: { id: string } };

export type ResetUserPasswordMutationVariables = Exact<{
  id: string;
  input: ResetUserPasswordInput;
}>;


export type ResetUserPasswordMutation = { resetUserPassword: boolean };

export type DeleteUserMutationVariables = Exact<{
  id: string;
}>;


export type DeleteUserMutation = { deleteUser: boolean };

export type MySessionsQueryVariables = Exact<{ [key: string]: never; }>;


export type MySessionsQuery = { mySessions: Array<{ id: string, deviceName: string | null, userAgent: string | null, ipAddress: string | null, createdAt: string, lastSeenAt: string, expiresAt: string, isCurrent: boolean }> };

export type RevokeMySessionMutationVariables = Exact<{
  id: string;
}>;


export type RevokeMySessionMutation = { revokeMySession: boolean };

export type ChangeMyPasswordMutationVariables = Exact<{
  input: ChangeMyPasswordInput;
}>;


export type ChangeMyPasswordMutation = { changeMyPassword: boolean };

export type AnalysisProgressFieldsFragment = { activity: AnalysisActivity, enabled: boolean, pending: number, analyzed: number, needsFfmpeg: number, failed: number, filesPerMinute: number, lastAnalyzedAt: string | null, error: string | null };

export type AnalysisProgressQueryVariables = Exact<{ [key: string]: never; }>;


export type AnalysisProgressQuery = { analysisProgress: { activity: AnalysisActivity, enabled: boolean, pending: number, analyzed: number, needsFfmpeg: number, failed: number, filesPerMinute: number, lastAnalyzedAt: string | null, error: string | null } };

export type SetAnalysisEnabledMutationVariables = Exact<{
  enabled: boolean;
}>;


export type SetAnalysisEnabledMutation = { setAnalysisEnabled: { activity: AnalysisActivity, enabled: boolean, pending: number, analyzed: number, needsFfmpeg: number, failed: number, filesPerMinute: number, lastAnalyzedAt: string | null, error: string | null } };

export type AnalysisProgressChangedSubscriptionVariables = Exact<{ [key: string]: never; }>;


export type AnalysisProgressChangedSubscription = { analysisProgressChanged: { activity: AnalysisActivity, enabled: boolean, pending: number, analyzed: number, needsFfmpeg: number, failed: number, filesPerMinute: number, lastAnalyzedAt: string | null, error: string | null } };

export type FfmpegStatusFieldsFragment = { available: boolean, path: string | null, version: string | null, source: FfmpegSource | null, configuredPath: string | null, configuredPathWorks: boolean, canDownload: boolean, download: { state: FfmpegDownloadState, bytesReceived: number, totalBytes: number | null, version: string | null, error: string | null } };

export type FfmpegQueryVariables = Exact<{ [key: string]: never; }>;


export type FfmpegQuery = { ffmpeg: { available: boolean, path: string | null, version: string | null, source: FfmpegSource | null, configuredPath: string | null, configuredPathWorks: boolean, canDownload: boolean, download: { state: FfmpegDownloadState, bytesReceived: number, totalBytes: number | null, version: string | null, error: string | null } } };

export type SetFfmpegPathMutationVariables = Exact<{
  path?: string | null | undefined;
}>;


export type SetFfmpegPathMutation = { setFfmpegPath: { available: boolean, path: string | null, version: string | null, source: FfmpegSource | null, configuredPath: string | null, configuredPathWorks: boolean, canDownload: boolean, download: { state: FfmpegDownloadState, bytesReceived: number, totalBytes: number | null, version: string | null, error: string | null } } };

export type InstallFfmpegMutationVariables = Exact<{ [key: string]: never; }>;


export type InstallFfmpegMutation = { installFfmpeg: { available: boolean, path: string | null, version: string | null, source: FfmpegSource | null, configuredPath: string | null, configuredPathWorks: boolean, canDownload: boolean, download: { state: FfmpegDownloadState, bytesReceived: number, totalBytes: number | null, version: string | null, error: string | null } } };

export type DuplicateTrackFieldsFragment = { id: string, title: string, durationMs: number, trackNumber: number | null, discNumber: number | null, album: { id: string, title: string }, artists: Array<{ artistId: string, name: string, role: ArtistRole }>, files: Array<{ id: string, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null }> };

export type DuplicateCandidatesQueryVariables = Exact<{
  status?: DuplicateStatus | null | undefined;
}>;


export type DuplicateCandidatesQuery = { duplicateCandidates: Array<{ id: string, similarity: number, status: DuplicateStatus, detectedAt: string, trackA: { id: string, title: string, durationMs: number, trackNumber: number | null, discNumber: number | null, album: { id: string, title: string }, artists: Array<{ artistId: string, name: string, role: ArtistRole }>, files: Array<{ id: string, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null }> }, trackB: { id: string, title: string, durationMs: number, trackNumber: number | null, discNumber: number | null, album: { id: string, title: string }, artists: Array<{ artistId: string, name: string, role: ArtistRole }>, files: Array<{ id: string, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null }> } }> };

export type MergeDuplicateMutationVariables = Exact<{
  id: string;
  keepTrackId: string;
}>;


export type MergeDuplicateMutation = { mergeDuplicate: { id: string } };

export type DismissDuplicateMutationVariables = Exact<{
  id: string;
}>;


export type DismissDuplicateMutation = { dismissDuplicate: boolean };

export type OnlineServicesFieldsFragment = { hasAcoustIdKey: boolean, hasOwnAcoustIdKey: boolean, settings: { musicBrainz: boolean, acoustId: boolean, coverArtArchive: boolean, lyrics: boolean }, services: Array<{ service: OnlineService, enabled: boolean, ready: boolean, pending: number, found: number, notFound: number, failed: number, lastRequestAt: string | null, lastError: string | null }> };

export type OnlineServicesQueryVariables = Exact<{ [key: string]: never; }>;


export type OnlineServicesQuery = { onlineServices: { hasAcoustIdKey: boolean, hasOwnAcoustIdKey: boolean, settings: { musicBrainz: boolean, acoustId: boolean, coverArtArchive: boolean, lyrics: boolean }, services: Array<{ service: OnlineService, enabled: boolean, ready: boolean, pending: number, found: number, notFound: number, failed: number, lastRequestAt: string | null, lastError: string | null }> } };

export type SetOnlineServicesMutationVariables = Exact<{
  input: SetOnlineServicesInput;
}>;


export type SetOnlineServicesMutation = { setOnlineServices: { hasAcoustIdKey: boolean, hasOwnAcoustIdKey: boolean, settings: { musicBrainz: boolean, acoustId: boolean, coverArtArchive: boolean, lyrics: boolean }, services: Array<{ service: OnlineService, enabled: boolean, ready: boolean, pending: number, found: number, notFound: number, failed: number, lastRequestAt: string | null, lastError: string | null }> } };

export type SetAcoustIdKeyMutationVariables = Exact<{
  input: SetAcoustIdKeyInput;
}>;


export type SetAcoustIdKeyMutation = { setAcoustIdKey: { hasAcoustIdKey: boolean, hasOwnAcoustIdKey: boolean, settings: { musicBrainz: boolean, acoustId: boolean, coverArtArchive: boolean, lyrics: boolean }, services: Array<{ service: OnlineService, enabled: boolean, ready: boolean, pending: number, found: number, notFound: number, failed: number, lastRequestAt: string | null, lastError: string | null }> } };

export type OrganizeRunFieldsFragment = { id: string, libraryId: string, kind: OrganizeRunKind, status: OrganizeRunStatus, template: string, startedAt: string, finishedAt: string | null, undoneAt: string | null, total: number, moved: number, failed: number, errorCode: string | null };

export type OrganizePreviewQueryVariables = Exact<{
  libraryId: string;
  template?: string | null | undefined;
}>;


export type OrganizePreviewQuery = { organizePreview: { template: string, filesToMove: number, filesInPlace: number, otherFilesToMove: number, unavailableSourceIds: Array<string>, moves: Array<{ sourceId: string, from: string, to: string }> } };

export type OrganizeRunsQueryVariables = Exact<{
  libraryId: string;
}>;


export type OrganizeRunsQuery = { organizeRuns: Array<{ id: string, libraryId: string, kind: OrganizeRunKind, status: OrganizeRunStatus, template: string, startedAt: string, finishedAt: string | null, undoneAt: string | null, total: number, moved: number, failed: number, errorCode: string | null }> };

export type UpdateLibraryOrganizationMutationVariables = Exact<{
  libraryId: string;
  input: UpdateLibraryOrganizationInput;
}>;


export type UpdateLibraryOrganizationMutation = { updateLibraryOrganization: { id: string, organization: { template: string, isDefaultTemplate: boolean, autoOrganize: boolean, autoOrganizeSince: string | null } | null } };

export type OrganizeLibraryMutationVariables = Exact<{
  libraryId: string;
}>;


export type OrganizeLibraryMutation = { organizeLibrary: { id: string, libraryId: string, kind: OrganizeRunKind, status: OrganizeRunStatus, template: string, startedAt: string, finishedAt: string | null, undoneAt: string | null, total: number, moved: number, failed: number, errorCode: string | null } };

export type UndoOrganizeRunMutationVariables = Exact<{
  id: string;
}>;


export type UndoOrganizeRunMutation = { undoOrganizeRun: { id: string, libraryId: string, kind: OrganizeRunKind, status: OrganizeRunStatus, template: string, startedAt: string, finishedAt: string | null, undoneAt: string | null, total: number, moved: number, failed: number, errorCode: string | null } };

export type DropFolderFieldsFragment = { id: string, path: string, libraryId: string, librarySourceId: string, enabled: boolean, duplicatePolicy: DuplicatePolicy, convertTo: TranscodeFormat | null, convertBitRate: number | null, convertLossy: boolean, createdAt: string, available: boolean };

export type DropFoldersQueryVariables = Exact<{ [key: string]: never; }>;


export type DropFoldersQuery = { dropFolders: Array<{ id: string, path: string, libraryId: string, librarySourceId: string, enabled: boolean, duplicatePolicy: DuplicatePolicy, convertTo: TranscodeFormat | null, convertBitRate: number | null, convertLossy: boolean, createdAt: string, available: boolean }> };

export type CreateDropFolderMutationVariables = Exact<{
  input: DropFolderInput;
}>;


export type CreateDropFolderMutation = { createDropFolder: { id: string, path: string, libraryId: string, librarySourceId: string, enabled: boolean, duplicatePolicy: DuplicatePolicy, convertTo: TranscodeFormat | null, convertBitRate: number | null, convertLossy: boolean, createdAt: string, available: boolean } };

export type UpdateDropFolderMutationVariables = Exact<{
  id: string;
  input: DropFolderInput;
}>;


export type UpdateDropFolderMutation = { updateDropFolder: { id: string, path: string, libraryId: string, librarySourceId: string, enabled: boolean, duplicatePolicy: DuplicatePolicy, convertTo: TranscodeFormat | null, convertBitRate: number | null, convertLossy: boolean, createdAt: string, available: boolean } };

export type DeleteDropFolderMutationVariables = Exact<{
  id: string;
}>;


export type DeleteDropFolderMutation = { deleteDropFolder: boolean };

export type ImportItemFieldsFragment = { id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null };

export type ImportQueueQueryVariables = Exact<{
  status?: ImportStatus | null | undefined;
}>;


export type ImportQueueQuery = { importSummary: { pending: number, needsReview: number, duplicate: number, failed: number, ignored: number, imported: number }, importItems: Array<{ id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null }> };

export type AcceptImportItemMutationVariables = Exact<{
  id: string;
  tags?: ImportTagsInput | null | undefined;
}>;


export type AcceptImportItemMutation = { acceptImportItem: { id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null } };

export type RetryImportItemMutationVariables = Exact<{
  id: string;
}>;


export type RetryImportItemMutation = { retryImportItem: { id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null } };

export type IgnoreImportItemMutationVariables = Exact<{
  id: string;
}>;


export type IgnoreImportItemMutation = { ignoreImportItem: { id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null } };

export type DiscardImportItemMutationVariables = Exact<{
  id: string;
}>;


export type DiscardImportItemMutation = { discardImportItem: { id: string, dropFolderId: string, path: string, size: number, status: ImportStatus, reason: ImportReason | null, destinationPath: string | null, trackId: string | null, createdAt: string, updatedAt: string, details: { title: string | null, artist: string | null, album: string | null, albumArtist: string | null, year: number | null, trackNumber: number | null, discNumber: number | null, genre: string | null, format: string | null, bitRate: number | null, sampleRate: number | null, bitDepth: number | null, durationMs: number } | null } };

export const ScanJobFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ScanJobFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ScanJob"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"requestedAt"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"filesSeen"}},{"kind":"Field","name":{"kind":"Name","value":"filesAdded"}},{"kind":"Field","name":{"kind":"Name","value":"filesUpdated"}},{"kind":"Field","name":{"kind":"Name","value":"filesMissing"}},{"kind":"Field","name":{"kind":"Name","value":"errors"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}},{"kind":"Field","name":{"kind":"Name","value":"progress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"phase"}},{"kind":"Field","name":{"kind":"Name","value":"filesFound"}},{"kind":"Field","name":{"kind":"Name","value":"filesToRead"}},{"kind":"Field","name":{"kind":"Name","value":"filesRead"}}]}}]}}]} as unknown as DocumentNode<ScanJobFieldsFragment, unknown>;
export const UserFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"UserFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"UserAccount"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"userName"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"role"}},{"kind":"Field","name":{"kind":"Name","value":"permissions"}},{"kind":"Field","name":{"kind":"Name","value":"isDisabled"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastLoginAt"}},{"kind":"Field","name":{"kind":"Name","value":"libraryIds"}}]}}]} as unknown as DocumentNode<UserFieldsFragment, unknown>;
export const AnalysisProgressFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"AnalysisProgressFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"AnalysisProgress"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"activity"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"analyzed"}},{"kind":"Field","name":{"kind":"Name","value":"needsFfmpeg"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"filesPerMinute"}},{"kind":"Field","name":{"kind":"Name","value":"lastAnalyzedAt"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]} as unknown as DocumentNode<AnalysisProgressFieldsFragment, unknown>;
export const FfmpegStatusFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"FfmpegStatusFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"FfmpegStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"available"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"source"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPath"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPathWorks"}},{"kind":"Field","name":{"kind":"Name","value":"canDownload"}},{"kind":"Field","name":{"kind":"Name","value":"download"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"bytesReceived"}},{"kind":"Field","name":{"kind":"Name","value":"totalBytes"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]}}]} as unknown as DocumentNode<FfmpegStatusFieldsFragment, unknown>;
export const DuplicateTrackFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DuplicateTrackFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"Track"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"album"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}}]}},{"kind":"Field","name":{"kind":"Name","value":"artists"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"artistId"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"role"}}]}},{"kind":"Field","name":{"kind":"Name","value":"files"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}}]}}]}}]} as unknown as DocumentNode<DuplicateTrackFieldsFragment, unknown>;
export const OnlineServicesFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OnlineServicesFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OnlineServicesStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"settings"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"musicBrainz"}},{"kind":"Field","name":{"kind":"Name","value":"acoustId"}},{"kind":"Field","name":{"kind":"Name","value":"coverArtArchive"}},{"kind":"Field","name":{"kind":"Name","value":"lyrics"}}]}},{"kind":"Field","name":{"kind":"Name","value":"hasAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"hasOwnAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"services"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"service"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"ready"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"found"}},{"kind":"Field","name":{"kind":"Name","value":"notFound"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"lastRequestAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastError"}}]}}]}}]} as unknown as DocumentNode<OnlineServicesFieldsFragment, unknown>;
export const OrganizeRunFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OrganizeRunFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OrganizeRun"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"undoneAt"}},{"kind":"Field","name":{"kind":"Name","value":"total"}},{"kind":"Field","name":{"kind":"Name","value":"moved"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}}]}}]} as unknown as DocumentNode<OrganizeRunFieldsFragment, unknown>;
export const DropFolderFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DropFolderFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolder"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"librarySourceId"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"duplicatePolicy"}},{"kind":"Field","name":{"kind":"Name","value":"convertTo"}},{"kind":"Field","name":{"kind":"Name","value":"convertBitRate"}},{"kind":"Field","name":{"kind":"Name","value":"convertLossy"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"available"}}]}}]} as unknown as DocumentNode<DropFolderFieldsFragment, unknown>;
export const ImportItemFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<ImportItemFieldsFragment, unknown>;
export const ServerInfoDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ServerInfo"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"serverInfo"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"apiVersion"}},{"kind":"Field","name":{"kind":"Name","value":"sourceUrl"}}]}}]}}]} as unknown as DocumentNode<ServerInfoQuery, ServerInfoQueryVariables>;
export const OverviewDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Overview"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"libraries"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}},{"kind":"Field","name":{"kind":"Name","value":"artists"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"first"},"value":{"kind":"IntValue","value":"1"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"totalCount"}}]}},{"kind":"Field","name":{"kind":"Name","value":"albums"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"first"},"value":{"kind":"IntValue","value":"1"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"totalCount"}}]}},{"kind":"Field","name":{"kind":"Name","value":"tracks"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"first"},"value":{"kind":"IntValue","value":"1"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"totalCount"}}]}}]}}]} as unknown as DocumentNode<OverviewQuery, OverviewQueryVariables>;
export const LibrariesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Libraries"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"libraries"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"visibleToAll"}},{"kind":"Field","name":{"kind":"Name","value":"scanIntervalHours"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastScanAt"}},{"kind":"Field","name":{"kind":"Name","value":"sources"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"location"}}]}},{"kind":"Field","name":{"kind":"Name","value":"organization"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"isDefaultTemplate"}},{"kind":"Field","name":{"kind":"Name","value":"autoOrganize"}},{"kind":"Field","name":{"kind":"Name","value":"autoOrganizeSince"}}]}}]}}]}}]} as unknown as DocumentNode<LibrariesQuery, LibrariesQueryVariables>;
export const CreateLibraryDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"CreateLibrary"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"CreateLibraryInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"createLibrary"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<CreateLibraryMutation, CreateLibraryMutationVariables>;
export const UpdateLibraryDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateLibrary"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateLibraryInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateLibrary"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateLibraryMutation, UpdateLibraryMutationVariables>;
export const DeleteLibraryDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteLibrary"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteLibrary"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteLibraryMutation, DeleteLibraryMutationVariables>;
export const AddLibrarySourceDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"AddLibrarySource"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"LibrarySourceInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"addLibrarySource"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<AddLibrarySourceMutation, AddLibrarySourceMutationVariables>;
export const RemoveLibrarySourceDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RemoveLibrarySource"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"removeLibrarySource"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<RemoveLibrarySourceMutation, RemoveLibrarySourceMutationVariables>;
export const ServerDirectoriesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ServerDirectories"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"path"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"serverDirectories"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"path"},"value":{"kind":"Variable","name":{"kind":"Name","value":"path"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"parent"}},{"kind":"Field","name":{"kind":"Name","value":"directories"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"isHidden"}}]}}]}}]}}]} as unknown as DocumentNode<ServerDirectoriesQuery, ServerDirectoriesQueryVariables>;
export const ScanJobsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ScanJobs"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"limit"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"scanJobs"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}},{"kind":"Argument","name":{"kind":"Name","value":"limit"},"value":{"kind":"Variable","name":{"kind":"Name","value":"limit"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ScanJobFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ScanJobFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ScanJob"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"requestedAt"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"filesSeen"}},{"kind":"Field","name":{"kind":"Name","value":"filesAdded"}},{"kind":"Field","name":{"kind":"Name","value":"filesUpdated"}},{"kind":"Field","name":{"kind":"Name","value":"filesMissing"}},{"kind":"Field","name":{"kind":"Name","value":"errors"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}},{"kind":"Field","name":{"kind":"Name","value":"progress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"phase"}},{"kind":"Field","name":{"kind":"Name","value":"filesFound"}},{"kind":"Field","name":{"kind":"Name","value":"filesToRead"}},{"kind":"Field","name":{"kind":"Name","value":"filesRead"}}]}}]}}]} as unknown as DocumentNode<ScanJobsQuery, ScanJobsQueryVariables>;
export const StartScanDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"StartScan"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"full"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"startScan"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}},{"kind":"Argument","name":{"kind":"Name","value":"full"},"value":{"kind":"Variable","name":{"kind":"Name","value":"full"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ScanJobFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ScanJobFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ScanJob"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"requestedAt"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"filesSeen"}},{"kind":"Field","name":{"kind":"Name","value":"filesAdded"}},{"kind":"Field","name":{"kind":"Name","value":"filesUpdated"}},{"kind":"Field","name":{"kind":"Name","value":"filesMissing"}},{"kind":"Field","name":{"kind":"Name","value":"errors"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}},{"kind":"Field","name":{"kind":"Name","value":"progress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"phase"}},{"kind":"Field","name":{"kind":"Name","value":"filesFound"}},{"kind":"Field","name":{"kind":"Name","value":"filesToRead"}},{"kind":"Field","name":{"kind":"Name","value":"filesRead"}}]}}]}}]} as unknown as DocumentNode<StartScanMutation, StartScanMutationVariables>;
export const CancelScanDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"CancelScan"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"cancelScan"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<CancelScanMutation, CancelScanMutationVariables>;
export const ScanJobUpdatedDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"subscription","name":{"kind":"Name","value":"ScanJobUpdated"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"scanJobUpdated"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ScanJobFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ScanJobFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ScanJob"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"requestedAt"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"filesSeen"}},{"kind":"Field","name":{"kind":"Name","value":"filesAdded"}},{"kind":"Field","name":{"kind":"Name","value":"filesUpdated"}},{"kind":"Field","name":{"kind":"Name","value":"filesMissing"}},{"kind":"Field","name":{"kind":"Name","value":"errors"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}},{"kind":"Field","name":{"kind":"Name","value":"progress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"phase"}},{"kind":"Field","name":{"kind":"Name","value":"filesFound"}},{"kind":"Field","name":{"kind":"Name","value":"filesToRead"}},{"kind":"Field","name":{"kind":"Name","value":"filesRead"}}]}}]}}]} as unknown as DocumentNode<ScanJobUpdatedSubscription, ScanJobUpdatedSubscriptionVariables>;
export const UsersDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Users"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"users"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"UserFields"}}]}},{"kind":"Field","name":{"kind":"Name","value":"libraries"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"visibleToAll"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"UserFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"UserAccount"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"userName"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"role"}},{"kind":"Field","name":{"kind":"Name","value":"permissions"}},{"kind":"Field","name":{"kind":"Name","value":"isDisabled"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastLoginAt"}},{"kind":"Field","name":{"kind":"Name","value":"libraryIds"}}]}}]} as unknown as DocumentNode<UsersQuery, UsersQueryVariables>;
export const CreateUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"CreateUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"CreateUserInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"createUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<CreateUserMutation, CreateUserMutationVariables>;
export const UpdateUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateUserInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateUserMutation, UpdateUserMutationVariables>;
export const SetUserLibrariesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetUserLibraries"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SetUserLibrariesInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setUserLibraries"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<SetUserLibrariesMutation, SetUserLibrariesMutationVariables>;
export const ResetUserPasswordDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"ResetUserPassword"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ResetUserPasswordInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"resetUserPassword"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<ResetUserPasswordMutation, ResetUserPasswordMutationVariables>;
export const DeleteUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteUserMutation, DeleteUserMutationVariables>;
export const MySessionsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"MySessions"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"mySessions"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"deviceName"}},{"kind":"Field","name":{"kind":"Name","value":"userAgent"}},{"kind":"Field","name":{"kind":"Name","value":"ipAddress"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastSeenAt"}},{"kind":"Field","name":{"kind":"Name","value":"expiresAt"}},{"kind":"Field","name":{"kind":"Name","value":"isCurrent"}}]}}]}}]} as unknown as DocumentNode<MySessionsQuery, MySessionsQueryVariables>;
export const RevokeMySessionDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RevokeMySession"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"revokeMySession"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<RevokeMySessionMutation, RevokeMySessionMutationVariables>;
export const ChangeMyPasswordDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"ChangeMyPassword"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ChangeMyPasswordInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"changeMyPassword"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<ChangeMyPasswordMutation, ChangeMyPasswordMutationVariables>;
export const AnalysisProgressDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"AnalysisProgress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"analysisProgress"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"AnalysisProgressFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"AnalysisProgressFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"AnalysisProgress"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"activity"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"analyzed"}},{"kind":"Field","name":{"kind":"Name","value":"needsFfmpeg"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"filesPerMinute"}},{"kind":"Field","name":{"kind":"Name","value":"lastAnalyzedAt"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]} as unknown as DocumentNode<AnalysisProgressQuery, AnalysisProgressQueryVariables>;
export const SetAnalysisEnabledDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetAnalysisEnabled"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"enabled"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setAnalysisEnabled"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"enabled"},"value":{"kind":"Variable","name":{"kind":"Name","value":"enabled"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"AnalysisProgressFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"AnalysisProgressFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"AnalysisProgress"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"activity"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"analyzed"}},{"kind":"Field","name":{"kind":"Name","value":"needsFfmpeg"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"filesPerMinute"}},{"kind":"Field","name":{"kind":"Name","value":"lastAnalyzedAt"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]} as unknown as DocumentNode<SetAnalysisEnabledMutation, SetAnalysisEnabledMutationVariables>;
export const AnalysisProgressChangedDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"subscription","name":{"kind":"Name","value":"AnalysisProgressChanged"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"analysisProgressChanged"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"AnalysisProgressFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"AnalysisProgressFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"AnalysisProgress"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"activity"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"analyzed"}},{"kind":"Field","name":{"kind":"Name","value":"needsFfmpeg"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"filesPerMinute"}},{"kind":"Field","name":{"kind":"Name","value":"lastAnalyzedAt"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]} as unknown as DocumentNode<AnalysisProgressChangedSubscription, AnalysisProgressChangedSubscriptionVariables>;
export const FfmpegDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Ffmpeg"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"ffmpeg"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"FfmpegStatusFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"FfmpegStatusFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"FfmpegStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"available"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"source"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPath"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPathWorks"}},{"kind":"Field","name":{"kind":"Name","value":"canDownload"}},{"kind":"Field","name":{"kind":"Name","value":"download"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"bytesReceived"}},{"kind":"Field","name":{"kind":"Name","value":"totalBytes"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]}}]} as unknown as DocumentNode<FfmpegQuery, FfmpegQueryVariables>;
export const SetFfmpegPathDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetFfmpegPath"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"path"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setFfmpegPath"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"path"},"value":{"kind":"Variable","name":{"kind":"Name","value":"path"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"FfmpegStatusFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"FfmpegStatusFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"FfmpegStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"available"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"source"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPath"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPathWorks"}},{"kind":"Field","name":{"kind":"Name","value":"canDownload"}},{"kind":"Field","name":{"kind":"Name","value":"download"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"bytesReceived"}},{"kind":"Field","name":{"kind":"Name","value":"totalBytes"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]}}]} as unknown as DocumentNode<SetFfmpegPathMutation, SetFfmpegPathMutationVariables>;
export const InstallFfmpegDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"InstallFfmpeg"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"installFfmpeg"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"FfmpegStatusFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"FfmpegStatusFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"FfmpegStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"available"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"source"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPath"}},{"kind":"Field","name":{"kind":"Name","value":"configuredPathWorks"}},{"kind":"Field","name":{"kind":"Name","value":"canDownload"}},{"kind":"Field","name":{"kind":"Name","value":"download"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"bytesReceived"}},{"kind":"Field","name":{"kind":"Name","value":"totalBytes"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"error"}}]}}]}}]} as unknown as DocumentNode<InstallFfmpegMutation, InstallFfmpegMutationVariables>;
export const DuplicateCandidatesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"DuplicateCandidates"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"status"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"DuplicateStatus"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"duplicateCandidates"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"status"},"value":{"kind":"Variable","name":{"kind":"Name","value":"status"}}},{"kind":"Argument","name":{"kind":"Name","value":"first"},"value":{"kind":"IntValue","value":"200"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"similarity"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"detectedAt"}},{"kind":"Field","name":{"kind":"Name","value":"trackA"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"DuplicateTrackFields"}}]}},{"kind":"Field","name":{"kind":"Name","value":"trackB"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"DuplicateTrackFields"}}]}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DuplicateTrackFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"Track"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"album"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}}]}},{"kind":"Field","name":{"kind":"Name","value":"artists"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"artistId"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"role"}}]}},{"kind":"Field","name":{"kind":"Name","value":"files"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}}]}}]}}]} as unknown as DocumentNode<DuplicateCandidatesQuery, DuplicateCandidatesQueryVariables>;
export const MergeDuplicateDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"MergeDuplicate"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"keepTrackId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"mergeDuplicate"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"keepTrackId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"keepTrackId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<MergeDuplicateMutation, MergeDuplicateMutationVariables>;
export const DismissDuplicateDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DismissDuplicate"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"dismissDuplicate"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DismissDuplicateMutation, DismissDuplicateMutationVariables>;
export const OnlineServicesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"OnlineServices"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"onlineServices"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OnlineServicesFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OnlineServicesFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OnlineServicesStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"settings"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"musicBrainz"}},{"kind":"Field","name":{"kind":"Name","value":"acoustId"}},{"kind":"Field","name":{"kind":"Name","value":"coverArtArchive"}},{"kind":"Field","name":{"kind":"Name","value":"lyrics"}}]}},{"kind":"Field","name":{"kind":"Name","value":"hasAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"hasOwnAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"services"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"service"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"ready"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"found"}},{"kind":"Field","name":{"kind":"Name","value":"notFound"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"lastRequestAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastError"}}]}}]}}]} as unknown as DocumentNode<OnlineServicesQuery, OnlineServicesQueryVariables>;
export const SetOnlineServicesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetOnlineServices"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SetOnlineServicesInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setOnlineServices"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OnlineServicesFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OnlineServicesFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OnlineServicesStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"settings"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"musicBrainz"}},{"kind":"Field","name":{"kind":"Name","value":"acoustId"}},{"kind":"Field","name":{"kind":"Name","value":"coverArtArchive"}},{"kind":"Field","name":{"kind":"Name","value":"lyrics"}}]}},{"kind":"Field","name":{"kind":"Name","value":"hasAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"hasOwnAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"services"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"service"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"ready"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"found"}},{"kind":"Field","name":{"kind":"Name","value":"notFound"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"lastRequestAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastError"}}]}}]}}]} as unknown as DocumentNode<SetOnlineServicesMutation, SetOnlineServicesMutationVariables>;
export const SetAcoustIdKeyDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetAcoustIdKey"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SetAcoustIdKeyInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setAcoustIdKey"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OnlineServicesFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OnlineServicesFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OnlineServicesStatus"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"settings"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"musicBrainz"}},{"kind":"Field","name":{"kind":"Name","value":"acoustId"}},{"kind":"Field","name":{"kind":"Name","value":"coverArtArchive"}},{"kind":"Field","name":{"kind":"Name","value":"lyrics"}}]}},{"kind":"Field","name":{"kind":"Name","value":"hasAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"hasOwnAcoustIdKey"}},{"kind":"Field","name":{"kind":"Name","value":"services"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"service"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"ready"}},{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"found"}},{"kind":"Field","name":{"kind":"Name","value":"notFound"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"lastRequestAt"}},{"kind":"Field","name":{"kind":"Name","value":"lastError"}}]}}]}}]} as unknown as DocumentNode<SetAcoustIdKeyMutation, SetAcoustIdKeyMutationVariables>;
export const OrganizePreviewDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"OrganizePreview"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"template"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"organizePreview"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}},{"kind":"Argument","name":{"kind":"Name","value":"template"},"value":{"kind":"Variable","name":{"kind":"Name","value":"template"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"filesToMove"}},{"kind":"Field","name":{"kind":"Name","value":"filesInPlace"}},{"kind":"Field","name":{"kind":"Name","value":"otherFilesToMove"}},{"kind":"Field","name":{"kind":"Name","value":"unavailableSourceIds"}},{"kind":"Field","name":{"kind":"Name","value":"moves"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"sourceId"}},{"kind":"Field","name":{"kind":"Name","value":"from"}},{"kind":"Field","name":{"kind":"Name","value":"to"}}]}}]}}]}}]} as unknown as DocumentNode<OrganizePreviewQuery, OrganizePreviewQueryVariables>;
export const OrganizeRunsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"OrganizeRuns"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"organizeRuns"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OrganizeRunFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OrganizeRunFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OrganizeRun"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"undoneAt"}},{"kind":"Field","name":{"kind":"Name","value":"total"}},{"kind":"Field","name":{"kind":"Name","value":"moved"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}}]}}]} as unknown as DocumentNode<OrganizeRunsQuery, OrganizeRunsQueryVariables>;
export const UpdateLibraryOrganizationDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateLibraryOrganization"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateLibraryOrganizationInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateLibraryOrganization"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"organization"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"isDefaultTemplate"}},{"kind":"Field","name":{"kind":"Name","value":"autoOrganize"}},{"kind":"Field","name":{"kind":"Name","value":"autoOrganizeSince"}}]}}]}}]}}]} as unknown as DocumentNode<UpdateLibraryOrganizationMutation, UpdateLibraryOrganizationMutationVariables>;
export const OrganizeLibraryDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"OrganizeLibrary"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"organizeLibrary"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"libraryId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"libraryId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OrganizeRunFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OrganizeRunFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OrganizeRun"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"undoneAt"}},{"kind":"Field","name":{"kind":"Name","value":"total"}},{"kind":"Field","name":{"kind":"Name","value":"moved"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}}]}}]} as unknown as DocumentNode<OrganizeLibraryMutation, OrganizeLibraryMutationVariables>;
export const UndoOrganizeRunDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UndoOrganizeRun"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"undoOrganizeRun"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"OrganizeRunFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"OrganizeRunFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"OrganizeRun"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"template"}},{"kind":"Field","name":{"kind":"Name","value":"startedAt"}},{"kind":"Field","name":{"kind":"Name","value":"finishedAt"}},{"kind":"Field","name":{"kind":"Name","value":"undoneAt"}},{"kind":"Field","name":{"kind":"Name","value":"total"}},{"kind":"Field","name":{"kind":"Name","value":"moved"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"errorCode"}}]}}]} as unknown as DocumentNode<UndoOrganizeRunMutation, UndoOrganizeRunMutationVariables>;
export const DropFoldersDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"DropFolders"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"dropFolders"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"DropFolderFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DropFolderFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolder"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"librarySourceId"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"duplicatePolicy"}},{"kind":"Field","name":{"kind":"Name","value":"convertTo"}},{"kind":"Field","name":{"kind":"Name","value":"convertBitRate"}},{"kind":"Field","name":{"kind":"Name","value":"convertLossy"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"available"}}]}}]} as unknown as DocumentNode<DropFoldersQuery, DropFoldersQueryVariables>;
export const CreateDropFolderDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"CreateDropFolder"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolderInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"createDropFolder"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"DropFolderFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DropFolderFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolder"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"librarySourceId"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"duplicatePolicy"}},{"kind":"Field","name":{"kind":"Name","value":"convertTo"}},{"kind":"Field","name":{"kind":"Name","value":"convertBitRate"}},{"kind":"Field","name":{"kind":"Name","value":"convertLossy"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"available"}}]}}]} as unknown as DocumentNode<CreateDropFolderMutation, CreateDropFolderMutationVariables>;
export const UpdateDropFolderDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateDropFolder"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolderInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateDropFolder"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"DropFolderFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"DropFolderFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"DropFolder"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"libraryId"}},{"kind":"Field","name":{"kind":"Name","value":"librarySourceId"}},{"kind":"Field","name":{"kind":"Name","value":"enabled"}},{"kind":"Field","name":{"kind":"Name","value":"duplicatePolicy"}},{"kind":"Field","name":{"kind":"Name","value":"convertTo"}},{"kind":"Field","name":{"kind":"Name","value":"convertBitRate"}},{"kind":"Field","name":{"kind":"Name","value":"convertLossy"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"available"}}]}}]} as unknown as DocumentNode<UpdateDropFolderMutation, UpdateDropFolderMutationVariables>;
export const DeleteDropFolderDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteDropFolder"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteDropFolder"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteDropFolderMutation, DeleteDropFolderMutationVariables>;
export const ImportQueueDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ImportQueue"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"status"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"ImportStatus"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"importSummary"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"pending"}},{"kind":"Field","name":{"kind":"Name","value":"needsReview"}},{"kind":"Field","name":{"kind":"Name","value":"duplicate"}},{"kind":"Field","name":{"kind":"Name","value":"failed"}},{"kind":"Field","name":{"kind":"Name","value":"ignored"}},{"kind":"Field","name":{"kind":"Name","value":"imported"}}]}},{"kind":"Field","name":{"kind":"Name","value":"importItems"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"status"},"value":{"kind":"Variable","name":{"kind":"Name","value":"status"}}},{"kind":"Argument","name":{"kind":"Name","value":"first"},"value":{"kind":"IntValue","value":"200"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ImportItemFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<ImportQueueQuery, ImportQueueQueryVariables>;
export const AcceptImportItemDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"AcceptImportItem"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"tags"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"ImportTagsInput"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"acceptImportItem"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}},{"kind":"Argument","name":{"kind":"Name","value":"tags"},"value":{"kind":"Variable","name":{"kind":"Name","value":"tags"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ImportItemFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<AcceptImportItemMutation, AcceptImportItemMutationVariables>;
export const RetryImportItemDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RetryImportItem"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"retryImportItem"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ImportItemFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<RetryImportItemMutation, RetryImportItemMutationVariables>;
export const IgnoreImportItemDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"IgnoreImportItem"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"ignoreImportItem"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ImportItemFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<IgnoreImportItemMutation, IgnoreImportItemMutationVariables>;
export const DiscardImportItemDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DiscardImportItem"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"discardImportItem"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"ImportItemFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"ImportItemFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"ImportItem"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"dropFolderId"}},{"kind":"Field","name":{"kind":"Name","value":"path"}},{"kind":"Field","name":{"kind":"Name","value":"size"}},{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"reason"}},{"kind":"Field","name":{"kind":"Name","value":"destinationPath"}},{"kind":"Field","name":{"kind":"Name","value":"trackId"}},{"kind":"Field","name":{"kind":"Name","value":"createdAt"}},{"kind":"Field","name":{"kind":"Name","value":"updatedAt"}},{"kind":"Field","name":{"kind":"Name","value":"details"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"artist"}},{"kind":"Field","name":{"kind":"Name","value":"album"}},{"kind":"Field","name":{"kind":"Name","value":"albumArtist"}},{"kind":"Field","name":{"kind":"Name","value":"year"}},{"kind":"Field","name":{"kind":"Name","value":"trackNumber"}},{"kind":"Field","name":{"kind":"Name","value":"discNumber"}},{"kind":"Field","name":{"kind":"Name","value":"genre"}},{"kind":"Field","name":{"kind":"Name","value":"format"}},{"kind":"Field","name":{"kind":"Name","value":"bitRate"}},{"kind":"Field","name":{"kind":"Name","value":"sampleRate"}},{"kind":"Field","name":{"kind":"Name","value":"bitDepth"}},{"kind":"Field","name":{"kind":"Name","value":"durationMs"}}]}}]}}]} as unknown as DocumentNode<DiscardImportItemMutation, DiscardImportItemMutationVariables>;