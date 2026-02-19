using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Game.Core;

namespace Game.Server.Scenarios;

public static class BotCombatScenario
{
    private static readonly SkillId PrimarySkillId = new(101);

    private sealed record BotSpec(EntityId EntityId, ZoneId ZoneId, int Team, Vec2Fix SpawnPos);

    public sealed record RunResult(
        string FinalGlobalChecksum,
        ImmutableArray<ZoneChecksum> FinalPerZoneChecksums,
        string CombatEventsHash,
        bool NoCrossZoneDuplicates,
        bool ExpectedEntityCountMaintained,
        int DurationTicks);

    public static RunResult RunDeterministic()
    {
        const int seed = 57_057;
        const int tickHz = 20;
        const int durationTicks = tickHz * 20;
        const int expectedEntityCount = 4;
        const int cooldownTicks = 4;

        SimulationConfig config = SimulationConfig.Default(seed) with
        {
            TickHz = tickHz,
            ZoneCount = 2,
            MapWidth = 24,
            MapHeight = 24,
            NpcCountPerZone = 0,
            SkillDefinitions = ImmutableArray.Create(
                new SkillDefinition(
                    PrimarySkillId,
                    RangeRaw: Fix32.FromInt(3).Raw,
                    HitRadiusRaw: Fix32.OneRaw,
                    MaxTargets: 1,
                    CooldownTicks: cooldownTicks,
                    CastTimeTicks: 0,
                    GlobalCooldownTicks: 0,
                    ResourceCost: 0,
                    TargetType: SkillTargetType.Entity,
                    EffectKind: SkillEffectKind.Damage,
                    BaseAmount: 14,
                    CoefRaw: Fix32.OneRaw))
        };

        ImmutableArray<BotSpec> bots = ImmutableArray.Create(
            new BotSpec(new EntityId(1001), new ZoneId(1), 1, new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(4))),
            new BotSpec(new EntityId(1002), new ZoneId(1), 2, new Vec2Fix(Fix32.FromInt(10), Fix32.FromInt(4))),
            new BotSpec(new EntityId(1003), new ZoneId(2), 1, new Vec2Fix(Fix32.FromInt(4), Fix32.FromInt(4))),
            new BotSpec(new EntityId(1004), new ZoneId(2), 2, new Vec2Fix(Fix32.FromInt(10), Fix32.FromInt(4))));

        WorldState state = Simulation.CreateInitialState(config);
        state = SpawnBots(config, state, bots);

        Dictionary<int, int> nextCastTickByEntityId = bots.ToDictionary(b => b.EntityId.Value, _ => 0);
        StringBuilder combatEventStream = new();

        bool noCrossZoneDuplicates = true;
        bool expectedEntityCountMaintained = true;

        for (int tick = 1; tick <= durationTicks; tick++)
        {
            ImmutableArray<WorldCommand>.Builder commands = ImmutableArray.CreateBuilder<WorldCommand>();
            foreach (BotSpec bot in bots.OrderBy(b => b.EntityId.Value))
            {
                if (!state.TryGetEntityZone(bot.EntityId, out ZoneId zoneId) || !state.TryGetZone(zoneId, out ZoneState zone))
                {
                    continue;
                }

                EntityState? botEntity = zone.Entities.FirstOrDefault(entity => entity.Id.Value == bot.EntityId.Value);
                if (botEntity is null || !botEntity.IsAlive)
                {
                    continue;
                }

                EntityState? target = zone.Entities
                    .Where(candidate => candidate.IsAlive
                        && candidate.Id.Value != bot.EntityId.Value
                        && bots.Any(spec => spec.EntityId.Value == candidate.Id.Value && spec.Team != bot.Team))
                    .OrderBy(candidate => candidate.Id.Value)
                    .FirstOrDefault();

                if (target is null)
                {
                    continue;
                }

                bool inRange = IsWithinRange(botEntity.Value.Pos, target.Pos, Fix32.FromInt(3));
                if (inRange && tick >= nextCastTickByEntityId[bot.EntityId.Value])
                {
                    commands.Add(new WorldCommand(
                        Kind: WorldCommandKind.CastSkill,
                        EntityId: bot.EntityId,
                        ZoneId: zoneId,
                        SkillId: PrimarySkillId,
                        TargetKind: CastTargetKind.Entity,
                        TargetEntityId: target.Id));
                    nextCastTickByEntityId[bot.EntityId.Value] = tick + cooldownTicks;
                }
                else
                {
                    sbyte moveX = Direction(botEntity.Value.Pos.X.Raw, target.Pos.X.Raw);
                    sbyte moveY = Direction(botEntity.Value.Pos.Y.Raw, target.Pos.Y.Raw);
                    commands.Add(new WorldCommand(
                        Kind: WorldCommandKind.MoveIntent,
                        EntityId: bot.EntityId,
                        ZoneId: zoneId,
                        MoveX: moveX,
                        MoveY: moveY));
                }
            }

            state = Simulation.Step(config, state, new Inputs(commands.ToImmutable()));

            AppendCombatEvents(combatEventStream, state.CombatEvents);
            noCrossZoneDuplicates &= VerifyNoCrossZoneDuplicates(state);
            expectedEntityCountMaintained &= state.Zones.Sum(z => z.Entities.Length) == expectedEntityCount;
        }

