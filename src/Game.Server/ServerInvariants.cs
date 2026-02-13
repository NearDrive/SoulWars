using Game.Protocol;
using Game.Core;

namespace Game.Server;

public static class ServerInvariants
{
    public static void Validate(ServerHostDebugView view)
    {
        int lastSessionId = 0;
        HashSet<int> assignedEntityIds = new();

        foreach (ServerSessionDebugView session in view.Sessions.OrderBy(s => s.SessionId))
        {
            if (session.SessionId <= lastSessionId)
            {
                throw new InvariantViolationException($"Session ids must be monotonic. sessionId={session.SessionId} last={lastSessionId}");
            }

            lastSessionId = session.SessionId;

            if (session.EntityId.HasValue && !assignedEntityIds.Add(session.EntityId.Value))
            {
                throw new InvariantViolationException($"Entity {session.EntityId.Value} assigned to more than one session.");
            }
        }

        foreach (Snapshot snapshot in view.Snapshots)
        {
            int lastEntityId = int.MinValue;
            foreach (Protocol.SnapshotEntity entity in snapshot.Entities)
            {
                if (entity.EntityId < lastEntityId)
                {
                    throw new InvariantViolationException($"Snapshot entities are not ordered by id at tick={snapshot.Tick}.");
                }

                lastEntityId = entity.EntityId;
            }
        }

        if (view.CurrentTick < view.LastTick)
        {
            throw new InvariantViolationException($"Tick is not monotonic. current={view.CurrentTick} last={view.LastTick}");
        }
    }
}

public sealed record ServerHostDebugView(int LastTick, int CurrentTick, IReadOnlyList<ServerSessionDebugView> Sessions, IReadOnlyList<Snapshot> Snapshots);

public sealed record ServerSessionDebugView(int SessionId, int? EntityId);
