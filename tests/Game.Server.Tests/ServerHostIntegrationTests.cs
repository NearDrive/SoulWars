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

        client.Send(new HelloV2("test-client", "alice"));
        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
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
    public async Task Server_Teleport_ChangesZoneSnapshots()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await using ServerRuntime runtime = new();
        await runtime.StartAsync(ServerConfig.Default(seed: 33) with { SnapshotEveryTicks = 1, ZoneCount = 2 }, IPAddress.Loopback, 0, cts.Token);

        await using HeadlessClient client = new();
        await client.ConnectAsync("127.0.0.1", runtime.BoundPort, cts.Token);

        client.Send(new HelloV2("teleport-client", "teleporter"));
        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        _ = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2), s => s.ZoneId == 1 && s.Entities.Any(e => e.EntityId == ack.EntityId));

        client.Teleport(2);

        Snapshot teleported = await WaitForMessageAsync<Snapshot>(
            runtime,
            client,
            TimeSpan.FromSeconds(2),
            s => s.ZoneId == 2 && s.Entities.Any(e => e.EntityId == ack.EntityId));

        Assert.Equal(2, teleported.ZoneId);
        Assert.Contains(teleported.Entities, e => e.EntityId == ack.EntityId);
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

        client.Send(new HelloV2("test-client", "alice"));
        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        Snapshot currentSnapshot = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2), s => s.Entities.Any(e => e.EntityId == ack.EntityId));
        SnapshotEntity firstEntity = currentSnapshot.Entities.Single(e => e.EntityId == ack.EntityId);

        int previousX = firstEntity.PosXRaw;
        bool moved = false;
        int mapMaxRawX = config.MapWidth * Fix32.OneRaw;
        sbyte moveX = firstEntity.PosXRaw >= (mapMaxRawX / 2) ? (sbyte)-1 : (sbyte)1;

        for (int i = 0; i < 20; i++)
        {
            int previousTick = currentSnapshot.Tick;
            client.SendInput(previousTick + 1, moveX, 0);
            runtime.StepOnce();

            currentSnapshot = await WaitForMessageAsync<Snapshot>(
                runtime,
                client,
                TimeSpan.FromSeconds(2),
                s => s.Tick > previousTick && s.Entities.Any(e => e.EntityId == ack.EntityId),
                advanceServer: false);

            SnapshotEntity entity = currentSnapshot.Entities.Single(e => e.EntityId == ack.EntityId);
            Assert.InRange(entity.PosXRaw, 0, mapMaxRawX);
            if ((moveX > 0 && entity.PosXRaw > previousX) || (moveX < 0 && entity.PosXRaw < previousX))
            {
                moved = true;
                break;
            }

            previousX = entity.PosXRaw;
        }

        if (!moved)
        {
            await WaitUntilAsync(TimeSpan.FromMilliseconds(400), () =>
            {
                int previousTick = currentSnapshot.Tick;
                client.SendInput(previousTick + 1, moveX, 0);
                runtime.StepOnce();

                while (client.TryRead(out IServerMessage? message))
                {
                    if (message is Snapshot snapshot && snapshot.Tick > previousTick && snapshot.Entities.Any(e => e.EntityId == ack.EntityId))
                    {
                        currentSnapshot = snapshot;
                    }
                }

                SnapshotEntity entity = currentSnapshot.Entities.Single(e => e.EntityId == ack.EntityId);
                Assert.InRange(entity.PosXRaw, 0, mapMaxRawX);
                if ((moveX > 0 && entity.PosXRaw > previousX) || (moveX < 0 && entity.PosXRaw < previousX))
                {
                    moved = true;
                    return true;
                }

                previousX = entity.PosXRaw;
                return false;
            }, "Move intent was not reflected in snapshots within the short polling window.");
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

        client.Send(new HelloV2("determinism-client", "determinism"));
        _ = await WaitForMessageAsync<Welcome>(runtime, client, TimeSpan.FromSeconds(2));
        client.EnterZone(1);

        EnterZoneAck ack = await WaitForMessageAsync<EnterZoneAck>(runtime, client, TimeSpan.FromSeconds(2));
        Snapshot currentSnapshot = await WaitForMessageAsync<Snapshot>(runtime, client, TimeSpan.FromSeconds(2), s => s.Entities.Any(e => e.EntityId == ack.EntityId));

        const int inputCount = 30;
        Random deterministic = new(999);

        using IncrementalHash checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < inputCount; i++)
        {
            int previousTick = currentSnapshot.Tick;
            sbyte moveX = (sbyte)deterministic.Next(-1, 2);
            sbyte moveY = (sbyte)deterministic.Next(-1, 2);

            client.SendInput(previousTick + 1, moveX, moveY);
            runtime.StepOnce();

            currentSnapshot = await WaitForMessageAsync<Snapshot>(
                runtime,
                client,
                TimeSpan.FromSeconds(2),
                s => s.Tick > previousTick && s.Entities.Any(e => e.EntityId == ack.EntityId),
                advanceServer: false);

            AppendSnapshot(checksum, currentSnapshot with { Tick = i + 1 });
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

    private static async Task WaitUntilAsync(TimeSpan timeout, Func<bool> condition, string failureMessage)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        throw new Xunit.Sdk.XunitException(failureMessage);
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
