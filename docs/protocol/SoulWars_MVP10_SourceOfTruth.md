# SoulWars MVP10 — Source of Truth (OPCIÓ B expandida)

## Declaració oficial (vigent)

A partir d'aquest punt, **MVP10 oficial** queda definit per:

- `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (**vigent**, source-of-truth) — PR-54..PR-61.

El document següent queda com a **històric / legacy**:

- `docs/protocol/SoulWars_MVP10_PR_PLAN.md` (**legacy**, no vigent) — PR-53..PR-57.

## Regles anti-drift (obligatòries)

1. **Una sola font de veritat per MVP10:** qualsevol canvi de scope/DoD/PR ordering d'MVP10 s'ha de fer només al MasterPlan vigent.
2. **El PR_PLAN legacy no es reescriu funcionalment:** només s'hi admeten notes de context històric.
3. **Traçabilitat obligatòria:** qualsevol PR/issue que parli d'MVP10 ha de referenciar PRs del MasterPlan (PR-54..PR-61).
4. **Mapatge únic:** relacions entre legacy i vigent només es documenten a `docs/protocol/SoulWars_MVP10_MappingTable.md`.
5. **DoD oficial únic:** la validació de tancament d'MVP10 es basa en `docs/protocol/SoulWars_MVP10_DoD_Strict.md`.
6. **Estat operatiu únic:** l'estat MVP1→MVP10 es reporta a `docs/protocol/PROJECT_STATUS_SNAPSHOT.md`.

## Resolució del conflicte documental MVP10

- Conflicte detectat: coexistència de PR-53..57 (legacy) i PR-54..61 (MasterPlan) per MVP10.
- Resolució adoptada: **OPCIÓ B (expandida)** = es manté el MasterPlan com a contracte oficial i el PR_PLAN anterior com a artefacte històric.
