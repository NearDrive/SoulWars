using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Game.BotRunner;
using Game.Core;
using Game.Protocol;
using Game.Server;
using Xunit;

namespace Game.Server.Tests;

public sealed class ServerHostIntegrationTests
{
    [Fact]
    public async Task Tcp_Connect_EnterZone_ReceivesSnapshot()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await using ServerRuntime runtime = new();
        await runtime.StartAsync(ServerConfig.Default(seed: 77) with { SnapshotEveryTicks = 1 }, IPAddress.Loopback, 0, cts.Token);

        await using HeadlessClient client = new();
        await client.ConnectAsync("127.0.0.1", runtime.BoundPort, cts.Token);

        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.Send(new Hello("test-client"));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        Snapshot snapshot = await WaitForMessageAsync<Snapshot>(
            runtime,
            client,
            TimeSpan.FromSeconds(2),
            s => s.Entities.Any(e => e.EntityId == ack.EntityId));

        Assert.Equal(1, ack.ZoneId);
        Assert.Equal(1, snapshot.ZoneId);
    }

    [Fact]
    public async Task Tcp_MoveIntent_ChangesPosition()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        ServerConfig config = ServerConfig.Default(seed: 11) with { SnapshotEveryTicks = 1 };

        await using ServerRuntime runtime = new();
        await runtime.StartAsync(config, IPAddress.Loopback, 0, cts.Token);

        await using HeadlessClient client = new();
        await client.ConnectAsync("127.0.0.1", runtime.BoundPort, cts.Token);

        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.Send(new Hello("test-client"));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        Snapshot firstSnapshot = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2), s => s.Entities.Any(e => e.EntityId == ack.EntityId));
        SnapshotEntity firstEntity = firstSnapshot.Entities.Single(e => e.EntityId == ack.EntityId);

        int previousX = firstEntity.PosXRaw;
        bool moved = false;
        int mapMaxRawX = config.MapWidth * Fix32.OneRaw;

        for (int i = 0; i < 20; i++)
        {
            client.SendInput(firstSnapshot.Tick + i + 1, 1, 0);
            runtime.StepOnce();

            Snapshot snap = await WaitForMessageAsync<Snapshot>(
                runtime,
                client,
                TimeSpan.FromSeconds(2),
                s => s.Entities.Any(e => e.EntityId == ack.EntityId),
                advanceServer: false);

            SnapshotEntity entity = snap.Entities.Single(e => e.EntityId == ack.EntityId);
            Assert.InRange(entity.PosXRaw, 0, mapMaxRawX);
            if (entity.PosXRaw > previousX)
            {
                moved = true;
            }

            previousX = entity.PosXRaw;
        }

        Assert.True(moved);
    }

    [Fact]
    public async Task Tcp_Determinism_SameInputs_SameSnapshotChecksums()
    {
        string first = await RunScenarioAndComputeChecksumAsync();
        string second = await RunScenarioAndComputeChecksumAsync();

        Assert.Equal(first, second);
    }

    private static async Task<string> RunScenarioAndComputeChecksumAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await using ServerRuntime runtime = new();
        await runtime.StartAsync(ServerConfig.Default(seed: 123) with { SnapshotEveryTicks = 1 }, IPAddress.Loopback, 0, cts.Token);

        await using HeadlessClient client = new();
        await client.ConnectAsync("127.0.0.1", runtime.BoundPort, cts.Token);

        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.Send(new Hello("determinism-client"));
        client.EnterZone(1);

        _ = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        _ = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2));

        int lastInputTick = firstInputTick + inputCount - 1;
        Random deterministic = new(999);
            runtime,
            client,
            firstInputTick,
            lastInputTick,
            TimeSpan.FromSeconds(2));

        Assert.Equal(inputCount, snapshotsByTick.Count);

        using IncrementalHash checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int tick = firstInputTick; tick <= lastInputTick; tick++)
        {
            bool ok = snapshotsByTick.TryGetValue(tick, out Snapshot? snapshot);
            Assert.True(ok, $"Missing snapshot for tick {tick}.");
            Assert.NotNull(snapshot);
            AppendSnapshot(checksum, snapshot);
        }

        return Convert.ToHexString(checksum.GetHashAndReset());
    }

    private static async Task<Dictionary<int, Snapshot>> CollectSnapshotsForTickRangeAsync(
        ServerRuntime runtime,
        HeadlessClient client,
        int firstTick,
        int lastTick,
        TimeSpan timeout)
    {
        Dictionary<int, Snapshot> snapshotsByTick = new();
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout && snapshotsByTick.Count < (lastTick - firstTick + 1))
        {

            while (client.TryRead(out IServerMessage? message))
                if (message is Snapshot snapshot)
                {
                    if (snapshot.Tick >= firstTick && snapshot.Tick <= lastTick)
                    {
                        snapshotsByTick[snapshot.Tick] = snapshot;
                    }
                }
            }

            await Task.Yield();
        return snapshotsByTick;
    }

    private static void AppendSnapshot(IncrementalHash checksum, Snapshot snapshot)
    {
        byte[] header = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), snapshot.Tick);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), snapshot.ZoneId);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), snapshot.Entities.Length);
        checksum.AppendData(header);

        foreach (SnapshotEntity entity in snapshot.Entities.OrderBy(e => e.EntityId))
        {
            byte[] entityData = new byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(0, 4), entity.EntityId);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(4, 4), entity.PosXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(8, 4), entity.PosYRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(12, 4), entity.VelXRaw);
            BinaryPrimitives.WriteInt32LittleEndian(entityData.AsSpan(16, 4), entity.VelYRaw);
            checksum.AppendData(entityData);
        }
    }

    private static async Task<T> WaitForMessageAsync<T>(
        ServerRuntime runtime,
        HeadlessClient client,
        TimeSpan timeout,
        Func<T, bool>? predicate = null,
        bool advanceServer = true)
        where T : class, IServerMessage
    {
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            while (client.TryRead(out IServerMessage? message))
            {
                if (message is T typed && (predicate is null || predicate(typed)))
                {
                    return typed;
                }
            }

            if (advanceServer)
            {
                runtime.StepOnce();
            }

            await Task.Yield();
        }

        throw new Xunit.Sdk.XunitException($"No message of type {typeof(T).Name} received within {timeout.TotalMilliseconds}ms.");
    }
}
