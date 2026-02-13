using Game.Protocol;

namespace Game.BotRunner;

public sealed class BotAgent
{
    private const int DefaultAttackRangeRaw = 1 << 16;
    private const int RetreatHpPercent = 30;

    public BotAgent(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
    }

    public BotConfig Config { get; }

    public BotDecision Decide(BotClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        Snapshot? snapshot = client.LastSnapshot;
        int? selfEntityId = client.EntityId;
        if (snapshot is null || selfEntityId is null)
        {
            return BotDecision.Idle;
        }

        SnapshotEntity? self = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == selfEntityId.Value);
        if (self is null || self.Hp <= 0)
        {
            return BotDecision.Idle;
        }

        SnapshotEntity? target = snapshot.Entities
            .Where(entity => entity.Kind == SnapshotEntityKind.Npc && entity.Hp > 0)
            .OrderBy(entity => DistSq(self, entity))
            .ThenBy(entity => entity.EntityId)
            .FirstOrDefault();

        if (target is null)
        {
            return new BotDecision(snapshot.Tick, 0, 0, null);
        }

        int decisionTick = snapshot.Tick;
        bool shouldRetreat = IsLowHp(self.Hp);
        int dx = target.PosXRaw - self.PosXRaw;
        int dy = target.PosYRaw - self.PosYRaw;

        sbyte moveX = ToMoveAxis(shouldRetreat ? -dx : dx);
        sbyte moveY = ToMoveAxis(shouldRetreat ? -dy : dy);

        int? attackTargetId = null;
        if (!shouldRetreat && DistSq(self, target) <= (long)DefaultAttackRangeRaw * DefaultAttackRangeRaw)
        {
            attackTargetId = target.EntityId;
        }

        return new BotDecision(decisionTick, moveX, moveY, attackTargetId);
    }

    private static bool IsLowHp(int hp)
    {
        return hp > 0 && hp <= ((100 * RetreatHpPercent) / 100);
    }

    private static long DistSq(SnapshotEntity a, SnapshotEntity b)
    {
        long dx = (long)b.PosXRaw - a.PosXRaw;
        long dy = (long)b.PosYRaw - a.PosYRaw;
        return (dx * dx) + (dy * dy);
    }

    private static sbyte ToMoveAxis(int delta) => delta switch
    {
        > 0 => 1,
        < 0 => -1,
        _ => 0
    };
}

public readonly record struct BotDecision(int Tick, sbyte MoveX, sbyte MoveY, int? AttackTargetId)
{
    public static BotDecision Idle => new(1, 0, 0, null);
}
