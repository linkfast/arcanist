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
      organization {
        template
        isDefaultTemplate
        autoOrganize
        autoOrganizeSince
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

export const CreateServerDirectoryMutation = graphql(`
  mutation CreateServerDirectory($input: CreateServerDirectoryInput!) {
    createServerDirectory(input: $input) {
      name
      path
      isHidden
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

export const OrganizeRunFields = graphql(`
  fragment OrganizeRunFields on OrganizeRun {
    id
    libraryId
    kind
    status
    template
    startedAt
    finishedAt
    undoneAt
    total
    moved
    failed
    errorCode
  }
`);

export const OrganizePreviewQuery = graphql(`
  query OrganizePreview($libraryId: ID!, $template: String) {
    organizePreview(libraryId: $libraryId, template: $template) {
      template
      filesToMove
      filesInPlace
      otherFilesToMove
      unavailableSourceIds
      moves {
        sourceId
        from
        to
      }
    }
  }
`);

export const OrganizeRunsQuery = graphql(`
  query OrganizeRuns($libraryId: ID!) {
    organizeRuns(libraryId: $libraryId) {
      ...OrganizeRunFields
    }
  }
`);

export const UpdateLibraryOrganizationMutation = graphql(`
  mutation UpdateLibraryOrganization($libraryId: ID!, $input: UpdateLibraryOrganizationInput!) {
    updateLibraryOrganization(libraryId: $libraryId, input: $input) {
      id
      organization {
        template
        isDefaultTemplate
        autoOrganize
        autoOrganizeSince
      }
    }
  }
`);

export const OrganizeLibraryMutation = graphql(`
  mutation OrganizeLibrary($libraryId: ID!) {
    organizeLibrary(libraryId: $libraryId) {
      ...OrganizeRunFields
    }
  }
`);

export const UndoOrganizeRunMutation = graphql(`
  mutation UndoOrganizeRun($id: ID!) {
    undoOrganizeRun(id: $id) {
      ...OrganizeRunFields
    }
  }
`);

export const DropFolderFields = graphql(`
  fragment DropFolderFields on DropFolder {
    id
    path
    libraryId
    librarySourceId
    enabled
    duplicatePolicy
    convertTo
    convertBitRate
    convertLossy
    createdAt
    available
  }
`);

export const DropFoldersQuery = graphql(`
  query DropFolders {
    dropFolders {
      ...DropFolderFields
    }
  }
`);

export const CreateDropFolderMutation = graphql(`
  mutation CreateDropFolder($input: DropFolderInput!) {
    createDropFolder(input: $input) {
      ...DropFolderFields
    }
  }
`);

export const UpdateDropFolderMutation = graphql(`
  mutation UpdateDropFolder($id: ID!, $input: DropFolderInput!) {
    updateDropFolder(id: $id, input: $input) {
      ...DropFolderFields
    }
  }
`);

export const DeleteDropFolderMutation = graphql(`
  mutation DeleteDropFolder($id: ID!) {
    deleteDropFolder(id: $id)
  }
`);

export const ImportItemFields = graphql(`
  fragment ImportItemFields on ImportItem {
    id
    dropFolderId
    path
    size
    status
    reason
    destinationPath
    trackId
    createdAt
    updatedAt
    details {
      title
      artist
      album
      albumArtist
      year
      trackNumber
      discNumber
      genre
      format
      bitRate
      sampleRate
      bitDepth
      durationMs
    }
  }
`);

export const ImportQueueQuery = graphql(`
  query ImportQueue($status: ImportStatus) {
    importSummary {
      pending
      needsReview
      duplicate
      failed
      ignored
      imported
    }
    importItems(status: $status, first: 200) {
      ...ImportItemFields
    }
  }
`);

export const AcceptImportItemMutation = graphql(`
  mutation AcceptImportItem($id: ID!, $tags: ImportTagsInput) {
    acceptImportItem(id: $id, tags: $tags) {
      ...ImportItemFields
    }
  }
`);

export const RetryImportItemMutation = graphql(`
  mutation RetryImportItem($id: ID!) {
    retryImportItem(id: $id) {
      ...ImportItemFields
    }
  }
`);

export const IgnoreImportItemMutation = graphql(`
  mutation IgnoreImportItem($id: ID!) {
    ignoreImportItem(id: $id) {
      ...ImportItemFields
    }
  }
`);

export const DiscardImportItemMutation = graphql(`
  mutation DiscardImportItem($id: ID!) {
    discardImportItem(id: $id) {
      ...ImportItemFields
    }
  }
`);

export const RatingModeQuery = graphql(`
  query RatingMode {
    serverInfo {
      ratingMode
    }
  }
`);

export const SetRatingModeMutation = graphql(`
  mutation SetRatingMode($mode: RatingMode!) {
    setRatingMode(mode: $mode)
  }
`);

export const StatsTrackFields = graphql(`
  fragment StatsTrackFields on Track {
    id
    title
    album {
      id
      title
    }
    artists {
      artistId
      name
      role
    }
  }
`);

export const ServerStatsQuery = graphql(`
  query ServerStats($period: StatsPeriod!, $timeZone: String) {
    serverStats(period: $period, timeZone: $timeZone) {
      library {
        artists
        albums
        tracks
        files
        durationMs
        sizeBytes
        formats {
          format
          files
          sizeBytes
        }
      }
      listening {
        period
        from
        plays
        listenedMs
        distinctTracks
        bucket
        timeline {
          start
          plays
          listenedMs
        }
        topTracks {
          plays
          track {
            ...StatsTrackFields
          }
        }
        topArtists {
          plays
          artist {
            id
            name
          }
        }
        topAlbums {
          plays
          album {
            id
            title
            artworkId
            artist {
              id
              name
            }
          }
        }
      }
      users {
        userId
        userName
        displayName
        plays
        listenedMs
        lastPlayedAt
      }
    }
  }
`);

export const AppPasswordFields = graphql(`
  fragment AppPasswordFields on AppPassword {
    id
    name
    createdAt
    lastUsedAt
  }
`);

export const AppPasswordsQuery = graphql(`
  query AppPasswords {
    appPasswords {
      ...AppPasswordFields
    }
  }
`);

export const CreateAppPasswordMutation = graphql(`
  mutation CreateAppPassword($name: String!) {
    createAppPassword(name: $name) {
      secret
      appPassword {
        ...AppPasswordFields
      }
    }
  }
`);

export const DeleteAppPasswordMutation = graphql(`
  mutation DeleteAppPassword($id: ID!) {
    deleteAppPassword(id: $id)
  }
`);

export const FrontendFields = graphql(`
  fragment FrontendFields on Frontend {
    id
    name
    version
    description
    author
    license
    homepage
    builtIn
    isDefault
    path
    installedAt
  }
`);

export const FrontendsQuery = graphql(`
  query Frontends {
    frontends {
      ...FrontendFields
    }
  }
`);

export const InstallFrontendFromUrlMutation = graphql(`
  mutation InstallFrontendFromUrl($url: String!) {
    installFrontendFromUrl(url: $url) {
      ...FrontendFields
    }
  }
`);

export const SetDefaultFrontendMutation = graphql(`
  mutation SetDefaultFrontend($id: String!) {
    setDefaultFrontend(id: $id) {
      ...FrontendFields
    }
  }
`);

export const UninstallFrontendMutation = graphql(`
  mutation UninstallFrontend($id: String!) {
    uninstallFrontend(id: $id)
  }
`);

export const ExtensionFields = graphql(`
  fragment ExtensionFields on Extension {
    id
    name
    version
    description
    author
    license
    homepage
    builtIn
    enabled
    status
    errorCode
    restartRequired
    routes
    basePath
  }
`);

export const ExtensionsQuery = graphql(`
  query Extensions {
    extensions {
      ...ExtensionFields
    }
  }
`);

export const InstallExtensionFromUrlMutation = graphql(`
  mutation InstallExtensionFromUrl($url: String!) {
    installExtensionFromUrl(url: $url) {
      ...ExtensionFields
    }
  }
`);

export const SetExtensionEnabledMutation = graphql(`
  mutation SetExtensionEnabled($id: String!, $enabled: Boolean!) {
    setExtensionEnabled(id: $id, enabled: $enabled) {
      ...ExtensionFields
    }
  }
`);

export const UninstallExtensionMutation = graphql(`
  mutation UninstallExtension($id: String!) {
    uninstallExtension(id: $id)
  }
`);

export const MstreamImportJobFields = graphql(`
  fragment MstreamImportJobFields on MstreamImportJob {
    id
    kind
    source
    status
    createdAt
    finishedAt
    librariesCreated
    usersCreated
    playlists
    ratings
    plays
    unmatched
    errorCode
  }
`);

export const MstreamAnalysisFields = graphql(`
  fragment MstreamAnalysisFields on MstreamAnalysis {
    version
    libraries {
      vpath
      root
      rootExists
      sourceId
    }
    users {
      userName
      isAdmin
      vpaths
      exists
      canSignIn
    }
    playlists
    ratedTracks
    playedTracks
  }
`);

export const MstreamImportsQuery = graphql(`
  query MstreamImports {
    mstreamImports {
      ...MstreamImportJobFields
    }
  }
`);

export const MstreamLibrarySourcesQuery = graphql(`
  query MstreamLibrarySources {
    libraries {
      id
      name
      sources {
        id
        location
      }
    }
  }
`);

export const AnalyzeMstreamFolderMutation = graphql(`
  mutation AnalyzeMstreamFolder($folder: String!) {
    analyzeMstreamFolder(folder: $folder) {
      ...MstreamAnalysisFields
    }
  }
`);

export const AnalyzeMstreamServerMutation = graphql(`
  mutation AnalyzeMstreamServer($input: MstreamServerInput!) {
    analyzeMstreamServer(input: $input) {
      ...MstreamAnalysisFields
    }
  }
`);

export const ImportMstreamMutation = graphql(`
  mutation ImportMstream($input: ImportMstreamInput!) {
    importMstream(input: $input) {
      ...MstreamImportJobFields
    }
  }
`);
