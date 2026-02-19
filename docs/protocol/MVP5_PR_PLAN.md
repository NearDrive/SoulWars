# MVP5 PR Plan — Canonical plan (reconstructed)

> This document is reconstructed from repository evidence; if conflicts appear, repo+CI wins.

## Scope reconstruction

Repository history shows MVP5-era work landed through PR-29..PR-32 (GitHub PR #35..#38 merge chain) and focused on transport/session routing behavior, AOI filtering, snapshot sequencing, and deterministic perf budgeting.

## PR list

### PR-29 — Strict multi-zone session routing and zone-aware snapshots
- **Short scope:** route sessions per zone deterministically and enforce zone-aware snapshot behavior for enter/leave flows.
- **Key paths touched:**
  - `src/Game.Server/ServerHost.cs`
  - `src/Game.Protocol/Messages.cs`
  - `src/Game.Protocol/ProtocolCodec.cs`
  - `src/Game.BotRunner/BotClient.cs`
  - `tests/Game.Server.Tests/MultiZoneRoutingTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/MultiZoneRoutingTests.cs`
  - CI `dotnet test --filter "Category!=Soak"`

### PR-30 — Deterministic AOI visibility filtering
- **Short scope:** add per-session AOI filtering in deterministic order.
- **Key paths touched:**
  - `src/Game.Server/IAoiProvider.cs`
  - `src/Game.Server/RadiusAoiProvider.cs`
  - `src/Game.Server/ServerHost.cs`
  - `tests/Game.Server.Tests/AoiProviderTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/AoiProviderTests.cs`
  - `tests/Game.Server.Tests/AoiMvp9Tests.cs` (regression coverage)
  - CI `dotnet test --filter "Category!=Soak"`

### PR-31 — Snapshot sequencing + client ack + resend retry limits
- **Short scope:** harden snapshot delivery contract with sequence numbers and client acknowledgements.
- **Key paths touched:**
  - `src/Game.Protocol/Messages.cs`
  - `src/Game.Protocol/ProtocolCodec.cs`
  - `src/Game.Server/ServerHost.cs`
  - `src/Game.Server/ServerConfig.cs`
  - `tests/Game.Server.Tests/SnapshotSequencingTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/SnapshotSequencingTests.cs`
  - `tests/Game.Server.Tests/DoDGlobalValidationTests.cs` (`DoD_SnapshotSeq_Resend_OnDrop`)
  - CI `dotnet test --filter "Category=DoD"`

### PR-32 — Deterministic performance budgets + regression gate
- **Short scope:** add perf counters/budgets and CI-verifiable deterministic budget evaluation.
- **Key paths touched:**
  - `src/Game.Server/PerfBudgetConfig.cs`
  - `src/Game.Server/PerfBudgetEvaluator.cs`
  - `src/Game.Server/PerfSnapshot.cs`
  - `src/Game.Server/PerfCounters.cs`
  - `tests/Game.Server.Tests/PerfBudgetTests.cs`
- **Tests/gates that verify it:**
  - `tests/Game.Server.Tests/PerfBudgetTests.cs`
  - `tests/Game.Server.Tests/DoDGlobalValidationTests.cs` (`DoD_PerfBudgets_ReferenceScenario_WithinLimits`)
  - CI `dotnet test --filter "Category=DoD"`
