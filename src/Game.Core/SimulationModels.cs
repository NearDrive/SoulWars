using System.Collections.Immutable;

namespace Game.Core;

public readonly record struct EntityId(int Value);

public sealed record EntityState(EntityId Id, Vec2Fix Pos, Vec2Fix Vel);

public sealed record WorldState(int Tick, TileMap Map, ImmutableArray<EntityState> Entities);

public sealed record Inputs(ImmutableArray<PlayerInput> Players);

public sealed record PlayerInput(EntityId EntityId, sbyte MoveX, sbyte MoveY);
