# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Fases 1 (fundaciones y biblioteca), 2 (distribución), 3 (análisis de audio, ffmpeg y servicios en línea), 4 (carpetas de entrada y organización), 5 (valoraciones, reproducciones, playlists, búsqueda y descubrimiento) y 6 (reproductores, extensiones, Subsonic, mStream e importador) completas. Pendiente:
- De la F2: probar el instalador de Windows y el modo servicio en una máquina limpia, publicar la primera release y enviar el paquete a SynoCommunity.
- De la F3: registrar la clave de AcoustID del proyecto (`OnlineOptions.ProjectAcoustIdKey`). La descarga real de ffmpeg desde el panel ya se probó (build BtbN n9.0, Windows x64).
- De la F4: sugerir etiquetas con AcoustID para los archivos por revisar (necesita la clave del proyecto).
- De la F6: probar Subsonic y mStream con apps reales (Symfonium, DSub, Feishin, Amperfy, mStream Music) y el importador con una instalación real de mStream 5 y 6 (también el hash PBKDF2: no hay vector de prueba publicado).
- Antes de la primera release, fusionar las migraciones en una `Initial` (ver "Datos").

Siguiente: Fase 7, ver "Roadmap". La sección "Arquitectura decidida" describe el diseño aprobado: comprobar en el código qué existe antes de asumirlo. Mantener este archivo alineado con el código conforme avance.

Git en la rama `main`, sin remoto todavía; el repositorio público será `github.com/linkfast/arcanist` (los workflows, `RepositoryUrl` y el paquete Synology ya apuntan ahí). Hay dos `.gitignore`: el de la raíz (`dotnet new gitignore`) y el de `src/Arcanist.Web/` (Angular). `.gitattributes` fuerza LF en todo el repo salvo `.bat`/`.cmd`.

**Todos los commits llevan `Signed-off-by`** (`git commit -s`): el proyecto usa DCO (ver `CONTRIBUTING.md`).

## Estructura y comandos

```
Arcanist.slnx                 solución .NET (formato .slnx)
Directory.Build.props         metadatos comunes (licencia AGPL-3.0-or-later, copyright)
global.json                   SDK 10.0.100, rollForward latestFeature (la máquina tiene SDKs 8, 9 y 10)
mise.toml                     fija Node 24.15.0 para todo el repo
src/Arcanist.Core/            servidor ASP.NET Core, net10.0; el ejecutable se llama `arcanist`
src/Arcanist.Extensibility/  SDK de extensiones (IArcanistExtension, manifiesto)
src/Arcanist.Web/             panel de administración: Angular 22 + Tailwind v4 + spartan-ng (paquete npm `arcanist-web`)
src/Arcanist.Tray/            app de bandeja para Windows (Avalonia 12 + Velopack), net10.0-windows
native/arcanist-audio/        crate Rust de análisis de audio (Symphonia), con C ABI para .NET
tests/                        Arcanist.Core.Tests, Arcanist.Tray.Tests y Arcanist.TestExtension (extensión de prueba)
packaging/                    instalador de Windows, tarballs y unidad systemd de Linux, paquete Synology (spksrc)
.github/workflows/            ci.yml y release.yml
```

`Arcanist.Web` **no está en la solución `.slnx`** (Angular no es un proyecto MSBuild); se construye y prueba por separado con npm.

**Crate nativa** (`native/arcanist-audio`, Rust 1.85+ por la edición 2024):
- `dotnet build` del servidor ejecuta `cargo build --release` si hay Rust (incremental, target `BuildNativeAudio` del csproj) y copia `arcanist_audio.dll`/`libarcanist_audio.so` a la salida. Sin Rust ni librería precompilada, avisa y el servidor funciona sin análisis.
- Para otros RID: `packaging/native/build.sh <rid>` deja la librería en `artifacts/native/<rid>/`, de donde la toma `dotnet publish -r <rid>`. Linux usa cargo-zigbuild con glibc 2.27 (`pip install cargo-zigbuild ziglang`).
- `cargo test --release --manifest-path native/arcanist-audio/Cargo.toml` para sus tests. Ejemplos de diagnóstico: `cargo run --release --example analyze -- <archivos>` (análisis y velocidad) y `--example compare -- <referencia> <archivos>` (similitud de huellas).
- Los pánicos se capturan en la frontera FFI: un archivo corrupto nunca tumba el servidor. **No poner `panic = "abort"`.**

**Core (.NET)**, desde la raíz:

```bash
dotnet build Arcanist.slnx
dotnet run --project src/Arcanist.Core          # http://localhost:5263 / https://localhost:7137
dotnet test                                      # todos los tests
dotnet test --project tests/Arcanist.Core.Tests -- --filter-method "*Nombre_del_test"   # un test
dotnet test --project tests/Arcanist.Core.Tests -- --filter-class "*DataDirectoryTests" # una clase
dotnet test --project tests/Arcanist.Tray.Tests  # tests del tray (solo Windows)
```

- **Si el servidor de desarrollo está corriendo**, bloquea `bin/Debug/arcanist.exe`: compilar y probar con `--artifacts-path artifacts/test-build` (dentro del repo, para que `ApiContractTests` encuentre la raíz) y crear migraciones con `--configuration Release`. No parar el servidor del usuario.
- Los tests usan **xUnit v3 sobre Microsoft Testing Platform**, activado en `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`). Por eso no hay `Microsoft.NET.Test.Sdk` ni `xunit.runner.visualstudio`, y los filtros van después de `--` con la sintaxis de MTP, no con `--filter` de VSTest.
- `xunit.runner.json` amplía a 30 s la espera de xUnit al cerrar el proceso de tests (por defecto 10 s). Con la máquina muy cargada, el cierre normal superaba los 10 s y xUnit fallaba con "Foreground threads were left running" aunque todos los tests pasaran; se investigó y no había hilos de Arcanist vivos. Una fuga real sigue fallando, porque nunca termina.
- Los tests de integración usan `ArcanistFactory` (`WebApplicationFactory<Program>`), que arranca el servidor con un directorio de datos temporal y el entorno `Testing`.
- `ArcanistFactory` desactiva el análisis en segundo plano (`Arcanist:Analysis:Enabled=false`); los tests que lo necesitan lo activan y esperan con `WaitForAnalysisAsync()`. Su tercer parámetro permite reemplazar servicios (por ejemplo, `FakeOnlineServices` para los clientes HTTP de los servicios en línea: los tests nunca salen a internet).
- Los tests que necesitan un ffmpeg real (`TestFfmpeg.Require()`) se marcan como **skipped** si no hay ffmpeg en el `PATH`.
- `DatabaseProviderTests` ejecuta las mismas aserciones contra SQLite en memoria y contra PostgreSQL 17 en un contenedor de Testcontainers. Si Docker no está disponible, los de PostgreSQL aparecen como **skipped**, no como verdes. Incluye un test que falla si el modelo cambió sin migración para alguno de los dos proveedores.

**Migraciones EF Core** (herramienta local `dotnet-ef` en `dotnet-tools.json`; ejecutar `dotnet tool restore` una vez). Cada cambio de modelo necesita **las dos**:

```bash
dotnet ef migrations add <Nombre> --project src/Arcanist.Core --context SqliteArcanistDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add <Nombre> --project src/Arcanist.Core --context PostgresArcanistDbContext --output-dir Data/Migrations/Postgres
```

