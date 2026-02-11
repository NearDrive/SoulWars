namespace Game.Core;

public readonly record struct SimulationConfig(int Seed, int TickHz, float Dt);
