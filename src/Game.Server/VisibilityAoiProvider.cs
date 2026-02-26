using System.Collections.Immutable;
using Game.Core;

namespace Game.Server;

public sealed class VisibilityAoiProvider : IAoiProvider
{
    private readonly Fix32 _fallbackRadiusSq;

    public VisibilityAoiProvider()
        : this(Fix32.Zero)
    {
    }

    public VisibilityAoiProvider(Fix32 fallbackRadiusSq)
    {
        _fallbackRadiusSq = fallbackRadiusSq;
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

        FactionId viewerFactionId = viewer.FactionId;
        ImmutableArray<FactionId> knownFactions = zone.Visibility.GetFactionIdsOrdered();
        bool useVisibility = viewerFactionId != FactionId.None && knownFactions.Any(faction => faction.Value == viewerFactionId.Value);

        ImmutableArray<EntityId> visible = zone.Entities
            .Where(entity =>
            {
                perfCounters?.CountAoiChecks(1);
                if (entity.Id.Value == viewerEntityId.Value)
                {
                    return true;
                }

                if (useVisibility)
                {
                    int tileX = Fix32.FloorToInt(entity.Pos.X);
                    int tileY = Fix32.FloorToInt(entity.Pos.Y);
                    return zone.Visibility.IsVisible(viewerFactionId, tileX, tileY);
                }

                return IsWithinFallbackRadius(viewer.Pos, entity.Pos);
            })
            .Select(entity => entity.Id)
            .OrderBy(id => id.Value)
            .ToImmutableArray();

        return new VisibleSet(zoneId, visible);
    }

    private bool IsWithinFallbackRadius(Vec2Fix observerPos, Vec2Fix targetPos)
    {
        Vec2Fix delta = targetPos - observerPos;
        Fix32 distSq = delta.LengthSq();
        return distSq <= _fallbackRadiusSq;
    }
}
