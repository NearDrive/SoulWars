# 📦 SoulWars --- MVP4 PR Plan

**Objectiu:** Hardening + Operabilitat + Contractes\
**Principi rector:** No trencar determinisme ni replay-verify.\
**No s'introdueix economia/crafting.**

------------------------------------------------------------------------

# PR-24 --- Protocol Hardening + Versioning

## 🎯 Objectiu

Formalitzar el contracte de protocol i blindar el servidor davant
clients malformats o versions incompatibles.

## Scope

-   Afegir `ProtocolVersion` al handshake.
-   `ServerCapabilities` al `Welcome`.
-   Rebuig explícit de:
    -   Missatges desconeguts
    -   Payload malformat
    -   Version mismatch
-   No modificar cap lògica de simulació.

## Contractes

### HandshakeRequest

int ProtocolVersion

### Welcome

int ProtocolVersion\
string\[\] ServerCapabilities

## Regles

-   Version mismatch → DisconnectReason.VersionMismatch
-   Missatge desconegut → log + ignore (no crash)
-   Decode error → disconnect net

## Tests

### Unit

-   Decode missatge amb camp extra → no crash
-   Decode missatge truncat → disconnect net
-   Version mismatch → no crea sessió activa

### Integration

-   Client fake envia 10 missatges corruptes → server estable
-   Replay existent continua donant mateix checksum

## DoD

-   Replay-verify passa sense drift
-   CI verd
-   No canvi de checksum en escenaris existents

------------------------------------------------------------------------

# PR-25 --- Input Validation + Rate Limiting

## 🎯 Objectiu

Evitar que un client pugui: - Spamejar intents - Enviar vectors fora de
límits - Enviar payloads desproporcionats

## Scope

-   Clamp de moviment (vector normalitzat + max speed)
-   Max inputs per tick per sessió
-   Max payload size configurable
-   Early reject abans d'entrar a simulació

## Regles

-   Si supera rate → ignore inputs extra
-   Si vector \> max → clamp
-   No generar excepcions

## Tests

### Unit

-   Input amb vector (999,999) → clamp correcte
-   100 inputs en un tick → només N acceptats
-   Payload \> max → reject

### Property / Invariant

-   Tick monotònic sempre
-   Cap entity amb velocitat \> límit
-   Cap NaN a posicions

### Integration

-   Fuzz simple 1k inputs random → server estable

## DoD

-   Replay-verify intacte
-   Cap drift
-   Invariants verds

------------------------------------------------------------------------

# PR-26 --- Observabilitat Headless (Structured Logging + Metrics)

## 🎯 Objectiu

Fer el servidor operable sense UI.

## Scope

-   Logs estructurats (JSON):
    -   tick
    -   sessionCount
    -   messagesIn
    -   messagesOut
    -   simStepMs
-   Mètriques agregades:
    -   Tick p50 / p95
    -   Msg/s
-   Sense dependències externes

## Regles

-   Logging no afecta simulació
-   Cap DateTime.Now dins sim
-   Metrics fora del core loop determinista

## Tests

### Unit

-   Log entry té camps mínims
-   Metrics no modifiquen world state

### Integration

-   Run 500 ticks → log consistent
-   Replay-verify checksum idèntic amb logs activats/desactivats

## DoD

-   Logs activables per config
-   CI confirma checksum igual amb logs ON/OFF

------------------------------------------------------------------------

# PR-27 --- Deterministic Soak Runner

## 🎯 Objectiu

Test d'estrès determinista a CI.

## Scope

-   Runner headless:
    -   N bots
    -   T ticks virtuals (no wall-clock)
    -   Snapshot cada X ticks
-   Assertions:
    -   Invariants world
    -   No memory growth descontrolat
    -   Checksum final estable

## Regles

-   RNG seed fixa
-   Cap dependència de temps real
-   Mateix escenari → mateix checksum

## Tests

### Integration

-   50 bots
-   10.000 ticks
-   Checksum igual en 2 execucions consecutives

### Invariants

-   Cap entity duplicada
-   Tick monotònic
-   Posicions finites
-   Sessions consistents

## CI Gate

-   Falla si checksum drift
-   Falla si invariant trenca

## DoD

-   Soak job afegit a pipeline
-   100% determinista

------------------------------------------------------------------------

# PR-28 --- Extended Invariants + Persistence Consistency

## 🎯 Objectiu

Blindar consistència world + persistència.

## Scope

-   Invariants addicionals:
    -   No entity orphan
    -   No referències nul·les
    -   ID únic global
-   Test Save → Load → Continue
-   Test Restart → Load from SQLite

## Regles

-   Persistència no altera tick
-   IDs no es regeneren
-   Snapshot hash estable

## Tests

### Integration

1.  Run 500 ticks
2.  Save
3.  Restart server
4.  Load
5.  Continue 500 ticks
6.  Checksum = run 1000 ticks directes

### Invariants

-   Entity count estable
-   Cap duplicat
-   Audit log coherent

## DoD

-   Replay-verify passa amb persistència
-   Restart consistent
-   CI amb test de persistència obligatori

------------------------------------------------------------------------

# 📌 MVP4 Definition of Done Global

-   [ ] Cap drift en replay-verify
-   [ ] Soak runner estable
-   [ ] Persistència consistent
-   [ ] Protocol versioned
-   [ ] Input blindat
-   [ ] Logs estructurats
-   [ ] CI bloqueja regressions

------------------------------------------------------------------------

# 🔒 Filosofia MVP4

MVP3 et dona persistència i identitat.\
MVP4 et dona resistència, observabilitat i confiança operativa.
