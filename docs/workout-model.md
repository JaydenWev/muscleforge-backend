# Workout model and persistence agreement

MF-19 provides the server-side model and additive schema for later template and session endpoints. It introduces no HTTP routes or Android screens. The existing account, preferences, exercise and authentication contracts remain unchanged.

## Aggregates and snapshots

`WorkoutTemplate` owns ordered `TemplateExercise` and `TemplateSet` rows. A template has an owner, name, positive integer version and optional archive timestamp. Each exercise references an exercise owned by the same account, records a position and optional rest seconds, and contains set prescriptions: warm-up/working kind, optional canonical target weight and optional positive minimum/maximum repetition range. Both range endpoints are present or absent together, and minimum cannot exceed maximum.

`WorkoutSession` owns `SessionExercise` and `WorkoutSet` rows. Session, session-exercise and workout-set IDs are supplied by the caller; EF never replaces them. `WorkoutSession.FromTemplate` copies the template's name/version, exercise name/equipment/tracking/load convention, rest seconds and full set prescriptions using caller-supplied child-ID maps. It rejects missing mappings, another owner's sources and archived sources. Subsequent source edits or archives do not alter the copy. Empty sessions use the same session model without template provenance.

Session sets keep prescribed kind/range/weight separately from actual kind, weight, repetitions and completion. Updating actuals does not overwrite prescriptions. Actuals can be null while drafting; non-null repetitions must be positive. Completed sets require both weight and repetitions. Zero external weight is valid. Notes are optional and limited to 2,000 characters.

Positions are zero-based integers from 0 through 99, unique within each parent; gaps are allowed. This bounds each aggregate to 100 exercises and each exercise to 100 sets. Consumers must order materialized children by `Position`; database row order is not an ordering contract. Rest is either unspecified or 0–86,400 seconds. Template names and exercise snapshot labels are bounded to 100 characters.

## Weight, time and lifecycle

Target and actual weights are canonical decimal kilograms stored as PostgreSQL `numeric(10,3)`, from zero through 9,999,999.999. Model methods reuse the MF-18 validation policy and reject excess scale before persistence. PostgreSQL numeric typemods round direct SQL inputs; they do not replace boundary validation. Both lower and upper checks also reject numeric NaN. Pound conversion remains a future API-boundary operation, following [the units agreement](units-timezones.md).

Timestamps are normalized to UTC and stored as instants. The session separately captures a known IANA time-zone identifier for local-day reporting; later account-preference changes do not rewrite it. Only `InProgress` can transition to `Completed` or `Discarded`, with an end timestamp no earlier than the start. Completion requires at least one completed working set. Public aggregate mutations reject terminal state; child mutations are internal and exposed collections are read-only. Domain guards enforce cross-row lifecycle rules; the schema does not claim to prevent arbitrary administrative SQL changes to terminal children.

Template and session `Version` are explicit positive integer EF concurrency tokens, initially 1. **A future accepted request advances its aggregate version exactly once, before archiving/completing/discarding, and saves all changes in one transaction.** `AdvanceVersion` rejects terminal or archived aggregates and integer exhaustion. EF does not increment automatically. The future workflow must validate the request before committing, handle stale-version conflicts and discard failed in-memory mutations; MF-19 does not implement those endpoints.

## Retry storage for MF-22 and MF-23

`StartRequestHash` is an immutable lowercase SHA-256 hex digest for both template and empty starts. Template provenance is either entirely absent or includes source template ID, captured name and positive source version. MF-22 will define normalization of the semantic start input, compare the persisted hash before reusing the client session ID, and enforce expected template version transactionally. This model supplies storage and snapshot creation, not idempotent HTTP start handling.

`WorkoutMutationReceipt` has the primary key `(OwnerId, MutationId)`, plus session ID, lowercase semantic SHA-256 request hash, positive result version, UTC creation time and the original acknowledgement JSON object. The acknowledgement is stored as bounded text, preserving its original representation; PostgreSQL validates that it is a JSON object without rewriting it to jsonb. It is limited to 262,144 characters. Receipt objects expose no update methods, and restrictive foreign keys prevent cascade removal with a session.

The agreed future acknowledgement identifies the accepted mutation ID and accepted result version; it must not be reconstructed from a newer session version. MF-23 will specify exact wire names and semantic-hash normalization, write session and receipt atomically, resolve duplicate receipt races, and check matching receipts before rejecting terminal state. GET will return current authoritative state, which may be newer than that acknowledgement. Retain receipts for the initial project lifetime; this migration introduces no expiry or cleanup job.

## Schema and migration

`20261117071531_WorkoutModels` adds the seven workout tables and the exercise alternate key needed by composite foreign keys. Parent/source references include owner IDs to prevent cross-account attachment. Referenced exercises and templates are archived rather than deleted. Unique parent/position indexes, checks for supported enum values and coherent nullable fields, a partial unique active-session index per owner, an `(OwnerId, StartedAt, Id)` history index and receipt lookup keys reinforce the model.

Apply migrations explicitly using the repository's normal deployment commands. Startup does not migrate. The additive upgrade preserves existing account/exercise rows. Downgrading removes workout data and the new exercise alternate key while retaining prior account/preferences/exercise tables; use only with an appropriate backup and deployment plan.

## Verification

On 2026-11-17, SDK 10.0.403 built the complete Release solution with warnings treated as errors: zero warnings/errors. All 30 unit tests and all 142 integration tests passed, with zero errors, failures, skips or unexecuted tests. The full integration run used real isolated PostgreSQL 17.11 containers and captured exit code 0. The new cases cover upgrade/downgrade preservation, owner/parent constraints, malformed direct SQL, active-session races, stale contexts, template snapshots, receipt identity and multirow rollback.

The EF pending-model check passed. OpenAPI export matched the committed snapshot byte-for-byte; compatibility passed against the supported baseline. The unchanged compatibility fixture passed and endpoint-removal fixture failed as expected. Existing configured owner/auth/catalog behavior remains covered by the full suite. Manager and independent review findings were resolved before these checks.

The first local integration attempt could not open the Docker socket under the container's non-root UID. Repeating the in-process runner as root inside the isolated verification container reached actual PostgreSQL and produced the complete passing result above; the failed environment attempt is not counted as acceptance. No personal deployment database was used or migrated.

Reproduce with the standalone backend commands in the README: locked restore, local tool restore, Release build, unit/integration tests, pending-model check, contract export and compatibility checks. In the constrained ARM64 workspace, the executable xUnit runners were invoked inside the SDK container with `-maxThreads 1 -trx ...`; TRX reports and the integration log/exit code are retained under ignored `artifacts/verification/`. Ordinary hosted CI continues using the repository's standard test commands and also builds the runtime image and verifies explicit migration/readiness.
