using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

[Trait("Category", "PR72")]
public sealed class AggroChaseIntegrationTests
{
    [Fact]
    public void Threat_Drives_Chase_And_Retargets_Back_When_Threat_Changes()
    {
        SimulationConfig config = CreateConfig(7201);
        WorldState state = Simulation.CreateInitialState(config, BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId playerA = new(10);
        EntityId playerB = new(11);

        state = Step(config, state, ImmutableArray.Create(
            Enter(playerA, 2, 2),
            Enter(playerB, 9, 9)));

        for (int tick = 0; tick < 3; tick++)
        {
            state = Step(config, state, ImmutableArray.Create(Cast(playerA, npcId, 1)));
        }

        EntityState npcAfterA = GetNpc(state);
        Assert.Equal(MoveIntentType.ChaseEntity, npcAfterA.MoveIntent.Type);
        Assert.Equal(playerA.Value, npcAfterA.MoveIntent.TargetEntityId.Value);

        state = Step(config, state, ImmutableArray.Create(Cast(playerB, npcId, 2)));
        EntityState npcAfterBurst = GetNpc(state);
        Assert.Equal(playerB.Value, npcAfterBurst.MoveIntent.TargetEntityId.Value);

        for (int tick = 0; tick < 3; tick++)
        {
            state = Step(config, state, ImmutableArray.Create(Cast(playerA, npcId, 2)));
        }

        bool returnedToA = false;
        for (int tick = 0; tick < 8 && !returnedToA; tick++)
        {
            state = Step(config, state, ImmutableArray<WorldCommand>.Empty);
            returnedToA = GetNpc(state).MoveIntent.TargetEntityId.Value == playerA.Value;
        }

        Assert.True(returnedToA);
    }

    private static WorldCommand Enter(EntityId id, int x, int y)
        => new(WorldCommandKind.EnterZone, id, new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(y)));

    private static WorldCommand Cast(EntityId caster, EntityId target, int skillId)
        => new(WorldCommandKind.CastSkill, caster, new ZoneId(1), TargetEntityId: target, SkillId: new SkillId(skillId), TargetKind: CastTargetKind.Entity);

    private static WorldState Step(SimulationConfig config, WorldState state, ImmutableArray<WorldCommand> commands)
        => Simulation.Step(config, state, new Inputs(commands));

    private static EntityState GetNpc(WorldState state)
        => Assert.Single(state.Zones[0].Entities.Where(e => e.Id.Value == 100001));

    private static ZoneDefinitions BuildZone()
    {
        ZoneDefinition zone = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(16), Fix32.FromInt(16)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static SimulationConfig CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 2),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 12)),
        Invariants: InvariantOptions.Enabled);
}

[Trait("Category", "PR72")]
public sealed class RetargetDeterminismTests
{
    [Fact]
    public void Tie_Uses_Lowest_PlayerId_And_Cooldown_Prevents_FlipFlop()
    {
        SimulationConfig config = AggroChaseIntegrationTests_CreateConfig(7202);
        WorldState state = Simulation.CreateInitialState(config, AggroChaseIntegrationTests_BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId low = new(20);
        EntityId high = new(21);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, low, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(3))),
            new WorldCommand(WorldCommandKind.EnterZone, high, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8), Fix32.FromInt(8))))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, low, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity),
            new WorldCommand(WorldCommandKind.CastSkill, high, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity))));

        EntityState npc = Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));
        Assert.Equal(low.Value, npc.MoveIntent.TargetEntityId.Value);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, high, zoneId, TargetEntityId: npcId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity))));
        npc = Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));
        Assert.Equal(high.Value, npc.MoveIntent.TargetEntityId.Value);

        for (int i = 0; i < 3; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
                new WorldCommand(WorldCommandKind.CastSkill, low, zoneId, TargetEntityId: npcId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity))));

            npc = Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));
            Assert.Equal(high.Value, npc.MoveIntent.TargetEntityId.Value);
        }

        for (int i = 0; i < 3; i++)
        {
            state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        }

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, low, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity))));

        npc = Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));
        Assert.Equal(low.Value, npc.MoveIntent.TargetEntityId.Value);
    }

    private static ZoneDefinitions AggroChaseIntegrationTests_BuildZone()
    {
        ZoneDefinition zone = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(16), Fix32.FromInt(16)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static SimulationConfig AggroChaseIntegrationTests_CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 1),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 4)),
        Invariants: InvariantOptions.Enabled);
}

public sealed class ReplayVerify_AggroChase
{
    [Fact]
    [Trait("Category", "PR72")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void Replay_Is_Stable_With_Restart_And_Target_Transitions()
    {
        ScenarioRunResult baseline = Run(restartTick: null);
        ScenarioRunResult resumed = Run(restartTick: 15);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
        Assert.Contains(30, baseline.TargetTrace);
        Assert.Contains(31, baseline.TargetTrace);
    }

    private static ScenarioRunResult Run(int? restartTick)
    {
        SimulationConfig config = AggroChaseIntegrationTests_CreateConfig(7203);
        WorldState state = Simulation.CreateInitialState(config, AggroChaseIntegrationTests_BuildZone());
        ZoneId zoneId = new(1);
        EntityId npcId = new(100001);
        EntityId a = new(30);
        EntityId b = new(31);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, a, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, b, zoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(11), Fix32.FromInt(11))))));

        ImmutableArray<int>.Builder targetTrace = ImmutableArray.CreateBuilder<int>();
        for (int tick = 0; tick < 30; tick++)
        {
            ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();
            if (tick % 3 == 0)
            {
                commands.Add(new WorldCommand(WorldCommandKind.CastSkill, a, zoneId, TargetEntityId: npcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity));
            }

            if (tick >= 10 && tick % 5 == 0)
            {
                commands.Add(new WorldCommand(WorldCommandKind.CastSkill, b, zoneId, TargetEntityId: npcId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity));
            }

            state = Simulation.Step(config, state, new Inputs(commands.ToImmutable()));
            EntityState npc = Assert.Single(state.Zones[0].Entities.Where(e => e.Id == npcId));
            targetTrace.Add(npc.MoveIntent.TargetEntityId.Value);

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = Game.Persistence.WorldStateSerializer.SaveToBytes(state);
                state = Game.Persistence.WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return new ScenarioRunResult(StateChecksum.ComputeGlobalChecksum(state), targetTrace.ToImmutable());
    }

    private readonly record struct ScenarioRunResult(string FinalChecksum, ImmutableArray<int> TargetTrace);

    private static ZoneDefinitions AggroChaseIntegrationTests_BuildZone()
    {
        ZoneDefinition zone = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(16), Fix32.FromInt(16)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static SimulationConfig AggroChaseIntegrationTests_CreateConfig(int seed) => new(
        Seed: seed,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 16,
        MapHeight: 16,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 2),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 8)),
        Invariants: InvariantOptions.Enabled);
}
