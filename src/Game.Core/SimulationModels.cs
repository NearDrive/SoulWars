using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public readonly record struct ZoneId(int Value);

public enum CastResult : byte
{
    Ok = 0,
    Rejected_NoSuchCaster = 1,
    Rejected_NoSuchSkill = 2,
    Rejected_OnCooldown = 3,
    Rejected_NotEnoughResource = 4,
    Rejected_InvalidTarget = 5,
    Rejected_OutOfRange = 6,
    Rejected_Stunned = 7
}

public enum StatusEffectType : byte
{
    Slow = 1,
    Stun = 2
}

public enum StatusEventType : byte
{
    Applied = 1,
    Refreshed = 2,
    Expired = 3
}

public readonly record struct StatusEffectInstance(
    StatusEffectType Type,
    EntityId SourceEntityId,
    int ExpiresAtTick,
    int MagnitudeRaw);

public readonly record struct OptionalStatusEffect(
    StatusEffectType Type,
    int DurationTicks,
    int MagnitudeRaw);

public readonly record struct StatusEvent(
    int Tick,
    EntityId SourceId,
    EntityId TargetId,
    StatusEventType Type,
    StatusEffectType EffectType,
    int ExpiresAtTick,
    int MagnitudeRaw);

public readonly record struct StatusEffectsComponent(ImmutableArray<StatusEffectInstance> Effects)
{
    public static StatusEffectsComponent Empty => new(ImmutableArray<StatusEffectInstance>.Empty);

    public bool Has(StatusEffectType type) => OrderedEffects().Any(effect => effect.Type == type);

    public bool HasStun => Has(StatusEffectType.Stun);

    public Fix32 GetSlowMultiplier()
    {
        foreach (StatusEffectInstance effect in OrderedEffects())
        {
            if (effect.Type == StatusEffectType.Slow)
            {
                return new Fix32(effect.MagnitudeRaw);
            }
        }

        return Fix32.One;
    }

    public StatusEffectsComponent TickExpire(int currentTick, EntityId targetId, ImmutableArray<StatusEvent>.Builder statusEvents)
    {
        ImmutableArray<StatusEffectInstance> kept = OrderedEffects().Where(effect => effect.ExpiresAtTick > currentTick).ToImmutableArray();
        foreach (StatusEffectInstance expired in OrderedEffects().Where(effect => effect.ExpiresAtTick <= currentTick))
        {
            statusEvents.Add(new StatusEvent(currentTick, expired.SourceEntityId, targetId, StatusEventType.Expired, expired.Type, expired.ExpiresAtTick, expired.MagnitudeRaw));
        }

        return new StatusEffectsComponent(kept);
    }

    public StatusEffectsComponent ApplyOrRefresh(StatusEffectInstance incoming, int currentTick, EntityId targetId, ImmutableArray<StatusEvent>.Builder statusEvents)
    {
        ImmutableArray<StatusEffectInstance> effects = OrderedEffects().Where(effect => effect.ExpiresAtTick > currentTick).ToImmutableArray();
        bool hasExisting = false;
        StatusEffectInstance existing = default;
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i].Type == incoming.Type)
            {
                hasExisting = true;
                existing = effects[i];
                break;
            }
        }

        if (incoming.Type == StatusEffectType.Stun)
        {
            if (!hasExisting)
            {
                statusEvents.Add(new StatusEvent(currentTick, incoming.SourceEntityId, targetId, StatusEventType.Applied, incoming.Type, incoming.ExpiresAtTick, incoming.MagnitudeRaw));
                return new StatusEffectsComponent(effects.Add(incoming).OrderBy(e => e.Type).ThenBy(e => e.SourceEntityId.Value).ToImmutableArray());
            }

            StatusEffectInstance resolved = existing with { ExpiresAtTick = Math.Max(existing.ExpiresAtTick, incoming.ExpiresAtTick) };
            statusEvents.Add(new StatusEvent(currentTick, incoming.SourceEntityId, targetId, StatusEventType.Refreshed, incoming.Type, resolved.ExpiresAtTick, resolved.MagnitudeRaw));
            return Replace(effects, resolved);
        }

        if (incoming.Type == StatusEffectType.Slow)
        {
            if (!hasExisting)
            {
                statusEvents.Add(new StatusEvent(currentTick, incoming.SourceEntityId, targetId, StatusEventType.Applied, incoming.Type, incoming.ExpiresAtTick, incoming.MagnitudeRaw));
                return new StatusEffectsComponent(effects.Add(incoming).OrderBy(e => e.Type).ThenBy(e => e.SourceEntityId.Value).ToImmutableArray());
            }

            bool stronger = incoming.MagnitudeRaw < existing.MagnitudeRaw;
            bool tieAndPreferredSource = incoming.MagnitudeRaw == existing.MagnitudeRaw && incoming.SourceEntityId.Value < existing.SourceEntityId.Value;
            if (stronger || tieAndPreferredSource)
            {
                statusEvents.Add(new StatusEvent(currentTick, incoming.SourceEntityId, targetId, StatusEventType.Refreshed, incoming.Type, incoming.ExpiresAtTick, incoming.MagnitudeRaw));
                return Replace(effects, incoming);
            }

            return new StatusEffectsComponent(effects);
        }

        return new StatusEffectsComponent(effects);
    }

    private ImmutableArray<StatusEffectInstance> OrderedEffects() => (Effects.IsDefault ? ImmutableArray<StatusEffectInstance>.Empty : Effects)
        .OrderBy(effect => effect.Type)
        .ThenBy(effect => effect.SourceEntityId.Value)
        .ToImmutableArray();

    private static StatusEffectsComponent Replace(ImmutableArray<StatusEffectInstance> effects, StatusEffectInstance replacement)
    {
        return new StatusEffectsComponent(effects
            .Where(effect => effect.Type != replacement.Type)
            .Append(replacement)
            .OrderBy(effect => effect.Type)
            .ThenBy(effect => effect.SourceEntityId.Value)
            .ToImmutableArray());
    }
}

