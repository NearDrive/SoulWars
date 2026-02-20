using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Game.Core;

public readonly record struct ZoneChecksum(int ZoneId, string Value);

public static class StateChecksum
{
    public static string Compute(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(state.Tick);

        ImmutableArray<ZoneState> orderedZones = state.Zones.OrderBy(zone => zone.Id.Value).ToImmutableArray();
        writer.Write(orderedZones.Length);

        foreach (ZoneState zone in orderedZones)
        {
            WriteZoneData(writer, zone);
        }

        WriteGlobalWorldData(writer, state);

        writer.Flush();
        return ComputeSha256Hex(stream.ToArray());
    }

    public static string ComputeGlobalChecksum(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ImmutableArray<ZoneChecksum> zoneChecksums = ComputeZoneChecksums(state);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(zoneChecksums.Length);
        foreach (ZoneChecksum zoneChecksum in zoneChecksums)
        {
            writer.Write(zoneChecksum.ZoneId);
            writer.Write(zoneChecksum.Value);
        }

        writer.Flush();
        return ComputeSha256Hex(stream.ToArray());
    }

    public static ImmutableArray<ZoneChecksum> ComputeZoneChecksums(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Zones
            .OrderBy(zone => zone.Id.Value)
            .Select(zone => zone.ComputeChecksum())
            .ToImmutableArray();
    }

    public static string ComputeZoneChecksum(ZoneState zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        WriteZoneData(writer, zone);

        writer.Flush();
        return ComputeSha256Hex(stream.ToArray());
    }

    private static void WriteZoneData(BinaryWriter writer, ZoneState zone)
    {
        writer.Write(zone.Id.Value);
        writer.Write(zone.Map.Width);
        writer.Write(zone.Map.Height);

        byte[] mapHash = ComputeMapHash(zone.Map);
        writer.Write(mapHash.Length);
        writer.Write(mapHash);

        ImmutableArray<EntityState> orderedEntities = zone.Entities.OrderBy(entity => entity.Id.Value).ToImmutableArray();
        writer.Write(orderedEntities.Length);

        foreach (EntityState entity in orderedEntities)
        {
            writer.Write(entity.Id.Value);
            writer.Write(entity.Pos.X.Raw);
            writer.Write(entity.Pos.Y.Raw);
            writer.Write(entity.Vel.X.Raw);
            writer.Write(entity.Vel.Y.Raw);
        }

        ImmutableArray<ProjectileComponent> orderedProjectiles = (zone.Projectiles.IsDefault ? ImmutableArray<ProjectileComponent>.Empty : zone.Projectiles)
            .OrderBy(p => p.ProjectileId)
            .ToImmutableArray();
        if (!orderedProjectiles.IsDefaultOrEmpty)
        {
            writer.Write(unchecked((int)0x50524A54)); // "PRJT"
            writer.Write(orderedProjectiles.Length);
            foreach (ProjectileComponent projectile in orderedProjectiles)
            {
                writer.Write(projectile.ProjectileId);
                writer.Write(projectile.OwnerId.Value);
                writer.Write(projectile.TargetId.Value);
                writer.Write(projectile.SkillId.Value);
                writer.Write(projectile.PosX.Raw);
                writer.Write(projectile.PosY.Raw);
                writer.Write(projectile.TargetX.Raw);
                writer.Write(projectile.TargetY.Raw);
                writer.Write(projectile.SpawnTick);
                writer.Write(projectile.MaxLifetimeTicks);
                writer.Write(projectile.CollidesWithWorld);
                writer.Write(projectile.RequiresLoSOnSpawn);
            }
        }
    }

