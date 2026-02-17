using System.Collections.Immutable;
using System.Text;
using Game.Core;

namespace Game.Persistence;

public static class WorldStateSerializer
{
    private sealed record RawSnapshotPayload(
        int Version,
        int Tick,
        ImmutableArray<ZoneState> Zones,
        ImmutableArray<LootEntityState> LootEntities,
        ImmutableArray<PlayerInventoryState> PlayerInventories,
        ImmutableArray<PlayerWalletState> PlayerWallets,
        ImmutableArray<VendorDefinition> Vendors,
        ImmutableArray<VendorTransactionAuditEntry> VendorAudit);

    private sealed record V4SnapshotPayload(
        int Tick,
        ImmutableArray<ZoneState> Zones,
        ImmutableArray<LootEntityState> LootEntities,
        ImmutableArray<PlayerInventoryState> PlayerInventories,
        ImmutableArray<PlayerWalletState> PlayerWallets,
        ImmutableArray<VendorDefinition> Vendors,
        ImmutableArray<VendorTransactionAuditEntry> VendorAudit);

    private static readonly byte[] Magic = "SWWORLD\0"u8.ToArray();
    private const int CurrentVersion = 4;
    public static int SerializerVersion => CurrentVersion;
    private const int MaxZoneCount = 10_000;
    private const int MaxMapDimension = 16_384;
    private const int MaxEntityCountPerZone = 2_000_000;
    private const int MaxLootEntityCount = 2_000_000;
    private const int MaxLootItemsPerEntity = 4096;
    private const int MaxInventoryCount = 2_000_000;
    private const int MaxInventoryCapacity = 4096;
    private const int MaxWalletCount = 2_000_000;
    private const int MaxVendorCount = 100_000;
    private const int MaxVendorOffers = 4096;
    private const int MaxVendorAuditCount = 5_000_000;

    public static void Save(Stream stream, WorldState world)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(world);

        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(CurrentVersion);
        writer.Write(world.Tick);

        ImmutableArray<ZoneState> zones = world.Zones;
        EnsureSortedZones(zones);

        writer.Write(zones.Length);

        foreach (ZoneState zone in zones)
        {
            writer.Write(zone.Id.Value);
            WriteMap(writer, zone.Map);
            WriteEntities(writer, zone.EntitiesData);
        }

