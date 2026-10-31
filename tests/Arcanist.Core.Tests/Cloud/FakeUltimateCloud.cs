using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanist.Core.Cloud;
using Arcanist.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Cloud;

/// <summary>
/// Plays Keycloak (discovery, device authorization, tokens, revocation) and Ultimate Cloud's GraphQL API from
/// memory. Nothing reaches the network.
/// </summary>
public sealed class FakeUltimateCloud : HttpMessageHandler
{
    public const string Authority = "https://auth.test/realms/clients";
    public const string ApiUrl = "https://cloud.test/graphql/";

    public bool Approve { get; set; }
    public bool Deny { get; set; }
    public bool SessionEnded { get; set; }
    public string Plan { get; set; } = "Cloud";
    public ConcurrentQueue<(string Operation, HttpRequestMessage Request, string Body)> Api { get; } = new();
    public ConcurrentQueue<string> Revoked { get; } = new();
    public string? RefreshToken { get; private set; }

    /// <summary>The account the next approved code links: <c>user-1</c> (Ana, the default) or <c>user-2</c> (Bruno).</summary>
    public string ApprovingAs { get; set; } = "user-1";

    /// <summary>The refresh tokens still valid (each is used once), and whose account they are.</summary>
    private readonly ConcurrentDictionary<string, string> _refreshTokens = new();

    /// <summary>The library, StarTracks, devices and plan of accounts other than <c>user-1</c> (whose are the plain ones).</summary>
    public ConcurrentDictionary<string, List<(string Id, object Payload, byte[] Audio)>> AccountTracks { get; } = new();

    public ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> AccountStars { get; } = new();

    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> AccountDevices { get; } = new();

    public ConcurrentDictionary<string, string> Plans { get; } = new();

    /// <summary>Every recorded play, with the account it was recorded for.</summary>
    public ConcurrentQueue<(string Account, string AcoustId)> PlaysBy { get; } = new();

    /// <summary>A call of server audio as an Ultimate Connect device (registering, heartbeats, states, going offline).</summary>
    public sealed record ConnectCall(string Account, string DeviceId, string Operation, JsonNode? Variables);

    public ConcurrentQueue<ConnectCall> ConnectCalls { get; } = new();

    /// <summary>The servers' nodes each account registered.</summary>
    public ConcurrentQueue<(string Account, string NodeId)> RegisteredBy { get; } = new();

    private ConcurrentDictionary<string, bool> StarsOf(string account) => account == "user-1" ? Stars : AccountStars.GetOrAdd(account, _ => new());

    private List<(string Id, object Payload, byte[] Audio)> TracksOf(string account) => account == "user-1" ? Tracks : AccountTracks.GetOrAdd(account, _ => []);

    private ConcurrentDictionary<string, string> DevicesOf(string account) => account == "user-1" ? DeviceNodes : AccountDevices.GetOrAdd(account, _ => new());

    /// <summary>The account of a request's access token.</summary>
    private static string AccountOf(HttpRequestMessage request)
    {
        var payload = request.Headers.Authorization!.Parameter!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonNode.Parse(Convert.FromBase64String(payload))!["sub"]!.GetValue<string>();
    }
    private int _issued;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = request.Content is FormUrlEncodedContent
            ? body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')))
            : [];

        if (url == $"{Authority}/.well-known/openid-configuration")
        {
            return Json(new
            {
                issuer = Authority,
                device_authorization_endpoint = $"{Authority}/device",
                token_endpoint = $"{Authority}/token",
                revocation_endpoint = $"{Authority}/revoke",
            });
        }

        if (url == $"{Authority}/device")
        {
            Assert.Equal("arcanist", form["client_id"]);
            Assert.Contains("offline_access", form["scope"]);
            return Json(new
            {
                device_code = "device-code",
                user_code = "WDJB-MJHT",
                verification_uri = "https://auth.test/device",
                verification_uri_complete = "https://auth.test/device?user_code=WDJB-MJHT",
                expires_in = 600,
                interval = 0,
            });
        }

