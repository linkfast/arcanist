using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanist.Core.Auth;
using Arcanist.Core.Capabilities;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Scanning;
using Arcanist.Core.Storage;
using Arcanist.Core.Storage.S3;
using Microsoft.EntityFrameworkCore;
using LibraryEntity = Arcanist.Core.Data.Entities.Library;
using LibrarySourceEntity = Arcanist.Core.Data.Entities.LibrarySource;

namespace Arcanist.Core.Application.Libraries;

// Types in this file are part of the public API contracts (GraphQL schema and OpenAPI document).

/// <summary>A bucket of an S3-compatible service (AWS, MinIO, Backblaze B2, Wasabi, Cloudflare R2…).</summary>
/// <param name="Endpoint">The service's address, e.g. <c>https://s3.eu-central-1.wasabisys.com</c>.</param>
/// <param name="Region">The bucket's region (<c>us-east-1</c> when the service has none).</param>
/// <param name="Prefix">A folder in the bucket; the whole bucket when empty.</param>
/// <param name="PathStyle">Address the bucket in the path (MinIO and most S3-compatible services) instead of the host name (AWS).</param>
public sealed record S3ConnectionInput(
    string Endpoint,
    string? Region,
    string Bucket,
    string? Prefix,
    string AccessKeyId,
    string SecretAccessKey,
    bool PathStyle);

/// <summary>What testing a bucket found.</summary>
/// <param name="ErrorCode"><c>s3_unreachable</c>, <c>s3_access_denied</c> or <c>s3_bucket_not_found</c>.</param>
/// <param name="AudioFiles">Audio files among the first thousand objects.</param>
/// <param name="Truncated">There are more objects than were looked at.</param>
public sealed record S3ConnectionTest(bool Success, string? ErrorCode, int AudioFiles, bool Truncated);

public sealed record CreateS3LibraryInput(string Name, S3ConnectionInput Connection);

