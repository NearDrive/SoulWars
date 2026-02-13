namespace Game.Core;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException(string message)
        : base(message)
    {
    }
}
