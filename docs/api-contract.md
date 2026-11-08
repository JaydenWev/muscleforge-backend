# API contract tooling

Build Release, then run `python3 scripts/export-openapi.py` to update the canonical `contracts/openapi.json`; use `--check` to detect export drift. The exporter starts a local development API with owner and Data Protection key configuration cleared. It does not apply migrations or provision an account. Review the snapshot diff with endpoint changes.

Run `python3 -m unittest discover -s tests/contract_tooling -v` and `python3 scripts/check-contract-compatibility.py`. The latter compares against the complete MF-11 snapshot in `contracts/baselines/mf11-provisional.json`. This is a provisional development baseline, not a released support matrix or a promise that an app version is supported. MF-47 will define release support and baseline policy. Do not silently update the baseline to pass a breaking change.

The checker supports canonical inline OpenAPI 3.0.3, HTTP operations, path/query parameters, JSON-style schema bodies and responses, and bearer security alternatives. It checks removal and direction-sensitive input/output constraints. It treats composition, patterns, defaults, additional-properties and headers conservatively: changes fail when compatibility cannot be proven. Unknown schema and operation keywords fail closed; this is not a general-purpose OpenAPI compatibility proof or a substitute for behavioral and semantic review. Changes in error responses are checked too. Add explicit support and tests before using new contract constructs.

CI packages the checked snapshot and `manifest.json` containing the full source revision, route API version, document version, OpenAPI specification version and SHA-256 of the exact snapshot. Each artifact name contains the route/document versions, source commit and run identity. CI publishes a workflow artifact; it does not create a GitHub Release or deploy the API. Frontends deliberately copy a reviewed snapshot and record its source commit and hash; builds do not fetch another repository or a live server.

## Coordinated updates

Prepare the backend PR and verify export/compatibility first. In a separate frontend PR, deliberately copy the snapshot from that exact commit or its checked artifact, verify the hash, update provenance, regenerate and test the client. Record both PRs and revisions. For additive APIs, deploy the compatible backend before distributing a client that uses the new capability. A breaking result needs an explicit version/migration decision rather than a silent baseline refresh.

The route `/api/v1`, OpenAPI `info.version` and repository commit/release revisions describe different things: HTTP API boundary, contract document version and exact build provenance. MF-47 defines released app/server support policy; this provisional baseline does not establish one.
