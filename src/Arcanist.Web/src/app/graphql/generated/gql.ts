/* eslint-disable */
import * as types from './graphql';
import type { TypedDocumentNode as DocumentNode } from '@graphql-typed-document-node/core';

/**
 * Map of all GraphQL operations in the project.
 *
 * This map has several performance disadvantages:
 * 1. It is not tree-shakeable, so it will include all operations in the project.
 * 2. It is not minifiable, so the string of a GraphQL query will be multiple times inside the bundle.
 * 3. It does not support dead code elimination, so it will add unused operations.
 *
 * Therefore it is highly recommended to use the babel or swc plugin for production.
 * Learn more about it here: https://the-guild.dev/graphql/codegen/plugins/presets/preset-client#reducing-bundle-size
 */
type Documents = {
    "\n  fragment ScanJobFields on ScanJob {\n    id\n    libraryId\n    kind\n    status\n    requestedAt\n    startedAt\n    finishedAt\n    filesSeen\n    filesAdded\n    filesUpdated\n    filesMissing\n    errors\n    errorCode\n    progress {\n      phase\n      filesFound\n      filesToRead\n      filesRead\n    }\n  }\n": typeof types.ScanJobFieldsFragmentDoc,
    "\n  query ServerInfo {\n    serverInfo {\n      name\n      version\n      apiVersion\n      sourceUrl\n    }\n  }\n": typeof types.ServerInfoDocument,
    "\n  query Overview {\n    libraries {\n      id\n    }\n    artists(first: 1) {\n      totalCount\n    }\n    albums(first: 1) {\n      totalCount\n    }\n    tracks(first: 1) {\n      totalCount\n    }\n  }\n": typeof types.OverviewDocument,
    "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalMinutes\n      watchForChanges\n      createdAt\n      lastScanAt\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": typeof types.LibrariesDocument,
    "\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n": typeof types.CreateLibraryDocument,
    "\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n": typeof types.UpdateLibraryDocument,
    "\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n": typeof types.DeleteLibraryDocument,
    "\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n": typeof types.AddLibrarySourceDocument,
    "\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n": typeof types.RemoveLibrarySourceDocument,
    "\n  fragment LibrarySourceMoveFields on LibrarySourceMove {\n    sourceId\n    libraryId\n    from\n    to\n    keepOriginals\n    phase\n    filesDone\n    filesTotal\n    bytesDone\n    bytesTotal\n    startedAt\n    finishedAt\n    errorCode\n  }\n": typeof types.LibrarySourceMoveFieldsFragmentDoc,
    "\n  query LibrarySourceMoves {\n    librarySourceMoves {\n      ...LibrarySourceMoveFields\n    }\n  }\n": typeof types.LibrarySourceMovesDocument,
    "\n  mutation MoveLibrarySource($id: ID!, $input: MoveLibrarySourceInput!) {\n    moveLibrarySource(id: $id, input: $input) {\n      ...LibrarySourceMoveFields\n    }\n  }\n": typeof types.MoveLibrarySourceDocument,
    "\n  query ServerDirectories($path: String) {\n    serverDirectories(path: $path) {\n      path\n      parent\n      directories {\n        name\n        path\n        isHidden\n      }\n    }\n  }\n": typeof types.ServerDirectoriesDocument,
    "\n  mutation CreateServerDirectory($input: CreateServerDirectoryInput!) {\n    createServerDirectory(input: $input) {\n      name\n      path\n      isHidden\n    }\n  }\n": typeof types.CreateServerDirectoryDocument,
    "\n  query ScanJobs($libraryId: ID, $limit: Int) {\n    scanJobs(libraryId: $libraryId, limit: $limit) {\n      ...ScanJobFields\n    }\n  }\n": typeof types.ScanJobsDocument,
    "\n  mutation StartScan($libraryId: ID!, $full: Boolean!) {\n    startScan(libraryId: $libraryId, full: $full) {\n      ...ScanJobFields\n    }\n  }\n": typeof types.StartScanDocument,
    "\n  mutation CancelScan($id: ID!) {\n    cancelScan(id: $id)\n  }\n": typeof types.CancelScanDocument,
    "\n  subscription ScanJobUpdated {\n    scanJobUpdated {\n      ...ScanJobFields\n    }\n  }\n": typeof types.ScanJobUpdatedDocument,
    "\n  fragment UserFields on UserAccount {\n    id\n    userName\n    displayName\n    role\n    permissions\n    isDisabled\n    createdAt\n    lastLoginAt\n    libraryIds\n  }\n": typeof types.UserFieldsFragmentDoc,
    "\n  query Users {\n    users {\n      ...UserFields\n    }\n    libraries {\n      id\n      name\n      visibleToAll\n    }\n  }\n": typeof types.UsersDocument,
    "\n  mutation CreateUser($input: CreateUserInput!) {\n    createUser(input: $input) {\n      id\n    }\n  }\n": typeof types.CreateUserDocument,
    "\n  mutation UpdateUser($id: ID!, $input: UpdateUserInput!) {\n    updateUser(id: $id, input: $input) {\n      id\n    }\n  }\n": typeof types.UpdateUserDocument,
    "\n  mutation SetUserLibraries($id: ID!, $input: SetUserLibrariesInput!) {\n    setUserLibraries(id: $id, input: $input) {\n      id\n    }\n  }\n": typeof types.SetUserLibrariesDocument,
    "\n  mutation ResetUserPassword($id: ID!, $input: ResetUserPasswordInput!) {\n    resetUserPassword(id: $id, input: $input)\n  }\n": typeof types.ResetUserPasswordDocument,
    "\n  mutation DeleteUser($id: ID!) {\n    deleteUser(id: $id)\n  }\n": typeof types.DeleteUserDocument,
    "\n  query MySessions {\n    mySessions {\n      id\n      deviceName\n      userAgent\n      ipAddress\n      createdAt\n      lastSeenAt\n      expiresAt\n      isCurrent\n    }\n  }\n": typeof types.MySessionsDocument,
    "\n  mutation RevokeMySession($id: ID!) {\n    revokeMySession(id: $id)\n  }\n": typeof types.RevokeMySessionDocument,
    "\n  mutation ChangeMyPassword($input: ChangeMyPasswordInput!) {\n    changeMyPassword(input: $input)\n  }\n": typeof types.ChangeMyPasswordDocument,
    "\n  fragment AnalysisProgressFields on AnalysisProgress {\n    activity\n    enabled\n    pending\n    analyzed\n    needsFfmpeg\n    failed\n    filesPerMinute\n    lastAnalyzedAt\n    error\n  }\n": typeof types.AnalysisProgressFieldsFragmentDoc,
    "\n  query AnalysisProgress {\n    analysisProgress {\n      ...AnalysisProgressFields\n    }\n  }\n": typeof types.AnalysisProgressDocument,
    "\n  mutation SetAnalysisEnabled($enabled: Boolean!) {\n    setAnalysisEnabled(enabled: $enabled) {\n      ...AnalysisProgressFields\n    }\n  }\n": typeof types.SetAnalysisEnabledDocument,
    "\n  subscription AnalysisProgressChanged {\n    analysisProgressChanged {\n      ...AnalysisProgressFields\n    }\n  }\n": typeof types.AnalysisProgressChangedDocument,
    "\n  fragment FfmpegStatusFields on FfmpegStatus {\n    available\n    path\n    version\n    source\n    configuredPath\n    configuredPathWorks\n    canDownload\n    download {\n      state\n      bytesReceived\n      totalBytes\n      version\n      error\n    }\n  }\n": typeof types.FfmpegStatusFieldsFragmentDoc,
    "\n  fragment ServerConfigurationFields on ServerConfiguration {\n    defaultLanguage\n    ratingMode\n    shareAnonymousStatistics\n    ffmpegPath\n    onlineServices {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    privacy {\n      noTelemetry\n      externalServices\n    }\n  }\n": typeof types.ServerConfigurationFieldsFragmentDoc,
    "\n  query Privacy {\n    privacy {\n      noTelemetry\n      externalServices\n    }\n    anonymousStatistics\n  }\n": typeof types.PrivacyDocument,
    "\n  mutation SetPrivacy($input: SetPrivacyInput!) {\n    setPrivacy(input: $input) {\n      noTelemetry\n      externalServices\n    }\n  }\n": typeof types.SetPrivacyDocument,
    "\n  query SetupExtras {\n    users {\n      id\n      userName\n      displayName\n      role\n    }\n    p2pStatus {\n      enabled\n      customName\n      relay\n      relayUrl\n      bandwidthKbps\n      maxStreams\n    }\n    extensions {\n      id\n      builtIn\n      enabled\n    }\n  }\n": typeof types.SetupExtrasDocument,
    "\n  query ServerConfiguration {\n    serverConfiguration {\n      ...ServerConfigurationFields\n    }\n  }\n": typeof types.ServerConfigurationDocument,
    "\n  mutation ReconfigureServer($input: ReconfigureServerInput!) {\n    reconfigureServer(input: $input) {\n      ...ServerConfigurationFields\n    }\n  }\n": typeof types.ReconfigureServerDocument,
    "\n  mutation SetAnonymousStatistics($enabled: Boolean!) {\n    setAnonymousStatistics(enabled: $enabled)\n  }\n": typeof types.SetAnonymousStatisticsDocument,
    "\n  mutation ResetSettings {\n    resetSettings {\n      restartRequired\n    }\n  }\n": typeof types.ResetSettingsDocument,
    "\n  mutation ResetUserData {\n    resetUserData {\n      restartRequired\n    }\n  }\n": typeof types.ResetUserDataDocument,
    "\n  mutation FactoryReset($input: FactoryResetInput!) {\n    factoryReset(input: $input) {\n      restartRequired\n    }\n  }\n": typeof types.FactoryResetDocument,
    "\n  query Ffmpeg {\n    ffmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n": typeof types.FfmpegDocument,
    "\n  mutation SetFfmpegPath($path: String) {\n    setFfmpegPath(path: $path) {\n      ...FfmpegStatusFields\n    }\n  }\n": typeof types.SetFfmpegPathDocument,
    "\n  mutation InstallFfmpeg {\n    installFfmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n": typeof types.InstallFfmpegDocument,
    "\n  fragment DuplicateTrackFields on Track {\n    id\n    title\n    durationMs\n    trackNumber\n    discNumber\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n    files {\n      id\n      format\n      bitRate\n      sampleRate\n      bitDepth\n    }\n  }\n": typeof types.DuplicateTrackFieldsFragmentDoc,
    "\n  query DuplicateCandidates($status: DuplicateStatus) {\n    duplicateCandidates(status: $status, first: 200) {\n      id\n      similarity\n      status\n      detectedAt\n      trackA {\n        ...DuplicateTrackFields\n      }\n      trackB {\n        ...DuplicateTrackFields\n      }\n    }\n  }\n": typeof types.DuplicateCandidatesDocument,
    "\n  mutation MergeDuplicate($id: ID!, $keepTrackId: ID!) {\n    mergeDuplicate(id: $id, keepTrackId: $keepTrackId) {\n      id\n    }\n  }\n": typeof types.MergeDuplicateDocument,
    "\n  mutation DismissDuplicate($id: ID!) {\n    dismissDuplicate(id: $id)\n  }\n": typeof types.DismissDuplicateDocument,
    "\n  fragment OnlineServicesFields on OnlineServicesStatus {\n    settings {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    hasAcoustIdKey\n    hasOwnAcoustIdKey\n    services {\n      service\n      enabled\n      ready\n      pending\n      found\n      notFound\n      failed\n      lastRequestAt\n      lastError\n    }\n  }\n": typeof types.OnlineServicesFieldsFragmentDoc,
    "\n  query OnlineServices {\n    onlineServices {\n      ...OnlineServicesFields\n    }\n  }\n": typeof types.OnlineServicesDocument,
    "\n  mutation SetOnlineServices($input: SetOnlineServicesInput!) {\n    setOnlineServices(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n": typeof types.SetOnlineServicesDocument,
    "\n  mutation SetAcoustIdKey($input: SetAcoustIdKeyInput!) {\n    setAcoustIdKey(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n": typeof types.SetAcoustIdKeyDocument,
    "\n  fragment OrganizeRunFields on OrganizeRun {\n    id\n    libraryId\n    kind\n    status\n    template\n    startedAt\n    finishedAt\n    undoneAt\n    total\n    moved\n    failed\n    errorCode\n  }\n": typeof types.OrganizeRunFieldsFragmentDoc,
    "\n  query OrganizePreview($libraryId: ID!, $template: String) {\n    organizePreview(libraryId: $libraryId, template: $template) {\n      template\n      filesToMove\n      filesInPlace\n      otherFilesToMove\n      unavailableSourceIds\n      moves {\n        sourceId\n        from\n        to\n      }\n    }\n  }\n": typeof types.OrganizePreviewDocument,
    "\n  query OrganizeRuns($libraryId: ID!) {\n    organizeRuns(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n": typeof types.OrganizeRunsDocument,
    "\n  mutation UpdateLibraryOrganization($libraryId: ID!, $input: UpdateLibraryOrganizationInput!) {\n    updateLibraryOrganization(libraryId: $libraryId, input: $input) {\n      id\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": typeof types.UpdateLibraryOrganizationDocument,
    "\n  mutation OrganizeLibrary($libraryId: ID!) {\n    organizeLibrary(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n": typeof types.OrganizeLibraryDocument,
    "\n  mutation UndoOrganizeRun($id: ID!) {\n    undoOrganizeRun(id: $id) {\n      ...OrganizeRunFields\n    }\n  }\n": typeof types.UndoOrganizeRunDocument,
    "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    mode\n    kind\n    uploadToCloud\n    uploadAccount\n    createdAt\n    available\n  }\n": typeof types.DropFolderFieldsFragmentDoc,
    "\n  query DropFolders {\n    dropFolders {\n      ...DropFolderFields\n    }\n  }\n": typeof types.DropFoldersDocument,
    "\n  mutation CreateDropFolder($input: DropFolderInput!) {\n    createDropFolder(input: $input) {\n      ...DropFolderFields\n    }\n  }\n": typeof types.CreateDropFolderDocument,
    "\n  mutation UpdateDropFolder($id: ID!, $input: DropFolderInput!) {\n    updateDropFolder(id: $id, input: $input) {\n      ...DropFolderFields\n    }\n  }\n": typeof types.UpdateDropFolderDocument,
    "\n  mutation DeleteDropFolder($id: ID!) {\n    deleteDropFolder(id: $id)\n  }\n": typeof types.DeleteDropFolderDocument,
    "\n  fragment ImportItemFields on ImportItem {\n    id\n    dropFolderId\n    path\n    size\n    status\n    reason\n    destinationPath\n    trackId\n    createdAt\n    updatedAt\n    details {\n      title\n      artist\n      album\n      albumArtist\n      year\n      trackNumber\n      discNumber\n      genre\n      format\n      bitRate\n      sampleRate\n      bitDepth\n      durationMs\n    }\n  }\n": typeof types.ImportItemFieldsFragmentDoc,
    "\n  query ImportQueue($status: ImportStatus) {\n    importSummary {\n      pending\n      needsReview\n      duplicate\n      failed\n      ignored\n      imported\n    }\n    importItems(status: $status, first: 200) {\n      ...ImportItemFields\n    }\n  }\n": typeof types.ImportQueueDocument,
    "\n  mutation AcceptImportItem($id: ID!, $tags: ImportTagsInput) {\n    acceptImportItem(id: $id, tags: $tags) {\n      ...ImportItemFields\n    }\n  }\n": typeof types.AcceptImportItemDocument,
    "\n  mutation RetryImportItem($id: ID!) {\n    retryImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": typeof types.RetryImportItemDocument,
    "\n  mutation IgnoreImportItem($id: ID!) {\n    ignoreImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": typeof types.IgnoreImportItemDocument,
    "\n  mutation DiscardImportItem($id: ID!) {\n    discardImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": typeof types.DiscardImportItemDocument,
    "\n  query RatingMode {\n    serverInfo {\n      ratingMode\n    }\n  }\n": typeof types.RatingModeDocument,
    "\n  mutation SetRatingMode($mode: RatingMode!) {\n    setRatingMode(mode: $mode)\n  }\n": typeof types.SetRatingModeDocument,
    "\n  fragment StatsTrackFields on Track {\n    id\n    title\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n  }\n": typeof types.StatsTrackFieldsFragmentDoc,
    "\n  query ServerStats($period: StatsPeriod!, $timeZone: String) {\n    serverStats(period: $period, timeZone: $timeZone) {\n      library {\n        artists\n        albums\n        tracks\n        files\n        durationMs\n        sizeBytes\n        formats {\n          format\n          files\n          sizeBytes\n        }\n      }\n      listening {\n        period\n        from\n        plays\n        listenedMs\n        distinctTracks\n        bucket\n        timeline {\n          start\n          plays\n          listenedMs\n        }\n        topTracks {\n          plays\n          track {\n            ...StatsTrackFields\n          }\n        }\n        topArtists {\n          plays\n          artist {\n            id\n            name\n          }\n        }\n        topAlbums {\n          plays\n          album {\n            id\n            title\n            artworkId\n            artist {\n              id\n              name\n            }\n          }\n        }\n      }\n      users {\n        userId\n        userName\n        displayName\n        plays\n        listenedMs\n        lastPlayedAt\n      }\n    }\n  }\n": typeof types.ServerStatsDocument,
    "\n  fragment AppPasswordFields on AppPassword {\n    id\n    name\n    createdAt\n    lastUsedAt\n  }\n": typeof types.AppPasswordFieldsFragmentDoc,
    "\n  query AppPasswords {\n    appPasswords {\n      ...AppPasswordFields\n    }\n  }\n": typeof types.AppPasswordsDocument,
    "\n  mutation CreateAppPassword($name: String!) {\n    createAppPassword(name: $name) {\n      secret\n      appPassword {\n        ...AppPasswordFields\n      }\n    }\n  }\n": typeof types.CreateAppPasswordDocument,
    "\n  mutation DeleteAppPassword($id: ID!) {\n    deleteAppPassword(id: $id)\n  }\n": typeof types.DeleteAppPasswordDocument,
    "\n  fragment FrontendFields on Frontend {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    isDefault\n    path\n    installedAt\n  }\n": typeof types.FrontendFieldsFragmentDoc,
    "\n  query Frontends {\n    frontends {\n      ...FrontendFields\n    }\n  }\n": typeof types.FrontendsDocument,
    "\n  mutation InstallFrontendFromUrl($url: String!) {\n    installFrontendFromUrl(url: $url) {\n      ...FrontendFields\n    }\n  }\n": typeof types.InstallFrontendFromUrlDocument,
    "\n  mutation SetDefaultFrontend($id: String!) {\n    setDefaultFrontend(id: $id) {\n      ...FrontendFields\n    }\n  }\n": typeof types.SetDefaultFrontendDocument,
    "\n  mutation UninstallFrontend($id: String!) {\n    uninstallFrontend(id: $id)\n  }\n": typeof types.UninstallFrontendDocument,
    "\n  fragment ExtensionFields on Extension {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    enabled\n    status\n    errorCode\n    restartRequired\n    routes\n    basePath\n  }\n": typeof types.ExtensionFieldsFragmentDoc,
    "\n  query Extensions {\n    extensions {\n      ...ExtensionFields\n    }\n  }\n": typeof types.ExtensionsDocument,
    "\n  mutation InstallExtensionFromUrl($url: String!) {\n    installExtensionFromUrl(url: $url) {\n      ...ExtensionFields\n    }\n  }\n": typeof types.InstallExtensionFromUrlDocument,
    "\n  mutation SetExtensionEnabled($id: String!, $enabled: Boolean!) {\n    setExtensionEnabled(id: $id, enabled: $enabled) {\n      ...ExtensionFields\n    }\n  }\n": typeof types.SetExtensionEnabledDocument,
    "\n  mutation UninstallExtension($id: String!) {\n    uninstallExtension(id: $id)\n  }\n": typeof types.UninstallExtensionDocument,
    "\n  fragment MstreamImportJobFields on MstreamImportJob {\n    id\n    kind\n    source\n    status\n    createdAt\n    finishedAt\n    librariesCreated\n    usersCreated\n    playlists\n    ratings\n    plays\n    unmatched\n    errorCode\n  }\n": typeof types.MstreamImportJobFieldsFragmentDoc,
    "\n  fragment MstreamAnalysisFields on MstreamAnalysis {\n    version\n    libraries {\n      vpath\n      root\n      rootExists\n      sourceId\n    }\n    users {\n      userName\n      isAdmin\n      vpaths\n      exists\n      canSignIn\n    }\n    playlists\n    ratedTracks\n    playedTracks\n  }\n": typeof types.MstreamAnalysisFieldsFragmentDoc,
    "\n  query MstreamImports {\n    mstreamImports {\n      ...MstreamImportJobFields\n    }\n  }\n": typeof types.MstreamImportsDocument,
    "\n  fragment DlnaImportJobFields on DlnaImportJob {\n    id\n    serverName\n    folders\n    libraryId\n    libraryName\n    status\n    found\n    copied\n    duplicates\n    failed\n    copiedBytes\n    createdAt\n    finishedAt\n    errorCode\n    failures {\n      title\n      artist\n      album\n      reason\n    }\n  }\n": typeof types.DlnaImportJobFieldsFragmentDoc,
    "\n  query DlnaImports {\n    dlnaImports {\n      ...DlnaImportJobFields\n    }\n  }\n": typeof types.DlnaImportsDocument,
    "\n  query DlnaMediaServers {\n    dlnaMediaServers {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n": typeof types.DlnaMediaServersDocument,
    "\n  query DlnaMediaServer($location: String!) {\n    dlnaMediaServer(location: $location) {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n": typeof types.DlnaMediaServerDocument,
    "\n  query BrowseDlnaMediaServer($location: String!, $folderId: String) {\n    browseDlnaMediaServer(location: $location, folderId: $folderId) {\n      id\n      tracks\n      truncated\n      folders {\n        id\n        title\n        childCount\n      }\n    }\n  }\n": typeof types.BrowseDlnaMediaServerDocument,
    "\n  mutation StartDlnaImport($input: StartDlnaImportInput!) {\n    startDlnaImport(input: $input) {\n      ...DlnaImportJobFields\n    }\n  }\n": typeof types.StartDlnaImportDocument,
    "\n  mutation DeleteDlnaImport($id: ID!) {\n    deleteDlnaImport(id: $id)\n  }\n": typeof types.DeleteDlnaImportDocument,
    "\n  mutation DeleteMstreamImport($id: ID!) {\n    deleteMstreamImport(id: $id)\n  }\n": typeof types.DeleteMstreamImportDocument,
    "\n  mutation ClearImportHistory {\n    clearImportHistory\n  }\n": typeof types.ClearImportHistoryDocument,
    "\n  mutation CancelDlnaImport($id: ID!) {\n    cancelDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n": typeof types.CancelDlnaImportDocument,
    "\n  mutation RetryDlnaImport($id: ID!) {\n    retryDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n": typeof types.RetryDlnaImportDocument,
    "\n  mutation AnalyzeMstreamFolder($folder: String!) {\n    analyzeMstreamFolder(folder: $folder) {\n      ...MstreamAnalysisFields\n    }\n  }\n": typeof types.AnalyzeMstreamFolderDocument,
    "\n  mutation AnalyzeMstreamServer($input: MstreamServerInput!) {\n    analyzeMstreamServer(input: $input) {\n      ...MstreamAnalysisFields\n    }\n  }\n": typeof types.AnalyzeMstreamServerDocument,
    "\n  mutation ImportMstream($input: ImportMstreamInput!) {\n    importMstream(input: $input) {\n      ...MstreamImportJobFields\n    }\n  }\n": typeof types.ImportMstreamDocument,
    "\n  fragment DlnaStatusFields on DlnaStatus {\n    enabled\n    name\n    friendlyName\n    libraryIds\n    sections\n    ratingsUserId\n    addresses\n    convertsFormats\n  }\n": typeof types.DlnaStatusFieldsFragmentDoc,
    "\n  fragment MstreamStatusFields on MstreamStatus {\n    enabled\n    starThreshold\n    passwordSignIn\n    features\n    quickConnect\n    p2pEnabled\n    pairingCode\n  }\n": typeof types.MstreamStatusFieldsFragmentDoc,
    "\n  query Mstream {\n    mstream {\n      ...MstreamStatusFields\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n": typeof types.MstreamDocument,
    "\n  mutation SetMstreamSettings($input: MstreamSettingsInput!) {\n    setMstreamSettings(input: $input) {\n      ...MstreamStatusFields\n    }\n  }\n": typeof types.SetMstreamSettingsDocument,
    "\n  mutation RotateMstreamQuickConnect {\n    rotateMstreamQuickConnect {\n      ...MstreamStatusFields\n    }\n  }\n": typeof types.RotateMstreamQuickConnectDocument,
    "\n  query Dlna {\n    dlna {\n      ...DlnaStatusFields\n    }\n    libraries {\n      id\n      name\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n": typeof types.DlnaDocument,
    "\n  mutation SetDlnaSettings($input: DlnaSettingsInput!) {\n    setDlnaSettings(input: $input) {\n      ...DlnaStatusFields\n    }\n  }\n": typeof types.SetDlnaSettingsDocument,
    "\n  fragment DlnaRendererFields on DlnaRenderer {\n    id\n    name\n    manufacturer\n    model\n    address\n    manual\n  }\n": typeof types.DlnaRendererFieldsFragmentDoc,
    "\n  query DlnaRenderers {\n    dlnaRenderers {\n      ...DlnaRendererFields\n    }\n  }\n": typeof types.DlnaRenderersDocument,
    "\n  fragment DlnaRendererStateFields on DlnaRendererState {\n    renderer {\n      ...DlnaRendererFields\n    }\n    state\n    positionMs\n    durationMs\n    volume\n    queue\n    queueIndex\n    track {\n      id\n      title\n      artworkId\n      album {\n        title\n      }\n      artists {\n        name\n        role\n      }\n    }\n  }\n": typeof types.DlnaRendererStateFieldsFragmentDoc,
    "\n  query DlnaRenderer($id: String!) {\n    dlnaRenderer(id: $id) {\n      ...DlnaRendererStateFields\n    }\n  }\n": typeof types.DlnaRendererDocument,
    "\n  mutation AddDlnaRenderer($url: String!) {\n    addDlnaRenderer(url: $url) {\n      ...DlnaRendererFields\n    }\n  }\n": typeof types.AddDlnaRendererDocument,
    "\n  mutation PlayOnDlnaRenderer($input: PlayOnDlnaRendererInput!) {\n    playOnDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n": typeof types.PlayOnDlnaRendererDocument,
    "\n  mutation ControlDlnaRenderer($input: ControlDlnaRendererInput!) {\n    controlDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n": typeof types.ControlDlnaRendererDocument,
    "\n  mutation SetDlnaRendererVolume($rendererId: String!, $volume: Int!) {\n    setDlnaRendererVolume(rendererId: $rendererId, volume: $volume) {\n      ...DlnaRendererStateFields\n    }\n  }\n": typeof types.SetDlnaRendererVolumeDocument,
    "\n  query DlnaAlbumSearch($search: String!) {\n    albums(search: $search, first: 8) {\n      items {\n        id\n        title\n        artist {\n          name\n        }\n      }\n    }\n  }\n": typeof types.DlnaAlbumSearchDocument,
    "\n  query DlnaAlbumTracks($albumId: ID!) {\n    tracks(albumId: $albumId) {\n      items {\n        id\n      }\n    }\n  }\n": typeof types.DlnaAlbumTracksDocument,
    "\n  fragment P2PStatusFields on P2PStatus {\n    enabled\n    available\n    running\n    errorCode\n    nodeId\n    name\n    customName\n    relay\n    relayUrl\n    homeRelay\n    relayConnected\n    bandwidthKbps\n    maxStreams\n    connectedPeers\n    activeStreams\n    currentBytesPerSecond\n    peakBytesPerSecond\n  }\n": typeof types.P2PStatusFieldsFragmentDoc,
    "\n  fragment NetworkPeerFields on NetworkPeer {\n    id\n    kind\n    source\n    name\n    nodeId\n    userId\n    userName\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    libraryIds\n    createdAt\n    expiresAt\n    revokedAt\n    lastSeenAt\n    online\n    activeStreams\n    bytesSent\n    bytesReceived\n    currentBytesPerSecond\n    averageBytesPerSecond\n    peakBytesPerSecond\n    streams\n  }\n": typeof types.NetworkPeerFieldsFragmentDoc,
    "\n  fragment ReceivedShareFields on ReceivedShare {\n    id\n    name\n    nodeId\n    libraryId\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    active\n    online\n    createdAt\n    lastContactAt\n    copy {\n      status\n      total\n      copied\n      skipped\n      errorCode\n    }\n  }\n": typeof types.ReceivedShareFieldsFragmentDoc,
    "\n  query Network {\n    p2pStatus {\n      ...P2PStatusFields\n    }\n    networkPeers {\n      ...NetworkPeerFields\n    }\n    receivedShares {\n      ...ReceivedShareFields\n    }\n    p2pActivity(limit: 50) {\n      id\n      at\n      kind\n      peerId\n      peerName\n      detail\n    }\n    libraries {\n      id\n      name\n      sources {\n        id\n        kind\n        location\n      }\n    }\n  }\n": typeof types.NetworkDocument,
    "\n  query MyDevices {\n    networkPeers(kind: DEVICE) {\n      ...NetworkPeerFields\n    }\n  }\n": typeof types.MyDevicesDocument,
    "\n  mutation SetP2PSettings($input: P2PSettingsInput!) {\n    setP2pSettings(input: $input) {\n      ...P2PStatusFields\n    }\n  }\n": typeof types.SetP2PSettingsDocument,
    "\n  mutation CreateDeviceInvitation($input: CreateDeviceInvitationInput!) {\n    createDeviceInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": typeof types.CreateDeviceInvitationDocument,
    "\n  mutation CreateShareInvitation($input: CreateShareInvitationInput!) {\n    createShareInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": typeof types.CreateShareInvitationDocument,
    "\n  mutation UpdateNetworkPeer($input: UpdateNetworkPeerInput!) {\n    updateNetworkPeer(input: $input) {\n      ...NetworkPeerFields\n    }\n  }\n": typeof types.UpdateNetworkPeerDocument,
    "\n  mutation RevokeNetworkPeer($id: ID!) {\n    revokeNetworkPeer(id: $id) {\n      ...NetworkPeerFields\n    }\n  }\n": typeof types.RevokeNetworkPeerDocument,
    "\n  mutation AcceptShare($link: String!) {\n    acceptShare(link: $link) {\n      ...ReceivedShareFields\n    }\n  }\n": typeof types.AcceptShareDocument,
    "\n  mutation CopyReceivedShare($input: CopyReceivedShareInput!) {\n    copyReceivedShare(input: $input) {\n      ...ReceivedShareFields\n    }\n  }\n": typeof types.CopyReceivedShareDocument,
    "\n  fragment DatabaseStatusFields on DatabaseStatus {\n    provider\n    lockedByEnvironment\n    location\n    sizeBytes\n    migration {\n      phase\n      tables {\n        name\n        rows\n        copied\n      }\n      rows\n      copied\n      errorCode\n      startedAt\n      finishedAt\n    }\n    previousSqlite {\n      path\n      sizeBytes\n      exists\n      migratedAt\n    }\n  }\n": typeof types.DatabaseStatusFieldsFragmentDoc,
    "\n  query Database {\n    database {\n      ...DatabaseStatusFields\n    }\n  }\n": typeof types.DatabaseDocument,
    "\n  mutation TestPostgresConnection($connectionString: String!) {\n    testPostgresConnection(connectionString: $connectionString) {\n      success\n      error\n    }\n  }\n": typeof types.TestPostgresConnectionDocument,
    "\n  mutation MigrateToPostgres($connectionString: String!) {\n    migrateToPostgres(connectionString: $connectionString) {\n      ...DatabaseStatusFields\n    }\n  }\n": typeof types.MigrateToPostgresDocument,
    "\n  fragment UltimateCloudStatusFields on UltimateCloudStatus {\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      usedBytes\n      limitBytes\n      readOnly\n      renewsAt\n      checkedAt\n    }\n    tier\n    capabilities\n    errorCode\n    libraries {\n      libraryId\n      name\n      collection\n    }\n    audioTool {\n      version\n      errorCode\n    }\n    enabled\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n": typeof types.UltimateCloudStatusFieldsFragmentDoc,
    "\n  mutation SetUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setUltimateCloudServices(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.SetUltimateCloudServicesDocument,
    "\n  query UltimateCloudAccounts {\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n  }\n": typeof types.UltimateCloudAccountsDocument,
    "\n  fragment MyUltimateCloudStatusFields on MyUltimateCloudStatus {\n    enabled\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      readOnly\n      checkedAt\n    }\n    tier\n    errorCode\n    libraryId\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n": typeof types.MyUltimateCloudStatusFieldsFragmentDoc,
    "\n  query MyUltimateCloud {\n    myUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.MyUltimateCloudDocument,
    "\n  mutation StartMyUltimateCloudLink($input: StartMyUltimateCloudLinkInput!) {\n    startMyUltimateCloudLink(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.StartMyUltimateCloudLinkDocument,
    "\n  mutation CancelMyUltimateCloudLink {\n    cancelMyUltimateCloudLink {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.CancelMyUltimateCloudLinkDocument,
    "\n  mutation UnlinkMyUltimateCloud {\n    unlinkMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.UnlinkMyUltimateCloudDocument,
    "\n  mutation RefreshMyUltimateCloud {\n    refreshMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.RefreshMyUltimateCloudDocument,
    "\n  mutation SetMyUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setMyUltimateCloudServices(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": typeof types.SetMyUltimateCloudServicesDocument,
    "\n  query MyUltimateCloudMetrics {\n    myUltimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n    }\n  }\n": typeof types.MyUltimateCloudMetricsDocument,
    "\n  query UltimateCloudMetrics {\n    ultimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongThisMonth {\n        title\n        artist\n        playCount\n      }\n      topSongThisYear {\n        title\n        artist\n        playCount\n      }\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistThisMonth {\n        artist\n        playCount\n      }\n      topArtistThisYear {\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n      topAlbumThisMonth {\n        title\n        artist\n        playCount\n      }\n      topAlbumThisYear {\n        title\n        artist\n        playCount\n      }\n      topAlbumAllTime {\n        title\n        artist\n        playCount\n      }\n      topGenreThisMonth {\n        genre\n        playCount\n      }\n      topGenreThisYear {\n        genre\n        playCount\n      }\n      topGenreAllTime {\n        genre\n        playCount\n      }\n      topDecade {\n        decadeStart\n        decadeEnd\n        playCount\n      }\n    }\n  }\n": typeof types.UltimateCloudMetricsDocument,
    "\n  query UltimateCloud {\n    ultimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.UltimateCloudDocument,
    "\n  mutation StartUltimateCloudLink {\n    startUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.StartUltimateCloudLinkDocument,
    "\n  mutation CancelUltimateCloudLink {\n    cancelUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.CancelUltimateCloudLinkDocument,
    "\n  mutation UnlinkUltimateCloud {\n    unlinkUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.UnlinkUltimateCloudDocument,
    "\n  mutation AddUltimateCloudLibrary($input: AddUltimateCloudLibraryInput!) {\n    addUltimateCloudLibrary(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.AddUltimateCloudLibraryDocument,
    "\n  fragment CloudSyncStateFields on LibraryCloudSyncState {\n    libraryId\n    mode\n    accountUserId\n    downloadSourceId\n    lastRunAt\n    requested\n    running {\n      phase\n      done\n      total\n    }\n    matched\n    uploaded\n    downloaded\n    localOnly\n    failed\n    errorCode\n    failures {\n      title\n      path\n      reason\n    }\n  }\n": typeof types.CloudSyncStateFieldsFragmentDoc,
    "\n  query CloudSyncs {\n    cloudSyncs {\n      ...CloudSyncStateFields\n    }\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n": typeof types.CloudSyncsDocument,
    "\n  mutation SetLibraryCloudSync($libraryId: ID!, $input: SetLibraryCloudSyncInput!) {\n    setLibraryCloudSync(libraryId: $libraryId, input: $input) {\n      ...CloudSyncStateFields\n    }\n  }\n": typeof types.SetLibraryCloudSyncDocument,
    "\n  mutation SyncLibraryWithCloud($libraryId: ID!) {\n    syncLibraryWithCloud(libraryId: $libraryId) {\n      ...CloudSyncStateFields\n    }\n  }\n": typeof types.SyncLibraryWithCloudDocument,
    "\n  mutation TestS3Connection($input: S3ConnectionInput!) {\n    testS3Connection(input: $input) {\n      success\n      errorCode\n      audioFiles\n      truncated\n    }\n  }\n": typeof types.TestS3ConnectionDocument,
    "\n  mutation CreateS3Library($input: CreateS3LibraryInput!) {\n    createS3Library(input: $input) {\n      id\n    }\n  }\n": typeof types.CreateS3LibraryDocument,
    "\n  mutation RefreshUltimateCloud {\n    refreshUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": typeof types.RefreshUltimateCloudDocument,
    "\n  mutation ResolvePreviousSqlite($input: ResolvePreviousSqliteInput!) {\n    resolvePreviousSqlite(input: $input) {\n      ...DatabaseStatusFields\n    }\n  }\n": typeof types.ResolvePreviousSqliteDocument,
    "\n  fragment BackupJobFields on BackupJob {\n    id\n    backupId\n    backupName\n    status\n    trigger\n    snapshot\n    requestedAt\n    startedAt\n    finishedAt\n    files\n    copied\n    unchanged\n    versioned\n    skipped\n    failed\n    bytes\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n": typeof types.BackupJobFieldsFragmentDoc,
    "\n  fragment BackupDestinationFields on BackupDestinationInfo {\n    id\n    name\n    kind\n    location\n    backups\n    createdAt\n  }\n": typeof types.BackupDestinationFieldsFragmentDoc,
    "\n  fragment BackupFields on BackupDefinition {\n    id\n    name\n    destination {\n      id\n      name\n      kind\n    }\n    scope\n    items {\n      id\n      name\n    }\n    includeUserData\n    includeServer\n    schedule\n    intervalHours\n    timeOfDay\n    dayOfWeek\n    timeZone\n    retentionDays\n    enabled\n    folder\n    createdAt\n    nextRunAt\n    lastRun {\n      ...BackupJobFields\n    }\n  }\n": typeof types.BackupFieldsFragmentDoc,
    "\n  fragment RestoreJobFields on RestoreJobInfo {\n    id\n    backupName\n    snapshot\n    status\n    files\n    userData\n    filesTotal\n    restored\n    identical\n    renamed\n    filesFailed\n    ratings\n    playCounts\n    plays\n    playlists\n    unmatched\n    requestedAt\n    finishedAt\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n": typeof types.RestoreJobFieldsFragmentDoc,
    "\n  fragment BackupGuestFields on BackupGuest {\n    peerId\n    name\n    folder\n    usedBytes\n    quotaBytes\n    revoked\n    lastSeenAt\n  }\n": typeof types.BackupGuestFieldsFragmentDoc,
    "\n  query BackupsPage {\n    backupDestinations {\n      ...BackupDestinationFields\n    }\n    backups {\n      ...BackupFields\n    }\n    restoreJobs {\n      ...RestoreJobFields\n    }\n    backupGuests {\n      ...BackupGuestFields\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n": typeof types.BackupsPageDocument,
    "\n  query BackupsOverview {\n    backups {\n      id\n      name\n      enabled\n      schedule\n      nextRunAt\n      lastRun {\n        id\n        status\n        finishedAt\n        requestedAt\n      }\n    }\n  }\n": typeof types.BackupsOverviewDocument,
    "\n  query BackupOptions {\n    libraries {\n      id\n      name\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n    }\n    playlists {\n      id\n      name\n      trackCount\n      owner {\n        id\n        name\n      }\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n  }\n": typeof types.BackupOptionsDocument,
    "\n  query BackupAlbumSearch($search: String!) {\n    albums(search: $search, first: 20) {\n      items {\n        id\n        title\n        year\n        artist {\n          name\n        }\n      }\n    }\n  }\n": typeof types.BackupAlbumSearchDocument,
    "\n  query BackupRuns($backupId: ID!) {\n    backupRuns(backupId: $backupId) {\n      ...BackupJobFields\n    }\n  }\n": typeof types.BackupRunsDocument,
    "\n  subscription BackupJobUpdated {\n    backupJobUpdated {\n      ...BackupJobFields\n    }\n  }\n": typeof types.BackupJobUpdatedDocument,
    "\n  subscription RestoreJobUpdated {\n    restoreJobUpdated {\n      ...RestoreJobFields\n    }\n  }\n": typeof types.RestoreJobUpdatedDocument,
    "\n  mutation CreateBackupDestination($input: CreateBackupDestinationInput!) {\n    createBackupDestination(input: $input) {\n      ...BackupDestinationFields\n    }\n  }\n": typeof types.CreateBackupDestinationDocument,
    "\n  mutation TestBackupDestination($id: ID!) {\n    testBackupDestination(id: $id) {\n      success\n      errorCode\n    }\n  }\n": typeof types.TestBackupDestinationDocument,
    "\n  mutation DeleteBackupDestination($id: ID!) {\n    deleteBackupDestination(id: $id)\n  }\n": typeof types.DeleteBackupDestinationDocument,
    "\n  mutation CreateBackup($input: CreateBackupInput!) {\n    createBackup(input: $input) {\n      ...BackupFields\n    }\n  }\n": typeof types.CreateBackupDocument,
    "\n  mutation UpdateBackup($id: ID!, $input: UpdateBackupInput!) {\n    updateBackup(id: $id, input: $input) {\n      ...BackupFields\n    }\n  }\n": typeof types.UpdateBackupDocument,
    "\n  mutation DeleteBackup($id: ID!) {\n    deleteBackup(id: $id)\n  }\n": typeof types.DeleteBackupDocument,
    "\n  mutation RunBackup($id: ID!) {\n    runBackup(id: $id) {\n      ...BackupJobFields\n    }\n  }\n": typeof types.RunBackupDocument,
    "\n  mutation CancelBackupRun($id: ID!) {\n    cancelBackupRun(id: $id) {\n      ...BackupJobFields\n    }\n  }\n": typeof types.CancelBackupRunDocument,
    "\n  mutation CreateBackupInvitation($input: CreateBackupInvitationInput!) {\n    createBackupInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": typeof types.CreateBackupInvitationDocument,
    "\n  query StoredBackups($destinationId: ID!) {\n    storedBackups(destinationId: $destinationId) {\n      folder\n      id\n      name\n      scope\n      machine\n      arcanistVersion\n      updatedAt\n      snapshots {\n        name\n        createdAt\n        files\n        bytes\n        userData\n        server\n      }\n      libraries {\n        folder\n        name\n        sourceId\n      }\n    }\n  }\n": typeof types.StoredBackupsDocument,
    "\n  query BackupSnapshotContents($destinationId: ID!, $folder: String!, $snapshot: String!) {\n    backupSnapshotContents(destinationId: $destinationId, folder: $folder, snapshot: $snapshot) {\n      libraries {\n        folder\n        name\n        files\n        bytes\n      }\n      albums {\n        id\n        title\n        artist\n        year\n        paths\n      }\n      playlists {\n        owner\n        name\n        tracks\n        paths\n      }\n      users {\n        userName\n        displayName\n        ratings\n        plays\n        playlists\n        userId\n      }\n    }\n  }\n": typeof types.BackupSnapshotContentsDocument,
    "\n  mutation StartRestore($input: StartRestoreInput!) {\n    startRestore(input: $input) {\n      ...RestoreJobFields\n    }\n  }\n": typeof types.StartRestoreDocument,
    "\n  mutation CancelRestore($id: ID!) {\n    cancelRestore(id: $id) {\n      ...RestoreJobFields\n    }\n  }\n": typeof types.CancelRestoreDocument,
};
const documents: Documents = {
    "\n  fragment ScanJobFields on ScanJob {\n    id\n    libraryId\n    kind\n    status\n    requestedAt\n    startedAt\n    finishedAt\n    filesSeen\n    filesAdded\n    filesUpdated\n    filesMissing\n    errors\n    errorCode\n    progress {\n      phase\n      filesFound\n      filesToRead\n      filesRead\n    }\n  }\n": types.ScanJobFieldsFragmentDoc,
    "\n  query ServerInfo {\n    serverInfo {\n      name\n      version\n      apiVersion\n      sourceUrl\n    }\n  }\n": types.ServerInfoDocument,
    "\n  query Overview {\n    libraries {\n      id\n    }\n    artists(first: 1) {\n      totalCount\n    }\n    albums(first: 1) {\n      totalCount\n    }\n    tracks(first: 1) {\n      totalCount\n    }\n  }\n": types.OverviewDocument,
    "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalMinutes\n      watchForChanges\n      createdAt\n      lastScanAt\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": types.LibrariesDocument,
    "\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n": types.CreateLibraryDocument,
    "\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n": types.UpdateLibraryDocument,
    "\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n": types.DeleteLibraryDocument,
    "\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n": types.AddLibrarySourceDocument,
    "\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n": types.RemoveLibrarySourceDocument,
    "\n  fragment LibrarySourceMoveFields on LibrarySourceMove {\n    sourceId\n    libraryId\n    from\n    to\n    keepOriginals\n    phase\n    filesDone\n    filesTotal\n    bytesDone\n    bytesTotal\n    startedAt\n    finishedAt\n    errorCode\n  }\n": types.LibrarySourceMoveFieldsFragmentDoc,
    "\n  query LibrarySourceMoves {\n    librarySourceMoves {\n      ...LibrarySourceMoveFields\n    }\n  }\n": types.LibrarySourceMovesDocument,
    "\n  mutation MoveLibrarySource($id: ID!, $input: MoveLibrarySourceInput!) {\n    moveLibrarySource(id: $id, input: $input) {\n      ...LibrarySourceMoveFields\n    }\n  }\n": types.MoveLibrarySourceDocument,
    "\n  query ServerDirectories($path: String) {\n    serverDirectories(path: $path) {\n      path\n      parent\n      directories {\n        name\n        path\n        isHidden\n      }\n    }\n  }\n": types.ServerDirectoriesDocument,
    "\n  mutation CreateServerDirectory($input: CreateServerDirectoryInput!) {\n    createServerDirectory(input: $input) {\n      name\n      path\n      isHidden\n    }\n  }\n": types.CreateServerDirectoryDocument,
    "\n  query ScanJobs($libraryId: ID, $limit: Int) {\n    scanJobs(libraryId: $libraryId, limit: $limit) {\n      ...ScanJobFields\n    }\n  }\n": types.ScanJobsDocument,
    "\n  mutation StartScan($libraryId: ID!, $full: Boolean!) {\n    startScan(libraryId: $libraryId, full: $full) {\n      ...ScanJobFields\n    }\n  }\n": types.StartScanDocument,
    "\n  mutation CancelScan($id: ID!) {\n    cancelScan(id: $id)\n  }\n": types.CancelScanDocument,
    "\n  subscription ScanJobUpdated {\n    scanJobUpdated {\n      ...ScanJobFields\n    }\n  }\n": types.ScanJobUpdatedDocument,
    "\n  fragment UserFields on UserAccount {\n    id\n    userName\n    displayName\n    role\n    permissions\n    isDisabled\n    createdAt\n    lastLoginAt\n    libraryIds\n  }\n": types.UserFieldsFragmentDoc,
    "\n  query Users {\n    users {\n      ...UserFields\n    }\n    libraries {\n      id\n      name\n      visibleToAll\n    }\n  }\n": types.UsersDocument,
    "\n  mutation CreateUser($input: CreateUserInput!) {\n    createUser(input: $input) {\n      id\n    }\n  }\n": types.CreateUserDocument,
    "\n  mutation UpdateUser($id: ID!, $input: UpdateUserInput!) {\n    updateUser(id: $id, input: $input) {\n      id\n    }\n  }\n": types.UpdateUserDocument,
    "\n  mutation SetUserLibraries($id: ID!, $input: SetUserLibrariesInput!) {\n    setUserLibraries(id: $id, input: $input) {\n      id\n    }\n  }\n": types.SetUserLibrariesDocument,
    "\n  mutation ResetUserPassword($id: ID!, $input: ResetUserPasswordInput!) {\n    resetUserPassword(id: $id, input: $input)\n  }\n": types.ResetUserPasswordDocument,
    "\n  mutation DeleteUser($id: ID!) {\n    deleteUser(id: $id)\n  }\n": types.DeleteUserDocument,
    "\n  query MySessions {\n    mySessions {\n      id\n      deviceName\n      userAgent\n      ipAddress\n      createdAt\n      lastSeenAt\n      expiresAt\n      isCurrent\n    }\n  }\n": types.MySessionsDocument,
    "\n  mutation RevokeMySession($id: ID!) {\n    revokeMySession(id: $id)\n  }\n": types.RevokeMySessionDocument,
    "\n  mutation ChangeMyPassword($input: ChangeMyPasswordInput!) {\n    changeMyPassword(input: $input)\n  }\n": types.ChangeMyPasswordDocument,
    "\n  fragment AnalysisProgressFields on AnalysisProgress {\n    activity\n    enabled\n    pending\n    analyzed\n    needsFfmpeg\n    failed\n    filesPerMinute\n    lastAnalyzedAt\n    error\n  }\n": types.AnalysisProgressFieldsFragmentDoc,
    "\n  query AnalysisProgress {\n    analysisProgress {\n      ...AnalysisProgressFields\n    }\n  }\n": types.AnalysisProgressDocument,
    "\n  mutation SetAnalysisEnabled($enabled: Boolean!) {\n    setAnalysisEnabled(enabled: $enabled) {\n      ...AnalysisProgressFields\n    }\n  }\n": types.SetAnalysisEnabledDocument,
    "\n  subscription AnalysisProgressChanged {\n    analysisProgressChanged {\n      ...AnalysisProgressFields\n    }\n  }\n": types.AnalysisProgressChangedDocument,
    "\n  fragment FfmpegStatusFields on FfmpegStatus {\n    available\n    path\n    version\n    source\n    configuredPath\n    configuredPathWorks\n    canDownload\n    download {\n      state\n      bytesReceived\n      totalBytes\n      version\n      error\n    }\n  }\n": types.FfmpegStatusFieldsFragmentDoc,
    "\n  fragment ServerConfigurationFields on ServerConfiguration {\n    defaultLanguage\n    ratingMode\n    shareAnonymousStatistics\n    ffmpegPath\n    onlineServices {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    privacy {\n      noTelemetry\n      externalServices\n    }\n  }\n": types.ServerConfigurationFieldsFragmentDoc,
    "\n  query Privacy {\n    privacy {\n      noTelemetry\n      externalServices\n    }\n    anonymousStatistics\n  }\n": types.PrivacyDocument,
    "\n  mutation SetPrivacy($input: SetPrivacyInput!) {\n    setPrivacy(input: $input) {\n      noTelemetry\n      externalServices\n    }\n  }\n": types.SetPrivacyDocument,
    "\n  query SetupExtras {\n    users {\n      id\n      userName\n      displayName\n      role\n    }\n    p2pStatus {\n      enabled\n      customName\n      relay\n      relayUrl\n      bandwidthKbps\n      maxStreams\n    }\n    extensions {\n      id\n      builtIn\n      enabled\n    }\n  }\n": types.SetupExtrasDocument,
    "\n  query ServerConfiguration {\n    serverConfiguration {\n      ...ServerConfigurationFields\n    }\n  }\n": types.ServerConfigurationDocument,
    "\n  mutation ReconfigureServer($input: ReconfigureServerInput!) {\n    reconfigureServer(input: $input) {\n      ...ServerConfigurationFields\n    }\n  }\n": types.ReconfigureServerDocument,
    "\n  mutation SetAnonymousStatistics($enabled: Boolean!) {\n    setAnonymousStatistics(enabled: $enabled)\n  }\n": types.SetAnonymousStatisticsDocument,
    "\n  mutation ResetSettings {\n    resetSettings {\n      restartRequired\n    }\n  }\n": types.ResetSettingsDocument,
    "\n  mutation ResetUserData {\n    resetUserData {\n      restartRequired\n    }\n  }\n": types.ResetUserDataDocument,
    "\n  mutation FactoryReset($input: FactoryResetInput!) {\n    factoryReset(input: $input) {\n      restartRequired\n    }\n  }\n": types.FactoryResetDocument,
    "\n  query Ffmpeg {\n    ffmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n": types.FfmpegDocument,
    "\n  mutation SetFfmpegPath($path: String) {\n    setFfmpegPath(path: $path) {\n      ...FfmpegStatusFields\n    }\n  }\n": types.SetFfmpegPathDocument,
    "\n  mutation InstallFfmpeg {\n    installFfmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n": types.InstallFfmpegDocument,
    "\n  fragment DuplicateTrackFields on Track {\n    id\n    title\n    durationMs\n    trackNumber\n    discNumber\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n    files {\n      id\n      format\n      bitRate\n      sampleRate\n      bitDepth\n    }\n  }\n": types.DuplicateTrackFieldsFragmentDoc,
    "\n  query DuplicateCandidates($status: DuplicateStatus) {\n    duplicateCandidates(status: $status, first: 200) {\n      id\n      similarity\n      status\n      detectedAt\n      trackA {\n        ...DuplicateTrackFields\n      }\n      trackB {\n        ...DuplicateTrackFields\n      }\n    }\n  }\n": types.DuplicateCandidatesDocument,
    "\n  mutation MergeDuplicate($id: ID!, $keepTrackId: ID!) {\n    mergeDuplicate(id: $id, keepTrackId: $keepTrackId) {\n      id\n    }\n  }\n": types.MergeDuplicateDocument,
    "\n  mutation DismissDuplicate($id: ID!) {\n    dismissDuplicate(id: $id)\n  }\n": types.DismissDuplicateDocument,
    "\n  fragment OnlineServicesFields on OnlineServicesStatus {\n    settings {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    hasAcoustIdKey\n    hasOwnAcoustIdKey\n    services {\n      service\n      enabled\n      ready\n      pending\n      found\n      notFound\n      failed\n      lastRequestAt\n      lastError\n    }\n  }\n": types.OnlineServicesFieldsFragmentDoc,
    "\n  query OnlineServices {\n    onlineServices {\n      ...OnlineServicesFields\n    }\n  }\n": types.OnlineServicesDocument,
    "\n  mutation SetOnlineServices($input: SetOnlineServicesInput!) {\n    setOnlineServices(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n": types.SetOnlineServicesDocument,
    "\n  mutation SetAcoustIdKey($input: SetAcoustIdKeyInput!) {\n    setAcoustIdKey(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n": types.SetAcoustIdKeyDocument,
    "\n  fragment OrganizeRunFields on OrganizeRun {\n    id\n    libraryId\n    kind\n    status\n    template\n    startedAt\n    finishedAt\n    undoneAt\n    total\n    moved\n    failed\n    errorCode\n  }\n": types.OrganizeRunFieldsFragmentDoc,
    "\n  query OrganizePreview($libraryId: ID!, $template: String) {\n    organizePreview(libraryId: $libraryId, template: $template) {\n      template\n      filesToMove\n      filesInPlace\n      otherFilesToMove\n      unavailableSourceIds\n      moves {\n        sourceId\n        from\n        to\n      }\n    }\n  }\n": types.OrganizePreviewDocument,
    "\n  query OrganizeRuns($libraryId: ID!) {\n    organizeRuns(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n": types.OrganizeRunsDocument,
    "\n  mutation UpdateLibraryOrganization($libraryId: ID!, $input: UpdateLibraryOrganizationInput!) {\n    updateLibraryOrganization(libraryId: $libraryId, input: $input) {\n      id\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": types.UpdateLibraryOrganizationDocument,
    "\n  mutation OrganizeLibrary($libraryId: ID!) {\n    organizeLibrary(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n": types.OrganizeLibraryDocument,
    "\n  mutation UndoOrganizeRun($id: ID!) {\n    undoOrganizeRun(id: $id) {\n      ...OrganizeRunFields\n    }\n  }\n": types.UndoOrganizeRunDocument,
    "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    mode\n    kind\n    uploadToCloud\n    uploadAccount\n    createdAt\n    available\n  }\n": types.DropFolderFieldsFragmentDoc,
    "\n  query DropFolders {\n    dropFolders {\n      ...DropFolderFields\n    }\n  }\n": types.DropFoldersDocument,
    "\n  mutation CreateDropFolder($input: DropFolderInput!) {\n    createDropFolder(input: $input) {\n      ...DropFolderFields\n    }\n  }\n": types.CreateDropFolderDocument,
    "\n  mutation UpdateDropFolder($id: ID!, $input: DropFolderInput!) {\n    updateDropFolder(id: $id, input: $input) {\n      ...DropFolderFields\n    }\n  }\n": types.UpdateDropFolderDocument,
    "\n  mutation DeleteDropFolder($id: ID!) {\n    deleteDropFolder(id: $id)\n  }\n": types.DeleteDropFolderDocument,
    "\n  fragment ImportItemFields on ImportItem {\n    id\n    dropFolderId\n    path\n    size\n    status\n    reason\n    destinationPath\n    trackId\n    createdAt\n    updatedAt\n    details {\n      title\n      artist\n      album\n      albumArtist\n      year\n      trackNumber\n      discNumber\n      genre\n      format\n      bitRate\n      sampleRate\n      bitDepth\n      durationMs\n    }\n  }\n": types.ImportItemFieldsFragmentDoc,
    "\n  query ImportQueue($status: ImportStatus) {\n    importSummary {\n      pending\n      needsReview\n      duplicate\n      failed\n      ignored\n      imported\n    }\n    importItems(status: $status, first: 200) {\n      ...ImportItemFields\n    }\n  }\n": types.ImportQueueDocument,
    "\n  mutation AcceptImportItem($id: ID!, $tags: ImportTagsInput) {\n    acceptImportItem(id: $id, tags: $tags) {\n      ...ImportItemFields\n    }\n  }\n": types.AcceptImportItemDocument,
    "\n  mutation RetryImportItem($id: ID!) {\n    retryImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": types.RetryImportItemDocument,
    "\n  mutation IgnoreImportItem($id: ID!) {\n    ignoreImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": types.IgnoreImportItemDocument,
    "\n  mutation DiscardImportItem($id: ID!) {\n    discardImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n": types.DiscardImportItemDocument,
    "\n  query RatingMode {\n    serverInfo {\n      ratingMode\n    }\n  }\n": types.RatingModeDocument,
    "\n  mutation SetRatingMode($mode: RatingMode!) {\n    setRatingMode(mode: $mode)\n  }\n": types.SetRatingModeDocument,
    "\n  fragment StatsTrackFields on Track {\n    id\n    title\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n  }\n": types.StatsTrackFieldsFragmentDoc,
    "\n  query ServerStats($period: StatsPeriod!, $timeZone: String) {\n    serverStats(period: $period, timeZone: $timeZone) {\n      library {\n        artists\n        albums\n        tracks\n        files\n        durationMs\n        sizeBytes\n        formats {\n          format\n          files\n          sizeBytes\n        }\n      }\n      listening {\n        period\n        from\n        plays\n        listenedMs\n        distinctTracks\n        bucket\n        timeline {\n          start\n          plays\n          listenedMs\n        }\n        topTracks {\n          plays\n          track {\n            ...StatsTrackFields\n          }\n        }\n        topArtists {\n          plays\n          artist {\n            id\n            name\n          }\n        }\n        topAlbums {\n          plays\n          album {\n            id\n            title\n            artworkId\n            artist {\n              id\n              name\n            }\n          }\n        }\n      }\n      users {\n        userId\n        userName\n        displayName\n        plays\n        listenedMs\n        lastPlayedAt\n      }\n    }\n  }\n": types.ServerStatsDocument,
    "\n  fragment AppPasswordFields on AppPassword {\n    id\n    name\n    createdAt\n    lastUsedAt\n  }\n": types.AppPasswordFieldsFragmentDoc,
    "\n  query AppPasswords {\n    appPasswords {\n      ...AppPasswordFields\n    }\n  }\n": types.AppPasswordsDocument,
    "\n  mutation CreateAppPassword($name: String!) {\n    createAppPassword(name: $name) {\n      secret\n      appPassword {\n        ...AppPasswordFields\n      }\n    }\n  }\n": types.CreateAppPasswordDocument,
    "\n  mutation DeleteAppPassword($id: ID!) {\n    deleteAppPassword(id: $id)\n  }\n": types.DeleteAppPasswordDocument,
    "\n  fragment FrontendFields on Frontend {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    isDefault\n    path\n    installedAt\n  }\n": types.FrontendFieldsFragmentDoc,
    "\n  query Frontends {\n    frontends {\n      ...FrontendFields\n    }\n  }\n": types.FrontendsDocument,
    "\n  mutation InstallFrontendFromUrl($url: String!) {\n    installFrontendFromUrl(url: $url) {\n      ...FrontendFields\n    }\n  }\n": types.InstallFrontendFromUrlDocument,
    "\n  mutation SetDefaultFrontend($id: String!) {\n    setDefaultFrontend(id: $id) {\n      ...FrontendFields\n    }\n  }\n": types.SetDefaultFrontendDocument,
    "\n  mutation UninstallFrontend($id: String!) {\n    uninstallFrontend(id: $id)\n  }\n": types.UninstallFrontendDocument,
    "\n  fragment ExtensionFields on Extension {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    enabled\n    status\n    errorCode\n    restartRequired\n    routes\n    basePath\n  }\n": types.ExtensionFieldsFragmentDoc,
    "\n  query Extensions {\n    extensions {\n      ...ExtensionFields\n    }\n  }\n": types.ExtensionsDocument,
    "\n  mutation InstallExtensionFromUrl($url: String!) {\n    installExtensionFromUrl(url: $url) {\n      ...ExtensionFields\n    }\n  }\n": types.InstallExtensionFromUrlDocument,
    "\n  mutation SetExtensionEnabled($id: String!, $enabled: Boolean!) {\n    setExtensionEnabled(id: $id, enabled: $enabled) {\n      ...ExtensionFields\n    }\n  }\n": types.SetExtensionEnabledDocument,
    "\n  mutation UninstallExtension($id: String!) {\n    uninstallExtension(id: $id)\n  }\n": types.UninstallExtensionDocument,
    "\n  fragment MstreamImportJobFields on MstreamImportJob {\n    id\n    kind\n    source\n    status\n    createdAt\n    finishedAt\n    librariesCreated\n    usersCreated\n    playlists\n    ratings\n    plays\n    unmatched\n    errorCode\n  }\n": types.MstreamImportJobFieldsFragmentDoc,
    "\n  fragment MstreamAnalysisFields on MstreamAnalysis {\n    version\n    libraries {\n      vpath\n      root\n      rootExists\n      sourceId\n    }\n    users {\n      userName\n      isAdmin\n      vpaths\n      exists\n      canSignIn\n    }\n    playlists\n    ratedTracks\n    playedTracks\n  }\n": types.MstreamAnalysisFieldsFragmentDoc,
    "\n  query MstreamImports {\n    mstreamImports {\n      ...MstreamImportJobFields\n    }\n  }\n": types.MstreamImportsDocument,
    "\n  fragment DlnaImportJobFields on DlnaImportJob {\n    id\n    serverName\n    folders\n    libraryId\n    libraryName\n    status\n    found\n    copied\n    duplicates\n    failed\n    copiedBytes\n    createdAt\n    finishedAt\n    errorCode\n    failures {\n      title\n      artist\n      album\n      reason\n    }\n  }\n": types.DlnaImportJobFieldsFragmentDoc,
    "\n  query DlnaImports {\n    dlnaImports {\n      ...DlnaImportJobFields\n    }\n  }\n": types.DlnaImportsDocument,
    "\n  query DlnaMediaServers {\n    dlnaMediaServers {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n": types.DlnaMediaServersDocument,
    "\n  query DlnaMediaServer($location: String!) {\n    dlnaMediaServer(location: $location) {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n": types.DlnaMediaServerDocument,
    "\n  query BrowseDlnaMediaServer($location: String!, $folderId: String) {\n    browseDlnaMediaServer(location: $location, folderId: $folderId) {\n      id\n      tracks\n      truncated\n      folders {\n        id\n        title\n        childCount\n      }\n    }\n  }\n": types.BrowseDlnaMediaServerDocument,
    "\n  mutation StartDlnaImport($input: StartDlnaImportInput!) {\n    startDlnaImport(input: $input) {\n      ...DlnaImportJobFields\n    }\n  }\n": types.StartDlnaImportDocument,
    "\n  mutation DeleteDlnaImport($id: ID!) {\n    deleteDlnaImport(id: $id)\n  }\n": types.DeleteDlnaImportDocument,
    "\n  mutation DeleteMstreamImport($id: ID!) {\n    deleteMstreamImport(id: $id)\n  }\n": types.DeleteMstreamImportDocument,
    "\n  mutation ClearImportHistory {\n    clearImportHistory\n  }\n": types.ClearImportHistoryDocument,
    "\n  mutation CancelDlnaImport($id: ID!) {\n    cancelDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n": types.CancelDlnaImportDocument,
    "\n  mutation RetryDlnaImport($id: ID!) {\n    retryDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n": types.RetryDlnaImportDocument,
    "\n  mutation AnalyzeMstreamFolder($folder: String!) {\n    analyzeMstreamFolder(folder: $folder) {\n      ...MstreamAnalysisFields\n    }\n  }\n": types.AnalyzeMstreamFolderDocument,
    "\n  mutation AnalyzeMstreamServer($input: MstreamServerInput!) {\n    analyzeMstreamServer(input: $input) {\n      ...MstreamAnalysisFields\n    }\n  }\n": types.AnalyzeMstreamServerDocument,
    "\n  mutation ImportMstream($input: ImportMstreamInput!) {\n    importMstream(input: $input) {\n      ...MstreamImportJobFields\n    }\n  }\n": types.ImportMstreamDocument,
    "\n  fragment DlnaStatusFields on DlnaStatus {\n    enabled\n    name\n    friendlyName\n    libraryIds\n    sections\n    ratingsUserId\n    addresses\n    convertsFormats\n  }\n": types.DlnaStatusFieldsFragmentDoc,
    "\n  fragment MstreamStatusFields on MstreamStatus {\n    enabled\n    starThreshold\n    passwordSignIn\n    features\n    quickConnect\n    p2pEnabled\n    pairingCode\n  }\n": types.MstreamStatusFieldsFragmentDoc,
    "\n  query Mstream {\n    mstream {\n      ...MstreamStatusFields\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n": types.MstreamDocument,
    "\n  mutation SetMstreamSettings($input: MstreamSettingsInput!) {\n    setMstreamSettings(input: $input) {\n      ...MstreamStatusFields\n    }\n  }\n": types.SetMstreamSettingsDocument,
    "\n  mutation RotateMstreamQuickConnect {\n    rotateMstreamQuickConnect {\n      ...MstreamStatusFields\n    }\n  }\n": types.RotateMstreamQuickConnectDocument,
    "\n  query Dlna {\n    dlna {\n      ...DlnaStatusFields\n    }\n    libraries {\n      id\n      name\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n": types.DlnaDocument,
    "\n  mutation SetDlnaSettings($input: DlnaSettingsInput!) {\n    setDlnaSettings(input: $input) {\n      ...DlnaStatusFields\n    }\n  }\n": types.SetDlnaSettingsDocument,
    "\n  fragment DlnaRendererFields on DlnaRenderer {\n    id\n    name\n    manufacturer\n    model\n    address\n    manual\n  }\n": types.DlnaRendererFieldsFragmentDoc,
    "\n  query DlnaRenderers {\n    dlnaRenderers {\n      ...DlnaRendererFields\n    }\n  }\n": types.DlnaRenderersDocument,
    "\n  fragment DlnaRendererStateFields on DlnaRendererState {\n    renderer {\n      ...DlnaRendererFields\n    }\n    state\n    positionMs\n    durationMs\n    volume\n    queue\n    queueIndex\n    track {\n      id\n      title\n      artworkId\n      album {\n        title\n      }\n      artists {\n        name\n        role\n      }\n    }\n  }\n": types.DlnaRendererStateFieldsFragmentDoc,
    "\n  query DlnaRenderer($id: String!) {\n    dlnaRenderer(id: $id) {\n      ...DlnaRendererStateFields\n    }\n  }\n": types.DlnaRendererDocument,
    "\n  mutation AddDlnaRenderer($url: String!) {\n    addDlnaRenderer(url: $url) {\n      ...DlnaRendererFields\n    }\n  }\n": types.AddDlnaRendererDocument,
    "\n  mutation PlayOnDlnaRenderer($input: PlayOnDlnaRendererInput!) {\n    playOnDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n": types.PlayOnDlnaRendererDocument,
    "\n  mutation ControlDlnaRenderer($input: ControlDlnaRendererInput!) {\n    controlDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n": types.ControlDlnaRendererDocument,
    "\n  mutation SetDlnaRendererVolume($rendererId: String!, $volume: Int!) {\n    setDlnaRendererVolume(rendererId: $rendererId, volume: $volume) {\n      ...DlnaRendererStateFields\n    }\n  }\n": types.SetDlnaRendererVolumeDocument,
    "\n  query DlnaAlbumSearch($search: String!) {\n    albums(search: $search, first: 8) {\n      items {\n        id\n        title\n        artist {\n          name\n        }\n      }\n    }\n  }\n": types.DlnaAlbumSearchDocument,
    "\n  query DlnaAlbumTracks($albumId: ID!) {\n    tracks(albumId: $albumId) {\n      items {\n        id\n      }\n    }\n  }\n": types.DlnaAlbumTracksDocument,
    "\n  fragment P2PStatusFields on P2PStatus {\n    enabled\n    available\n    running\n    errorCode\n    nodeId\n    name\n    customName\n    relay\n    relayUrl\n    homeRelay\n    relayConnected\n    bandwidthKbps\n    maxStreams\n    connectedPeers\n    activeStreams\n    currentBytesPerSecond\n    peakBytesPerSecond\n  }\n": types.P2PStatusFieldsFragmentDoc,
    "\n  fragment NetworkPeerFields on NetworkPeer {\n    id\n    kind\n    source\n    name\n    nodeId\n    userId\n    userName\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    libraryIds\n    createdAt\n    expiresAt\n    revokedAt\n    lastSeenAt\n    online\n    activeStreams\n    bytesSent\n    bytesReceived\n    currentBytesPerSecond\n    averageBytesPerSecond\n    peakBytesPerSecond\n    streams\n  }\n": types.NetworkPeerFieldsFragmentDoc,
    "\n  fragment ReceivedShareFields on ReceivedShare {\n    id\n    name\n    nodeId\n    libraryId\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    active\n    online\n    createdAt\n    lastContactAt\n    copy {\n      status\n      total\n      copied\n      skipped\n      errorCode\n    }\n  }\n": types.ReceivedShareFieldsFragmentDoc,
    "\n  query Network {\n    p2pStatus {\n      ...P2PStatusFields\n    }\n    networkPeers {\n      ...NetworkPeerFields\n    }\n    receivedShares {\n      ...ReceivedShareFields\n    }\n    p2pActivity(limit: 50) {\n      id\n      at\n      kind\n      peerId\n      peerName\n      detail\n    }\n    libraries {\n      id\n      name\n      sources {\n        id\n        kind\n        location\n      }\n    }\n  }\n": types.NetworkDocument,
    "\n  query MyDevices {\n    networkPeers(kind: DEVICE) {\n      ...NetworkPeerFields\n    }\n  }\n": types.MyDevicesDocument,
    "\n  mutation SetP2PSettings($input: P2PSettingsInput!) {\n    setP2pSettings(input: $input) {\n      ...P2PStatusFields\n    }\n  }\n": types.SetP2PSettingsDocument,
    "\n  mutation CreateDeviceInvitation($input: CreateDeviceInvitationInput!) {\n    createDeviceInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": types.CreateDeviceInvitationDocument,
    "\n  mutation CreateShareInvitation($input: CreateShareInvitationInput!) {\n    createShareInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": types.CreateShareInvitationDocument,
    "\n  mutation UpdateNetworkPeer($input: UpdateNetworkPeerInput!) {\n    updateNetworkPeer(input: $input) {\n      ...NetworkPeerFields\n    }\n  }\n": types.UpdateNetworkPeerDocument,
    "\n  mutation RevokeNetworkPeer($id: ID!) {\n    revokeNetworkPeer(id: $id) {\n      ...NetworkPeerFields\n    }\n  }\n": types.RevokeNetworkPeerDocument,
    "\n  mutation AcceptShare($link: String!) {\n    acceptShare(link: $link) {\n      ...ReceivedShareFields\n    }\n  }\n": types.AcceptShareDocument,
    "\n  mutation CopyReceivedShare($input: CopyReceivedShareInput!) {\n    copyReceivedShare(input: $input) {\n      ...ReceivedShareFields\n    }\n  }\n": types.CopyReceivedShareDocument,
    "\n  fragment DatabaseStatusFields on DatabaseStatus {\n    provider\n    lockedByEnvironment\n    location\n    sizeBytes\n    migration {\n      phase\n      tables {\n        name\n        rows\n        copied\n      }\n      rows\n      copied\n      errorCode\n      startedAt\n      finishedAt\n    }\n    previousSqlite {\n      path\n      sizeBytes\n      exists\n      migratedAt\n    }\n  }\n": types.DatabaseStatusFieldsFragmentDoc,
    "\n  query Database {\n    database {\n      ...DatabaseStatusFields\n    }\n  }\n": types.DatabaseDocument,
    "\n  mutation TestPostgresConnection($connectionString: String!) {\n    testPostgresConnection(connectionString: $connectionString) {\n      success\n      error\n    }\n  }\n": types.TestPostgresConnectionDocument,
    "\n  mutation MigrateToPostgres($connectionString: String!) {\n    migrateToPostgres(connectionString: $connectionString) {\n      ...DatabaseStatusFields\n    }\n  }\n": types.MigrateToPostgresDocument,
    "\n  fragment UltimateCloudStatusFields on UltimateCloudStatus {\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      usedBytes\n      limitBytes\n      readOnly\n      renewsAt\n      checkedAt\n    }\n    tier\n    capabilities\n    errorCode\n    libraries {\n      libraryId\n      name\n      collection\n    }\n    audioTool {\n      version\n      errorCode\n    }\n    enabled\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n": types.UltimateCloudStatusFieldsFragmentDoc,
    "\n  mutation SetUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setUltimateCloudServices(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.SetUltimateCloudServicesDocument,
    "\n  query UltimateCloudAccounts {\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n  }\n": types.UltimateCloudAccountsDocument,
    "\n  fragment MyUltimateCloudStatusFields on MyUltimateCloudStatus {\n    enabled\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      readOnly\n      checkedAt\n    }\n    tier\n    errorCode\n    libraryId\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n": types.MyUltimateCloudStatusFieldsFragmentDoc,
    "\n  query MyUltimateCloud {\n    myUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.MyUltimateCloudDocument,
    "\n  mutation StartMyUltimateCloudLink($input: StartMyUltimateCloudLinkInput!) {\n    startMyUltimateCloudLink(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.StartMyUltimateCloudLinkDocument,
    "\n  mutation CancelMyUltimateCloudLink {\n    cancelMyUltimateCloudLink {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.CancelMyUltimateCloudLinkDocument,
    "\n  mutation UnlinkMyUltimateCloud {\n    unlinkMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.UnlinkMyUltimateCloudDocument,
    "\n  mutation RefreshMyUltimateCloud {\n    refreshMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.RefreshMyUltimateCloudDocument,
    "\n  mutation SetMyUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setMyUltimateCloudServices(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n": types.SetMyUltimateCloudServicesDocument,
    "\n  query MyUltimateCloudMetrics {\n    myUltimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n    }\n  }\n": types.MyUltimateCloudMetricsDocument,
    "\n  query UltimateCloudMetrics {\n    ultimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongThisMonth {\n        title\n        artist\n        playCount\n      }\n      topSongThisYear {\n        title\n        artist\n        playCount\n      }\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistThisMonth {\n        artist\n        playCount\n      }\n      topArtistThisYear {\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n      topAlbumThisMonth {\n        title\n        artist\n        playCount\n      }\n      topAlbumThisYear {\n        title\n        artist\n        playCount\n      }\n      topAlbumAllTime {\n        title\n        artist\n        playCount\n      }\n      topGenreThisMonth {\n        genre\n        playCount\n      }\n      topGenreThisYear {\n        genre\n        playCount\n      }\n      topGenreAllTime {\n        genre\n        playCount\n      }\n      topDecade {\n        decadeStart\n        decadeEnd\n        playCount\n      }\n    }\n  }\n": types.UltimateCloudMetricsDocument,
    "\n  query UltimateCloud {\n    ultimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.UltimateCloudDocument,
    "\n  mutation StartUltimateCloudLink {\n    startUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.StartUltimateCloudLinkDocument,
    "\n  mutation CancelUltimateCloudLink {\n    cancelUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.CancelUltimateCloudLinkDocument,
    "\n  mutation UnlinkUltimateCloud {\n    unlinkUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.UnlinkUltimateCloudDocument,
    "\n  mutation AddUltimateCloudLibrary($input: AddUltimateCloudLibraryInput!) {\n    addUltimateCloudLibrary(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.AddUltimateCloudLibraryDocument,
    "\n  fragment CloudSyncStateFields on LibraryCloudSyncState {\n    libraryId\n    mode\n    accountUserId\n    downloadSourceId\n    lastRunAt\n    requested\n    running {\n      phase\n      done\n      total\n    }\n    matched\n    uploaded\n    downloaded\n    localOnly\n    failed\n    errorCode\n    failures {\n      title\n      path\n      reason\n    }\n  }\n": types.CloudSyncStateFieldsFragmentDoc,
    "\n  query CloudSyncs {\n    cloudSyncs {\n      ...CloudSyncStateFields\n    }\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n": types.CloudSyncsDocument,
    "\n  mutation SetLibraryCloudSync($libraryId: ID!, $input: SetLibraryCloudSyncInput!) {\n    setLibraryCloudSync(libraryId: $libraryId, input: $input) {\n      ...CloudSyncStateFields\n    }\n  }\n": types.SetLibraryCloudSyncDocument,
    "\n  mutation SyncLibraryWithCloud($libraryId: ID!) {\n    syncLibraryWithCloud(libraryId: $libraryId) {\n      ...CloudSyncStateFields\n    }\n  }\n": types.SyncLibraryWithCloudDocument,
    "\n  mutation TestS3Connection($input: S3ConnectionInput!) {\n    testS3Connection(input: $input) {\n      success\n      errorCode\n      audioFiles\n      truncated\n    }\n  }\n": types.TestS3ConnectionDocument,
    "\n  mutation CreateS3Library($input: CreateS3LibraryInput!) {\n    createS3Library(input: $input) {\n      id\n    }\n  }\n": types.CreateS3LibraryDocument,
    "\n  mutation RefreshUltimateCloud {\n    refreshUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n": types.RefreshUltimateCloudDocument,
    "\n  mutation ResolvePreviousSqlite($input: ResolvePreviousSqliteInput!) {\n    resolvePreviousSqlite(input: $input) {\n      ...DatabaseStatusFields\n    }\n  }\n": types.ResolvePreviousSqliteDocument,
    "\n  fragment BackupJobFields on BackupJob {\n    id\n    backupId\n    backupName\n    status\n    trigger\n    snapshot\n    requestedAt\n    startedAt\n    finishedAt\n    files\n    copied\n    unchanged\n    versioned\n    skipped\n    failed\n    bytes\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n": types.BackupJobFieldsFragmentDoc,
    "\n  fragment BackupDestinationFields on BackupDestinationInfo {\n    id\n    name\n    kind\n    location\n    backups\n    createdAt\n  }\n": types.BackupDestinationFieldsFragmentDoc,
    "\n  fragment BackupFields on BackupDefinition {\n    id\n    name\n    destination {\n      id\n      name\n      kind\n    }\n    scope\n    items {\n      id\n      name\n    }\n    includeUserData\n    includeServer\n    schedule\n    intervalHours\n    timeOfDay\n    dayOfWeek\n    timeZone\n    retentionDays\n    enabled\n    folder\n    createdAt\n    nextRunAt\n    lastRun {\n      ...BackupJobFields\n    }\n  }\n": types.BackupFieldsFragmentDoc,
    "\n  fragment RestoreJobFields on RestoreJobInfo {\n    id\n    backupName\n    snapshot\n    status\n    files\n    userData\n    filesTotal\n    restored\n    identical\n    renamed\n    filesFailed\n    ratings\n    playCounts\n    plays\n    playlists\n    unmatched\n    requestedAt\n    finishedAt\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n": types.RestoreJobFieldsFragmentDoc,
    "\n  fragment BackupGuestFields on BackupGuest {\n    peerId\n    name\n    folder\n    usedBytes\n    quotaBytes\n    revoked\n    lastSeenAt\n  }\n": types.BackupGuestFieldsFragmentDoc,
    "\n  query BackupsPage {\n    backupDestinations {\n      ...BackupDestinationFields\n    }\n    backups {\n      ...BackupFields\n    }\n    restoreJobs {\n      ...RestoreJobFields\n    }\n    backupGuests {\n      ...BackupGuestFields\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n": types.BackupsPageDocument,
    "\n  query BackupsOverview {\n    backups {\n      id\n      name\n      enabled\n      schedule\n      nextRunAt\n      lastRun {\n        id\n        status\n        finishedAt\n        requestedAt\n      }\n    }\n  }\n": types.BackupsOverviewDocument,
    "\n  query BackupOptions {\n    libraries {\n      id\n      name\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n    }\n    playlists {\n      id\n      name\n      trackCount\n      owner {\n        id\n        name\n      }\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n  }\n": types.BackupOptionsDocument,
    "\n  query BackupAlbumSearch($search: String!) {\n    albums(search: $search, first: 20) {\n      items {\n        id\n        title\n        year\n        artist {\n          name\n        }\n      }\n    }\n  }\n": types.BackupAlbumSearchDocument,
    "\n  query BackupRuns($backupId: ID!) {\n    backupRuns(backupId: $backupId) {\n      ...BackupJobFields\n    }\n  }\n": types.BackupRunsDocument,
    "\n  subscription BackupJobUpdated {\n    backupJobUpdated {\n      ...BackupJobFields\n    }\n  }\n": types.BackupJobUpdatedDocument,
    "\n  subscription RestoreJobUpdated {\n    restoreJobUpdated {\n      ...RestoreJobFields\n    }\n  }\n": types.RestoreJobUpdatedDocument,
    "\n  mutation CreateBackupDestination($input: CreateBackupDestinationInput!) {\n    createBackupDestination(input: $input) {\n      ...BackupDestinationFields\n    }\n  }\n": types.CreateBackupDestinationDocument,
    "\n  mutation TestBackupDestination($id: ID!) {\n    testBackupDestination(id: $id) {\n      success\n      errorCode\n    }\n  }\n": types.TestBackupDestinationDocument,
    "\n  mutation DeleteBackupDestination($id: ID!) {\n    deleteBackupDestination(id: $id)\n  }\n": types.DeleteBackupDestinationDocument,
    "\n  mutation CreateBackup($input: CreateBackupInput!) {\n    createBackup(input: $input) {\n      ...BackupFields\n    }\n  }\n": types.CreateBackupDocument,
    "\n  mutation UpdateBackup($id: ID!, $input: UpdateBackupInput!) {\n    updateBackup(id: $id, input: $input) {\n      ...BackupFields\n    }\n  }\n": types.UpdateBackupDocument,
    "\n  mutation DeleteBackup($id: ID!) {\n    deleteBackup(id: $id)\n  }\n": types.DeleteBackupDocument,
    "\n  mutation RunBackup($id: ID!) {\n    runBackup(id: $id) {\n      ...BackupJobFields\n    }\n  }\n": types.RunBackupDocument,
    "\n  mutation CancelBackupRun($id: ID!) {\n    cancelBackupRun(id: $id) {\n      ...BackupJobFields\n    }\n  }\n": types.CancelBackupRunDocument,
    "\n  mutation CreateBackupInvitation($input: CreateBackupInvitationInput!) {\n    createBackupInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n": types.CreateBackupInvitationDocument,
    "\n  query StoredBackups($destinationId: ID!) {\n    storedBackups(destinationId: $destinationId) {\n      folder\n      id\n      name\n      scope\n      machine\n      arcanistVersion\n      updatedAt\n      snapshots {\n        name\n        createdAt\n        files\n        bytes\n        userData\n        server\n      }\n      libraries {\n        folder\n        name\n        sourceId\n      }\n    }\n  }\n": types.StoredBackupsDocument,
    "\n  query BackupSnapshotContents($destinationId: ID!, $folder: String!, $snapshot: String!) {\n    backupSnapshotContents(destinationId: $destinationId, folder: $folder, snapshot: $snapshot) {\n      libraries {\n        folder\n        name\n        files\n        bytes\n      }\n      albums {\n        id\n        title\n        artist\n        year\n        paths\n      }\n      playlists {\n        owner\n        name\n        tracks\n        paths\n      }\n      users {\n        userName\n        displayName\n        ratings\n        plays\n        playlists\n        userId\n      }\n    }\n  }\n": types.BackupSnapshotContentsDocument,
    "\n  mutation StartRestore($input: StartRestoreInput!) {\n    startRestore(input: $input) {\n      ...RestoreJobFields\n    }\n  }\n": types.StartRestoreDocument,
    "\n  mutation CancelRestore($id: ID!) {\n    cancelRestore(id: $id) {\n      ...RestoreJobFields\n    }\n  }\n": types.CancelRestoreDocument,
};

