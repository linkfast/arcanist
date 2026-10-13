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
    "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalHours\n      createdAt\n      lastScanAt\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": typeof types.LibrariesDocument,
    "\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n": typeof types.CreateLibraryDocument,
    "\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n": typeof types.UpdateLibraryDocument,
    "\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n": typeof types.DeleteLibraryDocument,
    "\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n": typeof types.AddLibrarySourceDocument,
    "\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n": typeof types.RemoveLibrarySourceDocument,
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
    "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    createdAt\n    available\n  }\n": typeof types.DropFolderFieldsFragmentDoc,
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
};
const documents: Documents = {
    "\n  fragment ScanJobFields on ScanJob {\n    id\n    libraryId\n    kind\n    status\n    requestedAt\n    startedAt\n    finishedAt\n    filesSeen\n    filesAdded\n    filesUpdated\n    filesMissing\n    errors\n    errorCode\n    progress {\n      phase\n      filesFound\n      filesToRead\n      filesRead\n    }\n  }\n": types.ScanJobFieldsFragmentDoc,
    "\n  query ServerInfo {\n    serverInfo {\n      name\n      version\n      apiVersion\n      sourceUrl\n    }\n  }\n": types.ServerInfoDocument,
    "\n  query Overview {\n    libraries {\n      id\n    }\n    artists(first: 1) {\n      totalCount\n    }\n    albums(first: 1) {\n      totalCount\n    }\n    tracks(first: 1) {\n      totalCount\n    }\n  }\n": types.OverviewDocument,
    "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalHours\n      createdAt\n      lastScanAt\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n": types.LibrariesDocument,
    "\n  mutation CreateLibrary($input: CreateLibraryInput!) {\n    createLibrary(input: $input) {\n      id\n    }\n  }\n": types.CreateLibraryDocument,
    "\n  mutation UpdateLibrary($id: ID!, $input: UpdateLibraryInput!) {\n    updateLibrary(id: $id, input: $input) {\n      id\n    }\n  }\n": types.UpdateLibraryDocument,
    "\n  mutation DeleteLibrary($id: ID!) {\n    deleteLibrary(id: $id)\n  }\n": types.DeleteLibraryDocument,
    "\n  mutation AddLibrarySource($libraryId: ID!, $input: LibrarySourceInput!) {\n    addLibrarySource(libraryId: $libraryId, input: $input) {\n      id\n    }\n  }\n": types.AddLibrarySourceDocument,
    "\n  mutation RemoveLibrarySource($id: ID!) {\n    removeLibrarySource(id: $id) {\n      id\n    }\n  }\n": types.RemoveLibrarySourceDocument,
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
    "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    createdAt\n    available\n  }\n": types.DropFolderFieldsFragmentDoc,
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
export function graphql(source: "\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalHours\n      createdAt\n      lastScanAt\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"): (typeof documents)["\n  query Libraries {\n    libraries {\n      id\n      name\n      visibleToAll\n      scanIntervalHours\n      createdAt\n      lastScanAt\n      sources {\n        id\n        kind\n        location\n      }\n      organization {\n        template\n        isDefaultTemplate\n        autoOrganize\n        autoOrganizeSince\n      }\n    }\n  }\n"];
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
export function graphql(source: "\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    createdAt\n    available\n  }\n"): (typeof documents)["\n  fragment DropFolderFields on DropFolder {\n    id\n    path\n    libraryId\n    librarySourceId\n    enabled\n    duplicatePolicy\n    convertTo\n    convertBitRate\n    convertLossy\n    createdAt\n    available\n  }\n"];
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

export function graphql(source: string) {
  return (documents as any)[source] ?? {};
}

export type DocumentType<TDocumentNode extends DocumentNode<any, any>> = TDocumentNode extends DocumentNode<  infer TType,  any>  ? TType  : never;