        return new RunResult(
            FinalGlobalChecksum: StateChecksum.ComputeGlobalChecksum(state),
            FinalPerZoneChecksums: StateChecksum.ComputeZoneChecksums(state),
            CombatEventsHash: ComputeSha256(combatEventStream.ToString()),
            NoCrossZoneDuplicates: noCrossZoneDuplicates,
            ExpectedEntityCountMaintained: expectedEntityCountMaintained,
            DurationTicks: durationTicks);
    }

    private static WorldState SpawnBots(SimulationConfig config, WorldState state, ImmutableArray<BotSpec> bots)
    {
        ImmutableArray<WorldCommand> enters = bots
            .OrderBy(bot => bot.EntityId.Value)
            .Select(bot => new WorldCommand(
                Kind: WorldCommandKind.EnterZone,
                EntityId: bot.EntityId,
                ZoneId: bot.ZoneId,
                SpawnPos: bot.SpawnPos))
            .ToImmutableArray();

        WorldState spawned = Simulation.Step(config, state, new Inputs(enters));
        foreach (ZoneState zone in spawned.Zones)
        {
            ZoneState updatedZone = zone.WithEntities(zone.Entities
                .Select(entity => entity with { MaxHp = 120, Hp = 120 })
                .OrderBy(entity => entity.Id.Value)
                .ToImmutableArray());
            spawned = spawned.WithZoneUpdated(updatedZone);
        }

        return spawned;
    }

    private static bool VerifyNoCrossZoneDuplicates(WorldState state)
    {
        Dictionary<int, int> seen = new();
        foreach (ZoneState zone in state.Zones)
        {
            foreach (EntityState entity in zone.Entities)
            {
                seen.TryGetValue(entity.Id.Value, out int count);
                seen[entity.Id.Value] = count + 1;
            }
        }

        return seen.Values.All(count => count == 1);
    }

    private static void AppendCombatEvents(StringBuilder builder, ImmutableArray<CombatEvent> events)
    {
        ImmutableArray<CombatEvent> ordered = (events.IsDefault ? ImmutableArray<CombatEvent>.Empty : events)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ThenBy(e => (byte)e.Type)
            .ThenBy(e => e.Amount)
            .ToImmutableArray();

        foreach (CombatEvent combatEvent in ordered)
        {
            builder.Append(combatEvent.Tick)
                .Append('|')
                .Append(combatEvent.SourceId.Value)
                .Append('|')
                .Append(combatEvent.TargetId.Value)
                .Append('|')
                .Append(combatEvent.SkillId.Value)
                .Append('|')
                .Append((byte)combatEvent.Type)
                .Append('|')
                .Append(combatEvent.Amount)
                .Append(';');
        }
    }

    private static bool IsWithinRange(Vec2Fix from, Vec2Fix to, Fix32 range)
    {
        long dx = (long)to.X.Raw - from.X.Raw;
        long dy = (long)to.Y.Raw - from.Y.Raw;
        long distanceSq = dx * dx + dy * dy;
        long rangeSq = (long)range.Raw * range.Raw;
        return distanceSq <= rangeSq;
    }

    private static sbyte Direction(int currentRaw, int targetRaw) =>
        targetRaw > currentRaw ? (sbyte)1 : targetRaw < currentRaw ? (sbyte)(-1) : (sbyte)0;

    private static string ComputeSha256(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