        if (url == $"{Authority}/token")
        {
            if (form["grant_type"] == "urn:ietf:params:oauth:grant-type:device_code")
            {
                if (Deny)
                    return Error("access_denied");
                return Approve ? Tokens(ApprovingAs) : Error("authorization_pending");
            }

            Assert.Equal("refresh_token", form["grant_type"]);
            return SessionEnded || !_refreshTokens.TryRemove(form["refresh_token"], out var owner) ? Error("invalid_grant") : Tokens(owner);
        }

        if (url == $"{Authority}/revoke")
        {
            Revoked.Enqueue(form["token"]);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (url == ApiUrl)
        {
            var query = JsonNode.Parse(body)!["query"]!.GetValue<string>();
            Api.Enqueue((query.Contains("touchSyncedDevice") ? "touch" : "plan", request, body));
            if (query.Contains("recordAnonymousPlays("))
            {
                // No account: anonymous statistics, from the server's device.
                Assert.Null(request.Headers.Authorization);
                Assert.StartsWith("arcanist-", request.Headers.GetValues("X-Device-Id").Single());
                var anonymous = JsonNode.Parse(body)!["variables"]!;
                foreach (var play in anonymous["p"]!.AsArray())
                    SharedPlays.Enqueue((play!["acoustId"]!.GetValue<string>(), play["title"]?.GetValue<string>(), play["playedAt"]?.GetValue<DateTime>(),
                        play["playedMs"]?.GetValue<int>(), play["skipped"]?.GetValue<bool>(), play["completed"]?.GetValue<bool>()));
                foreach (var album in anonymous["a"]?.AsArray() ?? [])
                    AnonymousAlbums.Enqueue((album!["artist"]!.GetValue<string>(), album["album"]!.GetValue<string>()));
                return Json(new { data = new { recordAnonymousPlays = anonymous["p"]!.AsArray().Count } });
            }

            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: not null })
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var account = AccountOf(request);
            var plan = Plans.GetValueOrDefault(account) ?? Plan;
            var connect = new[] { "touchSyncedDevice", "connectHeartbeat", "publishPlaybackState", "goOfflineForConnect" }.FirstOrDefault(query.Contains);
            if (connect is not null && request.Headers.GetValues("X-Client-App").Single() == "arcanist-server-audio")
                ConnectCalls.Enqueue(new ConnectCall(account, request.Headers.GetValues("X-Device-Id").Single(), connect,
                    JsonNode.Parse(body)!["variables"]?.DeepClone()));
            if (query.Contains("connectHeartbeat"))
                return Json(new { data = new { connectHeartbeat = true } });
            if (query.Contains("publishPlaybackState"))
                return Json(new { data = new { publishPlaybackState = new { status = "PLAYING" } } });
            if (query.Contains("goOfflineForConnect"))
                return Json(new { data = new { goOfflineForConnect = true } });
            if (query.Contains("touchSyncedDevice"))
                return Json(new { data = new { touchSyncedDevice = true } });
            if (query.Contains("myLibrary"))
                return Json(new { data = new { myLibrary = new { id = "lib-1" } } });
            if (query.Contains("cloudDropTracks"))
                return Json(new { data = new { cloudDropTracks = DropTracks.Select(t => t.Payload).ToArray() } });
            if (query.Contains("library {"))
                return Json(new { data = new { library = TracksOf(account).Select(t => t.Payload).ToArray() } });
            var variables = JsonNode.Parse(body)!["variables"];
            if (query.Contains("recordPlay("))
            {
                Plays.Enqueue((variables!["a"]!.GetValue<string>(), variables["title"]?.GetValue<string>(), variables["at"]?.GetValue<DateTime>()));
                PlaysBy.Enqueue((account, variables["a"]!.GetValue<string>()));
                return Json(new { data = new { recordPlay = true } });
            }

