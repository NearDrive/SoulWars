# 📦 SoulWars --- MVP7 PR Plan

## Risk → Reward → Loss Vertical Slice (Server‑First, Determinista)

**Principi rector:**\
No trencar determinisme.\
No trencar replay‑verify.\
Servidor autoritari.\
Cap dependència de UI.

------------------------------------------------------------------------

# 🎯 Objectiu MVP7

Construir el **primer loop jugable real** del joc:

> Combat → Mort → Drop → Loot → Risc per zona → Possible pèrdua real

Sense economia global encara.\
Sense crafting complet encara.

Aquest MVP valida que el joc té tensió sistèmica abans d'afegir
complexitat.

------------------------------------------------------------------------

# 📜 PR-36 --- Zone Definitions manuals + Loader + Invariants

## Objectiu

Permetre zones 100% definides a mà via fitxers versionats al repo.

## ZoneDefinition (contracte de dades)

-   ZoneId
-   StaticObstacles (AABB)
-   NpcSpawns\[\]
    -   NpcArchetypeId
    -   Count
    -   Level
    -   SpawnPoints\[\] (coords fixes)
-   LootRules (placeholder per PR-37)

## Regles

-   Cap RNG per decidir què spawneja
-   Ordre estable d'IDs: (ZoneId, ArchetypeId, index)
-   Fail-fast si definició mal formada

## Tests

-   LoadZoneDefinitions_StableHash()
-   SpawnFromManualDefs_DeterministicChecksum()
-   Restart_NoDuplicateEntities()

## DoD

-   Replay-verify intacte
-   Soak estable
-   Checksum no canvia

------------------------------------------------------------------------

# 📜 PR-37 --- Loot determinista minimal

## Objectiu

Afegir drops deterministes en morir un NPC.

## Regles

-   Drop table fixa per Archetype
-   Sense RNG encara
-   EntityDeath → spawn LootEntity
-   LootIntent per recollir

## Invariants

-   Loot no es duplica
-   Loot desapareix en recollir
-   No loot orphan després restart

## Tests

-   Mateix combat → mateix loot
-   Replay checksum estable

------------------------------------------------------------------------

# 📜 PR-38 --- Inventory minimal server-side

## Objectiu

Implementar inventari autoritari.

## Components

-   InventoryComponent
-   Capacity fixa
-   StackLimit fix

## Regles

-   Snapshot inclou inventory hash
-   No stack \> max
-   No item orphan
-   No dupes via reconnect

## Tests

-   Loot → inventory correcte
-   Restart → inventory intacte
-   SaveLoad → checksum estable

------------------------------------------------------------------------

# 📜 PR-39 --- Death Penalty real

## Objectiu

Mort amb conseqüència real.

## Regles

-   Player death:
    -   Drop inventory complet
    -   Respawn determinista
-   Audit log registra death + drop

## Tests

-   Kill → inventory buida
-   Altres bots poden loot
-   No duplicació per reconnect

------------------------------------------------------------------------

# 📜 PR-40 --- Risk Gradient per Zones (manual)

## Objectiu

Crear gradient real de perill via definicions manuals.

## Exemple conceptual

Zona Safe: - 2 mobs lvl 1

Zona Mid: - 8 mobs lvl 5

Zona Hot: - 15 mobs lvl 12

Tot definit manualment via ZoneDefinition.

## Tests

-   Mateix seed + mateixes defs → mateix checksum
-   Zones no interfereixen
-   Spawn dins bounds

------------------------------------------------------------------------

# 📜 PR-41 --- Deterministic Vendor Stub (No Economy)

## Objectiu

Preparar infra de futur sense economia real.

## Scope

-   Vendor fix per zona
-   Preus fixos
-   Compra/Venda sense RNG
-   Audit obligatori

## Regles

-   No trading entre jugadors
-   No mercat global
-   Sense fluctuacions

## Tests

-   Compra determinista
-   Restart consistent
-   Replay intacte

------------------------------------------------------------------------

# 📜 PR-42 --- Anti-Dupe Extended Invariants

## Objectiu

Blindar consistència world + inventory + loot.

## Invariants addicionals

-   ID global únic
-   No entity orphan
-   No item referenciat per 2 inventories
-   No loot entity invisible persistent

## Tests

-   Save → Load → Continue = mateix checksum
-   Kill + Loot + Restart = consistent
-   Fuzz intents de loot → no crash

------------------------------------------------------------------------

# 🎯 MVP7 Definition of Done

-   Zones manuals funcionals
-   Loot determinista
-   Inventari autoritari
-   Mort amb pèrdua real
-   Gradient de risc real
-   Vendor stub operatiu
-   Replay-verify intacte
-   Soak runner estable
-   CI bloqueja qualsevol drift

------------------------------------------------------------------------

# 🔒 Filosofia MVP7

MVP6 = Infra robusta\
MVP7 = Primer loop real del joc

Si MVP7 funciona, el joc té tensió sistèmica.\
Si no funciona, millor descobrir-ho ara que després d'implementar
economia complexa.
