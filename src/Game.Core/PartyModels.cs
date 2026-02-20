using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct PartyId(int Value);

public readonly record struct PartyMember(EntityId EntityId);

public readonly record struct PartyInvite(
    EntityId InviteeId,
    PartyId PartyId,
    EntityId InviterId,
    int CreatedTick);

public sealed record PartyInviteRegistry(ImmutableArray<PartyInvite> Invites)
{
    public static PartyInviteRegistry Empty => new(ImmutableArray<PartyInvite>.Empty);

    public bool Equals(PartyInviteRegistry? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        ImmutableArray<PartyInvite> left = Invites.IsDefault ? ImmutableArray<PartyInvite>.Empty : Invites;
        ImmutableArray<PartyInvite> right = other.Invites.IsDefault ? ImmutableArray<PartyInvite>.Empty : other.Invites;
        return left.SequenceEqual(right);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        ImmutableArray<PartyInvite> invites = Invites.IsDefault ? ImmutableArray<PartyInvite>.Empty : Invites;
        foreach (PartyInvite invite in invites)
        {
            hash.Add(invite);
        }

        return hash.ToHashCode();
    }

    public PartyInviteRegistry Canonicalize()
    {
        ImmutableArray<PartyInvite> canonical = (Invites.IsDefault ? ImmutableArray<PartyInvite>.Empty : Invites)
            .GroupBy(i => i.InviteeId.Value)
            .Select(g => g
                .OrderBy(i => i.CreatedTick)
                .ThenBy(i => i.PartyId.Value)
                .ThenBy(i => i.InviterId.Value)
                .First())
            .OrderBy(i => i.InviteeId.Value)
            .ToImmutableArray();

        return this with { Invites = canonical };
    }
}

public sealed record PartyState(
    PartyId Id,
    EntityId LeaderId,
    ImmutableArray<PartyMember> Members)
{
    public bool Equals(PartyState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        ImmutableArray<PartyMember> left = Members.IsDefault ? ImmutableArray<PartyMember>.Empty : Members;
        ImmutableArray<PartyMember> right = other.Members.IsDefault ? ImmutableArray<PartyMember>.Empty : other.Members;
        return Id.Equals(other.Id)
            && LeaderId.Equals(other.LeaderId)
            && left.SequenceEqual(right);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Id);
        hash.Add(LeaderId);

        ImmutableArray<PartyMember> members = Members.IsDefault ? ImmutableArray<PartyMember>.Empty : Members;
        foreach (PartyMember member in members)
        {
            hash.Add(member);
        }

        return hash.ToHashCode();
    }

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

    public bool Equals(PartyRegistry? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        ImmutableArray<PartyState> left = Parties.IsDefault ? ImmutableArray<PartyState>.Empty : Parties;
        ImmutableArray<PartyState> right = other.Parties.IsDefault ? ImmutableArray<PartyState>.Empty : other.Parties;
        return NextPartySequence == other.NextPartySequence
            && left.SequenceEqual(right);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(NextPartySequence);

        ImmutableArray<PartyState> parties = Parties.IsDefault ? ImmutableArray<PartyState>.Empty : Parties;
        foreach (PartyState party in parties)
        {
            hash.Add(party);
        }

        return hash.ToHashCode();
    }

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
