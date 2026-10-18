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
  `DataDirectory` of its own, its `Configuration` (the `Arcanist:Extensions:<id>` section of the settings) and its
  `PackageDirectory`, the installed package's folder, to serve files shipped with it (assemblies are loaded from
  memory, so `Assembly.Location` is empty).
- Use the server's application services (`Arcanist.Core.Application.*`, from dependency injection) for anything
  about users' data, so permissions and library access apply. When your extension checks credentials of its own,
  call `ExtensionAuthentication.SignInAsync(context, userId, scheme, cancellationToken)` to make the rest of the
  request act as that user.
- To put a file into a library for a user (something the extension fetched), call
  `UploadService.UploadAsync(access, libraryId, fileName, stream, source: "<your id>", cancellationToken)`. It is
  the user's upload: it needs their `UPLOAD` permission and access to the library, goes where the library's template
  says without overwriting anything, is scanned shortly after and shows in their history of uploads (`myUploads`)
  with `source` naming your extension. From background work, where there is no request, get the user with
  `UserAccessCache.GetUserAsync(userId, cancellationToken)` (and skip disabled users).

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
  "routes": [],
  "apps": [
    {
      "id": "greeter",
      "name": "Greeter",
      "names": { "es": "Saludador" },
      "description": "Says hello.",
      "descriptions": { "es": "Saluda." },
      "path": "/ext/hello/app/",
      "icon": "/ext/hello/icon.svg",
      "permissions": ["UPLOAD"],
      "width": 720,
      "height": 640
    }
  ],
  "adminPage": { "path": "/ext/hello/admin", "icon": "/ext/hello/icon.svg" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | 2–64 characters: lowercase letters, digits and dashes. |
| `name`, `version` | Yes | Shown in the panel. |
| `assembly` | Yes | The file, relative to the package, with the `IArcanistExtension` implementation (exactly one public, non-abstract class). |
| `routes` | No | Prefixes claimed besides `/ext/<id>`. |
| `apps` | No | Pages users open from their players (see [Apps](#apps)). |
| `adminPage` | No | Its settings page for administrators: `path` and an `icon`, under `/ext/<id>/` (see [Settings page](#settings-page)). |
| `minApiVersion` | No (1) | The oldest `serverInfo.apiVersion` the extension works with. |
| `description`, `author`, `license`, `homepage` | No | Shown in the panel. |

## Apps

An extension can offer pages to users, which players list with `extensionApps` (`GET /rest/v1/extension-apps`) and
open in a window (an iframe) or a tab of their own. Each app has:

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | Lowercase letters, digits and dashes, unique within the extension. |
| `name` | Yes | In English; `names` has it in other languages (`es`, `pt-BR`…). |
| `path` | Yes | The page, under `/ext/<id>/`. |
| `icon` | No | A square SVG, under `/ext/<id>/`. |
| `description`, `descriptions` | No | What it is for. |
| `permissions` | No | What a user needs to see it, as the API names permissions (`UPLOAD`, `DOWNLOAD`…). |
| `adminOnly` | No | Only administrators see it. |
| `width`, `height` | No | The window's preferred size in CSS pixels. |

Only the apps of extensions that are switched on and running are listed, and only to users with their permissions.
Serve the page and its files from your package (`ExtensionContext.PackageDirectory`). The permission is about
showing it: your API must still check what each request may do.

### Settings page

Settings that administrators change belong in a page of the extension declared as `adminPage`. The admin panel
lists it under **Extensions** (with the extension's name and the page's icon) while the extension runs, and shows it
inside the panel in an iframe, so the panel's menu does not grow with every extension. It gets the session, the
language and the panel's look with the same host protocol as apps. Changes that need a recent authentication answer
`reauthentication_required`: ask for the password and call `POST /rest/v1/auth/reauthenticate` with the token (it
returns a token that counts as just signed in, without touching the refresh cookie).

### The host protocol

An app is a page of the same server, so it calls the API like any client, with a Bearer token. It gets the token,
the language and the look of the player that opened it through `postMessage`, between windows of the same origin
(always check `event.origin === location.origin`; the player never sends the token anywhere else). The player is
`window.parent` when the app is in an iframe and `window.opener` when it opened the app in a tab.

| From | Message | Data |
|---|---|---|
| app | `arcanist:hello` | `{ version: 1 }`: asks for the session. Send it when the page loads. |
| player | `arcanist:session` | `{ token, expiresAt, language, theme }`. Sent again when the token is refreshed or the theme changes. |
| app | `arcanist:token-expired` | The token was refused (`401`): the player refreshes it and sends `arcanist:session`. |
| app | `arcanist:open` | `{ target: "uploads" }` opens the user's uploads in the player; `{ target: "track", id }` shows a song. |
| app | `arcanist:notify` | `{ kind: "info" \| "success" \| "error", text }`: a notice from the player (optional). |

Every message is an object with its name in `type` and the data beside it:
`{ type: "arcanist:session", token: "…", expiresAt: "2026-10-18T10:00:00Z", language: "es", theme: { … } }`.

`theme` lets the app look like the player: `scheme` (`light` or `dark`) and colors as CSS values, `background`,
`foreground`, `muted` (secondary text), `surface` (cards), `border`, `accent` and `accentForeground`, plus `radius`
(a CSS length) and `font` (a font family). Apply them as CSS custom properties, and keep a sensible look of your own
for a player that sends no theme or only some of these.

Opened on its own (no player answers `arcanist:hello` within a second), the app restores the session with the
refresh cookie (`POST /rest/v1/auth/refresh` with `{ "useCookie": true }`) or sends the user to sign in, and follows
`prefers-color-scheme`. Inside a player, never refresh the token yourself: refresh tokens rotate, and two clients
refreshing the same session would sign each other out.

## Loading

An installed extension is loaded at startup when it is switched on, in an `AssemblyLoadContext` of its own: the SDK,
the server and the framework are shared with the server, and the extension's other dependencies are its own.
Assemblies are read into memory, so a package can be replaced while the server runs. Switching an installed
extension on, installing or updating it takes effect after a restart (the panel offers **Restart now**,
`restartServer`); switching it off applies at once. The panel
shows whether an extension is running, failed (see the server log) or waits for a restart.