El proveedor se configura en `Arcanist:Database` (`Provider`: `Sqlite`/`Postgres`, `ConnectionString`). Nada se resuelve contra la BD hasta que esa sección existe. Los servicios obtienen el contexto por DI (`ArcanistDbContext`, scoped) o con `IArcanistDbContextFactory`, que es lo que usan los trabajos en segundo plano.

**Distribución** (todas las variantes incluyen el panel y escuchan en el puerto 8484, definido en `appsettings.json`):
- `docker build -t arcanist .`: imagen multi-etapa. El panel y el servidor se compilan en `$BUILDPLATFORM` y la etapa final no tiene `RUN`, así que `docker buildx build --platform linux/amd64,linux/arm64 .` no necesita QEMU. Usa el usuario `app` y el volumen `/data`; `--build-arg VERSION=x.y.z` fija la versión. Desde el host las peticiones no son loopback, así que el asistente pide el código de setup que aparece en `docker logs`.
- `packaging/linux/pack.sh [versión] [--skip-panel]`: tarballs self-contained linux-x64/arm64 en `artifacts/linux/`, con `arcanist.service` (systemd con `DynamicUser`, `StateDirectory` y `Type=notify`). Probado bajo systemd real en un contenedor.
- `pwsh packaging/windows/pack.ps1 [-Version x.y.z] [-SkipPanel]`: instalador Velopack en `artifacts/windows/releases/`, con el tray como `mainExe` y el servidor en `server\`. vpk se niega a empaquetar una versión igual o menor que otra que ya esté en esa carpeta: borrarla para repetir.
- `packaging/build-panel.sh` compila el panel una vez para todos los paquetes (usa mise si existe). Se quitan los `.pdb`: los nativos de SkiaSharp duplicarían el tamaño.
- `packaging/synology/spksrc/`: `cross/arcanist` y `spk/arcanist` para SynoCommunity; descargan los tarballs de la release. **No se han construido en spksrc**: hacerlo al enviarlos, tras la primera release.
- `dotnet publish src/Arcanist.Core -c Release -p:IncludeAdminPanel=true` también incluye el panel en `wwwroot/admin` (necesita npm con Node 24 en el PATH).
- **CI** (`ci.yml`): servidor en Linux (con PostgreSQL), tray en Windows y panel. **Release** (`release.yml`, al empujar un tag `v*`): crea un borrador de release, adjunta el instalador y los paquetes de Velopack (con delta respecto a la release anterior) y los tarballs, publica la imagen multi-arch en `ghcr.io/<owner>/arcanist` y después publica la release.
- `docs/developers/README.md` es la guía pública para quien haga reproductores. **Actualizarla cuando cambie el contrato** (autenticación, paginación, streaming, errores…).

**Supervisión** (`Hosting/Supervision.cs`), el contrato con los lanzadores:
- Mientras el servidor corre existe `<datos>/runtime.json` con `processId`, `version`, `addresses` y `startedAt`. Se borra al parar.
- Con `--Arcanist:Hosting:ExitWhenInputCloses=true`, el servidor se detiene limpiamente al cerrarse su stdin. Así lo para el tray, y no sobrevive si el tray muere.
- Si el directorio de trabajo no tiene `appsettings.json` pero el del ejecutable sí, se usa este como content root (`Hosting/ContentRoot.cs`).
- `SupervisionTests` lo comprueba lanzando el ejecutable real.
- El servidor se niega a arrancar sin ICU (`TextNormalizer.EnsureSupported`): en modo invariante, .NET no normaliza y las búsquedas dejarían de ignorar los acentos.

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
npm start                        # ng serve en http://localhost:4200/admin/, con proxy de /rest, /graphql (WebSocket) y /health a Core :5263
npm run build                    # dist/arcanist-web/browser; Core lo sirve en /admin con Arcanist__Frontends__AdminRoot=<esa ruta>
npm run codegen                  # tipos de las operaciones GraphQL desde docs/api/schema.graphql (los generados se versionan)
npm test                         # ng test (Vitest + jsdom); usar `npx ng test --watch=false` para una sola pasada
npx ng test --watch=false --include="src/app/app.spec.ts"   # un solo archivo de test
```

**Node:** Angular 22 exige Node ≥ 22.22.3 o ≥ 24.15. La versión está fijada en `mise.toml`. El `node` que haya en el `PATH` de la shell puede ser otro más viejo (por ejemplo, uno de fnm), así que hay que ejecutar npm a través de mise:

```bash
mise exec -- npm run build
mise exec -- npx ng test --watch=false
```

mise solo lee `mise.toml` después de un `mise trust` (una vez por clon). Si falla con "not trusted", pedírselo al usuario en lugar de ejecutarlo.

### Panel de administración (`src/Arcanist.Web`)

- **Datos:** setup y autenticación van por REST (`core/api/rest.ts`); todo lo demás va por GraphQL con Apollo (`core/graphql/`). Las operaciones viven en `src/app/graphql/operations.ts` y se tipan con `npm run codegen`; **regenerar tras cambiar operaciones o el esquema** (codegen valida contra `docs/api/schema.graphql`).
- **Sesión** (`core/auth/`):
  - El token de acceso vive solo en memoria y el de refresco va en la cookie HttpOnly (`useCookie: true`). Tras recargar, `authGuard` restaura la sesión con `refresh()`.
  - `authInterceptor` añade el Bearer a `/rest` y `/graphql`. Ante `unauthorized` refresca una vez y reintenta; ante `reauthentication_required` abre `ReauthDialog` y reintenta. Lo hace también para GraphQL, cuyos errores llegan en el cuerpo.
  - Las suscripciones usan `graphql-ws` y envían el token en `connection_init`.
- **i18n propia** (`core/i18n/`): diccionarios `es.ts`/`en.ts` con claves anidadas, pipe `t` y `I18n.error(code)` para los códigos de la API. `i18n.spec.ts` exige las mismas claves en ambos idiomas y una traducción para cada código de `Application/Errors.cs`: **al añadir un código en el servidor, traducirlo aquí**.
- **Patrones:** componentes standalone, zoneless, señales y formularios por plantilla (`ngModel`). Los diálogos de spartan se abren con `[state]` y `*brnDialogContent`; como se ajustan a su contenido, los anchos fijos van en `hlm-dialog-content`. La interfaz oculta lo que el usuario no puede hacer (`AuthService.can`), pero la autorización real está en el servidor. Los tiempos relativos (`relativeTime`) leen un reloj-señal que avanza cada 10 s: sin zone.js, un pipe impuro dentro de un `@for` no se reevalúa por sí solo.
- Prettier: `npx prettier --write src` (ignora `src/app/graphql/generated`). El presupuesto de bundle inicial es de 1 MB por Apollo y GraphQL.

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
  - `/graphql`: la API principal (HTTP y WebSocket). `/graphql/ui` (GraphiQL) y `/graphql/schema.graphql` (SDL).
  - `/rest/v1/**`: la API OpenAPI, equivalente a GraphQL salvo las suscripciones. Contrato en `/rest/v1/openapi.json` y documentación Scalar en `/rest/docs`.
  - `/admin/**`: este panel, compilado con `--base-href /admin/`.
  - `/health`.
  - `/`: el frontend de reproducción predeterminado (Ultimate Web Player, o uno instalado desde el admin).
  - `/players/{id}/**`: todos los frontends (también el predeterminado).
  - `/ext/{id}/**`: extensiones.
  - **Prefijos que pueden reclamar las extensiones** (`ExtensionHost.ClaimablePrefixes`):
    - La de mStream: `/api/**`, `/media/**`, `/album-art/**` y `/transcode/**`. `/shared/**` queda reservado (enlaces compartidos de mStream, sin implementar).
    - La de Subsonic: `/rest/{método}[.view]`, nunca `/rest/v1`.
