using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Xunit;

namespace Game.Core.Tests;

public sealed class ThreatAccumulationTests
{
    [Fact]
    [Trait("Category", "PR67")]
    public void Threat_Accumulates_Per_Player_And_Stays_In_Canonical_Order()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = CreateWorld(config);

        state = EnterPlayers(state, new EntityId(10), new EntityId(11));
        state = Cast(state, new EntityId(10), NpcId);
        state = Cast(state, new EntityId(11), NpcId);
        state = Cast(state, new EntityId(11), NpcId);

        EntityState npc = GetNpc(state);
        ImmutableArray<ThreatEntry> entries = npc.Threat.OrderedEntries();

        Assert.Equal(2, entries.Length);
        Assert.Equal(10, entries[0].SourceEntityId.Value);
        Assert.Equal(10, entries[0].Threat);
        Assert.Equal(11, entries[1].SourceEntityId.Value);
        Assert.Equal(20, entries[1].Threat);
    }

    private static readonly EntityId NpcId = new(100001);

    private readonly record struct ScenarioRunResult(string FinalChecksum, ImmutableArray<int> AggroTrace);

    private static WorldState EnterPlayers(WorldState state, params EntityId[] players)
    {
        ImmutableArray<WorldCommand> commands = players
            .OrderBy(p => p.Value)
            .Select((player, index) => new WorldCommand(
                WorldCommandKind.EnterZone,
                player,
                new ZoneId(1),
                SpawnPos: new Vec2Fix(Fix32.FromInt(5 + index), Fix32.FromInt(5))))
            .ToImmutableArray();

        return Simulation.Step(CreateConfig(), state, new Inputs(commands));
    }

    private static WorldState Cast(WorldState state, EntityId caster, EntityId target)
    {
        return Simulation.Step(CreateConfig(), state, new Inputs(ImmutableArray.Create(
            new WorldCommand(
                WorldCommandKind.CastSkill,
                caster,
                new ZoneId(1),
                TargetEntityId: target,
                SkillId: new SkillId(1),
                TargetKind: CastTargetKind.Entity))));
    }

    private static EntityState GetNpc(WorldState state) => Assert.Single(state.Zones[0].Entities.Where(e => e.Id.Value == NpcId.Value));

    private static WorldState CreateWorld(SimulationConfig config)
    {
        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(10), Fix32.FromInt(10))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return Simulation.CreateInitialState(config, new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
    }

    private static SimulationConfig CreateConfig() => new(
        Seed: 67,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 10)),
        Invariants: InvariantOptions.Enabled);
}

public sealed class AggroDeterminismTests
{
    [Fact]
    [Trait("Category", "PR67")]
    public void Aggro_Uses_Threat_Max_Then_Lowest_PlayerId_TieBreaker()
    {
        SimulationConfig config = ThreatAccumulationTests_CreateConfig();
        WorldState state = ThreatAccumulationTests_CreateWorld(config);

        state = Step(state, ImmutableArray.Create(
            Enter(10, 4),
            Enter(11, 6)));

        state = Step(state, ImmutableArray.Create(Cast(10), Cast(11)));
        EntityState npcAfterTie = GetNpc(state);
        Assert.Equal(-1, npcAfterTie.WanderX);

        state = Step(state, ImmutableArray.Create(Cast(11)));
        EntityState npcAfterLeadChange = GetNpc(state);
        Assert.Equal(1, npcAfterLeadChange.WanderX);
    }

    private static readonly EntityId NpcId = new(100001);

    private readonly record struct ScenarioRunResult(string FinalChecksum, ImmutableArray<int> AggroTrace);

    private static WorldState Step(WorldState state, ImmutableArray<WorldCommand> commands) => Simulation.Step(ThreatAccumulationTests_CreateConfig(), state, new Inputs(commands));

