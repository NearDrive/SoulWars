using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace Game.BotRunner;

public sealed class ReplayReader : IDisposable
{
    private readonly Stream _input;
    private bool _disposed;

    public ReplayReader(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            throw new ArgumentException("Replay input stream must be readable.", nameof(input));
        }

        _input = input;
        Header = ReplayHeader.ReadFrom(_input);
    }

    public ReplayHeader Header { get; }

    public bool TryReadNext(out ReplayEvent evt)
    {
        ThrowIfDisposed();

        int typeByte = _input.ReadByte();
        if (typeByte < 0)
        {
            evt = default;
            return false;
        }

        ReplayRecordType recordType = (ReplayRecordType)typeByte;
        switch (recordType)
        {
            case ReplayRecordType.TickInputs:
                evt = ReadTickInputs();
                return true;
            case ReplayRecordType.FinalChecksum:
                evt = ReadFinalChecksum();
                return true;
            default:
                throw new InvalidDataException($"Unknown replay record type: {typeByte}.");
        }
    }

    private ReplayEvent ReadTickInputs()
    {
        Span<byte> tickBuffer = stackalloc byte[sizeof(int)];
        FillExactly(tickBuffer);
        int tick = BinaryPrimitives.ReadInt32LittleEndian(tickBuffer);

        ReplayMove[] moves = new ReplayMove[Header.BotCount];
        if (Header.Version >= ReplayHeader.CurrentVersion)
        {
            Span<byte> moveBuffer = stackalloc byte[2 + 1 + sizeof(int)];
            for (int i = 0; i < moves.Length; i++)
            {
                FillExactly(moveBuffer);
                int? attackTargetId = moveBuffer[2] == 1 ? BinaryPrimitives.ReadInt32LittleEndian(moveBuffer[3..]) : null;
                moves[i] = new ReplayMove(
                    MoveX: unchecked((sbyte)moveBuffer[0]),
                    MoveY: unchecked((sbyte)moveBuffer[1]),
                    AttackTargetId: attackTargetId);
            }
        }
        else
        {
            Span<byte> moveBuffer = stackalloc byte[2];
            for (int i = 0; i < moves.Length; i++)
            {
                FillExactly(moveBuffer);
                moves[i] = new ReplayMove(
                    MoveX: unchecked((sbyte)moveBuffer[0]),
                    MoveY: unchecked((sbyte)moveBuffer[1]),
                    AttackTargetId: null);
            }
        }

        return new ReplayEvent(
            RecordType: ReplayRecordType.TickInputs,
            Tick: tick,
            Moves: moves.ToImmutableArray(),
            FinalChecksumHex: null);
    }

    private ReplayEvent ReadFinalChecksum()
    {
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        FillExactly(lengthBuffer);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length < 0 || length > 1024)
        {
            throw new InvalidDataException($"Invalid final checksum length: {length}.");
        }

        byte[] checksumBytes = new byte[length];
        FillExactly(checksumBytes);
        string checksum = Encoding.ASCII.GetString(checksumBytes);

        return new ReplayEvent(
            RecordType: ReplayRecordType.FinalChecksum,
            Tick: 0,
            Moves: ImmutableArray<ReplayMove>.Empty,
            FinalChecksumHex: checksum);
    }

    private void FillExactly(Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = _input.Read(destination[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException($"Replay stream ended unexpectedly while reading {destination.Length} bytes.");
            }

            offset += read;
        }
    }

    private void FillExactly(byte[] destination) => FillExactly(destination.AsSpan());

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
