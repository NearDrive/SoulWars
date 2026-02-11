using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public sealed record EntityState(EntityId Id, int X, int Y);

public sealed record WorldState(int Tick, ImmutableArray<EntityState> Entities);

public sealed record Inputs(ImmutableArray<PlayerInput> Players);

public sealed record PlayerInput(EntityId EntityId, int Dx, int Dy);
