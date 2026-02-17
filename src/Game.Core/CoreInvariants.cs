using System.Collections.Immutable;

namespace Game.Core;

public static class CoreInvariants
{
    private const int MinAcceptedLastAttackTick = -1_000_000;

    public static void Validate(WorldState world, int tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        Dictionary<int, ZoneState> zonesById = world.Zones.ToDictionary(z => z.Id.Value);
        HashSet<int> seenEntityIds = new();
        Dictionary<int, int> entityZones = new();
        HashSet<int> seenLootIds = new();
        int lastZoneId = int.MinValue;

        foreach (ZoneState zone in world.Zones)
        {
            if (zone is null)
            {
                throw new InvariantViolationException($"invariant=ZoneNotNull tick={tick}");
            }

            if (zone.Map is null)
            {
                throw new InvariantViolationException($"invariant=ZoneMapNotNull tick={tick} zoneId={zone.Id.Value}");
            }

            if (zone.Id.Value <= lastZoneId)
            {
                throw new InvariantViolationException($"invariant=ZonesOrdered tick={tick} zoneId={zone.Id.Value} lastZoneId={lastZoneId}");
            }

            lastZoneId = zone.Id.Value;

            if (zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Masks.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Kinds.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Positions.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Health.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Combat.Length
                || zone.EntitiesData.AliveIds.Length != zone.EntitiesData.Ai.Length)
            {
                throw new InvariantViolationException($"invariant=ParallelArraysLength tick={tick} zoneId={zone.Id.Value}");
            }

            int lastAliveId = int.MinValue;
            for (int i = 0; i < zone.EntitiesData.AliveIds.Length; i++)
            {
                int id = zone.EntitiesData.AliveIds[i].Value;
                if (id <= lastAliveId)
                {
                    throw new InvariantViolationException($"invariant=AliveIdsOrdered tick={tick} zoneId={zone.Id.Value} entityId={id} lastEntityId={lastAliveId}");
                }

                lastAliveId = id;
                ComponentMask mask = zone.EntitiesData.Masks[i];
                if (!mask.Has(ComponentMask.PositionBit)
                    || !mask.Has(ComponentMask.HealthBit)
                    || !mask.Has(ComponentMask.CombatBit))
                {
                    throw new InvariantViolationException($"invariant=RequiredMaskBits tick={tick} zoneId={zone.Id.Value} entityId={id} mask={mask.Bits}");
                }

                bool expectsAi = zone.EntitiesData.Kinds[i] == EntityKind.Npc;
                bool hasAi = mask.Has(ComponentMask.AiBit);
                if (expectsAi != hasAi)
                {
                    throw new InvariantViolationException($"invariant=AiMaskMatchesKind tick={tick} zoneId={zone.Id.Value} entityId={id} kind={zone.EntitiesData.Kinds[i]} hasAi={hasAi}");
                }

                if (mask.Has(ComponentMask.CombatBit) && !mask.Has(ComponentMask.PositionBit))
                {
                    throw new InvariantViolationException($"invariant=CombatRequiresPosition tick={tick} zoneId={zone.Id.Value} entityId={id}");
                }

                if (mask.Has(ComponentMask.AiBit) && !mask.Has(ComponentMask.PositionBit))
                {
                    throw new InvariantViolationException($"invariant=AiRequiresPosition tick={tick} zoneId={zone.Id.Value} entityId={id}");
                }
            }

            int lastEntityId = int.MinValue;
            foreach (EntityState entity in zone.Entities)
            {
                if (entity.Id.Value <= lastEntityId)
                {
                    throw new InvariantViolationException($"invariant=EntitiesOrdered tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastEntityId={lastEntityId}");
                }

                lastEntityId = entity.Id.Value;

                if (!seenEntityIds.Add(entity.Id.Value))
                {
                    throw new InvariantViolationException($"invariant=UniqueEntityAcrossZones tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value}");
                }

                entityZones[entity.Id.Value] = zone.Id.Value;

                if (entity.Hp > entity.MaxHp)
                {
                    throw new InvariantViolationException($"invariant=HealthUpperBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp} maxHp={entity.MaxHp}");
                }

                if (entity.Hp < 0)
                {
                    throw new InvariantViolationException($"invariant=HealthLowerBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp}");
                }

                if (entity.IsAlive != (entity.Hp > 0))
                {
                    throw new InvariantViolationException($"invariant=IsAliveMatchesHp tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp} isAlive={entity.IsAlive}");
                }

                if (entity.Hp <= 0)
                {
                    throw new InvariantViolationException($"invariant=NoDeadEntityInZone tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} hp={entity.Hp}");
                }

                if (entity.AttackCooldownTicks < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackCooldownNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} cooldown={entity.AttackCooldownTicks}");
                }

                if (entity.LastAttackTick > tick)
                {
                    throw new InvariantViolationException($"invariant=LastAttackNotInFuture tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastAttackTick={entity.LastAttackTick}");
                }

                if (entity.LastAttackTick < MinAcceptedLastAttackTick)
                {
                    throw new InvariantViolationException($"invariant=LastAttackTickLowerBound tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} lastAttackTick={entity.LastAttackTick}");
                }

                if (entity.AttackDamage < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackDamageNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} damage={entity.AttackDamage}");
                }

                if (entity.AttackRange.Raw < 0)
                {
                    throw new InvariantViolationException($"invariant=AttackRangeNonNegative tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} attackRangeRaw={entity.AttackRange.Raw}");
                }

                int tileX = Fix32.FloorToInt(entity.Pos.X);
                int tileY = Fix32.FloorToInt(entity.Pos.Y);
                if (zone.Map.Get(tileX, tileY) == TileKind.Solid)
                {
                    throw new InvariantViolationException($"invariant=EntityNotOnSolidTile tick={tick} zoneId={zone.Id.Value} entityId={entity.Id.Value} tileX={tileX} tileY={tileY}");
                }
            }
        }


        int lastLootId = int.MinValue;
        foreach (LootEntityState loot in (world.LootEntities.IsDefault ? ImmutableArray<LootEntityState>.Empty : world.LootEntities))
        {
            if (loot.Id.Value <= lastLootId)
            {
                throw new InvariantViolationException($"invariant=LootEntitiesOrdered tick={tick} lootEntityId={loot.Id.Value} lastLootEntityId={lastLootId}");
            }

            lastLootId = loot.Id.Value;

            if (!seenLootIds.Add(loot.Id.Value))
            {
                throw new InvariantViolationException($"invariant=UniqueLootEntityId tick={tick} lootEntityId={loot.Id.Value}");
            }

            if (seenEntityIds.Contains(loot.Id.Value))
            {
                throw new InvariantViolationException($"invariant=LootEntityIdCollision tick={tick} lootEntityId={loot.Id.Value}");
            }

            if (!zonesById.TryGetValue(loot.ZoneId.Value, out ZoneState? lootZone))
            {
                throw new InvariantViolationException($"invariant=LootZoneExists tick={tick} lootEntityId={loot.Id.Value} zoneId={loot.ZoneId.Value}");
            }

            if (!IsFinite(loot.Pos.X) || !IsFinite(loot.Pos.Y))
            {
                throw new InvariantViolationException($"invariant=LootPositionFinite tick={tick} lootEntityId={loot.Id.Value} posXRaw={loot.Pos.X.Raw} posYRaw={loot.Pos.Y.Raw}");
            }

            int lootTileX = Fix32.FloorToInt(loot.Pos.X);
            int lootTileY = Fix32.FloorToInt(loot.Pos.Y);
            if (lootTileX < 0 || lootTileY < 0 || lootTileX >= lootZone.Map.Width || lootTileY >= lootZone.Map.Height)
            {
                throw new InvariantViolationException($"invariant=LootInsideBounds tick={tick} lootEntityId={loot.Id.Value} tileX={lootTileX} tileY={lootTileY}");
            }

            if (loot.Items.IsDefaultOrEmpty)
            {
                throw new InvariantViolationException($"invariant=LootItemsNonEmpty tick={tick} lootEntityId={loot.Id.Value}");
            }

            string lastItemId = string.Empty;
            for (int i = 0; i < loot.Items.Length; i++)
            {
                ItemStack item = loot.Items[i];
                if (string.CompareOrdinal(item.ItemId, lastItemId) < 0)
                {
                    throw new InvariantViolationException($"invariant=LootItemsOrdered tick={tick} lootEntityId={loot.Id.Value} itemId={item.ItemId} lastItemId={lastItemId}");
                }

                if (item.Quantity <= 0)
                {
                    throw new InvariantViolationException($"invariant=LootItemQuantityPositive tick={tick} lootEntityId={loot.Id.Value} itemId={item.ItemId} quantity={item.Quantity}");
                }

                lastItemId = item.ItemId;
            }
        }

        int lastInventoryEntityId = int.MinValue;
        foreach (PlayerInventoryState playerInventory in (world.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : world.PlayerInventories))
        {
            if (playerInventory.EntityId.Value <= lastInventoryEntityId)
            {
                throw new InvariantViolationException($"invariant=PlayerInventoriesOrdered tick={tick} entityId={playerInventory.EntityId.Value} lastEntityId={lastInventoryEntityId}");
            }

            lastInventoryEntityId = playerInventory.EntityId.Value;
            InventoryComponent inventory = playerInventory.Inventory;
            if (inventory.Capacity <= 0 || inventory.Slots.Length != inventory.Capacity)
            {
                throw new InvariantViolationException($"invariant=InventoryCapacityMatchesSlots tick={tick} entityId={playerInventory.EntityId.Value} capacity={inventory.Capacity} slots={inventory.Slots.Length}");
            }

            if (inventory.StackLimit <= 0)
            {
                throw new InvariantViolationException($"invariant=InventoryStackLimitPositive tick={tick} entityId={playerInventory.EntityId.Value} stackLimit={inventory.StackLimit}");
            }

            for (int i = 0; i < inventory.Slots.Length; i++)
            {
                InventorySlot slot = inventory.Slots[i];
                if (slot.Quantity < 0)
                {
                    throw new InvariantViolationException($"invariant=InventoryQuantityNonNegative tick={tick} entityId={playerInventory.EntityId.Value} slot={i} quantity={slot.Quantity}");
                }

                if (slot.Quantity > inventory.StackLimit)
                {
                    throw new InvariantViolationException($"invariant=NoStackOverMax tick={tick} entityId={playerInventory.EntityId.Value} slot={i} quantity={slot.Quantity} stackLimit={inventory.StackLimit}");
                }

                if (slot.Quantity == 0 && !string.IsNullOrEmpty(slot.ItemId))
                {
                    throw new InvariantViolationException($"invariant=EmptyInventorySlotHasNoItemId tick={tick} entityId={playerInventory.EntityId.Value} slot={i}");
                }

                if (slot.Quantity > 0 && string.IsNullOrWhiteSpace(slot.ItemId))
                {
                    throw new InvariantViolationException($"invariant=InventoryItemIdRequired tick={tick} entityId={playerInventory.EntityId.Value} slot={i} quantity={slot.Quantity}");
                }

            }
        }


        int lastWalletEntityId = int.MinValue;
        foreach (PlayerWalletState wallet in (world.PlayerWallets.IsDefault ? ImmutableArray<PlayerWalletState>.Empty : world.PlayerWallets))
        {
            if (wallet.EntityId.Value <= lastWalletEntityId)
            {
                throw new InvariantViolationException($"invariant=PlayerWalletsOrdered tick={tick} entityId={wallet.EntityId.Value} lastEntityId={lastWalletEntityId}");
            }

            lastWalletEntityId = wallet.EntityId.Value;
            if (wallet.Gold < 0)
            {
                throw new InvariantViolationException($"invariant=WalletGoldNonNegative tick={tick} entityId={wallet.EntityId.Value} gold={wallet.Gold}");
            }
        }

        int lastVendorZoneId = int.MinValue;
        string lastVendorId = string.Empty;
        foreach (VendorDefinition vendor in (world.Vendors.IsDefault ? ImmutableArray<VendorDefinition>.Empty : world.Vendors))
        {
            if (vendor.ZoneId.Value < lastVendorZoneId || (vendor.ZoneId.Value == lastVendorZoneId && string.CompareOrdinal(vendor.VendorId, lastVendorId) <= 0))
            {
                throw new InvariantViolationException($"invariant=VendorsOrdered tick={tick} zoneId={vendor.ZoneId.Value} vendorId={vendor.VendorId}");
            }

            if (!world.Zones.Any(z => z.Id.Value == vendor.ZoneId.Value))
            {
                throw new InvariantViolationException($"invariant=VendorZoneExists tick={tick} zoneId={vendor.ZoneId.Value} vendorId={vendor.VendorId}");
            }

            lastVendorZoneId = vendor.ZoneId.Value;
            lastVendorId = vendor.VendorId;

            string lastItemId = string.Empty;
            foreach (VendorOfferDefinition offer in vendor.CanonicalOffers)
            {
                if (string.CompareOrdinal(offer.ItemId, lastItemId) < 0)
                {
                    throw new InvariantViolationException($"invariant=VendorOffersOrdered tick={tick} vendorId={vendor.VendorId} itemId={offer.ItemId}");
                }

                if (offer.BuyPrice < 0 || offer.SellPrice < 0)
                {
                    throw new InvariantViolationException($"invariant=VendorPricesNonNegative tick={tick} vendorId={vendor.VendorId} itemId={offer.ItemId}");
                }

                lastItemId = offer.ItemId;
            }
        }

        int lastVendorAuditTick = int.MinValue;
        int lastVendorAuditPlayerId = int.MinValue;
        foreach (VendorTransactionAuditEntry entry in (world.VendorTransactionAuditLog.IsDefault ? ImmutableArray<VendorTransactionAuditEntry>.Empty : world.VendorTransactionAuditLog))
        {
            if (entry.Tick < lastVendorAuditTick || (entry.Tick == lastVendorAuditTick && entry.PlayerEntityId.Value < lastVendorAuditPlayerId))
            {
                throw new InvariantViolationException($"invariant=VendorAuditOrdered tick={tick} entryTick={entry.Tick} entityId={entry.PlayerEntityId.Value}");
            }

            lastVendorAuditTick = entry.Tick;
            lastVendorAuditPlayerId = entry.PlayerEntityId.Value;

            if (entry.GoldAfter < 0 || entry.GoldBefore < 0)
            {
                throw new InvariantViolationException($"invariant=VendorAuditGoldNonNegative tick={tick} entityId={entry.PlayerEntityId.Value}");
            }
        }

        int lastAuditTick = int.MinValue;
        int lastAuditPlayerEntityId = int.MinValue;
        foreach (PlayerDeathAuditEntry entry in (world.PlayerDeathAuditLog.IsDefault ? ImmutableArray<PlayerDeathAuditEntry>.Empty : world.PlayerDeathAuditLog))
        {
            if (entry.Tick < lastAuditTick || (entry.Tick == lastAuditTick && entry.PlayerEntityId.Value < lastAuditPlayerEntityId))
            {
                throw new InvariantViolationException($"invariant=PlayerDeathAuditOrdered tick={tick} entryTick={entry.Tick} entityId={entry.PlayerEntityId.Value}");
            }

            lastAuditTick = entry.Tick;
            lastAuditPlayerEntityId = entry.PlayerEntityId.Value;

            if (!world.Zones.Any(z => z.Id.Value == entry.ZoneId.Value))
            {
                throw new InvariantViolationException($"invariant=PlayerDeathAuditZoneExists tick={tick} zoneId={entry.ZoneId.Value} entityId={entry.PlayerEntityId.Value}");
            }

            foreach (ItemStack dropped in entry.DroppedItems)
            {
                if (dropped.Quantity <= 0)
                {
                    throw new InvariantViolationException($"invariant=PlayerDeathAuditDropPositive tick={tick} entityId={entry.PlayerEntityId.Value} itemId={dropped.ItemId} quantity={dropped.Quantity}");
                }
            }
        }

        int lastLocationEntityId = int.MinValue;
        foreach (EntityLocation location in world.EntityLocations)
        {
            if (location.Id.Value <= lastLocationEntityId)
            {
                throw new InvariantViolationException($"invariant=EntityLocationsOrdered tick={tick} entityId={location.Id.Value} lastEntityId={lastLocationEntityId}");
            }

            lastLocationEntityId = location.Id.Value;

            if (!entityZones.TryGetValue(location.Id.Value, out int zoneId) || zoneId != location.ZoneId.Value)
            {
                throw new InvariantViolationException($"invariant=EntityLocationMatchesZone tick={tick} entityId={location.Id.Value} locationZoneId={location.ZoneId.Value}");
            }
        }

        if (entityZones.Count != world.EntityLocations.Length)
        {
            throw new InvariantViolationException($"invariant=EntityLocationCountMatches tick={tick} zoneEntities={entityZones.Count} locations={world.EntityLocations.Length}");
        }
    }

    private static bool IsFinite(Fix32 value) => value.Raw != int.MinValue && value.Raw != int.MaxValue;
}
