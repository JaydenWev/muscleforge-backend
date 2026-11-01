# MuscleForge Backend

The ASP.NET Core API for MuscleForge, a personal workout tracker.

Planned stack: .NET 10, ASP.NET Core, PostgreSQL, EF Core, and ASP.NET Core Identity.

## Status

Repository initialized. Application implementation has not started.

## Architecture

See [the architecture baseline](docs/architecture.md) for requirements, repository boundaries, and implementation milestones.

The backend owns domain rules, authentication, persistence, and the versioned OpenAPI contract. It builds and deploys independently of the native Android app.

Companion repository: [muscleforge-frontend](https://github.com/JaydenWev/muscleforge-frontend).
