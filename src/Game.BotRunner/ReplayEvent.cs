using System.Collections.Immutable;

namespace Game.BotRunner;

public enum ReplayRecordType : byte
{
    TickInputs = 1,
    FinalChecksum = 2
}

public readonly record struct ReplayMove(sbyte MoveX, sbyte MoveY, int? AttackTargetId);

public readonly record struct ReplayEvent(
    ReplayRecordType RecordType,
    int Tick,
    ImmutableArray<ReplayMove> Moves,
    string? FinalChecksumHex);
