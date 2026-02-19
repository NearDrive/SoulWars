using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
    private const int DefaultMaxHp = 100;
    private const int DefaultAttackDamage = 10;
    private const int DefaultAttackCooldownTicks = 10;
    private const int NpcSpawnMaxAttempts = 64;
    private static readonly Fix32 LootPickupRange = Fix32.FromInt(3) / Fix32.FromInt(2);
    private const string DefaultNpcArchetypeId = "npc.default";
    public const long DefaultStartingGold = 1000;

    public static WorldState CreateInitialState(SimulationConfig config, ZoneDefinitions? zoneDefinitions = null)
    {
        if (zoneDefinitions is not null)
        {
            return CreateInitialStateFromManualDefinitions(config, zoneDefinitions);
        }

        if (config.ZoneCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "ZoneCount must be > 0.");
        }

        ImmutableArray<ZoneState>.Builder zones = ImmutableArray.CreateBuilder<ZoneState>(config.ZoneCount);

        for (int zoneValue = 1; zoneValue <= config.ZoneCount; zoneValue++)
        {
            ZoneId zoneId = new(zoneValue);
            int zoneSeed = config.ZoneCount == 1
                ? config.Seed
                : unchecked((int)Hash32(unchecked((uint)config.Seed), unchecked((uint)zoneId.Value), 0x20E1u, 0xC0DEu));
            SimulationConfig zoneConfig = config with { Seed = zoneSeed };
            TileMap map = WorldGen.Generate(zoneConfig, config.MapWidth, config.MapHeight);

            zones.Add(new ZoneState(
                Id: zoneId,
                Map: map,
                Entities: SpawnNpcsProcedural(config, zoneId, map)));
        }

        ImmutableArray<ZoneState> initialZones = zones
            .OrderBy(z => z.Id.Value)
            .ToImmutableArray();

        return new WorldState(
            Tick: 0,
            Zones: initialZones,
            EntityLocations: BuildEntityLocations(initialZones),
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            PlayerInventories: ImmutableArray<PlayerInventoryState>.Empty,
            PlayerWallets: ImmutableArray<PlayerWalletState>.Empty,
            VendorTransactionAuditLog: ImmutableArray<VendorTransactionAuditEntry>.Empty,
            Vendors: ImmutableArray<VendorDefinition>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty,
            StatusEvents: ImmutableArray<StatusEvent>.Empty);
    }

    private static WorldState CreateInitialStateFromManualDefinitions(SimulationConfig config, ZoneDefinitions definitions)
    {
        if (definitions.Zones.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("Zone definitions are empty.");
        }

        ImmutableArray<ZoneState> zones = definitions.Zones
            .OrderBy(z => z.ZoneId.Value)
            .Select(z => new ZoneState(
                Id: z.ZoneId,
                Map: BuildMapFromObstacles(config, z.StaticObstacles),
                Entities: SpawnNpcsFromDefinitions(z)))
            .ToImmutableArray();

        return new WorldState(
            Tick: 0,
            Zones: zones,
            EntityLocations: BuildEntityLocations(zones),
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            PlayerInventories: ImmutableArray<PlayerInventoryState>.Empty,
            PlayerWallets: ImmutableArray<PlayerWalletState>.Empty,
            VendorTransactionAuditLog: ImmutableArray<VendorTransactionAuditEntry>.Empty,
            Vendors: ImmutableArray<VendorDefinition>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty,
            StatusEvents: ImmutableArray<StatusEvent>.Empty);
    }

    public static WorldState Step(SimulationConfig config, WorldState state, Inputs inputs, SimulationInstrumentation? instrumentation = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputs);

        ImmutableArray<WorldCommand> commands = inputs.Commands.IsDefault
            ? ImmutableArray<WorldCommand>.Empty
            : inputs.Commands;

        WorldState updated = state with
        {
            Tick = state.Tick + 1,
            CombatEvents = ImmutableArray<CombatEvent>.Empty,
            StatusEvents = ImmutableArray<StatusEvent>.Empty
        };
        List<ZoneTransferEvent> pendingTransfers = new();
        ImmutableArray<StatusEvent>.Builder tickStatusEvents = ImmutableArray.CreateBuilder<StatusEvent>();

        foreach (ZoneState zone in updated.Zones.OrderBy(z => z.Id.Value).ToArray())
        {
            (ZoneState nextZone, ImmutableArray<StatusEvent> expireEvents) = ExpireZoneStatuses(updated.Tick, zone);
            updated = updated.WithZoneUpdated(nextZone);
            if (!expireEvents.IsDefaultOrEmpty)
            {
                tickStatusEvents.AddRange(expireEvents);
            }
        }

        foreach (ZoneState zone in updated.Zones.OrderBy(z => z.Id.Value).ToArray())
        {
            ZoneState nextZone = TickDownZoneSkillCooldowns(zone);
            updated = updated.WithZoneUpdated(nextZone);
        }

        ImmutableArray<(WorldCommand Command, int Index)> orderedCommands = commands
            .Select((command, index) => (Command: command, Index: index))
            .OrderBy(x => x.Command.ZoneId.Value)
            .ThenBy(x => x.Command.EntityId.Value)
            .ThenBy(x => (int)x.Command.Kind)
            .ThenBy(x => x.Index)
            .ToImmutableArray();

        Dictionary<int, int> projectedTeleportZones = updated.EntityLocations
            .ToDictionary(location => location.Id.Value, location => location.ZoneId.Value);

        foreach (WorldCommand command in commands.Where(c => c.Kind == WorldCommandKind.TeleportIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            QueueTeleportIntent(config, updated, command, pendingTransfers, projectedTeleportZones, instrumentation);
        }

        foreach ((ZoneId zoneId, ImmutableArray<(WorldCommand Command, int CommandIndex)> zoneCommands) in orderedCommands
                     .Where(x => x.Command.Kind is WorldCommandKind.EnterZone or WorldCommandKind.MoveIntent or WorldCommandKind.AttackIntent or WorldCommandKind.CastSkill or WorldCommandKind.LeaveZone or WorldCommandKind.LootIntent)
                     .GroupBy(x => x.Command.ZoneId.Value)
                     .OrderBy(g => g.Key)
                     .Select(g => (new ZoneId(g.Key), g.Select(v => (v.Command, v.Index)).ToImmutableArray())))
        {
            if (!updated.TryGetZone(zoneId, out ZoneState zone))
            {
                continue;
            }

            (ZoneState nextZone, ImmutableArray<CombatEvent> zoneEvents, ImmutableArray<StatusEvent> zoneStatusEvents) = ProcessZoneCommands(config, updated.Tick, zone, zoneCommands, instrumentation);
            updated = updated.WithZoneUpdated(nextZone);
            if (!zoneEvents.IsDefaultOrEmpty)
            {
                ImmutableArray<CombatEvent> combined = (updated.CombatEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : updated.CombatEvents)
                    .AddRange(zoneEvents);
                if (config.MaxCombatEventsPerTick > 0 && combined.Length > config.MaxCombatEventsPerTick)
                {
                    throw new InvariantViolationException($"invariant=MaxCombatEventsPerTickExceeded tick={updated.Tick} max={config.MaxCombatEventsPerTick} actual={combined.Length}");
                }

                updated = updated.WithCombatEvents(combined);
            }

            if (!zoneStatusEvents.IsDefaultOrEmpty)
            {
                tickStatusEvents.AddRange(zoneStatusEvents);
            }

            updated = RebuildEntityLocations(updated);
        }

        foreach (ZoneState zone in updated.Zones.OrderBy(z => z.Id.Value).ToArray())
        {
            (ZoneState nextZone, ImmutableArray<CombatEvent> pendingEvents, ImmutableArray<StatusEvent> pendingStatusEvents) = ProcessPendingCasts(config, updated.Tick, zone);
            updated = updated.WithZoneUpdated(nextZone);

            if (!pendingEvents.IsDefaultOrEmpty)
            {
                ImmutableArray<CombatEvent> combined = (updated.CombatEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : updated.CombatEvents)
                    .AddRange(pendingEvents);
                if (config.MaxCombatEventsPerTick > 0 && combined.Length > config.MaxCombatEventsPerTick)
                {
                    throw new InvariantViolationException($"invariant=MaxCombatEventsPerTickExceeded tick={updated.Tick} max={config.MaxCombatEventsPerTick} actual={combined.Length}");
                }

                updated = updated.WithCombatEvents(combined);
            }

            if (!pendingStatusEvents.IsDefaultOrEmpty)
            {
                tickStatusEvents.AddRange(pendingStatusEvents);
            }
        }

        foreach (ZoneState zone in updated.Zones.OrderBy(z => z.Id.Value).ToArray())
        {
            ZoneState nextZone = RunNpcAiAndApply(config, updated.Tick, zone, instrumentation);
            updated = updated.WithZoneUpdated(nextZone);
        }

        updated = ApplyTransfers(updated, pendingTransfers, instrumentation);
        updated = RebuildEntityLocations(updated);
        updated = SpawnLootForNpcDeaths(state, updated);
        updated = HandlePlayerDeaths(config, state, updated, orderedCommands.Select(x => x.Command).ToImmutableArray());
        updated = ProcessLootIntents(updated, orderedCommands.Select(x => x.Command).ToImmutableArray());
        updated = ProcessVendorIntents(updated, orderedCommands.Select(x => x.Command).ToImmutableArray());
        updated = EnsureWalletsForPlayers(updated);
        updated = updated.WithStatusEvents(tickStatusEvents.ToImmutable());

        if (config.Invariants.EnableCoreInvariants)
        {
            CoreInvariants.Validate(updated, updated.Tick);
        }

        _ = config.MaxSpeed;
        return updated;
    }

    private static ZoneState RunNpcAiAndApply(SimulationConfig config, int tick, ZoneState zone, SimulationInstrumentation? instrumentation)
    {
        List<WorldCommand> npcCommands = new();
        ImmutableArray<EntityState> ordered = zone.Entities
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();

        ImmutableArray<EntityState>.Builder postAiEntities = ImmutableArray.CreateBuilder<EntityState>(ordered.Length);
        Fix32 aggroRangeSq = config.NpcAggroRange * config.NpcAggroRange;

        foreach (EntityState entity in ordered)
        {
            if (entity.Kind != EntityKind.Npc || !entity.IsAlive)
            {
                postAiEntities.Add(entity);
                continue;
            }

            EntityState npc = entity;
            sbyte moveX = npc.WanderX;
            sbyte moveY = npc.WanderY;

            if (tick >= npc.NextWanderChangeTick)
            {
                SimRng wanderRng = CreateNpcTickRng(config.Seed, zone.Id, npc.Id, tick);
                moveX = (sbyte)wanderRng.NextInt(-1, 2);
                moveY = (sbyte)wanderRng.NextInt(-1, 2);
                npc = npc with
                {
                    WanderX = moveX,
                    WanderY = moveY,
                    NextWanderChangeTick = tick + config.NpcWanderPeriodTicks
                };
            }

            EntityState? closestPlayer = null;
            Fix32 closestDistSq = new(int.MaxValue);

            foreach (EntityState candidate in ordered)
            {
                instrumentation?.CountEntitiesVisited?.Invoke(1);
                if (candidate.Kind != EntityKind.Player || !candidate.IsAlive)
                {
                    continue;
                }

                Fix32 dx = candidate.Pos.X - npc.Pos.X;
                Fix32 dy = candidate.Pos.Y - npc.Pos.Y;
                Fix32 distSq = (dx * dx) + (dy * dy);
                if (distSq > aggroRangeSq || distSq >= closestDistSq)
                {
                    continue;
                }

                closestDistSq = distSq;
                closestPlayer = candidate;
            }

            if (closestPlayer is not null)
            {
                Fix32 dx = closestPlayer.Pos.X - npc.Pos.X;
                Fix32 dy = closestPlayer.Pos.Y - npc.Pos.Y;
                moveX = SignToSByte(dx);
                moveY = SignToSByte(dy);

                Fix32 attackRangeSq = npc.AttackRange * npc.AttackRange;
                bool canAttack = tick - npc.LastAttackTick >= npc.AttackCooldownTicks;
                if (canAttack && closestDistSq <= attackRangeSq)
                {
                    npcCommands.Add(new WorldCommand(
                        Kind: WorldCommandKind.AttackIntent,
                        EntityId: npc.Id,
                        ZoneId: zone.Id,
                        TargetEntityId: closestPlayer.Id));
                }
            }

            npc = npc with { WanderX = moveX, WanderY = moveY };
            postAiEntities.Add(npc);

            npcCommands.Add(new WorldCommand(
                Kind: WorldCommandKind.MoveIntent,
                EntityId: npc.Id,
                ZoneId: zone.Id,
                MoveX: moveX,
                MoveY: moveY));
        }

        ZoneState updatedZone = zone.WithEntities(postAiEntities
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());

        foreach (WorldCommand move in npcCommands.Where(c => c.Kind == WorldCommandKind.MoveIntent).OrderBy(c => c.EntityId.Value))
        {
            updatedZone = ApplyMoveIntent(config, updatedZone, move, instrumentation: instrumentation);
        }

        foreach (WorldCommand attack in npcCommands.Where(c => c.Kind == WorldCommandKind.AttackIntent).OrderBy(c => c.EntityId.Value))
        {
            updatedZone = ApplyAttackIntent(tick, updatedZone, attack);
        }

        return updatedZone;
    }

    private static ZoneState TickDownZoneSkillCooldowns(ZoneState zone)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        ImmutableArray<EntityState>.Builder next = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        bool anyChanged = false;

        for (int i = 0; i < entities.Length; i++)
        {
            EntityState current = entities[i];
            SkillCooldownsComponent cooldowns = current.SkillCooldowns;
            SkillCooldownsComponent ticked = cooldowns.TickDown();
            if (!Equals(cooldowns, ticked))
            {
                anyChanged = true;
                next.Add(current with { SkillCooldowns = ticked });
                continue;
            }

            next.Add(current);
        }

        if (!anyChanged)
        {
            return zone;
        }

        return zone.WithEntities(next.MoveToImmutable());
    }


    private static WorldState SpawnLootForNpcDeaths(WorldState before, WorldState after)
    {
        List<LootEntityState> nextLoot = after.LootEntities.IsDefault
            ? new List<LootEntityState>()
            : after.LootEntities.OrderBy(l => l.Id.Value).ToList();

        Dictionary<int, EntityState> beforeNpcs = before.Zones
            .SelectMany(zone => zone.Entities.Select(entity => (zone.Id, entity)))
            .Where(x => x.entity.Kind == EntityKind.Npc)
            .ToDictionary(x => x.entity.Id.Value, x => x.entity);

        HashSet<int> afterEntityIds = after.Zones
            .SelectMany(zone => zone.Entities.Select(entity => entity.Id.Value))
            .ToHashSet();

        foreach (KeyValuePair<int, EntityState> pair in beforeNpcs.OrderBy(p => p.Key))
        {
            if (afterEntityIds.Contains(pair.Key))
            {
                continue;
            }

            ImmutableArray<ItemStack> items = ResolveLootTable(DefaultNpcArchetypeId);
            if (items.IsDefaultOrEmpty)
            {
                continue;
            }

            EntityState deadNpc = pair.Value;
            ZoneId zoneId = FindEntityZone(before, deadNpc.Id);
            LootEntityState loot = new(
                Id: DeriveLootEntityId(deadNpc.Id),
                ZoneId: zoneId,
                Pos: deadNpc.Pos,
                Items: items);

            if (nextLoot.Any(existing => existing.Id.Value == loot.Id.Value))
            {
                throw new InvariantViolationException($"invariant=UniqueLootEntityId entityId={loot.Id.Value}");
            }

            nextLoot.Add(loot);
        }

        return after.WithLootEntities(nextLoot
            .OrderBy(l => l.Id.Value)
            .ToImmutableArray());
    }


    private static WorldState HandlePlayerDeaths(SimulationConfig config, WorldState before, WorldState after, ImmutableArray<WorldCommand> commands)
    {
        Dictionary<int, (ZoneId ZoneId, EntityState Entity)> beforePlayers = before.Zones
            .SelectMany(zone => zone.Entities.Select(entity => (ZoneId: zone.Id, Entity: entity)))
            .Where(x => x.Entity.Kind == EntityKind.Player)
            .ToDictionary(x => x.Entity.Id.Value, x => (x.ZoneId, x.Entity));

        HashSet<int> afterEntityIds = after.Zones
            .SelectMany(zone => zone.Entities.Select(entity => entity.Id.Value))
            .ToHashSet();

        List<LootEntityState> nextLoot = (after.LootEntities.IsDefault ? ImmutableArray<LootEntityState>.Empty : after.LootEntities)
            .OrderBy(l => l.Id.Value)
            .ToList();
        List<PlayerInventoryState> nextInventories = (after.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : after.PlayerInventories)
            .OrderBy(i => i.EntityId.Value)
            .ToList();
        List<ZoneState> nextZones = after.Zones.OrderBy(z => z.Id.Value).ToList();
        List<PlayerDeathAuditEntry> nextAudit = (after.PlayerDeathAuditLog.IsDefault ? ImmutableArray<PlayerDeathAuditEntry>.Empty : after.PlayerDeathAuditLog)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.PlayerEntityId.Value)
            .ToList();

        foreach (KeyValuePair<int, (ZoneId ZoneId, EntityState Entity)> pair in beforePlayers.OrderBy(p => p.Key))
        {
            int playerId = pair.Key;
            if (afterEntityIds.Contains(playerId))
            {
                continue;
            }

            EntityState deadPlayer = pair.Value.Entity;
            ZoneId zoneId = pair.Value.ZoneId;
            if (commands.Any(c => c.Kind == WorldCommandKind.LeaveZone
                                  && c.EntityId.Value == playerId
                                  && c.ZoneId.Value == zoneId.Value))
            {
                continue;
            }

            EntityId lootEntityId = DerivePlayerDeathLootEntityId(deadPlayer.Id);

            int inventoryIndex = nextInventories.FindIndex(i => i.EntityId.Value == playerId);
            InventoryComponent inventory = inventoryIndex >= 0
                ? nextInventories[inventoryIndex].Inventory
                : InventoryComponent.CreateDefault();

            ImmutableArray<ItemStack> droppedItems = CanonicalizeDeathDropItems(inventory);

            if (!droppedItems.IsDefaultOrEmpty && !nextLoot.Any(l => l.Id.Value == lootEntityId.Value))
            {
                nextLoot.Add(new LootEntityState(
                    Id: lootEntityId,
                    ZoneId: zoneId,
                    Pos: deadPlayer.Pos,
                    Items: droppedItems));
            }

            InventoryComponent clearedInventory = ClearInventory(inventory);
            if (inventoryIndex >= 0)
            {
                nextInventories[inventoryIndex] = nextInventories[inventoryIndex] with { Inventory = clearedInventory };
            }
            else
            {
                nextInventories.Add(new PlayerInventoryState(deadPlayer.Id, clearedInventory));
            }

            int zoneIndex = nextZones.FindIndex(z => z.Id.Value == zoneId.Value);
            if (zoneIndex < 0)
            {
                continue;
            }

            ZoneState zone = nextZones[zoneIndex];
            Vec2Fix respawnPos = ResolveRespawnPosition(config, zone);
            EntityState respawnedPlayer = deadPlayer with
            {
                Pos = respawnPos,
                Vel = Vec2Fix.Zero,
                Hp = deadPlayer.MaxHp,
                IsAlive = true
            };

            ZoneState updatedZone = zone.WithEntities(zone.Entities
                .Add(respawnedPlayer)
                .GroupBy(e => e.Id.Value)
                .Select(g => g.Last())
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray());
            nextZones[zoneIndex] = updatedZone;

            nextAudit.Add(new PlayerDeathAuditEntry(
                Tick: after.Tick,
                PlayerEntityId: deadPlayer.Id,
                ZoneId: zoneId,
                DeathPosition: deadPlayer.Pos,
                DroppedItems: droppedItems,
                LootEntityId: lootEntityId,
                RespawnPosition: respawnPos));
        }

        WorldState withUpdates = after
            .WithLootEntities(nextLoot.OrderBy(l => l.Id.Value).ToImmutableArray())
            .WithPlayerInventories(nextInventories.OrderBy(i => i.EntityId.Value).ToImmutableArray())
            .WithPlayerDeathAuditLog(nextAudit
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.PlayerEntityId.Value)
                .ThenBy(e => e.LootEntityId.Value)
                .ToImmutableArray());

        foreach (ZoneState zone in nextZones)
        {
            withUpdates = withUpdates.WithZoneUpdated(zone);
        }

        return RebuildEntityLocations(withUpdates);
    }

    private static ImmutableArray<ItemStack> CanonicalizeDeathDropItems(InventoryComponent inventory)
    {
        ImmutableArray<ItemStack>.Builder dropped = ImmutableArray.CreateBuilder<ItemStack>();
        for (int i = 0; i < inventory.Slots.Length; i++)
        {
            InventorySlot slot = inventory.Slots[i];
            if (slot.Quantity <= 0)
            {
                continue;
            }

            dropped.Add(new ItemStack(slot.ItemId, slot.Quantity));
        }

        return dropped
            .ToImmutable()
            .OrderBy(i => i.ItemId, StringComparer.Ordinal)
            .ThenBy(i => i.Quantity)
            .ToImmutableArray();
    }

    private static InventoryComponent ClearInventory(InventoryComponent inventory)
    {
        ImmutableArray<InventorySlot>.Builder clearedSlots = ImmutableArray.CreateBuilder<InventorySlot>(inventory.Slots.Length);
        for (int i = 0; i < inventory.Slots.Length; i++)
        {
            clearedSlots.Add(new InventorySlot(string.Empty, 0));
        }

        return inventory with { Slots = clearedSlots.MoveToImmutable() };
    }

    private static Vec2Fix ResolveRespawnPosition(SimulationConfig config, ZoneState zone)
        => FindSpawn(zone.Map, config.Radius);

    private static EntityId DerivePlayerDeathLootEntityId(EntityId deadPlayerId)
        => new(unchecked(int.MinValue + deadPlayerId.Value));

    private static WorldState ProcessLootIntents(WorldState state, ImmutableArray<WorldCommand> commands)
    {
        if (state.LootEntities.IsDefaultOrEmpty)
        {
            return state;
        }

        List<LootEntityState> nextLoot = state.LootEntities.OrderBy(l => l.Id.Value).ToList();
        List<PlayerInventoryState> inventories = (state.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : state.PlayerInventories)
            .OrderBy(i => i.EntityId.Value)
            .ToList();

        foreach (WorldCommand command in commands
                     .Where(c => c.Kind == WorldCommandKind.LootIntent && c.LootEntityId is not null)
                     .OrderBy(c => c.ZoneId.Value)
                     .ThenBy(c => c.EntityId.Value)
                     .ThenBy(c => c.LootEntityId!.Value.Value))
        {
            int lootIndex = nextLoot.FindIndex(l => l.Id.Value == command.LootEntityId!.Value.Value);
            if (lootIndex < 0)
            {
                continue;
            }

            LootEntityState loot = nextLoot[lootIndex];
            if (loot.ZoneId.Value != command.ZoneId.Value)
            {
                continue;
            }

            if (!state.TryGetZone(command.ZoneId, out ZoneState zone))
            {
                continue;
            }

            int actorIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);
            if (actorIndex < 0)
            {
                continue;
            }

            EntityState actor = zone.Entities[actorIndex];
            Fix32 dx = actor.Pos.X - loot.Pos.X;
            Fix32 dy = actor.Pos.Y - loot.Pos.Y;
            Fix32 distSq = (dx * dx) + (dy * dy);
            Fix32 rangeSq = LootPickupRange * LootPickupRange;
            if (distSq > rangeSq)
            {
                continue;
            }

            int inventoryIndex = inventories.FindIndex(i => i.EntityId.Value == command.EntityId.Value);
            PlayerInventoryState inventoryState = inventoryIndex >= 0
                ? inventories[inventoryIndex]
                : new PlayerInventoryState(command.EntityId, InventoryComponent.CreateDefault());

            if (!TryAddLootAtomically(inventoryState.Inventory, loot.Items, out InventoryComponent? updatedInventory))
            {
                continue;
            }

            if (inventoryIndex >= 0)
            {
                inventories[inventoryIndex] = inventoryState with { Inventory = updatedInventory! };
            }
            else
            {
                inventories.Add(new PlayerInventoryState(command.EntityId, updatedInventory!));
            }

            nextLoot.RemoveAt(lootIndex);
        }

        return state
            .WithLootEntities(nextLoot.ToImmutableArray())
            .WithPlayerInventories(inventories
                .OrderBy(i => i.EntityId.Value)
                .ToImmutableArray());
    }

    private static bool TryAddLootAtomically(InventoryComponent inventory, ImmutableArray<ItemStack> lootItems, out InventoryComponent? updated)
    {
        ImmutableArray<ItemStack> canonicalItems = lootItems
            .OrderBy(i => i.ItemId, StringComparer.Ordinal)
            .ThenBy(i => i.Quantity)
            .ToImmutableArray();

        InventorySlot[] slots = inventory.Slots.ToArray();

        foreach (ItemStack stack in canonicalItems)
        {
            int remaining = stack.Quantity;
            if (remaining <= 0)
            {
                updated = null;
                return false;
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || !string.Equals(slot.ItemId, stack.ItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                int room = inventory.StackLimit - slot.Quantity;
                if (room <= 0)
                {
                    continue;
                }

                int moved = Math.Min(room, remaining);
                slots[i] = slot with { Quantity = slot.Quantity + moved };
                remaining -= moved;
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (!slot.IsEmpty)
                {
                    continue;
                }

                int moved = Math.Min(inventory.StackLimit, remaining);
                slots[i] = new InventorySlot(stack.ItemId, moved);
                remaining -= moved;
            }

            if (remaining != 0)
            {
                updated = null;
                return false;
            }
        }

        updated = inventory with { Slots = slots.ToImmutableArray() };
        return true;
    }

    private static WorldState ProcessVendorIntents(WorldState state, ImmutableArray<WorldCommand> commands)
    {
        ImmutableArray<VendorDefinition> vendors = state.Vendors.IsDefault ? ImmutableArray<VendorDefinition>.Empty : state.Vendors;
        if (vendors.IsDefaultOrEmpty)
        {
            return state;
        }

        List<PlayerInventoryState> inventories = (state.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : state.PlayerInventories)
            .OrderBy(i => i.EntityId.Value)
            .ToList();
        List<PlayerWalletState> wallets = (state.PlayerWallets.IsDefault ? ImmutableArray<PlayerWalletState>.Empty : state.PlayerWallets)
            .OrderBy(w => w.EntityId.Value)
            .ToList();
        List<VendorTransactionAuditEntry> audit = (state.VendorTransactionAuditLog.IsDefault ? ImmutableArray<VendorTransactionAuditEntry>.Empty : state.VendorTransactionAuditLog)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.PlayerEntityId.Value)
            .ToList();

        foreach (WorldCommand command in commands
                     .Where(c => c.Kind is WorldCommandKind.VendorBuyIntent or WorldCommandKind.VendorSellIntent)
                     .OrderBy(c => c.ZoneId.Value)
                     .ThenBy(c => c.EntityId.Value)
                     .ThenBy(c => c.VendorId, StringComparer.Ordinal)
                     .ThenBy(c => c.ItemId, StringComparer.Ordinal)
                     .ThenBy(c => c.Quantity)
                     .ThenBy(c => (int)c.Kind))
        {
            if (command.Quantity <= 0)
            {
                continue;
            }

            if (!state.TryGetEntityZone(command.EntityId, out ZoneId playerZone) || playerZone.Value != command.ZoneId.Value)
            {
                continue;
            }

            VendorDefinition? vendor = vendors.FirstOrDefault(v =>
                v.ZoneId.Value == command.ZoneId.Value &&
                string.Equals(v.VendorId, command.VendorId, StringComparison.Ordinal));
            if (vendor is null)
            {
                continue;
            }

            VendorOfferDefinition? offer = vendor.CanonicalOffers.FirstOrDefault(o => string.Equals(o.ItemId, command.ItemId, StringComparison.Ordinal));
            if (offer is null)
            {
                continue;
            }

            if (offer.MaxPerTransaction is int maxPerTx && command.Quantity > maxPerTx)
            {
                continue;
            }

            int inventoryIndex = inventories.FindIndex(i => i.EntityId.Value == command.EntityId.Value);
            PlayerInventoryState inventoryState = inventoryIndex >= 0
                ? inventories[inventoryIndex]
                : new PlayerInventoryState(command.EntityId, InventoryComponent.CreateDefault());
            int walletIndex = wallets.FindIndex(w => w.EntityId.Value == command.EntityId.Value);
            PlayerWalletState walletState = walletIndex >= 0
                ? wallets[walletIndex]
                : new PlayerWalletState(command.EntityId, DefaultStartingGold);

            if (command.Kind == WorldCommandKind.VendorBuyIntent)
            {
                long total = offer.BuyPrice * command.Quantity;
                if (walletState.Gold < total)
                {
                    continue;
                }

                if (!TryAddLootAtomically(inventoryState.Inventory, ImmutableArray.Create(new ItemStack(command.ItemId, command.Quantity)), out InventoryComponent? updatedInventory))
                {
                    continue;
                }

                PlayerWalletState updatedWallet = walletState with { Gold = walletState.Gold - total };
                inventoryState = inventoryState with { Inventory = updatedInventory! };
                if (inventoryIndex >= 0) inventories[inventoryIndex] = inventoryState; else inventories.Add(inventoryState);
                if (walletIndex >= 0) wallets[walletIndex] = updatedWallet; else wallets.Add(updatedWallet);

                audit.Add(new VendorTransactionAuditEntry(
                    Tick: state.Tick,
                    PlayerEntityId: command.EntityId,
                    ZoneId: command.ZoneId,
                    VendorId: vendor.VendorId,
                    Action: VendorAction.Buy,
                    ItemId: command.ItemId,
                    Quantity: command.Quantity,
                    UnitPrice: offer.BuyPrice,
                    GoldBefore: walletState.Gold,
                    GoldAfter: updatedWallet.Gold));
            }
            else
            {
                if (!TryRemoveItemsAtomically(inventoryState.Inventory, command.ItemId, command.Quantity, out InventoryComponent? updatedInventory))
                {
                    continue;
                }

                long total = offer.SellPrice * command.Quantity;
                PlayerWalletState updatedWallet = walletState with { Gold = walletState.Gold + total };
                inventoryState = inventoryState with { Inventory = updatedInventory! };
                if (inventoryIndex >= 0) inventories[inventoryIndex] = inventoryState; else inventories.Add(inventoryState);
                if (walletIndex >= 0) wallets[walletIndex] = updatedWallet; else wallets.Add(updatedWallet);

                audit.Add(new VendorTransactionAuditEntry(
                    Tick: state.Tick,
                    PlayerEntityId: command.EntityId,
                    ZoneId: command.ZoneId,
                    VendorId: vendor.VendorId,
                    Action: VendorAction.Sell,
                    ItemId: command.ItemId,
                    Quantity: command.Quantity,
                    UnitPrice: offer.SellPrice,
                    GoldBefore: walletState.Gold,
                    GoldAfter: updatedWallet.Gold));
            }
        }

        return state
            .WithPlayerInventories(inventories.ToImmutableArray())
            .WithPlayerWallets(wallets.ToImmutableArray())
            .WithVendorTransactionAuditLog(audit.ToImmutableArray());
    }

    private static bool TryRemoveItemsAtomically(InventoryComponent inventory, string itemId, int quantity, out InventoryComponent? updated)
    {
        if (quantity <= 0)
        {
            updated = null;
            return false;
        }

        InventorySlot[] slots = inventory.Slots.ToArray();
        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (string.Equals(slots[i].ItemId, itemId, StringComparison.Ordinal))
            {
                total += slots[i].Quantity;
            }
        }

        if (total < quantity)
        {
            updated = null;
            return false;
        }

        int remaining = quantity;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (!string.Equals(slot.ItemId, itemId, StringComparison.Ordinal) || slot.Quantity <= 0)
            {
                continue;
            }

            int removed = Math.Min(slot.Quantity, remaining);
            int nextQty = slot.Quantity - removed;
            slots[i] = nextQty == 0 ? new InventorySlot(string.Empty, 0) : slot with { Quantity = nextQty };
            remaining -= removed;
        }

        if (remaining != 0)
        {
            updated = null;
            return false;
        }

        updated = inventory with { Slots = slots.ToImmutableArray() };
        return true;
    }


    private static WorldState EnsureWalletsForPlayers(WorldState state)
    {
        List<PlayerWalletState> wallets = (state.PlayerWallets.IsDefault ? ImmutableArray<PlayerWalletState>.Empty : state.PlayerWallets)
            .OrderBy(w => w.EntityId.Value)
            .ToList();

        HashSet<int> walletIds = wallets.Select(w => w.EntityId.Value).ToHashSet();
        foreach (EntityState player in state.Zones
                     .OrderBy(z => z.Id.Value)
                     .SelectMany(z => z.Entities.OrderBy(e => e.Id.Value))
                     .Where(e => e.Kind == EntityKind.Player))
        {
            if (walletIds.Add(player.Id.Value))
            {
                wallets.Add(new PlayerWalletState(player.Id, DefaultStartingGold));
            }
        }

        return state.WithPlayerWallets(wallets.ToImmutableArray());
    }

    private static EntityId DeriveLootEntityId(EntityId deadNpcId) => new(-deadNpcId.Value);

    private static ZoneId FindEntityZone(WorldState state, EntityId entityId)
    {
        foreach (ZoneState zone in state.Zones.OrderBy(z => z.Id.Value))
        {
            if (zone.Entities.Any(e => e.Id.Value == entityId.Value))
            {
                return zone.Id;
            }
        }

        throw new InvalidOperationException($"Entity {entityId.Value} zone not found.");
    }

    private static ImmutableArray<ItemStack> ResolveLootTable(string npcArchetypeId)
    {
        ImmutableArray<ItemStack> items = npcArchetypeId switch
        {
            DefaultNpcArchetypeId => ImmutableArray.Create(
                new ItemStack("gold.coin", 3),
                new ItemStack("potion.minor", 1)),
            _ => ImmutableArray<ItemStack>.Empty
        };

        return items
            .OrderBy(i => i.ItemId, StringComparer.Ordinal)
            .ThenBy(i => i.Quantity)
            .ToImmutableArray();
    }

    private static sbyte SignToSByte(Fix32 value)
    {
        if (value.Raw > 0)
        {
            return 1;
        }

        if (value.Raw < 0)
        {
            return -1;
        }

        return 0;
    }

    private static ImmutableArray<EntityState> SpawnNpcsProcedural(SimulationConfig config, ZoneId zoneId, TileMap map)
    {
        SimRng rng = new(unchecked((int)Hash32(unchecked((uint)config.Seed), unchecked((uint)zoneId.Value), 0xA11CEu, 0xB07u)));
        ImmutableArray<EntityState>.Builder npcs = ImmutableArray.CreateBuilder<EntityState>(config.NpcCountPerZone);

        for (int i = 0; i < config.NpcCountPerZone; i++)
        {
            EntityId entityId = new((zoneId.Value * 100000) + i + 1);
            Vec2Fix spawn = FindDeterministicNpcSpawn(map, config.Radius, rng);
            npcs.Add(new EntityState(
                Id: entityId,
                Pos: spawn,
                Vel: Vec2Fix.Zero,
                MaxHp: DefaultMaxHp,
                Hp: DefaultMaxHp,
                IsAlive: true,
                AttackRange: Fix32.FromInt(1),
                AttackDamage: DefaultAttackDamage,
                AttackCooldownTicks: DefaultAttackCooldownTicks,
                LastAttackTick: -DefaultAttackCooldownTicks,
                Defense: 0,
                Kind: EntityKind.Npc,
                NextWanderChangeTick: 0,
                WanderX: 0,
                WanderY: 0));
        }

        return npcs
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();
    }

    private static ImmutableArray<EntityState> SpawnNpcsFromDefinitions(ZoneDefinition zone)
    {
        List<Vec2Fix> orderedSpawns = new();

        foreach (NpcSpawnDefinition spawn in zone.NpcSpawns
                     .Select((value, index) => (Spawn: value, Index: index))
                     .OrderBy(x => x.Spawn.NpcArchetypeId, StringComparer.Ordinal)
                     .ThenBy(x => x.Index)
                     .Select(x => x.Spawn))
        {
            if (spawn.Count > spawn.SpawnPoints.Length)
            {
                throw new InvalidOperationException($"Zone {zone.ZoneId.Value} has Count > SpawnPoints for archetype '{spawn.NpcArchetypeId}'.");
            }

            for (int i = 0; i < spawn.Count; i++)
            {
                Vec2Fix point = spawn.SpawnPoints[i];
                if (!zone.Bounds.Contains(point))
                {
                    throw new InvariantViolationException($"invariant=NpcSpawnOutOfBounds zoneId={zone.ZoneId.Value} archetype={spawn.NpcArchetypeId} index={i}");
                }

                orderedSpawns.Add(point);
            }
        }

        if (orderedSpawns.Count > 99_999)
        {
            throw new InvalidOperationException($"Zone {zone.ZoneId.Value} defines too many NPC spawns ({orderedSpawns.Count}).");
        }

        ImmutableArray<EntityState>.Builder npcs = ImmutableArray.CreateBuilder<EntityState>(orderedSpawns.Count);
        for (int i = 0; i < orderedSpawns.Count; i++)
        {
            Vec2Fix position = orderedSpawns[i];
            int entityIdValue = (zone.ZoneId.Value * 100000) + i + 1;

            npcs.Add(new EntityState(
                Id: new EntityId(entityIdValue),
                Pos: position,
                Vel: Vec2Fix.Zero,
                MaxHp: DefaultMaxHp,
                Hp: DefaultMaxHp,
                IsAlive: true,
                AttackRange: Fix32.FromInt(1),
                AttackDamage: DefaultAttackDamage,
                AttackCooldownTicks: DefaultAttackCooldownTicks,
                LastAttackTick: -DefaultAttackCooldownTicks,
                Defense: 0,
                Kind: EntityKind.Npc,
                NextWanderChangeTick: 0,
                WanderX: 0,
                WanderY: 0));

        }

        return npcs
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray();
    }

    private static TileMap BuildMapFromObstacles(SimulationConfig config, ImmutableArray<ZoneAabb> obstacles)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(config.MapWidth * config.MapHeight);

        for (int y = 0; y < config.MapHeight; y++)
        {
            for (int x = 0; x < config.MapWidth; x++)
            {
                bool border = x == 0 || y == 0 || x == config.MapWidth - 1 || y == config.MapHeight - 1;
                if (border)
                {
                    tiles.Add(TileKind.Solid);
                    continue;
                }

                tiles.Add(TileOverlapsObstacle(x, y, obstacles)
                    ? TileKind.Solid
                    : TileKind.Empty);
            }
        }

        return new TileMap(config.MapWidth, config.MapHeight, tiles.MoveToImmutable());
    }

    private static bool TileOverlapsObstacle(int tileX, int tileY, ImmutableArray<ZoneAabb> obstacles)
    {
        Fix32 tileMinX = Fix32.FromInt(tileX);
        Fix32 tileMinY = Fix32.FromInt(tileY);
        Fix32 tileMaxX = Fix32.FromInt(tileX + 1);
        Fix32 tileMaxY = Fix32.FromInt(tileY + 1);

        foreach (ZoneAabb obstacle in obstacles)
        {
            Fix32 obstacleMinX = obstacle.Center.X - obstacle.HalfExtents.X;
            Fix32 obstacleMaxX = obstacle.Center.X + obstacle.HalfExtents.X;
            Fix32 obstacleMinY = obstacle.Center.Y - obstacle.HalfExtents.Y;
            Fix32 obstacleMaxY = obstacle.Center.Y + obstacle.HalfExtents.Y;

            bool overlaps = tileMinX < obstacleMaxX
                            && tileMaxX > obstacleMinX
                            && tileMinY < obstacleMaxY
                            && tileMaxY > obstacleMinY;
            if (overlaps)
            {
                return true;
            }
        }

        return false;
    }

    private static Vec2Fix FindDeterministicNpcSpawn(TileMap map, Fix32 radius, SimRng rng)
    {
        Fix32 half = new(Fix32.OneRaw / 2);

        for (int attempt = 0; attempt < NpcSpawnMaxAttempts; attempt++)
        {
            int x = rng.NextInt(1, map.Width - 1);
            int y = rng.NextInt(1, map.Height - 1);
            if (map.Get(x, y) == TileKind.Solid)
            {
                continue;
            }

            Vec2Fix candidate = new(Fix32.FromInt(x) + half, Fix32.FromInt(y) + half);
            if (!Physics2D.OverlapsSolidTile(candidate, radius, map))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to find deterministic NPC spawn after max attempts.");
    }

    public static uint Hash32(uint a, uint b, uint c, uint d)
    {
        uint h = 0x9E3779B9u;
        h ^= a + 0x85EBCA6Bu + (h << 6) + (h >> 2);
        h ^= b + 0xC2B2AE35u + (h << 6) + (h >> 2);
        h ^= c + 0x27D4EB2Fu + (h << 6) + (h >> 2);
        h ^= d + 0x165667B1u + (h << 6) + (h >> 2);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    public static SimRng CreateNpcTickRng(int serverSeed, ZoneId zid, EntityId eid, int tick)
    {
        uint seed = Hash32(unchecked((uint)serverSeed), unchecked((uint)zid.Value), unchecked((uint)eid.Value), unchecked((uint)tick));
        return new SimRng(unchecked((int)seed));
    }


    private static void QueueTeleportIntent(
        SimulationConfig config,
        WorldState state,
        WorldCommand command,
        List<ZoneTransferEvent> pendingTransfers,
        Dictionary<int, int> projectedTeleportZones,
        SimulationInstrumentation? instrumentation)
    {
        if (command.ToZoneId is null)
        {
            return;
        }

        ZoneId fromZoneId = command.ZoneId;
        ZoneId toZoneId = command.ToZoneId.Value;

        if (fromZoneId.Value == toZoneId.Value)
        {
            return;
        }

        if (!state.TryGetZone(toZoneId, out ZoneState toZone))
        {
            return;
        }

        if (!projectedTeleportZones.TryGetValue(command.EntityId.Value, out int projectedZoneValue) || projectedZoneValue != fromZoneId.Value)
        {
            return;
        }

        Vec2Fix spawn = FindSpawn(toZone.Map, config.Radius);

        int existingIndex = pendingTransfers.FindIndex(t => t.EntityId.Value == command.EntityId.Value);
        if (existingIndex >= 0)
        {
            ZoneTransferEvent existing = pendingTransfers[existingIndex];
            ZoneTransferEvent chained = existing with
            {
                ToZoneId = toZoneId.Value,
                Position = spawn,
                Tick = state.Tick
            };
            pendingTransfers[existingIndex] = chained;
            instrumentation?.OnZoneTransferQueued?.Invoke(chained);
        }
        else
        {
            if (!state.TryGetZone(fromZoneId, out ZoneState fromZone))
            {
                return;
            }

            int fromIndex = ZoneEntities.FindIndex(fromZone.EntitiesData.AliveIds, command.EntityId);
            if (fromIndex < 0)
            {
                return;
            }

            ZoneTransferEvent transfer = new(
                FromZoneId: fromZoneId.Value,
                ToZoneId: toZoneId.Value,
                EntityId: command.EntityId,
                Position: spawn,
                Reason: 1u,
                Tick: state.Tick);
            pendingTransfers.Add(transfer);
            instrumentation?.OnZoneTransferQueued?.Invoke(transfer);
        }

        projectedTeleportZones[command.EntityId.Value] = toZoneId.Value;
    }

    private static WorldState ApplyTransfers(WorldState state, List<ZoneTransferEvent> pendingTransfers, SimulationInstrumentation? instrumentation)
    {
        if (pendingTransfers.Count == 0)
        {
            return state;
        }

        ImmutableArray<ZoneTransferEvent> orderedTransfers = pendingTransfers
            .OrderBy(t => t.FromZoneId)
            .ThenBy(t => t.ToZoneId)
            .ThenBy(t => t.EntityId.Value)
            .ToImmutableArray();

        HashSet<int> duplicateGuard = new();
        foreach (ZoneTransferEvent transfer in orderedTransfers)
        {
            if (!duplicateGuard.Add(transfer.EntityId.Value))
            {
                throw new InvalidOperationException($"Duplicate ZoneTransferEvent for entity {transfer.EntityId.Value} in tick {transfer.Tick}.");
            }
        }

        WorldState updated = state;

        foreach (ZoneTransferEvent transfer in orderedTransfers)
        {
            if (transfer.FromZoneId == transfer.ToZoneId)
            {
                throw new InvalidOperationException($"Invalid ZoneTransferEvent with same source/target zone {transfer.FromZoneId} for entity {transfer.EntityId.Value}.");
            }

            ZoneId fromZoneId = new(transfer.FromZoneId);
            ZoneId toZoneId = new(transfer.ToZoneId);

            if (!updated.TryGetZone(fromZoneId, out ZoneState fromZone))
            {
                continue;
            }

            if (!updated.TryGetZone(toZoneId, out ZoneState toZone))
            {
                continue;
            }

            int fromIndex = ZoneEntities.FindIndex(fromZone.EntitiesData.AliveIds, transfer.EntityId);
            if (fromIndex < 0)
            {
                continue;
            }

            EntityState entity = fromZone.Entities[fromIndex];

            ZoneState nextFrom = fromZone.WithEntities(fromZone.Entities
                .Where(e => e.Id.Value != transfer.EntityId.Value)
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray());

            EntityState moved = entity with
            {
                Pos = transfer.Position,
                Vel = Vec2Fix.Zero
            };

            ZoneState nextTo = toZone.WithEntities(toZone.Entities
                .Add(moved)
                .GroupBy(e => e.Id.Value)
                .Select(g => g.Last())
                .OrderBy(e => e.Id.Value)
                .ToImmutableArray());

            updated = updated
                .WithZoneUpdated(nextFrom)
                .WithZoneUpdated(nextTo)
                .WithEntityLocation(transfer.EntityId, toZoneId);

            instrumentation?.OnZoneTransferApplied?.Invoke(transfer);
        }

        pendingTransfers.Clear();
        return updated;
    }

    private static WorldState RebuildEntityLocations(WorldState state)
    {
        return state with
        {
            EntityLocations = BuildEntityLocations(state.Zones)
        };
    }

    private static ImmutableArray<EntityLocation> BuildEntityLocations(ImmutableArray<ZoneState> zones)
    {
        ImmutableArray<EntityLocation>.Builder locations = ImmutableArray.CreateBuilder<EntityLocation>();

        foreach (ZoneState zone in zones.OrderBy(z => z.Id.Value))
        {
            foreach (EntityState entity in zone.Entities.OrderBy(e => e.Id.Value))
            {
                locations.Add(new EntityLocation(entity.Id, zone.Id));
            }
        }

        return locations
            .OrderBy(location => location.Id.Value)
            .ToImmutableArray();
    }

    private static (ZoneState Zone, ImmutableArray<CombatEvent> Events, ImmutableArray<StatusEvent> StatusEvents) ProcessZoneCommands(SimulationConfig config, int tick, ZoneState zone, ImmutableArray<(WorldCommand Command, int CommandIndex)> zoneCommands, SimulationInstrumentation? instrumentation)
    {
        ZoneState current = zone;
        ImmutableArray<CombatEvent>.Builder combatEvents = ImmutableArray.CreateBuilder<CombatEvent>();
        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>();

        foreach ((WorldCommand command, _) in zoneCommands.Where(c => c.Command.Kind is WorldCommandKind.EnterZone))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyEnterZone(config, current, command);
        }

        foreach ((WorldCommand command, _) in zoneCommands.Where(c => c.Command.Kind is WorldCommandKind.MoveIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyMoveIntent(config, current, command, instrumentation: instrumentation);
        }

        foreach ((WorldCommand command, _) in zoneCommands.Where(c => c.Command.Kind is WorldCommandKind.AttackIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyAttackIntent(tick, current, command);
        }

        foreach ((WorldCommand command, int commandIndex) in zoneCommands
                     .Where(c => c.Command.Kind is WorldCommandKind.CastSkill)
                     .OrderBy(c => c.Command.EntityId.Value)
                     .ThenBy(c => c.Command.SkillId?.Value ?? int.MinValue)
                     .ThenBy(c => c.Command.TargetEntityId?.Value ?? int.MinValue)
                     .ThenBy(c => c.CommandIndex))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            (ZoneState next, ImmutableArray<CombatEvent> castEvents, ImmutableArray<StatusEvent> castStatusEvents) = ApplyCastSkill(config, tick, current, command);
            current = next;
            for (int targetOrder = 0; targetOrder < castEvents.Length; targetOrder++)
            {
                combatEvents.Add(castEvents[targetOrder]);
            }

            for (int statusOrder = 0; statusOrder < castStatusEvents.Length; statusOrder++)
            {
                statusEvents.Add(castStatusEvents[statusOrder]);
            }
        }

        (ZoneState afterPending, ImmutableArray<CombatEvent> pendingEvents, ImmutableArray<StatusEvent> pendingStatusEvents) = ProcessPendingCasts(config, tick, current);
        current = afterPending;
        if (!pendingEvents.IsDefaultOrEmpty)
        {
            combatEvents.AddRange(pendingEvents);
        }

        if (!pendingStatusEvents.IsDefaultOrEmpty)
        {
            statusEvents.AddRange(pendingStatusEvents);
        }

        foreach ((WorldCommand command, _) in zoneCommands.Where(c => c.Command.Kind is WorldCommandKind.LeaveZone))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyLeaveZone(current, command);
        }

        return (current, combatEvents.ToImmutable(), statusEvents.ToImmutable());
    }

    private static bool IsValidCommand(WorldCommand command)
    {
        if (command.Kind is WorldCommandKind.MoveIntent)
        {
            if (command.MoveX is < -1 or > 1)
            {
                return false;
            }

            if (command.MoveY is < -1 or > 1)
            {
                return false;
            }
        }

        if (command.Kind is WorldCommandKind.TeleportIntent && command.ToZoneId is null)
        {
            return false;
        }

        if (command.Kind is WorldCommandKind.LootIntent && command.LootEntityId is null)
        {
            return false;
        }

        if (command.Kind is WorldCommandKind.CastSkill)
        {
            if (command.SkillId is null)
            {
                return false;
            }

            if (command.TargetKind == CastTargetKind.Entity && command.TargetEntityId is null)
            {
                return false;
            }
        }

        if (command.Kind is WorldCommandKind.VendorBuyIntent or WorldCommandKind.VendorSellIntent)
        {
            if (string.IsNullOrWhiteSpace(command.VendorId) || string.IsNullOrWhiteSpace(command.ItemId) || command.Quantity <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static ZoneState ApplyEnterZone(SimulationConfig config, ZoneState zone, WorldCommand command)
    {
        bool exists = zone.Entities.Any(entity => entity.Id.Value == command.EntityId.Value);
        if (exists)
        {
            return zone;
        }

        Vec2Fix spawn = command.SpawnPos ?? FindSpawn(zone.Map, config.Radius);

        EntityState entity = new(
            Id: command.EntityId,
            Pos: spawn,
            Vel: Vec2Fix.Zero,
            MaxHp: DefaultMaxHp,
            Hp: DefaultMaxHp,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: DefaultAttackDamage,
            AttackCooldownTicks: DefaultAttackCooldownTicks,
            LastAttackTick: -DefaultAttackCooldownTicks,
            Defense: 0,
            Kind: EntityKind.Player,
            NextWanderChangeTick: 0,
            WanderX: 0,
            WanderY: 0);

        return zone.WithEntities(zone.Entities
            .Add(entity)
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());
    }

    private static ZoneState ApplyLeaveZone(ZoneState zone, WorldCommand command)
    {
        return zone.WithEntities(zone.Entities
            .Where(entity => entity.Id.Value != command.EntityId.Value)
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray());
    }

    private static ZoneState ApplyMoveIntent(SimulationConfig config, ZoneState zone, WorldCommand command, SimulationInstrumentation? instrumentation = null)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        int entityIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);

        if (entityIndex < 0)
        {
            return zone;
        }

        instrumentation?.CountEntitiesVisited?.Invoke(1);

        EntityState entity = entities[entityIndex];
        if (!entity.IsAlive)
        {
            return zone;
        }

        if (entity.StatusEffects.HasStun)
        {
            EntityState stunned = entity with { Vel = Vec2Fix.Zero };
            return ReplaceEntity(zone, entityIndex, stunned);
        }

        PlayerInput playerInput = new(entity.Id, command.MoveX, command.MoveY);
        Fix32 slowMultiplier = entity.StatusEffects.GetSlowMultiplier();

        EntityState updated = Physics2D.Integrate(
            entity,
            playerInput,
            zone.Map,
            config.DtFix,
            config.MoveSpeed * slowMultiplier,
            config.Radius,
            instrumentation?.CountCollisionChecks);

        return ReplaceEntity(zone, entityIndex, updated);
    }

    private static ZoneState ApplyAttackIntent(int tick, ZoneState zone, WorldCommand command)
    {
        if (command.TargetEntityId is null)
        {
            return zone;
        }

        ImmutableArray<EntityState> entities = zone.Entities;
        int attackerIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);
        int targetIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.TargetEntityId.Value);

        if (attackerIndex < 0 || targetIndex < 0)
        {
            return zone;
        }

        EntityState attacker = entities[attackerIndex];
        EntityState target = entities[targetIndex];

        if (!attacker.IsAlive || !target.IsAlive)
        {
            return zone;
        }

        if (tick - attacker.LastAttackTick < attacker.AttackCooldownTicks)
        {
            return zone;
        }

        Fix32 dx = attacker.Pos.X - target.Pos.X;
        Fix32 dy = attacker.Pos.Y - target.Pos.Y;
        Fix32 distSq = (dx * dx) + (dy * dy);
        Fix32 rangeSq = attacker.AttackRange * attacker.AttackRange;

        if (distSq > rangeSq)
        {
            return zone;
        }

        EntityState updatedAttacker = attacker with { LastAttackTick = tick };

        int nextHp = Math.Max(0, target.Hp - attacker.AttackDamage);
        EntityState updatedTarget = target with
        {
            Hp = nextHp,
            IsAlive = nextHp > 0
        };

        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);

        for (int i = 0; i < entities.Length; i++)
        {
            if (i == attackerIndex)
            {
                builder.Add(updatedAttacker);
                continue;
            }

            if (i == targetIndex)
            {
                if (updatedTarget.IsAlive)
                {
                    builder.Add(updatedTarget);
                }

                continue;
            }

            builder.Add(entities[i]);
        }

        return zone.WithEntities(builder
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());
    }

    private static (ZoneState Zone, ImmutableArray<CombatEvent> Events, ImmutableArray<StatusEvent> StatusEvents) ApplyCastSkill(SimulationConfig config, int tick, ZoneState zone, WorldCommand command)
    {
        CastResult result = ValidateCastSkill(config, tick, zone, command);
        if (result != CastResult.Ok || command.SkillId is null)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        SkillDefinition? skill = FindSkill(config, command.SkillId.Value);
        if (skill is null)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        if (skill.Value.CastTimeTicks <= 0)
        {
            return ExecuteCastNow(config, tick, zone, command.EntityId, command, skill.Value);
        }

        if (zone.PendingCasts.Any(p => p.CasterId.Value == command.EntityId.Value))
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        uint nextSeq = 1;
        foreach (PendingCastComponent pending in zone.PendingCasts)
        {
            if (pending.CasterId.Value == command.EntityId.Value && pending.CastSeq >= nextSeq)
            {
                nextSeq = pending.CastSeq + 1;
            }
        }

        PendingCastComponent created = new(
            CasterId: command.EntityId,
            SkillId: skill.Value.Id,
            TargetKind: command.TargetKind,
            TargetEntityId: command.TargetEntityId ?? default,
            TargetPosXRaw: command.TargetPosXRaw,
            TargetPosYRaw: command.TargetPosYRaw,
            StartTick: tick,
            ExecuteTick: tick + skill.Value.CastTimeTicks,
            CastSeq: nextSeq);

        ImmutableArray<PendingCastComponent> updatedPending = (zone.PendingCasts.IsDefault ? ImmutableArray<PendingCastComponent>.Empty : zone.PendingCasts)
            .Add(created)
            .OrderBy(p => p.ExecuteTick)
            .ThenBy(p => p.CasterId.Value)
            .ThenBy(p => p.CastSeq)
            .ToImmutableArray();

        return (zone.WithPendingCasts(updatedPending), ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
    }

    private static (ZoneState Zone, ImmutableArray<CombatEvent> Events, ImmutableArray<StatusEvent> StatusEvents) ProcessPendingCasts(SimulationConfig config, int tick, ZoneState zone)
    {
        if (zone.PendingCasts.IsDefaultOrEmpty)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        ZoneState current = zone;
        ImmutableArray<CombatEvent>.Builder combatEvents = ImmutableArray.CreateBuilder<CombatEvent>();
        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>();
        ImmutableArray<PendingCastComponent> pending = zone.PendingCasts
            .OrderBy(p => p.ExecuteTick)
            .ThenBy(p => p.CasterId.Value)
            .ThenBy(p => p.CastSeq)
            .ToImmutableArray();
        ImmutableArray<PendingCastComponent>.Builder remaining = ImmutableArray.CreateBuilder<PendingCastComponent>(pending.Length);

        foreach (PendingCastComponent cast in pending)
        {
            if (cast.ExecuteTick != tick)
            {
                remaining.Add(cast);
                continue;
            }

            if (!TryBuildPendingCastCommand(cast, out WorldCommand command))
            {
                continue;
            }

            SkillDefinition? skill = FindSkill(config, cast.SkillId);
            if (skill is null)
            {
                continue;
            }

            if (ShouldCancelPendingCast(config, tick, current, command, skill.Value))
            {
                combatEvents.Add(new CombatEvent(tick, cast.CasterId, cast.TargetKind == CastTargetKind.Entity ? cast.TargetEntityId : cast.CasterId, cast.SkillId, CombatEventType.Cancelled, 0));
                continue;
            }

            (ZoneState executedZone, ImmutableArray<CombatEvent> executedEvents, ImmutableArray<StatusEvent> executedStatusEvents) = ExecuteCastNow(config, tick, current, cast.CasterId, command, skill.Value);
            current = executedZone;
            combatEvents.AddRange(executedEvents);
            statusEvents.AddRange(executedStatusEvents);
        }

        current = current.WithPendingCasts(remaining
            .OrderBy(p => p.ExecuteTick)
            .ThenBy(p => p.CasterId.Value)
            .ThenBy(p => p.CastSeq)
            .ToImmutableArray());

        return (current, combatEvents.ToImmutable(), statusEvents.ToImmutable());
    }

    private static bool TryBuildPendingCastCommand(PendingCastComponent pending, out WorldCommand command)
    {
        command = new WorldCommand(
            Kind: WorldCommandKind.CastSkill,
            EntityId: pending.CasterId,
            ZoneId: default,
            SkillId: pending.SkillId,
            TargetEntityId: pending.TargetKind == CastTargetKind.Entity ? pending.TargetEntityId : null,
            TargetKind: pending.TargetKind,
            TargetPosXRaw: pending.TargetPosXRaw,
            TargetPosYRaw: pending.TargetPosYRaw);
        return true;
    }

    private static bool ShouldCancelPendingCast(SimulationConfig config, int tick, ZoneState zone, WorldCommand command, SkillDefinition skill)
    {
        int casterIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);
        if (casterIndex < 0)
        {
            return true;
        }

        ImmutableArray<EntityState> entities = zone.Entities;
        EntityState caster = entities[casterIndex];
        if (!caster.IsAlive || caster.StatusEffects.HasStun)
        {
            return true;
        }

        if (command.TargetKind == CastTargetKind.Entity)
        {
            if (command.TargetEntityId is null)
            {
                return true;
            }

            int targetIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.TargetEntityId.Value);
            if (targetIndex < 0)
            {
                return true;
            }
        }

        Vec2Fix targetPos = command.TargetKind == CastTargetKind.Point
            ? new Vec2Fix(new Fix32(command.TargetPosXRaw), new Fix32(command.TargetPosYRaw))
            : entities[ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.TargetEntityId!.Value)].Pos;

        Fix32 dx = caster.Pos.X - targetPos.X;
        Fix32 dy = caster.Pos.Y - targetPos.Y;
        Fix32 distSq = (dx * dx) + (dy * dy);
        Fix32 range = new Fix32(skill.RangeQRaw);
        if (distSq > range * range)
        {
            return true;
        }

        if (command.TargetKind == CastTargetKind.Point)
        {
            ImmutableArray<int> pointTargetIndices = ResolveCastTargetIndices(zone, casterIndex, command, skill);
            if (pointTargetIndices.IsDefaultOrEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private static (ZoneState Zone, ImmutableArray<CombatEvent> Events, ImmutableArray<StatusEvent> StatusEvents) ExecuteCastNow(SimulationConfig config, int tick, ZoneState zone, EntityId casterId, WorldCommand command, SkillDefinition skill)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        int casterIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, casterId);
        if (casterIndex < 0)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        EntityState caster = entities[casterIndex];
        EntityState updatedCaster = caster with
        {
            LastAttackTick = tick,
            AttackCooldownTicks = Math.Max(skill.CooldownTicks, skill.GlobalCooldownTicks)
        };

        ImmutableArray<int> targetIndices = ResolveCastTargetIndices(zone, casterIndex, command, skill);
        if (targetIndices.IsDefaultOrEmpty)
        {
            return (zone, ImmutableArray<CombatEvent>.Empty, ImmutableArray<StatusEvent>.Empty);
        }

        Dictionary<int, EntityState> targetUpdates = new();
        ImmutableArray<CombatEvent>.Builder combatEvents = ImmutableArray.CreateBuilder<CombatEvent>(targetIndices.Length);
        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>(targetIndices.Length);

        for (int i = 0; i < targetIndices.Length; i++)
        {
            int targetIndex = targetIndices[i];
            EntityState target = entities[targetIndex];
            if (targetUpdates.TryGetValue(targetIndex, out EntityState? alreadyUpdated) && alreadyUpdated is not null)
            {
                target = alreadyUpdated;
            }

            int amount = skill.BaseAmount;
            if (skill.CoefRaw != 0)
            {
                amount += Fix32.FloorToInt(Fix32.FromInt(caster.AttackDamage) * new Fix32(skill.CoefRaw));
            }

            int finalAmount;
            CombatEventType eventType;
            EntityState updatedTarget;

            if (skill.EffectKind == SkillEffectKind.Heal)
            {
                finalAmount = Math.Min(Math.Max(0, target.MaxHp - target.Hp), Math.Max(0, amount));
                eventType = CombatEventType.Heal;
                updatedTarget = target with
                {
                    Hp = target.Hp + finalAmount,
                    IsAlive = target.Hp + finalAmount > 0
                };
            }
            else
            {
                finalAmount = Math.Max(1, amount - target.Defense);
                finalAmount = Math.Min(finalAmount, target.Hp);
                eventType = CombatEventType.Damage;
                int nextHp = Math.Max(0, target.Hp - finalAmount);
                updatedTarget = target with
                {
                    Hp = nextHp,
                    IsAlive = nextHp > 0
                };
            }

            targetUpdates[targetIndex] = updatedTarget;

            if (skill.StatusEffect is OptionalStatusEffect statusDefinition)
            {
                StatusEffectInstance incomingStatus = new(
                    statusDefinition.Type,
                    caster.Id,
                    tick + statusDefinition.DurationTicks,
                    statusDefinition.MagnitudeRaw);

                StatusEffectsComponent applied = updatedTarget.StatusEffects.ApplyOrRefresh(incomingStatus, tick, updatedTarget.Id, statusEvents);
                targetUpdates[targetIndex] = updatedTarget with { StatusEffects = applied };
            }

            combatEvents.Add(new CombatEvent(
                Tick: tick,
                SourceId: caster.Id,
                TargetId: target.Id,
                SkillId: skill.Id,
                Type: eventType,
                Amount: finalAmount));
        }

        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            if (i == casterIndex)
            {
                builder.Add(updatedCaster);
            }
            else if (targetUpdates.TryGetValue(i, out EntityState? updatedTarget) && updatedTarget is not null)
            {
                builder.Add(updatedTarget);
            }
            else
            {
                builder.Add(entities[i]);
            }
        }

        ZoneState updatedZone = zone.WithEntities(builder
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());

        return (updatedZone, combatEvents.ToImmutable(), statusEvents.ToImmutable());
    }

    public static CastResult ValidateCastSkill(SimulationConfig config, int tick, ZoneState zone, WorldCommand command)
    {
        if (command.SkillId is null)
        {
            return CastResult.Rejected_NoSuchSkill;
        }

        SkillDefinition? skill = FindSkill(config, command.SkillId.Value);
        if (skill is null)
        {
            return CastResult.Rejected_NoSuchSkill;
        }

        int casterIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);
        if (casterIndex < 0)
        {
            return CastResult.Rejected_NoSuchCaster;
        }

        ImmutableArray<EntityState> entities = zone.Entities;
        EntityState caster = entities[casterIndex];

        if (!caster.IsAlive)
        {
            return CastResult.Rejected_NoSuchCaster;
        }

        if (caster.StatusEffects.HasStun)
        {
            return CastResult.Rejected_Stunned;
        }

        if (!zone.PendingCasts.IsDefaultOrEmpty && zone.PendingCasts.Any(p => p.CasterId.Value == command.EntityId.Value))
        {
            return CastResult.Rejected_OnCooldown;
        }

        if (tick - caster.LastAttackTick < caster.AttackCooldownTicks)
        {
            return CastResult.Rejected_OnCooldown;
        }

        if (skill.Value.ResourceCost > 0)
        {
            return CastResult.Rejected_NotEnoughResource;
        }

        if (skill.Value.TargetKind != command.TargetKind)
        {
            return CastResult.Rejected_InvalidTarget;
        }

        Vec2Fix targetPos = command.TargetKind == CastTargetKind.Point
            ? new Vec2Fix(new Fix32(command.TargetPosXRaw), new Fix32(command.TargetPosYRaw))
            : default;

        if (command.TargetKind != CastTargetKind.Point)
        {
            ImmutableArray<int> resolvedIndices = ResolveCastTargetIndices(zone, casterIndex, command, skill.Value);
            if (resolvedIndices.IsDefaultOrEmpty)
            {
                return CastResult.Rejected_InvalidTarget;
            }

            targetPos = entities[resolvedIndices[0]].Pos;
        }

        Fix32 dx = caster.Pos.X - targetPos.X;
        Fix32 dy = caster.Pos.Y - targetPos.Y;
        Fix32 distSq = (dx * dx) + (dy * dy);
        Fix32 range = new Fix32(skill.Value.RangeQRaw);
        Fix32 rangeSq = range * range;

        if (distSq > rangeSq)
        {
            return CastResult.Rejected_OutOfRange;
        }

        if (command.TargetKind == CastTargetKind.Point)
        {
            ImmutableArray<int> pointTargetIndices = ResolveCastTargetIndices(zone, casterIndex, command, skill.Value);
            if (pointTargetIndices.IsDefaultOrEmpty)
            {
                return CastResult.Rejected_InvalidTarget;
            }
        }

        return CastResult.Ok;
    }

    private static (ZoneState Zone, ImmutableArray<StatusEvent> StatusEvents) ExpireZoneStatuses(int tick, ZoneState zone)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        ImmutableArray<StatusEvent>.Builder statusEvents = ImmutableArray.CreateBuilder<StatusEvent>();
        for (int i = 0; i < entities.Length; i++)
        {
            EntityState entity = entities[i];
            StatusEffectsComponent next = entity.StatusEffects.TickExpire(tick, entity.Id, statusEvents);
            builder.Add(entity with { StatusEffects = next });
        }

        return (zone.WithEntities(builder.ToImmutable().OrderBy(e => e.Id.Value).ToImmutableArray()), statusEvents.ToImmutable());
    }

    private static ZoneState ReplaceEntity(ZoneState zone, int index, EntityState entity)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            builder.Add(i == index ? entity : entities[i]);
        }

        return zone.WithEntities(builder.ToImmutable().OrderBy(e => e.Id.Value).ToImmutableArray());
    }

    private static ImmutableArray<int> ResolveCastTargetIndices(ZoneState zone, int casterIndex, WorldCommand command, SkillDefinition skill)
    {
        if (command.TargetKind == CastTargetKind.Self)
        {
            return ImmutableArray.Create(casterIndex);
        }

        if (command.TargetKind == CastTargetKind.Entity)
        {
            if (command.TargetEntityId is null)
            {
                return ImmutableArray<int>.Empty;
            }

            int targetIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.TargetEntityId.Value);
            if (targetIndex < 0)
            {
                return ImmutableArray<int>.Empty;
            }

            return zone.Entities[targetIndex].IsAlive
                ? ImmutableArray.Create(targetIndex)
                : ImmutableArray<int>.Empty;
        }

        if (command.TargetKind != CastTargetKind.Point)
        {
            return ImmutableArray<int>.Empty;
        }

        Vec2Fix point = new(new Fix32(command.TargetPosXRaw), new Fix32(command.TargetPosYRaw));
        return ResolveAoeTargets(zone, casterIndex, point, new Fix32(skill.HitRadiusRaw), skill.MaxTargets);
    }

    private static ImmutableArray<int> ResolveAoeTargets(ZoneState zone, int casterIndex, Vec2Fix point, Fix32 hitRadius, int maxTargets)
    {
        if (hitRadius.Raw <= 0)
        {
            return ImmutableArray<int>.Empty;
        }

        ImmutableArray<EntityState> entities = zone.Entities;
        Fix32 radiusSq = hitRadius * hitRadius;
        List<(int Index, Fix32 DistSq, int EntityId)> eligible = new();

        for (int i = 0; i < entities.Length; i++)
        {
            if (i == casterIndex)
            {
                continue;
            }

            EntityState candidate = entities[i];
            if (!candidate.IsAlive)
            {
                continue;
            }

            Vec2Fix delta = candidate.Pos - point;
            Fix32 distSq = delta.LengthSq();
            if (distSq > radiusSq)
            {
                continue;
            }

            eligible.Add((i, distSq, candidate.Id.Value));
        }

        if (eligible.Count == 0)
        {
            return ImmutableArray<int>.Empty;
        }

        IEnumerable<(int Index, Fix32 DistSq, int EntityId)> ordered = eligible
            .OrderBy(e => e.DistSq)
            .ThenBy(e => e.EntityId);

        if (maxTargets > 0)
        {
            ordered = ordered.Take(maxTargets);
        }

        return ordered
            .Select(e => e.Index)
            .ToImmutableArray();
    }

    private static SkillDefinition? FindSkill(SimulationConfig config, SkillId skillId)
    {
        ImmutableArray<SkillDefinition> skills = config.SkillDefinitions.IsDefault
            ? ImmutableArray<SkillDefinition>.Empty
            : config.SkillDefinitions;

        for (int i = 0; i < skills.Length; i++)
        {
            if (skills[i].Id.Value == skillId.Value)
            {
                return skills[i];
            }
        }

        return null;
    }

    private static Vec2Fix FindSpawn(TileMap map, Fix32 radius)
    {
        Fix32 half = new(Fix32.OneRaw / 2);

        for (int y = 1; y < map.Height - 1; y++)
        {
            for (int x = 1; x < map.Width - 1; x++)
            {
                if (map.Get(x, y) == TileKind.Solid)
                {
                    continue;
                }

                Vec2Fix candidate = new(Fix32.FromInt(x) + half, Fix32.FromInt(y) + half);
                if (!Physics2D.OverlapsSolidTile(candidate, radius, map))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Unable to find a valid spawn position.");
    }
}
