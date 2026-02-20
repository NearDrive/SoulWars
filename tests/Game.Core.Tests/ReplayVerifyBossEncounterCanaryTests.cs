using System.Collections.Immutable;
using System.Linq;
using Game.Core;
using Game.Persistence;
using Xunit;

namespace Game.Core.Tests;

public sealed class BossEncounterCanaryReplayVerifyTests
{
    [Fact]
    [Trait("Category", "PR68")]
    [Trait("Category", "ReplayVerify")]
    [Trait("Category", "Canary")]
    public void ReplayVerify_BossEncounter_Canary_HasStableTickChecksums_AndCombatLogCount()
    {
        SimulationConfig config = BossEncounterCanaryScenario.CreateConfig();

        ScenarioRun baseline = BossEncounterCanaryScenario.Run(config, restartTick: null);
        ScenarioRun replay = BossEncounterCanaryScenario.Run(config, restartTick: null);

        Assert.Equal(baseline.TickChecksums.Length, replay.TickChecksums.Length);

        int? firstDivergentTick = null;
        for (int i = 0; i < baseline.TickChecksums.Length; i++)
        {
            if (!string.Equals(baseline.TickChecksums[i], replay.TickChecksums[i], StringComparison.Ordinal))
            {
                firstDivergentTick = i + 1;
                break;
            }
        }

        Assert.Null(firstDivergentTick);
        Assert.Equal(baseline.FinalChecksum, replay.FinalChecksum);
        Assert.Equal(baseline.CombatEventCount, replay.CombatEventCount);
        Assert.Contains(-1, baseline.BossAggroDirectionTrace);
        Assert.Contains(1, baseline.BossAggroDirectionTrace);
    }
}

public sealed class BossEncounterRestartDeterminismTests
{
    [Fact]
    [Trait("Category", "PR68")]
    public void BossEncounter_RestartAtTick150_MatchesNoRestart_FinalChecksum()
    {
        SimulationConfig config = BossEncounterCanaryScenario.CreateConfig();

        ScenarioRun baseline = BossEncounterCanaryScenario.Run(config, restartTick: null);
        ScenarioRun resumed = BossEncounterCanaryScenario.Run(config, restartTick: 150);

        Assert.Equal(baseline.FinalChecksum, resumed.FinalChecksum);
    }
}

internal readonly record struct ScenarioRun(
    string FinalChecksum,
    ImmutableArray<string> TickChecksums,
    int CombatEventCount,
    ImmutableArray<int> BossAggroDirectionTrace);

internal static class BossEncounterCanaryScenario
{
    private static readonly ZoneId ZoneId = new(1);
    private static readonly EntityId TankId = new(201);
    private static readonly EntityId DpsId = new(202);
    private static readonly EntityId SupportId = new(203);

    public static ScenarioRun Run(SimulationConfig config, int? restartTick)
    {
        WorldState state = Simulation.CreateInitialState(config, CreateZoneDefinitions());

        state = EnterPartyMembers(state, config);
        state = CreatePartyAndInstance(state, config);

        const int totalTicks = 300;
        ImmutableArray<string>.Builder tickChecksums = ImmutableArray.CreateBuilder<string>(totalTicks);
        ImmutableArray<int>.Builder aggroTrace = ImmutableArray.CreateBuilder<int>(totalTicks);
        int combatEventCount = 0;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            state = Simulation.Step(config, state, new Inputs(BuildBotCommands(state, tick)));
            tickChecksums.Add(StateChecksum.Compute(state));
            combatEventCount += state.CombatEvents.IsDefault ? 0 : state.CombatEvents.Length;
            aggroTrace.Add(GetBossAggroDirection(state));

            if (restartTick.HasValue && tick + 1 == restartTick.Value)
            {
                byte[] snapshot = WorldStateSerializer.SaveToBytes(state);
                state = WorldStateSerializer.LoadFromBytes(snapshot);
            }
        }

