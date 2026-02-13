using System.Buffers.Binary;
using System.Text;

namespace Game.Protocol;

public static class ProtocolCodec
{
    private const byte ClientHello = 1;
    private const byte ClientEnterZoneRequest = 2;
    private const byte ClientInputCommand = 3;
    private const byte ClientLeaveZoneRequest = 4;
    private const byte ClientAttackIntent = 5;

    private const byte ServerWelcome = 101;
    private const byte ServerEnterZoneAck = 102;
    private const byte ServerSnapshot = 103;
    private const byte ServerError = 104;

    public static byte[] Encode(IClientMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        return msg switch
        {
            Hello hello => EncodeHello(hello),
            EnterZoneRequest request => EncodeIntMessage(ClientEnterZoneRequest, request.ZoneId),
            InputCommand input => EncodeInputCommand(input),
            AttackIntent attack => EncodeAttackIntent(attack),
            LeaveZoneRequest request => EncodeIntMessage(ClientLeaveZoneRequest, request.ZoneId),
            _ => throw new InvalidOperationException($"Unsupported client message type: {msg.GetType().Name}")
        };
    }

    public static IClientMessage DecodeClient(byte[] data)
    {
        if (TryDecodeClient(data, out IClientMessage? msg, out ProtocolErrorCode error))
        {
            return msg!;
        }

        throw new ProtocolDecodeException(error, $"Client decode failed with {error}.");
    }

    public static bool TryDecodeClient(ReadOnlySpan<byte> data, out IClientMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (data.Length == 0)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        switch (data[0])
        {
            case ClientHello:
                return TryDecodeHello(data, out msg, out error);
            case ClientEnterZoneRequest:
                if (!TryReadInt32(data, 1, out int enterZoneId, out error))
                {
                    return false;
                }

                if (enterZoneId <= 0)
                {
                    error = ProtocolErrorCode.ValueOutOfRange;
                    return false;
                }

                msg = new EnterZoneRequest(enterZoneId);
                error = ProtocolErrorCode.None;
                return true;
            case ClientInputCommand:
                return TryDecodeInputCommand(data, out msg, out error);
            case ClientAttackIntent:
                return TryDecodeAttackIntent(data, out msg, out error);
            case ClientLeaveZoneRequest:
                if (!TryReadInt32(data, 1, out int leaveZoneId, out error))
                {
                    return false;
                }

                if (leaveZoneId <= 0)
                {
                    error = ProtocolErrorCode.ValueOutOfRange;
                    return false;
                }

                msg = new LeaveZoneRequest(leaveZoneId);
                error = ProtocolErrorCode.None;
                return true;
            default:
                error = ProtocolErrorCode.UnknownMessageType;
                return false;
        }
    }

    public static byte[] Encode(IServerMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        return msg switch
        {
            Welcome welcome => EncodeIntMessage(ServerWelcome, welcome.SessionId.Value),
            EnterZoneAck ack => EncodeEnterZoneAck(ack),
            Snapshot snapshot => EncodeSnapshot(snapshot),
            Error error => EncodeError(error),
            _ => throw new InvalidOperationException($"Unsupported server message type: {msg.GetType().Name}")
        };
    }

    public static IServerMessage DecodeServer(byte[] data)
    {
        if (TryDecodeServer(data, out IServerMessage? msg, out ProtocolErrorCode error))
        {
            return msg!;
        }

        throw new ProtocolDecodeException(error, $"Server decode failed with {error}.");
    }

    public static bool TryDecodeServer(ReadOnlySpan<byte> data, out IServerMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (data.Length == 0)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        switch (data[0])
        {
            case ServerWelcome:
                if (!TryReadInt32(data, 1, out int sessionId, out error))
                {
                    return false;
                }

                msg = new Welcome(new SessionId(sessionId));
                error = ProtocolErrorCode.None;
                return true;
            case ServerEnterZoneAck:
                if (!TryReadInt32(data, 1, out int zoneId, out error) || !TryReadInt32(data, 5, out int entityId, out error))
                {
                    return false;
                }

                if (zoneId <= 0)
                {
                    error = ProtocolErrorCode.ValueOutOfRange;
                    return false;
                }

                msg = new EnterZoneAck(zoneId, entityId);
                error = ProtocolErrorCode.None;
                return true;
            case ServerSnapshot:
                return TryDecodeSnapshot(data, out msg, out error);
            case ServerError:
                return TryDecodeError(data, out msg, out error);
            default:
                error = ProtocolErrorCode.UnknownMessageType;
                return false;
        }
    }