            if (query.Contains("recordAlbumPlaythrough("))
            {
                Playthroughs.Enqueue((variables!["artist"]!.GetValue<string>(), variables["album"]!.GetValue<string>()));
                return Json(new { data = new { recordAlbumPlaythrough = true } });
            }

            if (query.Contains("addStarTracks(") || query.Contains("removeStarTracks("))
            {
                var ids = variables!["items"]!.AsArray().Select(i => i!["acoustId"]!.GetValue<string>()).ToList();
                foreach (var id in ids)
                {
                    if (query.Contains("addStarTracks("))
                        StarsOf(account)[id] = true;
                    else
                        StarsOf(account).TryRemove(id, out _);
                }

                return Json(new { data = new Dictionary<string, object> { [query.Contains("addStarTracks(") ? "addStarTracks" : "removeStarTracks"] = ids.Count } });
            }

            if (query.Contains("starTracks {"))
                return Json(new { data = new { starTracks = StarsOf(account).Keys.Select(id => new { acoustId = id }).ToArray() } });

            if (query.Contains("myMusicMetrics"))
            {
                return Json(new
                {
                    data = new
                    {
                        myMusicMetrics = new
                        {
                            totalPlays = 42L,
                            totalStreams = 7L,
                            topSongAllTime = new { acoustId = "a-1", title = "Top Song", artist = "Top Artist", playCount = 9L },
                            topArtistAllTime = new { artist = "Top Artist", playCount = 12L },
                            topDecade = new { decadeStart = 1990, decadeEnd = 1999, playCount = 20L },
                        },
                    },
                });
            }

            if (query.Contains("myP2PNodes"))
            {
                return Json(new
                {
                    data = new
                    {
                        myP2PNodes = DevicesOf(account).Select(d => new { nodeId = d.Key, kind = "DEVICE", name = d.Value })
                            .Concat(RegisteredNodes.Select(n => new { nodeId = n.Key, kind = "SERVER", name = n.Value })).ToArray(),
                    },
                });
            }

            if (query.Contains("registerP2PNode"))
            {
                if (plan != "Cloud")
                    return Json(new { errors = new[] { new { message = "Needs the Cloud plan.", extensions = new { code = "PLAN_REQUIRED" } } } });
                RegisteredNodes[variables!["n"]!.GetValue<string>()] = variables["name"]!.GetValue<string>();
                RegisteredBy.Enqueue((account, variables["n"]!.GetValue<string>()));
                RegisteredTickets.Enqueue(variables["t"]?.GetValue<string>());
                return Json(new { data = new { registerP2PNode = new { nodeId = variables["n"]!.GetValue<string>() } } });
            }

            if (query.Contains("backupUploadUrl"))
            {
                var path = variables!["p"]!.GetValue<string>();
                if (plan != "Cloud")
                    return Json(new { errors = new[] { new { message = "Needs the Cloud plan.", extensions = new { code = "PLAN_REQUIRED" } } } });
                if (Full)
                    return Json(new { errors = new[] { new { message = "Over quota.", extensions = new { code = "QUOTA_EXCEEDED" } } } });
                if (BackupFiles.TryGetValue(path, out var existing) && existing.Registered)
                    return Json(new { errors = new[] { new { message = "Exists.", extensions = new { code = "BACKUP_FILE_EXISTS" } } } });
                var key = existing.Key ?? $"Backups/{account}/{Guid.NewGuid():N}";
                BackupFiles[path] = (key, variables["s"]!.GetValue<long>(), false);
                return Json(new { data = new { backupUploadUrl = new { uploadUrl = $"{FilesBase}/b2api/upload", authorizationToken = "upload-token", objectKey = key } } });
            }

            if (query.Contains("registerBackupFile"))
            {
                var path = variables!["p"]!.GetValue<string>();
                if (!BackupFiles.TryGetValue(path, out var file) || !Uploaded.TryGetValue(file.Key, out var bytes) || bytes.Length != file.Size)
                    return Json(new { errors = new[] { new { message = "Not uploaded.", extensions = new { code = "BACKUP_UPLOAD_MISSING" } } } });
                BackupFiles[path] = file with { Registered = true };
                return Json(new { data = new { registerBackupFile = new { path } } });
            }

