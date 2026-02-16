namespace Game.Server;

public sealed class DenyList
{
    private readonly Dictionary<string, int> _untilTickByKey = new(StringComparer.Ordinal);

    public bool IsDenied(string key, int currentTick)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _untilTickByKey.TryGetValue(key, out int untilTick) && currentTick < untilTick;
    }

    public void Deny(string key, int untilTick)
    {
        ArgumentNullException.ThrowIfNull(key);
        _untilTickByKey[key] = untilTick;
    }

    public void CleanupExpired(int currentTick)
    {
        foreach (string key in _untilTickByKey.Where(kvp => kvp.Value <= currentTick).Select(kvp => kvp.Key).ToArray())
        {
            _untilTickByKey.Remove(key);
        }
    }
}
