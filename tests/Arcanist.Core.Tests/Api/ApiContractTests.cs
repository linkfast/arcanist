using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arcanist.Core.Tests.Api;

/// <summary>
/// Guards the public contracts: both APIs expose the same operations, and the committed contract files in
/// <c>docs/api</c> match what the server serves. After an intended API change, regenerate them with
/// <c>ARCANIST_UPDATE_CONTRACTS=1 dotnet test --project tests/Arcanist.Core.Tests -- --filter-class "*ApiContractTests"</c>
/// and review the diff: removing or renaming anything breaks third-party clients.
/// </summary>
public sealed class ApiContractTests : IAsyncLifetime
{
    private const string UpdateVariable = "ARCANIST_UPDATE_CONTRACTS";

    private readonly ArcanistFactory _factory = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _factory.CompleteSetupAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Every_graphql_operation_has_a_rest_operation_with_the_same_name()
    {
        var schema = await _factory.CreateClient().GraphQLAsync("""
            { __schema { queryType { fields { name } } mutationType { fields { name } } } }
            """);
        var fields = new[] { "queryType", "mutationType" }
            .SelectMany(t => schema.GetProperty("data").GetProperty("__schema").GetProperty(t).GetProperty("fields").EnumerateArray())
            .Select(f => f.GetProperty("name").GetString()!)
            .ToList();

        var openApi = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/rest/v1/openapi.json", Cancel);
        var operations = openApi.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.TryGetProperty("operationId", out _))
            .Select(operation => operation.Value.GetProperty("operationId").GetString()!)
            .ToHashSet();

        Assert.NotEmpty(fields);
        var missing = fields.Where(f => !operations.Contains(char.ToUpperInvariant(f[0]) + f[1..])).ToList();
        Assert.True(missing.Count == 0, $"GraphQL operations without a REST counterpart: {string.Join(", ", missing)}");
    }

    [Fact]
    public Task GraphQL_schema_matches_docs_api_schema_graphql() =>
        AssertContractAsync("/graphql/schema.graphql", "schema.graphql", text => text);

    [Fact]
    public Task OpenApi_document_matches_docs_api_openapi_json() =>
        AssertContractAsync("/rest/v1/openapi.json", "openapi.json",
            text => JsonSerializer.Serialize(JsonDocument.Parse(text), new JsonSerializerOptions { WriteIndented = true }));

    [Fact]
    public async Task GraphiQL_replaces_the_nitro_ide()
    {
        var client = _factory.CreateClient();

        var ui = await client.GetStringAsync("/graphql/ui", Cancel);
        Assert.Contains("graphiql@", ui);

        var request = new HttpRequestMessage(HttpMethod.Get, "/graphql");
        request.Headers.Accept.ParseAdd("text/html");
        var response = await client.SendAsync(request, Cancel);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task AssertContractAsync(string url, string fileName, Func<string, string> canonicalize)
    {
        var response = await _factory.CreateClient().GetAsync(url, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = Normalize(canonicalize(await response.Content.ReadAsStringAsync(Cancel)));

        var path = Path.Combine(RepositoryRoot(), "docs", "api", fileName);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, Cancel);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing; generate it with {UpdateVariable}=1.");
        var expected = Normalize(await File.ReadAllTextAsync(path, Cancel));
        Assert.True(expected == actual,
            $"The served contract differs from docs/api/{fileName}. If the API change is intended, regenerate it with {UpdateVariable}=1 and review the diff.");
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd() + "\n";

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Arcanist.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root (Arcanist.slnx) not found.");
    }
}
