using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class StatusEffectsTests
{
    [Fact]
    public void Stun_BlocksMovement_AndBlocksCasting()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config);

        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 20);
        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zoneAfterStun));

        WorldCommand move = new(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: 1, MoveY: 0);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(move)));
        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState movedZone));
        EntityState target = movedZone.Entities.Single(e => e.Id.Value == 2);
        Assert.Equal(Vec2Fix.Zero, target.Vel);

        WorldCommand castByStunned = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(2),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(21),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(1));

        CastResult result = Simulation.ValidateCastSkill(config, state.Tick + 1, movedZone, castByStunned);
        Assert.Equal(CastResult.Rejected_Stunned, result);
    }

    [Fact]
    public void Stun_RefreshesExpiresAtTick_Deterministic()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config);

        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 20);
        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 22);

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        StatusEffectInstance stun = target.StatusEffects.Effects.Single(e => e.Type == StatusEffectType.Stun);
        Assert.Equal(state.Tick + 6, stun.ExpiresAtTick);
    }

    [Fact]
    public void Slow_StrongestWins_TieBreakDeterministic()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config);

        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 23); // 0.8 source 1
        state = Cast(state, config, casterId: 3, targetId: 2, skillId: 24); // 0.6 source 3 stronger

        Assert.True(state.TryGetZone(new ZoneId(1), out ZoneState zone));
        EntityState target = zone.Entities.Single(e => e.Id.Value == 2);
        StatusEffectInstance slow = target.StatusEffects.Effects.Single(e => e.Type == StatusEffectType.Slow);
        Assert.Equal(Fix32.FromInt(6).Raw / 10, slow.MagnitudeRaw);
        Assert.Equal(3, slow.SourceEntityId.Value);
    }

    [Fact]
    public void StatusEvents_CanonicalOrder_StableAcrossRuns()
    {
        string runA = RunStatusSequence();
        string runB = RunStatusSequence();
        Assert.Equal(runA, runB);
    }

    [Fact]
    public void ReplayVerify_StatusScenario_Passes()
    {
        string first = RunStatusSequence();
        string second = RunStatusSequence();
        Assert.Equal(first, second);
    }

    private static string RunStatusSequence()
    {
        SimulationConfig config = CreateConfig();
        WorldState state = SpawnDuel(config);
        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 20);
        state = Cast(state, config, casterId: 1, targetId: 2, skillId: 22);
        state = Cast(state, config, casterId: 3, targetId: 2, skillId: 24);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(new WorldCommand(WorldCommandKind.MoveIntent, new EntityId(2), new ZoneId(1), MoveX: 1, MoveY: 0))));
        return StateChecksum.Compute(state);
    }

    private static WorldState Cast(WorldState state, SimulationConfig config, int casterId, int targetId, int skillId)
    {
        WorldCommand cast = new(
            Kind: WorldCommandKind.CastSkill,
            EntityId: new EntityId(casterId),
            ZoneId: new ZoneId(1),
            SkillId: new SkillId(skillId),
            TargetKind: CastTargetKind.Entity,
            TargetEntityId: new EntityId(targetId));

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(cast)));
    }

    private static WorldState SpawnDuel(SimulationConfig config)
    {
        TileMap map = BuildOpenMap(16, 16);
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(new ZoneState(new ZoneId(1), map, ImmutableArray<EntityState>.Empty)),
            EntityLocations: ImmutableArray<EntityLocation>.Empty,
            LootEntities: ImmutableArray<LootEntityState>.Empty);

        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))),
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
            Seed: 91,
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
                new SkillDefinition(new SkillId(20), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, StatusEffect: new OptionalStatusEffect(StatusEffectType.Stun, DurationTicks: 2, MagnitudeRaw: 0)),
                new SkillDefinition(new SkillId(21), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity),
                new SkillDefinition(new SkillId(22), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, StatusEffect: new OptionalStatusEffect(StatusEffectType.Stun, DurationTicks: 6, MagnitudeRaw: 0)),
                new SkillDefinition(new SkillId(23), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, StatusEffect: new OptionalStatusEffect(StatusEffectType.Slow, DurationTicks: 10, MagnitudeRaw: Fix32.FromInt(8).Raw / 10)),
                new SkillDefinition(new SkillId(24), Fix32.FromInt(8).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 1, ResourceCost: 0, TargetKind: CastTargetKind.Entity, StatusEffect: new OptionalStatusEffect(StatusEffectType.Slow, DurationTicks: 10, MagnitudeRaw: Fix32.FromInt(6).Raw / 10))),
            Invariants: InvariantOptions.Enabled);
    }
}
