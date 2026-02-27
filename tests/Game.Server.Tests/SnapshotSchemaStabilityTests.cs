using Game.Protocol;
using Xunit;

namespace Game.Server.Tests;

[Trait("Category", "PR86")]
public sealed class SnapshotSchemaStabilityTests
{
    [Fact]
    [Trait("Category", "PR86")]
    [Trait("Category", "Canary")]
    public void SnapshotV2_SerializeParseSerialize_IsStable_AndCanonicalized()
    {
        SnapshotV2 original = new(
            Tick: 77,
            ZoneId: 3,
            SnapshotSeq: 12,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(30, 5, 4, Kind: SnapshotEntityKind.Npc),
                new SnapshotEntity(10, 1, 2, Kind: SnapshotEntityKind.Player),
                new SnapshotEntity(20, 3, 4, Kind: SnapshotEntityKind.Npc)
            ],
            Leaves: [9, 1, 7],
            Enters:
            [
                new SnapshotEntity(20, 3, 4, Kind: SnapshotEntityKind.Npc),
                new SnapshotEntity(10, 1, 2, Kind: SnapshotEntityKind.Player)
            ],
            Updates:
            [
                new SnapshotEntity(30, 5, 4, Kind: SnapshotEntityKind.Npc),
                new SnapshotEntity(10, 1, 2, Kind: SnapshotEntityKind.Player)
            ]);

        byte[] encoded1 = ProtocolCodec.Encode(original);
        SnapshotV2 parsed = Assert.IsType<SnapshotV2>(ProtocolCodec.DecodeServer(encoded1));
        byte[] encoded2 = ProtocolCodec.Encode(parsed);

        Assert.Equal(encoded1, encoded2);
        Assert.Equal([10, 20, 30], parsed.Entities.Select(entity => entity.EntityId).ToArray());
        Assert.Equal([10, 20], parsed.Enters.Select(entity => entity.EntityId).ToArray());
        Assert.Equal([10, 30], parsed.Updates.Select(entity => entity.EntityId).ToArray());
        Assert.Equal([1, 7, 9], parsed.Leaves);
        Assert.Equal(77, parsed.Tick);
        Assert.Equal(3, parsed.ZoneId);
    }
}
