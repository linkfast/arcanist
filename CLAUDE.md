# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Fase 1 en curso. Hasta ahora solo existe el scaffolding: `Program.cs` mapea `GET /` → `"Hello World!"` y el panel Angular tiene una página de prueba. La sección "Arquitectura decidida" describe el diseño aprobado, que **todavía se está implementando**: comprobar en el código qué existe antes de asumirlo. Mantener este archivo alineado con el código conforme avance.

Git en la rama `main`, sin remoto. Hay dos `.gitignore`: el de la raíz (`dotnet new gitignore`) y el de `src/Arcanist.Web/` (Angular). `.gitattributes` fuerza LF en todo el repo salvo `.bat`/`.cmd`.

**Todos los commits llevan `Signed-off-by`** (`git commit -s`): el proyecto usa DCO (ver `CONTRIBUTING.md`).

## Estructura y comandos

```
Arcanist.slnx                 solución .NET (formato .slnx)
Directory.Build.props         metadatos comunes (licencia AGPL-3.0-or-later, copyright)
global.json                   SDK 10.0.100, rollForward latestFeature (la máquina tiene SDKs 8, 9 y 10)
mise.toml                     fija Node 24.15.0 para todo el repo
src/Arcanist.Core/            servidor ASP.NET Core, net10.0
src/Arcanist.Web/             panel de administración: Angular 22 + Tailwind v4 + spartan-ng (paquete npm `arcanist-web`)
```

`Arcanist.Web` **no está en la solución `.slnx`** (Angular no es un proyecto MSBuild); se construye y prueba por separado con npm.

**Core (.NET)**, desde la raíz:

```bash
dotnet build Arcanist.slnx
dotnet run --project src/Arcanist.Core          # http://localhost:5263 / https://localhost:7137
dotnet test                                      # todos los tests
dotnet test --project tests/Arcanist.Core.Tests -- --filter-method "*Nombre_del_test"   # un test
dotnet test --project tests/Arcanist.Core.Tests -- --filter-class "*DataDirectoryTests" # una clase
```

- Los tests usan **xUnit v3 sobre Microsoft Testing Platform**, activado en `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`). Por eso no hay `Microsoft.NET.Test.Sdk` ni `xunit.runner.visualstudio`, y los filtros van después de `--` con la sintaxis de MTP, no con `--filter` de VSTest.
- Los tests de integración usan `ArcanistFactory` (`WebApplicationFactory<Program>`), que arranca el servidor con un directorio de datos temporal y el entorno `Testing`.
- `DatabaseProviderTests` ejecuta las mismas aserciones contra SQLite en memoria y contra PostgreSQL 17 en un contenedor de Testcontainers. Si Docker no está disponible, los de PostgreSQL aparecen como **skipped**, no como verdes. Incluye un test que falla si el modelo cambió sin migración para alguno de los dos proveedores.

**Migraciones EF Core** (herramienta local `dotnet-ef` en `dotnet-tools.json`; ejecutar `dotnet tool restore` una vez). Cada cambio de modelo necesita **las dos**:

```bash
dotnet ef migrations add <Nombre> --project src/Arcanist.Core --context SqliteArcanistDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add <Nombre> --project src/Arcanist.Core --context PostgresArcanistDbContext --output-dir Data/Migrations/Postgres
```

El proveedor se configura en `Arcanist:Database` (`Provider`: `Sqlite`/`Postgres`, `ConnectionString`). Nada se resuelve contra la BD hasta que esa sección existe. Los servicios obtienen el contexto por DI (`ArcanistDbContext`, scoped) o con `IArcanistDbContextFactory`, que es lo que usan los trabajos en segundo plano.

**Directorio de datos** (`Hosting/DataDirectory.cs`, `Hosting/ArcanistPaths.cs`):
- Se elige, por orden, con `--data-dir`, `Arcanist__DataDir` o `ARCANIST_DATA_DIR`.
- Por defecto:
  - Development: `src/Arcanist.Core/.data/` (ignorado por git).
  - Contenedor: `/data`.
  - systemd: `$STATE_DIRECTORY`.
  - Windows: `%ProgramData%\Arcanist`.
  - Linux como root: `/var/lib/arcanist`; si no, `$XDG_DATA_HOME/arcanist`.
- Su `arcanist.json` se carga con más prioridad que `appsettings*.json` y menos que las variables de entorno y la línea de comandos.
- Los logs (Serilog) van a la consola y a `<datos>/logs/`.

**Web (Angular)**, desde `src/Arcanist.Web`:

```bash
npm start                        # ng serve
npm run build
npm test                         # ng test (Vitest + jsdom); usar `npx ng test --watch=false` para una sola pasada
npx ng test --watch=false --include="src/app/app.spec.ts"   # un solo archivo de test
```

