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
