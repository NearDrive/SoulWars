using System.Collections.Immutable;

namespace Game.Core;

public enum VendorAction : byte
{
    Buy = 1,
    Sell = 2
}

public sealed record VendorOfferDefinition(
    string ItemId,
    long BuyPrice,
    long SellPrice,
    int? MaxPerTransaction = null);

public sealed record VendorDefinition(
    string VendorId,
    ZoneId ZoneId,
    ImmutableArray<VendorOfferDefinition> Offers)
{
    public ImmutableArray<VendorOfferDefinition> CanonicalOffers => Offers
        .OrderBy(o => o.ItemId, StringComparer.Ordinal)
        .ThenBy(o => o.BuyPrice)
        .ThenBy(o => o.SellPrice)
        .ToImmutableArray();
}

public sealed record PlayerWalletState(EntityId EntityId, long Gold);

public sealed record VendorTransactionAuditEntry(
    int Tick,
    EntityId PlayerEntityId,
    ZoneId ZoneId,
    string VendorId,
    VendorAction Action,
    string ItemId,
    int Quantity,
    long UnitPrice,
    long GoldBefore,
    long GoldAfter);
