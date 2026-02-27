using System.Collections.Immutable;
using Game.Core;

namespace Game.Server;

public sealed class RadiusAoiProvider : IAoiProvider
{
    private readonly Fix32 _radiusSq;

    public RadiusAoiProvider(Fix32 radiusSq)
    {
        _radiusSq = radiusSq;
    }

    public VisibleSet ComputeVisible(in WorldState world, ZoneId zoneId, EntityId viewerEntityId, PerfCounters? perfCounters = null)
    {
        if (!world.TryGetZone(zoneId, out ZoneState zone))
        {
            return VisibleSet.Empty(zoneId);
        }

        EntityState? viewer = zone.Entities.FirstOrDefault(entity => entity.Id.Value == viewerEntityId.Value);
        if (viewer is null)
        {
            return VisibleSet.Empty(zoneId);
        }

        Vec2Fix viewerPos = viewer.Pos;
        ImmutableArray<EntityId> visible = zone.Entities
            .Where(entity =>
            {
                perfCounters?.CountAoiChecks(1);
                perfCounters?.CountAoiEntitiesConsidered(1);
                return entity.Id.Value == viewerEntityId.Value || IsWithinRadius(viewerPos, entity.Pos);
            })
            .Select(entity => entity.Id)
            .OrderBy(id => id.Value)
            .ToImmutableArray();

        return new VisibleSet(zoneId, visible);
    }

    private bool IsWithinRadius(Vec2Fix observerPos, Vec2Fix targetPos)
    {
        Vec2Fix delta = targetPos - observerPos;
        Fix32 distSq = delta.LengthSq();
        return distSq <= _radiusSq;
    }
}
