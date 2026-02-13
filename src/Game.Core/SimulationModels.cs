using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public readonly record struct ZoneId(int Value);

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
    int LastAttackTick);

public sealed record ZoneState(ZoneId Id, TileMap Map, ImmutableArray<EntityState> Entities)
{
    public ZoneState WithSortedEntities() => this with
    {
        Entities = Entities
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray()
    };
}

public sealed record WorldState(int Tick, ImmutableArray<ZoneState> Zones)
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
    AttackIntent = 4
}

public sealed record WorldCommand(
    WorldCommandKind Kind,
    EntityId EntityId,
    ZoneId ZoneId,
    sbyte MoveX = 0,
    sbyte MoveY = 0,
    Vec2Fix? SpawnPos = null,
    EntityId? TargetEntityId = null);

public sealed record Inputs(ImmutableArray<WorldCommand> Commands);
public sealed record PlayerInput(EntityId EntityId, sbyte MoveX, sbyte MoveY);