public enum SkillEffectKind : byte
{
    Damage = 1,
    Heal = 2
}

public enum CombatEventType : byte
{
    Damage = 1,
    Heal = 2,
    Cancelled = 3
}

public readonly record struct PendingCastComponent(
    EntityId CasterId,
    SkillId SkillId,
    CastTargetKind TargetKind,
    EntityId TargetEntityId,
    int TargetPosXRaw,
    int TargetPosYRaw,
    int StartTick,
    int ExecuteTick,
    uint CastSeq);

public readonly record struct CombatEvent(
    int Tick,
    EntityId SourceId,
    EntityId TargetId,
    SkillId SkillId,
    CombatEventType Type,
    int Amount);

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
    public const uint ThreatBit = 1u << 4;
    public const uint MoveIntentBit = 1u << 5;
    public const uint NavAgentBit = 1u << 6;

    public bool Has(uint bit) => (Bits & bit) != 0;
}

public readonly record struct PositionComponent(Vec2Fix Pos, Vec2Fix Vel);
public readonly record struct HealthComponent(int MaxHp, int Hp, bool IsAlive);
public readonly record struct CombatComponent(Fix32 Range, int Damage, int Defense, int MagicResist, int CooldownTicks, int LastAttackTick);
public readonly record struct AiComponent(int NextWanderChangeTick, sbyte WanderX, sbyte WanderY);

public enum MoveIntentType : byte
{
    None = 0,
    Hold = 1,
    ChaseEntity = 2,
    GoToPoint = 3
}

public readonly record struct MoveIntentComponent(
    MoveIntentType Type,
    EntityId TargetEntityId,
    Fix32 TargetX,
    Fix32 TargetY,
    int RepathEveryTicks,
    int NextRepathTick,
    ImmutableArray<TileCoord> Path,
    int PathLen,
    int PathIndex)
{
    public static MoveIntentComponent Default => new(
        Type: MoveIntentType.None,
        TargetEntityId: default,
        TargetX: Fix32.Zero,
        TargetY: Fix32.Zero,
        RepathEveryTicks: 10,
        NextRepathTick: 0,
        Path: ImmutableArray<TileCoord>.Empty,
        PathLen: 0,
        PathIndex: 0);
}

public readonly record struct NavAgentComponent(Fix32 ArrivalEpsilon)
{
    public static NavAgentComponent Default => new(new Fix32(Fix32.OneRaw / 8));
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
    sbyte WanderY = 0,
    MoveIntentComponent MoveIntent = default,
    NavAgentComponent NavAgent = default,
    DefenseStatsComponent DefenseStats = default,
    StatusEffectsComponent StatusEffects = default,
    SkillCooldownsComponent SkillCooldowns = default,
    ThreatComponent Threat = default);

