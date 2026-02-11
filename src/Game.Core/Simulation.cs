using System.Collections.Immutable;

namespace Game.Core;

public static class Simulation
{
    public static WorldState CreateInitialState(SimulationConfig config)
    {
        SimRng rng = new(config.Seed);
        EntityState player = new(new EntityId(1), rng.NextInt(-2, 3), rng.NextInt(-2, 3));

        return new WorldState(
            Tick: 0,
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
            if (input.Dx is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), "Dx must be in range [-1..1].");
            }

            if (input.Dy is < -1 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), "Dy must be in range [-1..1].");
            }

            inputsByEntityId[input.EntityId.Value] = input;
        }

        ImmutableArray<EntityState> orderedEntities = state.Entities
            .OrderBy(entity => entity.Id.Value)
            .ToImmutableArray();

        ImmutableArray<EntityState>.Builder updatedEntities = ImmutableArray.CreateBuilder<EntityState>(orderedEntities.Length);

        foreach (EntityState entity in orderedEntities)
        {
            if (inputsByEntityId.TryGetValue(entity.Id.Value, out PlayerInput? input) && input is not null)
            {
                updatedEntities.Add(entity with
                {
                    X = entity.X + input.Dx,
                    Y = entity.Y + input.Dy
                });

                continue;
            }

            updatedEntities.Add(entity);
        }

        _ = config;

        return new WorldState(
            Tick: state.Tick + 1,
            Entities: updatedEntities
                .ToImmutable()
                .OrderBy(entity => entity.Id.Value)
                .ToImmutableArray());
    }
}
