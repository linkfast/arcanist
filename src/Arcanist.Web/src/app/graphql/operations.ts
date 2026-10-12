import { graphql } from './generated';

// Every GraphQL operation the panel uses. `npm run codegen` turns them into typed documents.

export const ScanJobFields = graphql(`
  fragment ScanJobFields on ScanJob {
    id
    libraryId
    kind
    status
    requestedAt
    startedAt
    finishedAt
    filesSeen
    filesAdded
    filesUpdated
    filesMissing
    errors
    errorCode
    progress {
      phase
      filesFound
      filesToRead
      filesRead
    }
  }
`);

export const ServerInfoQuery = graphql(`
  query ServerInfo {
    serverInfo {
      name
      version
      apiVersion
      sourceUrl
    }
  }
`);

export const OverviewQuery = graphql(`
  query Overview {
    libraries {
      id
    }
    artists(first: 1) {
      totalCount
    }
    albums(first: 1) {
      totalCount
    }
    tracks(first: 1) {
      totalCount
    }
  }
`);

export const LibrariesQuery = graphql(`
  query Libraries {
    libraries {
      id
      name
      visibleToAll
      scanIntervalHours
      createdAt
      lastScanAt
      sources {
        id
        kind
        location
      }
    }
  }
`);

export const CreateLibraryMutation = graphql(`
  mutation CreateLibrary($input: CreateLibraryInput!) {
    createLibrary(input: $input) {
      id
    }
  }
`);

export const UpdateLibraryMutation = graphql(`
  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {
    updateLibrary(id: $id, input: $input) {
      id
    }
  }
`);

export const DeleteLibraryMutation = graphql(`
  mutation DeleteLibrary($id: ID!) {
    deleteLibrary(id: $id)
  }
`);

export const AddLibrarySourceMutation = graphql(`
  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {
    addLibrarySource(libraryId: $libraryId, input: $input) {
      id
    }
  }
`);

export const RemoveLibrarySourceMutation = graphql(`
  mutation RemoveLibrarySource($id: ID!) {
    removeLibrarySource(id: $id) {
      id
    }
  }
`);

export const ServerDirectoriesQuery = graphql(`
  query ServerDirectories($path: String) {
    serverDirectories(path: $path) {
      path
      parent
      directories {
        name
        path
        isHidden
      }
    }
  }
`);

export const ScanJobsQuery = graphql(`
  query ScanJobs($libraryId: ID, $limit: Int) {
    scanJobs(libraryId: $libraryId, limit: $limit) {
      ...ScanJobFields
    }
  }
`);

export const StartScanMutation = graphql(`
  mutation StartScan($libraryId: ID!, $full: Boolean!) {
    startScan(libraryId: $libraryId, full: $full) {
      ...ScanJobFields
    }
  }
`);

export const CancelScanMutation = graphql(`
  mutation CancelScan($id: ID!) {
    cancelScan(id: $id)
  }
`);

export const ScanJobUpdatedSubscription = graphql(`
  subscription ScanJobUpdated {
    scanJobUpdated {
      ...ScanJobFields
    }
  }
`);

export const UserFields = graphql(`
  fragment UserFields on UserAccount {
    id
    userName
    displayName
    role
    permissions
    isDisabled
    createdAt
    lastLoginAt
    libraryIds
  }
`);

export const UsersQuery = graphql(`
  query Users {
    users {
      ...UserFields
    }
    libraries {
      id
      name
      visibleToAll
    }
  }
`);

export const CreateUserMutation = graphql(`
  mutation CreateUser($input: CreateUserInput!) {
    createUser(input: $input) {
      id
    }
  }
`);

export const UpdateUserMutation = graphql(`
  mutation UpdateUser($id: ID!, $input: UpdateUserInput!) {
    updateUser(id: $id, input: $input) {
      id
    }
  }
`);

export const SetUserLibrariesMutation = graphql(`
  mutation SetUserLibraries($id: ID!, $input: SetUserLibrariesInput!) {
    setUserLibraries(id: $id, input: $input) {
      id
    }
  }
`);

export const ResetUserPasswordMutation = graphql(`
  mutation ResetUserPassword($id: ID!, $input: ResetUserPasswordInput!) {
    resetUserPassword(id: $id, input: $input)
  }
`);

export const DeleteUserMutation = graphql(`
  mutation DeleteUser($id: ID!) {
    deleteUser(id: $id)
  }
`);