- **Una lógica, dos APIs (implementado):**
  - Los resolvers GraphQL (`Api/GraphQL/`) y los endpoints Minimal API (`Api/Rest/`) son capas finas sobre los servicios de `Application/`.
  - La autorización vive **solo** en esos servicios: cada operación empieza con `AccessGuard` (`Authenticated()`, `Permission(...)`, `Admin()`, `RecentAuthentication()`), y lo mismo aplicará al filtrado por biblioteca.
  - Los fallos esperados se lanzan como `ArcanistException` (`Application/Errors.cs`). REST los convierte en problem details con `code`; GraphQL, en errores con `extensions.code` (y `extensions.errors` para validación).
  - **GraphQL usa GraphQL.NET** (MIT) con esquema "type-first": clases parciales `Query` y `Mutation` con métodos **estáticos**, `[Name("campo")]`, `[Description]`, `[Id]` para los Guid y `[FromServices]` para los servicios. Cada funcionalidad añade sus operaciones en su propio archivo parcial.
  - **Hot Chocolate quedó descartado:** todas sus versiones cargan en tiempo de ejecución el IDE Nitro (ChilliCream License 1.0, incompatible con AGPL). No reintroducirlo.
  - **Convención de nombres:** el `operationId` REST (`.WithName(...)`) es el campo GraphQL en PascalCase (`createUser` ↔ `CreateUser`). `ApiContractTests` falla si una operación GraphQL no tiene su equivalente REST. Login y setup existen solo en REST.
  - Los enums se serializan en UPPER_SNAKE_CASE en ambas APIs (`MANAGE_USERS`). GraphQL.NET separaría los dígitos (`MP_3`); `RestEnumNamesAttribute` (atributo global en `GraphQLSetup`) usa la misma conversión que System.Text.Json, así que ambas dicen `MP3`. Los tipos de contrato no se llaman igual que las entidades (`UserAccount`, no `User`).
  - Contratos versionados en `docs/api/schema.graphql` y `docs/api/openapi.json`. `ApiContractTests` falla si difieren de lo que sirve el servidor. Tras un cambio intencionado de API, regenerarlos y revisar el diff:

    ```bash
    ARCANIST_UPDATE_CONTRACTS=1 dotnet test --project tests/Arcanist.Core.Tests -- --filter-class "*ApiContractTests"
    ```

  - **Suscripciones** por WebSocket en `/graphql` (subprotocolos `graphql-transport-ws` y `graphql-ws`). El token va en el payload de `connection_init` como `{"Authorization": "Bearer …"}` y lo valida `WebSocketAuthentication` con `SessionValidator`, igual que en HTTP. Se comprueba al conectar; una suscripción abierta no caduca con el token. Las suscripciones no tienen equivalente REST.
  - Herramientas: GraphiQL en `/graphql/ui` (cargado de jsDelivr con SRI), SDL en `/graphql/schema.graphql`, Scalar en `/rest/docs` (embebido, funciona sin internet).
- **Datos:** EF Core 10 con dos contextos, `SqliteArcanistDbContext` y `PostgresArcanistDbContext`, que heredan de `ArcanistDbContext`.
  - Cada contexto tiene sus migraciones en `Data/Migrations/{Sqlite,Postgres}`. Toda migración se genera **para ambos**.
  - Antes de la primera versión publicada las migraciones se fusionaron en una `Initial`, y se volverán a fusionar justo antes de publicarla (la F3 añadió varias). **Una vez publicada, nunca reescribir ni fusionar migraciones.** Evitar operaciones que en SQLite obligan a reconstruir la tabla (renombrar o borrar columnas) salvo que sea imprescindible.
  - Claves `Guid.CreateVersion7()`, fechas como `DateTime` UTC, sin SQL específico de un proveedor. **Única excepción: la búsqueda** (`Data/Search/FullTextSearch.cs`), ver "Búsqueda".
  - El usuario elige SQLite o PostgreSQL en el asistente.
  - **Nunca** colocar el `.db` de SQLite en SMB/NFS.
- **Catálogo:**
  - `MediaFile` es el archivo físico, con `ContentHash` calculado solo sobre el audio.
  - `Track` es una grabación dentro de un álbum y puede tener varios `MediaFile`.
  - `Recording` es la grabación lógica, identificada por MBID o AcoustID.
  - Los datos de usuario cuelgan de `Track`/`Recording`, nunca de rutas.
  - La deduplicación va por `ContentHash`/MBID y, más adelante, por Chromaprint/AcoustID.
- **Almacenamiento (implementado):** `Library` → varias `LibrarySource`, siempre leídas a través de `ILibraryStorage` (`Storage/`), que se obtiene por tipo con `ILibraryStorageResolver`.
  - Nunca usar `System.IO` sobre archivos de bibliotecas fuera de `LocalStorage`. La única excepción es `DirectoryBrowser`, que lista y crea carpetas del servidor para elegir ubicaciones.
  - `LocalStorage` ignora archivos y carpetas ocultos (`.`) y carpetas de sistema de NAS y SO (`@eaDir`, `#recycle`, `$RECYCLE.BIN`…). También rechaza rutas relativas que escapen de la raíz.
  - **Las rutas relativas se guardan tal cual están en disco, sin normalizar Unicode:** en Linux, un nombre en NFD (copiado desde un Mac) solo se abre con esa grafía exacta.
  - Las ubicaciones se validan con `LibraryRules` (compartido con el setup): deben existir y ser legibles, y no pueden repetirse ni anidarse dentro de la misma biblioteca (código `overlapping`). Entre bibliotecas distintas sí pueden coincidir.
  - **Escrituras (F4):** `MoveAsync`, `ImportAsync` (trae un archivo local; entra con un nombre oculto `.arcanist-partial-*` y se renombra, así nunca se ve a medias), `TrashAsync`, `PurgeTrashAsync`, `RemoveEmptyFoldersAsync`, `CanWriteAsync` y `Watch` (FileSystemWatcher; null si no se puede vigilar). **Nunca sobrescriben** (`StorageConflictException`) **ni borran**: lo que se quita va a `.arcanist-trash/<aaaa-mm-dd>/` en la raíz de la ubicación (oculta, así que los escaneos no la ven) y se purga a los `Arcanist:Organization:TrashRetentionDays` (30).
- **Bibliotecas (implementado):** `Application/Libraries/`.
  - Cada usuario ve las bibliotecas concedidas más las `VisibleToAll`; las demás responden 404, para no revelar que existen.
  - `sources` (las rutas) solo se devuelve a quien tiene `MANAGE_LIBRARIES`.
  - Un gestor que no es admin solo gestiona las bibliotecas que ve, y recibe acceso automático a las que crea.
  - Borrar una biblioteca exige step-up y **nunca toca los archivos**.
  - `serverDirectories` y `createServerDirectory` son solo para admins; durante el setup, `GET`/`POST /rest/v1/setup/directories`. Los nombres de carpeta nuevos se validan con las reglas de Windows, las más estrictas.
