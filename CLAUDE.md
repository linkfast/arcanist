# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado del repositorio

Solo existe el scaffolding inicial (sin lógica de negocio ni GraphQL todavía). Git en la rama `main`, sin remoto configurado. Hay dos `.gitignore`: el de la raíz (generado con `dotnet new gitignore`, cubre `bin/`, `obj/`, etc.) y el de `src/Arcanist.Web/` (generado por Angular, cubre `node_modules`, `dist`, `.angular/cache`). Lo descrito en "Qué es Arcanist" y "Requisitos de producto" proviene de la descripción del usuario, no de código. Mantener este archivo alineado con el código conforme se implemente.

## Estructura y comandos

```
Arcanist.slnx                 solución .NET (formato .slnx, por defecto en .NET 10)
global.json                   SDK 10.0.100, rollForward latestFeature (la máquina tiene SDKs 8, 9 y 10)
mise.toml                     fija Node 24.15.0 para todo el repo
src/Arcanist.Core/            núcleo: proyecto ASP.NET Core vacío (`dotnet new web`), net10.0
src/Arcanist.Web/             frontend Angular 22 + Tailwind v4 + spartan-ng (paquete npm: `arcanist-web`)
```

`Arcanist.Web` **no está en la solución `.slnx`** (Angular no es un proyecto MSBuild); se construye y prueba por separado con npm.

**Web y Core todavía no están conectados.** `Program.cs` solo mapea `GET /` → `"Hello World!"`; no hay `proxy.conf.json` en Angular, ni CORS en Core, ni Core sirve el build de Angular. Al implementar el primer endpoint GraphQL hay que decidir y documentar aquí cómo se comunican en desarrollo (p. ej. proxy de `ng serve` hacia Core) y en producción (p. ej. Core sirviendo los estáticos de `dist/`).

**Core (.NET)** — desde la raíz:

```bash
dotnet build Arcanist.slnx
dotnet run --project src/Arcanist.Core          # http://localhost:5263 / https://localhost:7137
```

Aún no hay proyecto de tests .NET; al crearlo, añadirlo a la solución y documentar aquí cómo ejecutar un test individual.

**Web (Angular)** — desde `src/Arcanist.Web`:

```bash
npm start                        # ng serve
npm run build
npm test                         # ng test (Vitest + jsdom); usar `npx ng test --watch=false` para una sola pasada
npx ng test --watch=false --include="src/app/app.spec.ts"   # un solo archivo de test
```

**Node:** Angular 22 exige Node ≥ 22.22.3 o ≥ 24.15. La versión está fijada en `mise.toml` (raíz). El `node` que haya en el `PATH` de la shell puede ser otro más viejo (p. ej. uno de fnm), así que ejecutar npm a través de mise:

```bash
mise exec -- npm run build
mise exec -- npx ng test --watch=false
```

mise solo lee `mise.toml` después de un `mise trust` (una vez por clon); si falla con "not trusted", pedírselo al usuario en lugar de ejecutarlo.

### spartan-ng

- Los componentes se **copian al repo** (no se importan de un paquete): viven en `src/Arcanist.Web/libs/ui/<componente>` y se importan por alias `@spartan-ng/helm/<componente>` (paths en `tsconfig.json`). Son código propio y se pueden editar. La lógica headless viene de `@spartan-ng/brain`.
- Configuración del generador en `src/Arcanist.Web/components.json` (`libs/ui`, estilo `nova`, alias `@spartan-ng/helm`). Tema de colores `neutral` en `src/styles.css`.
- Añadir un componente: `npx ng g @spartan-ng/cli:ui <nombre>`. Sin `components.json` el generador lanza prompts interactivos que se cuelgan en terminales no interactivas; con el archivo presente funciona con `--defaults --no-interactive`.
- Tailwind v4 se configura vía `.postcssrc.json` (`@tailwindcss/postcss`), no hay `tailwind.config.js`.
- `npm audit` reporta 7 vulnerabilidades altas, todas en el tooling de desarrollo del CLI de spartan (`nx` → `smol-toml`); con `--omit=dev` son 0.

## Qué es Arcanist

Aplicación de archivo y organización de audio digital (biblioteca de música) similar a [mStream](https://github.com/IrosTheBeggar/mStream), pero pensada como producto **freemium, de código cerrado y no 100% gratuito**. Forma parte de la familia LinkFast "Ultimate" y es complementaria a **Ultimate Cloud** y **Ultimate Desktop**.

Caso de uso: el usuario construye su biblioteca de música digital en su **red LAN** y la consume desde todos los dispositivos del hogar (TV, tablet, PC, móvil, etc.).

## Stack decidido

- **Backend:** .NET Core 10 (C#).
- **API:** GraphQL (no REST como API principal).
- **Frontend web:** Angular con [spartan-ng](https://www.spartan.ng) como framework de UI.

## Requisitos de producto que condicionan la arquitectura

- **Multiusuario:** gestión de usuarios con opciones/preferencias propias por usuario, más una cuenta principal de **administrador**. La autorización (admin vs. usuario) debe atravesar toda la API GraphQL desde el inicio.
- **Modelo freemium:** habrá funcionalidades gratuitas y de pago; diseñar el código para que las capacidades se puedan habilitar/limitar por licencia o plan en lugar de asumir que todo está disponible.
- **Clientes heterogéneos en LAN:** el frontend debe funcionar en pantallas muy distintas (TV con control remoto/navegación por foco, tablet, móvil, escritorio). El servidor debe ser accesible dentro de la red local del usuario.

## Notas de licencia

mStream es open source con licencia **GPL-3.0** (copyleft). Se usa solo como referencia funcional: **no copiar ni portar código de mStream** a Arcanist, ya que el producto es de código cerrado.

## Decisiones abiertas

Aún sin decidir; no asumir una opción sin consultar al usuario, y documentar aquí la elección cuando se tome:

- Librería GraphQL para .NET (p. ej. Hot Chocolate).
- Base de datos / persistencia del catálogo y de los usuarios.
- Mecanismo de autenticación (cuentas locales en LAN, relación con cuentas de Ultimate Cloud).
- Conexión Web ↔ Core en desarrollo y producción (ver "Estructura y comandos").
