# SoulWars

Bootstrap .NET 8 solution for server-first MMO architecture (PR-00 scope only).

## Build and test

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

Or run:

```bash
./ci.sh
```

CI is the source of truth.

## Deterministic simulation note (PR-01)

PR-01 keeps positions as integer coordinates to avoid floating-point drift. PR-02 is expected to introduce `Vec2` float movement and collision logic.

State checksums are computed from a stable binary serialization and do not depend on JSON formatting.

## MVP1 verification (headless)

```bash
dotnet test -c Release
```
Runs unit/integration coverage including deterministic replay assertions.

```bash
dotnet run -c Release --project src/Game.App.Headless -- --verify-mvp1
```
Runs the replay baseline fixture and prints expected vs actual checksum as an explicit MVP1 gate.

```bash
dotnet run -c Release --project src/Game.App.Headless -- --run-scenario
```
Runs a short deterministic bot scenario headless and prints a summary with invariant status.
