# Canary replay fixtures

`Replays/mvp9_multizone_golden.json` defines the deterministic multi-zone canary scenario used by `Mvp9GoldenReplayTests`.
The test records and verifies the replay, then asserts a frozen final global checksum to detect protocol/drift regressions early in CI.