    private static void WriteGlobalWorldData(BinaryWriter writer, WorldState state)
    {
        ImmutableArray<PlayerInventoryState> orderedInventories = (state.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : state.PlayerInventories)
            .OrderBy(i => i.EntityId.Value)
            .ToImmutableArray();
        writer.Write(orderedInventories.Length);
        foreach (PlayerInventoryState inv in orderedInventories)
        {
            writer.Write(inv.EntityId.Value);
            writer.Write(inv.Inventory.Capacity);
            writer.Write(inv.Inventory.StackLimit);
            writer.Write(inv.Inventory.Slots.Length);
            for (int i = 0; i < inv.Inventory.Slots.Length; i++)
            {
                InventorySlot slot = inv.Inventory.Slots[i];
                writer.Write(slot.ItemId ?? string.Empty);
                writer.Write(slot.Quantity);
            }
        }

        ImmutableArray<PlayerWalletState> orderedWallets = (state.PlayerWallets.IsDefault ? ImmutableArray<PlayerWalletState>.Empty : state.PlayerWallets)
            .OrderBy(w => w.EntityId.Value)
            .ToImmutableArray();
        writer.Write(orderedWallets.Length);
        foreach (PlayerWalletState wallet in orderedWallets)
        {
            writer.Write(wallet.EntityId.Value);
            writer.Write(wallet.Gold);
        }

        ImmutableArray<VendorDefinition> orderedVendors = (state.Vendors.IsDefault ? ImmutableArray<VendorDefinition>.Empty : state.Vendors)
            .OrderBy(v => v.ZoneId.Value)
            .ThenBy(v => v.VendorId, StringComparer.Ordinal)
            .ToImmutableArray();
        writer.Write(orderedVendors.Length);
        foreach (VendorDefinition vendor in orderedVendors)
        {
            writer.Write(vendor.ZoneId.Value);
            writer.Write(vendor.VendorId);
            ImmutableArray<VendorOfferDefinition> offers = vendor.CanonicalOffers;
            writer.Write(offers.Length);
            for (int i = 0; i < offers.Length; i++)
            {
                VendorOfferDefinition offer = offers[i];
                writer.Write(offer.ItemId);
                writer.Write(offer.BuyPrice);
                writer.Write(offer.SellPrice);
                writer.Write(offer.MaxPerTransaction ?? -1);
            }
        }


        ImmutableArray<CombatEvent> orderedCombatEvents = (state.CombatEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : state.CombatEvents)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ToImmutableArray();
        if (!orderedCombatEvents.IsDefaultOrEmpty)
        {
            writer.Write(unchecked((int)0x43455654)); // "CEVT" marker to keep legacy checksum compatibility when empty
            writer.Write(orderedCombatEvents.Length);
            foreach (CombatEvent evt in orderedCombatEvents)
            {
                writer.Write(evt.Tick);
                writer.Write(evt.SourceId.Value);
                writer.Write(evt.TargetId.Value);
                writer.Write(evt.SkillId.Value);
                writer.Write((byte)evt.Type);
                writer.Write(evt.Amount);
            }
        }

        ImmutableArray<StatusEvent> orderedStatusEvents = (state.StatusEvents.IsDefault ? ImmutableArray<StatusEvent>.Empty : state.StatusEvents)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.EffectType)
            .ToImmutableArray();
        if (!orderedStatusEvents.IsDefaultOrEmpty)
        {
            writer.Write(unchecked((int)0x53455654)); // "SEVT"
            writer.Write(orderedStatusEvents.Length);
            foreach (StatusEvent evt in orderedStatusEvents)
            {
                writer.Write(evt.Tick);
                writer.Write(evt.SourceId.Value);
                writer.Write(evt.TargetId.Value);
                writer.Write((byte)evt.Type);
                writer.Write((byte)evt.EffectType);
                writer.Write(evt.ExpiresAtTick);
                writer.Write(evt.MagnitudeRaw);
            }
        }

