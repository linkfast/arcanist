# Contributing to Arcanist

Thanks for your interest in Arcanist.

## Developer Certificate of Origin

Arcanist does not use a Contributor License Agreement. Instead, every commit must be signed off under the [Developer Certificate of Origin 1.1](https://developercertificate.org/), certifying that you wrote the change or otherwise have the right to submit it under the project's license (AGPL-3.0-or-later).

Sign off by adding a `Signed-off-by` line with your real name and email to each commit message:

```
Signed-off-by: Jane Doe <jane@example.com>
```

`git commit -s` adds it for you. Pull requests containing commits without a valid sign-off cannot be merged.

## Licensing of contributions

- All contributions are licensed under AGPL-3.0-or-later.
- New dependencies must use a license compatible with AGPL-3.0 (for example MIT, BSD, Apache-2.0, MPL-2.0, LGPL or GPL-3.0). GPL-2.0-only code cannot be combined with Arcanist.
- Do not copy code from other projects unless its license is compatible and its copyright notices are preserved. Arcanist does not reuse code from mStream; compatibility with mStream is implemented from its public API documentation.
- Add new third-party components to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Development

See the "Building" section of the [README](README.md). Run the tests before opening a pull request:

```bash
dotnet test Arcanist.slnx
```

```bash
cd src/Arcanist.Web
npx ng test --watch=false
```