        return new ScenarioRun(
            FinalChecksum: StateChecksum.Compute(state),
            TickChecksums: tickChecksums.ToImmutable(),
            CombatEventCount: combatEventCount,
            BossAggroDirectionTrace: aggroTrace.ToImmutable());
    }

    public static SimulationConfig CreateConfig() => new(
        Seed: 6801,
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
            new SkillDefinition(new SkillId(1), Fix32.FromInt(32).Raw, 0, MaxTargets: 1, CooldownTicks: 12, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 2), // tank steady
            new SkillDefinition(new SkillId(2), Fix32.FromInt(32).Raw, 0, MaxTargets: 1, CooldownTicks: 1, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 8), // dps burst
            new SkillDefinition(new SkillId(3), Fix32.FromInt(32).Raw, 0, MaxTargets: 1, CooldownTicks: 30, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 1), // support soft dps
            new SkillDefinition(new SkillId(10), Fix32.FromInt(32).Raw, 0, MaxTargets: 8, CooldownTicks: 1, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Self, BaseAmount: 3), // encounter aoe
            new SkillDefinition(new SkillId(11), Fix32.FromInt(32).Raw, 0, MaxTargets: 1, CooldownTicks: 1, CastTimeTicks: 0, GlobalCooldownTicks: 0, ResourceCost: 0, CastTargetKind.Entity, BaseAmount: 4)),
        Invariants: InvariantOptions.Enabled);

    private static ZoneDefinitions CreateZoneDefinitions()
    {
        EncounterDefinition encounter = new(
            new EncounterId(6801),
            "boss-encounter-canary",
            Version: 1,
            ZoneId,
            ImmutableArray.Create(
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 10, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(10), Y: Fix32.FromInt(10), Count: 1))),
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnHpBelowPct, Target: EntityRef.Boss, Pct: 70, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SetPhase, PhaseIndex: 1))))),
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 60, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.CastSkill, Caster: EntityRef.Boss, SkillId: new SkillId(10), Target: TargetSpec.Self))),
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 120, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SpawnNpc, X: Fix32.FromInt(12), Y: Fix32.FromInt(12), Count: 3))),
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 180, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.CastSkill, Caster: EntityRef.Boss, SkillId: new SkillId(10), Target: TargetSpec.Self))),
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnHpBelowPct, Target: EntityRef.Boss, Pct: 30, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.SetPhase, PhaseIndex: 2))))),
                new EncounterPhaseDefinition(ImmutableArray.Create(
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 240, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.ApplyStatus, StatusSource: EntityRef.Boss, StatusTarget: EntityRef.Boss, StatusType: StatusEffectType.Slow, StatusDurationTicks: 60, StatusMagnitudeRaw: new Fix32(Fix32.OneRaw / 2).Raw))),
                    new EncounterTriggerDefinition(EncounterTriggerKind.OnTick, AtTickOffset: 270, Actions: ImmutableArray.Create(new EncounterActionDefinition(EncounterActionKind.CastSkill, Caster: EntityRef.Boss, SkillId: new SkillId(11), Target: TargetSpec.Entity(EntityRef.FromEntityId(TankId)))))))));

        ZoneDefinition zone = new(
            ZoneId,
            new ZoneBounds(Fix32.Zero, Fix32.Zero, Fix32.FromInt(32), Fix32.FromInt(32)),
            ImmutableArray<ZoneAabb>.Empty,
            ImmutableArray<NpcSpawnDefinition>.Empty,
            null,
            null,
            ImmutableArray.Create(encounter));

        return new ZoneDefinitions(ImmutableArray.Create(zone));
    }

    private static WorldState EnterPartyMembers(WorldState state, SimulationConfig config)
    {
        return Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.EnterZone, TankId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(8), Fix32.FromInt(10))),
            new WorldCommand(WorldCommandKind.EnterZone, DpsId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(12), Fix32.FromInt(10))),
            new WorldCommand(WorldCommandKind.EnterZone, SupportId, ZoneId, SpawnPos: new Vec2Fix(Fix32.FromInt(9), Fix32.FromInt(10))))));
    }

    private static WorldState CreatePartyAndInstance(WorldState state, SimulationConfig config)
    {
        PartyRegistry parties = state.PartyRegistryOrEmpty.CreateParty(TankId);
        state = state with { PartyRegistry = parties };

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.InviteToParty, TankId, ZoneId, InviteePlayerId: DpsId),
            new WorldCommand(WorldCommandKind.InviteToParty, TankId, ZoneId, InviteePlayerId: SupportId))));

        ImmutableArray<PartyInvite> invites = state.PartyInviteRegistryOrEmpty.Invites;
        PartyInvite dpsInvite = invites.Single(i => i.InviteeId == DpsId);
        PartyInvite supportInvite = invites.Single(i => i.InviteeId == SupportId);

        state = Simulation.Step(config, state, new Inputs(ImmutableArray.Create(
            new WorldCommand(WorldCommandKind.AcceptPartyInvite, DpsId, ZoneId, PartyId: dpsInvite.PartyId),
            new WorldCommand(WorldCommandKind.AcceptPartyInvite, SupportId, ZoneId, PartyId: supportInvite.PartyId))));

        PartyState party = Assert.Single(state.PartyRegistryOrEmpty.Parties);
        (InstanceRegistry instanceRegistry, ZoneInstanceState instance) = state.InstanceRegistryOrEmpty.CreateInstance(config.Seed, party.Id, ZoneId, creationTick: state.Tick);
        Assert.Equal(party.Id, instance.PartyId);
        return state with { InstanceRegistry = instanceRegistry };
    }

    private static ImmutableArray<WorldCommand> BuildBotCommands(WorldState state, int tick)
    {
        if (tick >= 256 || !TryGetBossId(state, out EntityId bossId))
        {
            return ImmutableArray<WorldCommand>.Empty;
        }

        ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();

        commands.Add(new WorldCommand(WorldCommandKind.CastSkill, TankId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(1), TargetKind: CastTargetKind.Entity));

        if ((tick + 30) % 60 == 0)
        {
            commands.Add(new WorldCommand(WorldCommandKind.CastSkill, DpsId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(2), TargetKind: CastTargetKind.Entity));
        }

        if (tick % 30 == 0)
        {
            commands.Add(new WorldCommand(WorldCommandKind.CastSkill, SupportId, ZoneId, TargetEntityId: bossId, SkillId: new SkillId(3), TargetKind: CastTargetKind.Entity));
        }

        return commands.ToImmutable();
    }

    private static bool TryGetBossId(WorldState state, out EntityId bossId)
    {
        bossId = default;
        if (!state.TryGetZone(ZoneId, out ZoneState zone))
        {
            return false;
        }

        ImmutableArray<EncounterRuntimeState> runtimes = state.EncounterRegistryOrEmpty.RuntimeStates;
        if (runtimes.IsDefaultOrEmpty)
        {
            return false;
        }

        EncounterRuntimeState runtime = runtimes[0];
        if (runtime.BossEntityId.Value <= 0)
        {
            return false;
        }

        bossId = runtime.BossEntityId;
        return true;
    }

    private static int GetBossAggroDirection(WorldState state)
    {
        if (!state.TryGetZone(ZoneId, out ZoneState zone))
        {
            return 0;
        }

        if (!TryGetBossId(state, out EntityId bossId))
        {
            return 0;
        }

        int index = ZoneEntities.FindIndex(zone.EntitiesData.AliveIds, bossId);
        return index < 0 ? 0 : zone.Entities[index].WanderX;
    }
}
