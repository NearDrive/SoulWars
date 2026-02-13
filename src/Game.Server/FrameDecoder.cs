using System.Buffers.Binary;
using System.Collections.Generic;

namespace Game.Server;

public sealed class FrameDecoder
{
    private readonly List<byte> _buffer = new();
    private readonly Queue<byte[]> _frames = new();

    public FrameDecoder(int maxFrameBytes)
    {
        if (maxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
        }

        MaxFrameBytes = maxFrameBytes;
    }

    public int MaxFrameBytes { get; }

    public bool IsClosed { get; private set; }

    public void Push(ReadOnlySpan<byte> bytes)
    {
        if (IsClosed || bytes.Length == 0)
        {
            return;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            _buffer.Add(bytes[i]);
        }

        while (TryExtractOne())
        {
        }
    }

    public bool TryDequeueFrame(out byte[] frame)
    {
        if (_frames.Count == 0)
        {
            frame = Array.Empty<byte>();
            return false;
        }

        frame = _frames.Dequeue();
        return true;
    }

    public void Close()
    {
        IsClosed = true;
        _buffer.Clear();
        _frames.Clear();
    }

    private bool TryExtractOne()
    {
        if (_buffer.Count < 4)
        {
            return false;
        }

        Span<byte> lenBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            lenBytes[i] = _buffer[i];
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
        if (length <= 0 || length > MaxFrameBytes)
        {
            Close();
            return false;
        }

        int total = 4 + length;
        if (_buffer.Count < total)
        {
            return false;
        }

        byte[] frame = new byte[length];
        for (int i = 0; i < length; i++)
        {
            frame[i] = _buffer[4 + i];
        }

        _buffer.RemoveRange(0, total);
        _frames.Enqueue(frame);
        return true;
    }
}
