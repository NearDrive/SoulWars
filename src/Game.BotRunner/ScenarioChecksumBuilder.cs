using System.Buffers.Binary;
using System.Security.Cryptography;
using Game.Protocol;

namespace Game.BotRunner;

public sealed class ScenarioChecksumBuilder : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void AppendSnapshot(int recipientIndex, Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        byte[] header = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), snapshot.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), snapshot.ZoneId);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), recipientIndex);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), snapshot.Entities.Length);
        _hash.AppendData(header);

        foreach (SnapshotEntity entity in snapshot.Entities.OrderBy(e => e.EntityId))
        {
            byte[] entityData = new byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(0, 4), entity.EntityId);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(4, 4), entity.PosXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(8, 4), entity.PosYRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(12, 4), entity.VelXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(16, 4), entity.VelYRaw);
            _hash.AppendData(entityData);
        }
    }

    public string BuildHexLower()
    {
        byte[] digest = _hash.GetHashAndReset();
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public void Dispose() => _hash.Dispose();
}
