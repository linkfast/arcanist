using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Arcanist.Core.Storage.S3;

/// <summary>Where a bucket is and how to sign in to it.</summary>
/// <param name="PathStyle">Address the bucket in the path (<c>endpoint/bucket/key</c>, MinIO and most S3-compatible
/// services) rather than in the host name (<c>bucket.endpoint/key</c>, AWS).</param>
internal sealed record S3Connection(Uri Endpoint, string Region, string Bucket, string AccessKeyId, string SecretAccessKey, bool PathStyle);

/// <param name="Metadata">The object's <c>x-amz-meta-*</c> headers (from HEAD), by lowercase name.</param>
internal sealed record S3Object(string Key, long Size, DateTime LastModified, IReadOnlyDictionary<string, string>? Metadata = null);

/// <param name="Prefixes">With a delimiter: the "folders" directly under the prefix.</param>
internal sealed record S3ListPage(IReadOnlyList<S3Object> Objects, string? ContinuationToken, IReadOnlyList<string>? Prefixes = null);

/// <summary>An error answered by the S3 service (its <c>Code</c>, e.g. <c>NoSuchBucket</c>, <c>AccessDenied</c>).</summary>
internal sealed class S3Exception(HttpStatusCode status, string? code) : IOException($"S3 answered {(int)status} {code}")
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;
}

/// <summary>
/// The few S3 operations a library needs, over plain HTTP with AWS Signature Version 4, so any S3-compatible service
/// works (AWS, MinIO, Backblaze B2, Wasabi, Cloudflare R2…) without an SDK. Payloads are not hashed
/// (<c>UNSIGNED-PAYLOAD</c>): audio is streamed, and TLS protects it.
/// </summary>
internal sealed class S3Client(HttpClient http, S3Connection connection, TimeProvider time)
{
    private static readonly XNamespace Ns = "http://s3.amazonaws.com/doc/2006-03-01/";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    /// <summary>One page of the objects under a prefix (ListObjectsV2).</summary>
    public async Task<S3ListPage> ListAsync(
        string prefix, string? continuationToken, int maxKeys, CancellationToken cancellationToken, string? delimiter = null)
    {
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["list-type"] = "2", ["max-keys"] = maxKeys.ToString(CultureInfo.InvariantCulture) };
        if (prefix.Length > 0)
            query["prefix"] = prefix;
        if (delimiter is not null)
            query["delimiter"] = delimiter;
        if (continuationToken is not null)
            query["continuation-token"] = continuationToken;

        using var response = await SendAsync(HttpMethod.Get, null, query, null, null, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        XDocument document;
        try
        {
            document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (XmlException)
        {
            throw new S3Exception(response.StatusCode, "InvalidResponse");
        }

        var root = document.Root!;
        var objects = root.Elements(Ns + "Contents")
            .Select(c => new S3Object(
                c.Element(Ns + "Key")!.Value,
                long.Parse(c.Element(Ns + "Size")?.Value ?? "0", CultureInfo.InvariantCulture),
                DateTime.TryParse(c.Element(Ns + "LastModified")?.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var modified)
                    ? modified
                    : DateTime.UnixEpoch))
            .ToList();
        var truncated = root.Element(Ns + "IsTruncated")?.Value == "true";
        var prefixes = root.Elements(Ns + "CommonPrefixes").Select(p => p.Element(Ns + "Prefix")?.Value).OfType<string>().ToList();
        return new S3ListPage(objects, truncated ? root.Element(Ns + "NextContinuationToken")?.Value : null, prefixes);
    }

    /// <summary>The size and date of an object; null when there is none.</summary>
    public async Task<S3Object?> HeadAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Head, key, null, null, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureAsync(response, cancellationToken);
        var metadata = response.Headers.Where(h => h.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(',', h.Value));
        return new S3Object(key, response.Content.Headers.ContentLength ?? 0, response.Content.Headers.LastModified?.UtcDateTime ?? DateTime.UnixEpoch,
            metadata);
    }

    /// <summary>An object's bytes from <paramref name="from"/> to <paramref name="to"/> (inclusive; null for the end). The caller disposes the response.</summary>
    public async Task<HttpResponseMessage> GetAsync(string key, long from, long? to, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, key, null, null, request =>
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new FileNotFoundException("No such object.", key);
        }

