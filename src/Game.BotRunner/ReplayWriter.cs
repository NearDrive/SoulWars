using System.Buffers.Binary;
using System.Text;

namespace Game.BotRunner;

public sealed class ReplayWriter : IDisposable
{
    private readonly Stream _output;
    private bool _disposed;

    public ReplayWriter(Stream output, ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("Replay output stream must be writable.", nameof(output));
        }

        _output = output;
        Header = header;
        Header.WriteTo(_output);
    }

    public ReplayHeader Header { get; }

    public void WriteTickInputs(int tick, ReadOnlySpan<ReplayMove> movesOrderedByBotIndex)
    {
        ThrowIfDisposed();

        if (movesOrderedByBotIndex.Length != Header.BotCount)
        {
            throw new ArgumentException($"Replay move count ({movesOrderedByBotIndex.Length}) must match BotCount ({Header.BotCount}).", nameof(movesOrderedByBotIndex));
        }

        Span<byte> recordHeader = stackalloc byte[1 + sizeof(int)];
        recordHeader[0] = (byte)ReplayRecordType.TickInputs;
        BinaryPrimitives.WriteInt32LittleEndian(recordHeader[1..5], tick);
        _output.Write(recordHeader);

        Span<byte> moveBytes = stackalloc byte[2];
        for (int i = 0; i < movesOrderedByBotIndex.Length; i++)
        {
            moveBytes[0] = unchecked((byte)movesOrderedByBotIndex[i].MoveX);
            moveBytes[1] = unchecked((byte)movesOrderedByBotIndex[i].MoveY);
            _output.Write(moveBytes);
        }
    }

    public void WriteFinalChecksum(string hex)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("Final checksum cannot be null or empty.", nameof(hex));
        }

        byte[] checksumBytes = Encoding.ASCII.GetBytes(hex.Trim());

        Span<byte> header = stackalloc byte[1 + sizeof(int)];
        header[0] = (byte)ReplayRecordType.FinalChecksum;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..5], checksumBytes.Length);
        _output.Write(header);
        _output.Write(checksumBytes);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
