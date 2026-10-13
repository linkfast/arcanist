# Writing a server extension

Extensions add endpoints and services to the Arcanist server. The built-in Subsonic and mStream APIs are extensions
written with the same SDK. Administrators install extensions from the admin panel (**Extensions**), by uploading a
`.zip` or giving its URL, and switch them on and off there.

An extension runs **inside the server, with full trust**: it can read and change everything the server can. The
panel says so before installing and asks for the administrator's password again. Since it links with the server,
its license must be compatible with the AGPL-3.0-or-later.

## The SDK

Reference the `Arcanist.Extensibility` assembly (and the server's `Arcanist.Core`, if you use its application
services) and target the same .NET version as the server. Implement `IArcanistExtension`:

```csharp
using Arcanist.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public sealed class HelloExtension : IArcanistExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
        services.AddSingleton<Greeter>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context) =>
        endpoints.MapGet($"{context.BasePath}/hello", (Greeter greeter) => greeter.Greet());
}
```

- `ConfigureServices` runs before the server starts, after the server registered its own services.
- `MapEndpoints` maps the extension's routes. `ExtensionContext` gives its `Id`, its `BasePath` (`/ext/<id>`), a
  `DataDirectory` of its own and its `Configuration` (the `Arcanist:Extensions:<id>` section of the settings).
- Use the server's application services (`Arcanist.Core.Application.*`, from dependency injection) for anything
  about users' data, so permissions and library access apply. When your extension checks credentials of its own,
  call `ExtensionAuthentication.SignInAsync(context, userId, scheme, cancellationToken)` to make the rest of the
  request act as that user.

## Routes

Every route must be under `/ext/<id>/`, or under a prefix the manifest claims among those Arcanist keeps for
compatibility layers: `/api`, `/media`, `/album-art`, `/transcode`, `/shared` and `/rest` (but never `/rest/v1`, the
core API). An extension that maps anything else fails to start. Two enabled extensions cannot claim the same prefix.
While an extension is switched off, its routes answer 404.

## The package

A `.zip` with the extension's assemblies (its dependencies included, but not the SDK, the server or ASP.NET Core,
which come from the server) and the manifest `arcanist-extension.json` at its root or inside a single top-level
folder:

```json
{
  "id": "hello",
  "name": "Hello",
  "version": "1.0.0",
  "description": "Says hello.",
  "author": "Jane Doe",
  "license": "AGPL-3.0-or-later",
  "homepage": "https://example.org/hello",
  "minApiVersion": 1,
  "assembly": "Hello.dll",
  "routes": []
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | 2–64 characters: lowercase letters, digits and dashes. |
| `name`, `version` | Yes | Shown in the panel. |
| `assembly` | Yes | The file, relative to the package, with the `IArcanistExtension` implementation (exactly one public, non-abstract class). |
| `routes` | No | Prefixes claimed besides `/ext/<id>`. |
| `minApiVersion` | No (1) | The oldest `serverInfo.apiVersion` the extension works with. |
| `description`, `author`, `license`, `homepage` | No | Shown in the panel. |

## Loading

An installed extension is loaded at startup when it is switched on, in an `AssemblyLoadContext` of its own: the SDK,
the server and the framework are shared with the server, and the extension's other dependencies are its own.
Assemblies are read into memory, so a package can be replaced while the server runs. Switching an installed
extension on, installing or updating it takes effect after a restart; switching it off applies at once. The panel
shows whether an extension is running, failed (see the server log) or waits for a restart.
