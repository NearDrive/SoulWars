using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct SkillCooldownEntry(SkillId SkillId, int RemainingTicks);

public readonly record struct SkillCooldownsComponent(ImmutableArray<SkillCooldownEntry> CooldownRemainingBySkillId)
{
    public static SkillCooldownsComponent Empty => new(ImmutableArray<SkillCooldownEntry>.Empty);

    public SkillCooldownsComponent TickDown()
    {
        ImmutableArray<SkillCooldownEntry> ordered = Ordered();
        if (ordered.IsDefaultOrEmpty)
        {
            return Empty;
        }

        ImmutableArray<SkillCooldownEntry>.Builder builder = ImmutableArray.CreateBuilder<SkillCooldownEntry>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            SkillCooldownEntry entry = ordered[i];
            int remaining = entry.RemainingTicks > 0 ? entry.RemainingTicks - 1 : 0;
            if (remaining > 0)
            {
                builder.Add(new SkillCooldownEntry(entry.SkillId, remaining));
            }
        }

        return builder.Count == 0
            ? Empty
            : new SkillCooldownsComponent(builder.MoveToImmutable());
    }

    public bool IsReady(SkillId skillId)
    {
        ImmutableArray<SkillCooldownEntry> ordered = Ordered();
        int index = FindIndex(ordered, skillId);
        return index < 0 || ordered[index].RemainingTicks <= 0;
    }

    public SkillCooldownsComponent StartCooldown(SkillId skillId, int cooldownTicks)
    {
        int clamped = cooldownTicks > 0 ? cooldownTicks : 0;
        ImmutableArray<SkillCooldownEntry> ordered = Ordered();
        int index = FindIndex(ordered, skillId);

        if (clamped <= 0)
        {
            if (index < 0)
            {
                return new SkillCooldownsComponent(ordered);
            }

            return new SkillCooldownsComponent(ordered.RemoveAt(index));
        }

        SkillCooldownEntry next = new(skillId, clamped);

        if (index >= 0)
        {
            return new SkillCooldownsComponent(ordered.SetItem(index, next));
        }

        int insertIndex = ~index;
        return new SkillCooldownsComponent(ordered.Insert(insertIndex, next));
    }

    private ImmutableArray<SkillCooldownEntry> Ordered() => CooldownRemainingBySkillId.IsDefault
        ? ImmutableArray<SkillCooldownEntry>.Empty
        : CooldownRemainingBySkillId;

    private static int FindIndex(ImmutableArray<SkillCooldownEntry> entries, SkillId skillId)
    {
        int lo = 0;
        int hi = entries.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            int cmp = entries[mid].SkillId.Value.CompareTo(skillId.Value);
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
}
