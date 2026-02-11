using System.Buffers.Binary;
using System.Text;

namespace Game.Protocol;

public static class ProtocolCodec
{
    private const byte ClientHello = 1;
    private const byte ClientEnterZoneRequest = 2;
    private const byte ClientInputCommand = 3;
    private const byte ClientLeaveZoneRequest = 4;

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
            LeaveZoneRequest request => EncodeIntMessage(ClientLeaveZoneRequest, request.ZoneId),
            _ => throw new InvalidOperationException($"Unsupported client message type: {msg.GetType().Name}")
        };
    }

    public static IClientMessage DecodeClient(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new InvalidOperationException("Client payload is empty.");
        }

        return data[0] switch
        {
            ClientHello => DecodeHello(data),
            ClientEnterZoneRequest => new EnterZoneRequest(ReadInt32(data, 1)),
            ClientInputCommand => DecodeInputCommand(data),
            ClientLeaveZoneRequest => new LeaveZoneRequest(ReadInt32(data, 1)),
            _ => throw new InvalidOperationException($"Unknown client message type: {data[0]}")
        };
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
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new InvalidOperationException("Server payload is empty.");
        }

        return data[0] switch
        {
            ServerWelcome => new Welcome(new SessionId(ReadInt32(data, 1))),
            ServerEnterZoneAck => new EnterZoneAck(ReadInt32(data, 1), ReadInt32(data, 5)),
            ServerSnapshot => DecodeSnapshot(data),
            ServerError => DecodeError(data),
            _ => throw new InvalidOperationException($"Unknown server message type: {data[0]}")
        };
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

    private static IClientMessage DecodeHello(byte[] data)
    {
        int length = ReadInt32(data, 1);
        EnsureLength(data, 5 + length);
        string version = Encoding.UTF8.GetString(data, 5, length);
        return new Hello(version);
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

    private static InputCommand DecodeInputCommand(byte[] data)
    {
        EnsureLength(data, 7);
        int tick = ReadInt32(data, 1);
        sbyte moveX = unchecked((sbyte)data[5]);
        sbyte moveY = unchecked((sbyte)data[6]);
        return new InputCommand(tick, moveX, moveY);
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
        byte[] data = new byte[1 + 4 + 4 + 4 + (entities.Length * (5 * 4))];
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
            offset += 20;
        }

        return data;
    }

    private static Snapshot DecodeSnapshot(byte[] data)
    {
        int tick = ReadInt32(data, 1);
        int zoneId = ReadInt32(data, 5);
        int entityCount = ReadInt32(data, 9);

        if (entityCount < 0)
        {
            throw new InvalidOperationException("Invalid negative entity count.");
        }

        int requiredLength = 13 + (entityCount * 20);
        EnsureLength(data, requiredLength);

        SnapshotEntity[] entities = new SnapshotEntity[entityCount];
        int offset = 13;

        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = new SnapshotEntity(
                EntityId: ReadInt32(data, offset),
                PosXRaw: ReadInt32(data, offset + 4),
                PosYRaw: ReadInt32(data, offset + 8),
                VelXRaw: ReadInt32(data, offset + 12),
                VelYRaw: ReadInt32(data, offset + 16));
            offset += 20;
        }

        return new Snapshot(tick, zoneId, entities);
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

    private static Error DecodeError(byte[] data)
    {
        int codeLength = ReadInt32(data, 1);
        EnsureLength(data, 5 + codeLength + 4);
        string code = Encoding.UTF8.GetString(data, 5, codeLength);

        int messageLengthOffset = 5 + codeLength;
        int messageLength = ReadInt32(data, messageLengthOffset);
        EnsureLength(data, messageLengthOffset + 4 + messageLength);
        string message = Encoding.UTF8.GetString(data, messageLengthOffset + 4, messageLength);

        return new Error(code, message);
    }

    private static void EnsureLength(byte[] data, int requiredLength)
    {
        if (data.Length < requiredLength)
        {
            throw new InvalidOperationException($"Payload too short. Expected at least {requiredLength}, got {data.Length}.");
        }
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        EnsureLength(data, offset + 4);
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
    }
}
