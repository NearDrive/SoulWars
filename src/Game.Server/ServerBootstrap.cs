using System.Collections.Immutable;
using Game.Core;

namespace Game.Server;

public sealed record BootstrapPlayerRecord(string AccountId, int PlayerId, int? EntityId, int? ZoneId, System.Collections.Immutable.ImmutableArray<Game.Core.ItemStack> PendingLoot);

public sealed record ServerBootstrap(WorldState World, int ServerSeed, ImmutableArray<BootstrapPlayerRecord> Players);
