# Third-party notices

Arcanist includes or depends on the following third-party components. Each is used under the license shown; full license texts are distributed with each package.

## Server (`src/Arcanist.Core`)

| Component | License |
|---|---|
| .NET / ASP.NET Core (including `Microsoft.Extensions.Hosting.WindowsServices`, `.Systemd` and `Microsoft.AspNetCore.Authentication.JwtBearer`) | MIT |
| Microsoft.IdentityModel (JSON Web Tokens) | MIT |
| Serilog (`Serilog.AspNetCore` and its sinks) | Apache-2.0 |
| Entity Framework Core, Microsoft.Data.Sqlite | MIT |
| SQLitePCLRaw | Apache-2.0 |
| SQLite | Public domain |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License |
| GraphQL.NET (`GraphQL`, `GraphQL.SystemTextJson`, `GraphQL.Server.Transports.AspNetCore`) | MIT |
| Microsoft.AspNetCore.OpenApi | MIT |
| Scalar API reference (`Scalar.AspNetCore`, served at `/rest/docs`) | MIT |
| ATL.NET (`z440.atl.core`) | MIT |
| SkiaSharp (including `SkiaSharp.NativeAssets.Linux.NoDependencies`) | MIT |
| Skia | BSD-3-Clause |

GraphiQL (MIT), React and ReactDOM (MIT) are loaded by the browser from jsDelivr at `/graphql/ui`; they are not bundled.

## Admin panel (`src/Arcanist.Web`, production dependencies)

| Component | License |
|---|---|
| Angular (`@angular/*`, `@angular/cdk`) | MIT |
| spartan-ng (`@spartan-ng/brain`; helm components copied into `libs/ui`) | MIT |
| Tailwind CSS (`tailwindcss`, `@tailwindcss/postcss`) | MIT |
| PostCSS | MIT |
| class-variance-authority | Apache-2.0 |
| clsx | MIT |
| tailwind-merge | MIT |
| RxJS | Apache-2.0 |
| tslib | 0BSD |
