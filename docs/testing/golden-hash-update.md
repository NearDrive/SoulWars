# PR98 Golden Hash Update Playbook

Use this process whenever `ClientServer_Smoke60s_Arena_Golden` fails only because of a `TraceHash` mismatch.

## Goal

Keep the golden trace gate deterministic while making hash refreshes explicit and auditable.

## Required workflow (Option B)

1. **Run canary lane and capture actual hash**
   - Execute the canary test lane.
   - Read the failing assertion message from `ClientServer_Smoke60s_Arena_Golden` and copy `Actual TraceHash`.

2. **Create a dedicated golden-update commit/PR**
   - Update `GoldenHash` in `tests/Game.Server.Tests/ClientSmoke/ClientServerGoldenSmokeTests.cs`.
   - Commit only the golden hash update (no behavior changes in the same commit).

3. **Re-run canary lane on that PR**
   - Confirm `ClientServer_Smoke60s_Arena_Golden` passes with the new hash.

4. **Stability confirmation**
   - Re-run the same canary lane once more (or rerun jobs) to confirm the hash is stable and not flaky.

## Notes

- If the hash changes repeatedly between reruns with no code changes, treat it as a determinism bug and investigate before merging.
- Keep behavior changes and golden refreshes separate for easier review and blame.