public sealed record ZoneEntities(
    ImmutableArray<EntityId> AliveIds,
    ImmutableArray<ComponentMask> Masks,
    ImmutableArray<EntityKind> Kinds,
    ImmutableArray<PositionComponent> Positions,
    ImmutableArray<HealthComponent> Health,
    ImmutableArray<CombatComponent> Combat,
    ImmutableArray<AiComponent> Ai,
    ImmutableArray<MoveIntentComponent> MoveIntents,
    ImmutableArray<NavAgentComponent> NavAgents,
    ImmutableArray<StatusEffectsComponent> StatusEffects = default,
    ImmutableArray<SkillCooldownsComponent> SkillCooldowns = default,
    ImmutableArray<ThreatComponent> Threat = default)
{
    public static ZoneEntities Empty => new(
        ImmutableArray<EntityId>.Empty,
        ImmutableArray<ComponentMask>.Empty,
        ImmutableArray<EntityKind>.Empty,
        ImmutableArray<PositionComponent>.Empty,
        ImmutableArray<HealthComponent>.Empty,
        ImmutableArray<CombatComponent>.Empty,
        ImmutableArray<AiComponent>.Empty,
        ImmutableArray<MoveIntentComponent>.Empty,
        ImmutableArray<NavAgentComponent>.Empty,
        ImmutableArray<StatusEffectsComponent>.Empty,
        ImmutableArray<SkillCooldownsComponent>.Empty,
        ImmutableArray<ThreatComponent>.Empty);

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
            MoveIntentComponent moveIntent = MoveIntents[i];
            NavAgentComponent navAgent = NavAgents[i];
            int statusCount = StatusEffects.IsDefault ? 0 : StatusEffects.Length;
            StatusEffectsComponent status = i < statusCount ? StatusEffects[i] : StatusEffectsComponent.Empty;
            int skillCooldownCount = SkillCooldowns.IsDefault ? 0 : SkillCooldowns.Length;
            SkillCooldownsComponent skillCooldowns = i < skillCooldownCount ? SkillCooldowns[i] : SkillCooldownsComponent.Empty;
            int threatCount = Threat.IsDefault ? 0 : Threat.Length;
            ThreatComponent threat = i < threatCount ? Threat[i] : ThreatComponent.Empty;
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
                DefenseStats: new DefenseStatsComponent(combat.Defense, combat.MagicResist),
                Kind: Kinds[i],
                NextWanderChangeTick: ai.NextWanderChangeTick,
                WanderX: ai.WanderX,
                WanderY: ai.WanderY,
                MoveIntent: moveIntent,
                NavAgent: navAgent,
                StatusEffects: status,
                SkillCooldowns: skillCooldowns,
                Threat: threat));
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
        ImmutableArray<MoveIntentComponent>.Builder moveIntents = ImmutableArray.CreateBuilder<MoveIntentComponent>(ordered.Length);
        ImmutableArray<NavAgentComponent>.Builder navAgents = ImmutableArray.CreateBuilder<NavAgentComponent>(ordered.Length);
        ImmutableArray<StatusEffectsComponent>.Builder statusEffects = ImmutableArray.CreateBuilder<StatusEffectsComponent>(ordered.Length);
        ImmutableArray<SkillCooldownsComponent>.Builder skillCooldowns = ImmutableArray.CreateBuilder<SkillCooldownsComponent>(ordered.Length);
        ImmutableArray<ThreatComponent>.Builder threat = ImmutableArray.CreateBuilder<ThreatComponent>(ordered.Length);

        foreach (EntityState entity in ordered)
        {
            uint bits = ComponentMask.PositionBit;
            bits |= ComponentMask.HealthBit;
            bits |= ComponentMask.CombatBit;
            if (entity.Kind == EntityKind.Npc)
            {
                bits |= ComponentMask.AiBit;
                bits |= ComponentMask.ThreatBit;
                bits |= ComponentMask.MoveIntentBit;
                bits |= ComponentMask.NavAgentBit;
            }

            ids.Add(entity.Id);
            masks.Add(new ComponentMask(bits));
            kinds.Add(entity.Kind);
            positions.Add(new PositionComponent(entity.Pos, entity.Vel));
            health.Add(new HealthComponent(entity.MaxHp, entity.Hp, entity.IsAlive));
            combat.Add(new CombatComponent(entity.AttackRange, entity.AttackDamage, entity.DefenseStats.Armor, entity.DefenseStats.MagicResist, entity.AttackCooldownTicks, entity.LastAttackTick));
            ai.Add(entity.Kind == EntityKind.Npc
                ? new AiComponent(entity.NextWanderChangeTick, entity.WanderX, entity.WanderY)
                : default);
            moveIntents.Add(entity.Kind == EntityKind.Npc
                ? entity.MoveIntent
                : MoveIntentComponent.Default);
            navAgents.Add(entity.Kind == EntityKind.Npc
                ? entity.NavAgent
                : NavAgentComponent.Default);
            statusEffects.Add(entity.StatusEffects);
            skillCooldowns.Add(entity.SkillCooldowns);
            threat.Add(entity.Threat);
        }

        return new ZoneEntities(
            ids.MoveToImmutable(),
            masks.MoveToImmutable(),
            kinds.MoveToImmutable(),
            positions.MoveToImmutable(),
            health.MoveToImmutable(),
            combat.MoveToImmutable(),
            ai.MoveToImmutable(),
            moveIntents.MoveToImmutable(),
            navAgents.MoveToImmutable(),
            statusEffects.MoveToImmutable(),
            skillCooldowns.MoveToImmutable(),
            threat.MoveToImmutable());
    }
}

