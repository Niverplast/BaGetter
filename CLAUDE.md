# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is BaGetter

BaGetter is a lightweight, open-source NuGet and symbol server (ASP.NET Core, .NET 10). Community fork of BaGet. Implements NuGet v3 protocol with pluggable database, storage, and search backends.

## Build & Test Commands

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
dotnet run --project src/BaGetter        # runs on http://localhost:5000
```

Run a single test class or method:
```bash
dotnet test --filter "FullyQualifiedName~UserServiceTests"
dotnet test --filter "FullyQualifiedName~UserServiceTests.TheGetOrCreateEntraUserAsyncMethod"
```

EF Core migrations (example for SQLite):
```bash
dotnet ef migrations add MigrationName --project src/BaGetter.Database.Sqlite --startup-project src/BaGetter
```

Requires .NET SDK 10.0.104 (pinned in `global.json`).

## Architecture

### Provider Pattern

Storage, database, and search services use a configuration-driven provider pattern (`IProvider<T>`). Multiple implementations coexist in DI (registered via `TryAddTransient`); the active one is selected by `appsettings.json` config at runtime. This is the core extensibility mechanism.

### Project Layout

- **`src/BaGetter/`** — ASP.NET Core host. Entry point (`Program.cs`), DI/middleware setup (`Startup.cs`), config.
- **`src/BaGetter.Core/`** — Business logic, EF Core entities, authentication services, configuration options, storage/search interfaces. Database-agnostic.
- **`src/BaGetter.Web/`** — HTTP controllers, Razor pages, endpoint routing (`BaGetterEndpointBuilder.cs`), health checks.
- **`src/BaGetter.Protocol/`** — NuGet v3 protocol client and models for upstream feed communication.
- **`src/BaGetter.Database.{Sqlite,SqlServer,PostgreSql,MySql}/`** — EF Core context + migrations per database provider.
- **`src/BaGetter.{Aws,Azure,Gcp,Aliyun,Tencent}/`** — Cloud storage provider implementations.

### Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IPackageDatabase` | Package CRUD operations |
| `IStorageService` | Raw blob storage |
| `IPackageStorageService` | Package-specific storage (nupkg, nuspec, readme, icon) |
| `ISearchService` / `ISearchIndexer` | Package search and indexing |
| `IContext` | EF Core DbContext abstraction |

### Authentication

Supports multiple simultaneous auth modes controlled by `AuthenticationMode` enum (None, Local, Entra, Hybrid). Auth services live in `src/BaGetter.Core/Authentication/`.

- **NuGetBasicAuth** is the default scheme; it forwards to the cookie scheme when a session cookie is present without an `Authorization` header (browser vs. client tool detection).
- **Entra ID** uses `AddMicrosoftIdentityWebApp()` (authorization code flow). `EntraRoleSyncService` syncs Entra app roles to local groups on token validation. Cookie name is `BaGetter.Auth`, 60-minute sliding session.
- **`IFeedAuthenticationService`** provides two auth paths: `AuthenticateByTokenAsync()` (PAT) and `AuthenticateByCredentialsAsync()` (username/password).
- **`FeedPermissionHandler`** (AuthorizationHandler) enforces feed-level permissions using the default feed ID `"default"`.
- Passwords use bcrypt; tokens store prefix + hash.

Authentication config structure:
```json
"Authentication": {
  "Mode": "Hybrid",
  "Entra": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": "",
    "CallbackPath": "/signin-oidc"
  },
  "MaxTokenExpiryDays": 365,
  "MaxFailedAttempts": 5,
  "LockoutMinutes": 15
}
```

### Data Model

Defined in `src/BaGetter.Core/Entities/AbstractContext.cs`. Core entities: Package, PackageDependency, PackageType, TargetFramework, User, PersonalAccessToken, Group, UserGroup, FeedPermission.

### API Endpoints

Routes defined in `BaGetterEndpointBuilder.cs`:
- Service index: `GET /v3/index.json`
- Search/autocomplete: `GET /v3/search`, `GET /v3/autocomplete`
- Package metadata: `GET /v3/registration/{id}/index.json`
- Package content: `GET /v3/package/{id}/{version}/{id}.nupkg`
- Publish: `PUT /api/v2/package`
- Symbols: `PUT /api/v2/symbol`, `GET /api/download/symbols/...`

### Middleware

- **`OperationCancelledMiddleware`** converts `OperationCanceledException` to HTTP 409 Conflict.
- **`ConfigureBaGetterServer`** (implements multiple `IConfigureOptions<T>`) configures CORS, `FormOptions` (multipart upload limits via `MaxPackageSizeGiB`), forwarded headers, and IIS options.

## Configuration

Options class: `BaGetterOptions` in `src/BaGetter.Core/Configuration/`. Config sources (in order): `appsettings.json`, env vars, user secrets, Docker secrets (`/run/secrets/`), optional `BAGET_CONFIG_ROOT` env var.

`ValidateBaGetterOptions` validates all config at startup — database/storage/search types against whitelists, Entra config completeness when required, and numeric minimums (token expiry, failed attempts, lockout).

## Code Style

Enforced via `.editorconfig`: 4-space indent (2-space for JSON), PascalCase for public APIs/constants, `_camelCase` for private fields, `var` preferred, System usings first, all usings outside namespace.

Additional enforced rules (warning severity):
- File-scoped namespaces (`namespace Foo;`)
- Accessibility modifiers always required
- Readonly fields where possible
- No `this.` qualification
- Predefined types (`int`, `string`) over framework types (`Int32`, `String`)
- No primary constructors (`csharp_style_prefer_primary_constructors = false`)
- No expression-bodied methods or constructors (properties/accessors OK)

Suppress CS1591 for non-public XML doc comments.

## Testing

xUnit + Moq. Test projects mirror source: `tests/BaGetter.Core.Tests/`, `tests/BaGetter.Web.Tests/`, `tests/BaGetter.Protocol.Tests/`. Integration tests use in-memory SQLite.

**`BaGetterApplication`** (WebApplicationFactory) is the integration test host — it mocks `SystemTime` to 2020-01-01, creates temp-dir SQLite databases, and wires up `XunitLoggerProvider`. Use `BaGetWebApplicationFactoryExtensions` helpers (`AddPackageAsync`, `AddSymbolPackageAsync`) to seed test data. Override config via in-memory dictionary config sources in test setup.

## Centralized Package Versions

All NuGet package versions managed in `Directory.Packages.props` at repo root. Do not specify versions in individual `.csproj` files.