            if (query.Contains("backupFiles("))
            {
                var prefix = variables!["p"]?.GetValue<string>() ?? "";
                var after = variables["a"]?.GetValue<string>();
                var first = variables["f"]!.GetValue<int>();
                var items = BackupFiles.Where(f => f.Value.Registered && f.Key.StartsWith(prefix, StringComparison.Ordinal)
                                                   && (after is null || string.CompareOrdinal(f.Key, after) > 0))
                    .OrderBy(f => f.Key, StringComparer.Ordinal).Take(first + 1).ToList();
                var next = items.Count > first ? items[first - 1].Key : null;
                return Json(new { data = new { backupFiles = new { items = items.Take(first).Select(f => new { path = f.Key, size = f.Value.Size }), nextCursor = next } } });
            }

            if (query.Contains("backupDownloadUrl"))
            {
                var path = variables!["p"]!.GetValue<string>();
                return BackupFiles.TryGetValue(path, out var file) && file.Registered
                    ? Json(new { data = new { backupDownloadUrl = new { url = $"{FilesBase}/file/bucket/{file.Key}" } } })
                    : Json(new { errors = new[] { new { message = "Not found.", extensions = new { code = "BACKUP_FILE_NOT_FOUND" } } } });
            }

            if (query.Contains("moveBackupFile"))
            {
                var (from, to) = (variables!["f"]!.GetValue<string>(), variables["t"]!.GetValue<string>());
                if (BackupFiles.ContainsKey(to))
                    return Json(new { errors = new[] { new { message = "Exists.", extensions = new { code = "BACKUP_FILE_EXISTS" } } } });
                if (!BackupFiles.TryRemove(from, out var file))
                    return Json(new { errors = new[] { new { message = "Not found.", extensions = new { code = "BACKUP_FILE_NOT_FOUND" } } } });
                BackupFiles[to] = file;
                return Json(new { data = new { moveBackupFile = true } });
            }

            if (query.Contains("deleteBackupFiles"))
            {
                var removed = variables!["p"]!.AsArray().Count(p => BackupFiles.TryRemove(p!.GetValue<string>(), out _));
                return Json(new { data = new { deleteBackupFiles = removed } });
            }

            if (query.Contains("myWishList"))
                return Json(new { data = new { myWishList = Wishes.ToArray() } });

            if (query.Contains("addWishes"))
            {
                var added = new List<JsonObject>();
                foreach (var item in variables!["items"]!.AsArray().OfType<JsonObject>())
                {
                    var wish = new JsonObject
                    {
                        ["id"] = $"w-{Wishes.Count + 1}", ["title"] = item["title"]?.DeepClone(), ["artist"] = item["artist"]?.DeepClone(),
                        ["album"] = item["album"]?.DeepClone(), ["acoustId"] = item["acoustId"]?.DeepClone(),
                        ["musicBrainzRecordingId"] = item["musicBrainzRecordingId"]?.DeepClone(), ["durationSeconds"] = item["durationSeconds"]?.DeepClone(),
                        ["obtained"] = item["obtainedAt"] is not null && item["obtainedAt"]!.GetValueKind() != JsonValueKind.Null,
                    };
                    Wishes.Add(wish);
                    added.Add(wish);
                }
                return Json(new { data = new { addWishes = added } });
            }

            if (query.Contains("removeWishes"))
            {
                var ids = variables!["ids"]!.AsArray().Select(i => i!.GetValue<string>()).ToHashSet();
                return Json(new { data = new { removeWishes = Wishes.RemoveAll(w => ids.Contains(w["id"]!.GetValue<string>())) } });
            }

            if (query.Contains("setWishesObtained"))
            {
                var ids = variables!["ids"]!.AsArray().Select(i => i!.GetValue<string>()).ToHashSet();
                foreach (var wish in Wishes.Where(w => ids.Contains(w["id"]!.GetValue<string>())))
                    wish["obtained"] = true;
                return Json(new { data = new { setWishesObtained = ids.Count } });
            }

