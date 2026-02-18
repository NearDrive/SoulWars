using System.IO;

namespace Game.Persistence.Sqlite;

public sealed class SnapshotChecksumMismatchException : IOException
{
    public SnapshotChecksumMismatchException(string expected, string actual, string snapshotPathOrId, SnapshotMeta snapshotMeta, string checksumScope = "global")
        : base($"Snapshot checksum mismatch for '{snapshotPathOrId}'. scope={checksumScope} expected={expected} actual={actual} serializerVersion={snapshotMeta.SerializerVersion} zoneDefinitionsHash={snapshotMeta.ZoneDefinitionsHash} configHash={snapshotMeta.ConfigHash} buildHash={(snapshotMeta.BuildHash ?? "null")}")
    {
        Expected = expected;
        Actual = actual;
        SnapshotPathOrId = snapshotPathOrId;
        SnapshotMeta = snapshotMeta;
        ChecksumScope = checksumScope;
    }

    public string Expected { get; }

    public string Actual { get; }

    public string SnapshotPathOrId { get; }

    public SnapshotMeta SnapshotMeta { get; }

    public string ChecksumScope { get; }
}
