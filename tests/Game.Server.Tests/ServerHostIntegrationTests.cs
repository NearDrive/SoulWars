using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Diagnostics;
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

        Welcome welcome = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.Send(new Hello("test-client"));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        Snapshot snapshot = await WaitForMessageAsync<Snapshot>(
            runtime,
            client,
            TimeSpan.FromSeconds(2),
            s => s.Entities.Any(entity => entity.EntityId == ack.EntityId));

        Assert.NotNull(welcome);
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
        SnapshotEntity firstEntity = firstSnapshot.Entities.Single(entity => entity.EntityId == ack.EntityId);

        int previousX = firstEntity.PosXRaw;
        bool moved = false;
        int mapMaxRawX = config.MapWidth * Fix32.OneRaw;

        for (int i = 0; i < 20; i++)
        {
            client.SendInput(firstSnapshot.Tick + i + 1, 1, 0);
            runtime.StepOnce();

            Snapshot snapshot = await WaitForMessageAsync<Snapshot>(
                runtime,
                client,
                TimeSpan.FromSeconds(2),
                s => s.Entities.Any(entity => entity.EntityId == ack.EntityId));
            SnapshotEntity entity = snapshot.Entities.Single(e => e.EntityId == ack.EntityId);

            Assert.InRange(entity.PosXRaw, 0, mapMaxRawX);
            if (entity.PosXRaw > previousX)
            {
                moved = true;
            }

            previousX = entity.PosXRaw;
        }

        Assert.True(moved, "Expected at least one positive X movement in snapshots.");
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

        using IncrementalHash checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Random deterministic = new(999);

        for (int i = 0; i < 30; i++)
        {
            sbyte moveX = (sbyte)deterministic.Next(-1, 2);
            sbyte moveY = (sbyte)deterministic.Next(-1, 2);

            client.SendInput(i + 2, moveX, moveY);
            runtime.StepOnce();

            Snapshot snapshot = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2));
            AppendSnapshot(checksum, snapshot);
        }

        return Convert.ToHexString(checksum.GetHashAndReset());
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
        Func<T, bool>? predicate = null)
        where T : class, IServerMessage
    {
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            T? found = null;
            DrainMessages(client, message =>
            {
                if (message is T typed && (predicate is null || predicate(typed)))
                {
                    found = typed;
                }
            });

            if (found is not null)
            {
                return found;
            }

            runtime.StepOnce();
            await Task.Yield();
        }

        throw new Xunit.Sdk.XunitException($"No message of type {typeof(T).Name} received within {timeout.TotalMilliseconds}ms.");
    }

    private static void DrainMessages(HeadlessClient client, Action<IServerMessage> onMessage)
    {
        while (client.TryRead(out IServerMessage? message))
        {
            if (message is not null)
            {
                onMessage(message);
            }
        }
    }
}
