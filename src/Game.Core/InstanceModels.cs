using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct ZoneInstanceId(ulong Value);

public sealed record ZoneInstanceState(
    ZoneInstanceId Id,
    PartyId PartyId,
    ZoneId ZoneId,
    int CreationTick,
    int Ordinal,
    int RngSeed)
{
    public SimRng CreateSimRng() => new(RngSeed);
}

public sealed record InstanceRegistry(
    int NextInstanceOrdinal,
    ImmutableArray<ZoneInstanceState> Instances)
{
    public static InstanceRegistry Empty => new(NextInstanceOrdinal: 1, Instances: ImmutableArray<ZoneInstanceState>.Empty);

    public InstanceRegistry Canonicalize()
    {
        ImmutableArray<ZoneInstanceState> canonical = (Instances.IsDefault ? ImmutableArray<ZoneInstanceState>.Empty : Instances)
            .OrderBy(i => i.Id.Value)
            .ToImmutableArray();

        return this with { Instances = canonical };
    }

    public (InstanceRegistry Registry, ZoneInstanceState Instance) CreateInstance(int serverSeed, PartyId partyId, ZoneId zoneId, int creationTick)
    {
        if (NextInstanceOrdinal <= 0)
        {
            throw new InvalidOperationException("Instance ordinal must be positive.");
        }

        int ordinal = NextInstanceOrdinal;
        ZoneInstanceId id = DeterministicInstanceId.Create(serverSeed, partyId, creationTick, ordinal);
        int rngSeed = DeterministicInstanceId.RngSeed(serverSeed, partyId, creationTick, ordinal);

        ZoneInstanceState instance = new(
            Id: id,
            PartyId: partyId,
            ZoneId: zoneId,
            CreationTick: creationTick,
            Ordinal: ordinal,
            RngSeed: rngSeed);

        ImmutableArray<ZoneInstanceState> updated = (Instances.IsDefault ? ImmutableArray<ZoneInstanceState>.Empty : Instances)
            .Add(instance)
            .OrderBy(i => i.Id.Value)
            .ToImmutableArray();

        return (new InstanceRegistry(ordinal + 1, updated), instance);
    }
}

public static class DeterministicInstanceId
{
    public static ZoneInstanceId Create(int serverSeed, PartyId partyId, int creationTick, int ordinal)
    {
        ulong value = Mix64(unchecked((uint)serverSeed), unchecked((uint)partyId.Value), unchecked((uint)creationTick), unchecked((uint)ordinal));
        return new ZoneInstanceId(value == 0 ? 1UL : value);
    }

    public static int RngSeed(int serverSeed, PartyId partyId, int creationTick, int ordinal)
    {
        uint mixed = Mix32(
            unchecked((uint)serverSeed),
            unchecked((uint)partyId.Value),
            unchecked((uint)creationTick),
            unchecked((uint)ordinal),
            0x1A57u);
        return unchecked((int)(mixed == 0 ? 0x6D2B79F5u : mixed));
    }

    private static ulong Mix64(uint a, uint b, uint c, uint d)
    {
        ulong x = 1469598103934665603UL;
        x = Mix(x, a);
        x = Mix(x, b);
        x = Mix(x, c);
        x = Mix(x, d);
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return x;
    }

    private static uint Mix32(uint a, uint b, uint c, uint d, uint e)
    {
        uint x = 2166136261u;
        x = (x ^ a) * 16777619u;
        x = (x ^ b) * 16777619u;
        x = (x ^ c) * 16777619u;
        x = (x ^ d) * 16777619u;
        x = (x ^ e) * 16777619u;
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    private static ulong Mix(ulong state, uint value)
    {
        return (state ^ value) * 1099511628211UL;
    }
}
