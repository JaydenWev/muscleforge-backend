# Foundation verification

Baseline: backend `18fbebfc471313a009a27d1bf2f78e81152b5a65`, frontend `e3241c2adec6d755422758dc1f5b907a4079be1e`. The repositories build independently and communicate through HTTP and the frontend's pinned contract.

On 2026-11-15, SDK 10.0.403 built the pinned backend Release solution with warnings treated as errors and zero warnings/errors. The complete PostgreSQL integration suite then passed in one captured serialized run: 117 tests, zero failures/errors/skips/not-run, exit code 0. The TRX, log and exit record are under `TestResults/verification.*` in the verification worktree. This supersedes the earlier incomplete local runs. The baseline also has 14 passing unit tests, and [hosted CI run 43729569269](https://github.com/JaydenWev/muscleforge-backend/actions/runs/43729569269) passed its full build/test, contract, migration and container checks.

Existing cases cover migration upgrades, owner provisioning and repeat seeding, authentication and refresh, and exercise HTTP ownership including foreign IDs and attempted owner injection. There are no template or session endpoints to test yet. Local tooling subprocesses require restored tools and the NuGet cache at the paths recorded by the build; setup failures must not be reported as product failures or silently skipped.

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

Restore the local .NET tool before running integration tests, since one invokes `dotnet ef`. Where host capacity requires it, add `-maxThreads 1` to the in-process integration runner. Tests use isolated Testcontainers PostgreSQL instances; Docker is required and failures are not skips. For a separately running synthetic HTTPS server, use the documented explicit migration and owner provisioning steps in the [README](../README.md), on a new isolated database and with synthetic credentials. Never point the verification consumer at personal data: it creates and archives an exercise. The frontend's `docs/foundation-verification.md` describes the HTTP consumer.

## Physical-phone acceptance — 2026-11-15

On a OnePlus 13 (CPH2653, Android 16/API 36), the frontend `e3241c2adec6d755422758dc1f5b907a4079be1e` development-signed Debug APK (SHA-256 `58dabcc18264401acdd0ad92d4b07aabda80072da59d0bd75033dca0037cb638`) connected over the normal Android HTTP handler to the isolated HTTPS API at `jadyn-server:7443`, running this backend revision and the `mf16-foundation` image with its existing PostgreSQL database. The phone trusted the installed development user CA; a synthetic owner account was used.

Native sign-in succeeded. The app displayed the seeded Barbell Bench Press with total-load guidance; creating a Dumbbell / PerDumbbell exercise, exact search, editing, and archiving all produced the expected results. Show archived displayed the archived row. Force-stop/relaunch restored the protected Shell through the real Android SecureStorage-backed credential. Sign-out returned to Sign in; Back/reopen briefly showed restoration, and a subsequent force-stop/cold start settled on Sign in without restoring the protected Shell. This verifies the observed sign-out and credential clearing across cold restart, but not exhaustive Back-stack behavior. No emulator work was performed. Android SecureStorage failure injection was not exercised on the phone; portable fakes do not establish that platform behavior.
