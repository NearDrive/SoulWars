# SoulWars — MVP14 PR PLAN

## Visibility-Driven AOI & Network Redaction

Date: 2026-02-26

---

## 🎯 OBJECTIVE

Enforce server-authoritative visibility at the network layer.

After MVP13 (Fog of War), targeting is restricted server-side. MVP14 ensures that snapshots, AOI routing, and entity payloads **do not leak invisible entities**.

This MVP closes the information leak vector between visibility logic and network output.

---

## 📦 MVP14 — PR-81..PR-85

---

## PR-81 — Visibility-Based AOI Provider

### Scope

- Implement `VisibilityAoiProvider`
- Replace/extend existing `RadiusAoiProvider`
- AOI = entities visible to the session's faction
- Zone-local only

### Determinism Rules

- Canonical entity ordering (`EntityId` ascending)
- Canonical faction ordering
- No unordered iteration

### Tests (Category=PR81)

- `VisibilityAoiProviderTests`
- `CrossFactionIsolationAoiTests`

### CI PR Lane

```bash
dotnet test --filter "Category=PR81 OR Category=Canary"
```

---

## PR-82 — Snapshot Redaction Layer

### Scope

- Filter entity payloads per session based on `VisibilityGrid`
- Invisible entities must not appear in snapshot or delta messages
- Ensure deterministic redaction ordering

### Invariants

- No partial entity state leaks
- No metadata leaks (stats, HP, position)

### Tests (Category=PR82)

- `SnapshotRedactionTests`
- `EntityPayloadIsolationTests`

### CI PR Lane

```bash
dotnet test --filter "Category=PR82 OR Category=Canary"
```

---

## PR-83 — Deterministic Spawn/Despawn Transitions

### Scope

- When entity becomes visible -> deterministic spawn event
- When entity becomes invisible -> deterministic despawn event
- No flicker or duplicate events

### Determinism

- Stable transition detection
- Tick-based state comparison

### Tests (Category=PR83)

- `VisibilityTransitionTests`
- `ReplayVerify_VisibilityTransitionScenario`

### CI PR Lane

```bash
dotnet test --filter "Category=PR83 OR Category=Canary"
```

---

## PR-84 — Network Fog Canary Scenario

### Scope

- Two factions in same zone
- Movement around LoS obstacles
- Verify session A never receives entity state for invisible session B entity
- Snapshot/restart mid-transition

### Tests (Category=PR84 + ReplayVerify + Canary)

- `FogNetworkReplayVerifyTests`
- `FogNetworkRestartDeterminismTests`

### CI PR Lane

```bash
dotnet test --filter "Category=PR84 OR Category=Canary"
```

---

## PR-85 — Snapshot & Multi-Zone Visibility Safety

### Scope

- Multi-zone routing respects visibility
- Snapshot/restart across zone transitions
- Ensure no cross-zone leakage

### Tests (Category=PR85)

- `MultiZoneVisibilityIsolationTests`
- `MultiZoneSnapshotRestartTests`

### CI PR Lane

```bash
dotnet test --filter "Category=PR85 OR Category=Canary"
```

---

## 🔒 GLOBAL MVP14 DEFINITION OF DONE

- [ ] AOI strictly respects visibility
- [ ] No invisible entity payload leaks
- [ ] Deterministic spawn/despawn transitions
- [ ] ReplayVerify green
- [ ] Snapshot/restart safe
- [ ] Multi-zone safe
- [ ] Canary covers network-level fog behavior

---

## 🚫 Non-Goals

- Client-side rendering logic
- Minimap fog persistence
- Long-term exploration memory
- Performance optimizations beyond deterministic correctness

---

## 🧠 Strategic Rationale

MVP13 enforced visibility at the gameplay layer. MVP14 enforces visibility at the transport layer.

This closes the final authority gap and ensures that:

- No cheating via packet inspection is possible.
- Deterministic replays remain valid.
- The server remains the single source of truth.

MVP14 completes the Fog of War system end-to-end.