    private static byte[] EncodeHello(Hello hello)
    {
        byte[] text = Encoding.UTF8.GetBytes(hello.ClientVersion ?? string.Empty);
        byte[] data = new byte[1 + 4 + text.Length];
        data[0] = ClientHello;
        WriteInt32(data, 1, text.Length);
        text.CopyTo(data.AsSpan(5));
        return data;
    }

    private static bool TryDecodeHello(ReadOnlySpan<byte> data, out IClientMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (!TryReadInt32(data, 1, out int length, out error))
        {
            return false;
        }

        if (length < 0)
        {
            error = ProtocolErrorCode.InvalidLength;
            return false;
        }

        long required = 5L + length;
        if (required > data.Length)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        string version = Encoding.UTF8.GetString(data.Slice(5, length));
        msg = new Hello(version);
        error = ProtocolErrorCode.None;
        return true;
    }

    private static byte[] EncodeInputCommand(InputCommand input)
    {
        byte[] data = new byte[1 + 4 + 1 + 1];
        data[0] = ClientInputCommand;
        WriteInt32(data, 1, input.Tick);
        unchecked
        {
            data[5] = (byte)input.MoveX;
            data[6] = (byte)input.MoveY;
        }

        return data;
    }

    private static bool TryDecodeInputCommand(ReadOnlySpan<byte> data, out IClientMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (data.Length < 7)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        if (!TryReadInt32(data, 1, out int tick, out error))
        {
            return false;
        }

        if (tick < 0)
        {
            error = ProtocolErrorCode.ValueOutOfRange;
            return false;
        }

        sbyte moveX = unchecked((sbyte)data[5]);
        sbyte moveY = unchecked((sbyte)data[6]);
        if ((moveX is < -1 or > 1) || (moveY is < -1 or > 1))
        {
            error = ProtocolErrorCode.ValueOutOfRange;
            return false;
        }

        msg = new InputCommand(tick, moveX, moveY);
        error = ProtocolErrorCode.None;
        return true;
    }


    private static byte[] EncodeAttackIntent(AttackIntent attack)
    {
        byte[] data = new byte[1 + 4 + 4 + 4 + 4];
        data[0] = ClientAttackIntent;
        WriteInt32(data, 1, attack.Tick);
        WriteInt32(data, 5, attack.AttackerId);
        WriteInt32(data, 9, attack.TargetId);
        WriteInt32(data, 13, attack.ZoneId);
        return data;
    }

    private static bool TryDecodeAttackIntent(ReadOnlySpan<byte> data, out IClientMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;

        if (data.Length < 17)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        if (!TryReadInt32(data, 1, out int tick, out error) ||
            !TryReadInt32(data, 5, out int attackerId, out error) ||
            !TryReadInt32(data, 9, out int targetId, out error) ||
            !TryReadInt32(data, 13, out int zoneId, out error))
        {
            return false;
        }

        if (tick < 0 || attackerId <= 0 || targetId <= 0 || zoneId <= 0)
        {
            error = ProtocolErrorCode.ValueOutOfRange;
            return false;
        }

        msg = new AttackIntent(tick, attackerId, targetId, zoneId);
        error = ProtocolErrorCode.None;
        return true;
    }

    private static byte[] EncodeIntMessage(byte type, int value)
    {
        byte[] data = new byte[1 + 4];
        data[0] = type;
        WriteInt32(data, 1, value);
        return data;
    }

    private static byte[] EncodeEnterZoneAck(EnterZoneAck ack)
    {
        byte[] data = new byte[1 + 4 + 4];
        data[0] = ServerEnterZoneAck;
        WriteInt32(data, 1, ack.ZoneId);
        WriteInt32(data, 5, ack.EntityId);
        return data;
    }

    private static byte[] EncodeSnapshot(Snapshot snapshot)
    {
        SnapshotEntity[] entities = snapshot.Entities ?? Array.Empty<SnapshotEntity>();
        byte[] data = new byte[1 + 4 + 4 + 4 + (entities.Length * ((6 * 4) + 1))];
        data[0] = ServerSnapshot;
        WriteInt32(data, 1, snapshot.Tick);
        WriteInt32(data, 5, snapshot.ZoneId);
        WriteInt32(data, 9, entities.Length);

        int offset = 13;
        for (int i = 0; i < entities.Length; i++)
        {
            SnapshotEntity entity = entities[i];
            WriteInt32(data, offset, entity.EntityId);
            WriteInt32(data, offset + 4, entity.PosXRaw);
            WriteInt32(data, offset + 8, entity.PosYRaw);
            WriteInt32(data, offset + 12, entity.VelXRaw);
            WriteInt32(data, offset + 16, entity.VelYRaw);
            WriteInt32(data, offset + 20, entity.Hp);
            data[offset + 24] = (byte)entity.Kind;
            offset += 25;
        }

        return data;
    }

