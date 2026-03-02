# MVP-C1 Audit Index (PR-91..PR-94)

> Context: aquest índex separa què va entrar realment a les PRs #91, #92, #93, #94 i ho contrasta amb el pla de MVP-C1.
> Fonts del pla MVP-C1: PR-91..PR-94 esperades al document de planificació client.【F:docs/MVP_C1.md†L46-L50】

## Metodologia (reproduïble)

Comandes usades (sense executar `dotnet`):

- `git log --oneline --decorate --all --grep='#91' --grep='#92' --grep='#93' --grep='#94'`
- `git log --oneline --reverse <merge>^1..<merge> --not <merge>^1`
- `git diff --name-only <merge>^1 <merge>^2`

Merge commits detectats:
- PR #91: `1cd8f43`
- PR #92: `3696015`
- PR #93: `a462832`
- PR #94: `ad5c499`

---

## PR-91 (`1cd8f43`) — Encounter definition/trigger system

### Commits associats
- `bebf675` PR-66: add deterministic encounter trigger/action system
- `c3fad26` fix snapshot migration payload constructor args
- `f3a5d8d` fix encounter runtime builder materialization
- `dba4057` fix encounter tests (cooldown/immutable builder)
- `9c14067` stabilize boss phase determinism test
- `2da52a0` strengthen deterministic milestone assertions
- `462af50` stabilize boss phase threshold scenario
- merge: `1cd8f43` (PR #91)

### Fitxers tocats
- `.github/workflows/ci.yml`
- `src/Game.Core/EncounterModels.cs`
- `src/Game.Core/EncounterSystem.cs`
- `src/Game.Core/Simulation.cs`
- `src/Game.Core/SimulationModels.cs`
- `src/Game.Core/StateChecksum.cs`
- `src/Game.Core/ZoneDefinitions.cs`
- `src/Game.Persistence/WorldStateSerializer.cs`
- `src/Game.Server/ZoneDefinitionsLoader.cs`
- `tests/Game.Core.Tests/BossPhaseTransitionTests.cs`
- `tests/Game.Core.Tests/EncounterTriggerTests.cs`
- `tests/Game.Core.Tests/ReplayVerifyBossEncounterCanaryTests.cs`

### Evidència funcional
- S’afegeixen models d’`Encounter` (triggers, accions, fases, runtime) al nucli, no pas client handshake/render/input.【F:src/Game.Core/EncounterModels.cs†L27-L40】【F:src/Game.Core/EncounterModels.cs†L73-L90】
- El focus de prova és de boss/encounter determinista al core tests.【F:tests/Game.Core.Tests/BossPhaseTransitionTests.cs†L1-L40】【F:tests/Game.Core.Tests/EncounterTriggerTests.cs†L1-L40】

### Cobertura MVP-C1
- **No compleix directament cap must-have de MVP-C1 #1..#5** (connectar client, handshake v1, snapshots/deltas client, dump canònic client, script move/cast amb HitEvent), perquè el canvi és de simulació encounter del servidor/core.【F:docs/MVP_C1.md†L16-L23】【F:src/Game.Core/EncounterModels.cs†L27-L40】
- Cobertura parcial només indirecta en qualitat/CI (determinisme/canary), no en funcionalitat de client headless MVP-C1.【F:docs/MVP_C1.md†L11-L13】【F:tests/Game.Core.Tests/ReplayVerifyBossEncounterCanaryTests.cs†L1-L40】

---

## PR-92 (`3696015`) — Threat/Aggro

### Commits associats
- `194c1db` PR67: add deterministic threat and aggro selection
- `86ff56b` fix replay test builder immutability
- `63822a4` stabilize replay checksum assertion
- `1a27e05` threat tests fix (Fix32 raw values)
- `fa1f8ce` replay scenario adjust per NPC death invariant
- `40710fa` tune replay damage budget
- merge: `3696015` (PR #92)

### Fitxers tocats
- `.github/workflows/ci.yml`
- `src/Game.Core/Combat/Components/ThreatComponent.cs`
- `src/Game.Core/Simulation.cs`
- `src/Game.Core/SimulationModels.cs`
- `src/Game.Persistence/WorldStateSerializer.cs`
- `tests/Game.Core.Tests/ThreatAggroSystemTests.cs`

### Evidència funcional
- Component explícit de threat/aggro (entries, add/remove threat, clamp), dins `Game.Core` combat logic.【F:src/Game.Core/Combat/Components/ThreatComponent.cs†L5-L17】【F:src/Game.Core/Combat/Components/ThreatComponent.cs†L87-L100】
- Tests centrats en threat/aggro, no en contracte client headless/connectivitat protocol v1.【F:tests/Game.Core.Tests/ThreatAggroSystemTests.cs†L1-L40】【F:docs/MVP_C1.md†L16-L23】

### Cobertura MVP-C1
- **No compleix directament MVP-C1 #1..#6**; és mecànica de combat servidor/core i persistència associada, fora del nucli client MVP-C1 definit.【F:docs/MVP_C1.md†L16-L24】【F:src/Game.Core/Combat/Components/ThreatComponent.cs†L5-L13】

---

## PR-93 (`a462832`) — Boss encounter canary scenario

### Commits associats
- `4c4125c` add deterministic boss encounter canary + restart checks
- `d2aed64` fix canary compile/stable boss targeting
- `9c83ec2` stabilize canary aggro assertions
- `4f3b109` relax canary combat-event assertion
- merge: `a462832` (PR #93)

### Fitxers tocats
- `.github/workflows/ci.yml`
- `tests/Game.Core.Tests/ReplayVerifyBossEncounterCanaryTests.cs`

### Evidència funcional
- El canvi és sobretot de canary/replay test de boss encounter, no d’implementació client headless ni protocol input/output client-side.【F:tests/Game.Core.Tests/ReplayVerifyBossEncounterCanaryTests.cs†L1-L60】【F:docs/MVP_C1.md†L16-L24】

### Cobertura MVP-C1
- Cobertura **indirecta** de guardrails CI/determinisme, alineada amb principis generals, però **sense** lliurar funcionalitats client MVP-C1 planificades per PR-93 (scripting d’inputs client move/cast).【F:docs/MVP_C1.md†L11-L13】【F:docs/MVP_C1.md†L49-L50】

---

## PR-94 (`ad5c499`) — NavGrid + Deterministic A*

### Commits associats
- `f08a590` PR-69: add deterministic NavGrid and A* pathfinding
- merge: `ad5c499` (PR #94)

### Fitxers tocats
- `.github/workflows/ci.yml`
- `src/Game.Core/DeterministicAStar.cs`
- `src/Game.Core/NavGrid.cs`
- `tests/Game.Core.Tests/PathfindingPr69Tests.cs`

### Evidència funcional
- Implementació de grid de navegació i pathfinding A* determinista al core del joc.【F:src/Game.Core/NavGrid.cs†L11-L23】【F:src/Game.Core/DeterministicAStar.cs†L3-L12】
- Tests orientats a pathfinding (PR69), no a harness client smoke in-proc especificat per MVP-C1 PR-94 planificat.【F:tests/Game.Core.Tests/PathfindingPr69Tests.cs†L1-L40】【F:docs/MVP_C1.md†L50-L50】

### Cobertura MVP-C1
- **No compleix** el PR-94 esperat de MVP-C1 (harness in-proc per client smoke CI); correspon a navegació/pathfinding de servidor/core.【F:docs/MVP_C1.md†L50-L50】【F:src/Game.Core/DeterministicAStar.cs†L14-L22】

---

## Drift explícit (fora de l’abast MVP-C1 PR-91..PR-94 del pla client)

1. **Desalineació de numeració/contingut**: les PRs #91..#94 reals implementen blocs de servidor/core (encounters, aggro, canary boss, pathfinding), mentre que el pla MVP-C1 assigna aquests números a increments de client headless (handshake/snapshot/apply/script/harness).【F:docs/MVP_C1.md†L47-L50】【F:src/Game.Core/EncounterModels.cs†L27-L40】
2. **Canvis repetits a CI** (`.github/workflows/ci.yml`) dins aquestes PRs, però el filtre canary actual està orientat a categories PR88/PR90/ClientSmoke i no introdueix cap traça clara de PR91..PR94 de client MVP-C1.【F:.github/workflows/ci.yml†L208-L210】
3. **No hi ha evidència en aquest paquet de PRs** d’entregables clau MVP-C1 client (connect/handshake v1, recepció/aplicació snapshots client, scripting move/cast client-side).【F:docs/MVP_C1.md†L16-L23】

## Resum executable
- Si cal revisar PRs MVP-C1 “client”, **aquestes PRs #91..#94 no són l’split funcional esperat del pla MVP-C1**; són una línia de treball diferent (server/core determinista).
- Per continuar auditoria, convé indexar en un següent pas quins commits sí toquen explícitament `src/Game.Client.Headless/` i mapar-los a MVP-C1 #1..#6.