    private static WorldCommand Enter(int playerId, int x) => new(WorldCommandKind.EnterZone, new EntityId(playerId), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(x), Fix32.FromInt(5)));

    private static WorldCommand Cast(int playerId) => new(
        WorldCommandKind.CastSkill,
        new EntityId(playerId),
        new ZoneId(1),
        TargetEntityId: NpcId,
        SkillId: new SkillId(1),
        TargetKind: CastTargetKind.Entity);

    private static EntityState GetNpc(WorldState state) => Assert.Single(state.Zones[0].Entities.Where(e => e.Id.Value == NpcId.Value));

    private static WorldState ThreatAccumulationTests_CreateWorld(SimulationConfig config)
    {
        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return Simulation.CreateInitialState(config, new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
    }

    private static SimulationConfig ThreatAccumulationTests_CreateConfig() => new(
        Seed: 67,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 10)),
        Invariants: InvariantOptions.Enabled);
}

public sealed class TankVsDpsScenarioReplayTests
{
    [Fact]
    [Trait("Category", "PR67")]
    [Trait("Category", "ReplayVerify")]
    public void TankVsDps_Replay_Is_Deterministic_And_Aggro_Follows_Threat()
    {
        SimulationConfig config = AggroDeterminismTests_ThreatAccumulationTests_CreateConfig();
        ScenarioRunResult baseline = Run(config, restartTick: null);
        ScenarioRunResult resumed = Run(config, restartTick: 10);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
        Assert.Contains(-1, baseline.AggroTrace);
        Assert.Contains(1, baseline.AggroTrace);
    }

    private static readonly EntityId NpcId = new(100001);

    private readonly record struct ScenarioRunResult(string FinalChecksum, ImmutableArray<int> AggroTrace);

    private static ScenarioRunResult Run(SimulationConfig config, int? restartTick)
    {
        WorldState state = AggroDeterminismTests_ThreatAccumulationTests_CreateWorld(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(20), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(5))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(21), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(5))))));

        ImmutableArray<int>.Builder aggroTrace = ImmutableArray.CreateBuilder<int>();
        for (int tick = 0; tick < 30; tick++)
        {
            ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();
            if (tick < 12)
            {
                commands.Add(new WorldCommand(WorldCommandKind.CastSkill, new EntityId(20), new ZoneId(1), TargetEntityId: NpcId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity));
            }
            if (tick >= 8 && tick % 4 == 0)
            {
                commands.Add(new WorldCommand(WorldCommandKind.CastSkill, new EntityId(21), new ZoneId(1), TargetEntityId: NpcId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity));
            }

            state = Simulation.Step(config, state, new Inputs(commands.ToImmutable()));
            aggroTrace.Add(GetNpc(state).WanderX);

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snap = Game.Persistence.WorldStateSerializer.SaveToBytes(state);
                state = Game.Persistence.WorldStateSerializer.LoadFromBytes(snap);
            }
        }

        return new ScenarioRunResult(StateChecksum.Compute(state), aggroTrace.ToImmutable());
    }

    private static EntityState GetNpc(WorldState state) => Assert.Single(state.Zones[0].Entities.Where(e => e.Id.Value == NpcId.Value));

    private static WorldState AggroDeterminismTests_ThreatAccumulationTests_CreateWorld(SimulationConfig config)
    {
        ZoneDefinition zoneDef = new(
            new ZoneId(1),
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray.Create(new NpcSpawnDefinition("boss", 1, 1, ImmutableArray.Create(new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(5))))),
            null,
            null,
            ImmutableArray<EncounterDefinition>.Empty);

        return Simulation.CreateInitialState(config, new ZoneDefinitions(ImmutableArray.Create(zoneDef)));
    }

    private static SimulationConfig AggroDeterminismTests_ThreatAccumulationTests_CreateConfig() => new(
        Seed: 77,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 32,
        MapHeight: 32,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 9999,
        NpcAggroRange: Fix32.FromInt(32),
        SkillDefinitions: ImmutableArray.Create(
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 2),
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, 1, 0, 0, 0, 0, CastTargetKind.Entity, BaseAmount: 5)),
        Invariants: InvariantOptions.Enabled);
}
