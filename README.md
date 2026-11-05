# MuscleForge Backend

ASP.NET Core API for MuscleForge, a personal workout tracker. Companion repository: [muscleforge-frontend](https://github.com/JaydenWev/muscleforge-frontend).

## Current increment

The initial foundation contains independent API, Core, and Infrastructure projects; a PostgreSQL/EF Core context prepared for Identity; an exercise domain model; health endpoints; a versioned status contract; tests; and container/CI configuration.

This is the first part of milestone 1. Identity and personal-exercise schema migrations and owner administration commands are included; authentication and exercise endpoints are next. There is no account registration or workout data endpoint yet. Readiness requires connectivity and the exact migration history expected by this API build; a reachable but unmigrated, outdated, or unrecognized schema is not ready.

See the [architecture baseline](docs/architecture.md), [toolchain](docs/toolchain.md), and [foundation decision](docs/decisions/0001-initial-foundation.md).

## Requirements

- [.NET SDK 10.0.403](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), pinned by `global.json`.
- Docker with Compose v2 for local PostgreSQL/container development.
- Python 3.11+ for contract export; CI uses 3.12.

## Run the local containers

```bash
cp .env.example .env
# Set a unique POSTGRES_PASSWORD in .env before continuing.
docker compose up -d --wait database
dotnet tool restore
dotnet user-secrets set 'ConnectionStrings:Database' 'Host=localhost;Port=5432;Database=muscleforge;Username=muscleforge;Password=YOUR_LOCAL_PASSWORD' --project src/MuscleForge.Api
dotnet ef database update --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
docker compose up --build -d api
curl --fail http://localhost:5187/health/live
curl --fail http://localhost:5187/health/ready
curl --fail http://localhost:5187/api/v1/status
```

Use the same local password in `.env` and the user-secret connection string. On an unmigrated database, `/health/live` and `/api/v1/status` work, but `/health/ready` returns 503 until the explicit migration step succeeds. API startup and health requests never create or migrate the schema.

Compose binds both ports to loopback. This is local development infrastructure. A deployed API needs the HTTPS proxy, Data Protection key persistence when authentication is added, backups, and explicit migrations in the architecture. The runtime API image contains no SDK or EF CLI; apply migrations from the host SDK or a separate deployment step, not by running `dotnet ef` in the API container.

`docker compose down` retains the database volume. `docker compose down -v` deletes its data.

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

Owner administration runs as a command mode of the existing API executable. Apply the migrations explicitly first and configure the same target connection string used by the API. These commands do not start an HTTP listener, migrate the database, or seed exercises. Exercise seeding belongs to MF-12; HTTP login and refresh belong to MF-11.

For local development, configure the API's connection-string user secret as shown above, then run:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/MuscleForge.Api -c Release --no-launch-profile -- owner create --email owner@example.test
```

Enter and confirm the password at the hidden terminal prompt. Passwords require at least 12 characters, including an uppercase letter, lowercase letter, digit and non-alphanumeric character. The command uses ASP.NET Core Identity's validators and password hasher, stores the email as the username, and requires unique email addresses. Use the same Identity registration and policy for future login endpoints.

For an already-published deployment, use its existing secret connection-string configuration and run the published executable:

```bash
dotnet /app/MuscleForge.Api.dll owner create --email owner@example.test
dotnet /app/MuscleForge.Api.dll owner --help
```

To use the existing local Compose configuration with an interactive terminal:

```bash
docker compose run --rm --no-deps -it api owner create --email owner@example.test
```

For automation, explicitly opt into reading one password line from standard input. Supply it from a protected deployment secret file; keep secret files outside the repository and restrict their permissions:

```bash
dotnet /app/MuscleForge.Api.dll owner create --email owner@example.test --password-stdin < /run/secrets/muscleforge-owner-password
```

The password limit is 4096 characters. A newline ends the input and is not part of the password; spaces are preserved. Redirected input without `--password-stdin` is rejected when a new password is required. Do not put passwords in command arguments, shell history, source files, or logs. The commands emit fixed administrative messages and never print passwords, password hashes, security stamps or connection details. They use the API's standard configuration and Development user secrets, with administrative arguments excluded from configuration parsing.

Repeating creation for the same normalized email reports that the account already exists, succeeds without reading a password, and leaves its ID, password, security stamp and personal data intact. The command does not reset existing accounts or replace personal data.

After suspected credential compromise, reset the account security stamp using the same configured target database:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/MuscleForge.Api -c Release --no-launch-profile -- owner reset-security-stamp --email owner@example.test
```

This changes the account security stamp while preserving its password and personal data. ASP.NET Core Identity's built-in refresh endpoint validates that stamp, so refresh sessions issued with the old stamp are rejected when the authentication endpoints are implemented in MF-11. Already-issued access tokens expire normally; this is not immediate access-token revocation or a per-device session-management system. See the framework's [refresh implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs) and [access-token handler](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Security/Authentication/BearerToken/src/BearerTokenHandler.cs).

| Exit code | Meaning |
| --- | --- |
| 0 | Account created, account already exists, stamp reset, or help displayed |
| 2 | Invalid arguments, input, email, or password validation |
| 3 | Account not found when resetting its security stamp |
| 4 | Configuration, schema readiness, database, or administration failure |
| 130 | Command cancelled |

A failure never triggers automatic migration or account deletion. Check the target configuration and schema separately; connection diagnostics are intentionally excluded from command output. No public registration route is exposed.

## Debug the API in Docker with Visual Studio

Use Visual Studio 2026 with the **ASP.NET and web development** workload and **Container development tools** installed, plus Docker Desktop running Linux containers.

1. Copy `.env.example` to `.env` and set a unique `POSTGRES_PASSWORD` if you have not already done so.
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

Visual Studio's Fast mode builds Debug assemblies on the host, mounts them into the Dockerfile's `base` stage, and manages the debugger. Stop debugging with **Shift+F5**; PostgreSQL stays running. Use `docker compose stop database` when finished, or `docker compose down` to remove the Compose containers while retaining database data.

For a Debug image built entirely in Docker, use `docker build --build-arg BUILD_CONFIGURATION=Debug -t muscleforge-backend:debug .`. A normal `docker build` or `docker compose up --build` builds Release.

References: [Visual Studio container launch settings](https://learn.microsoft.com/en-us/visualstudio/containers/container-launch-settings), [Fast mode and Dockerfile stages](https://learn.microsoft.com/en-us/visualstudio/containers/container-build).

## Build and check

```bash
dotnet build MuscleForge.Backend.sln -c Release
dotnet test MuscleForge.Backend.sln -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
python3 scripts/export-openapi.py --check
```

Domain tests cover owner requirements, supported tracking, validation, atomic edits, and archive timestamps. HTTP tests use an injected probe to check status, liveness/readiness, and unavailable registration. Migration tests use Testcontainers with isolated real PostgreSQL to verify schema/readiness, explicit migration behavior, constraints and round trips; Docker must be available, and these checks are not silently skipped. CI also applies migrations explicitly before checking the API container's readiness. Authenticated resource ownership tests will accompany the corresponding endpoints.

Owner-command tests launch the actual API executable against isolated PostgreSQL. On Linux, four additional tests use Python 3's standard-library PTY support to check hidden password entry, mismatched confirmation, and Ctrl+C at the terminal and an open stdin pipe. These tests caught and now guard against password echo before the first hidden read. They run in Linux CI and explicitly skip on other platforms; Windows terminal interaction has not been verified. Python 3 must be available as `python3` when running the Linux integration suite.

Restore generates `packages.lock.json` files. Commit the resolved files alongside dependency changes. CI enforces existing lockfiles.

## API contract

After a route/DTO change, build Release and run `python3 scripts/export-openapi.py`. It starts a temporary local development API, reads framework-generated OpenAPI, inlines schemas, and removes host/documentation metadata. CI checks the canonical `contracts/openapi.json` snapshot for drift. The exporter currently rejects authenticated or parameterized operations until their handling is implemented.

For a frontend update, commit the backend change, deliberately copy the contract, and record that backend commit and SHA-256 in its `contracts/source.json`. Frontend builds never fetch a running backend or another checkout. Breaking-change comparison and versioned release artifacts are required before the first supported app release.

## Validation status

MF-8 acceptance was verified on Linux ARM64 with SDK 10.0.403 and PostgreSQL 17.11. The Release build passes with warnings treated as errors; 13 unit tests and 27 integration tests pass with zero skips, including 23 real-PostgreSQL cases. The pending-model and OpenAPI contract checks pass.

The Release runtime image builds successfully. In an isolated container network, the API returned readiness 503 against a reachable empty database, then 200 after the explicit EF CLI step applied `20261103150257_InitialIdentityAndExercises`; the status endpoint remained available. Test containers/databases are isolated from local development data. Visual Studio debugger attachment remains a separate manual check in Visual Studio.

For MF-10, the combined Release build passes with warnings treated as errors. All 71 tests pass on Linux (13 unit and 58 integration cases), with no skips. The integration suite includes 43 real-PostgreSQL cases. The OpenAPI drift and pending-model checks pass; there is no HTTP or schema change. Implementation and verification were delegated separately, and both the manager and an independent reviewer reviewed the combined changes.

The new coverage includes sixteen PostgreSQL command cases and four real Linux terminal/pipe cases. Review and actual terminal probes found and resolved stdin cancellation and immediate-input password echo defects; the final suite verifies their fixes. Windows terminal behavior remains unverified. GitHub Actions and pull-request integration are pending for this branch; the current handoff is recorded in MF-10.
