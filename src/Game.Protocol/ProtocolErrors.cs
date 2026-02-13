namespace Game.Protocol;

public enum ProtocolErrorCode
{
    None = 0,
    UnknownMessageType,
    Truncated,
    InvalidLength,
    ValueOutOfRange
}

public sealed class ProtocolDecodeException : Exception
{
    public ProtocolDecodeException(ProtocolErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ProtocolErrorCode Code { get; }
}
