# 0002 — Owner provisioning from deployment configuration

Date: 2026-11-05

Status: Accepted by the owner

The owner requested that the app account's initial email and password be configured in `.env` instead of entered through an account-creation CLI. This supersedes the original CLI-creation direction in MF-10 and the architecture baseline. The PostgreSQL connection password remains a separate setting.

Docker Compose maps `OWNER_EMAIL` and `OWNER_PASSWORD` to ASP.NET Core's `Owner:Email` and `Owner:Password`. After explicit database migrations and before accepting HTTP requests, the API provisions the missing owner with Identity validation and password hashing. It never applies schema migrations or exposes public registration. Both settings absent skips bootstrap for SDK/contract tooling; incomplete or invalid configured bootstrap fails with safe output. Compose requires both values for its API service.

An existing normalized owner is preserved, including its password, security stamp and personal data. Changing the configured password does not rotate an existing password. A changed email that does not match an existing account fails rather than creating an additional bootstrap owner. A PostgreSQL transaction and transaction-scoped advisory lock serialize bootstrap across concurrent API instances, including those with conflicting email configuration.

The manual account-creation command and terminal-input implementation are removed. The security-stamp reset maintenance command remains; HTTP login/refresh is still MF-11 and exercise seeding is MF-12. Account recovery and password rotation remain explicit administration work rather than implicit startup actions.

`.env` stays outside Git and the Docker build context. Only `.env.example` is committed. The OpenAPI exporter explicitly clears owner configuration so contract generation never provisions an account from developer credentials. Provisioning runs after host construction so EF design-time tooling can construct the context before the database schema exists.
