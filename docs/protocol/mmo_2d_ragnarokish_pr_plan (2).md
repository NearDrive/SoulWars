# Roadmap de PRs (playability‑first, però verificable)

## PR‑00 — Repo bootstrap + CI
**Objectiu:** tenir solució compilable, tests i pipeline verds.
- Crear solution:
  - `src/Game.Core` (lògica pura)
  - `src/Game.Server` (host autoritari)
  - `src/Game.Protocol` (DTOs/commands)
  - `src/Game.Persistence` (abstraccions DB)
  - `src/Game.BotRunner` (clients headless)
  - `tests/Game.Core.Tests`, `tests/Game.Server.Tests` (mínim)
- `Directory.Build.props` (nullable, analyzers, TreatWarningsAsErrors opcional)
- GitHub Actions: build + test en Release.
- Script `./ci.sh` (local/CI), però CI és el “source of truth”.

**Acceptació**
- `dotnet build -c Release` OK
- `dotnet test -c Release` OK a CI

---

## PR‑01 — Model de simulació determinista (Core) + RNG central
**Objectiu:** sim determinista amb seed i tick loop pur.
- `SimulationConfig { int Seed; int TickHz; float Dt; }`
- `SimRng` central (ex: Xoshiro/PCG) injectat a la sim.
- `WorldState` immutable/serialitzable (o “mostly immutable”) amb:
  - Entities dictionary (players, mobs més tard)
  - Time/tick
- `SimStep(WorldState, Inputs) -> WorldState`
- Tests:
  - Mateixa seed + mateix input => mateix checksum (N ticks)
  - Seeds diferents => checksums diferents (probabilístic però controlat)

**Acceptació**
- Test de determinisme verd.

---

## PR‑02 — Física simple: moviment lliure + col·lisions (AABB) top‑down
**Objectiu:** moviment 2D lliure amb col·lisions simples (sense tilemap).
- Representació:
  - `Vec2 (float X,Y)`
  - `Aabb { Vec2 Center; Vec2 HalfExtents; }`
- Obstacles estàtics (llista d’AABB) per MVP.
- Moviment: integració simple (velocitat clamp) i resolució de col·lisions:
  - sweep/slide bàsic o resolució per eixos (X després Y)
- Tests:
  - no travessar obstacles
  - moviment màxim per tick
  - resolució estable (sense oscil·lació) en casos bàsics

**Acceptació**
- Sim “camina contra paret” sense travessar-la.

---

## PR‑03 — Zones: una zona local (encara) amb contractes d’entrada/sortida
**Objectiu:** dissenyar escalat sense implementar shard real encara.
- `ZoneId`
- `ZoneState` conté obstacles i entitats
- `WorldState` conté un mapa `ZoneId -> ZoneState` (o només la zona activa)
- Input inclou intents: `MoveIntent { Vec2 Direction; float Speed; }`
- Tests:
  - entrar a zona crea player entity
  - sortir elimina entity

**Acceptació**
- 1 zona funcional i “pluggable”.

---

## PR‑04 — Protocol i networking: WebSocket + JSON (Server autoritari)
**Objectiu:** connectar clients headless i avançar ticks.
- `Game.Server` amb:
  - Acceptar connexions WS
  - Sessions (connId -> playerId)
  - Cua d’inputs per tick
- Missatges (MVP):
  - Client→Server: `Hello`, `Login`, `EnterZone`, `MoveIntent`
  - Server→Client: `Welcome`, `EnterOk`, `Snapshot` (o `StateDelta`)
- “Snapshot” mínim: tick, posicions entities visibles (tota la zona per MVP)

**Acceptació**
- Un client pot connectar, entrar a zona, moure’s i rebre snapshots.

---

## PR‑05 — BotRunner: N bots + invariants
**Objectiu:** substituir UI amb bots repetibles.
- `BotRunner` crea N connexions, fa login/enter, envia moviments amb seed.
- Invariants bàsiques:
  - tick monotònic
  - posicions finites (no NaN/Inf)
  - velocitat <= max
  - col·lisió respectada (no dins obstacles)
- Output:
  - resum al final (ticks, msgs, errors)
  - exit code != 0 si falla invariant

**Acceptació**
- A CI: `BotRunner --bots 20 --seconds 10 --seed 123` passa.

---

## PR‑06 — Replay: enregistrar inputs + “replay-verify” amb checksum
**Objectiu:** regressions “visuals” sense pantalla.
- Format `ReplayRecord`:
  - Seed, config, llista inputs per tick (per jugador/bot)
