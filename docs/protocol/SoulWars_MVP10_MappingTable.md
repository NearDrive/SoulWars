# SoulWars MVP10 — Mapping Table (MasterPlan vs PR_PLAN legacy)

**Vigent:** `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (PR-54..PR-61).  
**Legacy:** `docs/protocol/SoulWars_MVP10_PR_PLAN.md` (PR-53..PR-57).

| MasterPlan PR | Títol MasterPlan | PR_PLAN PR (si aplica) | Títol PR_PLAN (si aplica) | Relació | Notes |
|---|---|---|---|---|---|
| PR-54 | Skill Model (Data + Runtime State) | PR-53 | Ability / Skill Model (Data + Runtime Skeleton) | Equivalent | Mateix nucli funcional (model de skill + cooldown runtime), amb renumeració. |
| PR-55 | CastSkillCommand + Validation | PR-54 | Combat Commands + Validation | Equivalent | Mateix objectiu: comanda de cast + validació autoritària deterministic. |
| PR-56 | Damage Pipeline + CombatEvents | PR-56 | Damage Pipeline + Combat Events | Equivalent | Mateix bloc funcional (dany deterministic + events en snapshot). |
| PR-57 | Point Targeting (Single Target) | PR-55 | Deterministic Targeting Model | Split | PR_PLAN defineix targeting genèric; MasterPlan el separa en peça concreta de point-target. |
| PR-58 | AoE Multi-Target + Budget | PR-55 | Deterministic Targeting Model | Expanded | Extensió explícita de targeting multi-objectiu + pressupostos de combat. |
| PR-59 | Status Effects (Slow / Stun) | - | - | Missing-in-legacy | No hi ha bloc equivalent al PR_PLAN legacy. |
| PR-60 | Cast Windup + Cancel Rules | - | - | Missing-in-legacy | No hi ha bloc equivalent al PR_PLAN legacy. |
| PR-61 | Line of Sight (Tile-Based) | - | - | Missing-in-legacy | No hi ha bloc equivalent al PR_PLAN legacy. |
| - | - | PR-57 | Headless Bot Combat Scenario | Legacy-only | Escenari de bots definit només al PR_PLAN; al MasterPlan queda absorbit dins CI/replay requirements, sense PR dedicada. |

## Notes de qualitat de mapatge

- Només s'han marcat equivalències quan el text dels dos documents comparteix scope i tests objectivament comparables.
- Quan el PR_PLAN tenia scope més agregat (targeting), s'ha classificat com `Split`/`Expanded` al MasterPlan.
- No s'han introduït PRs noves ni correspondències especulatives fora del text existent.
