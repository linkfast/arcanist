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
    mode
    kind
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
    addresses
    convertsFormats
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
  }
`);

export const SetDlnaSettingsMutation = graphql(`
  mutation SetDlnaSettings($input: DlnaSettingsInput!) {
    setDlnaSettings(input: $input) {
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
