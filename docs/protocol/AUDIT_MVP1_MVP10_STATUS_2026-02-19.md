# SoulWars Auditoria objectiva MVP1→MVP10 (estat actual del repo)

Data d'auditoria: 2026-02-19

## Metodologia i límits

- Auditoria feta només amb evidència del repositori local (`docs/`, `src/`, `tests/`, `.github/workflows`).
- No s'ha inferit implementació sense wiring + tests visibles.
- **Límit crític:** no s'ha pogut executar tests localment perquè `dotnet` no està instal·lat a l'entorn (`bash: command not found: dotnet`).
- Criteri aplicat: si no es pot demostrar **implementat + integrat + tests passant**, estat = `🟡 Partial`.

---

## FASE 1 — MVP plans detectats

### Plans localitzats

- `docs/protocol/mmo_2d_ragnarokish_pr_plan (2).md` (PR-00..PR-10)
- `docs/protocol/MVP3_PR_PLAN (1).md` (PR-19..PR-23)
- `docs/protocol/MVP4_PR_PLAN.md` (PR-24..PR-28)
- `docs/protocol/SoulWars_MVP7_PR_PLAN (1).md` (PR-36..PR-42)
- `docs/protocol/SoulWars_MVP8_Drift_Guardrails (1).md` (PR-43..PR-46)
- `docs/protocol/MVP9_PR_PLAN (1).md` (PR-48..PR-52)
- `docs/protocol/SoulWars_MVP10_PR_PLAN.md` (PR-53..PR-57)
- `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (PR-54..PR-61)

### MVPs sense pla oficial trobat

- MVP5: no s'ha trobat cap `MVP5*_PR_PLAN.md`/`MasterPlan`.
- MVP6: no s'ha trobat cap `MVP6*_PR_PLAN.md`/`MasterPlan`.

### Definition of Done (DoD) i invariants globals detectats

- MVP3 DoD formal definit al pla MVP3.
- MVP4 DoD global checklist definit al pla MVP4.
- MVP7 DoD definit al pla MVP7.
- MVP8 DoD definit al document de drift guardrails.
- MVP9 completion criteria + invariants requerits definits al pla MVP9.
- MVP10 DoD definit a **dos** documents (PR_PLAN i MasterPlan), amb desalineació de numeració PRs.

---

## FASE 2 — Mapatge PR → implementació real

> Nota: **cap PR es marca Completed** perquè no s'ha pogut verificar tests passant localment.

### MVP1 (segons roadmap PR-00..)

- **PR-00** → 🟡 Partial
  - Evidència: existeixen projectes `src/*`, `tests/*`, CI amb build+test.
- **PR-01** → 🟡 Partial
  - Evidència: `SimulationConfig`, `SimRng`, tests de determinisme.
- **PR-02** → 🟡 Partial
  - Evidència: moviment/collisions al core + tests de wiring/core.
- **PR-03** → 🟡 Partial
  - Evidència: `ZoneId`, world multi-zona, enter/leave commands.
- **PR-04** → 🟡 Partial
  - Evidència parcial: protocol i transport existeixen, però el runtime usa `TcpServerTransport` (no evidència de WebSocket JSON exactes del pla original).
- **PR-05** → 🟡 Partial
  - Evidència: projecte `Game.BotRunner` + runners/tests.
- **PR-06** → 🟡 Partial
  - Evidència: Replay runner + tests `ReplayVerify`.
- **PR-07** → 🟡 Partial
  - Evidència: persistència SQLite + tests de persistència.
- **PR-08** → 🟡 Partial
  - Evidència: observabilitat estructurada + mètriques + tests.
- **PR-09** → 🟡 Partial
  - Evidència: hardening/fuzz/rate-limit tests.
- **PR-10** → 🟡 Partial
  - Evidència: combat core present, però el PR original era “opcional MVP2” i no hi ha traçabilitat exacta amb aquell contracte.

### MVP2

- Sense PR plan específic independent trobat.
- Només rastre indirecte al roadmap base (PR-10 opcional per MVP2).

### MVP3 (PR-19..23)

- **PR-19** → 🟡 Partial
  - Evidència: `WorldStateSerializer` + tests save/load deterministic.
- **PR-20** → 🟡 Partial
  - Evidència: `PlayerIdentityTests`.
- **PR-21** → 🟡 Partial
  - Evidència: reconnect/continuity cobert a tests server host identity/session.
- **PR-22** → 🟡 Partial
  - Evidència: `Game.Persistence.Sqlite` + tests sqlite/persistence.
- **PR-23** → 🟡 Partial
  - Evidència: `Game.Audit` + `AuditTrailTests`.

### MVP4 (PR-24..28)

- **PR-24** → 🟡 Partial
  - Evidència: protocol versioning + `DisconnectReason.VersionMismatch` + tests hardening.
- **PR-25** → 🟡 Partial
  - Evidència: validació inputs/rate limits + fuzz/security tests.
- **PR-26** → 🟡 Partial
  - Evidència: logs/metrics + `StructuredObservabilityTests`.
- **PR-27** → 🟡 Partial
  - Evidència: `SoakRunnerTests` + job `Category=Soak` a CI.
- **PR-28** → 🟡 Partial
  - Evidència: persistence consistency + invariants tests.

### MVP5

- Sense PR plan oficial trobat → estat PRs no auditable.

### MVP6

- Sense PR plan oficial trobat → estat PRs no auditable.

### MVP7 (PR-36..42)

- **PR-36** → 🟡 Partial
  - Evidència: zone definitions loaders + fixtures + tests manuals.
- **PR-37** → 🟡 Partial
  - Evidència: loot deterministic tests core/server.
- **PR-38** → 🟡 Partial
  - Evidència: inventory tests core.
- **PR-39** → 🟡 Partial
  - Evidència: death penalty tests.
- **PR-40** → 🟡 Partial
  - Evidència: multi-zone + spawn/risk via manual defs (indirecte).
- **PR-41** → 🟡 Partial
  - Evidència: vendor definitions/loader + vendor tests.
- **PR-42** → 🟡 Partial
  - Evidència: anti-dupe extended invariant tests.

### MVP8 (PR-43..46)

- **PR-43** → 🟡 Partial
  - Evidència: TickReport + tests.
- **PR-44** → 🟡 Partial
  - Evidència: ReplayRunner mismatch artifacts.
- **PR-45** → 🟡 Partial
  - Evidència: snapshot guardrails + checksum-on-load tests.
- **PR-46** → 🟡 Partial
  - Evidència: snapshot migration fixtures/tests v1/v2/v3.

### MVP9 (PR-48..52)

- **PR-48** → 🟡 Partial
  - Evidència: `ZoneManager`, iteració ordenada.
- **PR-49** → 🟡 Partial
  - Evidència: `ZoneChecksum` + `ComputeGlobalChecksum` + tests jeràrquics.
- **PR-50** → 🟡 Partial
  - Evidència: `ZoneTransferEvent` + tests transfer/replay/restart.
- **PR-51** → 🟡 Partial
  - Evidència: multi-zone snapshot tests.
- **PR-52** → 🟡 Partial
  - Evidència: AOI/subscription/routing tests.

### MVP10 (documents conflictius)

#### Bloc comú visible al codi

- skill model, cast command, targeting, damage, events, bot combat, AoE, status, windup, LoS, projectiles: **evidència present**.

#### PR_PLAN

- **PR-53** → 🟡 Partial
- **PR-54** → 🟡 Partial
- **PR-55** → 🟡 Partial
- **PR-56** → 🟡 Partial
- **PR-57** → 🟡 Partial

#### MasterPlan (offset + extensió)

- **PR-54** → 🟡 Partial
- **PR-55** → 🟡 Partial
- **PR-56** → 🟡 Partial
- **PR-57** → 🟡 Partial
- **PR-58** → 🟡 Partial
- **PR-59** → 🟡 Partial
- **PR-60** → 🟡 Partial
- **PR-61** → 🟡 Partial

---

## FASE 3 — Determinisme / guardrails

- ReplayVerify tests: **OK** (suites i category dedicades trobades)
- SnapshotVerify tests: **Risk** (no etiqueta exacta `SnapshotVerify`; hi ha snapshot roundtrip/integrity)
- Checksum global + per-zona: **OK**
- Ordre canònic zone/entity/commands: **OK**
- Absència float/time/random al simulation loop: **Risk**
  - Fix32 dominant + SimRng determinista; no s'ha vist `DateTime.Now`/`Random` al core loop, però sense execució no validable end-to-end.
- Budgets definits i aplicats: **OK**

---

## FASE 4 — Combat system status (MVP10)

- SkillDefinition: **OK** (tipus i camps presents)
- SkillCooldowns: **OK** (component + tickdown)
- CastSkillCommand: **OK** (protocol + world command + server enqueue)
- Targeting validation: **OK** (validació cast/target + tests)
- Damage pipeline: **OK**
- Defense stats: **OK** (combat stats/defense present)
- Combat events: **OK** (retenció + snapshot/checksum ordering)
- Budgets per combat: **OK**
- Bot combat scenario: **OK** (scenario + tests)
- Projectiles: **OK**
- LoS: **OK**

**Wiring real a `Simulation.Step`:** detectat processament de `CastSkill`, pending casts, intents, skill effects i projectiles dins el loop de tick.

---

## FASE 5 — Sortida estructurada

### 1) Taula resum MVP per MVP

| MVP | Total PRs | Completed | Partial | Missing |
|---|---:|---:|---:|---:|
| MVP1 (roadmap) | 10 (PR-00..09) | 0 | 10 | 0 |
| MVP2 | 1 opcional (PR-10) | 0 | 1 | 0 |
| MVP3 | 5 | 0 | 5 | 0 |
| MVP4 | 5 | 0 | 5 | 0 |
| MVP5 | 0 oficial trobat | 0 | 0 | 0 |
| MVP6 | 0 oficial trobat | 0 | 0 | 0 |
| MVP7 | 7 | 0 | 7 | 0 |
| MVP8 | 4 | 0 | 4 | 0 |
| MVP9 | 5 | 0 | 5 | 0 |
| MVP10 (PR_PLAN) | 5 | 0 | 5 | 0 |
| MVP10 (MasterPlan) | 8 | 0 | 8 | 0 |

### 2) Llista detallada PR per PR

Veure seccions de FASE 2 (PR-00 fins PR-61, només els definits en plans detectats).

### 3) Riscos detectats

1. No es poden marcar PRs com Completed sense execució real de tests en aquest entorn.
2. MVP10 té **drift documental**: PR numeració diferent entre `PR_PLAN` i `MasterPlan`.
3. MVP5/MVP6 sense plan oficial localitzat: buit de traçabilitat formal.

### 4) Drift risks potencials

1. Diferències entre plans (especialment MVP10) poden provocar validacions inconsistents.
2. Si el contracte de transport de MVP1 depenia estrictament de WebSocket+JSON, l'ús de TCP pot ser desviació de contracte original.
3. Sense execució local de replay suites, no es pot confirmar absència de drift en aquest entorn concret.

### 5) PRs oficials pendents ordenades per número

Segons criteri estricte de l'auditoria (cap PR verificat amb tests passant localment), **tots els PRs oficials detectats queden pendents de confirmació final**:

`PR-00, PR-01, PR-02, PR-03, PR-04, PR-05, PR-06, PR-07, PR-08, PR-09, PR-10, PR-19, PR-20, PR-21, PR-22, PR-23, PR-24, PR-25, PR-26, PR-27, PR-28, PR-36, PR-37, PR-38, PR-39, PR-40, PR-41, PR-42, PR-43, PR-44, PR-45, PR-46, PR-48, PR-49, PR-50, PR-51, PR-52, PR-53, PR-54, PR-55, PR-56, PR-57, PR-58, PR-59, PR-60, PR-61`.

