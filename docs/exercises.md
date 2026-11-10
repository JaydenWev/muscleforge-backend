# Personal exercise catalog — MF-12

All routes require the existing bearer access token. The server resolves the current Identity account on every request and scopes every query to its UUID. Another owner's UUID and an unknown UUID both return `404 resource_not_found`. An absent/invalid token or a token for a deleted account returns `401 authentication_required`. Request DTOs cannot set ownership or seed provenance; responses expose neither field.

## HTTP contract

| Method and route | Result |
| --- | --- |
| `GET /api/v1/exercises` | `200` page of the owner's active exercises |
| `GET /api/v1/exercises/{id}` | `200` exercise, including an archived record |
| `POST /api/v1/exercises` | `201` custom exercise; `Location` points to its item route |
| `PUT /api/v1/exercises/{id}` | `200` full edited representation, or `409 state_conflict` for an attempt to unarchive |

List parameters: `search` is a literal case-insensitive substring of name or equipment, at most 100 characters after trimming; `%`, `_` and backslash have no wildcard meaning. `includeArchived` defaults to `false`; set it to `true` for catalog management. `offset` defaults to `0` and must be nonnegative. `limit` defaults to `50`, with a range of `1–100`. Results sort by PostgreSQL name order then UUID. A response is `{ "items": [...], "offset": 0, "limit": 50, "totalCount": 11 }`; total count includes the selected filters. Separate count/page queries do not promise a snapshot across concurrent catalog changes; offset pages may shift while other requests edit data.

Create example (JSON body):

```json
{
  "name": "Dumbbell incline press",
  "equipment": "Dumbbells and adjustable bench",
  "trackingType": "ExternalWeightAndReps",
  "loadConvention": "PerDumbbell"
}
```

`name` and `equipment` are required, nonblank and limited to 100 characters **after** trimming surrounding whitespace. Tracking supports only the case-sensitive string `ExternalWeightAndReps`. `loadConvention` accepts `TotalLoad` or `PerDumbbell`. Omitting tracking/convention uses `ExternalWeightAndReps`/`TotalLoad`; explicitly passing `null` or an unsupported value fails. For PUT, send those editable fields plus the required boolean `isArchived`. Retain the current convention when editing. Missing/null archive state is invalid. IDs and archive timestamps are server-owned.

An exercise response contains `id`, `name`, `equipment`, `trackingType`, `loadConvention`, `isArchived`, and nullable `archivedAt`. Names/equipment are returned trimmed. Invalid fields return `400 validation_failed` with fixed public field errors; malformed JSON/query values return `400 invalid_request`. The shared [API standards](api-errors.md) also apply to body limits and safe errors.

## Load and archive rules

`TotalLoad` means total external load (including the bar for a barbell lift). `PerDumbbell` means one dumbbell's load, rather than the sum of a pair. These distinguish exercises with different load conventions; the app should show that guidance. Bodyweight, assistance, distance and timed tracking require later models. MF-18 supplies unit/precision rules; these endpoints accept exercise metadata, not workout weights or reps.

Archive by sending `isArchived: true` in a PUT. There is no delete route or unarchive operation. The first archive timestamp is retained, including concurrent archive requests; the update transaction locks only the owner-scoped exercise row. Archived metadata may still be edited with `isArchived: true`. Editable metadata otherwise uses last-write-wins; no optimistic version or mutation receipt is promised for this catalog.

Normal list selection excludes archives. Include-archived listing and item lookup retain them for management and historical references. MF-20 and later template/session implementations must reject archived exercises when newly selecting them while retaining existing historical/active-session references. Those aggregate rules are not implemented by this catalog alone.

## Provisioning and migration

Apply migrations explicitly before starting the configured API. Use the existing `OWNER_EMAIL`/`OWNER_PASSWORD` environment setup in the README; there is no new account-creation or exercise-seeding CLI. After the configured account is created or found, startup inserts missing common exercises before serving requests. SDK/IDE runs need the equivalent `Owner:Email`/`Owner:Password` configuration; `.env` is consumed by Compose, not parsed by the SDK. With owner configuration absent, startup performs no seeding. OpenAPI export and EF tooling do not provision accounts or seeds.

The initial catalog contains barbell squat, bench press, deadlift, row and overhead press; dumbbell curl, press and row; lat pulldown, leg press and cable triceps pushdown. Each owner receives separate exercise UUIDs. Stable seed keys are unique per owner and are internal provenance, not a shared public catalog or mutable exercise names. Parameterized conflict-safe inserts in one transaction add missing keys only, including during concurrent startup. Renames, equipment/convention edits, archived seeds and same-name custom rows are preserved. Archiving a seed keeps its key, so startup cannot resurrect it.

`20261110172120_ExerciseCatalog` adds nullable provenance and a nonnull convention defaulting to `TotalLoad`; previous custom exercises retain their data and have no seed key. The reverse migration retains exercise rows but removes the added convention/provenance fields. Normal readiness rejects unapplied or unrecognized migration history. Back up the intended deployment database and follow the README's explicit migration steps; startup never migrates it.

## Client handoff and verification

The standard build-time exporter owns `contracts/openapi.json`. It describes bearer requirements, DTO requiredness, case-sensitive enum values/defaults, paging bounds and problem responses. MF-15 should pin this backend implementation commit and snapshot SHA-256, regenerate with its existing NSwag tool, then build the native catalog flows. No frontend source or sibling checkout is required by the backend build.

Tests use actual bearer logins and isolated PostgreSQL to cover ownership, CRUD, literal search/paging, input boundaries, archive visibility/concurrency, repeat/concurrent seeding, configured process startup and migration upgrade/downgrade. Validation results are recorded in the PR and Linear; Android catalog UI evidence belongs to MF-15.
