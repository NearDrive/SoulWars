using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class CastWindupTests
{
    [Fact]
    public void CastTime_DelaysEffect_UntilExecuteTick()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnArena(config);

        state = StepCast(state, config, casterId: 1, targetId: 3, skillId: 30);
        Assert.Empty(state.CombatEvents);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        Assert.Empty(state.CombatEvents);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        Assert.Empty(state.CombatEvents);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        CombatEvent evt = Assert.Single(state.CombatEvents);
        Assert.Equal(CombatEventType.Damage, evt.Type);
        Assert.Equal(30, evt.SkillId.Value);
    }

    [Fact]
    public void PendingCast_CancelledByStun_NoEffect()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnArena(config);

        state = StepCast(state, config, casterId: 1, targetId: 3, skillId: 30);
        state = StepCast(state, config, casterId: 2, targetId: 1, skillId: 31);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        CombatEvent cancel = Assert.Single(state.CombatEvents.Where(e => e.Type == CombatEventType.Cancelled));
        Assert.Equal(1, cancel.SourceId.Value);
        Assert.DoesNotContain(state.CombatEvents, e => e.Type == CombatEventType.Damage && e.SkillId.Value == 30);
    }

    [Fact]
    public void PendingCast_ExecutionOrder_Canonical()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnArena(config);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            CastCommand(casterId: 1, targetId: 3, skillId: 32),
            CastCommand(casterId: 2, targetId: 3, skillId: 32))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        CombatEvent[] events = state.CombatEvents.Where(e => e.SkillId.Value == 32 && e.Type == CombatEventType.Damage).ToArray();
        Assert.Equal(2, events.Length);
        Assert.True(events[0].SourceId.Value < events[1].SourceId.Value);
    }

    [Fact]
    public void ReplayVerify_WindupScenario_Passes()
    {
        SimulationConfig config = CreateConfig();
        string a = RunWindupChecksum(config);
        string b = RunWindupChecksum(config);
        Assert.Equal(a, b);
    }

    private static string RunWindupChecksum(SimulationConfig config)
    {
        WorldState state = SpawnArena(config);
        state = StepCast(state, config, casterId: 1, targetId: 3, skillId: 30);
        state = StepCast(state, config, casterId: 2, targetId: 1, skillId: 31);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        return StateChecksum.ComputeGlobalChecksum(state);
    }

    private static WorldState StepCast(WorldState state, SimulationConfig config, int casterId, int targetId, int skillId)
    {
        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(CastCommand(casterId, targetId, skillId))));
    }

    private static WorldCommand CastCommand(int casterId, int targetId, int skillId)
    {
        return new WorldCommand(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(casterId),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(skillId),
            TargetEntityId: new EntityId(targetId),
            TargetKind: CastTargetKind.Entity);
    }

    private static WorldState SpawnArena(SimulationConfig config)
    {
        TileMap map = BuildOpenMap(config.MapWidth, config.MapHeight);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty,
            CombatEvents: ImmutableArray<CombatEvent>.Empty);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(6), Fix32.FromInt(2))))));
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

    private static SimulationConfig CreateConfig()
    {
        return new SimulationConfig(
            Seed: 777,
            TickHz: 20,
            DtFix: new Fix32(3277),
            MoveSpeed: Fix32.FromInt(4),
            MaxSpeed: Fix32.FromInt(4),
            Radius: new Fix32(16384),
            ZoneCount: 1,
            MapWidth: 16,
            MapHeight: 16,
            NpcCountPerZone: 0,
            NpcWanderPeriodTicks: 30,
            NpcAggroRange: Fix32.FromInt(6),
            SkillDefinitions: ImmutableArray.Create(
                new SkillDefinition(new SkillId(30), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 2, CastTimeTicks: 3, GlobalCooldownTicks: 0, ResourceCost: 0, TargetKind: CastTargetKind.Entity, EffectKind: SkillEffectKind.Damage, BaseAmount: 8, CoefRaw: 0),
                new SkillDefinition(new SkillId(31), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, TargetKind: CastTargetKind.Entity, EffectKind: SkillEffectKind.Damage, BaseAmount: 1, CoefRaw: 0, StatusEffect: new OptionalStatusEffect(StatusEffectType.Stun, DurationTicks: 4, MagnitudeRaw: 0)),
                new SkillDefinition(new SkillId(32), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, CastTimeTicks: 2, GlobalCooldownTicks: 0, ResourceCost: 0, TargetKind: CastTargetKind.Entity, EffectKind: SkillEffectKind.Damage, BaseAmount: 4, CoefRaw: 0)),
            Invariants: InvariantOptions.Enabled);
    }
}
