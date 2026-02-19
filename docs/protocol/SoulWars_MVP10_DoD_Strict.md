# SoulWars MVP10 — DoD Official (Strict)

## Abast i font

Aquest DoD unifica els criteris més estrictes ja definits a:

- `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (DoD i CI requirements MVP10)
- `docs/protocol/SoulWars_MVP10_PR_PLAN.md` (DoD i canonical ordering MVP10 legacy)
- `docs/protocol/SoulWars_MVP8_Drift_Guardrails (1).md` (drift diagnostics + snapshot guardrails)
- `docs/protocol/MVP9_PR_PLAN (1).md` (multi-zona, transfer invariants, checksums)

No introdueix requisits nous: consolida requisits existents en versió verificable única.

---

## 1) Determinisme (obligatori)

S'ha de complir simultàniament:

- Simulació **Fix32/int only** en runtime de combat (sense `float`/`MathF` al loop de sim).
- Cap RNG no controlat; cap font temporal no determinista al loop (`DateTime.Now`-style runtime coupling fora de scope de sim).
- Entrades de combat com a **WorldCommands** autoritaris.
- Ordre canònic global:
  - Zones: `ZoneId asc`
  - Sessions: `SessionId asc`
  - Entitats: `EntityId asc`
  - Resolució combat/commands: `Tick -> CasterId/EntityId -> Sequence`
  - Llistes de snapshot/events ordenades determinísticament
- Cap iteració no ordenada de `Dictionary` pot afectar resultats observables.

**Evidència verificable mínima:** tests de determinisme/canonical order + invariants de zona i ordering en suites CI.

---

## 2) ReplayVerify / SnapshotVerify / checksums

- `ReplayVerify` ha de mantenir checksums idèntics entre execucions equivalents.
- En mismatch de replay: artefactes de diagnòstic requerits (`expected`, `actual`, `tickreport_*`, primer tick divergent).
- Snapshot Load amb **checksum-on-load fail-fast**.
- Snapshot meta i migracions versionades han de validar-se amb fixtures reals.
- Model jeràrquic de checksums multi-zona:
  - `ZoneChecksum`
  - `GlobalChecksum = Hash(ZoneChecksums ordenats per ZoneId)`
- Roundtrip snapshot + restart no poden introduir drift ni ID drift.

**Evidència verificable mínima:** suites ReplayVerify + snapshot roundtrip/restart + migració fixtures + checksum invariants.

---

## 3) Budgets (AOI, snapshots, combat events)

- AOI/subscription amb dispatch determinista i sense leakage cross-zone.
- Budgets de snapshot/dispatch i rendiment mantenen estabilitat (incloent validacions de perf/soak ja definides).
- Combat:
  - Events en ordre canònic
  - Guardrails de volum (`MaxTargets`, i/o `MaxCombatEventsPerTick` on aplica)

**Evidència verificable mínima:** tests AOI routing/subscription, perf budgets, combat budgets, scenario/replay tests.

---

## 4) Multi-zona (transfer, no dupes, invariants)

- Runtime multi-zona funcional amb tick order canònic.
- Transferències deterministes **end-of-tick** i atòmiques.
- Invariants de consistència:
  - entitat en 0 o 1 zona
  - no duplicació mid-tick
  - cua de transfer deterministic
  - IDs únics cross-zones
- Snapshot/restart multi-zona preserva ordres, IDs i checksums.

**Evidència verificable mínima:** tests de zone manager, transfer invariants, multi-zone snapshots/restart, canary/golden multi-zona.

---

## 5) Hardening (fuzz, rate-limit, soak)

- Input validation i rate limiting actius en servidor autoritari.
- Fuzz/security tests sense crash ni aplicació d'estats il·legals.
- Soak deterministic estable (sense drift no justificat).
- Drift guardrails (MVP8) han de continuar operatius després de canvis de combat.

**Evidència verificable mínima:** suites `Hardening`, `Security`, `Soak`, i gates de drift/DoD.

---

## 6) CI Gates oficials per considerar MVP10 “Done”

MVP10 només es considera **Done** si tots aquests gates estan verds a GitHub Actions:

1. Build Release (`dotnet build -c Release --no-restore`)
2. Test matrix principal (`dotnet test -c Release --filter "Category!=Soak"`)
3. DoD gate (`Category=DoD`)
4. ReplayVerify gate (`Category=ReplayVerify`)
5. Persistence consistency gate (`Category=Persistence`)
6. MVP7 verification gate (`./scripts/verify_mvp7.sh`) com a regressió sistèmica
7. MVP1 headless verify gate (`Game.App.Headless --verify-mvp1`) com a baseline funcional
8. Soak gate (`Category=Soak`)

## Condició final de tancament

Per tancar MVP10 oficialment:

- PR scope complert segons MasterPlan PR-54..PR-61.
- Tots els criteris d'aquest DoD strict verificats per proves/gates CI.
- Sense regressions en determinisme, checksums, snapshot integrity ni invariants multi-zona.
