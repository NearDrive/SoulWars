# Canary replay fixtures

`Replays/mvp9_multizone_golden.json` defines the existing deterministic multi-zone canary scenario used by `Mvp9GoldenReplayTests`.

`Replays/mvp9_multizone_canary.json` defines the PR-53H multi-zone canary scenario used by `Mvp9CanaryReplayTests`.
The test records and verifies the replay with the real `ReplayRunner` pipeline and checks a frozen `FinalGlobalChecksum` literal to catch CI drift across zones/transfers/AOI/checksum behavior.
