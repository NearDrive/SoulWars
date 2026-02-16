using System.Collections.Immutable;
using Game.Core;

namespace Game.Server;

public interface IAoiProvider
{
    VisibleSet ComputeVisible(in WorldState world, ZoneId zoneId, EntityId viewerEntityId);
}

public readonly record struct VisibleSet(ZoneId ZoneId, ImmutableArray<EntityId> EntityIds)
{
    public static VisibleSet Empty(ZoneId zoneId) => new(zoneId, ImmutableArray<EntityId>.Empty);
}