- **Escáner (implementado):** `Scanning/` y `Application/Scanning/`.
  - `ScanCoordinator` encola trabajos (`ScanJob` en BD) y `ScanWorker` (BackgroundService) los ejecuta **de uno en uno**. Una biblioteca nunca tiene dos trabajos activos. Al arrancar se recuperan los encolados y los interrumpidos.
  - Se escanea automáticamente al completar el setup, al crear una biblioteca y al añadir o quitar fuentes.
  - `LibraryScanner` procesa cada fuente así:
    1. Lista los archivos a través de `ILibraryStorage`.
    2. Salta los que no cambiaron en tamaño ni mtime (±2 s); los escaneos `Full` releen todo.
    3. Lee en paralelo (`Arcanist:Scanner:Parallelism`) con ATL (`TagReader`), calcula el `ContentHash` (`ContentHasher`, solo del audio) y guarda las carátulas (`ArtworkStore`, deduplicadas por SHA-256 en `<datos>/cache/artwork/`).
    4. Escribe por lotes con un único `CatalogWriter`.
  - **Salvaguarda:** si una fuente no responde, o aparece vacía cuando el catálogo tenía archivos de ella (un montaje caído), se omite con `source_unavailable` y **no se marca nada como desaparecido**.
  - Los archivos desaparecidos reciben `MissingSince` y se purgan tras `MissingFileRetentionDays` (7 por defecto). Después, `CatalogCleaner` borra lo que quede huérfano: pistas, álbumes, grabaciones, artistas, géneros y carátulas (también del disco).
  - **Reglas de `CatalogWriter`:**
    - Un archivo se une a una pista existente de su álbum si coincide el MBID de grabación o, si no, disco + número + título normalizado.
    - Un archivo re-etiquetado que es el único de su pista **conserva la pista**, para que los datos de usuario no se pierdan.
    - El álbum se agrupa por MBID de release o por artista del álbum + título normalizado. Sin etiqueta de álbum, se usa el nombre de la carpeta.
    - Los múltiples valores se separan por `;`, nunca por `/` (AC/DC); "feat." genera artistas `Featured`.
    - **Recopilatorios sin etiquetar:** archivos de artistas distintos en una misma carpeta, con el mismo título de álbum y sin AlbumArtist, forman un álbum "Various Artists" (`ToCompilation`). En escaneos incrementales se precargan los álbumes de esas carpetas. Los discos en subcarpetas acaban en el mismo álbum.
    - Un archivo con `TrackLocked` (unido por detección de duplicados o por un admin) se queda en su pista y sus etiquetas no la modifican.
  - **Letras:** se leen de las etiquetas y de un `.lrc` junto al audio, que gana. Un `.lrc` más reciente que el último escaneo de su canción la hace releer. Cada pista guarda las más fiables (`Lyrics`: Sidecar > Embedded > Online).
  - **Hojas CUE:** un `.cue` junto a un archivo (que lo nombre, aunque con otra extensión, o que se llame igual) convierte cada pista en una fila `MediaFile` con `CueTrack`, `StartMs`/`EndMs` y un `ContentHash` derivado del rango. Las filas de una ruta viajan juntas en el mismo lote; el escritor borra las de una CUE anterior. Añadir, editar o quitar la CUE hace releer el archivo.
  - **En vivo:** `ScanEvents` publica cambios de los trabajos (estado al momento; progreso como mucho cada 500 ms) para la suscripción GraphQL `scanJobUpdated(libraryId)`. Los clientes REST sondean `GET /rest/v1/scans/{id}`.
  - **Escaneo programado:** `ScanScheduler` revisa cada minuto y encola un incremental cuando pasó `ScanIntervalHours` desde el último escaneo solicitado (aunque fallara, para no reintentar cada minuto).
  - Tests: `AudioFixtures` genera WAV etiquetados al vuelo, sin binarios en el repo. `ScannerTests` espera a los trabajos sondeando `GET /rest/v1/scans/{id}`.
- **Catálogo (implementado):** `Application/Catalog/`. Operaciones `artists`, `artist`, `albums`, `album`, `tracks` y `track`.
  - **Visibilidad:** una pista es visible si tiene algún archivo presente (`MissingSince == null`) en una biblioteca accesible. Álbumes y artistas son visibles a través de esas pistas. Lo invisible responde 404.
  - `Track.files` lista solo las copias accesibles, de mejor a peor calidad.
  - **Paginación keyset** (`Keyset`) por clave normalizada + id, con cursor opaco (`after`/`nextCursor`) y `totalCount`. Probada en ambos proveedores. `tracks(albumId)` devuelve el álbum entero en orden de disco y pista, sin paginar.
  - `TextNormalizer` quita acentos y pliega letras como æ/ø/ß/ł ("agaetis" encuentra "Ágætis"). **Cambiar sus reglas tras publicar exige una migración que recalcule los valores guardados** (las columnas `Normalized*` y `SearchText`).
- **Búsqueda (implementado):** `Data/Search/` y `Application/Search/`.
  - Artistas, álbumes, pistas y playlists guardan `SearchText`: palabras normalizadas (una pista lleva título, artistas y álbum). `CatalogWriter` lo pone a null en cada pista que escribe y `SearchIndexer` lo recalcula tras cada lote del escáner (también el de sus álbumes); `SearchIndexBackfill` lo completa al arrancar; `PlaylistService` lo escribe directamente.
  - **Índice nativo de cada proveedor, fuera del modelo de EF:** FTS5 de contenido externo con triggers en SQLite y una columna `SearchVector` (`tsvector` generada, configuración `simple`) con índice GIN en PostgreSQL. `FullTextSearch.EnsureAsync` los crea tras migrar y, en SQLite, los rehace si faltan triggers (una migración que reconstruya la tabla los perdería).
  - Cada palabra debe aparecer como inicio de palabra (`"sig"*` / `sig:*`); los términos se normalizan y solo quedan letras y dígitos, así que nunca son sintaxis. `FullTextSearch.Match` devuelve un `IQueryable<SearchHit>` (id y puntuación) para componer con los filtros de visibilidad. `search(query)` busca de todo a la vez; el parámetro `search` de `artists`/`albums`/`tracks` usa el mismo índice.
- **Streaming y carátulas (implementado):** `Api/Rest/MediaEndpoints.cs`, `Application/Streaming/` y `Application/Artwork/`. Solo REST.
  - `GET|HEAD /rest/v1/stream/{trackId}`: elige la mejor copia accesible (o `fileId`). Admite `Range` (206), `ETag` = `ContentHash` y `Cache-Control: private`. Con `download=true` envía el original como adjunto y exige `DOWNLOAD`.
  - **Autenticación del streaming:** `Bearer`, o `?st=` de `Track.streamUrl`/`TrackFile.streamUrl`. `StreamTokens` es un HMAC con clave propia en `<datos>/keys/`, ligado a usuario y sesión y válido 6 h. Revocar la sesión lo invalida. El log de peticiones no incluye la query string, así que el token no queda registrado.
  - **Transcodificación** (`Media/Transcoder.cs`) con `format` (opus, mp3, aac, flac), `maxBitRate` y `timeOffset`: sale por streaming sin Range, con `X-Content-Duration`; ffmpeg abre directamente los archivos locales (`ILibraryStorage.TryGetLocalPath`) y lee de stdin los remotos; se mata si el cliente se va; hay un límite de conversiones simultáneas. Sin ffmpeg: 503 `transcoding_unavailable`. Las pistas CUE siempre se convierten (FLAC si el original es sin pérdida). `serverInfo.transcodingFormats` anuncia lo que el ffmpeg instalado sabe codificar.
  - `GET /rest/v1/artwork/{id}?size=`: **pública** (para `<img>`; los ids no se adivinan) e inmutable. Las miniaturas JPEG (64…1024 px) se generan con SkiaSharp en `<datos>/cache/thumbnails/`.