/// <summary>
/// Libraries in S3 buckets (Cloud plan). Administrators only: the server signs in to the bucket with the keys given,
/// which are kept protected and never returned; creating one needs a recent authentication.
/// </summary>
public sealed partial class S3LibraryService(
    ArcanistDbContext db,
    AccessGuard guard,
    ICapabilityService capabilities,
    ScanCoordinator scans,
    UserAccessCache accessCache,
    IServiceProvider services)
{
    private S3Storage Storage => (S3Storage)services.GetRequiredService<ILibraryStorageResolver>().For(LibrarySourceKind.S3);

    public async Task<S3ConnectionTest> TestAsync(S3ConnectionInput input, CancellationToken cancellationToken)
    {
        guard.Admin();
        RequirePlan();
        return await TestAsync(Validate(input, null), cancellationToken);
    }

    public async Task<Library> CreateAsync(CreateS3LibraryInput input, CancellationToken cancellationToken)
    {
        var actor = guard.Admin();
        guard.RecentAuthentication();
        RequirePlan();

        var errors = new ValidationErrors();
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
            errors.Add("name", ErrorCodes.Required);
        else if ((await db.Libraries.Select(l => l.Name).ToListAsync(cancellationToken)).Any(n => TextNormalizer.Normalize(n) == TextNormalizer.Normalize(name)))
            errors.Add("name", ErrorCodes.Duplicate);
        var target = Validate(input.Connection, errors, "connection.");
        errors.ThrowIfAny();
        var connection = target.Connection;

        var test = await TestAsync(target, cancellationToken);
        if (!test.Success)
            throw new ArcanistException.Conflict(test.ErrorCode!);

        var library = new LibraryEntity
        {
            Number = await LibraryService.NextNumberAsync(db, cancellationToken),
            Name = name,
            VisibleToAll = false,
            ScanIntervalHours = 24,
        };
        var prefix = target.Prefix;
        library.Sources.Add(new LibrarySourceEntity
        {
            Library = library,
            Kind = LibrarySourceKind.S3,
            Location = Storage.NormalizeLocation($"{S3Storage.Scheme}{connection.Bucket}/{prefix}"),
            Settings = JsonSerializer.Serialize(new S3SourceSettings(
                connection.Endpoint.ToString().TrimEnd('/'), connection.Region, connection.AccessKeyId, Storage.Protect(connection.SecretAccessKey),
                connection.PathStyle), JsonSerializerOptions.Web),
        });
        db.Libraries.Add(library);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.InvalidateAll();
        await scans.EnqueueAsync(library.Id, ScanKind.Full, actor.UserId, cancellationToken);
        return library.ToModel(includeSources: true);
    }

    private void RequirePlan()
    {
        if (!capabilities.IsEnabled(Capability.ThirdPartyStorage))
            throw new ArcanistException.Conflict(ErrorCodes.S3Unavailable);
    }

    private async Task<S3ConnectionTest> TestAsync((S3Connection Connection, string Prefix) target, CancellationToken cancellationToken)
    {
        var client = Storage.Client(target.Connection);
        var prefix = target.Prefix.Length == 0 ? "" : target.Prefix + "/";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var page = await client.ListAsync(prefix, null, 1000, timeout.Token);
            var audio = page.Objects.Count(o => AudioFormats.IsAudio(o.Key) && !o.Key[prefix.Length..].Split('/').Any(s => s.StartsWith('.')));
            return new S3ConnectionTest(true, null, audio, page.ContinuationToken is not null);
        }
        catch (S3Exception e)
        {
            var code = e.Code is "NoSuchBucket" || e.Status == HttpStatusCode.NotFound ? ErrorCodes.S3BucketNotFound
                : e.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized || e.Code is "AccessDenied" or "SignatureDoesNotMatch" or "InvalidAccessKeyId"
                    ? ErrorCodes.S3AccessDenied
                    : ErrorCodes.S3Unreachable;
            return new S3ConnectionTest(false, code, 0, false);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new S3ConnectionTest(false, ErrorCodes.S3Unreachable, 0, false);
        }
    }

    /// <summary>Checks the fields, reporting them as <paramref name="field"/>-prefixed errors (thrown at once when <paramref name="errors"/> is null).</summary>
    private static (S3Connection Connection, string Prefix) Validate(S3ConnectionInput? input, ValidationErrors? errors, string field = "")
    {
        var own = errors ?? new ValidationErrors();
        if (input is null)
        {
            own.Add(field + "endpoint", ErrorCodes.Required);
            own.ThrowIfAny();
            throw new InvalidOperationException();
        }

        if (!Uri.TryCreate(input.Endpoint?.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            own.Add(field + "endpoint", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(input.Bucket) || !BucketName().IsMatch(input.Bucket.Trim()))
            own.Add(field + "bucket", ErrorCodes.InvalidFormat);
        if (string.IsNullOrWhiteSpace(input.AccessKeyId))
            own.Add(field + "accessKeyId", ErrorCodes.Required);
        if (string.IsNullOrWhiteSpace(input.SecretAccessKey))
            own.Add(field + "secretAccessKey", ErrorCodes.Required);
        if (input.Prefix is { } prefix && prefix.Split('/').Any(s => s is "." or ".."))
            own.Add(field + "prefix", ErrorCodes.InvalidFormat);
        if (errors is null)
            own.ThrowIfAny();
        if (!own.IsEmpty)
            return (null!, "");

        var region = string.IsNullOrWhiteSpace(input.Region) ? "us-east-1" : input.Region.Trim();
        return (new S3Connection(endpoint!, region, input.Bucket.Trim(), input.AccessKeyId.Trim(), input.SecretAccessKey.Trim(), input.PathStyle),
            input.Prefix?.Trim().Trim('/') ?? "");
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]{1,254}$")]
    private static partial Regex BucketName();
}