        ImmutableArray<ProjectileEvent> orderedProjectileEvents = (state.ProjectileEvents.IsDefault ? ImmutableArray<ProjectileEvent>.Empty : state.ProjectileEvents)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.ProjectileId)
            .ThenBy(e => (int)e.Kind)
            .ToImmutableArray();
        bool hasProjectileMetadata = !orderedProjectileEvents.IsDefaultOrEmpty
            || state.ProjectileSpawnsDropped_Total > 0
            || state.ProjectileSpawnsDropped_LastTick > 0
            || state.NextProjectileId > 1;
        if (hasProjectileMetadata)
        {
            writer.Write(unchecked((int)0x50524556)); // "PREV"
            writer.Write(orderedProjectileEvents.Length);
            foreach (ProjectileEvent evt in orderedProjectileEvents)
            {
                writer.Write(evt.Tick);
                writer.Write(evt.ProjectileId);
                writer.Write((byte)evt.Kind);
                writer.Write(evt.OwnerId.Value);
                writer.Write(evt.TargetId.Value);
                writer.Write(evt.PosX.Raw);
                writer.Write(evt.PosY.Raw);
            }

            writer.Write(state.NextProjectileId);
            writer.Write(state.ProjectileSpawnsDropped_Total);
            writer.Write(state.ProjectileSpawnsDropped_LastTick);
        }

        ImmutableArray<VendorTransactionAuditEntry> orderedVendorAudit = (state.VendorTransactionAuditLog.IsDefault ? ImmutableArray<VendorTransactionAuditEntry>.Empty : state.VendorTransactionAuditLog)
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.PlayerEntityId.Value)
            .ThenBy(e => e.ZoneId.Value)
            .ThenBy(e => e.VendorId, StringComparer.Ordinal)
            .ThenBy(e => (int)e.Action)
            .ThenBy(e => e.ItemId, StringComparer.Ordinal)
            .ThenBy(e => e.Quantity)
            .ToImmutableArray();
        writer.Write(orderedVendorAudit.Length);
        foreach (VendorTransactionAuditEntry entry in orderedVendorAudit)
        {
            writer.Write(entry.Tick);
            writer.Write(entry.PlayerEntityId.Value);
            writer.Write(entry.ZoneId.Value);
            writer.Write(entry.VendorId);
            writer.Write((byte)entry.Action);
            writer.Write(entry.ItemId);
            writer.Write(entry.Quantity);
            writer.Write(entry.UnitPrice);
            writer.Write(entry.GoldBefore);
            writer.Write(entry.GoldAfter);
        }

        PartyRegistry partyRegistry = state.PartyRegistryOrEmpty.Canonicalize();
        bool hasPartyMetadata = partyRegistry.NextPartySequence != 1 || !partyRegistry.Parties.IsDefaultOrEmpty;

        PartyInviteRegistry inviteRegistry = state.PartyInviteRegistryOrEmpty.Canonicalize();
        bool hasInviteMetadata = !inviteRegistry.Invites.IsDefaultOrEmpty;
        if (hasInviteMetadata)
        {
            writer.Write(unchecked((int)0x50495654)); // "PIVT"
            writer.Write(inviteRegistry.Invites.Length);
            foreach (PartyInvite invite in inviteRegistry.Invites)
            {
                writer.Write(invite.InviteeId.Value);
                writer.Write(invite.PartyId.Value);
                writer.Write(invite.InviterId.Value);
                writer.Write(invite.CreatedTick);
            }
        }
        if (hasPartyMetadata)
        {
            writer.Write(unchecked((int)0x50525459)); // "PRTY"
            writer.Write(partyRegistry.NextPartySequence);
            writer.Write(partyRegistry.Parties.Length);
            foreach (PartyState party in partyRegistry.Parties)
            {
                writer.Write(party.Id.Value);
                writer.Write(party.LeaderId.Value);
                writer.Write(party.Members.Length);
                foreach (PartyMember member in party.Members)
                {
                    writer.Write(member.EntityId.Value);
                }
            }
        }
    }

    private static byte[] ComputeMapHash(TileMap map)
    {
        byte[] tileBytes = new byte[map.Tiles.Length];
        for (int i = 0; i < map.Tiles.Length; i++)
        {
            tileBytes[i] = (byte)map.Tiles[i];
        }

        return SHA256.HashData(tileBytes);
    }

    private static string ComputeSha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