- **Análisis de audio (implementado):** `Analysis/`, `native/arcanist-audio` y `Application/Analysis/`.
  - `AnalysisWorker` analiza cada audio distinto (por `ContentHash`) una vez por versión del analizador (`ANALYZER_VERSION` en `analyzer.rs`: subirla hace reanalizar), en hilos de prioridad baja (`Arcanist:Analysis:Parallelism`), y espera mientras hay escaneos. Una sola decodificación alimenta todo:
    - EBU R128 con histograma de bloques: `AlbumLoudness` combina los histogramas de las mejores copias de cada pista para la ganancia del álbum.
    - Huella Chromaprint de 120 s, compatible con AcoustID.
    - Waveform de 1000 puntos.
    - BPM por autocorrelación de flujo espectral, con un prior en 120.
    - Tonalidad por cromagrama y perfiles Krumhansl.
  - **Estados del análisis:**
    - Los formatos que Symphonia no lee quedan como `NeedsFfmpeg` y se reanalizan con `FfmpegDecoder` cuando hay ffmpeg.
    - Los archivos corruptos quedan como `Failed`.
    - Si un origen no se puede abrir, se reintenta en una hora sin marcar nada.
  - `AnalysisMonitor` + `analysisProgress`/`analysisProgressChanged`/`setAnalysisEnabled` (ajuste `analysis.enabled`). En el contrato: `Track.bpm`, `Track.key` (con código Camelot), `TrackFile.loudness` (ganancias ReplayGain 2.0 a −18 LUFS) y `trackWaveform`.
  - **Duplicados por huella** (`DuplicateDetector`, tras cada lote): compara con pistas del mismo álbum, título o artista y duración parecida.
    - Similitud ≥ 0,85 en el mismo álbum, con la misma duración (±3 s) y sin números de pista distintos: se fusionan (`TrackMerger`, que fija los archivos con `TrackLocked`).
    - Similitud ≥ 0,85 en otros álbumes: comparten `Recording`, salvo que tengan MBIDs distintos.
    - Similitud ≥ 0,7: van a `DuplicateCandidate` para el admin (página Duplicados). Los descartados no se vuelven a proponer.
    - Las señales sintéticas de los tests se parecen entre sí más que la música real; los umbrales se eligieron con eso en cuenta.
- **ffmpeg (implementado):** `Media/`. `FfmpegLocator` busca, por orden:
  1. `Arcanist:Ffmpeg:Path`.
  2. El ajuste `media.ffmpegPath` (del asistente o del panel; solo se lee con el setup completado).
  3. `<datos>/tools/ffmpeg`.
  4. Los paquetes de Synology.
  5. El `PATH` (los dos últimos, desactivables con `Arcanist:Ffmpeg:SearchSystem`).

  `FfmpegDownloader` descarga la build GPL estática de BtbN (zip en Windows, tar.xz en Linux, x64/arm64) y la verifica contra su SHA-256. Fijar la ruta o descargar exige admin con step-up: el servidor ejecutará ese archivo.
- **Servicios en línea (implementado):** `Online/` y `Application/Online/`. `EnrichmentWorker` ejecuta un bucle por servicio a `Arcanist:Online:RequestInterval` (1 s):
  - **AcoustID:** identifica grabaciones sin MBID.
  - **MusicBrainz:** identifica álbumes por título, artista y número de pistas.
  - **Cover Art Archive:** añade la carátula a los álbumes identificados que no tienen.
  - **LRCLib:** busca letras para las pistas que no las tienen.

  `OnlineLookups` evita repetir consultas: lo encontrado no se repite; lo no encontrado se reintenta a los 90 días; los fallos esperan más con cada intento. Los problemas de conexión pausan el servicio sin marcar nada. Todo está desactivado salvo lo que el admin active (`online.services`, asistente y Ajustes). La clave de AcoustID es la del admin (`online.acoustIdKey`) o la del proyecto.
- **Organización (implementado):** `Organization/` y `Application/Organization/`.
  - **Plantillas** (`PathTemplate`): `{albumartist}/<{year} - >{album}/<{disc}-><{track} >{title}` por defecto. El texto entre `<` y `>` desaparece si falta un dato dentro (no pueden aparecer en nombres de Windows, así que nunca chocan con texto literal). `{disc}` solo sale en álbumes de varios discos; `{track}` lleva dos cifras. La última parte debe tener `{title}`. Los nombres se limpian con las reglas de Windows (las más estrictas), sin punto inicial (ocultaría el archivo), sin nombres reservados y cortados a 150 caracteres / 230 bytes UTF-8. La plantilla vive en `Library.OrganizeTemplate` (null = la de por defecto).
  - **`LibraryOrganizer`:** planifica (`PlanAsync`) y mueve. Las letras `.lrc` y la `.cue` de un álbum en un archivo siguen a su audio; los demás archivos de una carpeta (carátulas, libretos, logs, subcarpetas sin audio) la siguen si todo su audio va a una misma carpeta (`Companions`). Un archivo partido por CUE conserva su nombre (la CUE lo nombra). Los choques reciben " (2)", y organizar dos veces no mueve nada. Cada movimiento se guarda en `OrganizeMove` al hacerse y la BD se actualiza en el acto (`MediaFile.RelativePath`), sin reescanear; deshacer recorre los movimientos al revés y salta los que ya no cuadran.
  - **Ejecución:** "Organizar ahora" y deshacer exigen step-up y van a `OrganizeQueue`/`OrganizeWorker` en segundo plano; un reinicio a mitad deja la ejecución `FAILED/interrupted`, que se puede deshacer. Con `Library.AutoOrganize`, `ScanWorker` organiza tras cada escaneo **solo los archivos añadidos desde `AutoOrganizeSince`** que lleven `StableSeconds` sin cambiar; los anteriores solo se mueven a petición, tras ver la vista previa. `LibraryWatcher` vigila esas bibliotecas y las escanea cuando sus archivos dejan de cambiar.
  - **`LibraryLocks`:** escaneo, organización e importación de una biblioteca nunca se solapan (el escaneo espera en cola). `IsWriting` permite al vigilante ignorar los cambios que hace el propio Arcanist.
  - **Carpetas de entrada** (`DropFolder`, solo admins: son rutas del servidor; no pueden estar dentro de una biblioteca ni de otra carpeta de entrada). `DropFolderWorker` las vigila y además las lista cada `PollSeconds` (60). Una carpeta de primer nivel se procesa cuando todos sus archivos llevan `StableSeconds` (30) sin cambiar. `DropFolderImporter`, con el candado de la biblioteca: lee etiquetas y hash; sin título, artista o álbum queda `NEEDS_REVIEW`; mismo audio en la biblioteca, `DUPLICATE`; misma canción del mismo álbum (MBID, o disco + número + título), según `DuplicatePolicy`: `Skip`, `KeepBest` (si es mejor: lossless > lossy; luego profundidad y frecuencia, o bitrate) o `AddVersion`. Convierte si se pidió (`ConvertTo`: los lossless siempre, los lossy solo con `ConvertLossy` y nunca a FLAC), copia etiquetas y carátula con ATL y deja el original en la papelera de la carpeta. Después escanea la biblioteca y espera a que termine, para que la siguiente pasada conozca lo importado.
  - **Cola por revisar** (`ImportItem`): aceptar (con etiquetas que se **escriben en el archivo**, la única vez que Arcanist escribe etiquetas, o "importar tal cual"/"añadir como copia"), reintentar, ignorar (hasta que el archivo cambie) o descartar (a la papelera). Lo importado se guarda como historial 30 días; `MaintainAsync` purga papeleras, historial y ejecuciones antiguas (20 por biblioteca) cada hora.
  - Tests: `Arcanist:Organization:StableSeconds=0` y `ArcanistFactory.WaitForImportsAsync()`/`WaitForOrganizeAsync()`. Con 0 s el importador puede leer un archivo mientras el test lo escribe: preparar los archivos fuera y moverlos dentro.
