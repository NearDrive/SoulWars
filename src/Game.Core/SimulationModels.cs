using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public readonly record struct ZoneId(int Value);

public enum EntityKind : byte
{
    Player = 1,
    Npc = 2
}

public sealed record EntityState(
    EntityId Id,
    Vec2Fix Pos,
    Vec2Fix Vel,
    int MaxHp,
    int Hp,
    bool IsAlive,
    Fix32 AttackRange,
    int AttackDamage,
    int AttackCooldownTicks,
    int LastAttackTick,
    EntityKind Kind = EntityKind.Player,
    int NextWanderChangeTick = 0,
    sbyte WanderX = 0,
    sbyte WanderY = 0);

public sealed record ZoneState(ZoneId Id, TileMap Map, ImmutableArray<EntityState> Entities)
{
    public ZoneState WithSortedEntities() => this with
    {
        Entities = Entities
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray()
    };
}

public readonly record struct EntityLocation(EntityId Id, ZoneId ZoneId);

public sealed record WorldState(int Tick, ImmutableArray<ZoneState> Zones, ImmutableArray<EntityLocation> EntityLocations)
{
    public bool TryGetZone(ZoneId id, out ZoneState zone)
    {
        for (int i = 0; i < Zones.Length; i++)
        {
            if (Zones[i].Id.Value == id.Value)
            {
                zone = Zones[i];
                return true;
            }
        }

        zone = null!;
        return false;
    }


    public bool TryGetEntityZone(EntityId entityId, out ZoneId zoneId)
    {
        for (int i = 0; i < EntityLocations.Length; i++)
        {
            EntityLocation location = EntityLocations[i];
            if (location.Id.Value == entityId.Value)
            {
                zoneId = location.ZoneId;
                return true;
            }
        }

        zoneId = default;
        return false;
    }

    public WorldState WithEntityLocation(EntityId entityId, ZoneId zoneId)
    {
        ImmutableArray<EntityLocation>.Builder locations = ImmutableArray.CreateBuilder<EntityLocation>(EntityLocations.Length + 1);
        bool replaced = false;

        for (int i = 0; i < EntityLocations.Length; i++)
        {
            EntityLocation current = EntityLocations[i];
            if (current.Id.Value == entityId.Value)
            {
                locations.Add(new EntityLocation(entityId, zoneId));
                replaced = true;
            }
            else
            {
                locations.Add(current);
            }
        }

        if (!replaced)
        {
            locations.Add(new EntityLocation(entityId, zoneId));
        }

        return this with
        {
            EntityLocations = locations
                .OrderBy(l => l.Id.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithoutEntityLocation(EntityId entityId)
    {
        return this with
        {
            EntityLocations = EntityLocations
                .Where(location => location.Id.Value != entityId.Value)
                .OrderBy(location => location.Id.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithZoneUpdated(ZoneState zone)
    {
        ImmutableArray<ZoneState>.Builder zones = ImmutableArray.CreateBuilder<ZoneState>(Zones.Length);
        bool replaced = false;

        for (int i = 0; i < Zones.Length; i++)
        {
            if (Zones[i].Id.Value == zone.Id.Value)
            {
                zones.Add(zone.WithSortedEntities());
                replaced = true;
            }
            else
            {
                zones.Add(Zones[i].WithSortedEntities());
            }
        }

        if (!replaced)
        {
            zones.Add(zone.WithSortedEntities());
        }

        return this with
        {
            Zones = zones
                .OrderBy(z => z.Id.Value)
                .ToImmutableArray()
        };
    }
}

public enum WorldCommandKind : byte
{
    EnterZone = 1,
    LeaveZone = 2,
    MoveIntent = 3,
    AttackIntent = 4,
    TeleportIntent = 5
}

public sealed record WorldCommand(
    WorldCommandKind Kind,
    EntityId EntityId,
    ZoneId ZoneId,
    ZoneId? ToZoneId = null,
    sbyte MoveX = 0,
    sbyte MoveY = 0,
    Vec2Fix? SpawnPos = null,
    EntityId? TargetEntityId = null);

public sealed record Inputs(ImmutableArray<WorldCommand> Commands);
public sealed record PlayerInput(EntityId EntityId, sbyte MoveX, sbyte MoveY);
