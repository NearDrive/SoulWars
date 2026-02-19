# PROJECT STATUS SNAPSHOT — MVP1→MVP10

Data snapshot: 2026-02-19  
Criteri d'estat aplicat:

- ✅ **Completed**: evidència clara de feature + wiring + tests al repo; CI verd assumit com a font de veritat.
- 🟡 **Partial**: hi ha implementació parcial o desalineació explícita entre pla i implementació (wiring/test mapping incomplet o contracte no idèntic).
- ❌ **Missing**: no hi ha evidència localitzable.

---

## 1) Plans MVP detectats al repo (paths exactes)

- `docs/protocol/mmo_2d_ragnarokish_pr_plan (2).md` (PR-00..PR-10)
- `docs/protocol/MVP3_PR_PLAN (1).md` (PR-19..PR-23)
- `docs/protocol/MVP4_PR_PLAN.md` (PR-24..PR-28)
- `docs/protocol/SoulWars_MVP7_PR_PLAN (1).md` (PR-36..PR-42)
- `docs/protocol/SoulWars_MVP8_Drift_Guardrails (1).md` (PR-43..PR-46)
- `docs/protocol/MVP9_PR_PLAN (1).md` (PR-48..PR-52)
- `docs/protocol/SoulWars_MVP10_PR_PLAN.md` (legacy, PR-53..PR-57)
- `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (vigent, PR-54..PR-61)

Plans MVP no trobats:

- MVP5: cap pla `MVP5*_PR_PLAN*` / `MasterPlan` localitzat.
- MVP6: cap pla `MVP6*_PR_PLAN*` / `MasterPlan` localitzat.

---

## 2) Taula resum per MVP

| MVP | Total PRs | Completed | Partial | Missing | Notes |
|---|---:|---:|---:|---:|---|
| MVP1 (PR-00..09) | 10 | 8 | 2 | 0 | PR-04 (transport) i PR-10 (combat minimal com a extensió) queden parcialment desalineats del redactat original. |
| MVP2 (PR-10 opcional) | 1 | 0 | 1 | 0 | PR-10 existeix funcionalment però és contracte "opcional MVP2" no formalitzat en pla propi. |
| MVP3 (PR-19..23) | 5 | 5 | 0 | 0 | Save/Load, identitat, reconnect, sqlite, audit amb tests. |
| MVP4 (PR-24..28) | 5 | 5 | 0 | 0 | Hardening, observabilitat, soak, consistency gates coberts. |
| MVP5 | 0 | 0 | 0 | 0 | Sense pla oficial localitzat. |
| MVP6 | 0 | 0 | 0 | 0 | Sense pla oficial localitzat. |
| MVP7 (PR-36..42) | 7 | 7 | 0 | 0 | Zone defs, loot, inventory, penalty, vendor, anti-dupe coberts. |
| MVP8 (PR-43..46) | 4 | 4 | 0 | 0 | TickReport, replay artifacts, snapshot guardrails, migracions. |
| MVP9 (PR-48..52) | 5 | 5 | 0 | 0 | Multi-zona deterministic + transfer + AOI + checksums. |
| MVP10 (oficial PR-54..61) | 8 | 8 | 0 | 0 | Combat core complet amb targeting, AoE, status, windup, LoS. |

---

## 3) Llista detallada PR per PR (estat + evidència)

### MVP1 roadmap + MVP2 opcional

- PR-00 — ✅ Completed  
  Evidència: estructura de projectes `src/*`, `tests/*`, CI pipeline.  
  Tests/gates: `.github/workflows/ci.yml`, `tests/Game.Server.Tests/ServerWiringTests.cs`.
- PR-01 — ✅ Completed  
  Evidència: `src/Game.Core/SimulationConfig.cs`, `src/Game.Core/SimRng.cs`, `src/Game.Core/Fix32.cs`.  
  Tests: `tests/Game.Core.Tests/SimulationDeterminismTests.cs`.
- PR-02 — ✅ Completed  
  Evidència: `src/Game.Core/Physics2D.cs`, `src/Game.Core/Simulation.cs`.  
  Tests: `tests/Game.Core.Tests/CoreWiringTests.cs`, `tests/Game.Core.Tests/CoreInvariantsTests.cs`.
- PR-03 — ✅ Completed  
  Evidència: `src/Game.Server/ZoneManager.cs`, `src/Game.Server/ServerRuntime.cs`.  
  Tests: `tests/Game.Server.Tests/ZoneManagerRuntimeTests.cs`, `tests/Game.Core.Tests/ZoneTransferTests.cs`.
- PR-04 — 🟡 Partial  
  Evidència: transport funcional a `src/Game.Server/TcpServerTransport.cs` + protocol (`src/Game.Protocol/*`).  
  Tests: `tests/Game.Server.Tests/ServerHostIntegrationTests.cs`.  
  Motiu partial: el pla original parlava explícitament de WebSocket+JSON; implementació actual principal és TCP framing.
- PR-05 — ✅ Completed  
  Evidència: `src/Game.BotRunner/*` (BotClient, ScenarioRunner, ReplayRunner).  
  Tests: `tests/Game.Server.Tests/ScenarioRunnerTests.cs`, `tests/Game.Server.Tests/Scenarios/BotCombatScenarioTests.cs`.
- PR-06 — ✅ Completed  
  Evidència: replay I/O i verify (`src/Game.BotRunner/ReplayReader.cs`, `ReplayWriter.cs`, `ReplayRunner.cs`).  
  Tests: `tests/Game.Server.Tests/ReplayRunnerTests.cs`, canary replay tests.
- PR-07 — ✅ Completed  
  Evidència: sqlite persistence (`src/Game.Persistence.Sqlite/SqliteGameStore.cs`, `SqliteSchema.cs`).  
  Tests: `tests/Game.Server.Tests/SqlitePersistenceTests.cs`, `PersistenceConsistencyIntegrationTests.cs`.
- PR-08 — ✅ Completed  
  Evidència: observabilitat/metrics (`src/Game.Server/ServerMetrics.cs`, `PerfCounters.cs`, `LogJson.cs`).  
  Tests: `tests/Game.Server.Tests/StructuredObservabilityTests.cs`.
- PR-09 — ✅ Completed  
  Evidència: hardening (`src/Game.Server/DenyList.cs`, `ServerInvariants.cs`).  
  Tests: `tests/Game.Server.Tests/HardeningFuzzTests.cs`, `SecurityHardeningTests.cs`.
- PR-10 — 🟡 Partial (low confidence)  
  Evidència: combat i bots (`src/Game.Core/Combat/*`, `src/Game.Server/Scenarios/BotCombatScenario.cs`).  
  Tests: `tests/Game.Core.Tests/CombatSimulationTests.cs`, `tests/Game.Server.Tests/Scenarios/BotCombatScenarioTests.cs`.  
  Motiu partial: PR original era "opcional MVP2" amb contracte menys formalitzat.

### MVP3

- PR-19 — ✅ Completed — `src/Game.Persistence/WorldStateSerializer.cs`; tests `tests/Game.Core.Tests/WorldStateSerializerTests.cs`.
- PR-20 — ✅ Completed — player identity a `src/Game.Server/PlayerRegistry.cs`; tests `tests/Game.Server.Tests/PlayerIdentityTests.cs`.
- PR-21 — ✅ Completed — reconnect continuity a `src/Game.Server/ServerHost.cs`; tests `tests/Game.Server.Tests/ServerHostIntegrationTests.cs`.
- PR-22 — ✅ Completed — SQLite backing `src/Game.Persistence.Sqlite/*`; tests `tests/Game.Server.Tests/SqlitePersistenceTests.cs`.
- PR-23 — ✅ Completed — audit trail `src/Game.Audit/*`; tests `tests/Game.Server.Tests/AuditTrailTests.cs`.

### MVP4

- PR-24 — ✅ Completed — protocol/version handling `src/Game.Protocol/*`; tests `tests/Game.Server.Tests/ServerWiringTests.cs`.
- PR-25 — ✅ Completed — validation/rate-limit via server hardening; tests `HardeningFuzzTests.cs`, `SecurityHardeningTests.cs`.
- PR-26 — ✅ Completed — observabilitat estructurada; tests `StructuredObservabilityTests.cs`.
- PR-27 — ✅ Completed — soak deterministic; tests `SoakRunnerTests.cs` + CI job soak.
- PR-28 — ✅ Completed — invariants + persistence consistency; tests `PersistenceConsistencyIntegrationTests.cs`, `DoDGlobalValidationTests.cs`.

### MVP7

- PR-36 — ✅ Completed — manual zone defs loader `src/Game.Server/ZoneDefinitionsLoader.cs`; tests `ManualZoneDefinitionsTests.cs`.
- PR-37 — ✅ Completed — loot deterministic; tests `tests/Game.Server.Tests/LootServerTests.cs`, `tests/Game.Core.Tests/LootSimulationTests.cs`.
- PR-38 — ✅ Completed — inventory server-side; tests `tests/Game.Core.Tests/InventorySimulationTests.cs`.
- PR-39 — ✅ Completed — death penalty; tests `tests/Game.Core.Tests/PlayerDeathPenaltyTests.cs`.
- PR-40 — ✅ Completed (low confidence) — risk gradient per zones via definitions + runtime multi-zona; tests `ManualZoneDefinitionsTests.cs`, `ZoneManagerRuntimeTests.cs`.
- PR-41 — ✅ Completed — vendor stub; evidència `src/Game.Server/VendorDefinitionsLoader.cs`, `src/Game.Core/VendorModels.cs`; tests `tests/Game.Core.Tests/VendorSimulationTests.cs`.
- PR-42 — ✅ Completed — anti-dupe invariants; tests `tests/Game.Server.Tests/AntiDupeExtendedInvariantTests.cs`.

### MVP8

- PR-43 — ✅ Completed — tick diagnostics `src/Game.Core/TickReport.cs`; tests `tests/Game.Server.Tests/TickReportTests.cs`.
- PR-44 — ✅ Completed — replay mismatch artifacts; tests `tests/Game.Server.Tests/ReplayRunnerTests.cs`.
- PR-45 — ✅ Completed — snapshot guardrails `src/Game.Persistence.Sqlite/SnapshotChecksumMismatchException.cs`, `SnapshotMeta.cs`; tests `SqlitePersistenceTests.cs`, `PersistenceConsistencyIntegrationTests.cs`.
- PR-46 — ✅ Completed — snapshot migrations `src/Game.Persistence.Sqlite/Migrations/*`; fixtures `tests/Fixtures/SnapshotMigration/*`; tests `tests/Game.Core.Tests/WorldStateMigrationTests.cs`.

### MVP9

- PR-48 — ✅ Completed — zone manager/canonical order `src/Game.Server/ZoneManager.cs`; tests `tests/Game.Server.Tests/ZoneManagerRuntimeTests.cs`.
- PR-49 — ✅ Completed — hierarchical checksums `src/Game.Core/StateChecksum.cs`; tests `tests/Game.Core.Tests/HierarchicalChecksumTests.cs`.
- PR-50 — ✅ Completed — deterministic transfers; tests `tests/Game.Core.Tests/ZoneTransferTests.cs`, `tests/Game.Server.Tests/Mvp9/Mvp9InvariantsTests.cs`.
- PR-51 — ✅ Completed — multi-zone snapshot/restart; tests `tests/Game.Server.Tests/MultiZoneSnapshotTests.cs`.
- PR-52 — ✅ Completed — session zone subscription/AOI; evidència `src/Game.Server/IAoiProvider.cs`, `RadiusAoiProvider.cs`; tests `tests/Game.Server.Tests/AoiProviderTests.cs`, `MultiZoneRoutingTests.cs`, `AoiMvp9Tests.cs`.

### MVP10 oficial (MasterPlan PR-54..61)

- PR-54 — ✅ Completed — skill model + cooldown component (`src/Game.Core/Combat/Skills/SkillDefinition.cs`, `Components/SkillCooldownsComponent.cs`); tests `tests/Game.Core.Tests/Combat/SkillModelTests.cs`.
- PR-55 — ✅ Completed — cast command + validation (`src/Game.Core/Combat/Commands/CastSkillCommand.cs`, `Systems/SkillCastSystem.cs`); tests `CastSkillCommandTests.cs`, `CastSkillValidationTests.cs`.
- PR-56 — ✅ Completed — damage/events pipeline (`Components/DefenseStatsComponent.cs`, `Systems/SkillEffectSystem.cs`, `Log/CombatLogEvent.cs`); tests `DamageAndEventsTests.cs`.
- PR-57 — ✅ Completed — point targeting deterministic; tests `tests/Game.Core.Tests/Combat/PointTargetingTests.cs`.
- PR-58 — ✅ Completed — AoE + budgets; tests `tests/Game.Core.Tests/Combat/AoeTests.cs`, `CombatBudgetsTests.cs`.
- PR-59 — ✅ Completed — status effects; tests `tests/Game.Core.Tests/Combat/StatusEffectsTests.cs`.
- PR-60 — ✅ Completed — windup + cancel; tests `tests/Game.Core.Tests/Combat/CastWindupTests.cs`.
- PR-61 — ✅ Completed — LoS tile-based; codi `src/Game.Core/Combat/Targeting/LineOfSight.cs`; tests `tests/Game.Core.Tests/Combat/LineOfSightTests.cs`.

---

## 4) CI / verificació estructural utilitzada en el snapshot

- Workflow CI central amb jobs `build-and-test`, `dod-gate`, `soak`: `.github/workflows/ci.yml`.
- Gates explícits per `Category=DoD`, `Category=ReplayVerify`, `Category=Persistence`, `Category=Soak`.
- Canary multi-zona present a `tests/Game.Server.Tests/Canary/*`.

Aquesta combinació (features + wiring + suites + workflow gates) sustenta la classificació `Completed` sota l'assumpció operativa de CI verd.

---

## 5) Known Doc Drift / Conflicts

1. **MVP10 PR_PLAN vs MasterPlan:** numeració i scope desalineats.  
   Resolució oficial aplicada: MasterPlan (`PR-54..61`) vigent, PR_PLAN (`PR-53..57`) legacy.
2. **MVP5/MVP6:** buit de traçabilitat formal (cap pla localitzat).
3. **MVP1 PR-04:** pla original indica WebSocket+JSON; implementació actual principal és TCP framing (desalineació de contracte històric).

---

## 6) Next Pending Official PRs

Amb criteri de snapshot actual + CI verd assumit, **no hi ha PRs pendents** dins els plans oficials detectats (inclòs MVP10 MasterPlan PR-54..PR-61).

Pendent de governança (no PR de gameplay):

- Crear/normalitzar plans oficials MVP5 i MVP6 per tancar buit documental.
