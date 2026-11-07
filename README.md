# MuscleForge Backend

ASP.NET Core API for MuscleForge, a personal workout tracker. Companion repository: [muscleforge-frontend](https://github.com/JaydenWev/muscleforge-frontend).

## Current increment

The initial foundation contains independent API, Core, and Infrastructure projects; a PostgreSQL/EF Core context prepared for Identity; an exercise domain model; health endpoints; a versioned status contract; tests; and container/CI configuration.

This is the first part of milestone 1. Identity and personal-exercise schema migrations, configured owner provisioning, an account maintenance command, and bearer login/refresh/current-account endpoints are included. Exercise endpoints are next. There is no account registration or workout data endpoint yet. Readiness requires connectivity and the exact migration history expected by this API build; a reachable but unmigrated, outdated, or unrecognized schema is not ready.

See [authentication setup and contract](docs/authentication.md) for token behavior and one-time certificate setup. Configure `DATA_PROTECTION_CERTIFICATE_PASSWORD` and create the PFX before starting Compose; the key volume and certificate must be preserved across restarts.

See the [architecture baseline](docs/architecture.md), [toolchain](docs/toolchain.md), and [foundation decision](docs/decisions/0001-initial-foundation.md).

The common [API error and validation contract](docs/api-errors.md) defines problem responses, validation helpers, resource limits and safe request-completion logs. Request bodies are capped at 256 KiB after decoding; Kestrel separately allows up to 2 MiB including chunk framing. Login and resource-ownership enforcement remain with their feature endpoints.

## Requirements

- [.NET SDK 10.0.403](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), pinned by `global.json`.
- Docker with Compose v2 for local PostgreSQL/container development.
- Python 3.11+ for contract export; CI uses 3.12.

## Run the local containers

```bash
cp .env.example .env
# Set POSTGRES_PASSWORD, OWNER_EMAIL, OWNER_PASSWORD and DATA_PROTECTION_CERTIFICATE_PASSWORD in .env.
# Create the PFX as described in docs/authentication.md before starting the API.
docker compose up -d --wait database
dotnet tool restore
dotnet user-secrets set 'ConnectionStrings:Database' 'Host=localhost;Port=5432;Database=muscleforge;Username=muscleforge;Password=YOUR_LOCAL_PASSWORD' --project src/MuscleForge.Api
dotnet ef database update --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
docker compose up --build -d api
curl --fail http://localhost:5187/health/live
curl --fail http://localhost:5187/health/ready
curl --fail http://localhost:5187/api/v1/status
```

Use the same PostgreSQL password in `.env` and the user-secret connection string. After the explicit migration step, starting the API provisions the account from `OWNER_EMAIL` and `OWNER_PASSWORD`. The configured bootstrap refuses to start against an unmigrated or incompatible database. SDK runs without owner configuration can still serve liveness/status while readiness returns 503 for an unusable schema. API startup and health requests never create or migrate the schema.

Compose binds both ports to loopback. This is local development infrastructure. A deployed API needs the HTTPS proxy, the protected-key configuration below, backups, and explicit migrations in the architecture. The runtime API image contains no SDK or EF CLI; apply migrations from the host SDK or a separate deployment step, not by running `dotnet ef` in the API container.

`docker compose down` retains the database volume. `docker compose down -v` deletes the database and authentication key volumes.

## Run the API from the SDK

Start PostgreSQL with `docker compose up -d database`. Configure the same connection string in user secrets with your local password:

```bash
dotnet tool restore
dotnet restore MuscleForge.Backend.sln
dotnet user-secrets set 'ConnectionStrings:Database' 'Host=localhost;Port=5432;Database=muscleforge;Username=muscleforge;Password=YOUR_LOCAL_PASSWORD' --project src/MuscleForge.Api
dotnet ef database update --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
dotnet dev-certs https --trust
dotnet run --project src/MuscleForge.Api --launch-profile https
```

HTTPS listens at `https://localhost:7243`. The development OpenAPI endpoint is `/openapi/v1.json`. On hosts without automatic certificate trust support, configure your platform's certificate store. User secrets are for local development.

## Apply and inspect migrations

The initial migration provisions Identity with UUID keys and owner-scoped exercises, including required bounded names/equipment, the supported `ExternalWeightAndReps` tracking type, archive timestamps, an owner/catalog index, and restrictive owner foreign keys. Database checks also reject whitespace-only names/equipment and unsupported tracking types.

Configure the target connection string first. For the local Compose database, use the user-secret command above with `Host=localhost` and the password from your `.env`. A deployment may supply `ConnectionStrings__Database` from its secret configuration instead; never commit that value.

From the repository root:

```bash
dotnet tool restore
dotnet ef migrations list --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
dotnet ef database update --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
dotnet ef migrations has-pending-model-changes --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
```

`--project` selects the Infrastructure migration assembly; `--startup-project` selects API configuration. Explicit Development configuration loads local user secrets. Use the intended deployment environment and secret configuration for a deployed database, take a backup first, and run the migration step once before rolling out the API. Reapplying the current migration is idempotent; an empty database remains unready until it is applied.

For SQL review, generate an idempotent script without applying it:

```bash
dotnet ef migrations script --idempotent --output artifacts/migrations.sql --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
```

After a successful update, retry `http://localhost:5187/health/ready` for Compose or `https://localhost:7243/health/ready` for the SDK profile. A 503 response contains no migration identifiers or connection diagnostics. Readiness compares migration history; real PostgreSQL tests and the pending-model check verify the model/migration contract.

## Provision and maintain the owner account

Set the account credentials in your local `.env` file, copied from [.env.example](.env.example):

```dotenv
POSTGRES_PASSWORD=
OWNER_EMAIL=you@example.com
OWNER_PASSWORD=
```

Fill both password values with your own passwords. `POSTGRES_PASSWORD` authenticates the backend to PostgreSQL; `OWNER_PASSWORD` is the initial password for your MuscleForge app login. Compose requires these settings and maps the owner values to `Owner__Email` and `Owner__Password` in the API container. Keep `.env` local and restrict access to it; it is excluded from Git and the Docker build context. Use single-quoted values when passwords contain `$`, `${...}`, or significant spaces so Compose preserves them literally; see [Docker's environment-file syntax](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/#env-file-syntax).

Apply the migrations as described above, then run `docker compose up --build -d api`. Before accepting HTTP requests, the API creates the owner with Identity validation and password hashing. Passwords must contain 12–4096 characters, including uppercase, lowercase, a digit and a non-alphanumeric character; password spaces are preserved. The email is used as the username. No separate account-creation command is required, and the old `owner create` command is removed.

**These settings are for initial creation.** Restarting the API preserves the existing normalized account, password, security stamp and personal data. Changing `OWNER_PASSWORD` does not reset an existing password. Changing `OWNER_EMAIL` to an address that does not match an existing account fails rather than silently creating another bootstrap owner. Concurrent startup is serialized through a short PostgreSQL transaction and advisory lock, with a ten-second lock wait limit.

Incomplete/invalid configuration, failed Identity validation or an incompatible schema stops configured startup with a generic error, without printing credentials or original exception details. Bootstrap never applies migrations or seeds exercises. Exercise seeding belongs to MF-12; HTTP login and refresh are described in [authentication.md](docs/authentication.md). The shared `AddMuscleForgeIdentity()` registration supplies the same password policy for those endpoints.

The SDK does not parse `.env`. For SDK or IDE runs, supply `Owner:Email` and `Owner:Password` through .NET user secrets or the equivalent `Owner__Email` and `Owner__Password` process environment variables. Leaving both values unset/empty skips bootstrap; supplying only one fails. OpenAPI export clears owner configuration so contract generation never creates an account.

After suspected credential compromise, the maintenance command can reset the account security stamp using the same configured database:

```bash
docker compose run --rm --no-deps api owner reset-security-stamp --email you@example.com
# Or use the published backend with its deployment connection-string configuration:
dotnet /app/MuscleForge.Api.dll owner reset-security-stamp --email you@example.com
dotnet /app/MuscleForge.Api.dll owner --help
```

This changes the account security stamp while preserving its password and personal data. The refresh endpoint validates that stamp, so refresh sessions issued with the old stamp are rejected. Already-issued access tokens expire normally; this is not immediate access-token revocation or a per-device session-management system. See the framework's [refresh implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs) and [access-token handler](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Security/Authentication/BearerToken/src/BearerTokenHandler.cs).

| Exit code | Meaning |
| --- | --- |
| 0 | Security stamp reset or help displayed |
| 2 | Invalid maintenance arguments or email |
| 3 | Account not found when resetting its security stamp |
| 4 | Configuration, schema readiness, database, or administration failure |
| 130 | Command cancelled |

These exit codes apply to maintenance commands. Configured API bootstrap failures stop the process before HTTP serving. A failure never triggers automatic migration or account deletion. No public registration route is exposed.

## Debug the API in Docker with Visual Studio

Use Visual Studio 2026 with the **ASP.NET and web development** workload and **Container development tools** installed, plus Docker Desktop running Linux containers.

1. Copy `.env.example` to `.env` if needed, then set `POSTGRES_PASSWORD`, `OWNER_EMAIL` and `OWNER_PASSWORD`. Also set `DATA_PROTECTION_CERTIFICATE_PASSWORD` and follow the certificate setup above; Compose validates all required settings even when starting only the database.
2. From the repository root, start PostgreSQL and wait for it to become healthy:

   ```bash
   docker compose up -d --wait database
   ```

   Apply the migrations from the host SDK using `Host=localhost`, as described above, before starting the debug container. The host cannot resolve the Compose-only hostname `database`.

3. Set the API's development connection string in user secrets, using the same password as `.env`:

   ```bash
   dotnet user-secrets set 'ConnectionStrings:Database' 'Host=database;Port=5432;Database=muscleforge;Username=muscleforge;Password=YOUR_LOCAL_PASSWORD' --project src/MuscleForge.Api
   ```

   `database` is the Compose service name. Visual Studio mounts this project's user secrets into its debug container. When switching back to the SDK's `https` profile, set the connection string's host back to `localhost`.

4. Open `MuscleForge.Backend.sln`, set `MuscleForge.Api` as the startup project, select **Debug** and the **Docker** launch profile, and press **F5**.
5. Set a breakpoint in `Program.cs` inside the `/api/v1/status` handler, then request `http://localhost:5187/api/v1/status`. Visual Studio stops at the breakpoint inside the API container. The browser initially opens `http://localhost:5187/openapi/v1.json`.

The Docker profile joins the `muscleforge_default` network created by Compose and exposes HTTP only on `127.0.0.1:5187`. If you previously started the API with Compose, run `docker compose stop api` before F5 to free that port. Use the default Compose project name `muscleforge` for this workflow.

Visual Studio's standalone Docker profile uses mounted Development user secrets rather than Compose's owner environment mapping. Provision the account with Compose first, or configure `Owner:Email` and `Owner:Password` in the API's Development user secrets when you want that profile to bootstrap it.

Visual Studio's Fast mode builds Debug assemblies on the host, mounts them into the Dockerfile's `base` stage, and manages the debugger. Stop debugging with **Shift+F5**; PostgreSQL stays running. Use `docker compose stop database` when finished, or `docker compose down` to remove the Compose containers while retaining database data.

For a Debug image built entirely in Docker, use `docker build --build-arg BUILD_CONFIGURATION=Debug -t muscleforge-backend:debug .`. A normal `docker build` or `docker compose up --build` builds Release.

References: [Visual Studio container launch settings](https://learn.microsoft.com/en-us/visualstudio/containers/container-launch-settings), [Fast mode and Dockerfile stages](https://learn.microsoft.com/en-us/visualstudio/containers/container-build).

## Build and check

```bash
dotnet tool restore
dotnet build MuscleForge.Backend.sln -c Release
dotnet test MuscleForge.Backend.sln -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
python3 scripts/export-openapi.py --check
```

Domain tests cover owner requirements, supported tracking, validation, atomic edits, and archive timestamps. HTTP tests use an injected probe to check status, liveness/readiness, and unavailable registration. Migration tests use Testcontainers with isolated real PostgreSQL to verify schema/readiness, explicit migration behavior, constraints and round trips; Docker must be available, and these checks are not silently skipped. CI also applies migrations explicitly before checking the API container's readiness. Authenticated resource ownership tests will accompany the corresponding endpoints.

Owner-bootstrap tests launch the actual API with environment-only credentials against isolated PostgreSQL. They verify creation/hash validation, repeated and concurrent startup, preservation of personal data, conflicting owner configuration, safe errors and schema prerequisites. Tooling cases exercise the restored EF CLI and Python exporter with owner configuration present, proving migration and contract work do not provision an account. Maintenance tests cover security-stamp reset and removal of the creation CLI. Interactive password input and its terminal-only tests have been removed.

Restore generates `packages.lock.json` files. Commit the resolved files alongside dependency changes. CI enforces existing lockfiles.

## API contract

After a route/DTO change, build Release and run `python3 scripts/export-openapi.py`. It starts a temporary local development API, reads framework-generated OpenAPI, inlines schemas, and removes host/documentation metadata. CI checks the canonical `contracts/openapi.json` snapshot for drift. The exporter supports authentication request bodies and bearer security metadata; parameterized operations remain an explicit MF-13 extension. The frontend stays pinned to its existing snapshot until its generator is updated.

For a frontend update, commit the backend change, deliberately copy the contract, and record that backend commit and SHA-256 in its `contracts/source.json`. Frontend builds never fetch a running backend or another checkout. Breaking-change comparison and versioned release artifacts are required before the first supported app release.

## Validation status

MF-8 acceptance was verified on Linux ARM64 with SDK 10.0.403 and PostgreSQL 17.11. The Release build passes with warnings treated as errors; 13 unit tests and 27 integration tests pass with zero skips, including 23 real-PostgreSQL cases. The pending-model and OpenAPI contract checks pass.

The Release runtime image builds successfully. In an isolated container network, the API returned readiness 503 against a reachable empty database, then 200 after the explicit EF CLI step applied `20261103150257_InitialIdentityAndExercises`; the status endpoint remained available. Test containers/databases are isolated from local development data. Visual Studio debugger attachment remains a separate manual check in Visual Studio.

The MF-10 environment-configured revision passes the Release solution build with warnings treated as errors and all 81 tests (13 unit and 68 integration, including 44 real-PostgreSQL cases), with zero skips on Linux. Actual EF CLI migration with owner configuration succeeds without creating an account, and OpenAPI export with ambient owner configuration leaves an empty database untouched. The OpenAPI contract and model/migration snapshot are unchanged.

The Release image builds successfully. An isolated Compose project using a synthetic `.env` created one hashed owner account and reached healthy readiness. The container preserved literal password characters, including `$` and spaces. Recreating the API with a changed bootstrap password preserved the account, stored password hash and security stamp and stayed healthy. The temporary containers, volume and network were removed afterward; the owner's real `.env` was not read or changed. Implementation/testing were delegated separately, and the manager and independent reviewer reviewed the result. MF-10 was merged through PR #3 with passing PR/main CI.

MF-9 passes the Release build with warnings treated as errors and all 111 tests (13 unit and 98 integration, zero skips). The suite includes 44 real-PostgreSQL cases and HTTP checks through Kestrel/TestServer for DTO validation, errors, auth challenges, body limits, cancellation and safe logs. The exact 256 KiB chunked-body boundary is covered after separating Kestrel's transport allowance from the decoded-body cap. The OpenAPI drift and pending-model checks pass, and the Release Docker image builds. The existing 200 status response is unchanged; the contract adds its shared 500 problem schema. No frontend contract copy or client-generation change is required for the current status client; authenticated/error-consuming clients remain MF-13. PR CI and integration are tracked in MF-9.

MF-11 passes the Release solution build with warnings treated as errors (zero warnings/errors) and all 120 tests (13 unit, 107 integration, zero skips). PostgreSQL-backed authentication checks cover login, refresh, stable account identity, generic credential errors, lockout, token expiry, stamp invalidation, per-IP throttling and safe logs. Encrypted keys preserve access and refresh tokens across a host restart; independent keys reject both. Production refuses missing key configuration. OpenAPI drift and EF pending-model checks pass, and the Release container builds. Android sign-in, generated-client adoption and deployed TLS/proxy verification remain MF-14, MF-13 and MF-43/MF-45 respectively.