export const MySessionsQuery = graphql(`
  query MySessions {
    mySessions {
      id
      deviceName
      userAgent
      ipAddress
      createdAt
      lastSeenAt
      expiresAt
      isCurrent
    }
  }
`);

export const RevokeMySessionMutation = graphql(`
  mutation RevokeMySession($id: ID!) {
    revokeMySession(id: $id)
  }
`);

export const ChangeMyPasswordMutation = graphql(`
  mutation ChangeMyPassword($input: ChangeMyPasswordInput!) {
    changeMyPassword(input: $input)
  }
`);

export const AnalysisProgressFields = graphql(`
  fragment AnalysisProgressFields on AnalysisProgress {
    activity
    enabled
    pending
    analyzed
    needsFfmpeg
    failed
    filesPerMinute
    lastAnalyzedAt
    error
  }
`);

export const AnalysisProgressQuery = graphql(`
  query AnalysisProgress {
    analysisProgress {
      ...AnalysisProgressFields
    }
  }
`);

export const SetAnalysisEnabledMutation = graphql(`
  mutation SetAnalysisEnabled($enabled: Boolean!) {
    setAnalysisEnabled(enabled: $enabled) {
      ...AnalysisProgressFields
    }
  }
`);

export const AnalysisProgressChangedSubscription = graphql(`
  subscription AnalysisProgressChanged {
    analysisProgressChanged {
      ...AnalysisProgressFields
    }
  }
`);

export const FfmpegStatusFields = graphql(`
  fragment FfmpegStatusFields on FfmpegStatus {
    available
    path
    version
    source
    configuredPath
    configuredPathWorks
    canDownload
    download {
      state
      bytesReceived
      totalBytes
      version
      error
    }
  }
`);

export const FfmpegQuery = graphql(`
  query Ffmpeg {
    ffmpeg {
      ...FfmpegStatusFields
    }
  }
`);

export const SetFfmpegPathMutation = graphql(`
  mutation SetFfmpegPath($path: String) {
    setFfmpegPath(path: $path) {
      ...FfmpegStatusFields
    }
  }
`);

export const InstallFfmpegMutation = graphql(`
  mutation InstallFfmpeg {
    installFfmpeg {
      ...FfmpegStatusFields
    }
  }
`);

export const DuplicateTrackFields = graphql(`
  fragment DuplicateTrackFields on Track {
    id
    title
    durationMs
    trackNumber
    discNumber
    album {
      id
      title
    }
    artists {
      artistId
      name
      role
    }
    files {
      id
      format
      bitRate
      sampleRate
      bitDepth
    }
  }
`);

export const DuplicateCandidatesQuery = graphql(`
  query DuplicateCandidates($status: DuplicateStatus) {
    duplicateCandidates(status: $status, first: 200) {
      id
      similarity
      status
      detectedAt
      trackA {
        ...DuplicateTrackFields
      }
      trackB {
        ...DuplicateTrackFields
      }
    }
  }
`);

export const MergeDuplicateMutation = graphql(`
  mutation MergeDuplicate($id: ID!, $keepTrackId: ID!) {
    mergeDuplicate(id: $id, keepTrackId: $keepTrackId) {
      id
    }
  }
`);

export const DismissDuplicateMutation = graphql(`
  mutation DismissDuplicate($id: ID!) {
    dismissDuplicate(id: $id)
  }
`);

export const OnlineServicesFields = graphql(`
  fragment OnlineServicesFields on OnlineServicesStatus {
    settings {
      musicBrainz
      acoustId
      coverArtArchive
      lyrics
    }
    hasAcoustIdKey
    hasOwnAcoustIdKey
    services {
      service
      enabled
      ready
      pending
      found
      notFound
      failed
      lastRequestAt
      lastError
    }
  }
`);

export const OnlineServicesQuery = graphql(`
  query OnlineServices {
    onlineServices {
      ...OnlineServicesFields
    }
  }
`);

export const SetOnlineServicesMutation = graphql(`
  mutation SetOnlineServices($input: SetOnlineServicesInput!) {
    setOnlineServices(input: $input) {
      ...OnlineServicesFields
    }
  }
`);

export const SetAcoustIdKeyMutation = graphql(`
  mutation SetAcoustIdKey($input: SetAcoustIdKeyInput!) {
    setAcoustIdKey(input: $input) {
      ...OnlineServicesFields
    }
  }
`);