public sealed record ZoneState(ZoneId Id, TileMap Map, ZoneEntities EntitiesData, ImmutableArray<PendingCastComponent> PendingCasts, ImmutableArray<ProjectileComponent> Projectiles)
{
    public ZoneState(ZoneId Id, TileMap Map, ImmutableArray<EntityState> Entities)
        : this(Id, Map, ZoneEntities.FromEntityStates(Entities), ImmutableArray<PendingCastComponent>.Empty, ImmutableArray<ProjectileComponent>.Empty)
    {
    }

    public ZoneState(ZoneId Id, TileMap Map, ZoneEntities EntitiesData)
        : this(Id, Map, EntitiesData, ImmutableArray<PendingCastComponent>.Empty, ImmutableArray<ProjectileComponent>.Empty)
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

    public ZoneState WithPendingCasts(ImmutableArray<PendingCastComponent> pendingCasts) => this with
    {
        PendingCasts = pendingCasts
    };

    public ZoneState WithProjectiles(ImmutableArray<ProjectileComponent> projectiles) => this with
    {
        Projectiles = (projectiles.IsDefault ? ImmutableArray<ProjectileComponent>.Empty : projectiles)
            .OrderBy(p => p.ProjectileId)
            .ToImmutableArray()
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
    PartyRegistry? PartyRegistry = null,
    PartyInviteRegistry? PartyInviteRegistry = null,
    InstanceRegistry? InstanceRegistry = null,
    ImmutableArray<LootEntityState> LootEntities = default,
    ImmutableArray<PlayerInventoryState> PlayerInventories = default,
    ImmutableArray<PlayerDeathAuditEntry> PlayerDeathAuditLog = default,
    ImmutableArray<PlayerWalletState> PlayerWallets = default,
    ImmutableArray<VendorTransactionAuditEntry> VendorTransactionAuditLog = default,
    ImmutableArray<VendorDefinition> Vendors = default,
    ImmutableArray<CombatEvent> CombatEvents = default,
    ImmutableArray<CombatLogEvent> CombatLogEvents = default,
    ImmutableArray<StatusEvent> StatusEvents = default,
    ImmutableArray<SkillCastIntent> SkillCastIntents = default,
    ImmutableArray<ProjectileEvent> ProjectileEvents = default,
    uint CombatEventsDropped_Total = 0,
    uint CombatEventsDropped_LastTick = 0,
    uint CombatEventsEmitted_LastTick = 0,
    int NextProjectileId = 1,
    uint ProjectileSpawnsDropped_Total = 0,
    uint ProjectileSpawnsDropped_LastTick = 0,
    EncounterRegistry? EncounterRegistry = null)
{
    public PartyRegistry PartyRegistryOrEmpty => PartyRegistry ?? Game.Core.PartyRegistry.Empty;
    public PartyInviteRegistry PartyInviteRegistryOrEmpty => PartyInviteRegistry ?? Game.Core.PartyInviteRegistry.Empty;
    public InstanceRegistry InstanceRegistryOrEmpty => InstanceRegistry ?? Game.Core.InstanceRegistry.Empty;
    public EncounterRegistry EncounterRegistryOrEmpty => EncounterRegistry ?? Game.Core.EncounterRegistry.Empty;

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



    public WorldState WithInstanceRegistry(InstanceRegistry instanceRegistry)
    {
        return this with
        {
            InstanceRegistry = instanceRegistry.Canonicalize()
        };
    }


    public WorldState WithEncounterRegistry(EncounterRegistry encounterRegistry)
    {
        return this with
        {
            EncounterRegistry = encounterRegistry.Canonicalize()
        };
    }

    public WorldState WithCombatBudgetCounters(uint droppedTotal, uint droppedLastTick, uint emittedLastTick)
    {
        return this with
        {
            CombatEventsDropped_Total = droppedTotal,
            CombatEventsDropped_LastTick = droppedLastTick,
            CombatEventsEmitted_LastTick = emittedLastTick
        };
    }

    public WorldState WithCombatEvents(ImmutableArray<CombatEvent> combatEvents)
    {
        return this with
        {
            CombatEvents = combatEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : combatEvents.ToImmutableArray()
        };
    }

    public WorldState WithCombatLogEvents(ImmutableArray<CombatLogEvent> combatLogEvents)
    {
        return this with
        {
            CombatLogEvents = (combatLogEvents.IsDefault ? ImmutableArray<CombatLogEvent>.Empty : combatLogEvents)
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SourceId.Value)
                .ThenBy(e => e.TargetId.Value)
                .ThenBy(e => e.SkillId.Value)
                .ThenBy(e => (int)e.Kind)
                .ThenBy(e => e.RawAmount)
            .ThenBy(e => e.FinalAmount)
                .ToImmutableArray()
        };
    }

