# 0001 — Initial foundation increment

Date: 2026-11-01

Status: Accepted for the initial scaffold

The architecture baseline remains the target. The first increment establishes project boundaries, a public API version check, database connectivity, contract ownership, and independent pipelines.

Core contains the initial exercise model and a narrow database probe interface. Infrastructure contains the EF/Identity model and PostgreSQL implementation. API owns HTTP DTOs and composition. Domain writes and authentication are not exposed yet.

The MAUI app consumes only the public status contract. Workout tabs describe future functionality. Authentication, secure owner provisioning, seeded exercises, EF migrations, and owner-scoped exercise endpoints form the next increment of milestone 1. Extend contract export and client generation with auth/request/error support alongside those endpoints.

Compose is local development infrastructure with loopback ports and an isolated database network. The production TLS proxy, key storage, migrations, backups, and restore procedure will accompany deployment. The connectivity probe does not certify schema or deployment readiness.

The authoring workspace has no .NET/Android toolchain, NuGet network access, or Docker daemon access. Runtime checks and resolved lockfiles are pending the first equipped build. CI contains the commands and publishes lockfiles for review. No successful build or device result is claimed.
