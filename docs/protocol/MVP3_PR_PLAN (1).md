# SoulWars --- MVP3 PR Plan

## 🎯 Objectiu MVP3

Passar de "combat sandbox determinista" (MVP2) a un **món persistent
autoritari**, mantenint:

-   Determinisme total
-   Replay verify a CI
-   Server autoritari
-   Cap dependència de UI
-   Tests + invariants

MVP3 introdueix persistència real sense trencar el model determinista.

------------------------------------------------------------------------

# 🧱 MVP3 Roadmap (PR-19+)

Ordre recomanat per minimitzar risc:

1.  Persistència binària determinista (save/load)
2.  Identitat persistent de player
3.  Reconnect estable
4.  DB backing (SQLite inicialment)
5.  Audit / anti-dupe base

------------------------------------------------------------------------

# PR-19 --- Save/Load determinista del WorldState

## Objectiu

Implementar serialització binària estable del WorldState complet:

-   Zones
-   Entities
-   Components (Position, Health, Combat, AI)
-   Seeds necessàries

## Requisits

-   SaveWorldState(Stream)
-   LoadWorldState(Stream)
-   Save → Load → Continue =\> mateix checksum que continuar sense
    guardar
-   Format binari estable (no JSON)

## Tests

-   SaveLoad_RoundTrip_EqualsOriginalChecksum()
-   Stress_SaveLoad_MidSimulation_Deterministic()

------------------------------------------------------------------------

# PR-20 --- Player Identity persistent

## Objectiu

Separar SessionId de PlayerId persistent.

-   PlayerId estable
-   AccountId (string o guid)
-   Map Session → Player

## Requisits

-   Reconnect recupera entity existent (si viu)
-   Si mort, respawn determinista
-   Identitat no depèn de SessionId

## Tests

-   Connect → Enter → Disconnect → Reconnect =\> mateixa entity
-   Checksum estable entre runs

------------------------------------------------------------------------

# PR-21 --- Reconnect + state continuity

## Objectiu

Permetre que un client es desconnecti i torni a connectar sense
corrompre estat.

## Requisits

-   Entity no es duplica
-   No doble assignació
-   Snapshots correctes després de reconnect

## Tests

-   Multi reconnect scenario determinista
-   No duplicate entity across sessions

------------------------------------------------------------------------

# PR-22 --- SQLite backing (persistència real)

## Objectiu

Guardar estat persistent fora de memòria:

-   Players
-   LastZone
-   LastPosition
-   World seed/config

## Regles

-   DB no afecta tick runtime
-   Només afecta inicialització (load)

## Tests

-   Save DB → Restart server → Load DB → Checksum consistent

------------------------------------------------------------------------

# PR-23 --- Audit trail bàsic (anti-dupe preparació)

## Objectiu

Registrar events crítics:

-   EnterZone
-   Teleport
-   Death
-   Spawn

Sense economia encara.

## Requisits

-   Event log append-only
-   Determinista
-   Reproducible

## Tests

-   Replay from audit log =\> mateix checksum

------------------------------------------------------------------------

# 🎯 Definició formal de MVP3 DONE

MVP3 es considera complet quan:

-   Save/Load world determinista funciona
-   Players tenen identitat persistent
-   Reconnect estable
-   Estat pot persistir via SQLite
-   Replay verify segueix passant
-   CI verd

------------------------------------------------------------------------

# ❌ Fora de MVP3

-   Economia
-   Inventari
-   Crafting
-   Loot tables
-   Trading
-   Sharding distribuït
-   Escalat horitzontal

------------------------------------------------------------------------

# 📈 Arquitectura després de MVP3

  Layer         Estat
  ------------- ---------------------------
  Core sim      Determinista + persistent
  Server        Multi-zone + reconnect
  Persistence   SQLite-backed
  Replay        CI gate intacte
  Bots          Combat agents persistents

MVP3 converteix SoulWars en un MMO headless persistent real.
