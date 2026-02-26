using System.Collections.Immutable;
using Game.Core;

namespace Game.Server;

public sealed class VisibilityAoiProvider : IAoiProvider
{
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
        bool hasVisibilityForViewerFaction = knownFactions.Any(faction => faction.Value == viewerFactionId.Value);
        bool fullZoneFallback = viewerFactionId == FactionId.None || !hasVisibilityForViewerFaction;

        ImmutableArray<EntityId> visible = zone.Entities
            .Where(entity =>
            {
                perfCounters?.CountAoiChecks(1);
                if (entity.Id.Value == viewerEntityId.Value)
                {
                    return true;
                }

                if (fullZoneFallback)
                {
                    return true;
                }

                int tileX = Fix32.FloorToInt(entity.Pos.X);
                int tileY = Fix32.FloorToInt(entity.Pos.Y);
                return zone.Visibility.IsVisible(viewerFactionId, tileX, tileY);
            })
            .Select(entity => entity.Id)
            .OrderBy(id => id.Value)
            .ToImmutableArray();

        return new VisibleSet(zoneId, visible);
    }
}
