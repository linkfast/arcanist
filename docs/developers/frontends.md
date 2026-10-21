# Packaging a web player

Administrators install web players from the admin panel (**Extensions → Web players**), by uploading a `.zip` or
giving its URL. One of them is the default and is served at `/`; every player is also served at `/players/<id>/`.
This guide describes the package. How a player talks to Arcanist is in the [client guide](README.md).

## The package

A `.zip` with the built player: static files only (HTML, JavaScript, CSS, images, fonts). The manifest
`arcanist-frontend.json` goes at the root of the archive, or inside a single top-level folder (as zipping a `dist`
folder usually produces).

```json
{
  "id": "my-player",
  "name": "My Player",
  "version": "1.2.0",
  "description": "A player for big screens.",
  "author": "Jane Doe",
  "license": "MIT",
  "homepage": "https://example.org/my-player",
  "minApiVersion": 1,
  "entry": "index.html"
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | 2–64 characters: lowercase letters, digits and dashes. It names the folder and the URL `/players/<id>/`. `admin` and `player` are taken, and so are the ids of the players that come with the server (`webamp`). Installing a package with an id already installed replaces that player. |
| `name`, `version` | Yes | Shown in the panel. |
| `description`, `author`, `license`, `homepage` | No | Shown in the panel, so administrators know what they install. |
| `minApiVersion` | No (1) | The oldest `serverInfo.apiVersion` the player works with. Servers with an older API refuse the package. |
| `entry` | No (`index.html`) | The page served for the player's routes. |

Limits: 250 MB compressed, 1 GB unpacked, 20,000 files. Paths that would leave the package folder are refused.

## Paths

The same build is served at `/` and at `/players/<id>/`. Arcanist sets the entry page's `<base href>` to wherever
it is served (replacing the one in the file, or adding one), so:

- refer to your files with **relative** URLs (`assets/app.js`, not `/assets/app.js`);
- call the APIs with absolute paths (`/graphql`, `/rest/v1/…`): they are on the same origin, wherever the player is.

Client-side routes fall back to the entry page. Files whose name contains a content hash (for example
`main-3F2A1B9C.js`) are sent as immutable; others are revalidated.

## Publishing it in the Marketplace

Administrators install web players from the **Marketplace** (Extensions → Marketplace → Web players) with one
click, as extensions. Its catalog is [github.com/linkfast/arcanist-extensions](https://github.com/linkfast/arcanist-extensions),
which lists web players in `players`: publish your player's `.zip` as a release of a public GitHub repository and
propose it with the **Add a web player** issue form; an automation checks the package (its `arcanist-frontend.json`)
and opens a pull request. Say how it is offered (open source, free, freemium or paid). Ultimate Web Player, LinkFast's
player, is listed there as well (free, official).

## Trust

A player runs on the same origin as the admin panel and the APIs: it acts with the session of whoever opens it. The
panel warns administrators of this and asks for their password again before installing. Never ask for or store
passwords in a player beyond the sign-in described in the client guide.
