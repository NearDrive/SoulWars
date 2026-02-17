using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Server;

public sealed class ZoneManager<TZone> where TZone : class
{
    private readonly SortedDictionary<int, TZone> _zones = new();

    public IReadOnlyCollection<int> ZoneIds => new ReadOnlyCollection<int>(_zones.Keys.ToArray());

    public TZone Get(int zoneId)
    {
        if (!_zones.TryGetValue(zoneId, out TZone? zone))
        {
            throw new KeyNotFoundException($"Zone {zoneId} is not registered.");
        }

        return zone;
    }

    public void Add(int zoneId, TZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zones.Add(zoneId, zone);
    }

    public IEnumerable<KeyValuePair<int, TZone>> OrderedEntries() => _zones;

    public void Clear() => _zones.Clear();

    public void ClearAndCopyFrom(ZoneManager<TZone> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _zones.Clear();
        foreach ((int zoneId, TZone zone) in other.OrderedEntries())
        {
            _zones.Add(zoneId, zone);
        }
    }
}
