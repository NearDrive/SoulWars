using Game.Core;
using Game.Protocol;

namespace Game.Server;

public static class ServerInvariants
{
    public static void Validate(ServerHostDebugView view)
    {
        EnsureWorldEntityIdsUnique(view.World, view.CurrentTick);
        EnsureWorldPositionsFinite(view.World, view.CurrentTick);

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

            if (session.EntityId.HasValue && !assignedEntityIds.Add(session.EntityId.Value))
            {
                throw new InvariantViolationException($"invariant=UniqueEntityPerSession sessionId={session.SessionId} entityId={session.EntityId.Value}");
            }

            if (session.LastSnapshotTick > view.CurrentTick)
            {
                throw new InvariantViolationException($"invariant=SessionSnapshotTickNotInFuture sessionId={session.SessionId} lastSnapshotTick={session.LastSnapshotTick} currentTick={view.CurrentTick}");
            }
        }

        foreach (Snapshot snapshot in view.Snapshots)
        {
            int lastEntityId = int.MinValue;
            foreach (SnapshotEntity entity in snapshot.Entities)
            {
                if (entity.EntityId < lastEntityId)
                {
                    throw new InvariantViolationException($"invariant=SnapshotEntitiesOrdered tick={snapshot.Tick} zoneId={snapshot.ZoneId} entityId={entity.EntityId} lastEntityId={lastEntityId}");
                }

                lastEntityId = entity.EntityId;
            }
        }

        if (view.CurrentTick < view.LastTick)
        {
            throw new InvariantViolationException($"invariant=ServerTickMonotonic currentTick={view.CurrentTick} lastTick={view.LastTick}");
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
}

public sealed record ServerHostDebugView(int LastTick, int CurrentTick, IReadOnlyList<ServerSessionDebugView> Sessions, IReadOnlyList<Snapshot> Snapshots, WorldState World);

public sealed record ServerSessionDebugView(int SessionId, int? EntityId, int LastSnapshotTick, int? ActiveZoneId);