/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 *
 *
 * @example
 * ```ts
 * const query = graphql(`query GetUser($id: ID!) { user(id: $id) { name } }`);
 * ```
 *
 * The query argument is unknown!
 * Please regenerate the types.
 */
export function graphql(source: string): unknown;

/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment ScanJobFields on ScanJob {\n    id\n    libraryId\n    kind\n    status\n    requestedAt\n    startedAt\n    finishedAt\n    filesSeen\n    filesAdded\n    filesUpdated\n    filesMissing\n    errors\n    errorCode\n    progress {\n      phase\n      filesFound\n      filesToRead\n      filesRead\n    }\n  }\n"): (typeof documents)["\n  fragment ScanJobFields on ScanJob {\n    id\n    libraryId\n    kind\n    status\n    requestedAt\n    startedAt\n    finishedAt\n    filesSeen\n    filesAdded\n    filesUpdated\n    filesMissing\n    errors\n    errorCode\n    progress {\n      phase\n      filesFound\n      filesToRead\n      filesRead\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ServerInfo {\n    serverInfo {\n      name\n      version\n      apiVersion\n      sourceUrl\n    }\n  }\n"): (typeof documents)["\n  query ServerInfo {\n    serverInfo {\n      name\n      version\n      apiVersion\n      sourceUrl\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Overview {\n    libraries {\n      id\n    }\n    artists(first: 1) {\n      totalCount\n    }\n    albums(first: 1) {\n      totalCount\n    }\n    tracks(first: 1) {\n      totalCount\n    }\n  }\n"): (typeof documents)["\n  query Overview {\n    libraries {\n      id\n    }\n    artists(first: 1) {\n      totalCount\n    }\n    albums(first: 1) {\n      totalCount\n    }\n    tracks(first: 1) {\n      totalCount\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalMinutes\n      watchForChanges\n      createdAt\n      lastScanAt\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"): (typeof documents)["\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalMinutes\n      watchForChanges\n      createdAt\n      lastScanAt\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment LibrarySourceMoveFields on LibrarySourceMove {\n    sourceId\n    libraryId\n    from\n    to\n    keepOriginals\n    phase\n    filesDone\n    filesTotal\n    bytesDone\n    bytesTotal\n    startedAt\n    finishedAt\n    errorCode\n  }\n"): (typeof documents)["\n  fragment LibrarySourceMoveFields on LibrarySourceMove {\n    sourceId\n    libraryId\n    from\n    to\n    keepOriginals\n    phase\n    filesDone\n    filesTotal\n    bytesDone\n    bytesTotal\n    startedAt\n    finishedAt\n    errorCode\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query LibrarySourceMoves {\n    librarySourceMoves {\n      ...LibrarySourceMoveFields\n    }\n  }\n"): (typeof documents)["\n  query LibrarySourceMoves {\n    librarySourceMoves {\n      ...LibrarySourceMoveFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation MoveLibrarySource($id: ID!, $input: MoveLibrarySourceInput!) {\n    moveLibrarySource(id: $id, input: $input) {\n      ...LibrarySourceMoveFields\n    }\n  }\n"): (typeof documents)["\n  mutation MoveLibrarySource($id: ID!, $input: MoveLibrarySourceInput!) {\n    moveLibrarySource(id: $id, input: $input) {\n      ...LibrarySourceMoveFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ServerDirectories($path: String) {\n    serverDirectories(path: $path) {\n      path\n      parent\n      directories {\n        name\n        path\n        isHidden\n      }\n    }\n  }\n"): (typeof documents)["\n  query ServerDirectories($path: String) {\n    serverDirectories(path: $path) {\n      path\n      parent\n      directories {\n        name\n        path\n        isHidden\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateServerDirectory($input: CreateServerDirectoryInput!) {\n    createServerDirectory(input: $input) {\n      name\n      path\n      isHidden\n    }\n  }\n"): (typeof documents)["\n  mutation CreateServerDirectory($input: CreateServerDirectoryInput!) {\n    createServerDirectory(input: $input) {\n      name\n      path\n      isHidden\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ScanJobs($libraryId: ID, $limit: Int) {\n    scanJobs(libraryId: $libraryId, limit: $limit) {\n      ...ScanJobFields\n    }\n  }\n"): (typeof documents)["\n  query ScanJobs($libraryId: ID, $limit: Int) {\n    scanJobs(libraryId: $libraryId, limit: $limit) {\n      ...ScanJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation StartScan($libraryId: ID!, $full: Boolean!) {\n    startScan(libraryId: $libraryId, full: $full) {\n      ...ScanJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation StartScan($libraryId: ID!, $full: Boolean!) {\n    startScan(libraryId: $libraryId, full: $full) {\n      ...ScanJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelScan($id: ID!) {\n    cancelScan(id: $id)\n  }\n"): (typeof documents)["\n  mutation CancelScan($id: ID!) {\n    cancelScan(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  subscription ScanJobUpdated {\n    scanJobUpdated {\n      ...ScanJobFields\n    }\n  }\n"): (typeof documents)["\n  subscription ScanJobUpdated {\n    scanJobUpdated {\n      ...ScanJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment UserFields on UserAccount {\n    id\n    userName\n    displayName\n    role\n    permissions\n    isDisabled\n    createdAt\n    lastLoginAt\n    libraryIds\n  }\n"): (typeof documents)["\n  fragment UserFields on UserAccount {\n    id\n    userName\n    displayName\n    role\n    permissions\n    isDisabled\n    createdAt\n    lastLoginAt\n    libraryIds\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Users {\n    users {\n      ...UserFields\n    }\n    libraries {\n      id\n      name\n      visibleToAll\n    }\n  }\n"): (typeof documents)["\n  query Users {\n    users {\n      ...UserFields\n    }\n    libraries {\n      id\n      name\n      visibleToAll\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateUser($input: CreateUserInput!) {\n    createUser(input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation CreateUser($input: CreateUserInput!) {\n    createUser(input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateUser($id: ID!, $input: UpdateUserInput!) {\n    updateUser(id: $id, input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateUser($id: ID!, $input: UpdateUserInput!) {\n    updateUser(id: $id, input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetUserLibraries($id: ID!, $input: SetUserLibrariesInput!) {\n    setUserLibraries(id: $id, input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation SetUserLibraries($id: ID!, $input: SetUserLibrariesInput!) {\n    setUserLibraries(id: $id, input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ResetUserPassword($id: ID!, $input: ResetUserPasswordInput!) {\n    resetUserPassword(id: $id, input: $input)\n  }\n"): (typeof documents)["\n  mutation ResetUserPassword($id: ID!, $input: ResetUserPasswordInput!) {\n    resetUserPassword(id: $id, input: $input)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteUser($id: ID!) {\n    deleteUser(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteUser($id: ID!) {\n    deleteUser(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query MySessions {\n    mySessions {\n      id\n      deviceName\n      userAgent\n      ipAddress\n      createdAt\n      lastSeenAt\n      expiresAt\n      isCurrent\n    }\n  }\n"): (typeof documents)["\n  query MySessions {\n    mySessions {\n      id\n      deviceName\n      userAgent\n      ipAddress\n      createdAt\n      lastSeenAt\n      expiresAt\n      isCurrent\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RevokeMySession($id: ID!) {\n    revokeMySession(id: $id)\n  }\n"): (typeof documents)["\n  mutation RevokeMySession($id: ID!) {\n    revokeMySession(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ChangeMyPassword($input: ChangeMyPasswordInput!) {\n    changeMyPassword(input: $input)\n  }\n"): (typeof documents)["\n  mutation ChangeMyPassword($input: ChangeMyPasswordInput!) {\n    changeMyPassword(input: $input)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment AnalysisProgressFields on AnalysisProgress {\n    activity\n    enabled\n    pending\n    analyzed\n    needsFfmpeg\n    failed\n    filesPerMinute\n    lastAnalyzedAt\n    error\n  }\n"): (typeof documents)["\n  fragment AnalysisProgressFields on AnalysisProgress {\n    activity\n    enabled\n    pending\n    analyzed\n    needsFfmpeg\n    failed\n    filesPerMinute\n    lastAnalyzedAt\n    error\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query AnalysisProgress {\n    analysisProgress {\n      ...AnalysisProgressFields\n    }\n  }\n"): (typeof documents)["\n  query AnalysisProgress {\n    analysisProgress {\n      ...AnalysisProgressFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetAnalysisEnabled($enabled: Boolean!) {\n    setAnalysisEnabled(enabled: $enabled) {\n      ...AnalysisProgressFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetAnalysisEnabled($enabled: Boolean!) {\n    setAnalysisEnabled(enabled: $enabled) {\n      ...AnalysisProgressFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  subscription AnalysisProgressChanged {\n    analysisProgressChanged {\n      ...AnalysisProgressFields\n    }\n  }\n"): (typeof documents)["\n  subscription AnalysisProgressChanged {\n    analysisProgressChanged {\n      ...AnalysisProgressFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment FfmpegStatusFields on FfmpegStatus {\n    available\n    path\n    version\n    source\n    configuredPath\n    configuredPathWorks\n    canDownload\n    download {\n      state\n      bytesReceived\n      totalBytes\n      version\n      error\n    }\n  }\n"): (typeof documents)["\n  fragment FfmpegStatusFields on FfmpegStatus {\n    available\n    path\n    version\n    source\n    configuredPath\n    configuredPathWorks\n    canDownload\n    download {\n      state\n      bytesReceived\n      totalBytes\n      version\n      error\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment ServerConfigurationFields on ServerConfiguration {\n    defaultLanguage\n    ratingMode\n    shareAnonymousStatistics\n    ffmpegPath\n    onlineServices {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    privacy {\n      noTelemetry\n      externalServices\n    }\n  }\n"): (typeof documents)["\n  fragment ServerConfigurationFields on ServerConfiguration {\n    defaultLanguage\n    ratingMode\n    shareAnonymousStatistics\n    ffmpegPath\n    onlineServices {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    privacy {\n      noTelemetry\n      externalServices\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Privacy {\n    privacy {\n      noTelemetry\n      externalServices\n    }\n    anonymousStatistics\n  }\n"): (typeof documents)["\n  query Privacy {\n    privacy {\n      noTelemetry\n      externalServices\n    }\n    anonymousStatistics\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetPrivacy($input: SetPrivacyInput!) {\n    setPrivacy(input: $input) {\n      noTelemetry\n      externalServices\n    }\n  }\n"): (typeof documents)["\n  mutation SetPrivacy($input: SetPrivacyInput!) {\n    setPrivacy(input: $input) {\n      noTelemetry\n      externalServices\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query SetupExtras {\n    users {\n      id\n      userName\n      displayName\n      role\n    }\n    p2pStatus {\n      enabled\n      customName\n      relay\n      relayUrl\n      bandwidthKbps\n      maxStreams\n    }\n    extensions {\n      id\n      builtIn\n      enabled\n    }\n  }\n"): (typeof documents)["\n  query SetupExtras {\n    users {\n      id\n      userName\n      displayName\n      role\n    }\n    p2pStatus {\n      enabled\n      customName\n      relay\n      relayUrl\n      bandwidthKbps\n      maxStreams\n    }\n    extensions {\n      id\n      builtIn\n      enabled\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ServerConfiguration {\n    serverConfiguration {\n      ...ServerConfigurationFields\n    }\n  }\n"): (typeof documents)["\n  query ServerConfiguration {\n    serverConfiguration {\n      ...ServerConfigurationFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ReconfigureServer($input: ReconfigureServerInput!) {\n    reconfigureServer(input: $input) {\n      ...ServerConfigurationFields\n    }\n  }\n"): (typeof documents)["\n  mutation ReconfigureServer($input: ReconfigureServerInput!) {\n    reconfigureServer(input: $input) {\n      ...ServerConfigurationFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetAnonymousStatistics($enabled: Boolean!) {\n    setAnonymousStatistics(enabled: $enabled)\n  }\n"): (typeof documents)["\n  mutation SetAnonymousStatistics($enabled: Boolean!) {\n    setAnonymousStatistics(enabled: $enabled)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ResetSettings {\n    resetSettings {\n      restartRequired\n    }\n  }\n"): (typeof documents)["\n  mutation ResetSettings {\n    resetSettings {\n      restartRequired\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ResetUserData {\n    resetUserData {\n      restartRequired\n    }\n  }\n"): (typeof documents)["\n  mutation ResetUserData {\n    resetUserData {\n      restartRequired\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation FactoryReset($input: FactoryResetInput!) {\n    factoryReset(input: $input) {\n      restartRequired\n    }\n  }\n"): (typeof documents)["\n  mutation FactoryReset($input: FactoryResetInput!) {\n    factoryReset(input: $input) {\n      restartRequired\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Ffmpeg {\n    ffmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n"): (typeof documents)["\n  query Ffmpeg {\n    ffmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetFfmpegPath($path: String) {\n    setFfmpegPath(path: $path) {\n      ...FfmpegStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetFfmpegPath($path: String) {\n    setFfmpegPath(path: $path) {\n      ...FfmpegStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation InstallFfmpeg {\n    installFfmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation InstallFfmpeg {\n    installFfmpeg {\n      ...FfmpegStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DuplicateTrackFields on Track {\n    id\n    title\n    durationMs\n    trackNumber\n    discNumber\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n    files {\n      id\n      format\n      bitRate\n      sampleRate\n      bitDepth\n    }\n  }\n"): (typeof documents)["\n  fragment DuplicateTrackFields on Track {\n    id\n    title\n    durationMs\n    trackNumber\n    discNumber\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n    files {\n      id\n      format\n      bitRate\n      sampleRate\n      bitDepth\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DuplicateCandidates($status: DuplicateStatus) {\n    duplicateCandidates(status: $status, first: 200) {\n      id\n      similarity\n      status\n      detectedAt\n      trackA {\n        ...DuplicateTrackFields\n      }\n      trackB {\n        ...DuplicateTrackFields\n      }\n    }\n  }\n"): (typeof documents)["\n  query DuplicateCandidates($status: DuplicateStatus) {\n    duplicateCandidates(status: $status, first: 200) {\n      id\n      similarity\n      status\n      detectedAt\n      trackA {\n        ...DuplicateTrackFields\n      }\n      trackB {\n        ...DuplicateTrackFields\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation MergeDuplicate($id: ID!, $keepTrackId: ID!) {\n    mergeDuplicate(id: $id, keepTrackId: $keepTrackId) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation MergeDuplicate($id: ID!, $keepTrackId: ID!) {\n    mergeDuplicate(id: $id, keepTrackId: $keepTrackId) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DismissDuplicate($id: ID!) {\n    dismissDuplicate(id: $id)\n  }\n"): (typeof documents)["\n  mutation DismissDuplicate($id: ID!) {\n    dismissDuplicate(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment OnlineServicesFields on OnlineServicesStatus {\n    settings {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    hasAcoustIdKey\n    hasOwnAcoustIdKey\n    services {\n      service\n      enabled\n      ready\n      pending\n      found\n      notFound\n      failed\n      lastRequestAt\n      lastError\n    }\n  }\n"): (typeof documents)["\n  fragment OnlineServicesFields on OnlineServicesStatus {\n    settings {\n      musicBrainz\n      acoustId\n      coverArtArchive\n      lyrics\n    }\n    hasAcoustIdKey\n    hasOwnAcoustIdKey\n    services {\n      service\n      enabled\n      ready\n      pending\n      found\n      notFound\n      failed\n      lastRequestAt\n      lastError\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query OnlineServices {\n    onlineServices {\n      ...OnlineServicesFields\n    }\n  }\n"): (typeof documents)["\n  query OnlineServices {\n    onlineServices {\n      ...OnlineServicesFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetOnlineServices($input: SetOnlineServicesInput!) {\n    setOnlineServices(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetOnlineServices($input: SetOnlineServicesInput!) {\n    setOnlineServices(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetAcoustIdKey($input: SetAcoustIdKeyInput!) {\n    setAcoustIdKey(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetAcoustIdKey($input: SetAcoustIdKeyInput!) {\n    setAcoustIdKey(input: $input) {\n      ...OnlineServicesFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment OrganizeRunFields on OrganizeRun {\n    id\n    libraryId\n    kind\n    status\n    template\n    startedAt\n    finishedAt\n    undoneAt\n    total\n    moved\n    failed\n    errorCode\n  }\n"): (typeof documents)["\n  fragment OrganizeRunFields on OrganizeRun {\n    id\n    libraryId\n    kind\n    status\n    template\n    startedAt\n    finishedAt\n    undoneAt\n    total\n    moved\n    failed\n    errorCode\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query OrganizePreview($libraryId: ID!, $template: String) {\n    organizePreview(libraryId: $libraryId, template: $template) {\n      template\n      filesToMove\n      filesInPlace\n      otherFilesToMove\n      unavailableSourceIds\n      moves {\n        sourceId\n        from\n        to\n      }\n    }\n  }\n"): (typeof documents)["\n  query OrganizePreview($libraryId: ID!, $template: String) {\n    organizePreview(libraryId: $libraryId, template: $template) {\n      template\n      filesToMove\n      filesInPlace\n      otherFilesToMove\n      unavailableSourceIds\n      moves {\n        sourceId\n        from\n        to\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query OrganizeRuns($libraryId: ID!) {\n    organizeRuns(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n"): (typeof documents)["\n  query OrganizeRuns($libraryId: ID!) {\n    organizeRuns(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateLibraryOrganization($libraryId: ID!, $input: UpdateLibraryOrganizationInput!) {\n    updateLibraryOrganization(libraryId: $libraryId, input: $input) {\n      id\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateLibraryOrganization($libraryId: ID!, $input: UpdateLibraryOrganizationInput!) {\n    updateLibraryOrganization(libraryId: $libraryId, input: $input) {\n      id\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation OrganizeLibrary($libraryId: ID!) {\n    organizeLibrary(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n"): (typeof documents)["\n  mutation OrganizeLibrary($libraryId: ID!) {\n    organizeLibrary(libraryId: $libraryId) {\n      ...OrganizeRunFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UndoOrganizeRun($id: ID!) {\n    undoOrganizeRun(id: $id) {\n      ...OrganizeRunFields\n    }\n  }\n"): (typeof documents)["\n  mutation UndoOrganizeRun($id: ID!) {\n    undoOrganizeRun(id: $id) {\n      ...OrganizeRunFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    mode\n    kind\n    uploadToCloud\n    uploadAccount\n    createdAt\n    available\n  }\n"): (typeof documents)["\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    mode\n    kind\n    uploadToCloud\n    uploadAccount\n    createdAt\n    available\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DropFolders {\n    dropFolders {\n      ...DropFolderFields\n    }\n  }\n"): (typeof documents)["\n  query DropFolders {\n    dropFolders {\n      ...DropFolderFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateDropFolder($input: DropFolderInput!) {\n    createDropFolder(input: $input) {\n      ...DropFolderFields\n    }\n  }\n"): (typeof documents)["\n  mutation CreateDropFolder($input: DropFolderInput!) {\n    createDropFolder(input: $input) {\n      ...DropFolderFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateDropFolder($id: ID!, $input: DropFolderInput!) {\n    updateDropFolder(id: $id, input: $input) {\n      ...DropFolderFields\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateDropFolder($id: ID!, $input: DropFolderInput!) {\n    updateDropFolder(id: $id, input: $input) {\n      ...DropFolderFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteDropFolder($id: ID!) {\n    deleteDropFolder(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteDropFolder($id: ID!) {\n    deleteDropFolder(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment ImportItemFields on ImportItem {\n    id\n    dropFolderId\n    path\n    size\n    status\n    reason\n    destinationPath\n    trackId\n    createdAt\n    updatedAt\n    details {\n      title\n      artist\n      album\n      albumArtist\n      year\n      trackNumber\n      discNumber\n      genre\n      format\n      bitRate\n      sampleRate\n      bitDepth\n      durationMs\n    }\n  }\n"): (typeof documents)["\n  fragment ImportItemFields on ImportItem {\n    id\n    dropFolderId\n    path\n    size\n    status\n    reason\n    destinationPath\n    trackId\n    createdAt\n    updatedAt\n    details {\n      title\n      artist\n      album\n      albumArtist\n      year\n      trackNumber\n      discNumber\n      genre\n      format\n      bitRate\n      sampleRate\n      bitDepth\n      durationMs\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ImportQueue($status: ImportStatus) {\n    importSummary {\n      pending\n      needsReview\n      duplicate\n      failed\n      ignored\n      imported\n    }\n    importItems(status: $status, first: 200) {\n      ...ImportItemFields\n    }\n  }\n"): (typeof documents)["\n  query ImportQueue($status: ImportStatus) {\n    importSummary {\n      pending\n      needsReview\n      duplicate\n      failed\n      ignored\n      imported\n    }\n    importItems(status: $status, first: 200) {\n      ...ImportItemFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AcceptImportItem($id: ID!, $tags: ImportTagsInput) {\n    acceptImportItem(id: $id, tags: $tags) {\n      ...ImportItemFields\n    }\n  }\n"): (typeof documents)["\n  mutation AcceptImportItem($id: ID!, $tags: ImportTagsInput) {\n    acceptImportItem(id: $id, tags: $tags) {\n      ...ImportItemFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RetryImportItem($id: ID!) {\n    retryImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"): (typeof documents)["\n  mutation RetryImportItem($id: ID!) {\n    retryImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation IgnoreImportItem($id: ID!) {\n    ignoreImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"): (typeof documents)["\n  mutation IgnoreImportItem($id: ID!) {\n    ignoreImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DiscardImportItem($id: ID!) {\n    discardImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"): (typeof documents)["\n  mutation DiscardImportItem($id: ID!) {\n    discardImportItem(id: $id) {\n      ...ImportItemFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query RatingMode {\n    serverInfo {\n      ratingMode\n    }\n  }\n"): (typeof documents)["\n  query RatingMode {\n    serverInfo {\n      ratingMode\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetRatingMode($mode: RatingMode!) {\n    setRatingMode(mode: $mode)\n  }\n"): (typeof documents)["\n  mutation SetRatingMode($mode: RatingMode!) {\n    setRatingMode(mode: $mode)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment StatsTrackFields on Track {\n    id\n    title\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n  }\n"): (typeof documents)["\n  fragment StatsTrackFields on Track {\n    id\n    title\n    album {\n      id\n      title\n    }\n    artists {\n      artistId\n      name\n      role\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query ServerStats($period: StatsPeriod!, $timeZone: String) {\n    serverStats(period: $period, timeZone: $timeZone) {\n      library {\n        artists\n        albums\n        tracks\n        files\n        durationMs\n        sizeBytes\n        formats {\n          format\n          files\n          sizeBytes\n        }\n      }\n      listening {\n        period\n        from\n        plays\n        listenedMs\n        distinctTracks\n        bucket\n        timeline {\n          start\n          plays\n          listenedMs\n        }\n        topTracks {\n          plays\n          track {\n            ...StatsTrackFields\n          }\n        }\n        topArtists {\n          plays\n          artist {\n            id\n            name\n          }\n        }\n        topAlbums {\n          plays\n          album {\n            id\n            title\n            artworkId\n            artist {\n              id\n              name\n            }\n          }\n        }\n      }\n      users {\n        userId\n        userName\n        displayName\n        plays\n        listenedMs\n        lastPlayedAt\n      }\n    }\n  }\n"): (typeof documents)["\n  query ServerStats($period: StatsPeriod!, $timeZone: String) {\n    serverStats(period: $period, timeZone: $timeZone) {\n      library {\n        artists\n        albums\n        tracks\n        files\n        durationMs\n        sizeBytes\n        formats {\n          format\n          files\n          sizeBytes\n        }\n      }\n      listening {\n        period\n        from\n        plays\n        listenedMs\n        distinctTracks\n        bucket\n        timeline {\n          start\n          plays\n          listenedMs\n        }\n        topTracks {\n          plays\n          track {\n            ...StatsTrackFields\n          }\n        }\n        topArtists {\n          plays\n          artist {\n            id\n            name\n          }\n        }\n        topAlbums {\n          plays\n          album {\n            id\n            title\n            artworkId\n            artist {\n              id\n              name\n            }\n          }\n        }\n      }\n      users {\n        userId\n        userName\n        displayName\n        plays\n        listenedMs\n        lastPlayedAt\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment AppPasswordFields on AppPassword {\n    id\n    name\n    createdAt\n    lastUsedAt\n  }\n"): (typeof documents)["\n  fragment AppPasswordFields on AppPassword {\n    id\n    name\n    createdAt\n    lastUsedAt\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query AppPasswords {\n    appPasswords {\n      ...AppPasswordFields\n    }\n  }\n"): (typeof documents)["\n  query AppPasswords {\n    appPasswords {\n      ...AppPasswordFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateAppPassword($name: String!) {\n    createAppPassword(name: $name) {\n      secret\n      appPassword {\n        ...AppPasswordFields\n      }\n    }\n  }\n"): (typeof documents)["\n  mutation CreateAppPassword($name: String!) {\n    createAppPassword(name: $name) {\n      secret\n      appPassword {\n        ...AppPasswordFields\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteAppPassword($id: ID!) {\n    deleteAppPassword(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteAppPassword($id: ID!) {\n    deleteAppPassword(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment FrontendFields on Frontend {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    isDefault\n    path\n    installedAt\n  }\n"): (typeof documents)["\n  fragment FrontendFields on Frontend {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    isDefault\n    path\n    installedAt\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Frontends {\n    frontends {\n      ...FrontendFields\n    }\n  }\n"): (typeof documents)["\n  query Frontends {\n    frontends {\n      ...FrontendFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation InstallFrontendFromUrl($url: String!) {\n    installFrontendFromUrl(url: $url) {\n      ...FrontendFields\n    }\n  }\n"): (typeof documents)["\n  mutation InstallFrontendFromUrl($url: String!) {\n    installFrontendFromUrl(url: $url) {\n      ...FrontendFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetDefaultFrontend($id: String!) {\n    setDefaultFrontend(id: $id) {\n      ...FrontendFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetDefaultFrontend($id: String!) {\n    setDefaultFrontend(id: $id) {\n      ...FrontendFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UninstallFrontend($id: String!) {\n    uninstallFrontend(id: $id)\n  }\n"): (typeof documents)["\n  mutation UninstallFrontend($id: String!) {\n    uninstallFrontend(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment ExtensionFields on Extension {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    enabled\n    status\n    errorCode\n    restartRequired\n    routes\n    basePath\n  }\n"): (typeof documents)["\n  fragment ExtensionFields on Extension {\n    id\n    name\n    version\n    description\n    author\n    license\n    homepage\n    builtIn\n    enabled\n    status\n    errorCode\n    restartRequired\n    routes\n    basePath\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Extensions {\n    extensions {\n      ...ExtensionFields\n    }\n  }\n"): (typeof documents)["\n  query Extensions {\n    extensions {\n      ...ExtensionFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation InstallExtensionFromUrl($url: String!) {\n    installExtensionFromUrl(url: $url) {\n      ...ExtensionFields\n    }\n  }\n"): (typeof documents)["\n  mutation InstallExtensionFromUrl($url: String!) {\n    installExtensionFromUrl(url: $url) {\n      ...ExtensionFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetExtensionEnabled($id: String!, $enabled: Boolean!) {\n    setExtensionEnabled(id: $id, enabled: $enabled) {\n      ...ExtensionFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetExtensionEnabled($id: String!, $enabled: Boolean!) {\n    setExtensionEnabled(id: $id, enabled: $enabled) {\n      ...ExtensionFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UninstallExtension($id: String!) {\n    uninstallExtension(id: $id)\n  }\n"): (typeof documents)["\n  mutation UninstallExtension($id: String!) {\n    uninstallExtension(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment MstreamImportJobFields on MstreamImportJob {\n    id\n    kind\n    source\n    status\n    createdAt\n    finishedAt\n    librariesCreated\n    usersCreated\n    playlists\n    ratings\n    plays\n    unmatched\n    errorCode\n  }\n"): (typeof documents)["\n  fragment MstreamImportJobFields on MstreamImportJob {\n    id\n    kind\n    source\n    status\n    createdAt\n    finishedAt\n    librariesCreated\n    usersCreated\n    playlists\n    ratings\n    plays\n    unmatched\n    errorCode\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment MstreamAnalysisFields on MstreamAnalysis {\n    version\n    libraries {\n      vpath\n      root\n      rootExists\n      sourceId\n    }\n    users {\n      userName\n      isAdmin\n      vpaths\n      exists\n      canSignIn\n    }\n    playlists\n    ratedTracks\n    playedTracks\n  }\n"): (typeof documents)["\n  fragment MstreamAnalysisFields on MstreamAnalysis {\n    version\n    libraries {\n      vpath\n      root\n      rootExists\n      sourceId\n    }\n    users {\n      userName\n      isAdmin\n      vpaths\n      exists\n      canSignIn\n    }\n    playlists\n    ratedTracks\n    playedTracks\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query MstreamImports {\n    mstreamImports {\n      ...MstreamImportJobFields\n    }\n  }\n"): (typeof documents)["\n  query MstreamImports {\n    mstreamImports {\n      ...MstreamImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DlnaImportJobFields on DlnaImportJob {\n    id\n    serverName\n    folders\n    libraryId\n    libraryName\n    status\n    found\n    copied\n    duplicates\n    failed\n    copiedBytes\n    createdAt\n    finishedAt\n    errorCode\n    failures {\n      title\n      artist\n      album\n      reason\n    }\n  }\n"): (typeof documents)["\n  fragment DlnaImportJobFields on DlnaImportJob {\n    id\n    serverName\n    folders\n    libraryId\n    libraryName\n    status\n    found\n    copied\n    duplicates\n    failed\n    copiedBytes\n    createdAt\n    finishedAt\n    errorCode\n    failures {\n      title\n      artist\n      album\n      reason\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaImports {\n    dlnaImports {\n      ...DlnaImportJobFields\n    }\n  }\n"): (typeof documents)["\n  query DlnaImports {\n    dlnaImports {\n      ...DlnaImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaMediaServers {\n    dlnaMediaServers {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n"): (typeof documents)["\n  query DlnaMediaServers {\n    dlnaMediaServers {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaMediaServer($location: String!) {\n    dlnaMediaServer(location: $location) {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n"): (typeof documents)["\n  query DlnaMediaServer($location: String!) {\n    dlnaMediaServer(location: $location) {\n      id\n      name\n      manufacturer\n      model\n      location\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BrowseDlnaMediaServer($location: String!, $folderId: String) {\n    browseDlnaMediaServer(location: $location, folderId: $folderId) {\n      id\n      tracks\n      truncated\n      folders {\n        id\n        title\n        childCount\n      }\n    }\n  }\n"): (typeof documents)["\n  query BrowseDlnaMediaServer($location: String!, $folderId: String) {\n    browseDlnaMediaServer(location: $location, folderId: $folderId) {\n      id\n      tracks\n      truncated\n      folders {\n        id\n        title\n        childCount\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation StartDlnaImport($input: StartDlnaImportInput!) {\n    startDlnaImport(input: $input) {\n      ...DlnaImportJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation StartDlnaImport($input: StartDlnaImportInput!) {\n    startDlnaImport(input: $input) {\n      ...DlnaImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteDlnaImport($id: ID!) {\n    deleteDlnaImport(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteDlnaImport($id: ID!) {\n    deleteDlnaImport(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteMstreamImport($id: ID!) {\n    deleteMstreamImport(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteMstreamImport($id: ID!) {\n    deleteMstreamImport(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ClearImportHistory {\n    clearImportHistory\n  }\n"): (typeof documents)["\n  mutation ClearImportHistory {\n    clearImportHistory\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelDlnaImport($id: ID!) {\n    cancelDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation CancelDlnaImport($id: ID!) {\n    cancelDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RetryDlnaImport($id: ID!) {\n    retryDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation RetryDlnaImport($id: ID!) {\n    retryDlnaImport(id: $id) {\n      ...DlnaImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AnalyzeMstreamFolder($folder: String!) {\n    analyzeMstreamFolder(folder: $folder) {\n      ...MstreamAnalysisFields\n    }\n  }\n"): (typeof documents)["\n  mutation AnalyzeMstreamFolder($folder: String!) {\n    analyzeMstreamFolder(folder: $folder) {\n      ...MstreamAnalysisFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AnalyzeMstreamServer($input: MstreamServerInput!) {\n    analyzeMstreamServer(input: $input) {\n      ...MstreamAnalysisFields\n    }\n  }\n"): (typeof documents)["\n  mutation AnalyzeMstreamServer($input: MstreamServerInput!) {\n    analyzeMstreamServer(input: $input) {\n      ...MstreamAnalysisFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ImportMstream($input: ImportMstreamInput!) {\n    importMstream(input: $input) {\n      ...MstreamImportJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation ImportMstream($input: ImportMstreamInput!) {\n    importMstream(input: $input) {\n      ...MstreamImportJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DlnaStatusFields on DlnaStatus {\n    enabled\n    name\n    friendlyName\n    libraryIds\n    sections\n    ratingsUserId\n    addresses\n    convertsFormats\n  }\n"): (typeof documents)["\n  fragment DlnaStatusFields on DlnaStatus {\n    enabled\n    name\n    friendlyName\n    libraryIds\n    sections\n    ratingsUserId\n    addresses\n    convertsFormats\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment MstreamStatusFields on MstreamStatus {\n    enabled\n    starThreshold\n    passwordSignIn\n    features\n    quickConnect\n    p2pEnabled\n    pairingCode\n  }\n"): (typeof documents)["\n  fragment MstreamStatusFields on MstreamStatus {\n    enabled\n    starThreshold\n    passwordSignIn\n    features\n    quickConnect\n    p2pEnabled\n    pairingCode\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Mstream {\n    mstream {\n      ...MstreamStatusFields\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n"): (typeof documents)["\n  query Mstream {\n    mstream {\n      ...MstreamStatusFields\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetMstreamSettings($input: MstreamSettingsInput!) {\n    setMstreamSettings(input: $input) {\n      ...MstreamStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetMstreamSettings($input: MstreamSettingsInput!) {\n    setMstreamSettings(input: $input) {\n      ...MstreamStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RotateMstreamQuickConnect {\n    rotateMstreamQuickConnect {\n      ...MstreamStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation RotateMstreamQuickConnect {\n    rotateMstreamQuickConnect {\n      ...MstreamStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Dlna {\n    dlna {\n      ...DlnaStatusFields\n    }\n    libraries {\n      id\n      name\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n"): (typeof documents)["\n  query Dlna {\n    dlna {\n      ...DlnaStatusFields\n    }\n    libraries {\n      id\n      name\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n    serverInfo {\n      ratingMode\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetDlnaSettings($input: DlnaSettingsInput!) {\n    setDlnaSettings(input: $input) {\n      ...DlnaStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetDlnaSettings($input: DlnaSettingsInput!) {\n    setDlnaSettings(input: $input) {\n      ...DlnaStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DlnaRendererFields on DlnaRenderer {\n    id\n    name\n    manufacturer\n    model\n    address\n    manual\n  }\n"): (typeof documents)["\n  fragment DlnaRendererFields on DlnaRenderer {\n    id\n    name\n    manufacturer\n    model\n    address\n    manual\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaRenderers {\n    dlnaRenderers {\n      ...DlnaRendererFields\n    }\n  }\n"): (typeof documents)["\n  query DlnaRenderers {\n    dlnaRenderers {\n      ...DlnaRendererFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DlnaRendererStateFields on DlnaRendererState {\n    renderer {\n      ...DlnaRendererFields\n    }\n    state\n    positionMs\n    durationMs\n    volume\n    queue\n    queueIndex\n    track {\n      id\n      title\n      artworkId\n      album {\n        title\n      }\n      artists {\n        name\n        role\n      }\n    }\n  }\n"): (typeof documents)["\n  fragment DlnaRendererStateFields on DlnaRendererState {\n    renderer {\n      ...DlnaRendererFields\n    }\n    state\n    positionMs\n    durationMs\n    volume\n    queue\n    queueIndex\n    track {\n      id\n      title\n      artworkId\n      album {\n        title\n      }\n      artists {\n        name\n        role\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaRenderer($id: String!) {\n    dlnaRenderer(id: $id) {\n      ...DlnaRendererStateFields\n    }\n  }\n"): (typeof documents)["\n  query DlnaRenderer($id: String!) {\n    dlnaRenderer(id: $id) {\n      ...DlnaRendererStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AddDlnaRenderer($url: String!) {\n    addDlnaRenderer(url: $url) {\n      ...DlnaRendererFields\n    }\n  }\n"): (typeof documents)["\n  mutation AddDlnaRenderer($url: String!) {\n    addDlnaRenderer(url: $url) {\n      ...DlnaRendererFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation PlayOnDlnaRenderer($input: PlayOnDlnaRendererInput!) {\n    playOnDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n"): (typeof documents)["\n  mutation PlayOnDlnaRenderer($input: PlayOnDlnaRendererInput!) {\n    playOnDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ControlDlnaRenderer($input: ControlDlnaRendererInput!) {\n    controlDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n"): (typeof documents)["\n  mutation ControlDlnaRenderer($input: ControlDlnaRendererInput!) {\n    controlDlnaRenderer(input: $input) {\n      ...DlnaRendererStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetDlnaRendererVolume($rendererId: String!, $volume: Int!) {\n    setDlnaRendererVolume(rendererId: $rendererId, volume: $volume) {\n      ...DlnaRendererStateFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetDlnaRendererVolume($rendererId: String!, $volume: Int!) {\n    setDlnaRendererVolume(rendererId: $rendererId, volume: $volume) {\n      ...DlnaRendererStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaAlbumSearch($search: String!) {\n    albums(search: $search, first: 8) {\n      items {\n        id\n        title\n        artist {\n          name\n        }\n      }\n    }\n  }\n"): (typeof documents)["\n  query DlnaAlbumSearch($search: String!) {\n    albums(search: $search, first: 8) {\n      items {\n        id\n        title\n        artist {\n          name\n        }\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query DlnaAlbumTracks($albumId: ID!) {\n    tracks(albumId: $albumId) {\n      items {\n        id\n      }\n    }\n  }\n"): (typeof documents)["\n  query DlnaAlbumTracks($albumId: ID!) {\n    tracks(albumId: $albumId) {\n      items {\n        id\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment P2PStatusFields on P2PStatus {\n    enabled\n    available\n    running\n    errorCode\n    nodeId\n    name\n    customName\n    relay\n    relayUrl\n    homeRelay\n    relayConnected\n    bandwidthKbps\n    maxStreams\n    connectedPeers\n    activeStreams\n    currentBytesPerSecond\n    peakBytesPerSecond\n  }\n"): (typeof documents)["\n  fragment P2PStatusFields on P2PStatus {\n    enabled\n    available\n    running\n    errorCode\n    nodeId\n    name\n    customName\n    relay\n    relayUrl\n    homeRelay\n    relayConnected\n    bandwidthKbps\n    maxStreams\n    connectedPeers\n    activeStreams\n    currentBytesPerSecond\n    peakBytesPerSecond\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment NetworkPeerFields on NetworkPeer {\n    id\n    kind\n    source\n    name\n    nodeId\n    userId\n    userName\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    libraryIds\n    createdAt\n    expiresAt\n    revokedAt\n    lastSeenAt\n    online\n    activeStreams\n    bytesSent\n    bytesReceived\n    currentBytesPerSecond\n    averageBytesPerSecond\n    peakBytesPerSecond\n    streams\n  }\n"): (typeof documents)["\n  fragment NetworkPeerFields on NetworkPeer {\n    id\n    kind\n    source\n    name\n    nodeId\n    userId\n    userName\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    libraryIds\n    createdAt\n    expiresAt\n    revokedAt\n    lastSeenAt\n    online\n    activeStreams\n    bytesSent\n    bytesReceived\n    currentBytesPerSecond\n    averageBytesPerSecond\n    peakBytesPerSecond\n    streams\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment ReceivedShareFields on ReceivedShare {\n    id\n    name\n    nodeId\n    libraryId\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    active\n    online\n    createdAt\n    lastContactAt\n    copy {\n      status\n      total\n      copied\n      skipped\n      errorCode\n    }\n  }\n"): (typeof documents)["\n  fragment ReceivedShareFields on ReceivedShare {\n    id\n    name\n    nodeId\n    libraryId\n    rules {\n      allowDownload\n      maxBitRate\n      bandwidthKbps\n      maxStreams\n    }\n    active\n    online\n    createdAt\n    lastContactAt\n    copy {\n      status\n      total\n      copied\n      skipped\n      errorCode\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Network {\n    p2pStatus {\n      ...P2PStatusFields\n    }\n    networkPeers {\n      ...NetworkPeerFields\n    }\n    receivedShares {\n      ...ReceivedShareFields\n    }\n    p2pActivity(limit: 50) {\n      id\n      at\n      kind\n      peerId\n      peerName\n      detail\n    }\n    libraries {\n      id\n      name\n      sources {\n        id\n        kind\n        location\n      }\n    }\n  }\n"): (typeof documents)["\n  query Network {\n    p2pStatus {\n      ...P2PStatusFields\n    }\n    networkPeers {\n      ...NetworkPeerFields\n    }\n    receivedShares {\n      ...ReceivedShareFields\n    }\n    p2pActivity(limit: 50) {\n      id\n      at\n      kind\n      peerId\n      peerName\n      detail\n    }\n    libraries {\n      id\n      name\n      sources {\n        id\n        kind\n        location\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query MyDevices {\n    networkPeers(kind: DEVICE) {\n      ...NetworkPeerFields\n    }\n  }\n"): (typeof documents)["\n  query MyDevices {\n    networkPeers(kind: DEVICE) {\n      ...NetworkPeerFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetP2PSettings($input: P2PSettingsInput!) {\n    setP2pSettings(input: $input) {\n      ...P2PStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetP2PSettings($input: P2PSettingsInput!) {\n    setP2pSettings(input: $input) {\n      ...P2PStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateDeviceInvitation($input: CreateDeviceInvitationInput!) {\n    createDeviceInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"): (typeof documents)["\n  mutation CreateDeviceInvitation($input: CreateDeviceInvitationInput!) {\n    createDeviceInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateShareInvitation($input: CreateShareInvitationInput!) {\n    createShareInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"): (typeof documents)["\n  mutation CreateShareInvitation($input: CreateShareInvitationInput!) {\n    createShareInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateNetworkPeer($input: UpdateNetworkPeerInput!) {\n    updateNetworkPeer(input: $input) {\n      ...NetworkPeerFields\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateNetworkPeer($input: UpdateNetworkPeerInput!) {\n    updateNetworkPeer(input: $input) {\n      ...NetworkPeerFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RevokeNetworkPeer($id: ID!) {\n    revokeNetworkPeer(id: $id) {\n      ...NetworkPeerFields\n    }\n  }\n"): (typeof documents)["\n  mutation RevokeNetworkPeer($id: ID!) {\n    revokeNetworkPeer(id: $id) {\n      ...NetworkPeerFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AcceptShare($link: String!) {\n    acceptShare(link: $link) {\n      ...ReceivedShareFields\n    }\n  }\n"): (typeof documents)["\n  mutation AcceptShare($link: String!) {\n    acceptShare(link: $link) {\n      ...ReceivedShareFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CopyReceivedShare($input: CopyReceivedShareInput!) {\n    copyReceivedShare(input: $input) {\n      ...ReceivedShareFields\n    }\n  }\n"): (typeof documents)["\n  mutation CopyReceivedShare($input: CopyReceivedShareInput!) {\n    copyReceivedShare(input: $input) {\n      ...ReceivedShareFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment DatabaseStatusFields on DatabaseStatus {\n    provider\n    lockedByEnvironment\n    location\n    sizeBytes\n    migration {\n      phase\n      tables {\n        name\n        rows\n        copied\n      }\n      rows\n      copied\n      errorCode\n      startedAt\n      finishedAt\n    }\n    previousSqlite {\n      path\n      sizeBytes\n      exists\n      migratedAt\n    }\n  }\n"): (typeof documents)["\n  fragment DatabaseStatusFields on DatabaseStatus {\n    provider\n    lockedByEnvironment\n    location\n    sizeBytes\n    migration {\n      phase\n      tables {\n        name\n        rows\n        copied\n      }\n      rows\n      copied\n      errorCode\n      startedAt\n      finishedAt\n    }\n    previousSqlite {\n      path\n      sizeBytes\n      exists\n      migratedAt\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query Database {\n    database {\n      ...DatabaseStatusFields\n    }\n  }\n"): (typeof documents)["\n  query Database {\n    database {\n      ...DatabaseStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation TestPostgresConnection($connectionString: String!) {\n    testPostgresConnection(connectionString: $connectionString) {\n      success\n      error\n    }\n  }\n"): (typeof documents)["\n  mutation TestPostgresConnection($connectionString: String!) {\n    testPostgresConnection(connectionString: $connectionString) {\n      success\n      error\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation MigrateToPostgres($connectionString: String!) {\n    migrateToPostgres(connectionString: $connectionString) {\n      ...DatabaseStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation MigrateToPostgres($connectionString: String!) {\n    migrateToPostgres(connectionString: $connectionString) {\n      ...DatabaseStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment UltimateCloudStatusFields on UltimateCloudStatus {\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      usedBytes\n      limitBytes\n      readOnly\n      renewsAt\n      checkedAt\n    }\n    tier\n    capabilities\n    errorCode\n    libraries {\n      libraryId\n      name\n      collection\n    }\n    audioTool {\n      version\n      errorCode\n    }\n    enabled\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n"): (typeof documents)["\n  fragment UltimateCloudStatusFields on UltimateCloudStatus {\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      usedBytes\n      limitBytes\n      readOnly\n      renewsAt\n      checkedAt\n    }\n    tier\n    capabilities\n    errorCode\n    libraries {\n      libraryId\n      name\n      collection\n    }\n    audioTool {\n      version\n      errorCode\n    }\n    enabled\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setUltimateCloudServices(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setUltimateCloudServices(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query UltimateCloudAccounts {\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n  }\n"): (typeof documents)["\n  query UltimateCloudAccounts {\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment MyUltimateCloudStatusFields on MyUltimateCloudStatus {\n    enabled\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      readOnly\n      checkedAt\n    }\n    tier\n    errorCode\n    libraryId\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n"): (typeof documents)["\n  fragment MyUltimateCloudStatusFields on MyUltimateCloudStatus {\n    enabled\n    state\n    account\n    ownerFirstName\n    linkedAt\n    pending {\n      userCode\n      verificationUri\n      verificationUriComplete\n      expiresAt\n    }\n    plan {\n      name\n      cloudStatus\n      readOnly\n      checkedAt\n    }\n    tier\n    errorCode\n    libraryId\n    services {\n      personalPlays\n      albumPlaythroughs\n      starTracks\n      metrics\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query MyUltimateCloud {\n    myUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  query MyUltimateCloud {\n    myUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation StartMyUltimateCloudLink($input: StartMyUltimateCloudLinkInput!) {\n    startMyUltimateCloudLink(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation StartMyUltimateCloudLink($input: StartMyUltimateCloudLinkInput!) {\n    startMyUltimateCloudLink(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelMyUltimateCloudLink {\n    cancelMyUltimateCloudLink {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation CancelMyUltimateCloudLink {\n    cancelMyUltimateCloudLink {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UnlinkMyUltimateCloud {\n    unlinkMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation UnlinkMyUltimateCloud {\n    unlinkMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RefreshMyUltimateCloud {\n    refreshMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation RefreshMyUltimateCloud {\n    refreshMyUltimateCloud {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetMyUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setMyUltimateCloudServices(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetMyUltimateCloudServices($input: UltimateCloudServicesInput!) {\n    setMyUltimateCloudServices(input: $input) {\n      ...MyUltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query MyUltimateCloudMetrics {\n    myUltimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n    }\n  }\n"): (typeof documents)["\n  query MyUltimateCloudMetrics {\n    myUltimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query UltimateCloudMetrics {\n    ultimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongThisMonth {\n        title\n        artist\n        playCount\n      }\n      topSongThisYear {\n        title\n        artist\n        playCount\n      }\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistThisMonth {\n        artist\n        playCount\n      }\n      topArtistThisYear {\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n      topAlbumThisMonth {\n        title\n        artist\n        playCount\n      }\n      topAlbumThisYear {\n        title\n        artist\n        playCount\n      }\n      topAlbumAllTime {\n        title\n        artist\n        playCount\n      }\n      topGenreThisMonth {\n        genre\n        playCount\n      }\n      topGenreThisYear {\n        genre\n        playCount\n      }\n      topGenreAllTime {\n        genre\n        playCount\n      }\n      topDecade {\n        decadeStart\n        decadeEnd\n        playCount\n      }\n    }\n  }\n"): (typeof documents)["\n  query UltimateCloudMetrics {\n    ultimateCloudMetrics {\n      totalPlays\n      totalStreams\n      topSongThisMonth {\n        title\n        artist\n        playCount\n      }\n      topSongThisYear {\n        title\n        artist\n        playCount\n      }\n      topSongAllTime {\n        title\n        artist\n        playCount\n      }\n      topArtistThisMonth {\n        artist\n        playCount\n      }\n      topArtistThisYear {\n        artist\n        playCount\n      }\n      topArtistAllTime {\n        artist\n        playCount\n      }\n      topAlbumThisMonth {\n        title\n        artist\n        playCount\n      }\n      topAlbumThisYear {\n        title\n        artist\n        playCount\n      }\n      topAlbumAllTime {\n        title\n        artist\n        playCount\n      }\n      topGenreThisMonth {\n        genre\n        playCount\n      }\n      topGenreThisYear {\n        genre\n        playCount\n      }\n      topGenreAllTime {\n        genre\n        playCount\n      }\n      topDecade {\n        decadeStart\n        decadeEnd\n        playCount\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query UltimateCloud {\n    ultimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  query UltimateCloud {\n    ultimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation StartUltimateCloudLink {\n    startUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation StartUltimateCloudLink {\n    startUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelUltimateCloudLink {\n    cancelUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation CancelUltimateCloudLink {\n    cancelUltimateCloudLink {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UnlinkUltimateCloud {\n    unlinkUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation UnlinkUltimateCloud {\n    unlinkUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation AddUltimateCloudLibrary($input: AddUltimateCloudLibraryInput!) {\n    addUltimateCloudLibrary(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation AddUltimateCloudLibrary($input: AddUltimateCloudLibraryInput!) {\n    addUltimateCloudLibrary(input: $input) {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment CloudSyncStateFields on LibraryCloudSyncState {\n    libraryId\n    mode\n    accountUserId\n    downloadSourceId\n    lastRunAt\n    requested\n    running {\n      phase\n      done\n      total\n    }\n    matched\n    uploaded\n    downloaded\n    localOnly\n    failed\n    errorCode\n    failures {\n      title\n      path\n      reason\n    }\n  }\n"): (typeof documents)["\n  fragment CloudSyncStateFields on LibraryCloudSyncState {\n    libraryId\n    mode\n    accountUserId\n    downloadSourceId\n    lastRunAt\n    requested\n    running {\n      phase\n      done\n      total\n    }\n    matched\n    uploaded\n    downloaded\n    localOnly\n    failed\n    errorCode\n    failures {\n      title\n      path\n      reason\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query CloudSyncs {\n    cloudSyncs {\n      ...CloudSyncStateFields\n    }\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n"): (typeof documents)["\n  query CloudSyncs {\n    cloudSyncs {\n      ...CloudSyncStateFields\n    }\n    ultimateCloudAccounts {\n      id\n      name\n      ownerFirstName\n      userId\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SetLibraryCloudSync($libraryId: ID!, $input: SetLibraryCloudSyncInput!) {\n    setLibraryCloudSync(libraryId: $libraryId, input: $input) {\n      ...CloudSyncStateFields\n    }\n  }\n"): (typeof documents)["\n  mutation SetLibraryCloudSync($libraryId: ID!, $input: SetLibraryCloudSyncInput!) {\n    setLibraryCloudSync(libraryId: $libraryId, input: $input) {\n      ...CloudSyncStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation SyncLibraryWithCloud($libraryId: ID!) {\n    syncLibraryWithCloud(libraryId: $libraryId) {\n      ...CloudSyncStateFields\n    }\n  }\n"): (typeof documents)["\n  mutation SyncLibraryWithCloud($libraryId: ID!) {\n    syncLibraryWithCloud(libraryId: $libraryId) {\n      ...CloudSyncStateFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation TestS3Connection($input: S3ConnectionInput!) {\n    testS3Connection(input: $input) {\n      success\n      errorCode\n      audioFiles\n      truncated\n    }\n  }\n"): (typeof documents)["\n  mutation TestS3Connection($input: S3ConnectionInput!) {\n    testS3Connection(input: $input) {\n      success\n      errorCode\n      audioFiles\n      truncated\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateS3Library($input: CreateS3LibraryInput!) {\n    createS3Library(input: $input) {\n      id\n    }\n  }\n"): (typeof documents)["\n  mutation CreateS3Library($input: CreateS3LibraryInput!) {\n    createS3Library(input: $input) {\n      id\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RefreshUltimateCloud {\n    refreshUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation RefreshUltimateCloud {\n    refreshUltimateCloud {\n      ...UltimateCloudStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation ResolvePreviousSqlite($input: ResolvePreviousSqliteInput!) {\n    resolvePreviousSqlite(input: $input) {\n      ...DatabaseStatusFields\n    }\n  }\n"): (typeof documents)["\n  mutation ResolvePreviousSqlite($input: ResolvePreviousSqliteInput!) {\n    resolvePreviousSqlite(input: $input) {\n      ...DatabaseStatusFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment BackupJobFields on BackupJob {\n    id\n    backupId\n    backupName\n    status\n    trigger\n    snapshot\n    requestedAt\n    startedAt\n    finishedAt\n    files\n    copied\n    unchanged\n    versioned\n    skipped\n    failed\n    bytes\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n"): (typeof documents)["\n  fragment BackupJobFields on BackupJob {\n    id\n    backupId\n    backupName\n    status\n    trigger\n    snapshot\n    requestedAt\n    startedAt\n    finishedAt\n    files\n    copied\n    unchanged\n    versioned\n    skipped\n    failed\n    bytes\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment BackupDestinationFields on BackupDestinationInfo {\n    id\n    name\n    kind\n    location\n    backups\n    createdAt\n  }\n"): (typeof documents)["\n  fragment BackupDestinationFields on BackupDestinationInfo {\n    id\n    name\n    kind\n    location\n    backups\n    createdAt\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment BackupFields on BackupDefinition {\n    id\n    name\n    destination {\n      id\n      name\n      kind\n    }\n    scope\n    items {\n      id\n      name\n    }\n    includeUserData\n    includeServer\n    schedule\n    intervalHours\n    timeOfDay\n    dayOfWeek\n    timeZone\n    retentionDays\n    enabled\n    folder\n    createdAt\n    nextRunAt\n    lastRun {\n      ...BackupJobFields\n    }\n  }\n"): (typeof documents)["\n  fragment BackupFields on BackupDefinition {\n    id\n    name\n    destination {\n      id\n      name\n      kind\n    }\n    scope\n    items {\n      id\n      name\n    }\n    includeUserData\n    includeServer\n    schedule\n    intervalHours\n    timeOfDay\n    dayOfWeek\n    timeZone\n    retentionDays\n    enabled\n    folder\n    createdAt\n    nextRunAt\n    lastRun {\n      ...BackupJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment RestoreJobFields on RestoreJobInfo {\n    id\n    backupName\n    snapshot\n    status\n    files\n    userData\n    filesTotal\n    restored\n    identical\n    renamed\n    filesFailed\n    ratings\n    playCounts\n    plays\n    playlists\n    unmatched\n    requestedAt\n    finishedAt\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n"): (typeof documents)["\n  fragment RestoreJobFields on RestoreJobInfo {\n    id\n    backupName\n    snapshot\n    status\n    files\n    userData\n    filesTotal\n    restored\n    identical\n    renamed\n    filesFailed\n    ratings\n    playCounts\n    plays\n    playlists\n    unmatched\n    requestedAt\n    finishedAt\n    errorCode\n    failures {\n      path\n      code\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  fragment BackupGuestFields on BackupGuest {\n    peerId\n    name\n    folder\n    usedBytes\n    quotaBytes\n    revoked\n    lastSeenAt\n  }\n"): (typeof documents)["\n  fragment BackupGuestFields on BackupGuest {\n    peerId\n    name\n    folder\n    usedBytes\n    quotaBytes\n    revoked\n    lastSeenAt\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupsPage {\n    backupDestinations {\n      ...BackupDestinationFields\n    }\n    backups {\n      ...BackupFields\n    }\n    restoreJobs {\n      ...RestoreJobFields\n    }\n    backupGuests {\n      ...BackupGuestFields\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n"): (typeof documents)["\n  query BackupsPage {\n    backupDestinations {\n      ...BackupDestinationFields\n    }\n    backups {\n      ...BackupFields\n    }\n    restoreJobs {\n      ...RestoreJobFields\n    }\n    backupGuests {\n      ...BackupGuestFields\n    }\n    serverInfo {\n      capabilities\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupsOverview {\n    backups {\n      id\n      name\n      enabled\n      schedule\n      nextRunAt\n      lastRun {\n        id\n        status\n        finishedAt\n        requestedAt\n      }\n    }\n  }\n"): (typeof documents)["\n  query BackupsOverview {\n    backups {\n      id\n      name\n      enabled\n      schedule\n      nextRunAt\n      lastRun {\n        id\n        status\n        finishedAt\n        requestedAt\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupOptions {\n    libraries {\n      id\n      name\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n    }\n    playlists {\n      id\n      name\n      trackCount\n      owner {\n        id\n        name\n      }\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n  }\n"): (typeof documents)["\n  query BackupOptions {\n    libraries {\n      id\n      name\n      disabled\n      personal\n      sources {\n        id\n        kind\n        location\n      }\n    }\n    playlists {\n      id\n      name\n      trackCount\n      owner {\n        id\n        name\n      }\n    }\n    users {\n      id\n      userName\n      displayName\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupAlbumSearch($search: String!) {\n    albums(search: $search, first: 20) {\n      items {\n        id\n        title\n        year\n        artist {\n          name\n        }\n      }\n    }\n  }\n"): (typeof documents)["\n  query BackupAlbumSearch($search: String!) {\n    albums(search: $search, first: 20) {\n      items {\n        id\n        title\n        year\n        artist {\n          name\n        }\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupRuns($backupId: ID!) {\n    backupRuns(backupId: $backupId) {\n      ...BackupJobFields\n    }\n  }\n"): (typeof documents)["\n  query BackupRuns($backupId: ID!) {\n    backupRuns(backupId: $backupId) {\n      ...BackupJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  subscription BackupJobUpdated {\n    backupJobUpdated {\n      ...BackupJobFields\n    }\n  }\n"): (typeof documents)["\n  subscription BackupJobUpdated {\n    backupJobUpdated {\n      ...BackupJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  subscription RestoreJobUpdated {\n    restoreJobUpdated {\n      ...RestoreJobFields\n    }\n  }\n"): (typeof documents)["\n  subscription RestoreJobUpdated {\n    restoreJobUpdated {\n      ...RestoreJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateBackupDestination($input: CreateBackupDestinationInput!) {\n    createBackupDestination(input: $input) {\n      ...BackupDestinationFields\n    }\n  }\n"): (typeof documents)["\n  mutation CreateBackupDestination($input: CreateBackupDestinationInput!) {\n    createBackupDestination(input: $input) {\n      ...BackupDestinationFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation TestBackupDestination($id: ID!) {\n    testBackupDestination(id: $id) {\n      success\n      errorCode\n    }\n  }\n"): (typeof documents)["\n  mutation TestBackupDestination($id: ID!) {\n    testBackupDestination(id: $id) {\n      success\n      errorCode\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteBackupDestination($id: ID!) {\n    deleteBackupDestination(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteBackupDestination($id: ID!) {\n    deleteBackupDestination(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateBackup($input: CreateBackupInput!) {\n    createBackup(input: $input) {\n      ...BackupFields\n    }\n  }\n"): (typeof documents)["\n  mutation CreateBackup($input: CreateBackupInput!) {\n    createBackup(input: $input) {\n      ...BackupFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation UpdateBackup($id: ID!, $input: UpdateBackupInput!) {\n    updateBackup(id: $id, input: $input) {\n      ...BackupFields\n    }\n  }\n"): (typeof documents)["\n  mutation UpdateBackup($id: ID!, $input: UpdateBackupInput!) {\n    updateBackup(id: $id, input: $input) {\n      ...BackupFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation DeleteBackup($id: ID!) {\n    deleteBackup(id: $id)\n  }\n"): (typeof documents)["\n  mutation DeleteBackup($id: ID!) {\n    deleteBackup(id: $id)\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation RunBackup($id: ID!) {\n    runBackup(id: $id) {\n      ...BackupJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation RunBackup($id: ID!) {\n    runBackup(id: $id) {\n      ...BackupJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelBackupRun($id: ID!) {\n    cancelBackupRun(id: $id) {\n      ...BackupJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation CancelBackupRun($id: ID!) {\n    cancelBackupRun(id: $id) {\n      ...BackupJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CreateBackupInvitation($input: CreateBackupInvitationInput!) {\n    createBackupInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"): (typeof documents)["\n  mutation CreateBackupInvitation($input: CreateBackupInvitationInput!) {\n    createBackupInvitation(input: $input) {\n      link\n      expiresAt\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query StoredBackups($destinationId: ID!) {\n    storedBackups(destinationId: $destinationId) {\n      folder\n      id\n      name\n      scope\n      machine\n      arcanistVersion\n      updatedAt\n      snapshots {\n        name\n        createdAt\n        files\n        bytes\n        userData\n        server\n      }\n      libraries {\n        folder\n        name\n        sourceId\n      }\n    }\n  }\n"): (typeof documents)["\n  query StoredBackups($destinationId: ID!) {\n    storedBackups(destinationId: $destinationId) {\n      folder\n      id\n      name\n      scope\n      machine\n      arcanistVersion\n      updatedAt\n      snapshots {\n        name\n        createdAt\n        files\n        bytes\n        userData\n        server\n      }\n      libraries {\n        folder\n        name\n        sourceId\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  query BackupSnapshotContents($destinationId: ID!, $folder: String!, $snapshot: String!) {\n    backupSnapshotContents(destinationId: $destinationId, folder: $folder, snapshot: $snapshot) {\n      libraries {\n        folder\n        name\n        files\n        bytes\n      }\n      albums {\n        id\n        title\n        artist\n        year\n        paths\n      }\n      playlists {\n        owner\n        name\n        tracks\n        paths\n      }\n      users {\n        userName\n        displayName\n        ratings\n        plays\n        playlists\n        userId\n      }\n    }\n  }\n"): (typeof documents)["\n  query BackupSnapshotContents($destinationId: ID!, $folder: String!, $snapshot: String!) {\n    backupSnapshotContents(destinationId: $destinationId, folder: $folder, snapshot: $snapshot) {\n      libraries {\n        folder\n        name\n        files\n        bytes\n      }\n      albums {\n        id\n        title\n        artist\n        year\n        paths\n      }\n      playlists {\n        owner\n        name\n        tracks\n        paths\n      }\n      users {\n        userName\n        displayName\n        ratings\n        plays\n        playlists\n        userId\n      }\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation StartRestore($input: StartRestoreInput!) {\n    startRestore(input: $input) {\n      ...RestoreJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation StartRestore($input: StartRestoreInput!) {\n    startRestore(input: $input) {\n      ...RestoreJobFields\n    }\n  }\n"];
/**
 * The graphql function is used to parse GraphQL queries into a document that can be used by GraphQL clients.
 */
export function graphql(source: "\n  mutation CancelRestore($id: ID!) {\n    cancelRestore(id: $id) {\n      ...RestoreJobFields\n    }\n  }\n"): (typeof documents)["\n  mutation CancelRestore($id: ID!) {\n    cancelRestore(id: $id) {\n      ...RestoreJobFields\n    }\n  }\n"];

export function graphql(source: string) {
  return (documents as any)[source] ?? {};
}

export type DocumentType<TDocumentNode extends DocumentNode<any, any>> = TDocumentNode extends DocumentNode<  infer TType,  any>  ? TType  : never;