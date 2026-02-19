# MVP6 PR Plan — Canonical plan (reconstructed)

> This document is reconstructed from repository evidence; if conflicts appear, repo+CI wins.

## Scope reconstruction

Repository history around PR-34 and PR-35 (GitHub PR #40 and #41) shows MVP6 executed as infra-hardening work: deterministic SQLite migrations and abuse/security controls. No repository evidence was found for a distinct PR-33 implementation in current history.

## PR list

### PR-34 — SQLite migrations, schema versioning, deterministic upgrade path
- **Short scope:** enforce versioned schema and deterministic migration flow for persisted worlds.
- **Key paths touched:**
  - `src/Game.Persistence.Sqlite/SqliteSchema.cs`
  - `src/Game.Persistence.Sqlite/SqliteMigrator.cs`
  - `src/Game.Persistence.Sqlite/Migrations/V0_To_V1.cs`
  - `src/Game.Persistence.Sqlite/Migrations/V1_To_V2.cs`
  - `tests/Game.Server.Tests/SqlitePersistenceTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/SqlitePersistenceTests.cs`
  - `tests/Game.Core.Tests/WorldStateMigrationTests.cs`
  - CI `dotnet test --filter "Category=Persistence"`

### PR-35 — Security hardening (connection limits, flood guards, denylist, structured disconnect)
- **Short scope:** harden server edge behavior against malformed/abusive clients while preserving deterministic simulation behavior.
- **Key paths touched:**
  - `src/Game.Server/DenyList.cs`
  - `src/Game.Server/ServerConfig.cs`
  - `src/Game.Server/ServerHost.cs`
  - `src/Game.Server/TcpEndpoint.cs`
  - `tests/Game.Server.Tests/SecurityHardeningTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/SecurityHardeningTests.cs`
  - `tests/Game.Server.Tests/HardeningFuzzTests.cs`
  - CI `dotnet test --filter "Category=Security"`

## Traceability note for PR-33

- Git history and docs in this repository do not currently expose a stable PR-33 implementation marker.
- Governance interpretation used here: MVP6 in practice is represented by PR-34 and PR-35 evidence currently present in repo + CI gates.
