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
