using System.ComponentModel;
using Arcanist.Core.Application.Scanning;
using Arcanist.Core.Auth;
using GraphQL;
using GraphQL.Server.Transports.AspNetCore.WebSockets;
using GraphQL.Transport;

namespace Arcanist.Core.Api.GraphQL;

// Subscriptions have no REST counterpart; REST clients poll (e.g. GET /rest/v1/scans/{id}).

public sealed partial class Subscription
{
    [Name("scanJobUpdated")]
    [Description("Changes of scans of one library, or of all the user can see: status changes immediately, progress at most twice a second. Requires RUN_SCANS or MANAGE_LIBRARIES.")]
    public static IObservable<ScanJob> ScanJobUpdated([Id] Guid? libraryId, [FromServices] ScanService scans) =>
        scans.Watch(libraryId);
}

/// <summary>
/// Authenticates GraphQL WebSocket connections. Browsers cannot send headers on WebSockets, so clients put the
/// access token in the <c>connection_init</c> payload as <c>{ "Authorization": "Bearer …" }</c>.
/// </summary>
internal sealed class WebSocketAuthentication : IWebSocketAuthenticationService
{
    public async Task AuthenticateAsync(IWebSocketConnection connection, string subProtocol, OperationMessage operationMessage)
    {
        var context = connection.HttpContext;
        var serializer = context.RequestServices.GetRequiredService<IGraphQLSerializer>();
        var payload = serializer.ReadNode<Dictionary<string, object?>>(operationMessage.Payload);

        // Values arrive as JsonElement; ToString() yields a string value's text.
        var header = payload?.FirstOrDefault(p => p.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return;

        var result = await SessionValidator.ValidateTokenAsync(header["Bearer ".Length..].Trim(), context.RequestServices, context.RequestAborted);
        if (result is not { } validated)
            return;

        context.User = validated.Principal;
        context.Items[typeof(UserAccess)] = validated.Access;
    }
}
