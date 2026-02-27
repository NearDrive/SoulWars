namespace Game.Protocol;

public readonly record struct TickMetadataV1(int TickId, int ZoneId);

public sealed record SnapshotEnvelopeV1(TickMetadataV1 Metadata, SnapshotEntity[] Entities);

public sealed record DeltaEnvelopeV1(
    TickMetadataV1 Metadata,
    SnapshotEntity[] Updates,
    SpawnEventV1[] Spawns,
    DespawnEventV1[] Despawns);

public sealed record SpawnEventV1(int EntityId, SnapshotEntity Entity);

public sealed record DespawnEventV1(int EntityId);