    private static bool TryDecodeSnapshot(ReadOnlySpan<byte> data, out IServerMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (!TryReadInt32(data, 1, out int tick, out error) ||
            !TryReadInt32(data, 5, out int zoneId, out error) ||
            !TryReadInt32(data, 9, out int entityCount, out error))
        {
            return false;
        }

        if (tick < 0 || zoneId <= 0 || entityCount < 0)
        {
            error = ProtocolErrorCode.ValueOutOfRange;
            return false;
        }

        int requiredLength;
        try
        {
            checked
            {
                requiredLength = 13 + (entityCount * 25);
            }
        }
        catch (OverflowException)
        {
            error = ProtocolErrorCode.InvalidLength;
            return false;
        }

        if (data.Length < requiredLength)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        SnapshotEntity[] entities = new SnapshotEntity[entityCount];
        int offset = 13;

        for (int i = 0; i < entityCount; i++)
        {
            byte rawKind = data[offset + 24];
            if (!TryDecodeSnapshotEntityKind(rawKind, out SnapshotEntityKind kind, out error))
            {
                return false;
            }

            entities[i] = new SnapshotEntity(
                EntityId: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
                PosXRaw: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4)),
                PosYRaw: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4)),
                VelXRaw: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 12, 4)),
                VelYRaw: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 16, 4)),
                Hp: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 20, 4)),
                Kind: kind);
            offset += 25;
        }

        msg = new Snapshot(tick, zoneId, entities);
        error = ProtocolErrorCode.None;
        return true;
    }

    private static bool TryDecodeSnapshotEntityKind(byte rawKind, out SnapshotEntityKind kind, out ProtocolErrorCode error)
    {
        switch ((SnapshotEntityKind)rawKind)
        {
            case SnapshotEntityKind.Unknown:
            case SnapshotEntityKind.Player:
            case SnapshotEntityKind.Npc:
                kind = (SnapshotEntityKind)rawKind;
                error = ProtocolErrorCode.None;
                return true;
            default:
                kind = SnapshotEntityKind.Unknown;
                error = ProtocolErrorCode.ValueOutOfRange;
                return false;
        }
    }

    private static byte[] EncodeError(Error error)
    {
        byte[] code = Encoding.UTF8.GetBytes(error.Code ?? string.Empty);
        byte[] message = Encoding.UTF8.GetBytes(error.Message ?? string.Empty);
        byte[] data = new byte[1 + 4 + code.Length + 4 + message.Length];

        data[0] = ServerError;
        WriteInt32(data, 1, code.Length);
        code.CopyTo(data.AsSpan(5));

        int messageLengthOffset = 5 + code.Length;
        WriteInt32(data, messageLengthOffset, message.Length);
        message.CopyTo(data.AsSpan(messageLengthOffset + 4));

        return data;
    }

    private static bool TryDecodeError(ReadOnlySpan<byte> data, out IServerMessage? msg, out ProtocolErrorCode error)
    {
        msg = null;
        if (!TryReadInt32(data, 1, out int codeLength, out error))
        {
            return false;
        }

        if (codeLength < 0)
        {
            error = ProtocolErrorCode.InvalidLength;
            return false;
        }

        long minimum = 5L + codeLength + 4;
        if (minimum > data.Length)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        string code = Encoding.UTF8.GetString(data.Slice(5, codeLength));

        int messageLengthOffset = 5 + codeLength;
        if (!TryReadInt32(data, messageLengthOffset, out int messageLength, out error))
        {
            return false;
        }

        if (messageLength < 0)
        {
            error = ProtocolErrorCode.InvalidLength;
            return false;
        }

        long required = (long)messageLengthOffset + 4 + messageLength;
        if (required > data.Length)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        string message = Encoding.UTF8.GetString(data.Slice(messageLengthOffset + 4, messageLength));

        msg = new Error(code, message);
        error = ProtocolErrorCode.None;
        return true;
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> data, int offset, out int value, out ProtocolErrorCode error)
    {
        value = default;
        if (offset < 0 || data.Length < offset + 4)
        {
            error = ProtocolErrorCode.Truncated;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        error = ProtocolErrorCode.None;
        return true;
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }
}
