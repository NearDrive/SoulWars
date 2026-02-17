using System.Collections.Immutable;
using Game.Core;
using Game.Persistence.Sqlite;

namespace Game.Server;

public sealed class ServerPersistence
{
    public void Save(ServerHost host, string dbPath)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        SqliteGameStore store = new(dbPath);
        WorldState world = host.CurrentWorld;
        string checksum = StateChecksum.Compute(world);

        PlayerRecord[] players = host
            .GetPlayersSnapshot()
            .Select(player => new PlayerRecord(player.AccountId, player.PlayerId.Value, player.EntityId, player.ZoneId))
            .ToArray();

        SnapshotMeta snapshotMeta = SnapshotMetaBuilder.Create(world, host.SimulationConfig, host.ZoneDefinitions, buildHash: null);

        store.SaveWorld(world, host.Seed, players, checksum, snapshotMeta);
    }

    public ServerBootstrap Load(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        SqliteGameStore store = new(dbPath);
        LoadResult loaded = store.LoadWorld();

        return new ServerBootstrap(
            loaded.World,
            loaded.ServerSeed,
            loaded.Players
                .Select(player => new BootstrapPlayerRecord(player.AccountId, player.PlayerId, player.EntityId, player.ZoneId))
                .ToImmutableArray());
    }

    public int LoadSeed(string dbPath)
    {
        SqliteGameStore store = new(dbPath);
        return store.LoadWorld().ServerSeed;
    }
}