- Comandes:
  - `Game.Server --record-replay out.json`
  - `Game.Tools replay-verify in.json`
- Checksum de l’estat per tick (o al final):
  - hash estable sobre entitats (ordenat)
- Tests:
  - replay-verify retorna el checksum esperat
  - canviar 1 input => checksum diferent

**Acceptació**
- CI executa un replay determinista i valida checksum.

---

## PR‑07 — Persistència mínima (accounts + characters + last position)
**Objectiu:** persistir el mínim sense trencar determinisme.
- DB schema mínim:
  - `Accounts(Id, UserName, PasswordHash, CreatedAt)`
  - `Characters(Id, AccountId, Name, ZoneId, X, Y, CreatedAt, UpdatedAt)`
- En el sim, **no** fer queries; el server carrega abans d’entrar i guarda en punts definits:
  - on disconnect
  - cada X segons (checkpoint)
- Tests:
  - create account/character
  - reconnect => posició restaurada

**Acceptació**
- `Server` arrenca amb DB, crea/recupera character i restaura posició.

---

## PR‑08 — Observabilitat headless: logs, mètriques i traces mínimes
**Objectiu:** saber què passa sense UI.
- Logging estructurat (Serilog o Microsoft.Extensions.Logging)
- Mètriques bàsiques:
  - players connected
  - tick time (avg/p95)
  - messages/sec
- Logs d’errors d’invariants i disconnect reasons.

**Acceptació**
- CI mostra logs útils; `BotRunner` imprimeix estadístiques.

---

## PR‑09 — Hardening: rate limits + validació d’inputs
**Objectiu:** client hostil.
- Clamp de vectors i velocitats
- Cooldown de MoveIntent (p.ex. max 20/s)
- Rebutjar missatges massa grans / mal formats
- Tests:
  - intents maliciosos => server no peta i no aplica moviments il·legals

**Acceptació**
- “Fuzz” simple de missatges passa.

---

## PR‑10 — Preparació per “Ragnarok loop”: combat minimal (opcional per MVP2)
**Objectiu:** assentar la base sense economia encara.
- Afegir “mob dummy” amb HP i aggro simple
- 1 skill: `BasicAttack` amb cooldown
- Snapshot inclou HP i estat bàsic
- Bots: alguns ataquen, altres kite

**Acceptació**
- replay-verify continua estable
- bots poden matar un mob sense desync

---

# Prompt “Projecte Codex” (creació del repo / implementació guiada)

Copia/enganxa això a Codex com a instruccions inicials del projecte.

## OBJECTIU
Implementa un MMO 2D top‑down amb moviment lliure i col·lisions simples, **server-first i headless**, validat per CI. L’MVP ha de permetre: connectar-se, entrar a una zona, moure’s amb col·lisions, rebre snapshots, i executar bots + replays deterministes.

## CONSTRAINTS (molt important)
- **No hi ha UI**: tot ha de ser verificable per tests, bots i replays.
- **Servidor autoritari**: el client només envia intents.
- **Determinisme**: sim amb seed i RNG central; replays han de reproduir checksums exactes.
- **Codi fortament tipat** (C#), nullable enabled.
- **No afegeixis features no demanades**: segueix estrictament el PR actual.
- Cada PR ha d’incloure:
  - canvis de codi
  - tests/validacions (mínim 1)
  - comandes per executar a CI/local
  - nota de riscos/edge cases

## SCOPE MVP
- 1 zona
- obstacles estàtics AABB
- moviment 2D lliure
- networking WebSocket + JSON
- `BotRunner` per simular clients
- `ReplayRecord` + `replay-verify`

## STRUCTURA REPO
- `src/Game.Core`
- `src/Game.Server`
- `src/Game.Protocol`
- `src/Game.Persistence`
- `src/Game.BotRunner`
- `src/Game.Tools`
- `tests/*`

## DEFINICIÓ MOVIMENT/COL·LISIÓ
- posició float (X,Y)
- AABB player (halfExtents definits)
- obstacles AABB estàtics
- resolució per eixos (X then Y) és acceptable per MVP si és estable i testejada

## ACCEPTATION CHECKLIST (per cada PR)
- `dotnet build -c Release` OK
- `dotnet test -c Release` OK
- Si aplica: `BotRunner ...` exit 0
- Si aplica: `replay-verify ...` checksum match

## FORMAT DE RESPOSTA (en cada PR)
1. Resum (què fa)
2. Fitxers canviats (llista)
3. Comandes per validar
4. Notes de disseny (curtes)
5. Riscos/limitacions

---
