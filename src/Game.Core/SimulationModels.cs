using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public readonly record struct ZoneId(int Value);

public readonly record struct SkillId(int Value);

public enum CastTargetKind : byte
{
    Self = 1,
    Entity = 2,
    Point = 3
}

public enum CastResult : byte
{
    Ok = 0,
    Rejected_NoSuchCaster = 1,
    Rejected_NoSuchSkill = 2,
    Rejected_OnCooldown = 3,
    Rejected_NotEnoughResource = 4,
    Rejected_InvalidTarget = 5,
    Rejected_OutOfRange = 6
}

public readonly record struct SkillDefinition(
    SkillId Id,
    int RangeQRaw,
    int CooldownTicks,
    int ResourceCost,
    CastTargetKind TargetKind);

public enum EntityKind : byte
{
    Player = 1,
    Npc = 2
}

public readonly record struct ComponentMask(uint Bits)
{
    public const uint PositionBit = 1u << 0;
    public const uint HealthBit = 1u << 1;
    public const uint CombatBit = 1u << 2;
    public const uint AiBit = 1u << 3;

    public bool Has(uint bit) => (Bits & bit) != 0;
}

public readonly record struct PositionComponent(Vec2Fix Pos, Vec2Fix Vel);
public readonly record struct HealthComponent(int MaxHp, int Hp, bool IsAlive);
public readonly record struct CombatComponent(Fix32 Range, int Damage, int CooldownTicks, int LastAttackTick);
public readonly record struct AiComponent(int NextWanderChangeTick, sbyte WanderX, sbyte WanderY);

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

