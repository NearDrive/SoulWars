using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class SkillModelTests
{
    [Fact]
    public void SkillCooldown_TicksDownDeterministically()
    {
        SkillId skillId = new(77);
        SkillCooldownsComponent cooldowns = SkillCooldownsComponent.Empty.StartCooldown(skillId, cooldownTicks: 5);

        EntityState entity = new(
            Id: new EntityId(1),
            Pos: new Vec2Fix(Fix32.Zero, Fix32.Zero),
            Vel: new Vec2Fix(Fix32.Zero, Fix32.Zero),
            MaxHp: 100,
            Hp: 100,
            IsAlive: true,
            AttackRange: Fix32.FromInt(1),
            AttackDamage: 1,
            AttackCooldownTicks: 1,
            LastAttackTick: -1,
            SkillCooldowns: cooldowns);

        TileMap map = new(4, 4, ImmutableArray.CreateRange(new TileKind[16]));
        ZoneState zone = new(new ZoneId(1), map, ImmutableArray.Create(entity));
        WorldState state = new(
            Tick: 0,
            Zones: ImmutableArray.Create(zone),
            EntityLocations: ImmutableArray.Create(new EntityLocation(entity.Id, zone.Id)));

        SimulationConfig config = CreateConfig();

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        ZoneState steppedZone = state.Zones.Single();
        EntityState steppedEntity = steppedZone.Entities.Single();
        SkillCooldownsComponent steppedCooldowns = steppedEntity.SkillCooldowns;

        Assert.False(steppedCooldowns.IsReady(skillId));
        Assert.Equal(2, steppedCooldowns.CooldownRemainingBySkillId.Single().RemainingTicks);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));
        state = Simulation.Step(config, state, new Inputs(ImmutableArray<WorldCommand>.Empty));

        steppedZone = state.Zones.Single();
        steppedEntity = steppedZone.Entities.Single();
        steppedCooldowns = steppedEntity.SkillCooldowns;

        Assert.True(steppedCooldowns.IsReady(skillId));
        Assert.Empty(steppedCooldowns.CooldownRemainingBySkillId);
        Assert.DoesNotContain(steppedCooldowns.CooldownRemainingBySkillId, x => x.RemainingTicks < 0);
    }

    [Fact]
    public void SkillDefinition_IsPureData()
    {
        Type type = typeof(SkillDefinition);
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(properties, property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(Random));
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(DateTime));

        SkillDefinition skill = new(
            Id: new SkillId(10),
            RangeRaw: Fix32.FromInt(8).Raw,
            HitRadiusRaw: Fix32.OneRaw,
            MaxTargets: 8,
            CooldownTicks: 5,
            CastTimeTicks: 0,
            GlobalCooldownTicks: 0,
            ResourceCost: 0,
            TargetType: SkillTargetType.Entity,
            Flags: SkillFlags.Ranged);

        Assert.Equal(10, skill.Id.Value);
        Assert.Equal(SkillTargetType.Entity, skill.TargetType);
        Assert.Equal(SkillFlags.Ranged, skill.Flags);
        Assert.Equal(CastTargetKind.Entity, skill.TargetKind);
    }

    private static SimulationConfig CreateConfig() => new(
        Seed: 123,
        TickHz: 20,
        DtFix: new Fix32(3277),
        MoveSpeed: Fix32.FromInt(4),
        MaxSpeed: Fix32.FromInt(4),
        Radius: new Fix32(16384),
        ZoneCount: 1,
        MapWidth: 8,
        MapHeight: 8,
        NpcCountPerZone: 0,
        NpcWanderPeriodTicks: 30,
        NpcAggroRange: Fix32.FromInt(6),
        Invariants: InvariantOptions.Enabled);
}
