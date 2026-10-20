using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Arcanist.Core.Api.Rest;
using Arcanist.Core.Application.Catalog;
using Arcanist.Core.Application.Libraries;
using Arcanist.Core.Application.WishList;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;

namespace Arcanist.Core.Tests.Api;

public sealed class WishListTests : IAsyncLifetime
{
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-wishes").FullName;
    private readonly string _incoming = Directory.CreateTempSubdirectory("arcanist-wishes-in").FullName;
    private readonly ArcanistFactory _factory = new();
    private HttpClient _admin = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        AudioFixtures.WriteWav(Path.Combine(_music, "Old", "1.wav"), 1, new FixtureTags("Already Here", "Band", "Old", TrackNumber: 1));
        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        _admin = await _factory.CreateAuthenticatedClientAsync();
        await _factory.WaitForScansAsync(_admin);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var dir in new[] { _music, _incoming })
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task A_wished_song_is_recognised_when_it_arrives_and_the_user_is_told()
    {
        // Wanted, and one the library has already.
        var wanted = await AddAsync(_admin, new WishInput("Heroes", "David Bowie", Note: "the album version"));
        Assert.Equal((WishStatus.Wanted, false), (wanted.Status, wanted.FoundWhenAdded));
        var there = await AddAsync(_admin, new WishInput("already here", "BAND"));
        Assert.Equal((WishStatus.Obtained, true, TrackMatchKind.Tags), (there.Status, there.FoundWhenAdded, there.MatchedBy));
        Assert.Equal("Already Here", there.Track!.Title);

        var token = (await _factory.LoginAsync()).AccessToken;
        using var socket = await ConnectAsync(token);
        await SendAsync(socket, new { id = "1", type = "subscribe", payload = new { query = "subscription { wishObtained { wishId title trackId } }" } });

        // The file arrives (copied in, as an import or an upload would leave it) and the library is scanned.
        var file = AudioFixtures.WriteWav(Path.Combine(_incoming, "heroes.wav"), 2, new FixtureTags("Heroes", "David Bowie", "\"Heroes\"", TrackNumber: 3));
        Directory.CreateDirectory(Path.Combine(_music, "Bowie"));
        File.Move(file, Path.Combine(_music, "Bowie", "heroes.wav"));
        var library = (await _admin.GetFromJsonAsync<List<Library>>("/rest/v1/libraries", ArcanistFactory.Json, Cancel))!.Single();
        await _admin.PostAsJsonAsync($"/rest/v1/libraries/{library.Id}/scans", new StartScanInput(), Cancel);
        await _factory.WaitForScansAsync(_admin);

        var message = await ReceiveAsync(socket);
        var obtained = message.GetProperty("payload").GetProperty("data").GetProperty("wishObtained");
        Assert.Equal(("Heroes", wanted.Id.ToString()), (obtained.GetProperty("title").GetString(), obtained.GetProperty("wishId").GetString()));

        var list = await _admin.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes", ArcanistFactory.Json, Cancel);
        Assert.Equal((0, 1), (list!.Wanted, list.Unseen));
        var heroes = list.Items.Single(w => w.Id == wanted.Id);
        Assert.Equal((WishStatus.Obtained, false, "Heroes"), (heroes.Status, heroes.FoundWhenAdded, heroes.Track!.Title));
        Assert.Equal("the album version", heroes.Note);

        Assert.Equal(1, await (await _admin.PostAsync("/rest/v1/me/wishes/seen", null, Cancel)).Content.ReadFromJsonAsync<int>(Cancel));
        Assert.Equal(0, (await _admin.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes?status=OBTAINED", ArcanistFactory.Json, Cancel))!.Unseen);
    }

    [Fact]
    public async Task Wishes_are_each_users_and_another_song_is_wanted_again()
    {
        await _factory.CreateUserAsync("ana");
        var ana = await _factory.CreateAuthenticatedClientAsync("ana");
        var wish = await AddAsync(ana, new WishInput("Already Here", "Band"));
        Assert.Equal(WishStatus.Obtained, wish.Status);

        // Nobody else sees or changes it.
        Assert.Empty((await _admin.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes", ArcanistFactory.Json, Cancel))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"/rest/v1/me/wishes/{wish.Id}", new WishInput("X", "Y"), ArcanistFactory.Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/rest/v1/me/wishes/{wish.Id}", Cancel)).StatusCode);

        // Another song: wanted again. By an album the library does not have, it stays wanted.
        var changed = await (await ana.PutAsJsonAsync($"/rest/v1/me/wishes/{wish.Id}", new WishInput("Already Here", "Band", Album: "Live"), ArcanistFactory.Json, Cancel))
            .Content.ReadFromJsonAsync<Wish>(ArcanistFactory.Json, Cancel);
        Assert.Equal((WishStatus.Wanted, null), (changed!.Status, changed.Track));

        // GraphQL, the same.
        var graphql = await ana.GraphQLAsync("""{ wishList(status: WANTED) { wanted items { title album status } } }""");
        Assert.Equal(1, graphql.GetProperty("data").GetProperty("wishList").GetProperty("wanted").GetInt32());

        var invalid = await ana.PostAsJsonAsync("/rest/v1/me/wishes", new { title = " ", artist = "Band", acoustId = "nope" }, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors");
        Assert.Equal(("required", "invalid_format"), (errors.GetProperty("title")[0].GetString(), errors.GetProperty("acoustId")[0].GetString()));

        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/rest/v1/me/wishes/{wish.Id}", Cancel)).StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<WishListResult>("/rest/v1/me/wishes", ArcanistFactory.Json, Cancel))!.Items);
    }

    private static async Task<Wish> AddAsync(HttpClient client, WishInput input)
    {
        var response = await client.PostAsJsonAsync("/rest/v1/me/wishes", input, ArcanistFactory.Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Wish>(ArcanistFactory.Json, Cancel))!;
    }

    private async Task<WebSocket> ConnectAsync(string token)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        var socket = await client.ConnectAsync(new Uri(_factory.Server.BaseAddress, "graphql"), Cancel);
        await SendAsync(socket, new { type = "connection_init", payload = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        Assert.Equal("connection_ack", (await ReceiveAsync(socket)).GetProperty("type").GetString());
        return socket;
    }

    private static Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, Cancel);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
    }
}