**Node:** Angular 22 exige Node ≥ 22.22.3 o ≥ 24.15. La versión está fijada en `mise.toml`. El `node` que haya en el `PATH` de la shell puede ser otro más viejo (por ejemplo, uno de fnm), así que hay que ejecutar npm a través de mise:

```bash
mise exec -- npm run build
mise exec -- npx ng test --watch=false
```

mise solo lee `mise.toml` después de un `mise trust` (una vez por clon). Si falla con "not trusted", pedírselo al usuario en lugar de ejecutarlo.

### spartan-ng

- Los componentes se **copian al repo** en lugar de importarse de un paquete: viven en `src/Arcanist.Web/libs/ui/<componente>` y se importan por el alias `@spartan-ng/helm/<componente>` (paths en `tsconfig.json`). Son código propio y se pueden editar. La lógica headless viene de `@spartan-ng/brain`.
- La configuración del generador está en `src/Arcanist.Web/components.json` (`libs/ui`, estilo `nova`, alias `@spartan-ng/helm`). El tema de colores `neutral` está en `src/styles.css`.
- Añadir un componente: `npx ng g @spartan-ng/cli:ui <nombre> --defaults --no-interactive`. Sin `components.json`, el generador lanza prompts interactivos que se cuelgan en terminales no interactivas.
- Tailwind v4 se configura en `.postcssrc.json` (`@tailwindcss/postcss`); no hay `tailwind.config.js`.
- `npm audit` reporta 7 vulnerabilidades altas, todas en el tooling de desarrollo del CLI de spartan (`nx` → `smol-toml`). Con `--omit=dev` son 0.

## Qué es Arcanist

