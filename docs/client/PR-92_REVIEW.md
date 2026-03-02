# PR-92 Review — Canonical state apply + dump

## Objectiu
Validar que l’aplicació de snapshots al client i el `dump` de l’estat siguin canònics (independents de l’ordre d’arribada/inserció) i no depenguin de cultura/locale.

## Fitxers revisats
- `src/Game.Client.Headless/ClientWorldView.cs`
- `tests/Game.Server.Tests/ClientSmoke/ClientMvpC1Tests.cs`
- `src/Game.Client.Headless/HeadlessClientRunner.cs` (verificació de logs sense timestamps)
- `src/Game.Client.Headless/Program.cs` (verificació de sortida simple sense dades no deterministes)

## Prova d’ordre canònic (on s’ordena)

### View model del món
- El view model intern usa `Dictionary<int, SnapshotEntity>` (`_entities`) per mantenir estat viu.
- El punt crític de canonicitat és el `dump`: **no s’enumera directament el `Dictionary` en ordre d’inserció**.
- A `DumpCanonical()`, les entitats s’iteren amb ordre explícit:
  - `OrderBy(e => e.EntityId)` (criteri principal canònic)
  - `ThenBy(e => e.Kind)`, `ThenBy(e => e.PosXRaw)`, `ThenBy(e => e.PosYRaw)` (tie-breakers estables).
- Per tant, el dump resultant és estable amb independència de l’ordre de recepció/actualització intern.

### Dump estable requerit: tick, zone, entities per `EntityId asc`
- Capçalera del dump: `tick=<...> zone=<...>` en una única línia.
- Línies d’entitats: ordenades per `EntityId` ascendent abans d’imprimir.
- Cobertura via test existent `ClientStateDump_IsCanonical` (`Category=PR92`), que valida exactament l’ordre esperat (`2,5,9`) tot i construir snapshot amb ordre no canònic (`9,2,5`).

## Prova de “no timestamps / no randomness”
- En el codi revisat de client (`ClientWorldView`, `HeadlessClientRunner`, `Program`) no hi ha ús de:
  - `DateTime.Now` / `DateTime.UtcNow`
  - `Guid.NewGuid()`
  - APIs de `Random`
- El dump canònic només serialitza camps de snapshot (`tick`, `zone`, `entityId`, `kind`, `pos`, `hp`) i no afegeix metadades temporals o aleatòries.

## Dependència de culture/locale

### Estat detectat
- El format de sortida era funcionalment numèric/simple, però no quedava explicitat de forma forta que la construcció del string fos invariant a cultura.

### Ajust mínim aplicat
- A `ClientWorldView.DumpCanonical()`, la construcció de línies passa a `FormattableString.Invariant(...)` tant per capçalera com per línies d’entitat.
- Això blinda la serialització textual contra variacions de cultura local del procés.

### Evidència de test
- S’afegeix `ClientStateDump_IsCultureInvariant` (`Category=PR92`, `ClientSmoke`, `Canary`), que força `CurrentCulture` i `CurrentUICulture` a `tr-TR` i valida que el dump és idèntic al canonical esperat.

## Enumeracions de Dictionary sense ordenar
- No s’ha detectat cap camí de dump que enumeri `Dictionary` sense ordenar.
- El dump de `ClientWorldView` ja aplicava ordenació explícita sobre `_entities.Values`.
- **Fix mínim realitzat igualment:** reforç d’invariància de cultura + test específic de cultura per prevenir regressions de canonicitat textual.
