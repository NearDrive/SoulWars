# SoulWars — MVP14 PR PLAN

## Visibility-Driven AOI & Network Redaction

Date: 2026-02-27

---

## 🎯 OBJECTIVE

Enforce server-authoritative visibility at the network layer.

After MVP13 (Fog of War), targeting is restricted server-side. MVP14 ensures snapshots, AOI routing, transition events, and entity payloads **do not leak invisible entities**.

This MVP closes the information leak vector between visibility logic and network output.

---

## 📦 MVP14 — PR-81..PR-87

---

## PR-81 — Visibility-Based AOI Provider

### Scope

- Implement `VisibilityAoiProvider`
- Replace/extend previous radius-only AOI selection with visibility-aware AOI
- AOI = entities visible to the session's faction
- Zone-local only

### Determinism Rules

- Canonical entity ordering (`EntityId` ascending)
- Canonical faction ordering
- No unordered iteration in AOI candidate aggregation

### Tests (Category=PR81)

- `VisibilityAoiProviderTests`
- `CrossFactionIsolationAoiTests`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR81 OR Category=Canary"
```

---

## PR-82 — Snapshot Redaction Layer

### Scope

- Filter entity payloads per session based on `VisibilityGrid`
- Invisible entities must not appear in snapshot or delta messages
- Preserve deterministic redaction ordering

### Invariants

- No partial entity state leaks
- No metadata leaks (`EntityId`, stats, HP, position)
- Redaction applied before outbound snapshot compose

### Tests (Category=PR82)

- `SnapshotRedactionTests`
- `EntityPayloadIsolationTests`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR82 OR Category=Canary"
```

---

## PR-83 — Deterministic Spawn/Despawn Transitions

### Scope

- When entity becomes visible -> deterministic spawn (`Enters`)
- When entity becomes invisible -> deterministic despawn (`Leaves`)
- No duplicate transitions / no flicker

### Determinism

- Stable transition detection
- Tick-based state comparison
- Canonical ordering for transition payloads

### Tests (Category=PR83)

- `VisibilityTransitionTests`
- `ReplayVerify_VisibilityTransitionScenario`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR83 OR Category=Canary"
```

---

## PR-84 — Network Fog Canary Scenario

### Scope

- Two factions in same zone
- Movement around LoS blockers
- Verify session A never receives entity state for invisible session B entity
- Restart mid-transition and verify deterministic continuity

### Tests (Category=PR84 + ReplayVerify + Canary)

- `FogNetworkReplayVerifyTests`
- `FogNetworkRestartDeterminismTests`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR84 OR Category=Canary"
```

---

## PR-85 — Visibility Performance Guardrails

### Scope

- Bound visibility/AOI CPU and allocations per tick
- Protect canonical ordering path from perf regressions
- Keep guardrails deterministic and CI-friendly

### Tests (Category=PR85)

- `Pr85VisibilityPerformanceGuardrailsTests`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR85 OR Category=Canary"
```

---

## PR-86 — Visibility Invariants Hardening

### Scope

- Harden no-leak invariant checks in stream validation
- Enforce spawn-before-state and despawn/respawn correctness
- Enforce canonical ordering and retransmit-safe semantics

### Tests (Category=PR86)

- `VisibilityNoLeakInvariantTests`
- `SpawnDespawnSequenceInvariantTests`
- `CanonicalOrderingInvariantTests`
- `VisibilityRetransmitInvariantTests`

### CI PR Lane

```bash
dotnet test -c Release --filter "Category=PR86 OR Category=Canary"
```

---

## PR-87 — End-to-End Documentation + Golden Replay Closure

### Scope

- Document full Fog of War transport pipeline and contracts
- Update MVP14 DoD with exact CI commands
- Add stable replay fixture `FogTransitionScenario` for ReplayVerify evidence

### Artifacts

- `docs/architecture/fog_of_war.md`
- `tests/Game.Server.Tests/Canary/Replays/fog_transition_scenario.json`
- `tests/Game.Server.Tests/Canary/FogTransitionGoldenReplayTests.cs`

---

## ✅ GLOBAL MVP14 DEFINITION OF DONE (Exact Checks)

### Canary lane (blocking)

```bash
dotnet test -c Release --filter "Category=Canary"
```

### PR-specific lanes

```bash
dotnet test -c Release --filter "Category=PR81 OR Category=Canary"
dotnet test -c Release --filter "Category=PR82 OR Category=Canary"
dotnet test -c Release --filter "Category=PR83 OR Category=Canary"
dotnet test -c Release --filter "Category=PR84 OR Category=Canary"
dotnet test -c Release --filter "Category=PR85 OR Category=Canary"
dotnet test -c Release --filter "Category=PR86 OR Category=Canary"
```

### ReplayVerify lane (blocking when enabled in gate)

```bash
dotnet test -c Release --filter "Category=ReplayVerify"
```

### Nightly soak lane (non-blocking / nightly)

```bash
dotnet test -c Release --filter "Category=Soak"
```

### Required invariants

- [x] AOI strictly respects visibility
- [x] Invisible entities (including `EntityId`) are never emitted
- [x] Deterministic spawn/despawn transitions
- [x] ReplayVerify green
- [x] Snapshot/restart safe for fog transitions
- [x] Visibility performance guardrails active
- [x] Invariant hardening active (`NoLeak`, `SpawnBeforeState`, `CanonicalOrder`)
- [x] Canary covers network-level fog behavior

---

## 🚫 Non-Goals

- Client-side rendering logic
- Minimap fog persistence
- Long-term exploration memory
- Runtime protocol redesign

---

## 🧠 Strategic Rationale

MVP13 enforced visibility at gameplay validation boundaries. MVP14 enforces the same guarantee at transport/output boundaries.

This closes the authority gap end-to-end and ensures:

- Packet-level no-leak guarantees.
- Deterministic replay verification for regressions.
- Stable CI guardrails for correctness + performance.

MVP14 is complete when all lanes above are green and the Fog transition replay evidence is stable.