- **Datos de usuario (implementado):** `Application/Listening/`, `Application/Playlists/` y `Application/Discovery/`. Cuelgan de `Track` y `TrackMerger` los mueve a la pista que queda (valoraciones: la mayor; reproducciones: se suman).
  - **Valoraciones:** `TrackRating` de 1 a 5. El modo es del servidor (`RatingMode`, ajuste `ratings.mode`, elegido en el asistente y cambiable en Ajustes, `serverInfo.ratingMode`): `STARS` (de 1 a 5) o `STAR` (una estrella). Una estrella se guarda como 5, así que al cambiar de modo las estrellas pasan a 5 estrellas y viceversa; en modo `STAR` las valoraciones de 1 a 4 se conservan pero no se muestran ni se pueden poner. `RatingSettings` lo cachea para `serverInfo`.
  - **Reproducciones:** el reproductor informa con `recordPlay` (pista, ms escuchados, inicio). El servidor decide: cuenta si son ≥ 30 s y la mitad de la pista o 4 min; es un salto si se para antes de 30 s en una pista de más de un minuto. Acepta informes de hasta 14 días (reproducción sin conexión) e ignora los repetidos (misma pista ±2 s). `TrackPlayStats` lleva los totales por usuario y pista.
  - **Estadísticas** (`StatsService`): por semana, mes, año o siempre, con días y meses en la zona horaria que pida el cliente (IANA; los cálculos de días se hacen en memoria). Las de otros usuarios y las del servidor (`serverStats`, con totales de la biblioteca) exigen `VIEW_ALL_STATS`.
  - **Playlists:** de su dueño; públicas (todos las ven y copian) y con colaboradores (añaden, quitan y reordenan). Las entradas tienen posiciones contiguas; las pistas que un usuario no puede ver siguen en la playlist pero no se le muestran. Máximo 10.000 entradas. `shareableUsers` lista los usuarios para invitar.
  - **Descubrimiento:** `home` (secciones por `HomeSectionKind`; el cliente pone los títulos), `mixes`/`mix(id)` y `artistOverview`. Los mixes (`artist:`, `genre:`, `decade:`, `favorites`, `discovery`, `track:` como radio) se generan al pedirlos con una semilla por usuario, mix y día: son estables todo el día. Excluyen lo valorado con 1–2 y lo que el usuario salta, no repiten más de tres pistas de un álbum, y se ordenan por tempo y tonalidad compatible (rueda Camelot) cuando hay análisis. Los artistas relacionados salen de géneros y álbumes compartidos, sin servicios externos.
- **Hosting de frontends (implementado):** `Hosting/FrontendHost.cs` y `Hosting/FrontendCatalog.cs`.
  - `/admin/**` sirve `Arcanist:Frontends:AdminRoot` (por defecto `wwwroot/admin`, el build de Angular).
  - Los frontends instalados viven en `<datos>/frontends/<id>/` con su manifiesto `arcanist-frontend.json`; el empaquetado de serie (`PlayerRoot`, por defecto `wwwroot/player`) tiene el id `player`. El predeterminado (ajuste `frontends.default`) se sirve en `/`, y todos en `/players/<id>/`; a la página de entrada se le inyecta `<base href>` con su punto de montaje. Sin ninguno, `/` muestra una página provisional con enlace al admin y aviso AGPL.
  - Se instalan desde un zip o una URL (`Packages/PackageArchive.cs`: sin zip-slip, 250 MB comprimidos, 1 GB y 20.000 archivos), con aviso de confianza en el panel y step-up. Guía pública en `docs/developers/frontends.md`.
  - Los fallbacks devuelven el archivo si existe y, si no, `index.html` (rutas del cliente). Los recursos con hash en el nombre se marcan `immutable`.
  - Los prefijos de API y reservados (`/rest`, `/graphql`, `/api`, `/media`, `/players`, `/ext`…) responden 404 JSON y **nunca HTML**.
  - Fallbacks y catch-alls solo atienden GET/HEAD: un catch-all con todos los métodos le quitaba peticiones POST sin cuerpo a los endpoints reales.
  - No usar `UseStaticFiles` para los frontends: no actúa cuando el enrutado ya eligió un endpoint (el fallback).
- **Extensiones (implementado):** SDK en `src/Arcanist.Extensibility`; servidor en `Extensions/` y `Application/Extensions/`.
  - Una extensión implementa `IArcanistExtension` (`ConfigureServices` y `MapEndpoints`) y se describe en `arcanist-extension.json`. Las instaladas viven en `<datos>/extensions/<id>/` y se cargan al arrancar, si están activadas, en su propio `AssemblyLoadContext` (el SDK, el servidor y el framework se comparten; los ensamblados se leen a memoria).
  - Sus rutas deben estar bajo `/ext/<id>` o un prefijo reclamado; si no, la extensión queda `FAILED` (`route_not_allowed`). Dos extensiones activas no pueden reclamar el mismo prefijo (`route_conflict`).
  - Activación en `arcanist.json` (`Arcanist:Extensions:<id>:Enabled`): apagar se aplica al momento (un filtro responde 404); encender una instalada requiere reiniciar. Si la clave viene del entorno, el panel no la cambia (`set_by_environment`).
  - Las integradas (`BuiltInExtensions.All`, `IBuiltInExtension`) usan el mismo SDK, se cargan siempre y empiezan desactivadas. Las que verifican credenciales propias llaman a `ExtensionAuthentication.SignInAsync`, que deja la petición como ese usuario (sin sesión: nada que necesite step-up funciona ahí).
  - Guía pública en `docs/developers/extensions.md`; `tests/Arcanist.TestExtension` es la extensión de los tests.
