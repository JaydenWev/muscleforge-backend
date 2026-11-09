# API contract tooling

The API owns `contracts/openapi.json`. Run `scripts/export-openapi.sh` after restoring the API project; `--check` regenerates into a temporary directory and compares exact bytes. Microsoft.Extensions.ApiDescription.Server 10.0.14 generates the standard referenced document with OpenAPI 3.0.4. The export is opt-in, uses an inert server, clears ambient owner/database/key configuration and supplies a dummy connection string. Normal builds and EF tooling do not export or provision an owner.

`scripts/check-contract-compatibility.sh` runs oasdiff v1.37.0 pinned by image digest against `contracts/baselines/mf11-provisional.json`. Both errors and warnings block the comparison. `tests/contract_tooling/smoke.sh` checks an unchanged contract and confirms endpoint removal fails. Docker is required. The original baseline remains unchanged; `mf11-oasdiff-ignore.txt` documents exactly two expiry-pattern findings caused by correcting an output-only integer schema. No rule is disabled globally.

`scripts/package-contract.sh --revision <full-commit> --output <directory>` uses a small .NET file-based app to copy the exact snapshot and write its source revision, route version, document version, OpenAPI version and SHA-256. CI publishes the package under its versions and source/run identity. Tooling consumes no account credentials or live database.

## Coordinated updates

Prepare the backend PR and verify export/compatibility first. In a separate frontend PR, copy the snapshot from that exact commit or artifact, verify its hash, update provenance and regenerate using the pinned NSwag tool. Record both PRs and revisions. Deploy additive backend APIs before distributing clients that use them. A breaking result needs an explicit version/migration decision rather than a silent baseline refresh.

`/api/v1`, OpenAPI `info.version` and repository commit/release revisions identify the HTTP boundary, document version and exact build respectively. This development baseline is provisional; MF-47 owns released app/server support policy.

Tool references: [Microsoft build-time generation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0), [oasdiff](https://github.com/oasdiff/oasdiff).
