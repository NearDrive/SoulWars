namespace Game.Core;

public sealed record InvariantOptions(bool EnableCoreInvariants, bool EnableServerInvariants)
{
    public static InvariantOptions Enabled { get; } = new(true, true);
}