public sealed record ZoneEntities(
    ImmutableArray<EntityId> AliveIds,
    ImmutableArray<ComponentMask> Masks,
    ImmutableArray<EntityKind> Kinds,
    ImmutableArray<PositionComponent> Positions,
    ImmutableArray<HealthComponent> Health,
    ImmutableArray<CombatComponent> Combat,
    ImmutableArray<AiComponent> Ai)
{
    public static ZoneEntities Empty => new(
        ImmutableArray<EntityId>.Empty,
        ImmutableArray<ComponentMask>.Empty,
        ImmutableArray<EntityKind>.Empty,
        ImmutableArray<PositionComponent>.Empty,
        ImmutableArray<HealthComponent>.Empty,
        ImmutableArray<CombatComponent>.Empty,
        ImmutableArray<AiComponent>.Empty);

    public static int FindIndex(ImmutableArray<EntityId> ids, EntityId id)
    {
        int lo = 0;
        int hi = ids.Length - 1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            int cmp = ids[mid].Value.CompareTo(id.Value);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    public bool HasComponent(int index, uint bit) => Masks[index].Has(bit);

    public ImmutableArray<EntityState> ToEntityStates()
    {
        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(AliveIds.Length);
        for (int i = 0; i < AliveIds.Length; i++)
        {
            PositionComponent position = Positions[i];
            HealthComponent health = Health[i];
            CombatComponent combat = Combat[i];
            AiComponent ai = Ai[i];
            builder.Add(new EntityState(
                Id: AliveIds[i],
                Pos: position.Pos,
                Vel: position.Vel,
                MaxHp: health.MaxHp,
                Hp: health.Hp,
                IsAlive: health.IsAlive,
                AttackRange: combat.Range,
                AttackDamage: combat.Damage,
                AttackCooldownTicks: combat.CooldownTicks,
                LastAttackTick: combat.LastAttackTick,
                Kind: Kinds[i],
                NextWanderChangeTick: ai.NextWanderChangeTick,
                WanderX: ai.WanderX,
                WanderY: ai.WanderY));
        }

        return builder.MoveToImmutable();
    }

    public static ZoneEntities FromEntityStates(ImmutableArray<EntityState> entities)
    {
        ImmutableArray<EntityState> ordered = entities
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();

        ImmutableArray<EntityId>.Builder ids = ImmutableArray.CreateBuilder<EntityId>(ordered.Length);
        ImmutableArray<ComponentMask>.Builder masks = ImmutableArray.CreateBuilder<ComponentMask>(ordered.Length);
        ImmutableArray<EntityKind>.Builder kinds = ImmutableArray.CreateBuilder<EntityKind>(ordered.Length);
        ImmutableArray<PositionComponent>.Builder positions = ImmutableArray.CreateBuilder<PositionComponent>(ordered.Length);
        ImmutableArray<HealthComponent>.Builder health = ImmutableArray.CreateBuilder<HealthComponent>(ordered.Length);
        ImmutableArray<CombatComponent>.Builder combat = ImmutableArray.CreateBuilder<CombatComponent>(ordered.Length);
        ImmutableArray<AiComponent>.Builder ai = ImmutableArray.CreateBuilder<AiComponent>(ordered.Length);

        foreach (EntityState entity in ordered)
        {
            uint bits = ComponentMask.PositionBit;
            bits |= ComponentMask.HealthBit;
            bits |= ComponentMask.CombatBit;
            if (entity.Kind == EntityKind.Npc)
            {
                bits |= ComponentMask.AiBit;
            }

            ids.Add(entity.Id);
            masks.Add(new ComponentMask(bits));
            kinds.Add(entity.Kind);
            positions.Add(new PositionComponent(entity.Pos, entity.Vel));
            health.Add(new HealthComponent(entity.MaxHp, entity.Hp, entity.IsAlive));
            combat.Add(new CombatComponent(entity.AttackRange, entity.AttackDamage, entity.AttackCooldownTicks, entity.LastAttackTick));
            ai.Add(entity.Kind == EntityKind.Npc
                ? new AiComponent(entity.NextWanderChangeTick, entity.WanderX, entity.WanderY)
                : default);
        }

        return new ZoneEntities(
            ids.MoveToImmutable(),
            masks.MoveToImmutable(),
            kinds.MoveToImmutable(),
            positions.MoveToImmutable(),
            health.MoveToImmutable(),
            combat.MoveToImmutable(),
            ai.MoveToImmutable());
    }
}

public sealed record ZoneState(ZoneId Id, TileMap Map, ZoneEntities EntitiesData)
{
    public ZoneState(ZoneId Id, TileMap Map, ImmutableArray<EntityState> Entities)
        : this(Id, Map, ZoneEntities.FromEntityStates(Entities))
    {
    }

    public ImmutableArray<EntityState> Entities => EntitiesData.ToEntityStates();

    public ZoneState WithEntities(ImmutableArray<EntityState> entities) => this with
    {
        EntitiesData = ZoneEntities.FromEntityStates(entities)
    };

    public ZoneState WithSortedEntities() => this with
    {
        EntitiesData = ZoneEntities.FromEntityStates(Entities)
    };

    public ZoneChecksum ComputeChecksum() => new(Id.Value, StateChecksum.ComputeZoneChecksum(this));
}

public readonly record struct EntityLocation(EntityId Id, ZoneId ZoneId);

public readonly record struct ZoneTransferEvent(
    int FromZoneId,
    int ToZoneId,
    EntityId EntityId,
    Vec2Fix Position,
    uint Reason,
    int Tick);

public sealed record ItemStack(string ItemId, int Quantity);

public static class InventoryConstants
{
    public const int DefaultCapacity = 20;
    public const int DefaultStackLimit = 99;
}

public sealed record InventorySlot(string ItemId, int Quantity)
{
    public bool IsEmpty => Quantity == 0;
}

public sealed record InventoryComponent(int Capacity, int StackLimit, ImmutableArray<InventorySlot> Slots)
{
    public static InventoryComponent CreateDefault()
    {
        ImmutableArray<InventorySlot>.Builder slots = ImmutableArray.CreateBuilder<InventorySlot>(InventoryConstants.DefaultCapacity);
        for (int i = 0; i < InventoryConstants.DefaultCapacity; i++)
        {
            slots.Add(new InventorySlot(string.Empty, 0));
        }

        return new InventoryComponent(InventoryConstants.DefaultCapacity, InventoryConstants.DefaultStackLimit, slots.MoveToImmutable());
    }
}

public sealed record PlayerInventoryState(EntityId EntityId, InventoryComponent Inventory);

public sealed record LootEntityState(
    EntityId Id,
    ZoneId ZoneId,
    Vec2Fix Pos,
    ImmutableArray<ItemStack> Items);

public sealed record PlayerDeathAuditEntry(
    int Tick,
    EntityId PlayerEntityId,
    ZoneId ZoneId,
    Vec2Fix DeathPosition,
    ImmutableArray<ItemStack> DroppedItems,
    EntityId LootEntityId,
    Vec2Fix RespawnPosition);

public sealed record WorldState(
    int Tick,
    ImmutableArray<ZoneState> Zones,
    ImmutableArray<EntityLocation> EntityLocations,
    ImmutableArray<LootEntityState> LootEntities = default,
    ImmutableArray<PlayerInventoryState> PlayerInventories = default,
    ImmutableArray<PlayerDeathAuditEntry> PlayerDeathAuditLog = default,
    ImmutableArray<PlayerWalletState> PlayerWallets = default,
    ImmutableArray<VendorTransactionAuditEntry> VendorTransactionAuditLog = default,
    ImmutableArray<VendorDefinition> Vendors = default)
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

    public WorldState WithLootEntities(ImmutableArray<LootEntityState> lootEntities)
    {
        return this with
        {
            LootEntities = lootEntities
                .OrderBy(l => l.Id.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithPlayerInventories(ImmutableArray<PlayerInventoryState> playerInventories)
    {
        return this with
        {
            PlayerInventories = playerInventories
                .OrderBy(i => i.EntityId.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithPlayerDeathAuditLog(ImmutableArray<PlayerDeathAuditEntry> playerDeathAuditLog)
    {
        return this with
        {
            PlayerDeathAuditLog = playerDeathAuditLog
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.PlayerEntityId.Value)
                .ThenBy(e => e.LootEntityId.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithPlayerWallets(ImmutableArray<PlayerWalletState> playerWallets)
    {
        return this with
        {
            PlayerWallets = playerWallets
                .OrderBy(w => w.EntityId.Value)
                .ToImmutableArray()
        };
    }

    public WorldState WithVendorTransactionAuditLog(ImmutableArray<VendorTransactionAuditEntry> vendorTransactionAuditLog)
    {
        return this with
        {
            VendorTransactionAuditLog = vendorTransactionAuditLog
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.PlayerEntityId.Value)
                .ThenBy(e => e.ZoneId.Value)
                .ThenBy(e => e.VendorId, StringComparer.Ordinal)
                .ThenBy(e => (int)e.Action)
                .ThenBy(e => e.ItemId, StringComparer.Ordinal)
                .ThenBy(e => e.Quantity)
                .ToImmutableArray()
        };
    }

    public WorldState WithVendors(ImmutableArray<VendorDefinition> vendors)
    {
        return this with
        {
            Vendors = vendors
                .OrderBy(v => v.ZoneId.Value)
                .ThenBy(v => v.VendorId, StringComparer.Ordinal)
                .Select(v => v with { Offers = v.CanonicalOffers })
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
    TeleportIntent = 5,
    LootIntent = 6,
    VendorBuyIntent = 7,
    VendorSellIntent = 8,
    CastSkill = 9
}

public sealed record WorldCommand(
    WorldCommandKind Kind,
    EntityId EntityId,
    ZoneId ZoneId,
    ZoneId? ToZoneId = null,
    sbyte MoveX = 0,
    sbyte MoveY = 0,
    Vec2Fix? SpawnPos = null,
    EntityId? TargetEntityId = null,
    SkillId? SkillId = null,
    CastTargetKind TargetKind = CastTargetKind.Self,
    int TargetPosXRaw = 0,
    int TargetPosYRaw = 0,
    EntityId? LootEntityId = null,
    string VendorId = "",
    string ItemId = "",
    int Quantity = 0);

public sealed record Inputs(ImmutableArray<WorldCommand> Commands);
public sealed record PlayerInput(EntityId EntityId, sbyte MoveX, sbyte MoveY);
