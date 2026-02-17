using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class NpcSimulationTests
{
    [Fact]
    public void NpcSpawn_IsDeterministic()
    {
        SimulationConfig config = CreateConfig(123);

        WorldState a = Simulation.CreateInitialState(config);
        WorldState b = Simulation.CreateInitialState(config);

        ZoneState za = a.Zones.Single();
        ZoneState zb = b.Zones.Single();

        EntityState[] npcsA = za.Entities.Where(e => e.Kind == EntityKind.Npc).OrderBy(e => e.Id.Value).ToArray();
        EntityState[] npcsB = zb.Entities.Where(e => e.Kind == EntityKind.Npc).OrderBy(e => e.Id.Value).ToArray();

        Assert.Equal(config.NpcCountPerZone, npcsA.Length);
        Assert.Equal(npcsA.Length, npcsB.Length);

        for (int i = 0; i < npcsA.Length; i++)
        {
            Assert.Equal(npcsA[i].Id.Value, npcsB[i].Id.Value);
            Assert.Equal(npcsA[i].Pos.X.Raw, npcsB[i].Pos.X.Raw);
            Assert.Equal(npcsA[i].Pos.Y.Raw, npcsB[i].Pos.Y.Raw);
        }
    }

    [Fact]
    public void NpcWander_IsDeterministic()
    {
        SimulationConfig config = CreateConfig(321);

        string checksumA = RunNoPlayerTicks(config, 120);
        string checksumB = RunNoPlayerTicks(config, 120);

        Assert.Equal(checksumA, checksumB);
    }

    [Fact]
    public void NpcCanKillPlayer_Deterministic()
    {
        SimulationConfig config = CreateConfig(777) with { NpcCountPerZone = 1 };

        int deathTickA = RunKillScenario(config);
        int deathTickB = RunKillScenario(config);

        Assert.True(deathTickA > 0);
        Assert.Equal(deathTickA, deathTickB);
    }

    private static string RunNoPlayerTicks(SimulationConfig config, int ticks)
    {
        WorldState state = Simulation.CreateInitialState(config);

        for (int i = 0; i < ticks; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        return StateChecksum.Compute(state);
    }

    private static int RunKillScenario(SimulationConfig config)
    {
        WorldState state = Simulation.CreateInitialState(config);
        ZoneState zone = state.Zones.Single();
        EntityState npc = zone.Entities.Single(e => e.Kind == EntityKind.Npc);

        Vec2Fix playerSpawn = npc.Pos + new Vec2Fix(new Fix32(Fix32.OneRaw / 4), Fix32.Zero);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), zone.Id, SpawnPos: playerSpawn))));

        for (int i = 0; i < 300; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
            if (!state.TryGetZone(zone.Id, out ZoneState current))
            {
                continue;
            }

            if (state.PlayerDeathAuditLog.Any(e => e.PlayerEntityId.Value == 1))
            {
                return state.Tick;
            }
        }

        return -1;
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new(16384),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 5,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
