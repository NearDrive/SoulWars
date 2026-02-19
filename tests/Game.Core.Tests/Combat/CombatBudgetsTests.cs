using System.Collections.Immutable;
using Game.Core;
using Xunit;

namespace Game.Core.Tests.Combat;

public sealed class CombatBudgetsTests
{
    [Fact]
    public void CombatEvents_OverTickBudget_TrimsDeterministically()
    {
        ImmutableArray<CombatEvent> unordered = ImmutableArray.Create(
            new CombatEvent(10, new EntityId(5), new EntityId(9), new SkillId(2), CombatEventType.Damage, 4),
            new CombatEvent(10, new EntityId(1), new EntityId(9), new SkillId(2), CombatEventType.Damage, 4),
            new CombatEvent(10, new EntityId(3), new EntityId(9), new SkillId(2), CombatEventType.Damage, 4));

        ImmutableArray<CombatEvent> ordered = CombatEventBudgets.OrderCanonically(unordered);
        ImmutableArray<CombatEvent> kept = CombatEventBudgets.TakeSnapshotEvents(unordered, 2);

        Assert.Equal(2, kept.Length);
        Assert.Equal(ordered[0], kept[0]);
        Assert.Equal(ordered[1], kept[1]);
        Assert.Equal(1, unordered.Length - kept.Length);
    }

    [Fact]
    public void CombatEvents_RingBuffer_CapsAtMaxRetained()
    {
        ImmutableArray<CombatEvent> retained = ImmutableArray<CombatEvent>.Empty;
        for (int tick = 1; tick <= 8; tick++)
        {
            ImmutableArray<CombatEvent> tickEvents = ImmutableArray.Create(
                new CombatEvent(tick, new EntityId(tick), new EntityId(99), new SkillId(10), CombatEventType.Damage, 1),
                new CombatEvent(tick, new EntityId(tick + 100), new EntityId(99), new SkillId(10), CombatEventType.Damage, 2));
            retained = CombatEventBuffer.AppendTickEvents(retained, tickEvents, 5);
        }

        Assert.Equal(5, retained.Length);
        Assert.True(CombatEventBudgets.IsCanonicalOrder(retained));
        Assert.All(retained, evt => Assert.True(evt.Tick >= 6));
    }

    [Fact]
    public void Snapshot_CombatEvents_Capped()
    {
        ImmutableArray<CombatEvent> events = ImmutableArray.CreateRange(
            Enumerable.Range(0, 10).Select(i => new CombatEvent(7, new EntityId(10 - i), new EntityId(20), new SkillId(2), CombatEventType.Damage, i)));

        ImmutableArray<CombatEvent> capped = CombatEventBudgets.TakeSnapshotEvents(events, 4);
        ImmutableArray<CombatEvent> expected = CombatEventBudgets.OrderCanonically(events).Take(4).ToImmutableArray();

        Assert.Equal(expected, capped);
    }

    [Fact]
    public void CombatEvents_DroppedCounters_TrackDeterministically()
    {
        SimulationConfig config = SimulationConfig.Default(77) with
        {
            ZoneCount = 1,
            NpcCountPerZone = 0,
            MaxCombatEventsPerTickPerZone = 1,
            MaxCombatEventsRetainedPerZone = 8,
            SkillDefinitions = ImmutableArray.Create(
                new SkillDefinition(new SkillId(10), Fix32.FromInt(6).Raw, HitRadiusRaw: Fix32.OneRaw, CooldownTicks: 0, ResourceCost: 0, TargetKind: CastTargetKind.Entity, EffectKind: SkillEffectKind.Damage, BaseAmount: 7, CoefRaw: Fix32.OneRaw))
        };

        WorldState state = Simulation.CreateInitialState(config);
        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(1), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(2), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(2), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(3), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(3), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(2))),
            new WorldCommand(WorldCommandKind.EnterZone, new EntityId(4), new ZoneId(1), SpawnPos: new Vec2Fix(Fix32.FromInt(5), Fix32.FromInt(2))))));

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(1), new ZoneId(1), SkillId: new SkillId(10), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(2)),
            new WorldCommand(WorldCommandKind.CastSkill, new EntityId(3), new ZoneId(1), SkillId: new SkillId(10), TargetKind: CastTargetKind.Entity, TargetEntityId: new EntityId(4)))));

        Assert.Equal((uint)1, state.CombatEventsDropped_LastTick);
        Assert.Equal((uint)1, state.CombatEventsDropped_Total);
        Assert.Equal((uint)1, state.CombatEventsEmitted_LastTick);
        Assert.Single(state.CombatEvents);
    }
}
