using System.Collections.Immutable;
using System.Security.Cryptography;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class LootSimulationTests
{
    [Fact]
    public void SameCombat_SameLoot()
    {
        SimulationConfig config = CreateConfig(99);

        WorldState first = RunNpcKillScenario(config);
        WorldState second = RunNpcKillScenario(config);

        LootEntityState firstLoot = Assert.Single(first.LootEntities);
        LootEntityState secondLoot = Assert.Single(second.LootEntities);

        Assert.Equal(firstLoot.Id, secondLoot.Id);
        Assert.Equal(firstLoot.ZoneId, secondLoot.ZoneId);
        Assert.Equal(firstLoot.Pos, secondLoot.Pos);

        Assert.Equal(new ItemStack("gold.coin", 3), firstLoot.Items[0]);
        Assert.Equal(new ItemStack("potion.minor", 1), firstLoot.Items[1]);
        Assert.Equal(firstLoot.Items.ToArray(), secondLoot.Items.ToArray());
    }

    [Fact]
    public void ReplayChecksum_Stable_WithLoot()
    {
        SimulationConfig config = CreateConfig(1337);

        string checksumA = RunScenarioChecksum(config);
        string checksumB = RunScenarioChecksum(config);

        Assert.Equal(checksumA, checksumB);
    }

    private static WorldState RunNpcKillScenario(SimulationConfig config)
    {
        WorldState initial = BuildWorldWithPlayerAndNpc();

        return Simulation.Step(config, initial, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.AttackIntent,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                TargetEntityId: new EntityId(2)))));
    }

    private static string RunScenarioChecksum(SimulationConfig config)
    {
        WorldState state = RunNpcKillScenario(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                Kind: WorldCommandKind.LootIntent,
                EntityId: new EntityId(1),
                ZoneId: new ZoneId(1),
                LootEntityId: new EntityId(-2)))));

        return ComputeLootAwareChecksum(state);
    }


    private static string ComputeLootAwareChecksum(WorldState state)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(StateChecksum.Compute(state));

        ImmutableArray<LootEntityState> orderedLoot = state.LootEntities
            .OrderBy(l => l.Id.Value)
            .ToImmutableArray();

        writer.Write(orderedLoot.Length);
        foreach (LootEntityState loot in orderedLoot)
        {
            writer.Write(loot.Id.Value);
            writer.Write(loot.ZoneId.Value);
            writer.Write(loot.Pos.X.Raw);
            writer.Write(loot.Pos.Y.Raw);
            writer.Write(loot.Items.Length);

            foreach (ItemStack item in loot.Items)
            {
                writer.Write(item.ItemId);
                writer.Write(item.Quantity);
            }
        }

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static WorldState BuildWorldWithPlayerAndNpc()
    {
        TileMap map = BuildOpenMap(8, 8);
        Vec2Fix pos = new(Fix32.FromInt(3), Fix32.FromInt(3));

        EntityState player = new(
            Id: new EntityId(1),
            Pos: pos,
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 100,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Player);

        EntityState npc = new(
            Id: new EntityId(2),
            Pos: pos,
            Vel: Vec2Fix.Zero,
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 10,
            AttackCooldownTicks: 10,
            LastAttackTick: -10,
            Kind: EntityKind.Npc,
            NextWanderChangeTick: int.MaxValue,
            WanderX: 0,
            WanderY: 0);

        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(player, npc));
        ImmutableArray<EntityLocation> locations = ImmutableArray.Create(
            new EntityLocation(new EntityId(1), new ZoneId(1)),
            new EntityLocation(new EntityId(2), new ZoneId(1)));

        return new WorldState(Tick: 0, Zones: ImmutableArray.Create(zone), EntityLocations: locations, LootEntities: ImmutableArray<LootEntityState>.Empty);
    }

    private static TileMap BuildOpenMap(int width, int height)
    {
        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles.Add(border ? TileKind.Solid : TileKind.Empty);
            }
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
