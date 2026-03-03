using Game.Client.Headless;
using Game.Client.Headless.Diagnostics;
using Game.Protocol;
using Xunit;

namespace Game.Client.Tests.Trace;

[Trait("Category", "PR95")]
[Trait("Category", "ClientSmoke")]
[Trait("Category", "Canary")]
public sealed class ClientTraceRecorderTests
{
    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public Task ClientTrace_IsDeterministicAcrossRuns()
    {
        ClientTraceRecorder run1 = BuildDeterministicScriptTrace();
        ClientTraceRecorder run2 = BuildDeterministicScriptTrace();

        Assert.Equal(run1.ComputeTraceHash(), run2.ComputeTraceHash());
        Assert.Equal(run1.BuildCanonicalTraceDump(), run2.BuildCanonicalTraceDump());
        return Task.CompletedTask;
    }


    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientTrace_UsesWorldStateVisibleEntities_ForDeltaSnapshots()
    {
        ClientWorldView world = new();
        ClientTraceRecorder recorder = new();

        SnapshotV2 full = new(
            Tick: 1,
            ZoneId: 1,
            SnapshotSeq: 1,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(3, 0, 0),
                new SnapshotEntity(1, 0, 0)
            ]);

        world.ApplySnapshot(full);
        recorder.RecordTick(full, world.GetVisibleEntityIdsCanonical());

        SnapshotV2 delta = new(
            Tick: 2,
            ZoneId: 1,
            SnapshotSeq: 2,
            IsFull: false,
            Entities: Array.Empty<SnapshotEntity>(),
            Leaves: [3],
            Enters: [new SnapshotEntity(2, 0, 0)],
            Updates: Array.Empty<SnapshotEntity>());

        world.ApplySnapshot(delta);
        recorder.RecordTick(delta, world.GetVisibleEntityIdsCanonical());

        string[] lines = recorder.BuildCanonicalTraceDump().Split('\n');
        Assert.Equal("T:2|Z:1|E:1,2|EV:2:1:-:-:-,3:2:-:-:-", lines[1]);
    }

    [Fact]
    [Trait("Category", "PR95")]
    [Trait("Category", "ClientSmoke")]
    [Trait("Category", "Canary")]
    public void ClientTrace_IsCanonicalOrdered()
    {
        ClientTraceRecorder recorder = new();
        SnapshotV2 snapshot = new(
            Tick: 15,
            ZoneId: 1,
            SnapshotSeq: 9,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(7, 0, 0),
                new SnapshotEntity(1, 0, 0),
                new SnapshotEntity(2, 0, 0)
            ],
            Leaves: [9, 3],
            Enters:
            [
                new SnapshotEntity(8, 0, 0),
                new SnapshotEntity(4, 0, 0)
            ],
            HitEvents:
            [
                new HitEventV1(TickId: 15, ZoneId: 1, SourceEntityId: 7, TargetEntityId: 2, AbilityId: 1, HitPosXRaw: 0, HitPosYRaw: 0, EventSeq: 2),
                new HitEventV1(TickId: 15, ZoneId: 1, SourceEntityId: 7, TargetEntityId: 1, AbilityId: 1, HitPosXRaw: 0, HitPosYRaw: 0, EventSeq: 1)
            ]);

        recorder.RecordTick(snapshot, new[] { 7, 1, 2 });

        string canonical = recorder.BuildCanonicalTraceDump();
        Assert.Equal("T:15|Z:1|E:1,2,7|EV:3:2:-:-:-,4:1:-:-:-,7:3:1:-:1,7:3:2:-:1,8:1:-:-:-,9:2:-:-:-", canonical);
    }

    private static ClientTraceRecorder BuildDeterministicScriptTrace()
    {
        ClientWorldView world = new();
        ClientTraceRecorder recorder = new();

        SnapshotV2 tick1 = new(
            Tick: 1,
            ZoneId: 1,
            SnapshotSeq: 1,
            IsFull: true,
            Entities:
            [
                new SnapshotEntity(10, 100, 100, Hp: 50, Kind: SnapshotEntityKind.Player),
                new SnapshotEntity(12, 120, 100, Hp: 35, Kind: SnapshotEntityKind.Npc)
            ]);

        world.ApplySnapshot(tick1);
        recorder.RecordTick(tick1, world.GetVisibleEntityIdsCanonical());

        SnapshotV2 tick2 = new(
            Tick: 2,
            ZoneId: 1,
            SnapshotSeq: 2,
            IsFull: false,
            Entities: Array.Empty<SnapshotEntity>(),
            Leaves: [12],
            Enters: [new SnapshotEntity(14, 90, 100, Hp: 40, Kind: SnapshotEntityKind.Npc)],
            Updates: [new SnapshotEntity(10, 101, 100, Hp: 50, Kind: SnapshotEntityKind.Player)],
            HitEvents:
            [
                new HitEventV1(TickId: 2, ZoneId: 1, SourceEntityId: 10, TargetEntityId: 14, AbilityId: 7, HitPosXRaw: 90, HitPosYRaw: 100, EventSeq: 1)
            ]);

        world.ApplySnapshot(tick2);
        recorder.RecordTick(tick2, world.GetVisibleEntityIdsCanonical());

        return recorder;
    }
}
