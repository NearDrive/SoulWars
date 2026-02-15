using System.Buffers.Binary;
using System.Text;

namespace Game.Audit;

public sealed class AuditLogWriter : IDisposable
{
    private static ReadOnlySpan<byte> Magic => "SWAUD01\0"u8;
    private readonly Stream _output;

    public AuditLogWriter(Stream output)
    {
        if (!output.CanWrite)
        {
            throw new ArgumentException("Audit output stream must be writable.", nameof(output));
        }

        _output = output;
        _output.Write(Magic);
        Span<byte> version = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(version, 1);
        _output.Write(version);
    }

    public void Append(in AuditEvent evt)
    {
        Span<byte> header = stackalloc byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(header[0..4], evt.Header.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], evt.Header.Seq);
        header[8] = (byte)evt.Header.Type;
        _output.Write(header);

        switch (evt.Header.Type)
        {
            case AuditEventType.PlayerConnected:
                WriteString(evt.AccountId);
                WriteInt(evt.PlayerId);
                break;
            case AuditEventType.EnterZone:
                WriteInt(evt.PlayerId);
                WriteInt(evt.ZoneId);
                WriteInt(evt.EntityId);
                break;
            case AuditEventType.Teleport:
                WriteInt(evt.PlayerId);
                WriteInt(evt.FromZoneId);
                WriteInt(evt.ToZoneId);
                WriteInt(evt.EntityId);
                break;
            case AuditEventType.Death:
                WriteInt(evt.EntityId);
                WriteInt(evt.KillerEntityId);
                break;
            case AuditEventType.DespawnDisconnected:
                WriteInt(evt.PlayerId);
                WriteInt(evt.EntityId);
                break;
            default:
                throw new InvalidDataException($"Unknown audit event type: {evt.Header.Type}.");
        }
    }

    public void Dispose() => _output.Dispose();

    private void WriteInt(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _output.Write(buffer);
    }

    private void WriteString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        WriteInt(utf8.Length);
        _output.Write(utf8);
    }
}

public sealed class AuditLogReader
{
    private static ReadOnlySpan<byte> Magic => "SWAUD01\0"u8;
    private readonly Stream _input;

    public AuditLogReader(Stream input)
    {
        if (!input.CanRead)
        {
            throw new ArgumentException("Audit input stream must be readable.", nameof(input));
        }

        _input = input;
        ValidateHeader();
    }

    public IEnumerable<AuditEvent> ReadAll()
    {
        while (TryReadHeader(out AuditEventHeader header))
        {
            yield return header.Type switch
            {
                AuditEventType.PlayerConnected => AuditEvent.PlayerConnected(header.Tick, header.Seq, ReadString(), ReadInt()),
                AuditEventType.EnterZone => AuditEvent.EnterZone(header.Tick, header.Seq, ReadInt(), ReadInt(), ReadInt()),
                AuditEventType.Teleport => AuditEvent.Teleport(header.Tick, header.Seq, ReadInt(), ReadInt(), ReadInt(), ReadInt()),
                AuditEventType.Death => AuditEvent.Death(header.Tick, header.Seq, ReadInt(), ReadNullableInt()),
                AuditEventType.DespawnDisconnected => AuditEvent.DespawnDisconnected(header.Tick, header.Seq, ReadInt(), ReadInt()),
                _ => throw new InvalidDataException($"Unknown audit event type: {header.Type}.")
            };
        }
    }

    private void ValidateHeader()
    {
        Span<byte> magicBuffer = stackalloc byte[Magic.Length];
        FillExactly(magicBuffer);
        if (!magicBuffer.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid audit magic header.");
        }

        if (ReadInt() != 1)
        {
            throw new InvalidDataException("Unsupported audit version.");
        }
    }

    private bool TryReadHeader(out AuditEventHeader header)
    {
        Span<byte> data = stackalloc byte[9];
        int read = _input.Read(data);
        if (read == 0)
        {
            header = default;
            return false;
        }

        if (read != data.Length)
        {
            throw new EndOfStreamException("Audit stream ended in the middle of a record header.");
        }

        header = new AuditEventHeader(
            BinaryPrimitives.ReadInt32LittleEndian(data[0..4]),
            BinaryPrimitives.ReadInt32LittleEndian(data[4..8]),
            (AuditEventType)data[8]);
        return true;
    }

    private int ReadNullableInt()
    {
        int value = ReadInt();
        return value < 0 ? -1 : value;
    }

    private int ReadInt()
    {
        Span<byte> buffer = stackalloc byte[4];
        FillExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private string ReadString()
    {
        int len = ReadInt();
        if (len < 0)
        {
            throw new InvalidDataException("Negative string length in audit stream.");
        }

        byte[] bytes = new byte[len];
        FillExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private void FillExactly(Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = _input.Read(destination[offset..]);
            if (read <= 0)
            {
                throw new EndOfStreamException($"Audit stream ended unexpectedly while reading {destination.Length} bytes.");
            }

            offset += read;
        }
    }
}
