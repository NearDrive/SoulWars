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
        ImmutableArray<VendorTransactionAuditEntry> VendorAudit,
        ImmutableArray<CombatEvent> CombatEvents,
        ImmutableArray<StatusEvent> StatusEvents,
        PartyRegistry PartyRegistry,
        PartyInviteRegistry PartyInviteRegistry,
        InstanceRegistry InstanceRegistry,
        EncounterRegistry EncounterRegistry);

    private sealed record V7SnapshotPayload(
        int Tick,
        ImmutableArray<ZoneState> Zones,
        ImmutableArray<LootEntityState> LootEntities,
        ImmutableArray<PlayerInventoryState> PlayerInventories,
        ImmutableArray<PlayerWalletState> PlayerWallets,
        ImmutableArray<VendorDefinition> Vendors,
        ImmutableArray<VendorTransactionAuditEntry> VendorAudit,
        ImmutableArray<CombatEvent> CombatEvents,
        ImmutableArray<StatusEvent> StatusEvents,
        PartyRegistry PartyRegistry,
        PartyInviteRegistry PartyInviteRegistry,
        InstanceRegistry InstanceRegistry,
        EncounterRegistry EncounterRegistry);

    private static readonly byte[] Magic = "SWWORLD\0"u8.ToArray();
    private const int CurrentVersion = 9;
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
    private const int MaxCombatEventCount = 10_000_000;
    private const int MaxStatusEventCount = 10_000_000;
    private const int MaxPartyCount = 2_000_000;
    private const int MaxPartyMembers = 2_000_000;
    private const int MaxPartyInvites = 2_000_000;
    private const int MaxInstances = 2_000_000;
    private const int MaxEncounters = 500_000;
    private const int MaxEncounterPhases = 256;
    private const int MaxEncounterTriggers = 2048;
    private const int MaxEncounterActions = 4096;

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
        WriteCombatEvents(writer, world.CombatEvents.IsDefault ? ImmutableArray<CombatEvent>.Empty : world.CombatEvents);
        WriteStatusEvents(writer, world.StatusEvents.IsDefault ? ImmutableArray<StatusEvent>.Empty : world.StatusEvents);
        WritePartyRegistry(writer, world.PartyRegistryOrEmpty);
        WritePartyInviteRegistry(writer, world.PartyInviteRegistryOrEmpty);
        WriteInstanceRegistry(writer, world.InstanceRegistryOrEmpty);
        WriteEncounterRegistry(writer, world.EncounterRegistryOrEmpty);
    }

    public static WorldState Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            RawSnapshotPayload raw = LoadRaw(reader);
            V7SnapshotPayload migrated = MigrateToV7(raw);
            return LoadV7(migrated);
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
        if (version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or CurrentVersion))
        {
            throw new InvalidDataException($"Unsupported world-state version '{version}'.");
        }

        if (version != CurrentVersion)
        {
            return ParseSnapshotPayload(reader, version, readDefenseForV4: false);
        }

        Stream stream = reader.BaseStream;
        if (!stream.CanSeek)
        {
            return ParseSnapshotPayload(reader, version, readDefenseForV4: true);
        }

        long payloadStart = stream.Position;
        try
        {
            return ParseSnapshotPayload(reader, version, readDefenseForV4: true);
        }
        catch (InvalidDataException)
        {
            stream.Position = payloadStart;
            return ParseSnapshotPayload(reader, version, readDefenseForV4: false);
        }
        catch (EndOfStreamException)
        {
            stream.Position = payloadStart;
            return ParseSnapshotPayload(reader, version, readDefenseForV4: false);
        }
    }

    private static RawSnapshotPayload ParseSnapshotPayload(BinaryReader reader, int version, bool readDefenseForV4)
    {
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
            ZoneEntities entities = ReadEntities(reader, version, readDefenseForV4);

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
        ImmutableArray<CombatEvent> combatEvents = version >= 4 && HasRemainingData(reader)
            ? ReadCombatEvents(reader)
            : ImmutableArray<CombatEvent>.Empty;
        ImmutableArray<StatusEvent> statusEvents = version >= 4 && HasRemainingData(reader)
            ? ReadStatusEvents(reader)
            : ImmutableArray<StatusEvent>.Empty;
        PartyRegistry partyRegistry = version >= 5 && HasRemainingData(reader)
            ? ReadPartyRegistry(reader)
            : PartyRegistry.Empty;
        PartyInviteRegistry partyInviteRegistry = version >= 6 && HasRemainingData(reader)
            ? ReadPartyInviteRegistry(reader)
            : PartyInviteRegistry.Empty;
        InstanceRegistry instanceRegistry = version >= 7 && HasRemainingData(reader)
            ? ReadInstanceRegistry(reader)
            : InstanceRegistry.Empty;
        EncounterRegistry encounterRegistry = version >= 8 && HasRemainingData(reader)
            ? ReadEncounterRegistry(reader)
            : EncounterRegistry.Empty;

        return new RawSnapshotPayload(
            Version: version,
            Tick: tick,
            Zones: zones.MoveToImmutable(),
            LootEntities: lootEntities,
            PlayerInventories: playerInventories,
            PlayerWallets: playerWallets,
            Vendors: vendors,
            VendorAudit: vendorAudit,
            CombatEvents: combatEvents,
            StatusEvents: statusEvents,
            PartyRegistry: partyRegistry,
            PartyInviteRegistry: partyInviteRegistry,
            InstanceRegistry: instanceRegistry,
            EncounterRegistry: encounterRegistry);
    }

    private static V7SnapshotPayload MigrateToV7(RawSnapshotPayload payload)
    {
        if (payload.Version == CurrentVersion)
        {
            return new V7SnapshotPayload(
                Tick: payload.Tick,
                Zones: payload.Zones,
                LootEntities: payload.LootEntities,
                PlayerInventories: payload.PlayerInventories,
                PlayerWallets: payload.PlayerWallets,
                Vendors: payload.Vendors,
                VendorAudit: payload.VendorAudit,
                CombatEvents: payload.CombatEvents,
                StatusEvents: payload.StatusEvents,
            PartyRegistry: payload.PartyRegistry,
            PartyInviteRegistry: payload.PartyInviteRegistry,
            InstanceRegistry: payload.InstanceRegistry,
            EncounterRegistry: payload.EncounterRegistry);
        }

        return new V7SnapshotPayload(
            Tick: payload.Tick,
            Zones: payload.Zones.OrderBy(z => z.Id.Value).ToImmutableArray(),
            LootEntities: payload.LootEntities.OrderBy(l => l.Id.Value).ToImmutableArray(),
            PlayerInventories: payload.PlayerInventories.OrderBy(i => i.EntityId.Value).ToImmutableArray(),
            PlayerWallets: (payload.Version >= 4 ? payload.PlayerWallets : ImmutableArray<PlayerWalletState>.Empty)
                .OrderBy(w => w.EntityId.Value)
                .ToImmutableArray(),
            Vendors: (payload.Version >= 4 ? payload.Vendors : ImmutableArray<VendorDefinition>.Empty)
                .OrderBy(v => v.ZoneId.Value)
                .ThenBy(v => v.VendorId, StringComparer.Ordinal)
                .ToImmutableArray(),
            VendorAudit: (payload.Version >= 4 ? payload.VendorAudit : ImmutableArray<VendorTransactionAuditEntry>.Empty)
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.PlayerEntityId.Value)
                .ThenBy(e => e.ZoneId.Value)
                .ThenBy(e => e.VendorId, StringComparer.Ordinal)
                .ThenBy(e => (int)e.Action)
                .ThenBy(e => e.ItemId, StringComparer.Ordinal)
                .ThenBy(e => e.Quantity)
                .ToImmutableArray(),
            CombatEvents: ImmutableArray<CombatEvent>.Empty,
            StatusEvents: ImmutableArray<StatusEvent>.Empty,
            PartyRegistry: PartyRegistry.Empty,
            PartyInviteRegistry: PartyInviteRegistry.Empty,
            InstanceRegistry: InstanceRegistry.Empty,
            EncounterRegistry: EncounterRegistry.Empty);
    }

    private static WorldState LoadV7(V7SnapshotPayload payload)
    {
        ImmutableArray<EntityLocation> locations = BuildEntityLocations(payload.Zones);
        WorldState loaded = new(
            Tick: payload.Tick,
            Zones: payload.Zones,
            EntityLocations: locations,
            PartyRegistry: payload.PartyRegistry,
            PartyInviteRegistry: payload.PartyInviteRegistry,
            InstanceRegistry: payload.InstanceRegistry,
            LootEntities: payload.LootEntities,
            PlayerInventories: payload.PlayerInventories,
            PlayerWallets: payload.PlayerWallets,
            VendorTransactionAuditLog: payload.VendorAudit,
            Vendors: payload.Vendors,
            CombatEvents: payload.CombatEvents,
            StatusEvents: payload.StatusEvents,
            EncounterRegistry: payload.EncounterRegistry);

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
        EnsureEqualLength(count, entities.MoveIntents.Length, nameof(entities.MoveIntents));
        EnsureEqualLength(count, entities.NavAgents.Length, nameof(entities.NavAgents));
        int threatCount = entities.Threat.IsDefault ? 0 : entities.Threat.Length;
        if (threatCount != 0)
        {
            EnsureEqualLength(count, threatCount, nameof(entities.Threat));
        }

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
                writer.Write(combat.Defense);
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

            if (mask.Has(ComponentMask.MoveIntentBit))
            {
                MoveIntentComponent moveIntent = entities.MoveIntents[i];
                writer.Write((byte)moveIntent.Type);
                writer.Write(moveIntent.TargetEntityId.Value);
                writer.Write(moveIntent.TargetX.Raw);
                writer.Write(moveIntent.TargetY.Raw);
                writer.Write(moveIntent.RepathEveryTicks);
                writer.Write(moveIntent.NextRepathTick);
                writer.Write(moveIntent.PathLen);
                writer.Write(moveIntent.PathIndex);
                ImmutableArray<TileCoord> path = moveIntent.Path.IsDefault ? ImmutableArray<TileCoord>.Empty : moveIntent.Path;
                writer.Write(path.Length);
                for (int pathIndex = 0; pathIndex < path.Length; pathIndex++)
                {
                    writer.Write(path[pathIndex].X);
                    writer.Write(path[pathIndex].Y);
                }
            }

            if (mask.Has(ComponentMask.NavAgentBit))
            {
                NavAgentComponent navAgent = entities.NavAgents[i];
                writer.Write(navAgent.ArrivalEpsilon.Raw);
            }

            if (mask.Has(ComponentMask.ThreatBit))
            {
                ThreatComponent threat = threatCount == 0 ? ThreatComponent.Empty : entities.Threat[i];
                ImmutableArray<ThreatEntry> entries = threat.OrderedEntries();
                writer.Write(entries.Length);
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    ThreatEntry entry = entries[entryIndex];
                    writer.Write(entry.SourceEntityId.Value);
                    writer.Write(entry.Threat);
                    writer.Write(entry.LastTick);
                }
            }

        }
    }

    private static ZoneEntities ReadEntities(BinaryReader reader, int snapshotVersion, bool readDefenseForV4)
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
        ImmutableArray<MoveIntentComponent>.Builder moveIntents = ImmutableArray.CreateBuilder<MoveIntentComponent>(entityCount);
        ImmutableArray<NavAgentComponent>.Builder navAgents = ImmutableArray.CreateBuilder<NavAgentComponent>(entityCount);
        ImmutableArray<ThreatComponent>.Builder threat = ImmutableArray.CreateBuilder<ThreatComponent>(entityCount);

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
            MoveIntentComponent moveIntent = MoveIntentComponent.Default;
            NavAgentComponent navAgent = NavAgentComponent.Default;
            ThreatComponent entityThreat = ThreatComponent.Empty;

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
                Fix32 range = new(reader.ReadInt32());
                int damage = reader.ReadInt32();
                int defense = (snapshotVersion >= 5 || (snapshotVersion == CurrentVersion && readDefenseForV4)) ? reader.ReadInt32() : 0;
                int magicResist = defense;
                int cooldownTicks = reader.ReadInt32();
                int lastAttackTick = reader.ReadInt32();

                entityCombat = new CombatComponent(
                    Range: range,
                    Damage: damage,
                    Defense: defense,
                    MagicResist: magicResist,
                    CooldownTicks: cooldownTicks,
                    LastAttackTick: lastAttackTick);
            }

            if (mask.Has(ComponentMask.AiBit))
            {
                entityAi = new AiComponent(
                    NextWanderChangeTick: reader.ReadInt32(),
                    WanderX: reader.ReadSByte(),
                    WanderY: reader.ReadSByte());
            }

            if (mask.Has(ComponentMask.MoveIntentBit) && snapshotVersion >= 9)
            {
                byte moveIntentTypeRaw = reader.ReadByte();
                if (!Enum.IsDefined(typeof(MoveIntentType), moveIntentTypeRaw))
                {
                    throw new InvalidDataException($"Unknown MoveIntentType value '{moveIntentTypeRaw}'.");
                }

                EntityId targetEntityId = new(reader.ReadInt32());
                Fix32 targetX = new(reader.ReadInt32());
                Fix32 targetY = new(reader.ReadInt32());
                int repathEveryTicks = reader.ReadInt32();
                int nextRepathTick = reader.ReadInt32();
                int pathLen = reader.ReadInt32();
                int pathIndex = reader.ReadInt32();
                int storedPathCount = reader.ReadInt32();
                ValidateCount(storedPathCount, MaxEntityCountPerZone, nameof(storedPathCount));

                ImmutableArray<TileCoord>.Builder path = ImmutableArray.CreateBuilder<TileCoord>(storedPathCount);
                for (int pathEntry = 0; pathEntry < storedPathCount; pathEntry++)
                {
                    path.Add(new TileCoord(reader.ReadInt32(), reader.ReadInt32()));
                }

                moveIntent = new MoveIntentComponent(
                    (MoveIntentType)moveIntentTypeRaw,
                    targetEntityId,
                    targetX,
                    targetY,
                    repathEveryTicks,
                    nextRepathTick,
                    path.MoveToImmutable(),
                    pathLen,
                    pathIndex);
            }

            if (mask.Has(ComponentMask.NavAgentBit) && snapshotVersion >= 9)
            {
                navAgent = new NavAgentComponent(new Fix32(reader.ReadInt32()));
            }

            if (mask.Has(ComponentMask.ThreatBit) && snapshotVersion >= 9)
            {
                int entryCount = reader.ReadInt32();
                ValidateCount(entryCount, MaxEntityCountPerZone, nameof(entryCount));
                ImmutableArray<ThreatEntry>.Builder entries = ImmutableArray.CreateBuilder<ThreatEntry>(entryCount);
                int previousSource = int.MinValue;
                for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    int sourceId = reader.ReadInt32();
                    int threatValue = reader.ReadInt32();
                    int lastTick = reader.ReadInt32();
                    if (sourceId <= previousSource)
                    {
                        throw new InvalidDataException("Threat entries are not in ascending SourceEntityId order.");
                    }

                    if (threatValue < 0)
                    {
                        throw new InvalidDataException("Threat entry value cannot be negative.");
                    }

                    previousSource = sourceId;
                    entries.Add(new ThreatEntry(new EntityId(sourceId), threatValue, lastTick));
                }

                entityThreat = new ThreatComponent(entries.MoveToImmutable());
            }

            ids.Add(new EntityId(entityIdValue));
            masks.Add(mask);
            kinds.Add((EntityKind)kindRaw);
            positions.Add(position);
            health.Add(entityHealth);
            combat.Add(entityCombat);
            ai.Add(entityAi);
            moveIntents.Add(moveIntent);
            navAgents.Add(navAgent);
            threat.Add(entityThreat);
        }

        return new ZoneEntities(
            ids.MoveToImmutable(),
            masks.MoveToImmutable(),
            kinds.MoveToImmutable(),
            positions.MoveToImmutable(),
            health.MoveToImmutable(),
            combat.MoveToImmutable(),
            ai.MoveToImmutable(),
            moveIntents.MoveToImmutable(),
            navAgents.MoveToImmutable(),
            ImmutableArray<StatusEffectsComponent>.Empty,
            ImmutableArray<SkillCooldownsComponent>.Empty,
            threat.MoveToImmutable());
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


    private static void WriteCombatEvents(BinaryWriter writer, ImmutableArray<CombatEvent> combatEvents)
    {
        ImmutableArray<CombatEvent> ordered = combatEvents
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ToImmutableArray();

        writer.Write(ordered.Length);
        foreach (CombatEvent evt in ordered)
        {
            writer.Write(evt.Tick);
            writer.Write(evt.SourceId.Value);
            writer.Write(evt.TargetId.Value);
            writer.Write(evt.SkillId.Value);
            writer.Write((byte)evt.Type);
            writer.Write(evt.Amount);
        }
    }

    private static ImmutableArray<CombatEvent> ReadCombatEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, MaxCombatEventCount, nameof(count));

        ImmutableArray<CombatEvent>.Builder events = ImmutableArray.CreateBuilder<CombatEvent>(count);
        for (int i = 0; i < count; i++)
        {
            int tick = reader.ReadInt32();
            int sourceId = reader.ReadInt32();
            int targetId = reader.ReadInt32();
            int skillId = reader.ReadInt32();
            byte typeRaw = reader.ReadByte();
            int amount = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(CombatEventType), typeRaw))
            {
                throw new InvalidDataException($"Unknown CombatEventType value '{typeRaw}'.");
            }

            events.Add(new CombatEvent(tick, new EntityId(sourceId), new EntityId(targetId), new SkillId(skillId), (CombatEventType)typeRaw, amount));
        }

        return events
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.SkillId.Value)
            .ToImmutableArray();
    }

    private static void WriteStatusEvents(BinaryWriter writer, ImmutableArray<StatusEvent> statusEvents)
    {
        ImmutableArray<StatusEvent> ordered = statusEvents
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.EffectType)
            .ToImmutableArray();

        writer.Write(ordered.Length);
        foreach (StatusEvent evt in ordered)
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

    private static ImmutableArray<StatusEvent> ReadStatusEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, MaxStatusEventCount, nameof(count));

        ImmutableArray<StatusEvent>.Builder events = ImmutableArray.CreateBuilder<StatusEvent>(count);
        for (int i = 0; i < count; i++)
        {
            int tick = reader.ReadInt32();
            int sourceId = reader.ReadInt32();
            int targetId = reader.ReadInt32();
            byte typeRaw = reader.ReadByte();
            byte effectTypeRaw = reader.ReadByte();
            int expiresAtTick = reader.ReadInt32();
            int magnitudeRaw = reader.ReadInt32();

            events.Add(new StatusEvent(tick, new EntityId(sourceId), new EntityId(targetId), (StatusEventType)typeRaw, (StatusEffectType)effectTypeRaw, expiresAtTick, magnitudeRaw));
        }

        return events
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.SourceId.Value)
            .ThenBy(e => e.TargetId.Value)
            .ThenBy(e => e.EffectType)
            .ToImmutableArray();
    }

    private static void WritePartyRegistry(BinaryWriter writer, PartyRegistry partyRegistry)
    {
        PartyRegistry canonical = partyRegistry.Canonicalize();
        writer.Write(canonical.NextPartySequence);
        writer.Write(canonical.Parties.Length);
        foreach (PartyState party in canonical.Parties)
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

    private static PartyRegistry ReadPartyRegistry(BinaryReader reader)
    {
        int nextPartySequence = reader.ReadInt32();
        if (nextPartySequence <= 0)
        {
            throw new InvalidDataException("Party next sequence must be positive.");
        }

        int partyCount = reader.ReadInt32();
        ValidateCount(partyCount, MaxPartyCount, nameof(partyCount));

        ImmutableArray<PartyState>.Builder parties = ImmutableArray.CreateBuilder<PartyState>(partyCount);
        for (int i = 0; i < partyCount; i++)
        {
            PartyId partyId = new(reader.ReadInt32());
            EntityId leaderId = new(reader.ReadInt32());
            int memberCount = reader.ReadInt32();
            ValidateCount(memberCount, MaxPartyMembers, nameof(memberCount));

            ImmutableArray<PartyMember>.Builder members = ImmutableArray.CreateBuilder<PartyMember>(memberCount);
            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                members.Add(new PartyMember(new EntityId(reader.ReadInt32())));
            }

            parties.Add(new PartyState(partyId, leaderId, members.MoveToImmutable()).Canonicalize());
        }

        return new PartyRegistry(nextPartySequence, parties.MoveToImmutable()).Canonicalize();
    }


    private static void WritePartyInviteRegistry(BinaryWriter writer, PartyInviteRegistry partyInviteRegistry)
    {
        PartyInviteRegistry canonical = partyInviteRegistry.Canonicalize();
        writer.Write(canonical.Invites.Length);
        foreach (PartyInvite invite in canonical.Invites)
        {
            writer.Write(invite.InviteeId.Value);
            writer.Write(invite.PartyId.Value);
            writer.Write(invite.InviterId.Value);
            writer.Write(invite.CreatedTick);
        }
    }

    private static PartyInviteRegistry ReadPartyInviteRegistry(BinaryReader reader)
    {
        int inviteCount = reader.ReadInt32();
        ValidateCount(inviteCount, MaxPartyInvites, nameof(inviteCount));

        ImmutableArray<PartyInvite>.Builder invites = ImmutableArray.CreateBuilder<PartyInvite>(inviteCount);
        for (int i = 0; i < inviteCount; i++)
        {
            EntityId inviteeId = new(reader.ReadInt32());
            PartyId partyId = new(reader.ReadInt32());
            EntityId inviterId = new(reader.ReadInt32());
            int createdTick = reader.ReadInt32();
            invites.Add(new PartyInvite(inviteeId, partyId, inviterId, createdTick));
        }

        return new PartyInviteRegistry(invites.MoveToImmutable()).Canonicalize();
    }

    private static void WriteInstanceRegistry(BinaryWriter writer, InstanceRegistry instanceRegistry)
    {
        InstanceRegistry canonical = instanceRegistry.Canonicalize();
        writer.Write(canonical.NextInstanceOrdinal);
        writer.Write(canonical.Instances.Length);
        foreach (ZoneInstanceState instance in canonical.Instances)
        {
            writer.Write(instance.Id.Value);
            writer.Write(instance.PartyId.Value);
            writer.Write(instance.ZoneId.Value);
            writer.Write(instance.CreationTick);
            writer.Write(instance.Ordinal);
            writer.Write(instance.RngSeed);
        }
    }

    private static InstanceRegistry ReadInstanceRegistry(BinaryReader reader)
    {
        int nextInstanceOrdinal = reader.ReadInt32();
        if (nextInstanceOrdinal <= 0)
        {
            throw new InvalidDataException("Instance next ordinal must be positive.");
        }

        int count = reader.ReadInt32();
        ValidateCount(count, MaxInstances, nameof(count));

        ImmutableArray<ZoneInstanceState>.Builder instances = ImmutableArray.CreateBuilder<ZoneInstanceState>(count);
        ulong lastInstanceId = 0;
        for (int i = 0; i < count; i++)
        {
            ulong instanceId = reader.ReadUInt64();
            if (i > 0 && instanceId <= lastInstanceId)
            {
                throw new InvalidDataException("Instances are not in strictly ascending ZoneInstanceId order.");
            }

            lastInstanceId = instanceId;
            PartyId partyId = new(reader.ReadInt32());
            ZoneId zoneId = new(reader.ReadInt32());
            int creationTick = reader.ReadInt32();
            int ordinal = reader.ReadInt32();
            int rngSeed = reader.ReadInt32();
            instances.Add(new ZoneInstanceState(new ZoneInstanceId(instanceId), partyId, zoneId, creationTick, ordinal, rngSeed));
        }

        return new InstanceRegistry(nextInstanceOrdinal, instances.MoveToImmutable()).Canonicalize();
    }



    private static void WriteEncounterRegistry(BinaryWriter writer, EncounterRegistry encounterRegistry)
    {
        EncounterRegistry canonical = encounterRegistry.Canonicalize();
        writer.Write(canonical.Definitions.Length);
        foreach (EncounterDefinition definition in canonical.Definitions)
        {
            writer.Write(definition.Id.Value);
            writer.Write(definition.Key);
            writer.Write(definition.Version);
            writer.Write(definition.ZoneId.Value);
            writer.Write(definition.PhasesOrEmpty.Length);
            foreach (EncounterPhaseDefinition phase in definition.PhasesOrEmpty)
            {
                writer.Write(phase.TriggersOrEmpty.Length);
                foreach (EncounterTriggerDefinition trigger in phase.TriggersOrEmpty)
                {
                    writer.Write((byte)trigger.Kind);
                    writer.Write(trigger.AtTickOffset);
                    writer.Write((byte)trigger.Target.Kind);
                    writer.Write(trigger.Target.EntityId.Value);
                    writer.Write(trigger.Pct);
                    writer.Write(trigger.ActionsOrEmpty.Length);
                    foreach (EncounterActionDefinition action in trigger.ActionsOrEmpty)
                    {
                        writer.Write((byte)action.Kind);
                        writer.Write(action.NpcArchetypeId);
                        writer.Write(action.X.Raw);
                        writer.Write(action.Y.Raw);
                        writer.Write(action.Count);
                        writer.Write((byte)action.Caster.Kind);
                        writer.Write(action.Caster.EntityId.Value);
                        writer.Write(action.SkillId.Value);
                        writer.Write((byte)action.Target.Kind);
                        writer.Write((byte)action.Target.EntityRef.Kind);
                        writer.Write(action.Target.EntityRef.EntityId.Value);
                        writer.Write(action.Target.X.Raw);
                        writer.Write(action.Target.Y.Raw);
                        writer.Write((byte)action.StatusSource.Kind);
                        writer.Write(action.StatusSource.EntityId.Value);
                        writer.Write((byte)action.StatusTarget.Kind);
                        writer.Write(action.StatusTarget.EntityId.Value);
                        writer.Write((byte)action.StatusType);
                        writer.Write(action.StatusDurationTicks);
                        writer.Write(action.StatusMagnitudeRaw);
                        writer.Write(action.PhaseIndex);
                    }
                }
            }
        }

        writer.Write(canonical.RuntimeStates.Length);
        foreach (EncounterRuntimeState runtime in canonical.RuntimeStates)
        {
            writer.Write(runtime.EncounterId.Value);
            writer.Write(runtime.CurrentPhase);
            writer.Write(runtime.StartTick);
            writer.Write(runtime.BossEntityId.Value);
            writer.Write(runtime.InstanceId.Value);
            writer.Write(runtime.FiredTriggers.Length);
            for (int i = 0; i < runtime.FiredTriggers.Length; i++)
            {
                writer.Write(runtime.FiredTriggers[i]);
            }
        }
    }

    private static EncounterRegistry ReadEncounterRegistry(BinaryReader reader)
    {
        int definitionCount = reader.ReadInt32();
        ValidateCount(definitionCount, MaxEncounters, nameof(definitionCount));
        ImmutableArray<EncounterDefinition>.Builder definitions = ImmutableArray.CreateBuilder<EncounterDefinition>(definitionCount);
        ulong lastEncounterId = 0;
        for (int i = 0; i < definitionCount; i++)
        {
            ulong encounterId = reader.ReadUInt64();
            if (i > 0 && encounterId <= lastEncounterId)
            {
                throw new InvalidDataException("Encounters are not in strictly ascending EncounterId order.");
            }

            lastEncounterId = encounterId;
            string key = reader.ReadString();
            int version = reader.ReadInt32();
            ZoneId zoneId = new(reader.ReadInt32());
            int phaseCount = reader.ReadInt32();
            ValidateCount(phaseCount, MaxEncounterPhases, nameof(phaseCount));
            ImmutableArray<EncounterPhaseDefinition>.Builder phases = ImmutableArray.CreateBuilder<EncounterPhaseDefinition>(phaseCount);
            for (int p = 0; p < phaseCount; p++)
            {
                int triggerCount = reader.ReadInt32();
                ValidateCount(triggerCount, MaxEncounterTriggers, nameof(triggerCount));
                ImmutableArray<EncounterTriggerDefinition>.Builder triggers = ImmutableArray.CreateBuilder<EncounterTriggerDefinition>(triggerCount);
                for (int t = 0; t < triggerCount; t++)
                {
                    EncounterTriggerKind triggerKind = (EncounterTriggerKind)reader.ReadByte();
                    int atTickOffset = reader.ReadInt32();
                    EntityRef target = new((EntityRefKind)reader.ReadByte(), new EntityId(reader.ReadInt32()));
                    int pct = reader.ReadInt32();
                    int actionCount = reader.ReadInt32();
                    ValidateCount(actionCount, MaxEncounterActions, nameof(actionCount));
                    ImmutableArray<EncounterActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<EncounterActionDefinition>(actionCount);
                    for (int a = 0; a < actionCount; a++)
                    {
                        EncounterActionKind actionKind = (EncounterActionKind)reader.ReadByte();
                        string npcArchetypeId = reader.ReadString();
                        Fix32 x = new(reader.ReadInt32());
                        Fix32 y = new(reader.ReadInt32());
                        int count = reader.ReadInt32();
                        EntityRef caster = new((EntityRefKind)reader.ReadByte(), new EntityId(reader.ReadInt32()));
                        SkillId skillId = new(reader.ReadInt32());
                        CastTargetKind targetKind = (CastTargetKind)reader.ReadByte();
                        EntityRef targetRef = new((EntityRefKind)reader.ReadByte(), new EntityId(reader.ReadInt32()));
                        TargetSpec targetSpec = new(targetKind, targetRef, new Fix32(reader.ReadInt32()), new Fix32(reader.ReadInt32()));
                        EntityRef statusSource = new((EntityRefKind)reader.ReadByte(), new EntityId(reader.ReadInt32()));
                        EntityRef statusTarget = new((EntityRefKind)reader.ReadByte(), new EntityId(reader.ReadInt32()));
                        StatusEffectType statusType = (StatusEffectType)reader.ReadByte();
                        int statusDurationTicks = reader.ReadInt32();
                        int statusMagnitudeRaw = reader.ReadInt32();
                        int phaseIndex = reader.ReadInt32();
                        actions.Add(new EncounterActionDefinition(actionKind, npcArchetypeId, x, y, count, caster, skillId, targetSpec, statusSource, statusTarget, statusType, statusDurationTicks, statusMagnitudeRaw, phaseIndex));
                    }

                    triggers.Add(new EncounterTriggerDefinition(triggerKind, atTickOffset, target, pct, actions.MoveToImmutable()));
                }

                phases.Add(new EncounterPhaseDefinition(triggers.MoveToImmutable()));
            }

            definitions.Add(new EncounterDefinition(new EncounterId(encounterId), key, version, zoneId, phases.MoveToImmutable()));
        }

        int runtimeCount = reader.ReadInt32();
        ValidateCount(runtimeCount, MaxEncounters, nameof(runtimeCount));
        ImmutableArray<EncounterRuntimeState>.Builder runtimes = ImmutableArray.CreateBuilder<EncounterRuntimeState>(runtimeCount);
        ulong lastRuntimeEncounterId = 0;
        for (int i = 0; i < runtimeCount; i++)
        {
            ulong encounterId = reader.ReadUInt64();
            if (i > 0 && encounterId <= lastRuntimeEncounterId)
            {
                throw new InvalidDataException("Encounter runtime states are not in strictly ascending EncounterId order.");
            }

            lastRuntimeEncounterId = encounterId;
            int currentPhase = reader.ReadInt32();
            int startTick = reader.ReadInt32();
            EntityId bossEntityId = new(reader.ReadInt32());
            ZoneInstanceId instanceId = new(reader.ReadUInt64());
            int firedCount = reader.ReadInt32();
            ValidateCount(firedCount, MaxEncounterTriggers, nameof(firedCount));
            ImmutableArray<bool>.Builder fired = ImmutableArray.CreateBuilder<bool>(firedCount);
            for (int f = 0; f < firedCount; f++)
            {
                fired.Add(reader.ReadBoolean());
            }

            runtimes.Add(new EncounterRuntimeState(new EncounterId(encounterId), currentPhase, startTick, fired.MoveToImmutable(), bossEntityId, instanceId));
        }

        return new EncounterRegistry(definitions.MoveToImmutable(), runtimes.MoveToImmutable()).Canonicalize();
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

    private static bool HasRemainingData(BinaryReader reader)
    {
        Stream stream = reader.BaseStream;
        if (!stream.CanSeek)
        {
            return false;
        }

        return stream.Position < stream.Length;
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
