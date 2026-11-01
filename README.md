# MuscleForge Backend

ASP.NET Core API for MuscleForge, a personal workout tracker. Companion repository: [muscleforge-frontend](https://github.com/JaydenWev/muscleforge-frontend).

## Current increment

The initial foundation contains independent API, Core, and Infrastructure projects; a PostgreSQL/EF Core context prepared for Identity; an exercise domain model; health endpoints; a versioned status contract; tests; and container/CI configuration.

This is the first part of milestone 1. Authentication, owner provisioning, database migrations, and exercise endpoints are next. There is no account registration or workout data endpoint yet. The database probe checks connectivity, not application schema readiness.

See the [architecture baseline](docs/architecture.md), [toolchain](docs/toolchain.md), and [foundation decision](docs/decisions/0001-initial-foundation.md).

## Requirements

- [.NET SDK 10.0.403](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), pinned by `global.json`.
- Docker with Compose v2 for local PostgreSQL/container development.
- Python 3.11+ for contract export; CI uses 3.12.

## Run the local containers

```bash
cp .env.example .env
# Set a unique POSTGRES_PASSWORD in .env before continuing.
docker compose up --build -d
curl --fail http://localhost:5187/health/live
curl --fail http://localhost:5187/health/ready
curl --fail http://localhost:5187/api/v1/status
```

Compose binds both ports to loopback. This is local development infrastructure. A deployed API needs the HTTPS proxy, Data Protection key persistence when authentication is added, backups, and explicit migrations in the architecture. No schema is created or migrated on startup.

`docker compose down` retains the database volume. `docker compose down -v` deletes its data.

## Run the API from the SDK

Start PostgreSQL with `docker compose up -d database`. Configure the same connection string in user secrets with your local password:

```bash
dotnet tool restore
dotnet restore MuscleForge.Backend.sln
dotnet user-secrets set 'ConnectionStrings:Database' 'Host=localhost;Port=5432;Database=muscleforge;Username=muscleforge;Password=YOUR_LOCAL_PASSWORD' --project src/MuscleForge.Api
dotnet dev-certs https --trust
dotnet run --project src/MuscleForge.Api --launch-profile https
```

HTTPS listens at `https://localhost:7243`. The development OpenAPI endpoint is `/openapi/v1.json`. On hosts without automatic certificate trust support, configure your platform's certificate store. User secrets are for local development.

## Debug the API in Docker with Visual Studio

Use Visual Studio 2026 with the **ASP.NET and web development** workload and **Container development tools** installed, plus Docker Desktop running Linux containers.

1. Copy `.env.example` to `.env` and set a unique `POSTGRES_PASSWORD` if you have not already done so.
2. From the repository root, start PostgreSQL and wait for it to become healthy:

   ```bash
   docker compose up -d --wait database
   ```

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
python3 scripts/export-openapi.py --check
```

Domain tests cover owner requirements, validation, atomic edits, and archive timestamps. HTTP tests use an injected probe to check status, liveness/readiness, and unavailable registration. CI separately starts real PostgreSQL and the API container to check connectivity. Schema and ownership integration tests will accompany authenticated endpoints.

Restore generates `packages.lock.json` files. Commit the resolved files alongside dependency changes. CI enforces existing lockfiles.

## API contract

After a route/DTO change, build Release and run `python3 scripts/export-openapi.py`. It starts a temporary local development API, reads framework-generated OpenAPI, inlines schemas, and removes host/documentation metadata. CI checks the canonical `contracts/openapi.json` snapshot for drift. The exporter currently rejects authenticated or parameterized operations until their handling is implemented.

For a frontend update, commit the backend change, deliberately copy the contract, and record that backend commit and SHA-256 in its `contracts/source.json`. Frontend builds never fetch a running backend or another checkout. Breaking-change comparison and versioned release artifacts are required before the first supported app release.

## Validation status

Debug and Release builds pass with warnings treated as errors, all nine tests pass, and the OpenAPI contract check passes. The Debug Docker image builds successfully. A container with mounted Debug assemblies and user secrets has been verified against real PostgreSQL, including status, liveness, readiness, and development OpenAPI endpoints. Visual Studio debugger attachment must be verified in Visual Studio; it cannot be exercised from the Linux command-line workspace.
