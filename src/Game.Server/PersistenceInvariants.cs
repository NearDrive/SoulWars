using Game.Core;

namespace Game.Server;

public static class PersistenceInvariants
{
    public static void Validate(
        WorldState world,
        IReadOnlyList<PlayerState> players,
        int tick,
        int nextEntityId,
        int nextSessionId,
        int maxSeenSessionId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);

        HashSet<int> worldEntityIds = new();
        int maxEntityId = 0;

        foreach (ZoneState zone in world.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                if (!worldEntityIds.Add(entity.Id.Value))
                {
                    throw new InvariantViolationException($"invariant=GlobalEntityIdUnique tick={tick} entityId={entity.Id.Value}");
                }

                maxEntityId = Math.Max(maxEntityId, entity.Id.Value);
            }
        }

        if (nextEntityId <= maxEntityId)
        {
            throw new InvariantViolationException($"invariant=NextEntityIdAhead tick={tick} nextEntityId={nextEntityId} maxEntityId={maxEntityId}");
        }

        if (nextSessionId <= maxSeenSessionId)
        {
            throw new InvariantViolationException($"invariant=NextSessionIdAhead tick={tick} nextSessionId={nextSessionId} maxSessionId={maxSeenSessionId}");
        }

        HashSet<int> playerIds = new();
        HashSet<string> accountIds = new(StringComparer.Ordinal);

        foreach (PlayerState player in players)
        {
            if (!playerIds.Add(player.PlayerId.Value))
            {
                throw new InvariantViolationException($"invariant=UniquePlayerId tick={tick} playerId={player.PlayerId.Value}");
            }

            if (!accountIds.Add(player.AccountId))
            {
                throw new InvariantViolationException($"invariant=UniqueAccountId tick={tick} accountId={player.AccountId}");
            }

            if (player.EntityId is null)
            {
                continue;
            }

            if (!world.TryGetEntityZone(new EntityId(player.EntityId.Value), out ZoneId zoneId))
            {
                throw new InvariantViolationException($"invariant=NoOrphanPlayerEntity tick={tick} playerId={player.PlayerId.Value} entityId={player.EntityId.Value}");
            }

            if (player.ZoneId is int playerZoneId && playerZoneId != zoneId.Value)
            {
                throw new InvariantViolationException($"invariant=PlayerZoneMatchesWorld tick={tick} playerId={player.PlayerId.Value} entityId={player.EntityId.Value} playerZoneId={playerZoneId} worldZoneId={zoneId.Value}");
            }
        }
    }
}
