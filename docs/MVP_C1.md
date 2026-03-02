# SoulWars — Client MVP (MVP-C1)

Data: 2026-03-02  
Timezone: Europe/Madrid

## Objectiu
Client mínim (**headless-first**) per a la primera prova jugable contra el servidor (fins MVP15), mantenint model **server-authoritative** i validació a **CI**.

## Principis
- Servidor autoritari: el client només envia intents i renderitza estat rebut.
- Determinisme en tests (scripts/fixtures repetibles).
- `Game.Protocol` com a font de veritat (Protocol v1).
- Validació principal en CI (`dotnet test`, canary, smoke).

## Must-have MVP-C1
1. Connectar al servidor i completar handshake amb `ProtocolVersion=1`.
2. Entrar a una zona arena i rebre snapshots/deltas.
3. Renderitzat mínim deterministic (dump textual canònic).
4. Enviar inputs:
   - `MoveTo` o `MoveDir` (segons contracte)
   - `CastPoint(AbilityId, x, y)`
5. Script headless: `connect -> move -> cast -> validar HitEvent`.
6. Contract smoke tests a CI (client headless vs harness/in-proc server).

## Fora d’abast (No MVP-C1)
- UI gràfica real (Godot/Unity/web).
- Predicció/interpolació client.
- Asset pipeline.
- Matchmaking/lobbies.

## Repo/Solució
- Monorepo i solució única.
- Projecte client headless: `src/Game.Client.Headless/`.
- Referències:
  - `Game.Protocol` via `ProjectReference`.
  - Opcional `Game.Server` només per tests/harness in-proc.

## Definition of Done (MVP-C1)
- CI en verd.
- Executable client headless amb flags mínims (`host`, `port`, `script`, `protocol`).
- Handshake v1 i control d’errors de versió.
- Loop de recepció i aplicació de missatges.
- Logs deterministes (sense timestamps de rellotge real).
- Tests `ClientSmoke` en lane ràpida.

## PR Plan suggerit
- **PR-91**: nou projecte + connect/handshake + recepció snapshot.
- **PR-92**: apply snapshot + dump canònic.
- **PR-93**: scripting d’inputs (move/cast) + assertions bàsiques.
- **PR-94**: harness in-proc per smoke CI robust.

## CI guardrails
- **Fast lane**: build + unit + `ClientSmoke` + Canary.
- **Nightly**: soak/replays llargs i bateries completes.
- No afegir tests lents a lane ràpida.

## Riscos a controlar
- Flakiness amb sockets a CI -> prioritzar in-memory/in-proc.
- Divergència de DTOs -> no copiar models; usar `Game.Protocol`.
- Logs no deterministes (timestamps/Guid random) -> evitar.