- **Contraseñas de aplicación (implementado):** `Application/Users/AppPasswordService.cs`. Las apps de Subsonic y mStream nunca usan la contraseña del usuario: cada usuario crea una por app (con step-up), que se muestra una vez (`xxxx-xxxx-xxxx-xxxx`) y se guarda cifrada con Data Protection (para recalcular el token `md5(contraseña + sal)` de Subsonic) junto a su SHA-256 (para buscarla como clave de API). Los fallos se limitan con `LoginThrottle` (claves `app:`), sin afectar al inicio de sesión principal.
- **Subsonic (implementado):** `Extensions/Subsonic/`, extensión integrada `subsonic`.
  - API 1.16.1 con OpenSubsonic (`apiKeyAuthentication`, `formPost`, `songLyrics` v1, `transcodeOffset`, `topSongsByArtistId`) en `/rest/{método}[.view]`, GET y POST de formulario, en XML, JSON o JSONP. Los errores van con HTTP 200 y el código de Subsonic. CORS abierto.
  - `SubsonicNode` escribe el mismo árbol en XML o JSON con las reglas de Subsonic (atributos como propiedades, listas siempre como arrays). Es una capa sobre los servicios de `Application/`: la visibilidad es la de Arcanist.
  - Ids: los Guid como texto; las carpetas de música son `Library.Number`, un entero que nunca cambia ni se repite (`libraries.lastNumber` guarda el último usado).
  - Navegación por etiquetas y por carpetas simuladas (artista → álbum → canciones); listas y `search3` (vacía = todo) paginan en orden estable. El streaming reutiliza `Api/Rest/MediaResponses.cs`; sin ffmpeg envía el original.
  - Solo se pueden marcar pistas: estrellas y valoraciones de álbumes y artistas se aceptan pero no se guardan. Con el modo de una estrella, cualquier valoración la marca. `getPlayQueue`/`savePlayQueue`, podcasts, radios, compartidos y vídeos responden vacío.
- **mStream (implementado):** `Extensions/Mstream/`, extensión integrada `mstream`, escrita a partir de la descripción de su API (no de su código).
  - Login con contraseña de aplicación (JSON o formulario); el token (Data Protection) nombra esa contraseña y vale hasta que se borra. Se lee del cuerpo, de `?token=`, de `x-access-token` o de la cookie.
  - Cada carpeta de una biblioteca visible es un vpath (el nombre de la biblioteca, con `-2`, `-3`… para las demás carpetas); una pista de CUE es `<archivo>@<n>`. El explorador de archivos sale del catálogo, nunca del disco.
  - Rutas de `db/*`, `file-explorer`, `playlist/*` (por nombre; los ids de entrada salen del Guid), `/media`, `/album-art`, `/transcode`, `lyrics` y `scrobble-by-filepath`. Valoraciones de 0 a 10 (el doble de las estrellas). Sin enlaces compartidos, subidas ni zips.
- **Importador de mStream (implementado):** `Import/Mstream/` y `Application/Import/`.
  - Lee una instalación local (`MstreamFolder`: mStream 5 con usuarios y carpetas en la configuración y datos en LokiJS; mStream 6 todo en SQLite, leído de una copia) o un servidor por su API con una cuenta de administrador (`MstreamServerClient`).
  - `analyzeMstream*` muestra lo que hay; `importMstream` (admin con step-up) crea las bibliotecas elegidas y los usuarios (admins siguen admins; los demás reciben `DOWNLOAD` y las bibliotecas de sus vpaths) y guarda un `MstreamImport`.
  - Contraseñas: desde carpeta, `PasswordHash = mstream$<sal>$<hash>` y `ArcanistPasswordHasher` lo verifica (PBKDF2-SHA512, 15000, 32 bytes, la sal base64 como texto) y pide rehash; desde servidor, `mstream-server$<import>` y `MstreamServerSignIn` (`IExternalSignIn` en `AuthService`) consulta a ese servidor en el primer login y trae entonces los datos del usuario.
  - `MstreamImportWorker` aplica los datos de una importación desde carpeta cuando terminan los escaneos de sus bibliotecas. `MstreamImporter` casa los archivos por ruta (exacta, o sin mayúsculas ni forma Unicode) y es idempotente: no duplica playlists por nombre, las valoraciones solo rellenan huecos y las reproducciones se quedan con el máximo (en `TrackPlayStats`, sin historial).
- **Permisos:** `Role` (Admin/User) + flags de permisos + `LibraryAccess(UserId, LibraryId)` por usuario y biblioteca. Los permisos se leen de la BD, no del JWT.
- **Capacidades:** `ICapabilityService`. Lo local siempre está activo. `UltimateCloudSource` requiere una cuenta gratuita; `CloudSync`, `ThirdPartyStorage` y `CloudConnect` requieren tier de pago.
- **Setup (implementado):** `Setup/` y `Api/Rest/SetupEndpoints.cs`.
  - Mientras `Arcanist:Setup:Completed` no sea `true`, `SetupGateMiddleware` redirige `/` a `/admin/setup` y responde 503 `setup_required` a todo lo que no sea el asistente, `/admin`, `/health` o la documentación.
  - El asistente envía todo de una vez a `POST /rest/v1/setup/complete`, que valida, guarda la BD en `arcanist.json` (salvo que venga fijada por el entorno), migra, crea el admin, las bibliotecas y los ajustes, y marca el setup como completado.
  - Desde fuera de loopback (o a través de un proxy) se exige la cabecera `X-Setup-Code`.
  - `ISettingsFileWriter` escribe `arcanist.json` y recarga la configuración en el acto.
- **Autenticación (implementada):** `Auth/` y `Api/Rest/AuthEndpoints.cs`.
  - `POST /rest/v1/auth/{login,refresh,logout,reauthenticate}`.
  - JWT HS256 de 15 min, firmado con la clave de `<datos>/keys/`. Claims: `sub`, `sid` (sesión), `sstamp`, `auth_time`.
  - Token de refresco opaco y rotatorio, guardado como hash en `UserSession`; la rotación es atómica (`ExecuteUpdate` condicionado al hash anterior). Con `useCookie` va en la cookie HttpOnly `arcanist_refresh`, limitada a `/rest/v1/auth`.
  - Cada petición valida usuario, `SecurityStamp` y sesión contra `UserAccessCache` (30 s). **Al cambiar permisos, acceso a bibliotecas, contraseña o estado de un usuario, llamar a `UserAccessCache.InvalidateUser/InvalidateSession/InvalidateAll`.**
  - El usuario actual se obtiene con `ICurrentUser.Access` (`UserAccess`: `Has(permiso)`, `CanAccessLibrary(id)`).
  - Políticas en `Auth/Policies`: `Policies.Permission(Permissions.X)`, `Policies.Admin` y `Policies.RecentAuthentication`. Esta última es el step-up y falla con 403 `reauthentication_required`.
  - Protección del login: rate limit `auth` por IP (también en el setup) y bloqueo exponencial por cuenta en `LoginThrottle`.
  - `TimeProvider` inyectado en todo lo que depende de la hora; los tests usan `FakeTimeProvider` a través de `new ArcanistFactory(time: …)`.
- **Errores de API como códigos:** `Api/Problems.cs`. REST devuelve problem details con `code`, y los errores de validación son `{ campo: [código] }`. La UI traduce los códigos; no mostrar textos del servidor.
- **Seguridad:**
  - Se asume que el puerto puede estar abierto a internet: rate limiting en auth y setup, y `ForwardedHeaders` para proxies inversos. Se confía en loopback más lo que se liste en `Arcanist:Network:TrustedProxies`.
  - El código de setup se exige fuera de loopback.
  - Los frontends y extensiones de terceros tienen confianza total: se avisa al instalarlos y se pide reautenticación para las acciones sensibles.

