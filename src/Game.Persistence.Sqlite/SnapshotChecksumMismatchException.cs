using System.IO;

namespace Game.Persistence.Sqlite;

public sealed class SnapshotChecksumMismatchException : IOException
{
    public SnapshotChecksumMismatchException(string expected, string actual, string snapshotPathOrId, SnapshotMeta snapshotMeta)
        : base($"Snapshot checksum mismatch for '{snapshotPathOrId}'. expected={expected} actual={actual} serializerVersion={snapshotMeta.SerializerVersion} zoneDefinitionsHash={snapshotMeta.ZoneDefinitionsHash} configHash={snapshotMeta.ConfigHash} buildHash={(snapshotMeta.BuildHash ?? "null")}")
    {
        Expected = expected;
        Actual = actual;
        SnapshotPathOrId = snapshotPathOrId;
        SnapshotMeta = snapshotMeta;
    }

    public string Expected { get; }

    public string Actual { get; }

    public string SnapshotPathOrId { get; }

    public SnapshotMeta SnapshotMeta { get; }
}
