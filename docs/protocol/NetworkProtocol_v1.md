# Network Protocol Surface v1 (PR-86)

Aquest document fixa la superfície client-facing de protocol per `ProtocolVersion = 1`.

## Handshake i versionat

- El client envia `HandshakeRequest(ProtocolVersion, AccountId)`.
- El servidor només accepta `ProtocolVersion == 1`.
- Versions desconegudes es rebutgen amb `Disconnect(VersionMismatch)` i no mantenen sessió activa.
- En acceptar, el servidor respon amb `Welcome(..., ProtocolVersion=1, ...)`.

## DTOs v1 client-facing

- `InputCommand`
  - `Tick`, `MoveX`, `MoveY` (moviment direccional; valors enters/sbyte)
- `CastSkillCommand` per `CastPoint`
  - `Tick`, `SkillId`, `ZoneId`, `TargetKind=Point`, `TargetPosXRaw`, `TargetPosYRaw`
  - No porta `TargetId` quan és `CastPoint`.

- `SnapshotEnvelopeV1`
  - `Metadata: TickMetadataV1(TickId, ZoneId)`
  - `Entities: SnapshotEntity[]`
- `DeltaEnvelopeV1` (envelope definit per compatibilitat)
  - `Metadata: TickMetadataV1(TickId, ZoneId)`
  - `Updates: SnapshotEntity[]`
  - `Spawns: SpawnEventV1[]`
  - `Despawns: DespawnEventV1[]`
- `SpawnEventV1`
  - `EntityId`, `Entity`
- `DespawnEventV1`
  - `EntityId`

## Regles de canonicitat i determinisme

- `Entities` ordenat per `EntityId asc`.
- Llistes de spawn/update ordenades per comparador estable:
  - `EntityId asc`, després tie-breaker complet de camps (`Kind`, `PosXRaw`, `PosYRaw`, `VelXRaw`, `VelYRaw`, `Hp`).
- `Despawns` ordenat per `EntityId asc`.
- No s'itera mai una `Dictionary/HashSet` directament cap al payload sense ordenar abans.
- La serialització binària del protocol és deterministic-friendly (sense dependència de locale/culture).