- **Windows (implementado):** `src/Arcanist.Tray`.
  - Velopack instala por usuario en `%LocalAppData%\Arcanist`, sin elevación. El tray arranca con Windows (clave `Run` de HKCU) y ejecuta el servidor de `current\server\` como proceso hijo. Si se cae, lo reinicia con espera creciente; tras 3 caídas seguidas se rinde.
  - Actualiza desde GitHub Releases (`UpdateManager` + `GithubSource`). Antes de aplicar una actualización detiene el servidor.
  - Los datos están en `%ProgramData%\Arcanist`, compartidos por los dos modos.
  - **Modo servicio opcional:** `ServiceHelper` se ejecuta elevado (`Arcanist.Tray.exe --service-helper install|update|uninstall|start|stop`) y deja un registro en `logs/service-helper.log`. Al instalar:
    - copia el servidor a `%ProgramFiles%\Arcanist\server`, nunca a una carpeta que el usuario pueda modificar;
    - lo registra con `sc.exe` como `NT SERVICE\Arcanist` (cuenta virtual) con arranque retrasado y reinicio ante fallos;
    - da permiso de escritura en los datos y de lectura en las carpetas de música elegidas;
    - deja al usuario iniciar y detener el servicio sin elevación (ACE en el SDDL);
    - abre el puerto en el firewall solo para redes privadas y de dominio.
  - El tray detecta el servicio y lo controla. Tras una actualización aplicada en modo servicio, se reinicia con `--update-service` y actualiza también el servicio (con UAC).
  - Para música en un NAS se recomienda el modo app o cambiar la cuenta del servicio en `services.msc`.
- **Synology:** DSM 7.2+, x64 y ARMv8. Usa `SERVICE_USER=auto` (`sc-arcanist`), los datos van en `SYNOPKG_PKGVAR` y la carpeta compartida de música sale del asistente (`SERVICE_WIZARD_SHARENAME`). El asistente genera y muestra el código de setup, que `service_postinst` escribe en `setup-code.txt`.

## Roadmap

- **F1:** fundaciones y biblioteca (completada).
- **F2:** distribución (completada): instalador Velopack con tray en Avalonia y servicio opcional, tarballs y systemd, Docker multi-arch en GHCR, paquete Synology y workflows de CI y release.
- **F3:** escáner avanzado y ffmpeg (completada): crate de análisis en Rust, transcodificación, waveform, BPM y tonalidad, EBU R128, duplicados por huella, AcoustID/MusicBrainz/Cover Art Archive/LRCLib opcionales, letras y hojas CUE. Las carátulas solo vienen del Cover Art Archive: iTunes y Deezer se descartaron por sus términos de uso.
- **F4:** carpetas de entrada y organización (completada): plantillas de rutas, organizar con vista previa y deshacer, organización automática de lo nuevo, carpetas de entrada con política de duplicados, conversión opcional y cola de revisión en el panel. Pendiente: sugerencias de AcoustID para lo que no se puede identificar.
- **F5:** experiencia de usuario (completada): valoraciones con modo de servidor (cinco estrellas o una), reproducciones informadas por los reproductores con historial y estadísticas (también del servidor en el panel), playlists públicas y colaborativas, búsqueda de texto completo por proveedor, e inicio, mixes y páginas de artista como API para los reproductores.
- **F6:** ecosistema (completada): reproductores web instalables desde zip o URL, SDK de extensiones con carga aislada, contraseñas de aplicación, Subsonic/OpenSubsonic y mStream como extensiones integradas, e importador de mStream desde su carpeta o su servidor (también desde el asistente).
- **F7:** DLNA.
- **F8:** red P2P iroh (crate Rust `arcanist-p2p` con C ABI para .NET y uniffi para móviles) y su sección en el admin.
- **F9:** migrador SQLite → PostgreSQL con UI.
- **F10:** integración con Ultimate Cloud.

## Decisiones abiertas

Aún sin decidir. No asumir una opción sin consultar al usuario, y documentar aquí la elección cuando se tome:

- Firma de código del instalador de Windows (Authenticode). Sin firma, SmartScreen avisa al instalar; `vpk pack` acepta los parámetros de firma cuando haya certificado.

Decididas en la F3:
- **Análisis de audio en una crate Rust** (`native/arcanist-audio`): Symphonia (MPL-2.0) decodifica; `ebur128`, `rusty-chromaprint` y `rustfft` calculan loudness, huella y BPM/tonalidad. Se expone con una C ABI a .NET. ffmpeg solo hace falta para los formatos que Symphonia no lee (Opus, WMA, APE, DSD…) y para transcodificar.
- **ffmpeg no se empaqueta:** se detecta en el sistema o en la ruta configurada (también desde el asistente), y el panel ofrece descargarlo si falta.
- **Servicios en línea opcionales** (MusicBrainz, AcoustID, Cover Art Archive, LRCLib): el asistente pregunta por cada uno y en instalaciones existentes empiezan desactivados. Funcionan en segundo plano a 1 petición/s, con su estado visible en el panel. Solo se envían huellas, MBIDs y artista/título; nunca rutas ni usuarios.
- **AcoustID usa una clave de aplicación del proyecto** (también obligatoria para consultar, no solo para enviar), que el admin puede sustituir por la suya. Mientras el proyecto no la registre, AcoustID queda desactivado.

Decididas en la F6 (con el usuario):
- **Extensiones oficiales integradas y de terceros desde zip**, con aviso de confianza total, step-up y reinicio.
- **Subsonic y mStream con contraseñas de aplicación**, nunca con la contraseña principal.
- **Importador de mStream desde su carpeta y desde su servidor.**
- **Reproductores web desde zip o URL**, con validación del manifiesto, aviso de confianza y step-up.
- Sin consultar (revisable): los tokens de mStream no se migran (no caducan y su secreto es un texto plano); las apps vuelven a iniciar sesión con una contraseña de aplicación.

Decididas en la F5 (con el usuario):
- **Valoraciones según el modo del servidor**, elegido en el asistente y cambiable en Ajustes: de una a cinco estrellas, o una estrella. Una estrella equivale a cinco estrellas en ambos sentidos.
- **Playlists privadas, públicas y colaborativas.**
- **Búsqueda con el índice de texto completo de cada proveedor** (FTS5 y `tsvector`), como excepción documentada a la regla de no usar SQL específico.
- **Inicio, mixes y páginas de artista son API para los reproductores**; el panel solo muestra las estadísticas del servidor.

Decididas en la F4 (sin consultar, por ser las opciones conservadoras; revisables):
- **Organizar nunca es automático sobre lo que ya existe:** activar la organización automática solo afecta a lo añadido después; el resto requiere "Organizar ahora" tras la vista previa, con step-up y registro para deshacer.
- **Nada se borra:** lo que sustituye `KeepBest`, los originales convertidos y lo descartado van a la papelera de su ubicación durante 30 días.
- **Las etiquetas solo se escriben cuando un admin las completa en la cola de revisión**, en el archivo de la carpeta de entrada antes de importarlo. Los archivos de las bibliotecas nunca se re-etiquetan.
- La política de duplicados por defecto es omitir.

Decididas antes: el instalador es Velopack. El repositorio es `github.com/linkfast/arcanist`: es el `RepositoryUrl` de `Directory.Build.props` y el valor por defecto de `serverInfo.sourceUrl`.
