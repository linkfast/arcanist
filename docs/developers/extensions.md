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
  "adminPage": { "icon": "/ext/hello/icon.svg" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | 2–64 characters: lowercase letters, digits and dashes. |
| `name`, `version` | Yes | Shown in the panel. |
| `assembly` | Yes | The file, relative to the package, with the `IArcanistExtension` implementation (exactly one public, non-abstract class). |
| `routes` | No | Prefixes claimed besides `/ext/<id>`. |
| `apps` | No | Pages users open from their players (see [Apps](#apps)). |
| `adminPage` | No | It has a settings page for administrators, with an optional `icon` under `/ext/<id>/` (see [Settings page](#settings-page)). |
| `minApiVersion` | No (1) | The oldest `serverInfo.apiVersion` the extension works with. |
| `description`, `author`, `homepage` | No | Shown in the panel. |
| `license` | No | An SPDX id or expression (`MIT`, `AGPL-3.0-or-later AND MIT`). It decides whether it is open source: without an open source license (none, or `LicenseRef-…`) it is closed source, which makes the server's edition STANDARD and is neither installed nor run while the server's 100% open source mode is on (`closed_source_disabled`). |

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

Settings that administrators change belong in the extension's settings page, declared as `adminPage` in the
manifest. The admin panel lists it under **Extensions** (with the extension's name and the page's icon) while the
extension runs, and **draws it with its own components**: the extension describes the page as data and no code of
the extension runs in the panel, so it looks like the rest of the panel, in light and dark, on any screen.

Implement `IExtensionAdminPage` and register it keyed by the extension's id:

```csharp
public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
    services.AddKeyedScoped<IExtensionAdminPage, HelloAdminPage>(context.Id);

public sealed class HelloAdminPage(HelloSettings settings) : IExtensionAdminPage
{
    public Task<AdminPage> GetAsync(AdminPageContext context, CancellationToken ct) => Task.FromResult(new AdminPage(
    [
        new AdminSection("greeting", context.Language == "es" ? "Saludo" : "Greeting", null,
        [
            AdminBlock.Field("text", context.Language == "es" ? "Texto" : "Text", AdminFieldType.Text, settings.Text),
            AdminBlock.Status("Status", settings.Text.Length > 0 ? "Ready" : "Empty", AdminTone.Good),
        ], [new AdminAction("reset", "Reset", AdminActionStyle.Destructive, "The greeting goes back to its default.")]),
    ]));

    public Task<AdminPageResult> SaveAsync(AdminPageContext context, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct)
    {
        if (values.TryGetValue("text", out var text))
            settings.Save(text.GetString() ?? "");
        return Task.FromResult(AdminPageResult.Done("Saved"));
    }

    public Task<AdminPageResult> RunAsync(AdminPageContext context, string action, IReadOnlyDictionary<string, JsonElement> values, CancellationToken ct) =>
        Task.FromResult(action == "reset" ? AdminPageResult.Done("Reset") : AdminPageResult.Failed("Unknown action"));
}
```

- A page is a list of **sections** (cards), each with a title, a description, **blocks** and **actions** (buttons).
- Blocks are fields (`Text`, `Secret`, `Number`, `Switch`, `Select`, `MultiSelect`, `TextArea`, `Url`, `Library`),
  statuses (a label and a value with a tone), notices, progress bars and lists. `RefreshSeconds` reads the page again
  while it is open, to follow something in progress.
- The panel sends every field to `SaveAsync` by its key, and the fields as they are on screen to `RunAsync`. A
  `Secret` field is never shown back: it comes empty, and empty means "keep what there is".
- Write the texts in `AdminPageContext.Language`. `AdminPageResult.Failed` takes a message and the problems by
  field key, which the panel shows under each field.
- Every call comes from an administrator; saving and actions also need a recent authentication (the panel asks for
  the password when needed). An exception becomes `extension_failed`.
- **What it still needs** (tools to download, an account to set): return the page with `Setup = new AdminSetup(title,
  description, items, action, actionLabel)` while it is missing, and null once done. Right after the extension is
  installed and starts, the panel shows it in a dialog and, with the administrator's consent, runs that action (the
  same `RunAsync`); the page shows it at the top too. Needs Arcanist 0.1.0-alpha.8 or later.

The panel uses `GET /rest/v1/extensions/{id}/admin`, `PUT /rest/v1/extensions/{id}/admin` (`{ "values": { … } }`)
and `POST /rest/v1/extensions/{id}/admin/actions/{action}`, which other admin tools can use too.

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
| app | `arcanist:play` | `{ trackIds: ["…"] }`: the player plays these songs of the library, after what plays now (at once if nothing does; up to 50). |
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

## Managed extensions

A deployment can install extensions through the server's configuration instead of the panel: private ones, for a
managed service, that its administrators must not remove. Each is an entry of `Arcanist:ManagedExtensions`, usually
given as environment variables:

```sh
Arcanist__ManagedExtensions__0__Id=my-extension
Arcanist__ManagedExtensions__0__Package=https://example.com/my-extension-1.2.0.zip   # or a path on the machine
Arcanist__ManagedExtensions__0__Sha256=<the .zip's SHA-256>
Arcanist__ManagedExtensions__0__Version=1.2.0   # optional: the oldest version to run
```

At startup, before extensions load, the server installs each one that is missing, or older than `Version`, from its
package (only if the SHA-256 matches; a failure is logged and the server starts anyway). A managed extension is always
on: switching it off or uninstalling it answers `extension_managed` (409), and the panel shows it as **Managed**
without those controls. Installing a newer version of it (from a file, a URL or the marketplace) updates it as usual;
the configured package only replaces a version older than `Version`. Its own settings, such as credentials, come from
its configuration section (`ExtensionContext.Configuration`, `Arcanist:Extensions:<id>:…`), environment variables
included. `Extension.managed` says which ones are.

### Managed libraries

A managed extension whose service keeps the music (a hosted storage) makes its libraries with the core's
`ManagedLibraries` (from DI):

```csharp
var id = await managed.EnsureS3Async("my-extension", "main", "My music",
    new S3ConnectionInput(endpoint, region, bucket, prefix, accessKeyId, secret, PathStyle: true),
    encryptionKey, // the customer's 256-bit key in base64, or null; see docs/storage-encryption.md
    cancellationToken);
```

The first call creates the library (visible to everyone, scanned every day) and scans it; later calls with the same
extension and key return the same id and update its name, credentials and key (rotated credentials are always the
newest), scanning again only if its place changed. Call it at startup and whenever the service's configuration changes.
These libraries work without Ultimate Cloud (neither its extension nor its plan), are used like any other (uploads,
organizing, drop folders), and administrators cannot delete them or change their sources (`library_managed`, 409).
`Library.managedBy` says which extension keeps one; the panel marks it **Managed**.

### Wish lists

`IWishLists` (from DI, in the SDK) reads and changes the users' wish lists: one user's (`ListAsync`), everyone's
(`ListAllAsync`, each entry with its user), `AddAsync` (the song is looked for at once among the libraries that user
can play), `RemoveAsync`, and `ResolveLinkAsync`, which reads a streaming service's link into a `WishListEntryInput`
(null when the link is not a song or outside services are not allowed). It checks no permissions: when an extension
shows someone else's wishes, it decides who may see them (the server's own rule is `VIEW_ALL_WISHES`). For example, a
downloader can offer what users want:

```csharp
var wanted = await wishLists.ListAllAsync(wantedOnly: true, cancellationToken);
```

## Publishing in the Marketplace

The panel's **Marketplace** (Extensions) lists the extensions of the catalog at
[github.com/linkfast/arcanist-extensions](https://github.com/linkfast/arcanist-extensions) and installs them with one
click: the server downloads the release, checks it against the SHA-256 the catalog lists (and that it holds the
extension the catalog names) and installs it as any other package, switched on, offering to restart. To be listed,
publish the extension's `.zip` as a release of a public repository and open a pull request there with its entry; its
README describes the format. Each entry says how it is offered (`pricing`: open source, free, freemium or
paid, with an optional `pricingUrl`), which the marketplace shows and filters by; purchases are handled by each
extension's maintainer, in the extension or on their site. `extensionMarketplace` / `GET /rest/v1/extensions/marketplace` and
`installMarketplaceExtension` / `POST /rest/v1/extensions/marketplace/install` are the API; another catalog can be
used with `Arcanist:Marketplace:Url`.
