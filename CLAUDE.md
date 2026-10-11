# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Fase 1 en curso: implementados setup, autenticación, usuarios, bibliotecas, escáner y las dos APIs (GraphQL y REST). Faltan progreso en vivo por suscripción, escaneo programado, streaming, hosting de frontends y el panel Angular (que sigue siendo una página de prueba; `GET /` aún responde `"Hello World!"`). La sección "Arquitectura decidida" describe el diseño aprobado: comprobar en el código qué existe antes de asumirlo. Mantener este archivo alineado con el código conforme avance.

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
  - `/graphql`: la API principal (HTTP y WebSocket). `/graphql/ui` (GraphiQL) y `/graphql/schema.graphql` (SDL).
  - `/rest/v1/**`: la API OpenAPI, equivalente a GraphQL salvo las suscripciones. Contrato en `/rest/v1/openapi.json` y documentación Scalar en `/rest/docs`.
  - `/admin/**`: este panel, compilado con `--base-href /admin/`.
  - `/health`.
  - `/`: el frontend de reproducción activo (Ultimate Web Player por defecto, o uno de terceros instalado desde el admin).
  - `/players/{id}/**`: los demás frontends instalados.
  - `/ext/{id}/**`: extensiones.
  - **Rutas reservadas:**
    - Para la extensión de compatibilidad con mStream: `/api/**`, `/media/**`, `/album-art/**`, `/transcode/**` y `/shared/**`.
    - Para la extensión Subsonic: `/rest/{método}[.view]`.
- **Una lógica, dos APIs (implementado):**
  - Los resolvers GraphQL (`Api/GraphQL/`) y los endpoints Minimal API (`Api/Rest/`) son capas finas sobre los servicios de `Application/`.
  - La autorización vive **solo** en esos servicios: cada operación empieza con `AccessGuard` (`Authenticated()`, `Permission(...)`, `Admin()`, `RecentAuthentication()`), y lo mismo aplicará al filtrado por biblioteca.
  - Los fallos esperados se lanzan como `ArcanistException` (`Application/Errors.cs`). REST los convierte en problem details con `code`; GraphQL, en errores con `extensions.code` (y `extensions.errors` para validación).
  - **GraphQL usa GraphQL.NET** (MIT) con esquema "type-first": clases parciales `Query` y `Mutation` con métodos **estáticos**, `[Name("campo")]`, `[Description]`, `[Id]` para los Guid y `[FromServices]` para los servicios. Cada funcionalidad añade sus operaciones en su propio archivo parcial.
  - **Hot Chocolate quedó descartado:** todas sus versiones cargan en tiempo de ejecución el IDE Nitro (ChilliCream License 1.0, incompatible con AGPL). No reintroducirlo.
  - **Convención de nombres:** el `operationId` REST (`.WithName(...)`) es el campo GraphQL en PascalCase (`createUser` ↔ `CreateUser`). `ApiContractTests` falla si una operación GraphQL no tiene su equivalente REST. Login y setup existen solo en REST.
  - Los enums se serializan en UPPER_SNAKE_CASE en ambas APIs (`MANAGE_USERS`). Los tipos de contrato no se llaman igual que las entidades (`UserAccount`, no `User`).
  - Contratos versionados en `docs/api/schema.graphql` y `docs/api/openapi.json`. `ApiContractTests` falla si difieren de lo que sirve el servidor. Tras un cambio intencionado de API, regenerarlos y revisar el diff:

    ```bash
    ARCANIST_UPDATE_CONTRACTS=1 dotnet test --project tests/Arcanist.Core.Tests -- --filter-class "*ApiContractTests"
    ```

  - Herramientas: GraphiQL en `/graphql/ui` (cargado de jsDelivr con SRI), SDL en `/graphql/schema.graphql`, Scalar en `/rest/docs` (embebido, funciona sin internet).
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
- **Almacenamiento (implementado):** `Library` → varias `LibrarySource`, siempre leídas a través de `ILibraryStorage` (`Storage/`), que se obtiene por tipo con `ILibraryStorageResolver`.
  - Nunca usar `System.IO` sobre archivos de bibliotecas fuera de `LocalStorage`. La única excepción es `DirectoryBrowser`, que lista carpetas del servidor para elegir ubicaciones.
  - `LocalStorage` ignora archivos y carpetas ocultos (`.`) y carpetas de sistema de NAS y SO (`@eaDir`, `#recycle`, `$RECYCLE.BIN`…). También rechaza rutas relativas que escapen de la raíz.
  - **Las rutas relativas se guardan tal cual están en disco, sin normalizar Unicode:** en Linux, un nombre en NFD (copiado desde un Mac) solo se abre con esa grafía exacta.
  - Las ubicaciones se validan con `LibraryRules` (compartido con el setup): deben existir y ser legibles, y no pueden repetirse ni anidarse dentro de la misma biblioteca (código `overlapping`). Entre bibliotecas distintas sí pueden coincidir.
- **Bibliotecas (implementado):** `Application/Libraries/`.
  - Cada usuario ve las bibliotecas concedidas más las `VisibleToAll`; las demás responden 404, para no revelar que existen.
  - `sources` (las rutas) solo se devuelve a quien tiene `MANAGE_LIBRARIES`.
  - Un gestor que no es admin solo gestiona las bibliotecas que ve, y recibe acceso automático a las que crea.
  - Borrar una biblioteca exige step-up y **nunca toca los archivos**.
  - `serverDirectories` es solo para admins.
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
  - **Limitación conocida:** un álbum sin etiqueta AlbumArtist ni marca de recopilatorio, con artistas distintos por pista, se parte en varios álbumes. Se mejorará en la F3.
  - Tests: `AudioFixtures` genera WAV etiquetados al vuelo, sin binarios en el repo. `ScannerTests` espera a los trabajos sondeando `GET /rest/v1/scans/{id}`.
- **Catálogo (implementado):** `Application/Catalog/`. Operaciones `artists`, `artist`, `albums`, `album`, `tracks` y `track`.
  - **Visibilidad:** una pista es visible si tiene algún archivo presente (`MissingSince == null`) en una biblioteca accesible. Álbumes y artistas son visibles a través de esas pistas. Lo invisible responde 404.
  - `Track.files` lista solo las copias accesibles, de mejor a peor calidad.
  - **Paginación keyset** (`Keyset`) por clave normalizada + id, con cursor opaco (`after`/`nextCursor`) y `totalCount`. Probada en ambos proveedores. `tracks(albumId)` devuelve el álbum entero en orden de disco y pista, sin paginar.
  - La búsqueda usa `Contains` sobre las columnas `Normalized*`. `TextNormalizer` quita acentos y pliega letras como æ/ø/ß/ł ("agaetis" encuentra "Ágætis"). **Cambiar sus reglas tras publicar exige una migración que recalcule los valores guardados.**
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
- URL pública del repositorio, para `serverInfo.sourceUrl` (configurable en `Arcanist:SourceUrl`; hoy es `null`).