            if (query.Contains("checkCloudFile"))
            {
                var hash = variables!["h"]!.GetValue<string>();
                var track = TrackHashes.FirstOrDefault(t => t.Value == hash).Key;
                return Json(new
                {
                    data = new
                    {
                        checkCloudFile = new
                        {
                            blobExists = Blobs.ContainsKey(hash) || track is not null,
                            cloudFileId = Blobs.GetValueOrDefault(hash),
                            alreadyInLibrary = track is not null,
                            existingTrackId = track,
                        },
                    },
                });
            }

            if (query.Contains("uploadUrl("))
            {
                if (Full)
                    return Json(new { errors = new[] { new { message = "Over quota.", extensions = new { code = "QUOTA_EXCEEDED" } } } });
                return Json(new { data = new { uploadUrl = new { uploadUrl = $"{FilesBase}/b2api/upload", authorizationToken = "upload-token", filePrefix = "CloudFiles/" } } });
            }

            if (query.Contains("registerCloudUpload"))
            {
                var input = variables!["i"]!;
                var hash = input["audioHash"]!.GetValue<string>();
                Assert.Equal("CloudFiles/" + hash, input["fileName"]!.GetValue<string>());
                Blobs[hash] = "file-" + hash[..8];
                return Json(new { data = new { registerCloudUpload = new { cloudFileId = Blobs[hash] } } });
            }

            if (query.Contains("addCloudTrack"))
            {
                var fileId = variables!["f"]!.GetValue<string>();
                var hash = Blobs.Single(b => b.Value == fileId).Key;
                var input = variables["i"]!;
                var id = account == "user-1" ? $"up-{Tracks.Count + 1}" : $"up-{account}-{TracksOf(account).Count + 1}";
                if (account == "user-1")
                    AddTrack(id, input["title"]!.GetValue<string>(), input["artist"]?.GetValue<string>() ?? "", input["album"]?.GetValue<string>() ?? "",
                        input["trackNumber"]?.GetValue<int>() ?? 0, Uploaded[$"CloudFiles/{hash}"], hash: hash, format: "WAV");
                else
                {
                    // Into the library of the account uploading it.
                    TrackHashes[id] = hash;
                    AddDescribed(TracksOf(account), id, input["title"]!.GetValue<string>(), input["artist"]?.GetValue<string>() ?? "",
                        input["album"]?.GetValue<string>() ?? "", input["trackNumber"]?.GetValue<int>() ?? 0, Uploaded[$"CloudFiles/{hash}"], null, "WAV");
                }
                return Json(new { data = new { addCloudTrack = new { cloudTrackId = id } } });
            }

            if (query.Contains("removeCloudTrack"))
            {
                var removed = variables!["id"]!.GetValue<string>();
                RemovedTracks.Enqueue(removed);
                DropTracks.RemoveAll(t => t.Id == removed);
                Tracks.RemoveAll(t => t.Id == removed);
                return Json(new { data = new { removeCloudTrack = true } });
            }

            if (query.Contains("downloadUrl"))
            {
                var id = variables!["id"]!.GetValue<string>();
                return Json(new { data = new { downloadUrl = new { url = $"{FilesBase}/file/bucket/{id}?Authorization=token" } } });
            }

            if (query.Contains("streamUrl"))
            {
                var id = variables!["id"]!.GetValue<string>();
                Interlocked.Increment(ref _streamUrls);
                return TracksOf(account).Any(t => t.Id == id)
                    ? Json(new { data = new { streamUrl = new { url = $"{FilesBase}/file/bucket/{id}?Authorization=token", validForSeconds = 3600 } } })
                    : Json(new { errors = new[] { new { message = $"Track '{id}' not found in library." } } });
            }

