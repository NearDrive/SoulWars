using System.Text;
using Game.Protocol;

namespace Game.Client.Headless;

public sealed class ClientWorldView
{
    private readonly Dictionary<int, SnapshotEntity> _entities = new();

    public int Tick { get; private set; }

    public int ZoneId { get; private set; }

    public int LastSnapshotSeq { get; private set; }

    public IReadOnlyCollection<HitEventV1> LastHitEvents { get; private set; } = Array.Empty<HitEventV1>();

    public void ApplySnapshot(SnapshotV2 snapshot)
    {
        Tick = snapshot.Tick;
        ZoneId = snapshot.ZoneId;
        LastSnapshotSeq = snapshot.SnapshotSeq;

        if (snapshot.IsFull)
        {
            _entities.Clear();
            foreach (SnapshotEntity entity in snapshot.Entities)
            {
                _entities[entity.EntityId] = entity;
            }
        }
        else
        {
            foreach (int leave in snapshot.Leaves.OrderBy(static value => value))
            {
                _entities.Remove(leave);
            }

            foreach (SnapshotEntity enter in snapshot.Enters)
            {
                _entities[enter.EntityId] = enter;
            }

            foreach (SnapshotEntity update in snapshot.Updates)
            {
                _entities[update.EntityId] = update;
            }
        }

        LastHitEvents = snapshot.HitEvents
            .OrderBy(hit => hit.TickId)
            .ThenBy(hit => hit.SourceEntityId)
            .ThenBy(hit => hit.TargetEntityId)
            .ThenBy(hit => hit.AbilityId)
            .ThenBy(hit => hit.EventSeq)
            .ToArray();
    }

    public string DumpCanonical()
    {
        StringBuilder builder = new();
        builder.Append(FormattableString.Invariant($"tick={Tick} zone={ZoneId}\n"));

        foreach (SnapshotEntity entity in _entities.Values
                     .OrderBy(e => e.EntityId)
                     .ThenBy(e => e.Kind)
                     .ThenBy(e => e.PosXRaw)
                     .ThenBy(e => e.PosYRaw))
        {
            builder.Append(FormattableString.Invariant(
                $"entity={entity.EntityId} kind={entity.Kind} pos=({entity.PosXRaw},{entity.PosYRaw}) hp={entity.Hp}\n"));
        }

        return builder.ToString().TrimEnd('\n');
    }
}