        WriteLootEntities(writer, world.LootEntities.IsDefault ? ImmutableArray<LootEntityState>.Empty : world.LootEntities);
        WritePlayerInventories(writer, world.PlayerInventories.IsDefault ? ImmutableArray<PlayerInventoryState>.Empty : world.PlayerInventories);
        WritePlayerWallets(writer, world.PlayerWallets.IsDefault ? ImmutableArray<PlayerWalletState>.Empty : world.PlayerWallets);
        WriteVendors(writer, world.Vendors.IsDefault ? ImmutableArray<VendorDefinition>.Empty : world.Vendors);
        WriteVendorAudit(writer, world.VendorTransactionAuditLog.IsDefault ? ImmutableArray<VendorTransactionAuditEntry>.Empty : world.VendorTransactionAuditLog);
    }

    public static WorldState Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            RawSnapshotPayload raw = LoadRaw(reader);
            V4SnapshotPayload migrated = MigrateToV4(raw);
            return LoadV4(migrated);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Unexpected end of stream while reading world state.", ex);
        }
    }

    public static byte[] SaveToBytes(WorldState world)
    {
        using MemoryStream stream = new();
        Save(stream, world);
        return stream.ToArray();
    }

    public static WorldState LoadFromBytes(ReadOnlySpan<byte> data)
    {
        using MemoryStream stream = new(data.ToArray(), writable: false);
        return Load(stream);
    }

    private static RawSnapshotPayload LoadRaw(BinaryReader reader)
    {
        byte[] magic = reader.ReadBytes(Magic.Length);
        if (magic.Length != Magic.Length || !magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid world-state magic header.");
        }

        int version = reader.ReadInt32();
        if (version is not (1 or 2 or 3 or CurrentVersion))
        {
            throw new InvalidDataException($"Unsupported world-state version '{version}'.");
        }

        int tick = reader.ReadInt32();
        int zoneCount = reader.ReadInt32();
        ValidateCount(zoneCount, MaxZoneCount, nameof(zoneCount));

        ImmutableArray<ZoneState>.Builder zones = ImmutableArray.CreateBuilder<ZoneState>(zoneCount);
        int previousZoneId = int.MinValue;

        for (int i = 0; i < zoneCount; i++)
        {
            int zoneIdValue = reader.ReadInt32();
            if (zoneIdValue <= previousZoneId)
            {
                throw new InvalidDataException("Zones are not in strictly ascending ZoneId order.");
            }

            previousZoneId = zoneIdValue;

            TileMap map = ReadMap(reader);
            ZoneEntities entities = ReadEntities(reader);

            zones.Add(new ZoneState(new ZoneId(zoneIdValue), map, entities));
        }

        ImmutableArray<LootEntityState> lootEntities = version >= 2
            ? ReadLootEntities(reader)
            : ImmutableArray<LootEntityState>.Empty;
        ImmutableArray<PlayerInventoryState> playerInventories = version >= 3
            ? ReadPlayerInventories(reader)
            : ImmutableArray<PlayerInventoryState>.Empty;
        ImmutableArray<PlayerWalletState> playerWallets = version >= 4
            ? ReadPlayerWallets(reader)
            : ImmutableArray<PlayerWalletState>.Empty;
        ImmutableArray<VendorDefinition> vendors = version >= 4
            ? ReadVendors(reader)
            : ImmutableArray<VendorDefinition>.Empty;
        ImmutableArray<VendorTransactionAuditEntry> vendorAudit = version >= 4
            ? ReadVendorAudit(reader)
            : ImmutableArray<VendorTransactionAuditEntry>.Empty;

        return new RawSnapshotPayload(
            Version: version,
            Tick: tick,
            Zones: zones.MoveToImmutable(),
            LootEntities: lootEntities,
            PlayerInventories: playerInventories,
            PlayerWallets: playerWallets,
            Vendors: vendors,
            VendorAudit: vendorAudit);
    }

    private static V4SnapshotPayload MigrateToV4(RawSnapshotPayload payload)
    {
        if (payload.Version == CurrentVersion)
        {
            return new V4SnapshotPayload(
                Tick: payload.Tick,
                Zones: payload.Zones,
                LootEntities: payload.LootEntities,
                PlayerInventories: payload.PlayerInventories,
                PlayerWallets: payload.PlayerWallets,
                Vendors: payload.Vendors,
                VendorAudit: payload.VendorAudit);
        }

        return new V4SnapshotPayload(
            Tick: payload.Tick,
            Zones: payload.Zones.OrderBy(z => z.Id.Value).ToImmutableArray(),
            LootEntities: payload.LootEntities.OrderBy(l => l.Id.Value).ToImmutableArray(),
            PlayerInventories: payload.PlayerInventories.OrderBy(i => i.EntityId.Value).ToImmutableArray(),
            PlayerWallets: ImmutableArray<PlayerWalletState>.Empty,
            Vendors: ImmutableArray<VendorDefinition>.Empty,
            VendorAudit: ImmutableArray<VendorTransactionAuditEntry>.Empty);
    }

    private static WorldState LoadV4(V4SnapshotPayload payload)
    {
        ImmutableArray<EntityLocation> locations = BuildEntityLocations(payload.Zones);
        WorldState loaded = new(
            payload.Tick,
            payload.Zones,
            locations,
            payload.LootEntities,
            payload.PlayerInventories,
            PlayerWallets: payload.PlayerWallets,
            VendorTransactionAuditLog: payload.VendorAudit,
            Vendors: payload.Vendors);

        CoreInvariants.Validate(loaded, payload.Tick);
        return loaded;
    }

    private static void WriteMap(BinaryWriter writer, TileMap map)
    {
        if (map.Width < 0 || map.Height < 0)
        {
            throw new InvalidDataException("Map dimensions cannot be negative.");
        }

        int expectedTileCount = checked(map.Width * map.Height);
        if (map.Tiles.Length != expectedTileCount)
        {
            throw new InvalidDataException("Map tile count does not match Width*Height.");
        }

        writer.Write(map.Width);
        writer.Write(map.Height);
        writer.Write(map.Tiles.Length);

        for (int i = 0; i < map.Tiles.Length; i++)
        {
            writer.Write((byte)map.Tiles[i]);
        }
    }

    private static TileMap ReadMap(BinaryReader reader)
    {
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();

        ValidateCount(width, MaxMapDimension, nameof(width));
        ValidateCount(height, MaxMapDimension, nameof(height));

        int expectedTileCount = checked(width * height);
        int tileCount = reader.ReadInt32();
        if (tileCount != expectedTileCount)
        {
            throw new InvalidDataException("TileCount does not match Width*Height.");
        }

        ImmutableArray<TileKind>.Builder tiles = ImmutableArray.CreateBuilder<TileKind>(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            byte raw = reader.ReadByte();
            if (!Enum.IsDefined(typeof(TileKind), raw))
            {
                throw new InvalidDataException($"Unknown TileKind value '{raw}' at index {i}.");
            }

            tiles.Add((TileKind)raw);
        }

        return new TileMap(width, height, tiles.MoveToImmutable());
    }

    private static void WriteEntities(BinaryWriter writer, ZoneEntities entities)
    {
        int count = entities.AliveIds.Length;

        EnsureEqualLength(count, entities.Masks.Length, nameof(entities.Masks));
        EnsureEqualLength(count, entities.Kinds.Length, nameof(entities.Kinds));
        EnsureEqualLength(count, entities.Positions.Length, nameof(entities.Positions));
        EnsureEqualLength(count, entities.Health.Length, nameof(entities.Health));
        EnsureEqualLength(count, entities.Combat.Length, nameof(entities.Combat));
        EnsureEqualLength(count, entities.Ai.Length, nameof(entities.Ai));

        EnsureSortedEntityIds(entities.AliveIds);

        writer.Write(count);

        for (int i = 0; i < count; i++)
        {
            writer.Write(entities.AliveIds[i].Value);

            ComponentMask mask = entities.Masks[i];
            writer.Write(mask.Bits);

            EntityKind kind = entities.Kinds[i];
            writer.Write((byte)kind);

            if (mask.Has(ComponentMask.PositionBit))
            {
                PositionComponent position = entities.Positions[i];
                writer.Write(position.Pos.X.Raw);
                writer.Write(position.Pos.Y.Raw);
                writer.Write(position.Vel.X.Raw);
                writer.Write(position.Vel.Y.Raw);
            }

            if (mask.Has(ComponentMask.HealthBit))
            {
                HealthComponent health = entities.Health[i];
                writer.Write(health.MaxHp);
                writer.Write(health.Hp);
                writer.Write(health.IsAlive);
            }

            if (mask.Has(ComponentMask.CombatBit))
            {
                CombatComponent combat = entities.Combat[i];
                writer.Write(combat.Range.Raw);
                writer.Write(combat.Damage);
                writer.Write(combat.CooldownTicks);
                writer.Write(combat.LastAttackTick);
            }

            if (mask.Has(ComponentMask.AiBit))
            {
                AiComponent ai = entities.Ai[i];
                writer.Write(ai.NextWanderChangeTick);
                writer.Write(ai.WanderX);
                writer.Write(ai.WanderY);
            }
        }
    }

    private static ZoneEntities ReadEntities(BinaryReader reader)
    {
        int entityCount = reader.ReadInt32();
        ValidateCount(entityCount, MaxEntityCountPerZone, nameof(entityCount));

        ImmutableArray<EntityId>.Builder ids = ImmutableArray.CreateBuilder<EntityId>(entityCount);
        ImmutableArray<ComponentMask>.Builder masks = ImmutableArray.CreateBuilder<ComponentMask>(entityCount);
        ImmutableArray<EntityKind>.Builder kinds = ImmutableArray.CreateBuilder<EntityKind>(entityCount);
        ImmutableArray<PositionComponent>.Builder positions = ImmutableArray.CreateBuilder<PositionComponent>(entityCount);
        ImmutableArray<HealthComponent>.Builder health = ImmutableArray.CreateBuilder<HealthComponent>(entityCount);
        ImmutableArray<CombatComponent>.Builder combat = ImmutableArray.CreateBuilder<CombatComponent>(entityCount);
        ImmutableArray<AiComponent>.Builder ai = ImmutableArray.CreateBuilder<AiComponent>(entityCount);

        int previousEntityId = int.MinValue;

        for (int i = 0; i < entityCount; i++)
        {
            int entityIdValue = reader.ReadInt32();
            if (entityIdValue <= previousEntityId)
            {
                throw new InvalidDataException("Entities are not in strictly ascending EntityId order.");
            }

            previousEntityId = entityIdValue;

            uint bits = reader.ReadUInt32();
            ComponentMask mask = new(bits);

            byte kindRaw = reader.ReadByte();
            if (!Enum.IsDefined(typeof(EntityKind), kindRaw))
            {
                throw new InvalidDataException($"Unknown EntityKind value '{kindRaw}'.");
            }

            PositionComponent position = default;
            HealthComponent entityHealth = default;
            CombatComponent entityCombat = default;
            AiComponent entityAi = default;

            if (mask.Has(ComponentMask.PositionBit))
            {
                position = new PositionComponent(
                    Pos: new Vec2Fix(new Fix32(reader.ReadInt32()), new Fix32(reader.ReadInt32())),
                    Vel: new Vec2Fix(new Fix32(reader.ReadInt32()), new Fix32(reader.ReadInt32())));
            }

            if (mask.Has(ComponentMask.HealthBit))
            {
                entityHealth = new HealthComponent(
                    MaxHp: reader.ReadInt32(),
                    Hp: reader.ReadInt32(),
                    IsAlive: reader.ReadBoolean());
            }

            if (mask.Has(ComponentMask.CombatBit))
            {
                entityCombat = new CombatComponent(
                    Range: new Fix32(reader.ReadInt32()),
                    Damage: reader.ReadInt32(),
                    CooldownTicks: reader.ReadInt32(),
                    LastAttackTick: reader.ReadInt32());
            }

            if (mask.Has(ComponentMask.AiBit))
            {
                entityAi = new AiComponent(
                    NextWanderChangeTick: reader.ReadInt32(),
                    WanderX: reader.ReadSByte(),
                    WanderY: reader.ReadSByte());
            }

            ids.Add(new EntityId(entityIdValue));
            masks.Add(mask);
            kinds.Add((EntityKind)kindRaw);
            positions.Add(position);
            health.Add(entityHealth);
            combat.Add(entityCombat);
            ai.Add(entityAi);
        }

        return new ZoneEntities(
            ids.MoveToImmutable(),
            masks.MoveToImmutable(),
            kinds.MoveToImmutable(),
            positions.MoveToImmutable(),
            health.MoveToImmutable(),
            combat.MoveToImmutable(),
            ai.MoveToImmutable());
    }

    private static void WriteLootEntities(BinaryWriter writer, ImmutableArray<LootEntityState> lootEntities)
    {
        ImmutableArray<LootEntityState> ordered = lootEntities
            .OrderBy(l => l.Id.Value)
            .ToImmutableArray();

        writer.Write(ordered.Length);
        foreach (LootEntityState loot in ordered)
        {
            writer.Write(loot.Id.Value);
            writer.Write(loot.ZoneId.Value);
            writer.Write(loot.Pos.X.Raw);
            writer.Write(loot.Pos.Y.Raw);

            ImmutableArray<ItemStack> orderedItems = loot.Items
                .OrderBy(i => i.ItemId, StringComparer.Ordinal)
                .ThenBy(i => i.Quantity)
                .ToImmutableArray();

            writer.Write(orderedItems.Length);
            foreach (ItemStack item in orderedItems)
            {
                writer.Write(item.ItemId);
                writer.Write(item.Quantity);
            }
        }
    }

    private static ImmutableArray<LootEntityState> ReadLootEntities(BinaryReader reader)
    {
        int lootCount = reader.ReadInt32();
        ValidateCount(lootCount, MaxLootEntityCount, nameof(lootCount));

        ImmutableArray<LootEntityState>.Builder loot = ImmutableArray.CreateBuilder<LootEntityState>(lootCount);
        int previousLootId = int.MinValue;

        for (int i = 0; i < lootCount; i++)
        {
            int lootEntityId = reader.ReadInt32();
            if (lootEntityId <= previousLootId)
            {
                throw new InvalidDataException("Loot entities are not in strictly ascending EntityId order.");
            }

            previousLootId = lootEntityId;

            ZoneId zoneId = new(reader.ReadInt32());
            Vec2Fix pos = new(new Fix32(reader.ReadInt32()), new Fix32(reader.ReadInt32()));
            int itemCount = reader.ReadInt32();
            ValidateCount(itemCount, MaxLootItemsPerEntity, nameof(itemCount));

            ImmutableArray<ItemStack>.Builder items = ImmutableArray.CreateBuilder<ItemStack>(itemCount);
            string previousItemId = string.Empty;
            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                string itemId = reader.ReadString();
                int quantity = reader.ReadInt32();
                if (quantity <= 0)
                {
                    throw new InvalidDataException($"Loot item quantity must be positive: {quantity}.");
                }

                if (string.CompareOrdinal(itemId, previousItemId) < 0)
                {
                    throw new InvalidDataException("Loot items are not in ascending ItemId order.");
                }

                previousItemId = itemId;
                items.Add(new ItemStack(itemId, quantity));
            }

            loot.Add(new LootEntityState(new EntityId(lootEntityId), zoneId, pos, items.MoveToImmutable()));
        }

        return loot.MoveToImmutable();
    }


    private static void WritePlayerInventories(BinaryWriter writer, ImmutableArray<PlayerInventoryState> playerInventories)
    {
        ImmutableArray<PlayerInventoryState> ordered = playerInventories
            .OrderBy(i => i.EntityId.Value)
            .ToImmutableArray();

        writer.Write(ordered.Length);
        foreach (PlayerInventoryState playerInventory in ordered)
        {
            writer.Write(playerInventory.EntityId.Value);
            writer.Write(playerInventory.Inventory.Capacity);
            writer.Write(playerInventory.Inventory.StackLimit);
            writer.Write(playerInventory.Inventory.Slots.Length);

            for (int i = 0; i < playerInventory.Inventory.Slots.Length; i++)
            {
                InventorySlot slot = playerInventory.Inventory.Slots[i];
                writer.Write(slot.ItemId ?? string.Empty);
                writer.Write(slot.Quantity);
            }
        }
    }

    private static ImmutableArray<PlayerInventoryState> ReadPlayerInventories(BinaryReader reader)
    {
        int inventoryCount = reader.ReadInt32();
        ValidateCount(inventoryCount, MaxInventoryCount, nameof(inventoryCount));

        ImmutableArray<PlayerInventoryState>.Builder inventories = ImmutableArray.CreateBuilder<PlayerInventoryState>(inventoryCount);
        int previousEntityId = int.MinValue;

        for (int i = 0; i < inventoryCount; i++)
        {
            int entityId = reader.ReadInt32();
            if (entityId <= previousEntityId)
            {
                throw new InvalidDataException("Player inventories are not in strictly ascending EntityId order.");
            }

            previousEntityId = entityId;
            int capacity = reader.ReadInt32();
            int stackLimit = reader.ReadInt32();
            int slotCount = reader.ReadInt32();
            ValidateCount(capacity, MaxInventoryCapacity, nameof(capacity));
            ValidateCount(slotCount, MaxInventoryCapacity, nameof(slotCount));

            if (capacity != slotCount)
            {
                throw new InvalidDataException("Inventory slot count must equal capacity.");
            }

            ImmutableArray<InventorySlot>.Builder slots = ImmutableArray.CreateBuilder<InventorySlot>(slotCount);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                string itemId = reader.ReadString();
                int quantity = reader.ReadInt32();
                if (quantity < 0)
                {
                    throw new InvalidDataException($"Inventory quantity cannot be negative: {quantity}.");
                }

                if (quantity == 0 && itemId.Length != 0)
                {
                    throw new InvalidDataException("Empty inventory slots must have empty item ids.");
                }

                if (quantity > 0 && itemId.Length == 0)
                {
                    throw new InvalidDataException("Non-empty inventory slots require an item id.");
                }

                slots.Add(new InventorySlot(itemId, quantity));
            }

            inventories.Add(new PlayerInventoryState(new EntityId(entityId), new InventoryComponent(capacity, stackLimit, slots.MoveToImmutable())));
        }

        return inventories.MoveToImmutable();
    }

    private static ImmutableArray<EntityLocation> BuildEntityLocations(ImmutableArray<ZoneState> zones)
    {
        ImmutableArray<EntityLocation>.Builder builder = ImmutableArray.CreateBuilder<EntityLocation>();

        foreach (ZoneState zone in zones)
        {
            foreach (EntityId entityId in zone.EntitiesData.AliveIds)
            {
                builder.Add(new EntityLocation(entityId, zone.Id));
            }
        }

        return builder
            .ToImmutable()
            .OrderBy(location => location.Id.Value)
            .ToImmutableArray();
    }

    private static void ValidateCount(int value, int maxValue, string name)
    {
        if (value < 0 || value > maxValue)
        {
            throw new InvalidDataException($"Invalid {name}: {value}.");
        }
    }

    private static void EnsureEqualLength(int expected, int actual, string name)
    {
        if (actual != expected)
        {
            throw new InvalidDataException($"Mismatched component array length for {name}. Expected {expected}, got {actual}.");
        }
    }

    private static void EnsureSortedZones(ImmutableArray<ZoneState> zones)
    {
        int previousZoneId = int.MinValue;

        for (int i = 0; i < zones.Length; i++)
        {
            int current = zones[i].Id.Value;
            if (current <= previousZoneId)
            {
                throw new InvalidDataException("WorldState zones must be sorted by ascending ZoneId before save.");
            }

            previousZoneId = current;
        }
    }

    private static void EnsureSortedEntityIds(ImmutableArray<EntityId> ids)
    {
        int previousEntityId = int.MinValue;

        for (int i = 0; i < ids.Length; i++)
        {
            int current = ids[i].Value;
            if (current <= previousEntityId)
            {
                throw new InvalidDataException("Zone entities must be sorted by ascending EntityId before save.");
            }

            previousEntityId = current;
        }
    }

    private static void WritePlayerWallets(BinaryWriter writer, ImmutableArray<PlayerWalletState> playerWallets)
    {
        ImmutableArray<PlayerWalletState> ordered = playerWallets.OrderBy(w => w.EntityId.Value).ToImmutableArray();
        writer.Write(ordered.Length);
        foreach (PlayerWalletState wallet in ordered)
        {
            writer.Write(wallet.EntityId.Value);
            writer.Write(wallet.Gold);
        }
    }

    private static ImmutableArray<PlayerWalletState> ReadPlayerWallets(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, MaxWalletCount, nameof(count));
        ImmutableArray<PlayerWalletState>.Builder wallets = ImmutableArray.CreateBuilder<PlayerWalletState>(count);
        int prev = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            int entityId = reader.ReadInt32();
            long gold = reader.ReadInt64();
            if (entityId <= prev)
            {
                throw new InvalidDataException("Player wallets are not in strictly ascending EntityId order.");
            }

            if (gold < 0)
            {
                throw new InvalidDataException("Player wallet gold cannot be negative.");
            }

            prev = entityId;
            wallets.Add(new PlayerWalletState(new EntityId(entityId), gold));
        }

        return wallets.MoveToImmutable();
    }

    private static void WriteVendors(BinaryWriter writer, ImmutableArray<VendorDefinition> vendors)
    {
        ImmutableArray<VendorDefinition> ordered = vendors
            .OrderBy(v => v.ZoneId.Value)
            .ThenBy(v => v.VendorId, StringComparer.Ordinal)
            .ToImmutableArray();
        writer.Write(ordered.Length);
        foreach (VendorDefinition vendor in ordered)
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
    }

    private static ImmutableArray<VendorDefinition> ReadVendors(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, MaxVendorCount, nameof(count));
        ImmutableArray<VendorDefinition>.Builder vendors = ImmutableArray.CreateBuilder<VendorDefinition>(count);
        int lastZone = int.MinValue;
        string lastVendorId = string.Empty;
        for (int i = 0; i < count; i++)
        {
            int zoneId = reader.ReadInt32();
            string vendorId = reader.ReadString();
            if (zoneId < lastZone || (zoneId == lastZone && string.CompareOrdinal(vendorId, lastVendorId) <= 0))
            {
                throw new InvalidDataException("Vendors are not in canonical order.");
            }

            int offerCount = reader.ReadInt32();
            ValidateCount(offerCount, MaxVendorOffers, nameof(offerCount));
            ImmutableArray<VendorOfferDefinition>.Builder offers = ImmutableArray.CreateBuilder<VendorOfferDefinition>(offerCount);
            string lastItemId = string.Empty;
            for (int offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                string itemId = reader.ReadString();
                long buyPrice = reader.ReadInt64();
                long sellPrice = reader.ReadInt64();
                int maxPerTxRaw = reader.ReadInt32();
                if (string.CompareOrdinal(itemId, lastItemId) < 0)
                {
                    throw new InvalidDataException("Vendor offers are not in ascending ItemId order.");
                }

                lastItemId = itemId;
                offers.Add(new VendorOfferDefinition(itemId, buyPrice, sellPrice, maxPerTxRaw < 0 ? null : maxPerTxRaw));
            }

            vendors.Add(new VendorDefinition(vendorId, new ZoneId(zoneId), offers.MoveToImmutable()));
            lastZone = zoneId;
            lastVendorId = vendorId;
        }

        return vendors.MoveToImmutable();
    }

    private static void WriteVendorAudit(BinaryWriter writer, ImmutableArray<VendorTransactionAuditEntry> entries)
    {
        ImmutableArray<VendorTransactionAuditEntry> ordered = entries
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.PlayerEntityId.Value)
            .ThenBy(e => e.ZoneId.Value)
            .ThenBy(e => e.VendorId, StringComparer.Ordinal)
            .ThenBy(e => (int)e.Action)
            .ThenBy(e => e.ItemId, StringComparer.Ordinal)
            .ThenBy(e => e.Quantity)
            .ToImmutableArray();
        writer.Write(ordered.Length);
        foreach (VendorTransactionAuditEntry entry in ordered)
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
    }

    private static ImmutableArray<VendorTransactionAuditEntry> ReadVendorAudit(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, MaxVendorAuditCount, nameof(count));
        ImmutableArray<VendorTransactionAuditEntry>.Builder entries = ImmutableArray.CreateBuilder<VendorTransactionAuditEntry>(count);
        int lastTick = int.MinValue;
        int lastPlayer = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            int tick = reader.ReadInt32();
            EntityId playerId = new(reader.ReadInt32());
            ZoneId zoneId = new(reader.ReadInt32());
            string vendorId = reader.ReadString();
            VendorAction action = (VendorAction)reader.ReadByte();
            string itemId = reader.ReadString();
            int quantity = reader.ReadInt32();
            long unitPrice = reader.ReadInt64();
            long goldBefore = reader.ReadInt64();
            long goldAfter = reader.ReadInt64();

            if (tick < lastTick || (tick == lastTick && playerId.Value < lastPlayer))
            {
                throw new InvalidDataException("Vendor audit entries are not in canonical order.");
            }

            lastTick = tick;
            lastPlayer = playerId.Value;
            entries.Add(new VendorTransactionAuditEntry(tick, playerId, zoneId, vendorId, action, itemId, quantity, unitPrice, goldBefore, goldAfter));
        }

        return entries.MoveToImmutable();
    }
}
