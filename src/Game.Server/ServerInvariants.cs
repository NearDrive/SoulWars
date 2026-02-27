using Game.Core;
using Game.Protocol;

namespace Game.Server;

public static class ServerInvariants
{
    public static void Validate(ServerHostDebugView view)
    {
        EnsureWorldEntityIdsUnique(view.World, view.CurrentTick);
        EnsureWorldPositionsFinite(view.World, view.CurrentTick);
        WorldInvariants.AssertNoCrossZoneDupes(view.World, view.CurrentTick);

        int lastSessionId = 0;
        HashSet<int> sessionIds = new();
        HashSet<int> assignedEntityIds = new();

        foreach (ServerSessionDebugView session in view.Sessions.OrderBy(s => s.SessionId))
        {
            if (!sessionIds.Add(session.SessionId))
            {
                throw new InvariantViolationException($"invariant=UniqueSessionId sessionId={session.SessionId}");
            }

            if (session.SessionId <= lastSessionId)
            {
                throw new InvariantViolationException($"invariant=SessionIdsMonotonic sessionId={session.SessionId} lastSessionId={lastSessionId}");
            }

            lastSessionId = session.SessionId;

            if (session.EntityId.HasValue)
            {
                if (!assignedEntityIds.Add(session.EntityId.Value))
                {
                    throw new InvariantViolationException($"invariant=UniqueEntityPerSession sessionId={session.SessionId} entityId={session.EntityId.Value}");
                }

                EntityId sessionEntityId = new(session.EntityId.Value);
                if (!view.World.TryGetEntityZone(sessionEntityId, out ZoneId entityZoneId))
                {
                    throw new InvariantViolationException($"invariant=SessionEntityExists sessionId={session.SessionId} entityId={session.EntityId.Value}");
                }

                if (session.ActiveZoneId is int activeZoneId && activeZoneId != entityZoneId.Value)
                {
                    throw new InvariantViolationException($"invariant=SessionZoneMatchesWorld sessionId={session.SessionId} entityId={session.EntityId.Value} activeZoneId={activeZoneId} worldZoneId={entityZoneId.Value}");
                }
            }

            if (session.LastSnapshotTick > view.CurrentTick)
            {
                throw new InvariantViolationException($"invariant=SessionSnapshotTickNotInFuture sessionId={session.SessionId} lastSnapshotTick={session.LastSnapshotTick} currentTick={view.CurrentTick}");
            }
        }

        AssertCanonicalOrders(view.Snapshots, view.CurrentTick);

        PersistenceInvariants.Validate(
            view.World,
            view.Players,
            view.CurrentTick,
            view.NextEntityId,
            view.NextSessionId,
            maxSeenSessionId: lastSessionId);

        if (view.CurrentTick < view.LastTick)
        {
            throw new InvariantViolationException($"invariant=ServerTickMonotonic currentTick={view.CurrentTick} lastTick={view.LastTick}");
        }
    }


    private static void AssertCanonicalOrders(IReadOnlyList<Snapshot> snapshots, int tick)
    {
        WorldInvariants.AssertSortedAscending(
            snapshots,
            snapshot => snapshot.ZoneId,
            arrayName: "snapshots.zones",
            tick: tick);

        foreach (Snapshot snapshot in snapshots)
        {
            WorldInvariants.AssertSortedAscending(
                snapshot.Entities,
                entity => entity.EntityId,
                arrayName: "snapshot.entities",
                tick: snapshot.Tick,
                zoneId: snapshot.ZoneId);
        }
    }

