# SoulWars -- MVP10 Combat Core (Server-First, Deterministic)

## Context

MVP9 delivered: - Multi-zone deterministic runtime - Canonical tick
order - Per-zone + global checksums - Deterministic transfers -
Multi-zone snapshots - AOI with budgets - CI invariants + golden canary
replay

MVP10 introduces the first complete combat layer, fully authoritative
and replay-safe.

------------------------------------------------------------------------

# MVP10 Global Objective

Deliver a deterministic combat system that:

-   Processes combat inputs as WorldCommands
-   Resolves effects server-side only
-   Produces canonical CombatEvents
-   Is replay-verifiable and CI-protected
-   Remains 100% fixed-point (Fix32 Q16.16)

No floats. No RNG. No non-deterministic ordering.

------------------------------------------------------------------------

# Global Definition of Done

-   [ ] All combat inputs are deterministic WorldCommands
-   [ ] Validation pipeline is authoritative
-   [ ] Canonical ordering enforced (Tick → CasterId → Sequence)
-   [ ] CombatEvents included in snapshot
-   [ ] ReplayVerify stable across runs
-   [ ] Canary replay still passes
-   [ ] No float introduced in runtime loop

------------------------------------------------------------------------

# PR-54 --- Skill Model (Data + Runtime State)

## Scope

Introduce immutable SkillDefinition:

-   SkillId
-   CooldownTicks
-   RangeRaw (Fix32.Raw)
-   ResourceCost
-   TargetType (Self / Entity / Point)
-   Flags

Runtime:

-   SkillCooldownsComponent per entity
-   Deterministic TickDown()

## Tests

-   SkillCooldown_TicksDownDeterministically()
-   SkillDefinition_IsPureData()

------------------------------------------------------------------------

# PR-55 --- CastSkillCommand + Validation

## Scope

Add CastSkillCommand:

-   CasterId
-   SkillId
-   TargetKind
-   TargetEntityId (if Entity)
-   TargetPosXRaw/YRaw (if Point)

Validation:

-   Caster exists
-   Skill exists
-   Cooldown ready
-   Resource sufficient
-   Target valid
-   Range check via Fix32 (distSq \<= range²)

Canonical resolution order enforced.

## Tests

-   CastSkill_OutOfRange_Rejected()
-   CastSkill_InRange_Accepted()
-   SameTick_OrderIsCanonical()

------------------------------------------------------------------------

# PR-56 --- Damage Pipeline + CombatEvents

## Scope

Add:

-   HealthComponent
-   CombatStatsComponent
-   Deterministic damage formula
-   CombatEvent (Damage / Heal)

Snapshot now includes CombatEvents\[\] (ordered).

## Tests

-   Damage_Computation_Deterministic()
-   CombatEvents_CanonicalOrder()
-   ReplayVerify_CombatScenario_Passes()

------------------------------------------------------------------------

# PR-57 --- Point Targeting (Single Target)

## Scope

Resolve nearest entity to point (Fix32):

-   Filter distSq \<= hitRadius²
-   Sort by distSq asc → EntityId asc
-   Select first

Apply PR-56 pipeline.

## Tests

-   PointTarget_SelectsNearest_Deterministic()
-   NoTargetInRadius_Rejected()
-   ReplayVerify_PointScenario_Passes()

------------------------------------------------------------------------

# PR-58 --- AoE Multi-Target + Budget

## Scope

-   Select all entities in radius
-   Sort by distSq asc → EntityId asc
-   Apply MaxTargets budget
-   Emit ordered CombatEvents

Optional global MaxCombatEventsPerTick safeguard.

## Tests

-   Aoe_SelectsSortedTargets()
-   Aoe_RespectsMaxTargets()
-   ReplayVerify_AoeScenario_Passes()

------------------------------------------------------------------------

# PR-59 --- Status Effects (Slow / Stun)

## Scope

Add:

-   StatusEffectType (Slow, Stun)
-   StatusEffectInstance
-   Deterministic stacking rules
-   Movement + Cast blocking integration
-   StatusEvents in snapshot

Stacking rules must be explicit and documented.

## Tests

-   Stun_BlocksMovementAndCast()
-   Slow_StrongestWins_Deterministic()
-   StatusEvents_StableAcrossRuns()

------------------------------------------------------------------------

# PR-60 --- Cast Windup + Cancel Rules

## Scope

Add:

-   CastTimeTicks
-   PendingCastComponent
-   ExecuteTick logic
-   Deterministic cancel rules
-   Optional GCD

Cooldown begins at execution (recommended).

## Tests

-   CastTime_DelaysExecution()
-   PendingCast_CancelledByStun()
-   Windup_OrderCanonical()

------------------------------------------------------------------------

# PR-61 --- Line of Sight (Tile-Based)

## Scope

Add deterministic LoS using Bresenham integer grid:

-   RequiresLineOfSight flag in SkillDefinition
-   HasLineOfSight(from, to, map)
-   Tile-based blocking

Optional MaxLosTilesPerCheck safeguard.

## Tests

-   LoS_ClearPath_ReturnsTrue()
-   LoS_BlockedBySolid_ReturnsFalse()
-   ReplayVerify_LoSScenario_Passes()

------------------------------------------------------------------------

# Determinism Rules (Non-Negotiable)

-   All math in Fix32 or int
-   No MathF / float comparisons
-   All collections sorted before snapshot emission
-   Canonical order:
    -   Zones → ZoneId asc
    -   Sessions → SessionId asc
    -   Entities → EntityId asc
    -   Commands → Tick → CasterId → Seq
    -   CombatEvents → deterministic sorted

------------------------------------------------------------------------

# CI Requirements

All must pass under:

dotnet test -c Release --filter "Category!=Soak"

Including:

-   Canary replay
-   Combat replay scenarios
-   Snapshot roundtrip
-   Restart stability

------------------------------------------------------------------------

# Result After MVP10

After completing MVP10, SoulWars will have:

-   Fully deterministic combat core
-   Multi-zone authoritative combat
-   Skill system foundation
-   AoE + status effects
-   Windup + cancel mechanics
-   LoS terrain interaction
-   Replay-verified PvP/PvE stability

At this point, the game is mechanically playable in headless mode.
