# 🚀 SoulWars --- MVP8: Drift Diagnostics + Persistence Guardrails

## 🎯 Objectiu General

Després de l'MVP7 (zones manuals, loot determinista, inventari
autoritari, mort amb pèrdua real, vendor stub, replay estable),
l'objectiu de l'MVP8 és **maximitzar la seguretat per desenvolupament
amb Codex**.

Aquest MVP NO afegeix gameplay nou. Aquest MVP afegeix:

-   Diagnòstic determinista de divergències
-   Enduriment de persistència
-   Guardrails forts a CI
-   Artefactes útils quan falla el checksum

Tot 100% headless i determinista.

------------------------------------------------------------------------

# 📦 PR-43 --- TickReport Determinista (Read‑Only Instrumentation)

## Objectiu

Afegir un report estructurat per tick/snapshot que permeti detectar el
primer punt de divergència.

## Output per tick

-   Tick
-   WorldChecksum
-   SnapshotHash (si aplica)
-   EntityCountByType
-   LootCount
-   InventoryTotals (sumatori per ItemId)
-   WalletTotals

## Regles

-   No entra al checksum
-   No modifica world state
-   Tot ordenat canònicament

## Tests

-   TickReport_DoesNotAffectChecksum()
-   TwoRuns_SameTickReport()

------------------------------------------------------------------------

# 📦 PR-44 --- ReplayRunner Artefactes en cas de Divergència

## Objectiu

Quan `ReplayVerify` falla:

-   Escriure expected_checksum.txt
-   Escriure actual_checksum.txt
-   Escriure tickreport_expected.jsonl
-   Escriure tickreport_actual.jsonl
-   Detectar primer tick divergent

## Regles

-   Només s'executa si hi ha mismatch
-   No afecta execució normal
-   No altera checksum

## Tests

-   ReplayVerify_Mismatch_EmitsArtifacts()

------------------------------------------------------------------------

# 📦 PR-45 --- Snapshot Guardrails

## Objectiu

Endurir el sistema Save/Load existent.

## Millores

-   Checksum-on-load (recalcular i comparar amb el guardat)
-   SnapshotMeta:
    -   SerializerVersion
    -   ZoneDefinitionsHash
    -   ConfigHash
    -   Optional BuildHash

## Fail-Fast

Si el checksum no coincideix → excepció clara.

## Tests

-   LoadFromSqlite_ChecksumMatchesStored()
-   LoadFromSqlite_BadChecksum_Fails()

------------------------------------------------------------------------

# 📦 PR-46 --- Migració Versionada Testejada

## Objectiu

Garantir que snapshots antics (v1/v2/v3) migren correctament a v4.

## Accions

-   Afegir fixtures de versions antigues
-   Test matrix migracions

## Tests

-   Load_OldVersions_Migrates_And_SameChecksum()
-   Restart_FromMigratedSnapshot_NoIdDrift()

------------------------------------------------------------------------

# 🧪 Definition of Done MVP8

-   Replay mismatch genera artefactes útils
-   Checksum validat en Load()
-   Migracions cobertes amb fixtures reals
-   TickReport no altera determinisme
-   Soak estable
-   CI falla si hi ha drift no justificat

------------------------------------------------------------------------

# 🔐 Impacte Estratègic

Després d'aquest MVP:

-   Qualsevol drift es detecta al tick exacte
-   Qualsevol corrupció de snapshot falla immediatament
-   Qualsevol canvi de serializer/migració és segur
-   Codex pot continuar afegint features amb risc mínim

Aquest MVP és la base per: - AOI avançat - Combat complex - Escalat real
MMO

Sense aquest blindatge, qualsevol feature futura incrementa risc
exponencialment.
