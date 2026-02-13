using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
    private const int DefaultMaxHp = 100;
    private const int DefaultAttackDamage = 10;
    private const int DefaultAttackCooldownTicks = 10;
    private const int NpcSpawnMaxAttempts = 64;

    public static WorldState CreateInitialState(SimulationConfig config)
    {
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
                Entities: SpawnNpcs(config, zoneId, map)));
        }

        ImmutableArray<ZoneState> initialZones = zones
            .OrderBy(z => z.Id.Value)
            .ToImmutableArray();

        return new WorldState(
            Tick: 0,
            Zones: initialZones,
            EntityLocations: BuildEntityLocations(initialZones));
    }

    public static WorldState Step(SimulationConfig config, WorldState state, Inputs inputs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputs);

        ImmutableArray<WorldCommand> commands = inputs.Commands.IsDefault
            ? ImmutableArray<WorldCommand>.Empty
            : inputs.Commands;

        WorldState updated = state with { Tick = state.Tick + 1 };

        ImmutableArray<(WorldCommand Command, int Index)> orderedCommands = commands
            .Select((command, index) => (Command: command, Index: index))
            .OrderBy(x => x.Command.ZoneId.Value)
            .ThenBy(x => x.Command.EntityId.Value)
            .ThenBy(x => (int)x.Command.Kind)
            .ThenBy(x => x.Index)
            .ToImmutableArray();

        foreach (WorldCommand command in commands.Where(c => c.Kind == WorldCommandKind.TeleportIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            updated = ApplyTeleportIntent(config, updated, command);
        }

        foreach ((ZoneId zoneId, ImmutableArray<WorldCommand> zoneCommands) in orderedCommands
                     .Where(x => x.Command.Kind is WorldCommandKind.EnterZone or WorldCommandKind.MoveIntent or WorldCommandKind.AttackIntent or WorldCommandKind.LeaveZone)
                     .GroupBy(x => x.Command.ZoneId.Value)
                     .OrderBy(g => g.Key)
                     .Select(g => (new ZoneId(g.Key), g.Select(v => v.Command).ToImmutableArray())))
        {
            if (!updated.TryGetZone(zoneId, out ZoneState zone))
            {
                continue;
            }

            ZoneState nextZone = ProcessZoneCommands(config, updated.Tick, zone, zoneCommands);
            updated = updated.WithZoneUpdated(nextZone);
            updated = RebuildEntityLocations(updated);
        }

        foreach (ZoneState zone in updated.Zones.OrderBy(z => z.Id.Value).ToArray())
        {
            ZoneState nextZone = RunNpcAiAndApply(config, updated.Tick, zone);
            updated = updated.WithZoneUpdated(nextZone);
        }

        updated = RebuildEntityLocations(updated);

        if (config.Invariants.EnableCoreInvariants)
        {
            CoreInvariants.Validate(updated, updated.Tick);
        }

        _ = config.MaxSpeed;
        return updated;
    }

    private static ZoneState RunNpcAiAndApply(SimulationConfig config, int tick, ZoneState zone)
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
            updatedZone = ApplyMoveIntent(config, updatedZone, move);
        }

        foreach (WorldCommand attack in npcCommands.Where(c => c.Kind == WorldCommandKind.AttackIntent).OrderBy(c => c.EntityId.Value))
        {
            updatedZone = ApplyAttackIntent(tick, updatedZone, attack);
        }

        return updatedZone;
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

    private static ImmutableArray<EntityState> SpawnNpcs(SimulationConfig config, ZoneId zoneId, TileMap map)
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


    private static WorldState ApplyTeleportIntent(SimulationConfig config, WorldState state, WorldCommand command)
    {
        if (command.ToZoneId is null)
        {
            return state;
        }

        ZoneId fromZoneId = command.ZoneId;
        ZoneId toZoneId = command.ToZoneId.Value;

        if (fromZoneId.Value == toZoneId.Value)
        {
            return state;
        }

        if (!state.TryGetZone(fromZoneId, out ZoneState fromZone) || !state.TryGetZone(toZoneId, out ZoneState toZone))
        {
            return state;
        }

        ImmutableArray<EntityState> fromEntities = fromZone.Entities;
        int fromIndex = ZoneEntities.FindIndex(fromZone.EntitiesData.AliveIds, command.EntityId);
        if (fromIndex < 0)
        {
            return state;
        }

        EntityState entity = fromEntities[fromIndex];

        ZoneState nextFrom = fromZone.WithEntities(fromEntities
            .Where(e => e.Id.Value != command.EntityId.Value)
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());

        Vec2Fix spawn = FindSpawn(toZone.Map, config.Radius);
        EntityState teleported = entity with
        {
            Pos = spawn,
            Vel = Vec2Fix.Zero
        };

        ZoneState nextTo = toZone.WithEntities(toZone.Entities
            .Add(teleported)
            .GroupBy(e => e.Id.Value)
            .Select(g => g.Last())
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());

        WorldState updated = state.WithZoneUpdated(nextFrom).WithZoneUpdated(nextTo);
        return updated.WithEntityLocation(command.EntityId, toZoneId);
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

    private static ZoneState ProcessZoneCommands(SimulationConfig config, int tick, ZoneState zone, ImmutableArray<WorldCommand> zoneCommands)
    {
        ZoneState current = zone;

        foreach (WorldCommand command in zoneCommands.Where(c => c.Kind is WorldCommandKind.EnterZone))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyEnterZone(config, current, command);
        }

        foreach (WorldCommand command in zoneCommands.Where(c => c.Kind is WorldCommandKind.MoveIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyMoveIntent(config, current, command);
        }

        foreach (WorldCommand command in zoneCommands.Where(c => c.Kind is WorldCommandKind.AttackIntent))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyAttackIntent(tick, current, command);
        }

        foreach (WorldCommand command in zoneCommands.Where(c => c.Kind is WorldCommandKind.LeaveZone))
        {
            if (!IsValidCommand(command))
            {
                continue;
            }

            current = ApplyLeaveZone(current, command);
        }

        return current;
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

    private static ZoneState ApplyMoveIntent(SimulationConfig config, ZoneState zone, WorldCommand command)
    {
        ImmutableArray<EntityState> entities = zone.Entities;
        int entityIndex = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, command.EntityId);

        if (entityIndex < 0)
        {
            return zone;
        }

        EntityState entity = entities[entityIndex];
        if (!entity.IsAlive)
        {
            return zone;
        }

        PlayerInput playerInput = new(entity.Id, command.MoveX, command.MoveY);

        EntityState updated = Physics2D.Integrate(
            entity,
            playerInput,
            zone.Map,
            config.DtFix,
            config.MoveSpeed,
            config.Radius);

        ImmutableArray<EntityState>.Builder builder = ImmutableArray.CreateBuilder<EntityState>(entities.Length);

        for (int i = 0; i < entities.Length; i++)
        {
            builder.Add(i == entityIndex ? updated : entities[i]);
        }

        return zone.WithEntities(builder
            .ToImmutable()
            .OrderBy(e => e.Id.Value)
            .ToImmutableArray());
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
