using Game.Core;
using Game.Protocol;

namespace Game.Server;

internal readonly record struct SessionSnapshotContext(int SessionId, int? SelfEntityId, FactionId FactionId);

internal static class SnapshotRedactor
{
    public static SnapshotEntity[] RedactEntities(
        SessionSnapshotContext session,
        ZoneState zone,
        VisibilityGrid visibility,
        IReadOnlyCollection<EntityState> candidateEntities,
        IEnumerable<SnapshotEntity> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        HashSet<int> candidateIds = candidateEntities
            .Select(entity => entity.Id.Value)
            .ToHashSet();

        SnapshotEntity[] redacted = payload
            .Where(entity => candidateIds.Contains(entity.EntityId))
            .Where(entity => IsEntityVisibleToSession(session, zone, visibility, entity.EntityId))
            .OrderBy(entity => entity.EntityId)
            .ToArray();

        return redacted;
    }

    public static int[] RedactEntityIds(
        SessionSnapshotContext session,
        ZoneState zone,
        VisibilityGrid visibility,
        IEnumerable<int> payloadEntityIds)
    {
        ArgumentNullException.ThrowIfNull(payloadEntityIds);

        return payloadEntityIds
            .Where(entityId => IsEntityVisibleToSession(session, zone, visibility, entityId))
            .OrderBy(entityId => entityId)
            .ToArray();
    }

    private static bool IsEntityVisibleToSession(
        SessionSnapshotContext session,
        ZoneState zone,
        VisibilityGrid visibility,
        int entityId)
    {
        if (session.SelfEntityId is int selfEntityId && entityId == selfEntityId)
        {
            return true;
        }

        if (!TryGetEntity(zone, entityId, out EntityState entity))
        {
            return false;
        }

        if (session.FactionId == FactionId.None)
        {
            return true;
        }

        int tileX = Fix32.FloorToInt(entity.Pos.X);
        int tileY = Fix32.FloorToInt(entity.Pos.Y);
        return visibility.IsVisible(session.FactionId, tileX, tileY);
    }

    private static bool TryGetEntity(ZoneState zone, int entityId, out EntityState entity)
    {
        int index = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, new EntityId(entityId));
        if (index >= 0)
        {
            entity = zone.Entities[index];
            return true;
        }

        entity = null!;
        return false;
    }
}
