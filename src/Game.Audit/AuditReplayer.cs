using System.Collections.Immutable;
using Game.Core;

namespace Game.Audit;

public static class AuditReplayer
{
    public static WorldState Replay(SimulationConfig config, IEnumerable<AuditEvent> events, int finalTick)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (finalTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalTick));
        }

        List<AuditEvent> ordered = events
            .OrderBy(e => e.Header.Tick)
            .ThenBy(e => e.Header.Seq)
            .ToList();

        Dictionary<int, List<AuditEvent>> byTick = ordered
            .GroupBy(e => e.Header.Tick)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Header.Seq).ToList());

        WorldState world = Simulation.CreateInitialState(config);

        for (int tick = 1; tick <= finalTick; tick++)
        {
            List<WorldCommand> commands = new();
            if (byTick.TryGetValue(tick, out List<AuditEvent>? tickEvents))
            {
                foreach (AuditEvent evt in tickEvents)
                {
                    if (evt.Header.Type == AuditEventType.EnterZone)
                    {
                        commands.Add(new WorldCommand(WorldCommandKind.EnterZone, new EntityId(evt.EntityId), new ZoneId(evt.ZoneId)));
                    }
                    else if (evt.Header.Type == AuditEventType.Teleport)
                    {
                        commands.Add(new WorldCommand(WorldCommandKind.TeleportIntent, new EntityId(evt.EntityId), new ZoneId(evt.FromZoneId), ToZoneId: new ZoneId(evt.ToZoneId)));
                    }
                }
            }

            world = Simulation.Step(config, world, new Inputs(commands.ToImmutableArray()));

            if (byTick.TryGetValue(tick, out List<AuditEvent>? postTickEvents))
            {
                foreach (AuditEvent evt in postTickEvents)
                {
                    if (evt.Header.Type is AuditEventType.Death or AuditEventType.DespawnDisconnected)
                    {
                        world = RemoveEntity(world, evt.EntityId);
                    }
                }
            }
        }

        return world;
    }

    private static WorldState RemoveEntity(WorldState world, int entityId)
    {
        EntityId id = new(entityId);
        if (!world.TryGetEntityZone(id, out ZoneId zoneId) || !world.TryGetZone(zoneId, out ZoneState zone))
        {
            return world;
        }

        ZoneState updatedZone = zone.WithEntities(zone.Entities
            .Where(entity => entity.Id.Value != entityId)
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray());

        return world.WithZoneUpdated(updatedZone).WithoutEntityLocation(id);
    }
}