    private static void EnsureWorldEntityIdsUnique(WorldState world, int tick)
    {
        HashSet<int> ids = new();
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                if (!ids.Add(entity.Id.Value))
                {
                    throw new InvariantViolationException($"invariant=UniqueEntityId tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value}");
                }
            }
        }
    }

    private static void EnsureWorldPositionsFinite(WorldState world, int tick)
    {
        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                if (!IsFinite(entity.Pos.X) || !IsFinite(entity.Pos.Y))
                {
                    throw new InvariantViolationException($"invariant=FinitePosition tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} posXRaw={entity.Pos.X.Raw} posYRaw={entity.Pos.Y.Raw}");
                }
            }
        }
    }

    private static bool IsFinite(Fix32 value) => value.Raw != int.MinValue && value.Raw != int.MaxValue;

    public static void ValidateManualZoneDefinitions(WorldState world, ZoneDefinitions definitions, int tick)
    {
        HashSet<int> zoneIds = definitions.Zones.Select(z => z.ZoneId.Value).ToHashSet();
        foreach (ZoneState zone in world.Zones)
        {
            if (!zoneIds.Contains(zone.Id.Value))
            {
                throw new InvariantViolationException($"invariant=ZoneWithoutDefinition tick={tick} zoneId={zone.Id.Value}");
            }
        }

        foreach (ZoneDefinition definition in definitions.Zones)
        {
            if (!world.TryGetZone(definition.ZoneId, out ZoneState zone))
            {
                throw new InvariantViolationException($"invariant=MissingZoneFromWorld tick={tick} zoneId={definition.ZoneId.Value}");
            }

            foreach (ZoneAabb obstacle in definition.StaticObstacles)
            {
                if (obstacle.HalfExtents.X.Raw <= 0 || obstacle.HalfExtents.Y.Raw <= 0)
                {
                    throw new InvariantViolationException($"invariant=ObstacleHalfExtentPositive tick={tick} zoneId={definition.ZoneId.Value}");
                }
            }

            foreach (NpcSpawnDefinition spawn in definition.NpcSpawns)
            {
                if (spawn.Count > spawn.SpawnPoints.Length)
                {
                    throw new InvariantViolationException($"invariant=NpcSpawnCountExceedsPoints tick={tick} zoneId={zone.Id.Value} archetype={spawn.NpcArchetypeId}");
                }

                for (int i = 0; i < spawn.Count; i++)
                {
                    Vec2Fix point = spawn.SpawnPoints[i];
                    if (!definition.Bounds.Contains(point))
                    {
                        throw new InvariantViolationException($"invariant=DefinedSpawnOutOfBounds tick={tick} zoneId={zone.Id.Value} archetype={spawn.NpcArchetypeId} index={i}");
                    }
                }
            }

            if (tick == 0)
            {
                HashSet<(int X, int Y)> allowedPoints = definition.NpcSpawns
                    .SelectMany(s => s.SpawnPoints.Take(s.Count))
                    .Select(p => (p.X.Raw, p.Y.Raw))
                    .ToHashSet();

                foreach (EntityState npc in zone.Entities.Where(e => e.Kind == EntityKind.Npc))
                {
                    if (!definition.Bounds.Contains(npc.Pos))
                    {
                        throw new InvariantViolationException($"invariant=NpcOutOfBoundsAtSpawn tick={tick} zoneId={zone.Id.Value} entityId={npc.Id.Value}");
                    }

                    if (!allowedPoints.Contains((npc.Pos.X.Raw, npc.Pos.Y.Raw)))
                    {
                        throw new InvariantViolationException($"invariant=NpcOutsideDefinedSpawn tick={tick} zoneId={zone.Id.Value} entityId={npc.Id.Value}");
                    }
                }
            }
        }
    }

    public static void ValidateVisibilityStreamInvariants(
        IReadOnlyList<SnapshotV2> snapshots,
        Func<int, IReadOnlySet<int>> visibleEntityIdsByTick,
        int sessionId)
    {
        Dictionary<int, VisibilityLifecycleState> lifecycleByEntityId = new();
        HashSet<int> seenTicks = new();
        int previousTick = int.MinValue;

        foreach (SnapshotV2 snapshot in snapshots)
        {
            EnsureTickUniqueAndMonotonic(snapshot.Tick, seenTicks, ref previousTick, sessionId);
            AssertCanonicalOrdering(snapshot, sessionId);

            IReadOnlySet<int> expectedVisible = visibleEntityIdsByTick(snapshot.Tick);
            AssertNoLeak(snapshot, expectedVisible, sessionId);

            foreach (int entityId in snapshot.Leaves)
            {
                lifecycleByEntityId[entityId] = VisibilityLifecycleState.RequiresSpawn;
            }

            foreach (SnapshotEntity entering in snapshot.Enters)
            {
                lifecycleByEntityId[entering.EntityId] = VisibilityLifecycleState.Visible;
            }

            foreach (SnapshotEntity entity in snapshot.Entities)
            {
                AssertSpawnBeforeState(entity.EntityId, lifecycleByEntityId, sessionId, snapshot.Tick, "entities");
            }

            foreach (SnapshotEntity entity in snapshot.Updates)
            {
                AssertSpawnBeforeState(entity.EntityId, lifecycleByEntityId, sessionId, snapshot.Tick, "updates");
            }
        }
    }

    private static void EnsureTickUniqueAndMonotonic(int tick, HashSet<int> seenTicks, ref int previousTick, int sessionId)
    {
        if (!seenTicks.Add(tick))
        {
            throw new InvariantViolationException($"invariant=VisibilityStreamTickUnique sessionId={sessionId} tick={tick}");
        }

        if (previousTick != int.MinValue && tick <= previousTick)
        {
            throw new InvariantViolationException($"invariant=VisibilityStreamTickMonotonic sessionId={sessionId} tick={tick} previousTick={previousTick}");
        }

        previousTick = tick;
    }

    private static void AssertCanonicalOrdering(SnapshotV2 snapshot, int sessionId)
    {
        WorldInvariants.AssertSortedAscending(snapshot.Entities, entity => entity.EntityId, "snapshot.entities", snapshot.Tick, snapshot.ZoneId);
        WorldInvariants.AssertSortedAscending(snapshot.Enters, entity => entity.EntityId, "snapshot.enters", snapshot.Tick, snapshot.ZoneId);
        WorldInvariants.AssertSortedAscending(snapshot.Updates, entity => entity.EntityId, "snapshot.updates", snapshot.Tick, snapshot.ZoneId);
        WorldInvariants.AssertSortedAscending(snapshot.Leaves, entityId => entityId, "snapshot.leaves", snapshot.Tick, snapshot.ZoneId);

        HashSet<int> entityIds = snapshot.Entities.Select(entity => entity.EntityId).ToHashSet();
        HashSet<int> enterIds = snapshot.Enters.Select(entity => entity.EntityId).ToHashSet();
        foreach (int enterId in enterIds)
        {
            if (!entityIds.Contains(enterId))
            {
                throw new InvariantViolationException($"invariant=SpawnMustAppearInEntities sessionId={sessionId} tick={snapshot.Tick} zoneId={snapshot.ZoneId} entityId={enterId}");
            }
        }
    }

    private static void AssertNoLeak(SnapshotV2 snapshot, IReadOnlySet<int> expectedVisible, int sessionId)
    {
        foreach (SnapshotEntity entity in snapshot.Entities)
        {
            if (!expectedVisible.Contains(entity.EntityId))
            {
                throw new InvariantViolationException($"invariant=NoLeakInvariant sessionId={sessionId} tick={snapshot.Tick} zoneId={snapshot.ZoneId} source=entities entityId={entity.EntityId}");
            }
        }

        foreach (SnapshotEntity entity in snapshot.Enters)
        {
            if (!expectedVisible.Contains(entity.EntityId))
            {
                throw new InvariantViolationException($"invariant=NoLeakInvariant sessionId={sessionId} tick={snapshot.Tick} zoneId={snapshot.ZoneId} source=enters entityId={entity.EntityId}");
            }
        }

        foreach (SnapshotEntity entity in snapshot.Updates)
        {
            if (!expectedVisible.Contains(entity.EntityId))
            {
                throw new InvariantViolationException($"invariant=NoLeakInvariant sessionId={sessionId} tick={snapshot.Tick} zoneId={snapshot.ZoneId} source=updates entityId={entity.EntityId}");
            }
        }
    }

    private static void AssertSpawnBeforeState(
        int entityId,
        IDictionary<int, VisibilityLifecycleState> lifecycleByEntityId,
        int sessionId,
        int tick,
        string source)
    {
        if (!lifecycleByEntityId.TryGetValue(entityId, out VisibilityLifecycleState state) || state != VisibilityLifecycleState.Visible)
        {
            throw new InvariantViolationException($"invariant=SpawnBeforeStateInvariant sessionId={sessionId} tick={tick} source={source} entityId={entityId}");
        }
    }

    private enum VisibilityLifecycleState : byte
    {
        RequiresSpawn = 0,
        Visible = 1
    }
}

public sealed record ServerHostDebugView(
    int LastTick,
    int CurrentTick,
    int NextSessionId,
    int NextEntityId,
    IReadOnlyList<ServerSessionDebugView> Sessions,
    IReadOnlyList<PlayerState> Players,
    IReadOnlyList<Snapshot> Snapshots,
    WorldState World);

public sealed record ServerSessionDebugView(int SessionId, int? EntityId, int LastSnapshotTick, int? ActiveZoneId);