    public WorldState WithStatusEvents(ImmutableArray<StatusEvent> statusEvents)
    {
        return this with
        {
            StatusEvents = statusEvents
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SourceId.Value)
                .ThenBy(e => e.TargetId.Value)
                .ThenBy(e => e.EffectType)
                .ToImmutableArray()
        };
    }

    public WorldState WithSkillCastIntents(ImmutableArray<SkillCastIntent> skillCastIntents)
    {
        return this with
        {
            SkillCastIntents = skillCastIntents
                .OrderBy(i => i.Tick)
                .ThenBy(i => i.CasterId.Value)
                .ThenBy(i => i.SkillId.Value)
                .ThenBy(i => (int)i.TargetType)
                .ThenBy(i => i.TargetEntityId.Value)
                .ThenBy(i => i.TargetX.Raw)
                .ThenBy(i => i.TargetY.Raw)
                .ToImmutableArray()
        };
    }


    public WorldState WithProjectileEvents(ImmutableArray<ProjectileEvent> projectileEvents)
    {
        return this with
        {
            ProjectileEvents = (projectileEvents.IsDefault ? ImmutableArray<ProjectileEvent>.Empty : projectileEvents)
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.ProjectileId)
                .ThenBy(e => (int)e.Kind)
                .ToImmutableArray()
        };
    }

    public WorldState WithProjectileCounters(int nextProjectileId, uint droppedTotal, uint droppedLastTick)
    {
        return this with
        {
            NextProjectileId = nextProjectileId,
            ProjectileSpawnsDropped_Total = droppedTotal,
            ProjectileSpawnsDropped_LastTick = droppedLastTick
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
    CastSkill = 9,
    InviteToParty = 10,
    AcceptPartyInvite = 11,
    LeaveParty = 12
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
    int Quantity = 0,
    EntityId? InviteePlayerId = null,
    EntityId? InviterPlayerId = null,
    PartyId? PartyId = null);

public sealed record Inputs(ImmutableArray<WorldCommand> Commands);
public sealed record PlayerInput(EntityId EntityId, sbyte MoveX, sbyte MoveY);
