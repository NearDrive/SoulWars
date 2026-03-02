# PR-93 Review — Scripted inputs deterministic

## Objectiu
Validar que el script `basic` del client headless sigui determinista, que l’ordenació per seqüència/tick sigui estable al servidor, i que el flux `move + cast` observi un resultat estructurat (`HitEvent`) a arena.

## Paths de script/runner
- Runner/script principal: `src/Game.Client.Headless/HeadlessClientRunner.cs`
- Configuració del script (`--script`, default `basic`): `src/Game.Client.Headless/ClientOptions.cs`
- Smoke test arena que valida el resultat: `tests/Game.Server.Tests/ClientSmoke/ClientMvpC1Tests.cs`
- Ordenació estable de cues d’inputs/cast al servidor: `src/Game.Server/ServerHost.cs`

## Determinisme del script `basic`
- El runner no usa `DateTime`, timers de rellotge de sistema ni RNG.
- Eliminada la dependència de `Task.Delay(1)` dins del loop de runner i del test; ara el progressat és per `Task.Yield()` i per ticks del servidor (`host.StepOnce()` al test), fent l’execució independent de latència temporal real.
- La lògica de `basic` és purament de dades de snapshot:
  - envia `InputCommand` mentre `inputTick <= snapshot.Tick + 1`
  - envia un únic `CastSkillCommand` quan detecta primer target vàlid ordenat per `EntityId`.

## Com es calcula `Sequence`
- La seqüència d’inputs **no la calcula el client**: al servidor és un comptador per sessió (`session.NextInputSequence`, inicial 1).
- Tant `InputCommand` com `CastSkillCommand` consumeixen la mateixa seqüència monotònica per sessió quan entren a pending queue.
- L’ordenació de processat és estable i explícita:
  - moves: `OrderBy(Tick).ThenBy(SessionId).ThenBy(Sequence)`
  - casts: `OrderBy(Tick).ThenBy(SessionId).ThenBy(Sequence)`

## `Move*` i `CastPoint` conforme protocol
- `InputCommand` manté exactament el contracte existent: `(Tick, MoveX, MoveY)`.
- `CastSkillCommand` manté exactament el contracte existent: `(Tick, CasterId, SkillId, ZoneId, TargetKind, TargetEntityId, TargetPosXRaw, TargetPosYRaw)`.
- El runner per cast a punt usa `TargetKind=3` i coordenades `TargetPosXRaw/TargetPosYRaw`, sense camps nous.

## Validació d’èxit (asserts exactes)
Al smoke test d’arena (`ClientServer_Smoke_Arena_BasicRun`) s’asserta de forma **estructurada**, no per logs:
1. `HandshakeAccepted == true`.
2. `SentInputs` no és buit.
3. Els ticks de `SentInputs` són monòtons ascendents i sense duplicats.
4. Hi ha exactament un cast (`Assert.Single(result.SentCasts)`).
5. El cast és punt (`TargetKind == 3`) i sense target d’entitat (`TargetEntityId == 0`).
6. Hi ha exactament un hit observable (`Assert.Single(result.ObservedHits)`).
7. L’`AbilityId` del `HitEvent` coincideix amb l’ability configurada al client.

Això substitueix l’antic patró “assert per logs” per una sortida mínima estructurada a `HeadlessRunResult`:
- `SentInputs`
- `SentCasts`
- `ObservedHits`
