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
      scanIntervalMinutes
      watchForChanges
      createdAt
      lastScanAt
      disabled
      personal
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

export const LibrarySourceMoveFields = graphql(`
  fragment LibrarySourceMoveFields on LibrarySourceMove {
    sourceId
    libraryId
    from
    to
    keepOriginals
    phase
    filesDone
    filesTotal
    bytesDone
    bytesTotal
    startedAt
    finishedAt
    errorCode
  }
`);

export const LibrarySourceMovesQuery = graphql(`
  query LibrarySourceMoves {
    librarySourceMoves {
      ...LibrarySourceMoveFields
    }
  }
`);

export const MoveLibrarySourceMutation = graphql(`
  mutation MoveLibrarySource($id: ID!, $input: MoveLibrarySourceInput!) {
    moveLibrarySource(id: $id, input: $input) {
      ...LibrarySourceMoveFields
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

export const ServerConfigurationFields = graphql(`
  fragment ServerConfigurationFields on ServerConfiguration {
    defaultLanguage
    ratingMode
    shareAnonymousStatistics
    ffmpegPath
    onlineServices {
      musicBrainz
      acoustId
      coverArtArchive
      lyrics
    }
    privacy {
      noTelemetry
      externalServices
    }
  }
`);

export const PrivacyQuery = graphql(`
  query Privacy {
    privacy {
      noTelemetry
      externalServices
    }
    anonymousStatistics
  }
`);

export const SetPrivacyMutation = graphql(`
  mutation SetPrivacy($input: SetPrivacyInput!) {
    setPrivacy(input: $input) {
      noTelemetry
      externalServices
    }
  }
`);

export const SetupExtrasQuery = graphql(`
  query SetupExtras {
    users {
      id
      userName
      displayName
      role
    }
    p2pStatus {
      enabled
      customName
      relay
      relayUrl
      bandwidthKbps
      maxStreams
    }
    extensions {
      id
      builtIn
      enabled
    }
  }
`);

export const ServerConfigurationQuery = graphql(`
  query ServerConfiguration {
    serverConfiguration {
      ...ServerConfigurationFields
    }
  }
`);

export const ReconfigureServerMutation = graphql(`
  mutation ReconfigureServer($input: ReconfigureServerInput!) {
    reconfigureServer(input: $input) {
      ...ServerConfigurationFields
    }
  }
`);

export const SetAnonymousStatisticsMutation = graphql(`
  mutation SetAnonymousStatistics($enabled: Boolean!) {
    setAnonymousStatistics(enabled: $enabled)
  }
`);

export const ResetSettingsMutation = graphql(`
  mutation ResetSettings {
    resetSettings {
      restartRequired
    }
  }
`);

export const ResetUserDataMutation = graphql(`
  mutation ResetUserData {
    resetUserData {
      restartRequired
    }
  }
`);

export const FactoryResetMutation = graphql(`
  mutation FactoryReset($input: FactoryResetInput!) {
    factoryReset(input: $input) {
      restartRequired
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
    mode
    kind
    uploadToCloud
    uploadAccount
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
    suggestionState
    suggestions {
      title
      artist
      album
      albumArtist
      year
      trackNumber
      discNumber
      score
      recordingMbid
      releaseMbid
    }
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
        remote
        server
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
    adminUrl
    iconUrl
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

export const TranscodingFields = graphql(`
  fragment TranscodingFields on TranscodingStatus {
    cacheBytes
    cacheFiles
    working
    ffmpegAvailable
    settings {
      cacheEnabled
      cacheMaxBytes
      cacheMaxAgeDays
      pregenerateLibraries
      pregenerateOnlyLossless
      pregenerate {
        format
        bitRate
      }
    }
    pregenerated {
      format
      bitRate
      done
      total
      bytes
      estimatedBytes
    }
  }
`);

export const TranscodingQuery = graphql(`
  query Transcoding {
    transcoding {
      ...TranscodingFields
    }
  }
`);

export const SetTranscodingSettingsMutation = graphql(`
  mutation SetTranscodingSettings($input: SetTranscodingSettingsInput!) {
    setTranscodingSettings(input: $input) {
      ...TranscodingFields
    }
  }
`);

export const ClearTranscodeCacheMutation = graphql(`
  mutation ClearTranscodeCache {
    clearTranscodeCache {
      ...TranscodingFields
    }
  }
`);

export const ServerUpdateFields = graphql(`
  fragment ServerUpdateFields on ServerUpdate {
    enabled
    currentVersion
    installKind
    updateAvailable
    checkedAt
    errorCode
    latest {
      version
      name
      notes
      url
      publishedAt
      prerelease
    }
  }
`);

export const ServerUpdateQuery = graphql(`
  query ServerUpdate {
    serverUpdate {
      ...ServerUpdateFields
    }
  }
`);

export const CheckServerUpdateMutation = graphql(`
  mutation CheckServerUpdate {
    checkServerUpdate {
      ...ServerUpdateFields
    }
  }
`);

export const SetServerUpdateSettingsMutation = graphql(`
  mutation SetServerUpdateSettings($input: SetServerUpdateSettingsInput!) {
    setServerUpdateSettings(input: $input) {
      ...ServerUpdateFields
    }
  }
`);

export const ExtensionMarketplaceQuery = graphql(`
  query ExtensionMarketplace($language: String, $refresh: Boolean) {
    extensionMarketplace(language: $language, refresh: $refresh) {
      available
      errorCode
      fetchedAt
      source
      extensions {
        id
        name
        description
        author
        homepage
        license
        iconUrl
        official
        categories
        version
        size
        minServerVersion
        compatible
        installedVersion
        updateAvailable
        builtIn
      }
    }
  }
`);

export const InstallMarketplaceExtensionMutation = graphql(`
  mutation InstallMarketplaceExtension($input: InstallMarketplaceExtensionInput!) {
    installMarketplaceExtension(input: $input) {
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

export const RestartServerMutation = graphql(`
  mutation RestartServer {
    restartServer {
      restartRequired
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
    filesTotal
    filesCopied
    filesPresent
    filesFailed
    canRetry
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
      files
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

export const DlnaImportJobFields = graphql(`
  fragment DlnaImportJobFields on DlnaImportJob {
    id
    serverName
    folders
    libraryId
    libraryName
    status
    found
    copied
    duplicates
    failed
    copiedBytes
    createdAt
    finishedAt
    errorCode
    failures {
      title
      artist
      album
      reason
    }
  }
`);

export const DlnaImportsQuery = graphql(`
  query DlnaImports {
    dlnaImports {
      ...DlnaImportJobFields
    }
  }
`);

export const DlnaMediaServersQuery = graphql(`
  query DlnaMediaServers {
    dlnaMediaServers {
      id
      name
      manufacturer
      model
      location
    }
  }
`);

export const DlnaMediaServerQuery = graphql(`
  query DlnaMediaServer($location: String!) {
    dlnaMediaServer(location: $location) {
      id
      name
      manufacturer
      model
      location
    }
  }
`);

export const BrowseDlnaMediaServerQuery = graphql(`
  query BrowseDlnaMediaServer($location: String!, $folderId: String) {
    browseDlnaMediaServer(location: $location, folderId: $folderId) {
      id
      tracks
      truncated
      folders {
        id
        title
        childCount
      }
    }
  }
`);

export const StartDlnaImportMutation = graphql(`
  mutation StartDlnaImport($input: StartDlnaImportInput!) {
    startDlnaImport(input: $input) {
      ...DlnaImportJobFields
    }
  }
`);

export const DeleteDlnaImportMutation = graphql(`
  mutation DeleteDlnaImport($id: ID!) {
    deleteDlnaImport(id: $id)
  }
`);

export const DeleteMstreamImportMutation = graphql(`
  mutation DeleteMstreamImport($id: ID!) {
    deleteMstreamImport(id: $id)
  }
`);

export const ClearImportHistoryMutation = graphql(`
  mutation ClearImportHistory {
    clearImportHistory
  }
`);

export const CancelDlnaImportMutation = graphql(`
  mutation CancelDlnaImport($id: ID!) {
    cancelDlnaImport(id: $id) {
      ...DlnaImportJobFields
    }
  }
`);

export const RetryDlnaImportMutation = graphql(`
  mutation RetryDlnaImport($id: ID!) {
    retryDlnaImport(id: $id) {
      ...DlnaImportJobFields
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

export const RetryMstreamImportMutation = graphql(`
  mutation RetryMstreamImport($id: ID!) {
    retryMstreamImport(id: $id) {
      ...MstreamImportJobFields
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

export const DlnaStatusFields = graphql(`
  fragment DlnaStatusFields on DlnaStatus {
    enabled
    name
    friendlyName
    libraryIds
    sections
    ratingsUserId
    addresses
    convertsFormats
    recordPlays
  }
`);

export const MstreamStatusFields = graphql(`
  fragment MstreamStatusFields on MstreamStatus {
    enabled
    starThreshold
    passwordSignIn
    features
    quickConnect
    p2pEnabled
    pairingCode
  }
`);

export const MstreamQuery = graphql(`
  query Mstream {
    mstream {
      ...MstreamStatusFields
    }
    serverInfo {
      ratingMode
    }
  }
`);

export const SetMstreamSettingsMutation = graphql(`
  mutation SetMstreamSettings($input: MstreamSettingsInput!) {
    setMstreamSettings(input: $input) {
      ...MstreamStatusFields
    }
  }
`);

export const RotateMstreamQuickConnectMutation = graphql(`
  mutation RotateMstreamQuickConnect {
    rotateMstreamQuickConnect {
      ...MstreamStatusFields
    }
  }
`);

export const DlnaQuery = graphql(`
  query Dlna {
    dlna {
      ...DlnaStatusFields
    }
    libraries {
      id
      name
    }
    users {
      id
      userName
      displayName
    }
    serverInfo {
      ratingMode
    }
  }
`);

export const SetDlnaSettingsMutation = graphql(`
  mutation SetDlnaSettings($input: DlnaSettingsInput!) {
    setDlnaSettings(input: $input) {
      ...DlnaStatusFields
    }
  }
`);

export const SetDlnaRecordPlaysMutation = graphql(`
  mutation SetDlnaRecordPlays($record: Boolean!) {
    setDlnaRecordPlays(record: $record) {
      ...DlnaStatusFields
    }
  }
`);

export const DlnaRendererFields = graphql(`
  fragment DlnaRendererFields on DlnaRenderer {
    id
    name
    manufacturer
    model
    address
    manual
  }
`);

export const DlnaRenderersQuery = graphql(`
  query DlnaRenderers {
    dlnaRenderers {
      ...DlnaRendererFields
    }
  }
`);

export const DlnaRendererStateFields = graphql(`
  fragment DlnaRendererStateFields on DlnaRendererState {
    renderer {
      ...DlnaRendererFields
    }
    state
    positionMs
    durationMs
    volume
    queue
    queueIndex
    startedByYou
    track {
      id
      title
      artworkId
      album {
        title
      }
      artists {
        name
        role
      }
    }
  }
`);

export const DlnaRendererQuery = graphql(`
  query DlnaRenderer($id: String!) {
    dlnaRenderer(id: $id) {
      ...DlnaRendererStateFields
    }
  }
`);

export const AddDlnaRendererMutation = graphql(`
  mutation AddDlnaRenderer($url: String!) {
    addDlnaRenderer(url: $url) {
      ...DlnaRendererFields
    }
  }
`);

export const PlayOnDlnaRendererMutation = graphql(`
  mutation PlayOnDlnaRenderer($input: PlayOnDlnaRendererInput!) {
    playOnDlnaRenderer(input: $input) {
      ...DlnaRendererStateFields
    }
  }
`);

export const ControlDlnaRendererMutation = graphql(`
  mutation ControlDlnaRenderer($input: ControlDlnaRendererInput!) {
    controlDlnaRenderer(input: $input) {
      ...DlnaRendererStateFields
    }
  }
`);

export const SetDlnaRendererVolumeMutation = graphql(`
  mutation SetDlnaRendererVolume($rendererId: String!, $volume: Int!) {
    setDlnaRendererVolume(rendererId: $rendererId, volume: $volume) {
      ...DlnaRendererStateFields
    }
  }
`);

export const DlnaAlbumSearchQuery = graphql(`
  query DlnaAlbumSearch($search: String!) {
    albums(search: $search, first: 8) {
      items {
        id
        title
        artist {
          name
        }
      }
    }
  }
`);

export const DlnaAlbumTracksQuery = graphql(`
  query DlnaAlbumTracks($albumId: ID!) {
    tracks(albumId: $albumId) {
      items {
        id
      }
    }
  }
`);

export const P2PStatusFields = graphql(`
  fragment P2PStatusFields on P2PStatus {
    enabled
    available
    running
    errorCode
    nodeId
    name
    customName
    relay
    relayUrl
    homeRelay
    relayConnected
    bandwidthKbps
    maxStreams
    connectedPeers
    activeStreams
    currentBytesPerSecond
    peakBytesPerSecond
  }
`);

export const NetworkPeerFields = graphql(`
  fragment NetworkPeerFields on NetworkPeer {
    id
    kind
    source
    name
    nodeId
    userId
    userName
    rules {
      allowDownload
      maxBitRate
      bandwidthKbps
      maxStreams
      allowCache
      countPlays
    }
    libraryIds
    createdAt
    expiresAt
    revokedAt
    lastSeenAt
    online
    activeStreams
    bytesSent
    bytesReceived
    currentBytesPerSecond
    averageBytesPerSecond
    peakBytesPerSecond
    streams
  }
`);

export const ReceivedShareFields = graphql(`
  fragment ReceivedShareFields on ReceivedShare {
    id
    name
    nodeId
    libraryId
    rules {
      allowDownload
      maxBitRate
      bandwidthKbps
      maxStreams
      allowCache
      countPlays
    }
    active
    online
    createdAt
    lastContactAt
    copy {
      status
      total
      copied
      skipped
      errorCode
    }
    sharePlays
  }
`);

export const RemoteLibrarySettingsFields = graphql(`
  fragment RemoteLibrarySettingsFields on RemoteLibrarySettings {
    cacheEnabled
    cacheMaxBytes
    cacheMaxAgeDays
    transcoding
    cacheBytes
    cacheFiles
  }
`);

export const RemoteLibrarySettingsQuery = graphql(`
  query RemoteLibrarySettings {
    remoteLibrarySettings {
      ...RemoteLibrarySettingsFields
    }
  }
`);

export const SetRemoteLibrarySettingsMutation = graphql(`
  mutation SetRemoteLibrarySettings($input: SetRemoteLibrarySettingsInput!) {
    setRemoteLibrarySettings(input: $input) {
      ...RemoteLibrarySettingsFields
    }
  }
`);

export const ClearRemoteLibraryCacheMutation = graphql(`
  mutation ClearRemoteLibraryCache {
    clearRemoteLibraryCache {
      ...RemoteLibrarySettingsFields
    }
  }
`);

export const SetReceivedSharePlaysMutation = graphql(`
  mutation SetReceivedSharePlays($input: SetReceivedSharePlaysInput!) {
    setReceivedSharePlays(input: $input) {
      ...ReceivedShareFields
    }
  }
`);

export const NetworkQuery = graphql(`
  query Network {
    p2pStatus {
      ...P2PStatusFields
    }
    networkPeers {
      ...NetworkPeerFields
    }
    receivedShares {
      ...ReceivedShareFields
    }
    p2pActivity(limit: 50) {
      id
      at
      kind
      peerId
      peerName
      detail
    }
    libraries {
      id
      name
      sources {
        id
        kind
        location
      }
    }
  }
`);

export const MyDevicesQuery = graphql(`
  query MyDevices {
    networkPeers(kind: DEVICE) {
      ...NetworkPeerFields
    }
  }
`);

export const SetP2PSettingsMutation = graphql(`
  mutation SetP2PSettings($input: P2PSettingsInput!) {
    setP2pSettings(input: $input) {
      ...P2PStatusFields
    }
  }
`);

export const CreateDeviceInvitationMutation = graphql(`
  mutation CreateDeviceInvitation($input: CreateDeviceInvitationInput!) {
    createDeviceInvitation(input: $input) {
      link
      expiresAt
    }
  }
`);

export const CreateShareInvitationMutation = graphql(`
  mutation CreateShareInvitation($input: CreateShareInvitationInput!) {
    createShareInvitation(input: $input) {
      link
      expiresAt
    }
  }
`);

export const UpdateNetworkPeerMutation = graphql(`
  mutation UpdateNetworkPeer($input: UpdateNetworkPeerInput!) {
    updateNetworkPeer(input: $input) {
      ...NetworkPeerFields
    }
  }
`);

export const RevokeNetworkPeerMutation = graphql(`
  mutation RevokeNetworkPeer($id: ID!) {
    revokeNetworkPeer(id: $id) {
      ...NetworkPeerFields
    }
  }
`);

export const AcceptShareMutation = graphql(`
  mutation AcceptShare($link: String!) {
    acceptShare(link: $link) {
      ...ReceivedShareFields
    }
  }
`);

export const CopyReceivedShareMutation = graphql(`
  mutation CopyReceivedShare($input: CopyReceivedShareInput!) {
    copyReceivedShare(input: $input) {
      ...ReceivedShareFields
    }
  }
`);

export const DatabaseStatusFields = graphql(`
  fragment DatabaseStatusFields on DatabaseStatus {
    provider
    lockedByEnvironment
    location
    sizeBytes
    migration {
      phase
      tables {
        name
        rows
        copied
      }
      rows
      copied
      errorCode
      startedAt
      finishedAt
    }
    previousSqlite {
      path
      sizeBytes
      exists
      migratedAt
    }
  }
`);

export const DatabaseQuery = graphql(`
  query Database {
    database {
      ...DatabaseStatusFields
    }
  }
`);

export const TestPostgresConnectionMutation = graphql(`
  mutation TestPostgresConnection($connectionString: String!) {
    testPostgresConnection(connectionString: $connectionString) {
      success
      error
    }
  }
`);

export const MigrateToPostgresMutation = graphql(`
  mutation MigrateToPostgres($connectionString: String!) {
    migrateToPostgres(connectionString: $connectionString) {
      ...DatabaseStatusFields
    }
  }
`);

export const UltimateCloudStatusFields = graphql(`
  fragment UltimateCloudStatusFields on UltimateCloudStatus {
    state
    account
    ownerFirstName
    linkedAt
    pending {
      userCode
      verificationUri
      verificationUriComplete
      expiresAt
    }
    plan {
      name
      cloudStatus
      usedBytes
      limitBytes
      readOnly
      renewsAt
      checkedAt
    }
    tier
    capabilities
    errorCode
    libraries {
      libraryId
      name
      collection
    }
    audioTool {
      version
      errorCode
    }
    enabled
    services {
      personalPlays
      albumPlaythroughs
      starTracks
      metrics
    }
  }
`);

export const SetUltimateCloudServicesMutation = graphql(`
  mutation SetUltimateCloudServices($input: UltimateCloudServicesInput!) {
    setUltimateCloudServices(input: $input) {
      ...UltimateCloudStatusFields
    }
  }
`);

export const UltimateCloudAccountsQuery = graphql(`
  query UltimateCloudAccounts {
    ultimateCloudAccounts {
      id
      name
      ownerFirstName
      userId
    }
  }
`);

export const MyUltimateCloudStatusFields = graphql(`
  fragment MyUltimateCloudStatusFields on MyUltimateCloudStatus {
    enabled
    state
    account
    ownerFirstName
    linkedAt
    pending {
      userCode
      verificationUri
      verificationUriComplete
      expiresAt
    }
    plan {
      name
      cloudStatus
      readOnly
      checkedAt
    }
    tier
    errorCode
    libraryId
    services {
      personalPlays
      albumPlaythroughs
      starTracks
      metrics
    }
  }
`);

export const MyUltimateCloudQuery = graphql(`
  query MyUltimateCloud {
    myUltimateCloud {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const StartMyUltimateCloudLinkMutation = graphql(`
  mutation StartMyUltimateCloudLink($input: StartMyUltimateCloudLinkInput!) {
    startMyUltimateCloudLink(input: $input) {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const CancelMyUltimateCloudLinkMutation = graphql(`
  mutation CancelMyUltimateCloudLink {
    cancelMyUltimateCloudLink {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const UnlinkMyUltimateCloudMutation = graphql(`
  mutation UnlinkMyUltimateCloud {
    unlinkMyUltimateCloud {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const RefreshMyUltimateCloudMutation = graphql(`
  mutation RefreshMyUltimateCloud {
    refreshMyUltimateCloud {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const SetMyUltimateCloudServicesMutation = graphql(`
  mutation SetMyUltimateCloudServices($input: UltimateCloudServicesInput!) {
    setMyUltimateCloudServices(input: $input) {
      ...MyUltimateCloudStatusFields
    }
  }
`);

export const MyUltimateCloudMetricsQuery = graphql(`
  query MyUltimateCloudMetrics {
    myUltimateCloudMetrics {
      totalPlays
      totalStreams
      topSongAllTime {
        title
        artist
        playCount
      }
      topArtistAllTime {
        artist
        playCount
      }
    }
  }
`);

export const UltimateCloudMetricsQuery = graphql(`
  query UltimateCloudMetrics {
    ultimateCloudMetrics {
      totalPlays
      totalStreams
      topSongThisMonth {
        title
        artist
        playCount
      }
      topSongThisYear {
        title
        artist
        playCount
      }
      topSongAllTime {
        title
        artist
        playCount
      }
      topArtistThisMonth {
        artist
        playCount
      }
      topArtistThisYear {
        artist
        playCount
      }
      topArtistAllTime {
        artist
        playCount
      }
      topAlbumThisMonth {
        title
        artist
        playCount
      }
      topAlbumThisYear {
        title
        artist
        playCount
      }
      topAlbumAllTime {
        title
        artist
        playCount
      }
      topGenreThisMonth {
        genre
        playCount
      }
      topGenreThisYear {
        genre
        playCount
      }
      topGenreAllTime {
        genre
        playCount
      }
      topDecade {
        decadeStart
        decadeEnd
        playCount
      }
    }
  }
`);

export const UltimateCloudQuery = graphql(`
  query UltimateCloud {
    ultimateCloud {
      ...UltimateCloudStatusFields
    }
  }
`);

export const StartUltimateCloudLinkMutation = graphql(`
  mutation StartUltimateCloudLink {
    startUltimateCloudLink {
      ...UltimateCloudStatusFields
    }
  }
`);

export const CancelUltimateCloudLinkMutation = graphql(`
  mutation CancelUltimateCloudLink {
    cancelUltimateCloudLink {
      ...UltimateCloudStatusFields
    }
  }
`);

export const UnlinkUltimateCloudMutation = graphql(`
  mutation UnlinkUltimateCloud {
    unlinkUltimateCloud {
      ...UltimateCloudStatusFields
    }
  }
`);

export const AddUltimateCloudLibraryMutation = graphql(`
  mutation AddUltimateCloudLibrary($input: AddUltimateCloudLibraryInput!) {
    addUltimateCloudLibrary(input: $input) {
      ...UltimateCloudStatusFields
    }
  }
`);

export const CloudSyncStateFields = graphql(`
  fragment CloudSyncStateFields on LibraryCloudSyncState {
    libraryId
    mode
    accountUserId
    downloadSourceId
    lastRunAt
    requested
    running {
      phase
      done
      total
    }
    matched
    uploaded
    downloaded
    localOnly
    failed
    errorCode
    failures {
      title
      path
      reason
    }
  }
`);

export const CloudSyncsQuery = graphql(`
  query CloudSyncs {
    cloudSyncs {
      ...CloudSyncStateFields
    }
    ultimateCloudAccounts {
      id
      name
      ownerFirstName
      userId
    }
    serverInfo {
      capabilities
    }
  }
`);

export const SetLibraryCloudSyncMutation = graphql(`
  mutation SetLibraryCloudSync($libraryId: ID!, $input: SetLibraryCloudSyncInput!) {
    setLibraryCloudSync(libraryId: $libraryId, input: $input) {
      ...CloudSyncStateFields
    }
  }
`);

export const SyncLibraryWithCloudMutation = graphql(`
  mutation SyncLibraryWithCloud($libraryId: ID!) {
    syncLibraryWithCloud(libraryId: $libraryId) {
      ...CloudSyncStateFields
    }
  }
`);

export const TestS3ConnectionMutation = graphql(`
  mutation TestS3Connection($input: S3ConnectionInput!) {
    testS3Connection(input: $input) {
      success
      errorCode
      audioFiles
      truncated
    }
  }
`);

export const CreateS3LibraryMutation = graphql(`
  mutation CreateS3Library($input: CreateS3LibraryInput!) {
    createS3Library(input: $input) {
      id
    }
  }
`);

export const RefreshUltimateCloudMutation = graphql(`
  mutation RefreshUltimateCloud {
    refreshUltimateCloud {
      ...UltimateCloudStatusFields
    }
  }
`);

export const ResolvePreviousSqliteMutation = graphql(`
  mutation ResolvePreviousSqlite($input: ResolvePreviousSqliteInput!) {
    resolvePreviousSqlite(input: $input) {
      ...DatabaseStatusFields
    }
  }
`);

// Backups

export const BackupJobFields = graphql(`
  fragment BackupJobFields on BackupJob {
    id
    backupId
    backupName
    status
    trigger
    snapshot
    requestedAt
    startedAt
    finishedAt
    files
    copied
    unchanged
    versioned
    skipped
    failed
    bytes
    errorCode
    failures {
      path
      code
    }
  }
`);

export const BackupDestinationFields = graphql(`
  fragment BackupDestinationFields on BackupDestinationInfo {
    id
    name
    kind
    location
    backups
    createdAt
  }
`);

export const BackupFields = graphql(`
  fragment BackupFields on BackupDefinition {
    id
    name
    destination {
      id
      name
      kind
    }
    scope
    items {
      id
      name
    }
    includeUserData
    includeServer
    schedule
    intervalHours
    timeOfDay
    dayOfWeek
    timeZone
    retentionDays
    enabled
    folder
    createdAt
    nextRunAt
    lastRun {
      ...BackupJobFields
    }
  }
`);

export const RestoreJobFields = graphql(`
  fragment RestoreJobFields on RestoreJobInfo {
    id
    backupName
    snapshot
    status
    files
    userData
    filesTotal
    restored
    identical
    renamed
    filesFailed
    ratings
    playCounts
    plays
    playlists
    unmatched
    requestedAt
    finishedAt
    errorCode
    failures {
      path
      code
    }
  }
`);

export const BackupGuestFields = graphql(`
  fragment BackupGuestFields on BackupGuest {
    peerId
    name
    folder
    usedBytes
    quotaBytes
    revoked
    lastSeenAt
  }
`);

export const BackupsPageQuery = graphql(`
  query BackupsPage {
    backupDestinations {
      ...BackupDestinationFields
    }
    backups {
      ...BackupFields
    }
    restoreJobs {
      ...RestoreJobFields
    }
    backupGuests {
      ...BackupGuestFields
    }
    serverInfo {
      capabilities
    }
  }
`);

export const BackupsOverviewQuery = graphql(`
  query BackupsOverview {
    backups {
      id
      name
      enabled
      schedule
      nextRunAt
      lastRun {
        id
        status
        finishedAt
        requestedAt
      }
    }
  }
`);

/** What a backup can cover, and this server's users (to give restored data to). */
export const BackupOptionsQuery = graphql(`
  query BackupOptions {
    libraries {
      id
      name
      disabled
      personal
      sources {
        id
        kind
        location
      }
    }
    playlists {
      id
      name
      trackCount
      owner {
        id
        name
      }
    }
    users {
      id
      userName
      displayName
    }
  }
`);

export const BackupAlbumSearchQuery = graphql(`
  query BackupAlbumSearch($search: String!) {
    albums(search: $search, first: 20) {
      items {
        id
        title
        year
        artist {
          name
        }
      }
    }
  }
`);

export const BackupRunsQuery = graphql(`
  query BackupRuns($backupId: ID!) {
    backupRuns(backupId: $backupId) {
      ...BackupJobFields
    }
  }
`);

export const BackupJobUpdatedSubscription = graphql(`
  subscription BackupJobUpdated {
    backupJobUpdated {
      ...BackupJobFields
    }
  }
`);

export const RestoreJobUpdatedSubscription = graphql(`
  subscription RestoreJobUpdated {
    restoreJobUpdated {
      ...RestoreJobFields
    }
  }
`);

export const CreateBackupDestinationMutation = graphql(`
  mutation CreateBackupDestination($input: CreateBackupDestinationInput!) {
    createBackupDestination(input: $input) {
      ...BackupDestinationFields
    }
  }
`);

export const TestBackupDestinationMutation = graphql(`
  mutation TestBackupDestination($id: ID!) {
    testBackupDestination(id: $id) {
      success
      errorCode
    }
  }
`);

export const DeleteBackupDestinationMutation = graphql(`
  mutation DeleteBackupDestination($id: ID!) {
    deleteBackupDestination(id: $id)
  }
`);

export const CreateBackupMutation = graphql(`
  mutation CreateBackup($input: CreateBackupInput!) {
    createBackup(input: $input) {
      ...BackupFields
    }
  }
`);

export const UpdateBackupMutation = graphql(`
  mutation UpdateBackup($id: ID!, $input: UpdateBackupInput!) {
    updateBackup(id: $id, input: $input) {
      ...BackupFields
    }
  }
`);

export const DeleteBackupMutation = graphql(`
  mutation DeleteBackup($id: ID!) {
    deleteBackup(id: $id)
  }
`);

export const RunBackupMutation = graphql(`
  mutation RunBackup($id: ID!) {
    runBackup(id: $id) {
      ...BackupJobFields
    }
  }
`);

export const CancelBackupRunMutation = graphql(`
  mutation CancelBackupRun($id: ID!) {
    cancelBackupRun(id: $id) {
      ...BackupJobFields
    }
  }
`);

export const CreateBackupInvitationMutation = graphql(`
  mutation CreateBackupInvitation($input: CreateBackupInvitationInput!) {
    createBackupInvitation(input: $input) {
      link
      expiresAt
    }
  }
`);

export const StoredBackupsQuery = graphql(`
  query StoredBackups($destinationId: ID!) {
    storedBackups(destinationId: $destinationId) {
      folder
      id
      name
      scope
      machine
      arcanistVersion
      updatedAt
      snapshots {
        name
        createdAt
        files
        bytes
        userData
        server
      }
      libraries {
        folder
        name
        sourceId
      }
    }
  }
`);

export const BackupSnapshotContentsQuery = graphql(`
  query BackupSnapshotContents($destinationId: ID!, $folder: String!, $snapshot: String!) {
    backupSnapshotContents(destinationId: $destinationId, folder: $folder, snapshot: $snapshot) {
      libraries {
        folder
        name
        files
        bytes
      }
      albums {
        id
        title
        artist
        year
        paths
      }
      playlists {
        owner
        name
        tracks
        paths
      }
      users {
        userName
        displayName
        ratings
        plays
        playlists
        userId
      }
    }
  }
`);

export const StartRestoreMutation = graphql(`
  mutation StartRestore($input: StartRestoreInput!) {
    startRestore(input: $input) {
      ...RestoreJobFields
    }
  }
`);

export const CancelRestoreMutation = graphql(`
  mutation CancelRestore($id: ID!) {
    cancelRestore(id: $id) {
      ...RestoreJobFields
    }
  }
`);

export const ServerAudioStatusFields = graphql(`
  fragment ServerAudioStatusFields on ServerAudioStatus {
    enabled
    available
    unavailableReason
    devices {
      id
      name
      isDefault
      sampleRate
      channels
    }
    deviceId
    device
    deviceError
    name
    customName
    volume
    muted
    connectAccounts
  }
`);

export const ServerAudioQuery = graphql(`
  query ServerAudio {
    serverAudio {
      ...ServerAudioStatusFields
    }
  }
`);

export const SetServerAudioSettingsMutation = graphql(`
  mutation SetServerAudioSettings($input: ServerAudioSettingsInput!) {
    setServerAudioSettings(input: $input) {
      ...ServerAudioStatusFields
    }
  }
`);

export const TestServerAudioMutation = graphql(`
  mutation TestServerAudio {
    testServerAudio
  }
`);

export const StorageUsageQuery = graphql(`
  query StorageUsage($refresh: Boolean!) {
    storageUsage(refresh: $refresh) {
      dataDirectory
      arcanistBytes
      musicBytes
      measuredAt
      parts {
        kind
        bytes
      }
      disk {
        totalBytes
        freeBytes
      }
      database {
        provider
        version
        sizeBytes
        tables {
          name
          rows
        }
      }
      catalog {
        libraries
        tracks
        albums
        artists
        files
        missingFiles
        playlists
        users
        plays
        ratings
      }
      libraries {
        id
        name
        kind
        files
        bytes
      }
    }
  }
`);

export const AiStatusFields = graphql(`
  fragment AiStatusFields on AiStatus {
    enabled
    activity
    songsPerMinute
    lastAnalyzedAt
    error
    maxThreads
    progress {
      analysisVersion
      analyzed
      pending
      failed
    }
    settings {
      threads
      pauseWhileScanning
      pauseWhileStreaming
      onDemandSources
      shareServerStats
    }
  }
`);

export const AiStatusQuery = graphql(`
  query AiStatus {
    aiStatus {
      ...AiStatusFields
    }
  }
`);

export const SetAiSettingsMutation = graphql(`
  mutation SetAiSettings($input: SetAiSettingsInput!) {
    setAiSettings(input: $input) {
      ...AiStatusFields
    }
  }
`);

export const ReanalyzeTracksMutation = graphql(`
  mutation ReanalyzeTracks {
    reanalyzeTracks {
      ...AiStatusFields
    }
  }
`);

export const AiTrackSearchQuery = graphql(`
  query AiTrackSearch($search: String!) {
    tracks(search: $search, first: 8) {
      items {
        id
        title
        durationMs
        artists {
          name
          role
        }
        album {
          title
        }
      }
    }
  }
`);

export const TrackAnalysisFields = graphql(`
  fragment TrackAnalysisFields on TrackAnalysisResult {
    trackId
    status
    stale
    position
    errorCode
    analysisVersion
    analyzedAt
    durationSeconds
    musicStart
    musicEnd
    tempo {
      bpm
      confidence
      stable
      beatsPerBar
      firstBeat
      firstDownbeat
      beats
      downbeats
    }
    loudness {
      integratedLufs
      range
      truePeakDb
    }
    key {
      tonic
      mode
      camelot
      confidence
    }
    energy {
      stepSeconds
      values
    }
    vocals {
      stepSeconds
      presence
    }
    silences {
      start
      end
      kind
    }
    fades {
      fadeInEnd
      fadeOutStart
    }
    sections {
      start
      end
      label
      name
      energy
      trend
      occurrence
    }
    highlight {
      start
      end
      confidence
    }
    mixPoints {
      introEnd
      outroStart
      mixIn
      mixOut
    }
    scratchCues {
      t
      duration
      kind
      isolation
      strength
    }
  }
`);

export const TrackAnalysisQuery = graphql(`
  query TrackAnalysis($trackId: ID!) {
    trackAnalysis(
      trackId: $trackId
      include: "beats,energy,vocals,sections,silences,cues,loudness,key"
    ) {
      ...TrackAnalysisFields
    }
  }
`);

export const PrioritizeTrackAnalysisMutation = graphql(`
  mutation PrioritizeTrackAnalysis($trackId: ID!) {
    prioritizeTrackAnalysis(trackId: $trackId) {
      ...TrackAnalysisFields
    }
  }
`);
