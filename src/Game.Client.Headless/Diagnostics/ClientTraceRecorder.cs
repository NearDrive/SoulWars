using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Game.Protocol;

namespace Game.Client.Headless.Diagnostics;

public sealed class ClientTraceRecorder
{
    private readonly List<TickTrace> _ticks = new();

    public int TotalHitEvents { get; private set; }

    public void RecordTick(SnapshotV2 snapshot, IReadOnlyList<int> visibleEntityIds)
    {
        int[] canonicalVisibleEntityIds = visibleEntityIds
            .OrderBy(static entityId => entityId)
            .ToArray();


        List<EventTrace> events = new();
        foreach (SnapshotEntity spawn in snapshot.Enters.OrderBy(static entity => entity.EntityId))
        {
            events.Add(new EventTrace(spawn.EntityId, EventKind: 1, TargetId: null, Amount: null, AbilityId: null));
        }

        foreach (int entityId in snapshot.Leaves.OrderBy(static leaveId => leaveId))
        {
            events.Add(new EventTrace(entityId, EventKind: 2, TargetId: null, Amount: null, AbilityId: null));
        }

        foreach (HitEventV1 hit in snapshot.HitEvents
                     .OrderBy(static evt => evt.SourceEntityId)
                     .ThenBy(static evt => evt.TargetEntityId)
                     .ThenBy(static evt => evt.AbilityId)
                     .ThenBy(static evt => evt.EventSeq))
        {
            events.Add(new EventTrace(hit.SourceEntityId, EventKind: 3, TargetId: hit.TargetEntityId, Amount: null, AbilityId: hit.AbilityId));
            TotalHitEvents++;
        }

        EventTrace[] sortedEvents = events
            .OrderBy(static evt => evt.EntityId)
            .ThenBy(static evt => evt.EventKind)
            .ThenBy(static evt => evt.TargetId ?? int.MinValue)
            .ThenBy(static evt => evt.Amount ?? int.MinValue)
            .ThenBy(static evt => evt.AbilityId ?? int.MinValue)
            .ToArray();

        _ticks.Add(new TickTrace(
            TickId: snapshot.Tick,
            ZoneId: snapshot.ZoneId,
            VisibleEntityIds: canonicalVisibleEntityIds,
            Events: sortedEvents));
    }

    public string BuildCanonicalTraceDump()
    {
        StringBuilder builder = new();
        foreach (TickTrace tick in _ticks.OrderBy(static trace => trace.TickId))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            AppendTick(builder, tick);
        }

        return builder.ToString();
    }

    public string ComputeTraceHash()
    {
        string canonicalDump = BuildCanonicalTraceDump();
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalDump);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static void AppendTick(StringBuilder builder, TickTrace tick)
    {
        builder.Append("T:");
        builder.Append(tick.TickId.ToString(CultureInfo.InvariantCulture));
        builder.Append("|Z:");
        builder.Append(tick.ZoneId.ToString(CultureInfo.InvariantCulture));
        builder.Append("|E:");
        AppendIntList(builder, tick.VisibleEntityIds);
        builder.Append("|EV:");

        for (int i = 0; i < tick.Events.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            EventTrace evt = tick.Events[i];
            builder.Append(evt.EntityId.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(evt.EventKind.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(evt.TargetId?.ToString(CultureInfo.InvariantCulture) ?? "-");
            builder.Append(':');
            builder.Append(evt.Amount?.ToString(CultureInfo.InvariantCulture) ?? "-");
            builder.Append(":");
            builder.Append(evt.AbilityId?.ToString(CultureInfo.InvariantCulture) ?? "-");
        }
    }

    private static void AppendIntList(StringBuilder builder, IReadOnlyList<int> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
    }
}

public sealed record TickTrace(int TickId, int ZoneId, int[] VisibleEntityIds, EventTrace[] Events);

public sealed record EventTrace(int EntityId, byte EventKind, int? TargetId, int? Amount, int? AbilityId);
