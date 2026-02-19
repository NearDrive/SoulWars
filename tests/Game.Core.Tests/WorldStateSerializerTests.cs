using System.Collections.Immutable;
using System.Security.Cryptography;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class WorldStateSerializerTests
{
    [Fact]
    public void SaveLoad_RoundTrip_EqualsOriginalChecksum()
    {
        SimulationConfig config = CreateConfig(seed: 1337);
        WorldState world = RunSimulation(config, BuildInputTimeline(config.ZoneCount, totalTicks: 200));

        string checksumA = ComputeDeterministicChecksum(world);

        byte[] serialized = WorldStateSerializer.SaveToBytes(world);
        WorldState loaded = WorldStateSerializer.LoadFromBytes(serialized);

        string checksumB = ComputeDeterministicChecksum(loaded);
        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void SaveLoad_MidSimulation_Continue_EqualsBaseline()
    {
        const int totalTicks = 500;
        const int splitTick = 250;

        SimulationConfig config = CreateConfig(seed: 2025);
        ImmutableArray<Inputs> timeline = BuildInputTimeline(config.ZoneCount, totalTicks);

        WorldState baseline = RunSimulation(config, timeline);
        string checksumBaseline = ComputeDeterministicChecksum(baseline);

        WorldState mid = Simulation.CreateInitialState(config);
        for (int tick = 0; tick < splitTick; tick++)
        {
            mid = Simulation.Step(config, mid, timeline[tick]);
        }

        byte[] bytes = WorldStateSerializer.SaveToBytes(mid);
        WorldState resumed = WorldStateSerializer.LoadFromBytes(bytes);

        for (int tick = splitTick; tick < totalTicks; tick++)
        {
            resumed = Simulation.Step(config, resumed, timeline[tick]);
        }

        string checksumMid = ComputeDeterministicChecksum(resumed);
        Assert.Equal(checksumBaseline, checksumMid);
    }

    [Fact]
    public void Load_InvalidMagic_Throws()
    {
        byte[] invalid = new byte[16];
        Assert.Throws<InvalidDataException>(() => WorldStateSerializer.LoadFromBytes(invalid));
    }

    private static WorldState RunSimulation(SimulationConfig config, ImmutableArray<Inputs> timeline)
    {
        WorldState state = Simulation.CreateInitialState(config);
        foreach (Inputs inputs in timeline)
        {
            state = Simulation.Step(config, state, inputs);
        }

        return state;
    }

    private static ImmutableArray<Inputs> BuildInputTimeline(int zoneCount, int totalTicks)
    {
        const int playerEntityId = 1;
        ZoneId currentZone = new(1);
        SimRng rng = new(777);

        ImmutableArray<Inputs>.Builder timeline = ImmutableArray.CreateBuilder<Inputs>(totalTicks);

        for (int tick = 1; tick <= totalTicks; tick++)
        {
            List<WorldCommand> commands = new();

            if (tick == 1)
            {
                commands.Add(new WorldCommand(
                    Kind: WorldCommandKind.EnterZone,
                    EntityId: new EntityId(playerEntityId),
                    ZoneId: currentZone));
            }

            sbyte moveX = (sbyte)rng.NextInt(-1, 2);
            sbyte moveY = (sbyte)rng.NextInt(-1, 2);
            commands.Add(new WorldCommand(
                Kind: WorldCommandKind.MoveIntent,
                EntityId: new EntityId(playerEntityId),
                ZoneId: currentZone,
                MoveX: moveX,
                MoveY: moveY));

            if (tick % 7 == 0)
            {
                commands.Add(new WorldCommand(
                    Kind: WorldCommandKind.AttackIntent,
                    EntityId: new EntityId(playerEntityId),
                    ZoneId: currentZone,
                    TargetEntityId: new EntityId((currentZone.Value * 100000) + 1)));
            }

            bool shouldTeleport = zoneCount > 1 && tick % 25 == 0 && rng.NextInt(0, 2) == 1;
            if (shouldTeleport)
            {
                ZoneId toZone = new(currentZone.Value == zoneCount ? 1 : currentZone.Value + 1);
                commands.Add(new WorldCommand(
                    Kind: WorldCommandKind.TeleportIntent,
                    EntityId: new EntityId(playerEntityId),
                    ZoneId: currentZone,
                    ToZoneId: toZone));
                currentZone = toZone;
            }

            timeline.Add(new Inputs(commands.ToImmutableArray()));
        }

        return timeline.MoveToImmutable();
    }

    private static string ComputeDeterministicChecksum(WorldState world)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(world.Tick);

        ImmutableArray<ZoneState> zones = world.Zones.OrderBy(z => z.Id.Value).ToImmutableArray();
        writer.Write(zones.Length);

        foreach (ZoneState zone in zones)
        {
            writer.Write(zone.Id.Value);
            writer.Write(zone.Map.Width);
            writer.Write(zone.Map.Height);
            writer.Write(zone.Map.Tiles.Length);
            foreach (TileKind tile in zone.Map.Tiles)
            {
                writer.Write((byte)tile);
            }

            ZoneEntities entities = zone.EntitiesData;
            writer.Write(entities.AliveIds.Length);

            for (int i = 0; i < entities.AliveIds.Length; i++)
            {
                writer.Write(entities.AliveIds[i].Value);
                writer.Write(entities.Masks[i].Bits);
                writer.Write((byte)entities.Kinds[i]);

                PositionComponent pos = entities.Positions[i];
                writer.Write(pos.Pos.X.Raw);
                writer.Write(pos.Pos.Y.Raw);
                writer.Write(pos.Vel.X.Raw);
                writer.Write(pos.Vel.Y.Raw);

                HealthComponent health = entities.Health[i];
                writer.Write(health.MaxHp);
                writer.Write(health.Hp);
                writer.Write(health.IsAlive);

                CombatComponent combat = entities.Combat[i];
                writer.Write(combat.Range.Raw);
                writer.Write(combat.Damage);
                writer.Write(combat.Defense);
                writer.Write(combat.MagicResist);
                writer.Write(combat.CooldownTicks);
                writer.Write(combat.LastAttackTick);

                AiComponent ai = entities.Ai[i];
                writer.Write(ai.NextWanderChangeTick);
                writer.Write(ai.WanderX);
                writer.Write(ai.WanderY);
            }
        }

        ImmutableArray<EntityLocation> locations = world.EntityLocations.OrderBy(l => l.Id.Value).ToImmutableArray();
        writer.Write(locations.Length);
        foreach (EntityLocation location in locations)
        {
            writer.Write(location.Id.Value);
            writer.Write(location.ZoneId.Value);
        }

        writer.Flush();
        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash);
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 2,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 2,
        NpcWanderPeriodTicks: 10,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
