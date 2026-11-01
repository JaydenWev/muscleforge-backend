# Toolchain baseline

Checked: 2026-11-01

| Component | Pin |
| --- | --- |
| .NET SDK | 10.0.403, roll forward disabled |
| ASP.NET Core runtime/container | 10.0.14 |
| Microsoft EF Core / Identity packages and EF CLI | 10.0.14 |
| Npgsql EF Core provider | 10.0.3, EF Core 10 |
| PostgreSQL container | 17.11-alpine |
| xUnit v3 / VSTest adapter | 3.2.2 / 3.1.5 |
| Microsoft.NET.Test.Sdk | 18.10.0 |
| Python | 3.11+; CI uses 3.12 |

Package versions are centralized in `Directory.Packages.props`. Restore writes lockfiles; commit them after the first equipped restore. CI enforces existing lockfiles.

Official references:

- [.NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).
- [Npgsql package and dependencies](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3).
- [ASP.NET Core OpenAPI generation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0).
- [PostgreSQL releases](https://www.postgresql.org/docs/release/).

An actual restore/build has not run in the authoring workspace. Validate the first CI result before treating the toolchain as verified.