Servidor de música self-hosted y open source: un "Spotify personal/familiar". El usuario crea bibliotecas a partir de sus carpetas y las consume desde todos sus dispositivos. Compite con [mStream](https://github.com/IrosTheBeggar/mStream) y quiere superarlo.

- **Siempre self-hosted:** Windows (instalador, servicio y tray), Linux, Docker y NAS (Synology primero). **No existe un Arcanist en la nube.**
- **Ultimate Cloud** es un servicio comercial aparte que hace de "Arcanist en la nube" y es compatible con él. Los clientes Ultimate (Ultimate Web Player, Ultimate Desktop y los futuros móviles) hablan con ambos, así que **el contrato de API debe mantenerse estable y versionado**.
- **Integración con Ultimate Cloud:**
  - Una cuenta gratuita vinculada permite usar lo almacenado en Ultimate Cloud como fuente de biblioteca.
  - Un tier de pago añade sincronización con Ultimate Cloud, nubes de terceros (S3) y conexión iroh transparente.
  - Todo lo local es gratis, incluida la red P2P.
- **UI first:** todo se configura desde la web, con un asistente inicial y el panel. La configuración por archivo o variables de entorno es para usuarios avanzados y Docker. La CLI va al final.
- **Archivos tal como vienen:** Arcanist no deduplica bytes ni reescribe archivos para ahorrar espacio. Sí evita **pistas duplicadas en el catálogo**.

## Licencia y reglas

- **AGPL-3.0-or-later** (`LICENSE`). Las contribuciones se aceptan con DCO, sin CLA, así que el código aportado por terceros no se puede relicenciar.
- Las dependencias nuevas deben ser compatibles con AGPL-3.0: MIT, BSD, Apache-2.0, MPL-2.0, LGPL y GPL-3.0 sirven; **GPL-2.0-only no** (por ejemplo, el servidor de Jellyfin).
  - Cada dependencia nueva se registra en `THIRD-PARTY-NOTICES.md`.
  - Se descartaron ImageSharp, cuya Split License es de pago por encima de cierto volumen de ingresos, y TagLibSharp, estancado. En su lugar se usan SkiaSharp y ATL.NET.
- **No se usa código de mStream.** Solo se implementa compatibilidad con su API pública y la importación desde él.
- Por el §13 de la AGPL, el servidor ofrece su código a los usuarios en red: `serverInfo.sourceUrl` y un enlace "Código fuente" en la UI.

## Arquitectura decidida (en implementación)

- **Mapa de rutas (un solo puerto).** No crear rutas fuera de este esquema:
  - `/graphql`: la API principal.
  - `/rest/v1/**`: la API OpenAPI, equivalente a GraphQL salvo las suscripciones. Contrato en `/rest/v1/openapi.json` y documentación Scalar en `/rest/docs`.
  - `/admin/**`: este panel, compilado con `--base-href /admin/`.
  - `/health`.
  - `/`: el frontend de reproducción activo (Ultimate Web Player por defecto, o uno de terceros instalado desde el admin).
  - `/players/{id}/**`: los demás frontends instalados.
  - `/ext/{id}/**`: extensiones.
  - **Rutas reservadas:**
    - Para la extensión de compatibilidad con mStream: `/api/**`, `/media/**`, `/album-art/**`, `/transcode/**` y `/shared/**`.
    - Para la extensión Subsonic: `/rest/{método}[.view]`.
- **Una lógica, dos APIs:** los resolvers de Hot Chocolate y los endpoints Minimal API son capas finas sobre los servicios de `Application/`. La autorización y el filtrado por biblioteca viven **solo** en esa capa. Un test de contrato verifica la paridad GraphQL ↔ REST. Los contratos `schema.graphql` y `openapi.json` se versionan en el repo y se documentan en `docs/developers/` para quien haga players.
- **Datos:** EF Core 10 con dos contextos, `SqliteArcanistDbContext` y `PostgresArcanistDbContext`, que heredan de `ArcanistDbContext`.
  - Cada contexto tiene sus migraciones en `Data/Migrations/{Sqlite,Postgres}`. Toda migración se genera **para ambos**.
  - Claves `Guid.CreateVersion7()`, fechas como `DateTime` UTC, sin SQL específico de un proveedor.
  - El usuario elige SQLite o PostgreSQL en el asistente.
  - **Nunca** colocar el `.db` de SQLite en SMB/NFS.
- **Catálogo:**
  - `MediaFile` es el archivo físico, con `ContentHash` calculado solo sobre el audio.
  - `Track` es una grabación dentro de un álbum y puede tener varios `MediaFile`.
  - `Recording` es la grabación lógica, identificada por MBID o AcoustID.
  - Los datos de usuario cuelgan de `Track`/`Recording`, nunca de rutas.
  - La deduplicación va por `ContentHash`/MBID y, más adelante, por Chromaprint/AcoustID.
- **Almacenamiento:** `Library` → varias `LibrarySource`, siempre leídas a través de `ILibraryStorage`. Nunca usar `System.IO` directamente fuera de `LocalStorage`.
- **Permisos:** `Role` (Admin/User) + flags de permisos + `LibraryAccess(UserId, LibraryId)` por usuario y biblioteca. Los permisos se leen de la BD, no del JWT.
- **Capacidades:** `ICapabilityService`. Lo local siempre está activo. `UltimateCloudSource` requiere una cuenta gratuita; `CloudSync`, `ThirdPartyStorage` y `CloudConnect` requieren tier de pago.
- **Setup (implementado):** `Setup/` y `Api/Rest/SetupEndpoints.cs`.
  - Mientras `Arcanist:Setup:Completed` no sea `true`, `SetupGateMiddleware` redirige `/` a `/admin/setup` y responde 503 `setup_required` a todo lo que no sea el asistente, `/admin`, `/health` o la documentación.
  - El asistente envía todo de una vez a `POST /rest/v1/setup/complete`, que valida, guarda la BD en `arcanist.json` (salvo que venga fijada por el entorno), migra, crea el admin, las bibliotecas y los ajustes, y marca el setup como completado.
  - Desde fuera de loopback (o a través de un proxy) se exige la cabecera `X-Setup-Code`.
  - `ISettingsFileWriter` escribe `arcanist.json` y recarga la configuración en el acto.
- **Errores de API como códigos:** `Api/Problems.cs`. REST devuelve problem details con `code`, y los errores de validación son `{ campo: [código] }`. La UI traduce los códigos; no mostrar textos del servidor.
- **Seguridad:**
  - Se asume que el puerto puede estar abierto a internet: rate limiting en auth y setup, y `ForwardedHeaders` para proxies inversos.
  - El código de setup se exige fuera de loopback.
  - Los frontends y extensiones de terceros tienen confianza total: se avisa al instalarlos y se pide reautenticación para las acciones sensibles.

## Roadmap

- **F1:** fundaciones y biblioteca (en curso).
- **F2:** distribución (instalador de Windows con servicio y tray en Avalonia, systemd, Docker, Synology).
- **F3:** escáner avanzado y ffmpeg (transcodificación, waveform, BPM y tonalidad, EBU R128, Chromaprint/AcoustID, letras, carátulas online, cue).
- **F4:** drop folder y auto-organización.
- **F5:** playlists, estadísticas y páginas tipo Spotify.
- **F6:** gestor de frontends, SDK de extensiones, compatibilidad e importador de mStream, Subsonic.
- **F7:** DLNA.
- **F8:** red P2P iroh (crate Rust `arcanist-p2p` con C ABI para .NET y uniffi para móviles) y su sección en el admin.
- **F9:** migrador SQLite → PostgreSQL con UI.
- **F10:** integración con Ultimate Cloud.

## Decisiones abiertas

Aún sin decidir. No asumir una opción sin consultar al usuario, y documentar aquí la elección cuando se tome:

- Herramienta del instalador de Windows (WiX, Inno Setup o Velopack). Se decide en la F2.
- DSP de análisis de audio en C# o en una crate Rust compartida. Se decide en la F3.
- Licencia del IDE Nitro de ChilliCream. Si no es compatible con AGPL, usar GraphiQL (MIT).
- URL pública del repositorio, para `serverInfo.sourceUrl`.
