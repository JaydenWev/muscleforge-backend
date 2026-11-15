# Foundation verification

Baseline: backend `18fbebfc471313a009a27d1bf2f78e81152b5a65`, frontend `e3241c2adec6d755422758dc1f5b907a4079be1e`. The repositories build independently and communicate through HTTP and the frontend's pinned contract.

On 2026-11-13, SDK 10.0.403 on Linux ARM64 built the backend Release solution with `dotnet build MuscleForge.Backend.sln -c Release -warnaserror -m:1 -p:UseSharedCompilation=false` (zero warnings/errors). The in-process xUnit runner passed 14 unit tests. The initial real PostgreSQL integration run completed with 117 total, 3 failed, zero skipped: Docker container creation timed out in a fixture during concurrent host load. A serial retry (`-maxThreads 1`) completed with 117 total, 1 failed, zero skipped. The remaining failure was the EF tooling test, whose `dotnet ef` process exited 1; the container had not had `dotnet tool restore` run. After restoring dotnet-ef 10.0.14, a separate run of that test passed (1/1, zero skipped). The original child process output was not retained, so its exact failure cause is unconfirmed. This is 116 passing cases in the full serial run plus one passing targeted retry, not a clean single full-suite run. Existing cases cover migration upgrades, owner provisioning and repeat seeding, authentication and refresh, and exercise HTTP ownership including foreign IDs and attempted owner injection. There are no template or session endpoints to test yet.

Run the full suite with Docker available:

```bash
dotnet tool restore
dotnet build MuscleForge.Backend.sln -c Release -warnaserror -m:1 -p:UseSharedCompilation=false
dotnet test MuscleForge.Backend.sln -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/MuscleForge.Infrastructure --startup-project src/MuscleForge.Api --configuration Release -- --environment Development
scripts/export-openapi.sh --check
```

Where the test host cannot create named pipes, run the built xUnit v3 executables in-process instead:

```bash
dotnet tests/MuscleForge.UnitTests/bin/Release/net10.0/MuscleForge.UnitTests.dll -trx /tmp/mf16-unit.trx
dotnet tests/MuscleForge.IntegrationTests/bin/Release/net10.0/MuscleForge.IntegrationTests.dll -trx /tmp/mf16-integration.trx
```

Restore the local .NET tool before running integration tests, since one invokes `dotnet ef`. Where host capacity requires it, add `-maxThreads 1` to the in-process integration runner. Tests use isolated Testcontainers PostgreSQL instances; Docker is required and failures are not skips. For a separately running synthetic HTTPS server, use the documented explicit migration and owner provisioning steps in the [README](../README.md), on a new isolated database and with synthetic credentials. Never point the verification consumer at personal data: it creates and archives an exercise. The frontend's `docs/foundation-verification.md` describes the HTTP consumer. Phone acceptance remains pending and requires a reachable HTTPS endpoint whose certificate the phone trusts; status alone does not verify authentication or catalog behavior.
