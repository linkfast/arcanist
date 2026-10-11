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
