using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct PartyId(int Value);

public readonly record struct PartyMember(EntityId EntityId);

public sealed record PartyState(
    PartyId Id,
    EntityId LeaderId,
    ImmutableArray<PartyMember> Members)
{
    public PartyState Canonicalize()
    {
        ImmutableArray<PartyMember> canonicalMembers = Members.IsDefault
            ? ImmutableArray<PartyMember>.Empty
            : Members
                .DistinctBy(m => m.EntityId.Value)
                .OrderBy(m => m.EntityId.Value)
                .ToImmutableArray();

        return this with { Members = canonicalMembers };
    }
}

public sealed record PartyRegistry(
    int NextPartySequence,
    ImmutableArray<PartyState> Parties)
{
    public static PartyRegistry Empty => new(NextPartySequence: 1, Parties: ImmutableArray<PartyState>.Empty);

    public bool IsMember(EntityId entityId)
    {
        foreach (PartyState party in Parties.IsDefault ? ImmutableArray<PartyState>.Empty : Parties)
        {
            if (party.Members.Any(m => m.EntityId.Value == entityId.Value))
            {
                return true;
            }
        }

        return false;
    }

    public PartyRegistry CreateParty(EntityId leaderId)
    {
        if (IsMember(leaderId))
        {
            throw new InvalidOperationException($"Entity {leaderId.Value} already belongs to a party.");
        }

        PartyState party = new(
            Id: new PartyId(NextPartySequence),
            LeaderId: leaderId,
            Members: ImmutableArray.Create(new PartyMember(leaderId)));

        ImmutableArray<PartyState> updated = (Parties.IsDefault ? ImmutableArray<PartyState>.Empty : Parties)
            .Add(party.Canonicalize())
            .OrderBy(p => p.Id.Value)
            .ToImmutableArray();

        return new PartyRegistry(NextPartySequence + 1, updated);
    }

    public PartyRegistry Canonicalize()
    {
        ImmutableArray<PartyState> canonicalParties = (Parties.IsDefault ? ImmutableArray<PartyState>.Empty : Parties)
            .Select(p => p.Canonicalize())
            .OrderBy(p => p.Id.Value)
            .ToImmutableArray();

        return this with { Parties = canonicalParties };
    }
}