        await EnsureAsync(response, cancellationToken);
        return response;
    }

    /// <param name="metadata">Stored with the object as <c>x-amz-meta-*</c> headers (names given in full, in lowercase).</param>
    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var response = await SendAsync(HttpMethod.Put, key, null, body, request =>
        {
            foreach (var (name, value) in metadata ?? new Dictionary<string, string>())
                request.Headers.Add(name, value);
        }, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    /// <summary>Copies an object within the bucket (moves are a copy and a delete: S3 has no rename).</summary>
    public async Task CopyAsync(string from, string to, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, to, null, null,
            request => request.Headers.Add("x-amz-copy-source", "/" + connection.Bucket + "/" + EncodePath(from)), cancellationToken);
        await EnsureAsync(response, cancellationToken);
        // A copy can fail after the 200 has been sent: the error is then in the body.
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Contains("<Error>", StringComparison.Ordinal))
            throw new S3Exception(response.StatusCode, Code(text));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, key, null, null, null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
            await EnsureAsync(response, cancellationToken);
    }

    /// <summary>Creates the bucket (for tests and first setups).</summary>
    public async Task CreateBucketAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, null, null, null, null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Conflict)
            await EnsureAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string? key, SortedDictionary<string, string>? query, HttpContent? content, Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        var (uri, host, path) = Address(key, query);
        var request = new HttpRequestMessage(method, uri) { Content = content };
        configure?.Invoke(request);

        var now = time.GetUtcNow().UtcDateTime;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        request.Headers.Add("x-amz-date", amzDate);
        request.Headers.Add("x-amz-content-sha256", UnsignedPayload);

        // Signed: the host and every x-amz-* header.
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["host"] = host };
        foreach (var header in request.Headers.Where(h => h.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)))
            headers[header.Key.ToLowerInvariant()] = string.Join(',', header.Value).Trim();

        request.Headers.TryAddWithoutValidation("Authorization",
            Authorization(method.Method, path, CanonicalQuery(query), headers, UnsignedPayload, now, connection.Region, connection.AccessKeyId, connection.SecretAccessKey));
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        finally
        {
            // The content is the caller's stream: only the request goes.
            request.Content = null;
            request.Dispose();
        }
    }

    /// <summary>The request URI, the Host header value and the canonical (encoded) path.</summary>
    private (Uri Uri, string Host, string Path) Address(string? key, SortedDictionary<string, string>? query)
    {
        var endpoint = connection.Endpoint;
        var basePath = endpoint.AbsolutePath.TrimEnd('/');
        var keyPath = key is null ? "" : "/" + EncodePath(key);
        string host;
        string path;
        if (connection.PathStyle)
        {
            host = endpoint.IsDefaultPort ? endpoint.Host : endpoint.Authority;
            path = basePath + "/" + Encode(connection.Bucket) + keyPath;
        }
        else
        {
            host = connection.Bucket + "." + (endpoint.IsDefaultPort ? endpoint.Host : endpoint.Authority);
            path = basePath + (keyPath.Length == 0 ? "/" : keyPath);
        }

        var queryString = CanonicalQuery(query);
        var uri = new Uri($"{endpoint.Scheme}://{host}{path}{(queryString.Length > 0 ? "?" + queryString : "")}");
        return (uri, host, path);
    }

    /// <summary>The Authorization header of AWS Signature Version 4 for S3.</summary>
    internal static string Authorization(
        string method, string canonicalPath, string canonicalQuery, SortedDictionary<string, string> headers, string payloadHash, DateTime now,
        string region, string accessKeyId, string secretAccessKey)
    {
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalHeaders = string.Concat(headers.Select(h => $"{h.Key}:{h.Value}\n"));
        var canonicalRequest = $"{method}\n{canonicalPath}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var scope = $"{date}/{region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), Encoding.UTF8.GetBytes(date));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, "s3"u8.ToArray());
        key = HMACSHA256.HashData(key, "aws4_request"u8.ToArray());
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        return $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    internal static string CanonicalQuery(SortedDictionary<string, string>? query) =>
        query is null ? "" : string.Join('&', query.Select(q => $"{Encode(q.Key)}={Encode(q.Value)}"));

    /// <summary>Percent-encodes everything but A-Z, a-z, 0-9, '-', '_', '.' and '~', as SigV4 asks.</summary>
    internal static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
                builder.Append((char)b);
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>A key as a path: each segment encoded, the slashes kept.</summary>
    internal static string EncodePath(string key) => string.Join('/', key.Split('/').Select(Encode));

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var text = response.RequestMessage?.Method == HttpMethod.Head ? "" : await response.Content.ReadAsStringAsync(cancellationToken);
        throw new S3Exception(response.StatusCode, Code(text) ?? response.StatusCode.ToString());
    }

    private static string? Code(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Root?.Element("Code")?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
