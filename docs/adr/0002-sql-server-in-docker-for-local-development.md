# 2. SQL Server in Docker for local development

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Production runs on Azure SQL. Locally, the options were SQL Server in Docker or SQLite.

SQLite is lighter, but EF Core migrations are provider-specific. SQLite cannot run several `ALTER TABLE` operations that SQL Server can, and it handles types such as `datetime2` and `nvarchar(n)` differently. Migrations generated against SQLite would not be the ones applied in production.

## Decision

Use SQL Server 2022 in Docker (`docker-compose.yml`) for local development. On Apple silicon it runs as an `amd64` image under Rosetta, because no native `arm64` image exists.

The SQLite in-memory provider is still used in unit tests that need a real relational database but do not depend on SQL Server behaviour.

## Consequences

- The same migrations run locally, in CI and against Azure SQL.
- Docker Desktop with Rosetta enabled is a prerequisite. The container uses about 2 GB of memory.
- Tests that depend on SQL Server specifics, if any are needed later, should use Testcontainers rather than SQLite.
