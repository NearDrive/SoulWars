using Game.Protocol;
using Game.Core;

namespace Game.Server;

public static class ServerInvariants
{
    public static void Validate(ServerHostDebugView view)
    {
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
            foreach (Protocol.SnapshotEntity entity in snapshot.Entities)
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
}

public sealed record ServerHostDebugView(int LastTick, int CurrentTick, IReadOnlyList<ServerSessionDebugView> Sessions, IReadOnlyList<Snapshot> Snapshots);

public sealed record ServerSessionDebugView(int SessionId, int? EntityId, int LastSnapshotTick, int? ActiveZoneId);