            if (query.Contains("coverImageUrl"))
                return Json(new { data = new { coverImageUrl = new { url = $"{FilesBase}/file/bucket/cover-{variables!["id"]!.GetValue<string>()}", validForSeconds = 3600 } } });
            return Json(new
            {
                data = new
                {
                    myPlan = new
                    {
                        plan,
                        featuresUnlocked = plan != "Standard",
                        cloudStatus = plan == "Cloud" ? "Active" : "None",
                        renewsAt = (DateTime?)null,
                        storage = new { usedBytes = 1024L, limitBytes = plan == "Cloud" ? 1L << 40 : 0L, isReadOnly = plan != "Cloud" },
                    },
                },
            });
        }

        // Backblaze B2 uploads, checked as B2 checks them.
        if (url == $"{FilesBase}/b2api/upload")
        {
            Assert.Equal("upload-token", request.Headers.GetValues("Authorization").Single());
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            if (!request.Headers.GetValues("X-Bz-Content-Sha1").Single().Equals(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            Uploaded[Uri.UnescapeDataString(request.Headers.GetValues("X-Bz-File-Name").Single())] = bytes;
            return Json(new { fileId = $"b2-{Uploaded.Count}" });
        }

        // Backblaze B2: the tracks' audio (with Range) and covers.
        if (url.StartsWith(FilesBase + "/file/bucket/", StringComparison.Ordinal))
        {
            var name = request.RequestUri!.AbsolutePath["/file/bucket/".Length..];
            if (name.StartsWith("cover-", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Cover) };
            if (name.StartsWith("Backups/", StringComparison.Ordinal))
                return Uploaded.TryGetValue(name, out var backup)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(backup) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            if (Tracks.Concat(DropTracks).Concat(AccountTracks.Values.SelectMany(t => t)).FirstOrDefault(t => t.Id == name) is not { Audio: not null } track)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            Ranges.Enqueue(request.Headers.Range?.ToString());
            if (request.Headers.Range?.Ranges.SingleOrDefault() is { } range)
            {
                var from = (int)range.From!.Value;
                var to = (int)Math.Min(range.To ?? track.Audio.Length - 1, track.Audio.Length - 1);
                var part = new ByteArrayContent(track.Audio[from..(to + 1)]);
                part.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, track.Audio.Length);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = part };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(track.Audio) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public const string FilesBase = "https://f000.backblazeb2.test";


    /// <summary>The account's own plays (<c>recordPlay</c>): AcoustID, title and when.</summary>
    public ConcurrentQueue<(string AcoustId, string? Title, DateTime? PlayedAt)> Plays { get; } = new();

    /// <summary>Plays reported anonymously for the rankings, without an account (<c>recordAnonymousPlays</c>).</summary>
    public ConcurrentQueue<(string AcoustId, string? Title, DateTime? PlayedAt, int? PlayedMs, bool? Skipped, bool? Completed)> SharedPlays { get; } = new();

    /// <summary>Albums heard whole, reported anonymously.</summary>
    public ConcurrentQueue<(string Artist, string Album)> AnonymousAlbums { get; } = new();

    /// <summary>Albums heard from start to finish (<c>recordAlbumPlaythrough</c>).</summary>
    public ConcurrentQueue<(string Artist, string Album)> Playthroughs { get; } = new();

    /// <summary>The account's StarTracks, by AcoustID.</summary>
    public ConcurrentDictionary<string, bool> Stars { get; } = new();

    /// <summary>The account's library: what <c>library</c> describes, and the audio B2 serves.</summary>
    public List<(string Id, object Payload, byte[] Audio)> Tracks { get; } = [];

    /// <summary>The account's Cloud Drop: what <c>cloudDropTracks</c> describes.</summary>
    public List<(string Id, object Payload, byte[] Audio)> DropTracks { get; } = [];

    /// <summary>Tracks taken off the account with <c>removeCloudTrack</c>.</summary>
    public ConcurrentQueue<string> RemovedTracks { get; } = new();

    /// <summary>A song in Cloud Drop, described like the library's.</summary>
    public void AddDropTrack(string id, string title, string artist, string album, int track, byte[] audio)
    {
        AddDescribed(DropTracks, id, title, artist, album, track, audio, null, "WAV");
    }

    public byte[] Cover { get; set; } = [];

    public ConcurrentQueue<string?> Ranges { get; } = new();

    public int StreamUrls => _streamUrls;

    private int _streamUrls;

    /// <summary>Ultimate's audio hash of each track (its blob).</summary>
    public ConcurrentDictionary<string, string> TrackHashes { get; } = new();

    /// <summary>Blobs by audio hash: their CloudFile ids.</summary>
    public ConcurrentDictionary<string, string> Blobs { get; } = new();

    /// <summary>What reached B2, by file name.</summary>
    public ConcurrentDictionary<string, byte[]> Uploaded { get; } = new();

    /// <summary>The account's devices with an iroh node (node id → name).</summary>
    public ConcurrentDictionary<string, string> DeviceNodes { get; } = new();

    /// <summary>Servers that registered their node (node id → name), and the tickets they gave.</summary>
    public ConcurrentDictionary<string, string> RegisteredNodes { get; } = new();

    public ConcurrentQueue<string?> RegisteredTickets { get; } = new();

    /// <summary>The account's storage is full.</summary>
    public bool Full { get; set; }

    /// <summary>The account's backup storage, by path: the B2 object, its size and whether its upload was registered.</summary>
    public ConcurrentDictionary<string, (string Key, long Size, bool Registered)> BackupFiles { get; } = new();

    /// <summary>The account's wish list, as <c>myWishList</c> gives it.</summary>
    public List<JsonObject> Wishes { get; } = [];

    public void AddTrack(string id, string title, string artist, string album, int track, byte[] audio, string? cover = null, string? hash = null,
        string format = "FLAC")
    {
        TrackHashes[id] = hash ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(audio));
        AddDescribed(Tracks, id, title, artist, album, track, audio, cover, format);
    }

    /// <summary>A track of another account's library (<see cref="ApprovingAs"/>).</summary>
    public void AddAccountTrack(string account, string id, string title, string artist, string album, int track, byte[] audio) =>
        AddDescribed(TracksOf(account), id, title, artist, album, track, audio, null, "FLAC");

    private static void AddDescribed(
        List<(string Id, object Payload, byte[] Audio)> tracks, string id, string title, string artist, string album, int track, byte[] audio, string? cover, string format) =>
        tracks.Add((id, new
        {
            cloudTrackId = id, title, artist, album, genre = "Ambient", trackNumber = track, discNumber = 1, year = 2020,
            duration = 8.5, audioBitrate = 900, audioSampleRate = 44100, audioFormat = format, size = (long)audio.Length,
            coverImageId = cover, musicBrainzRecordingId = (string?)null, musicBrainzArtistId = (string?)null,
            dateUpdated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }, audio));

    /// <summary>A new access token (expiring at once, so every call refreshes) and a rotated refresh token.</summary>
    private HttpResponseMessage Tokens(string account)
    {
        var n = Interlocked.Increment(ref _issued);
        RefreshToken = $"refresh-{n}";
        _refreshTokens[RefreshToken] = account;
        object claims = account == "user-1"
            ? new { sub = account, preferred_username = "ana", email = "ana@example.com", given_name = "Ana", family_name = "Pérez" }
            : new { sub = account, preferred_username = "bruno", email = "bruno@example.com", given_name = "Bruno", family_name = "Díaz" };
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Json(new { access_token = $"eyJhbGciOiJub25lIn0.{payload}.", expires_in = 10, refresh_token = RefreshToken, token_type = "Bearer" });
    }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(string error) =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { error }), Encoding.UTF8, "application/json") };

    public void Register(IServiceCollection services)
    {
        services.AddHttpClient(UltimateCloudClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => this);
        services.AddHttpClient(UltimateCloudClient.FilesClientName).ConfigurePrimaryHttpMessageHandler(() => this);
    }
}
