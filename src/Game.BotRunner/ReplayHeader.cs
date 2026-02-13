using System.Buffers.Binary;

namespace Game.BotRunner;

public readonly record struct ReplayHeader(
    int Version,
    int ServerSeed,
    int TickCount,
    int SnapshotEveryTicks,
    int BotCount,
    int ZoneId,
    int BaseBotSeed,
    int Reserved)
{
    public const int CurrentVersion = 2;

    public const string MagicText = "SWRPL01\0";
    public static ReadOnlySpan<byte> MagicBytes => "SWRPL01\0"u8;

    public const int ByteSize = 8 + (8 * sizeof(int));

    public static ReplayHeader FromScenarioConfig(ScenarioConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        return new ReplayHeader(
            Version: CurrentVersion,
            ServerSeed: cfg.ServerSeed,
            TickCount: cfg.TickCount,
            SnapshotEveryTicks: cfg.SnapshotEveryTicks,
            BotCount: cfg.BotCount,
            ZoneId: cfg.ZoneId,
            BaseBotSeed: cfg.BaseBotSeed,
            Reserved: 0);
    }

    public ScenarioConfig ToScenarioConfig()
    {
        return new ScenarioConfig(
            ServerSeed,
            TickCount,
            SnapshotEveryTicks,
            BotCount,
            ZoneId,
            BaseBotSeed);
    }

    public void WriteTo(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Span<byte> buffer = stackalloc byte[ByteSize];
        MagicBytes.CopyTo(buffer[..8]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..12], Version);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..16], ServerSeed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[16..20], TickCount);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[20..24], SnapshotEveryTicks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[24..28], BotCount);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[28..32], ZoneId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[32..36], BaseBotSeed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[36..40], Reserved);
        output.Write(buffer);
    }

    public static ReplayHeader ReadFrom(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Span<byte> buffer = stackalloc byte[ByteSize];
        FillExactly(input, buffer);

        if (!buffer[..8].SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException($"Invalid replay magic. Expected '{MagicText}'.");
        }

        ReplayHeader header = new(
            Version: BinaryPrimitives.ReadInt32LittleEndian(buffer[8..12]),
            ServerSeed: BinaryPrimitives.ReadInt32LittleEndian(buffer[12..16]),
            TickCount: BinaryPrimitives.ReadInt32LittleEndian(buffer[16..20]),
            SnapshotEveryTicks: BinaryPrimitives.ReadInt32LittleEndian(buffer[20..24]),
            BotCount: BinaryPrimitives.ReadInt32LittleEndian(buffer[24..28]),
            ZoneId: BinaryPrimitives.ReadInt32LittleEndian(buffer[28..32]),
            BaseBotSeed: BinaryPrimitives.ReadInt32LittleEndian(buffer[32..36]),
            Reserved: BinaryPrimitives.ReadInt32LittleEndian(buffer[36..40]));

        if (header.Version is not 1 and not CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported replay version '{header.Version}'.");
        }

        return header;
    }

    private static void FillExactly(Stream input, Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = input.Read(destination[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException($"Replay stream ended unexpectedly while reading {destination.Length} bytes.");
            }

            offset += read;
        }
    }
}
