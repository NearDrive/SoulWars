# MVP9 Snapshot Contract Freeze

This document freezes the MVP9 snapshot contract and determinism ordering rules.

## SnapshotMeta

`SnapshotMeta` freezes the deterministic envelope we care about for replay/persistence comparisons:

- `Tick`: simulation tick for the emitted/loaded snapshot.
- `ZoneId`: source zone for the snapshot slice.
- `SnapshotSeq` (if present in protocol version): per-session sequence number.
- `PerZoneChecksum` (`ZoneChecksums[]` in tick reports): ordered checksums for each zone.
- `GlobalChecksum`: checksum aggregated from the ordered per-zone checksums.

Persistence-specific metadata that remains part of the stored snapshot contract:

- `SerializerVersion`: world serializer version used for snapshot encoding.
- `ZoneDefinitionsHash`: canonical hash of zone definitions (or world-derived fallback for legacy flow).
- `ConfigHash`: canonical hash of simulation config fields relevant to deterministic behavior.
- `BuildHash`: optional build identifier for traceability.

## Snapshot structure (server DTO + persistence)

- Outbound snapshot DTO (`Snapshot`/`SnapshotV2`):
  - `Tick`
  - `ZoneId`
  - `SnapshotSeq` (V2)
  - `Entities` (`SnapshotEntity[]`)
  - Diff arrays in V2 (`Leaves`, `Enters`, `Updates`)
- Persisted world snapshot:
  - `Tick`
  - `Zones[]`
  - `Zone.Entities[]`
  - optional world collections (loot, inventories, wallets, vendors, vendor audit)
- Tick report metadata:
  - `GlobalChecksum`
  - `ZoneChecksums[]`

## Canonical ordering (frozen)

- zones: `ZoneId` ascending.
- outbound sessions: `SessionId` ascending.
- entities inside snapshots: `EntityId` ascending.
- transfer queue: `(FromZoneId, ToZoneId, EntityId)` ascending.
- checksum aggregation: zones aggregated ordered by `ZoneId` ascending.

## Field determinism table

| Field | Meaning | Determinism notes |
|---|---|---|
| `Tick` | Simulation tick for snapshot/report | Must be monotonically increasing. |
| `ZoneId` | Zone identity of DTO slice | Always sorted globally for aggregation and snapshot routing. |
| `SnapshotSeq` | Per-session snapshot sequence (V2) | Strictly increasing per session; session iteration is by `SessionId asc`. |
| `Snapshot.Entities[]` | AOI entities visible to session | Must be serialized in `EntityId asc` to avoid nondeterministic output drift. |
| `Leaves/Enters/Updates` | Snapshot V2 delta arrays | Must be canonically sorted (`EntityId asc`). |
| `ZoneChecksums[]` | Per-zone checksum list in tick reports | Built in `ZoneId asc` before global aggregation/hash. |
| `GlobalChecksum` | Hash of ordered per-zone checksums | Deterministic only if zone ordering remains canonical. |
| `SnapshotMeta.*Hash` | Compatibility + replay/persistence guardrail hashes | Computed from canonicalized inputs, not insertion order. |

## Freeze policy

No protocol format changes are introduced by this freeze. Guardrails are enforced by:

1. invariant checks for canonical ordering and cross-zone uniqueness,
2. checksum mismatch fail-fast errors with expected/actual context,
3. canary golden replay verification in CI.
