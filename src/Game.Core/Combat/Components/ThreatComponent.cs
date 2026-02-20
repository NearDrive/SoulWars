using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct ThreatEntry(EntityId SourceEntityId, int Threat, int LastTick);

public readonly record struct ThreatComponent(ImmutableArray<ThreatEntry> Entries)
{
    public const int MaxThreat = 1_000_000_000;

    public static ThreatComponent Empty => new(ImmutableArray<ThreatEntry>.Empty);

    public ThreatComponent AddThreat(EntityId source, int amount, int tick)
    {
        if (amount <= 0)
        {
            return this;
        }

        ImmutableArray<ThreatEntry> ordered = OrderedEntries();
        int index = FindIndex(ordered, source);
        if (index >= 0)
        {
            ThreatEntry current = ordered[index];
            int nextThreat = ClampThreat((long)current.Threat + amount);
            return new ThreatComponent(ordered.SetItem(index, current with { Threat = nextThreat, LastTick = tick }));
        }

        int insertAt = ~index;
        ThreatEntry incoming = new(source, ClampThreat(amount), tick);
        ImmutableArray<ThreatEntry>.Builder builder = ImmutableArray.CreateBuilder<ThreatEntry>(ordered.Length + 1);
        for (int i = 0; i < insertAt; i++)
        {
            builder.Add(ordered[i]);
        }

        builder.Add(incoming);
        for (int i = insertAt; i < ordered.Length; i++)
        {
            builder.Add(ordered[i]);
        }

        return new ThreatComponent(builder.MoveToImmutable());
    }

    public ThreatComponent RemoveAt(int index)
    {
        ImmutableArray<ThreatEntry> ordered = OrderedEntries();
        if (index < 0 || index >= ordered.Length)
        {
            return this;
        }

        return new ThreatComponent(ordered.RemoveAt(index));
    }

    public ImmutableArray<ThreatEntry> OrderedEntries() => Entries.IsDefault
        ? ImmutableArray<ThreatEntry>.Empty
        : Entries;

    private static int FindIndex(ImmutableArray<ThreatEntry> entries, EntityId source)
    {
        int lo = 0;
        int hi = entries.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            int cmp = entries[mid].SourceEntityId.Value.CompareTo(source.Value);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    private static int ClampThreat(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= MaxThreat)
        {
            return MaxThreat;
        }

        return (int)value;
    }
}
