using Game.Protocol;

namespace Game.Server;

public sealed class PlayerRegistry
{
    private readonly Dictionary<string, PlayerId> _byAccount = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PlayerState> _byPlayerId = new();
    private readonly Dictionary<int, SessionId> _activeSessionByPlayerId = new();
    private readonly Dictionary<int, PlayerId> _playerBySessionId = new();

    public PlayerId GetOrCreate(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (_byAccount.TryGetValue(accountId, out PlayerId existing))
        {
            return existing;
        }

        uint seed = StableHash32(accountId) & 0x7FFFFFFFu;
        if (seed == 0)
        {
            seed = 1;
        }

        string key = accountId;
        int attempt = 0;
        while (true)
        {
            int candidate = unchecked((int)seed);
            if (!_byPlayerId.TryGetValue(candidate, out PlayerState? state))
            {
                PlayerId created = new(candidate);
                PlayerState newState = new(created, accountId, null, null, false, false, null, null, null);
                _byAccount[accountId] = created;
                _byPlayerId[candidate] = newState;
                return created;
            }

            if (state.AccountId == accountId)
            {
                _byAccount[accountId] = state.PlayerId;
                return state.PlayerId;
            }

            attempt++;
            key = $"{accountId}#{attempt}";
            seed = StableHash32(key) & 0x7FFFFFFFu;
            if (seed == 0)
            {
                seed = 1;
            }
        }
    }

    public bool TryGetByAccount(string accountId, out PlayerId pid) => _byAccount.TryGetValue(accountId, out pid);

    public bool TryGetState(PlayerId pid, out PlayerState state) => _byPlayerId.TryGetValue(pid.Value, out state!);

    public IEnumerable<PlayerState> OrderedStates() => _byPlayerId.Values.OrderBy(s => s.PlayerId.Value);

    public int MaxPlayerId => _byPlayerId.Count == 0 ? 0 : _byPlayerId.Keys.Max();

    public void LoadFromRecords(IEnumerable<BootstrapPlayerRecord> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        _byAccount.Clear();
        _byPlayerId.Clear();
        _activeSessionByPlayerId.Clear();
        _playerBySessionId.Clear();

        foreach (BootstrapPlayerRecord player in players.OrderBy(p => p.PlayerId))
        {
            PlayerId playerId = new(player.PlayerId);
            PlayerState state = new(
                PlayerId: playerId,
                AccountId: player.AccountId,
                EntityId: player.EntityId,
                ZoneId: player.ZoneId,
                IsAlive: player.EntityId is not null,
                IsConnected: false,
                AttachedSessionId: null,
                DisconnectedAtTick: null,
                DespawnAtTick: null);

            _byAccount[player.AccountId] = playerId;
            _byPlayerId[playerId.Value] = state;
        }
    }

    public void AttachSession(PlayerId pid, SessionId sid)
    {
        _activeSessionByPlayerId[pid.Value] = sid;
        _playerBySessionId[sid.Value] = pid;
        UpdateConnectionState(pid, isConnected: true, attachedSessionId: sid, disconnectedAtTick: null, despawnAtTick: null);
    }

    public void DetachSession(SessionId sid)
    {
        if (_playerBySessionId.Remove(sid.Value, out PlayerId playerId) &&
            _activeSessionByPlayerId.TryGetValue(playerId.Value, out SessionId active) &&
            active.Value == sid.Value)
        {
            _activeSessionByPlayerId.Remove(playerId.Value);
        }
    }

    public bool TryGetPlayerBySession(SessionId sid, out PlayerId playerId) => _playerBySessionId.TryGetValue(sid.Value, out playerId);

    public bool TryGetActiveSession(PlayerId pid, out SessionId sid) => _activeSessionByPlayerId.TryGetValue(pid.Value, out sid);

    public void UpdateWorldState(PlayerId pid, int? entityId, int? zoneId, bool isAlive)
    {
        if (_byPlayerId.TryGetValue(pid.Value, out PlayerState? state) && state is not null)
        {
            _byPlayerId[pid.Value] = state with { EntityId = entityId, ZoneId = zoneId, IsAlive = isAlive };
        }
    }

    public void UpdateConnectionState(PlayerId pid, bool isConnected, SessionId? attachedSessionId, int? disconnectedAtTick, int? despawnAtTick)
    {
        if (_byPlayerId.TryGetValue(pid.Value, out PlayerState? state) && state is not null)
        {
            _byPlayerId[pid.Value] = state with
            {
                IsConnected = isConnected,
                AttachedSessionId = attachedSessionId,
                DisconnectedAtTick = disconnectedAtTick,
                DespawnAtTick = despawnAtTick
            };
        }
    }

    private static uint StableHash32(string value)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;

        uint hash = offset;
        ReadOnlySpan<char> chars = value.AsSpan();
        for (int i = 0; i < chars.Length; i++)
        {
            hash ^= chars[i];
            hash *= prime;
        }

        return hash;
    }
}

public sealed record PlayerState(
    PlayerId PlayerId,
    string AccountId,
    int? EntityId,
    int? ZoneId,
    bool IsAlive,
    bool IsConnected,
    SessionId? AttachedSessionId,
    int? DisconnectedAtTick,
    int? DespawnAtTick);
