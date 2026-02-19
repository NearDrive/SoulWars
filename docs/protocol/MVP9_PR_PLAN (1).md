# SoulWars --- MVP9 PR PLAN

## Multi-Zone Deterministic Runtime + Zone Transfer

------------------------------------------------------------------------

# 🎯 MVP-9 OBJECTIVE

Extend the authoritative headless server to support:

-   Multiple simultaneous zones in the same process
-   Deterministic global tick ordering
-   Deterministic cross-zone entity transfer
-   Per-zone + global checksums
-   Snapshot roundtrip for multi-zone state
-   ReplayVerify stability across multi-zone scenarios

This MVP must preserve:

1.  Determinism
2.  Replay reproducibility
3.  Snapshot integrity
4.  CI-verifiable invariants

------------------------------------------------------------------------

# 🔒 GATING REQUIREMENTS

MVP-8 must already guarantee:

-   ReplayVerify deterministic
-   Snapshot checksum-on-load
-   Migration fixtures passing
-   Drift diagnostic tooling operational

If any of these regress → MVP-9 is blocked.

------------------------------------------------------------------------

# 🧱 ARCHITECTURE ASSUMPTIONS (REAL TYPES)

Based on current runtime:

-   `ServerRuntime`
-   `ServerHost`
-   `WorldState`
-   `Zone`
-   `ReplayVerify`
-   `TickReport`
-   `SnapshotMeta`
-   `Load()` / `SaveToSqlite()`
-   `WorldChecksum`

We extend these --- we do not refactor them.

------------------------------------------------------------------------

# 📦 PR-48 --- ZoneManager + Canonical Tick Order

## Goal

Introduce `ZoneManager` managing multiple `Zone` instances
deterministically.

## Changes

-   Add `ZoneManager`
    -   `SortedDictionary<int, Zone>` (ZoneId asc)
-   `ServerRuntime.StepOnce()` updated:
    -   Iterate zones in ascending ZoneId
    -   Step each zone deterministically

## Invariants

-   Tick order must be canonical
-   No unordered dictionary iteration

## Tests

-   `TwoZones_TickOrderDeterministic()`
-   `TwoRuns_MultiZone_SameChecksum()`

## DoD

-   Multi-zone runtime functional
-   No checksum drift
-   ReplayVerify still green

------------------------------------------------------------------------

# 📦 PR-49 --- Per-Zone + Global Checksum

## Goal

Introduce hierarchical checksum model.

## Add

-   `ZoneChecksum`
-   `GlobalChecksum = Hash(ZoneChecksums ordered by ZoneId)`

## Rules

-   Ordering must be canonical
-   No additional randomness

## Tests

-   `PerZoneChecksum_Stable()`
-   `GlobalChecksum_StableAcrossRuns()`
-   `ReplayVerify_MultiZone_Passes()`

## DoD

-   ReplayVerify supports multi-zone
-   No regression in single-zone mode

------------------------------------------------------------------------

# 📦 PR-50 --- Deterministic ZoneTransferEvent

## Goal

Allow entities to transfer between zones without duplication or drift.

## Add

`ZoneTransferEvent` { EntityId, FromZoneId, ToZoneId, SpawnPosition }

## Rules

-   Transfer applied at end-of-tick phase
-   Remove from source zone
-   Insert into target zone
-   Atomic within same tick

## Invariants

-   Entity exists in exactly 0 or 1 zones
-   No mid-tick duplication
-   Transfer queue deterministic ordering

## Tests

-   `ZoneTransfer_NoDuplicateInvariant()`
-   `ZoneTransfer_ReplayStable()`
-   `ZoneTransfer_RestartStable()`

## DoD

-   ReplayVerify stable after transfers
-   Snapshot mid-transfer restores correctly

------------------------------------------------------------------------

# 📦 PR-51 --- Multi-Zone Snapshot + Restart Integrity

## Goal

Extend snapshot system to persist multiple zones.

## Snapshot Changes

Snapshot now contains: - All zones - Per-zone checksum - Global
checksum - SnapshotMeta preserved

## Load Rules

-   Validate per-zone checksum
-   Validate global checksum
-   Fail-fast on mismatch

## Tests

-   `Snapshot_MultiZone_Roundtrip_ChecksumMatches()`
-   `Restart_FromMultiZone_NoDrift()`

## DoD

-   Restart does not alter entity IDs
-   Restart does not alter ordering
-   Restart checksum == non-restart checksum

------------------------------------------------------------------------

# 📦 PR-52 --- Session Zone Subscription (Minimal AOI)

## Goal

Support session-to-zone binding.

## Add

-   Session property: `SubscribedZoneId`
-   Host dispatch filters outbound messages by zone

## Constraints

-   No interest grid yet
-   Only exact-zone filtering
-   Deterministic iteration of sessions

## Tests

-   `Session_ReceivesOnlySubscribedZone()`
-   `ReplayVerify_MultiZone_WithSessions_Passes()`

## DoD

-   Snapshot dispatch deterministic
-   Multi-zone networking stable
-   No cross-zone leakage

------------------------------------------------------------------------

# 🧪 REQUIRED CI INVARIANTS

Add invariant tests:

-   `EntityId_UniqueAcrossZones()`
-   `ZoneIteration_IsCanonical()`
-   `TransferQueue_IsDeterministic()`
-   `GlobalChecksum_EqualsHashOfOrderedZoneChecksums()`

All must run in Release mode.

------------------------------------------------------------------------

# 🚫 OUT OF SCOPE (MVP-9)

-   Combat systems
-   Loot economy
-   Crafting
-   Client prediction
-   Load balancing across processes
-   Distributed zone sharding

------------------------------------------------------------------------

# ✅ MVP-9 COMPLETION CRITERIA

MVP-9 is complete when:

-   Multi-zone replay is stable
-   Cross-zone transfers deterministic
-   Snapshot roundtrip stable
-   Restart does not introduce ID drift
-   CI passes full deterministic matrix
-   No new drift vectors introduced

------------------------------------------------------------------------

# 📌 RESULT

After MVP-9, SoulWars becomes:

-   Deterministic across zones
-   Transfer-safe
-   Snapshot-safe at scale
-   Architecturally ready for combat & AI
