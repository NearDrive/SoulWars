using System.Buffers.Binary;
using System.Security.Cryptography;
using Game.Protocol;

namespace Game.Server;

public static class SnapshotHash
{
    public static string ComputeHex(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, snapshot);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ComputeHex(IEnumerable<Snapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        (Snapshot Snapshot, string Digest)[] canonical = snapshots
            .Select(snapshot => (Snapshot: snapshot, Digest: ComputeHex(snapshot)))
            .OrderBy(entry => entry.Snapshot.Tick)
            .ThenBy(entry => entry.Snapshot.ZoneId)
            .ThenBy(entry => entry.Digest, StringComparer.Ordinal)
            .ToArray();

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] count = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, canonical.Length);
        hash.AppendData(count);

        foreach ((Snapshot snapshot, _) in canonical)
        {
            Append(hash, snapshot);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, Snapshot snapshot)
    {
        byte[] header = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), snapshot.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), snapshot.ZoneId);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), snapshot.Entities.Length);
        hash.AppendData(header);

        foreach (SnapshotEntity entity in snapshot.Entities.OrderBy(e => e.EntityId))
        {
            byte[] entityData = new byte[25];
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(0, 4), entity.EntityId);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(4, 4), entity.PosXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(8, 4), entity.PosYRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(12, 4), entity.VelXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(16, 4), entity.VelYRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(20, 4), entity.Hp);
            entityData[24] = (byte)entity.Kind;
            hash.AppendData(entityData);
        }
    }
}
