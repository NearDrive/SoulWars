using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
    public static WorldState CreateInitialState(SimulationConfig config)
    {
        TileMap map = WorldGen.Generate(config, config.MapWidth, config.MapHeight);
        Vec2Fix spawn = FindSpawn(map, config.Radius);

        EntityState player = new(
            Id: new EntityId(1),
            Pos: spawn,
            Vel: Vec2Fix.Zero);

        return new WorldState(
            Tick: 0,
            Map: map,
            Entities: ImmutableArray.Create(player));
    }

    public static WorldState Step(SimulationConfig config, WorldState state, Inputs inputs)
    {
        ArgumentNullException.ThrowIfNull(state);

        ImmutableArray<PlayerInput> playerInputs = inputs.Players.IsDefault
            ? ImmutableArray<PlayerInput>.Empty
            : inputs.Players;

        Dictionary<int, PlayerInput> inputsByEntityId = new();
        foreach (PlayerInput input in playerInputs)
        {
            if (input.MoveX is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), "MoveX must be in range [-1..1].");
            }

            if (input.MoveY is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), "MoveY must be in range [-1..1].");
            }

            inputsByEntityId[input.EntityId.Value] = input;
        }

        ImmutableArray<EntityState> orderedEntities = state.Entities
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray();

        ImmutableArray<EntityState>.Builder updatedEntities = ImmutableArray.CreateBuilder<EntityState>(orderedEntities.Length);

        foreach (EntityState entity in orderedEntities)
        {
            PlayerInput input = inputsByEntityId.TryGetValue(entity.Id.Value, out PlayerInput mapped)
                ? mapped
                : new PlayerInput(entity.Id, 0, 0);

            EntityState updated = Physics2D.Integrate(
                entity,
                input,
                state.Map,
                config.DtFix,
                config.MoveSpeed,
                config.Radius);

            updatedEntities.Add(updated);
        }

        _ = config.MaxSpeed;

        return new WorldState(
            Tick: state.Tick + 1,
            Map: state.Map,
            Entities: updatedEntities
                .ToImmutable()
                .OrderBy(entity => entity.Id.Value)
                .ToImmutableArray());
    }

    private static Vec2Fix FindSpawn(TileMap map, Fix32 radius)
    {
        Fix32 half = new(Fix32.OneRaw / 2);

        for (int y = 1; y < map.Height - 1; y++)
        {
            for (int x = 1; x < map.Width - 1; x++)
            {
                if (map.Get(x, y) == TileKind.Solid)
                {
                    continue;
                }

                Vec2Fix candidate = new(Fix32.FromInt(x) + half, Fix32.FromInt(y) + half);
                if (!Physics2D.OverlapsSolidTile(candidate, radius, map))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Unable to find a valid spawn position.");
    }
}
