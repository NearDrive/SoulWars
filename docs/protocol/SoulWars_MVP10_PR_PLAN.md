# SoulWars -- MVP10 PR Plan (Combat Core, Server-First, Deterministic)
> **LEGACY**: historical document only. Canonical MVP10 plan is `docs/protocol/SoulWars_MVP10_MasterPlan (1).md`.

> **Legacy notice (governança):** aquest document es manté només com a històric. El source-of-truth vigent d'MVP10 és `docs/protocol/SoulWars_MVP10_MasterPlan (1).md` (OPCIÓ B expandida).

## Context

MVP9 delivered: - Multi-zone deterministic runtime - Canonical tick
order - Per-zone + global checksums - Deterministic transfers -
Multi-zone snapshots - AOI with budgets

MVP10 goal: Introduce a minimal, authoritative, deterministic combat
core compatible with replay + CI checksum verification.

------------------------------------------------------------------------

# Global MVP10 Definition of Done

-   [ ] Combat inputs are deterministic WorldCommands
-   [ ] Server-authoritative validation (range, cooldown, cost, target)
-   [ ] Deterministic resolution order (tick, casterId, sequence)
-   [ ] Combat events included in snapshot
-   [ ] ReplayVerify produces identical checksums and combat event
    streams
-   [ ] Headless bot combat scenario passes in CI

------------------------------------------------------------------------

# PR-53 --- Ability / Skill Model (Data + Runtime Skeleton)

## Scope

Introduce minimal skill system:

SkillDefinition: - SkillId - CooldownTicks - Range - ResourceCost -
TargetType (Self, EntityTarget, Skillshot) - Flags (Melee, Ranged, AoE)

Runtime state per entity: - CooldownTracker

No balance. No animation. No UI.

## Tests

-   Skill_Cooldown_AdvancesDeterministically()
-   Skill_Definition_IsPureData()

## DoD

-   No randomness introduced
-   Replay remains stable

------------------------------------------------------------------------

# PR-54 --- Combat Commands + Validation

## Scope

Add CastSkillCommand: - EntityId Caster - SkillId - Optional
TargetEntityId - Optional AimVector (quantized)

Validation pipeline: - Caster exists - Skill exists - Cooldown ready -
Resource available - Target valid and in range

Invalid commands are rejected deterministically.

## Tests

-   CastSkill_InvalidRejected()
-   CastSkill_ValidAccepted()
-   Same input -\> same validation outcome (2 runs)

------------------------------------------------------------------------

# PR-55 --- Deterministic Targeting Model

## Scope

Target resolution rules:

Targeted: - Direct entity reference

Skillshot: - Quantized direction vector - No floating precision drift

Simultaneous casts resolved by: 1) Tick 2) Caster EntityId asc 3)
Internal sequence asc

## Tests

-   SimultaneousCasts_OrderIsCanonical()
-   Skillshot_VectorQuantization_StableAcrossRuns()

------------------------------------------------------------------------

# PR-56 --- Damage Pipeline + Combat Events

## Scope

Minimal combat components: - HP - BaseAttackPower - BaseDefense -
Optional MagicResist

Damage formula: Deterministic, no RNG.

Introduce CombatEvent: - Tick - SourceEntityId - TargetEntityId -
Amount - Type (Damage, Heal, StatusApplied)

CombatEvents appended to snapshot.

## Tests

-   Damage_Computation_IsStable()
-   CombatEvents_SerializedOrder_IsCanonical()
-   ReplayVerify_CombatScenario_Passes()

------------------------------------------------------------------------

# PR-57 --- Headless Bot Combat Scenario

## Scope

Add simple combat bot: - Move into range - Cast primary skill on
cooldown

Scenario: - 2--4 bots in 1--2 zones - 10--20 seconds simulated

CI Canary: - Record replay - Verify identical checksums + combat events

## Tests

-   BotCombat_ReplayStable()
-   BotCombat_NoEntityDuplicationAcrossZones()

------------------------------------------------------------------------

# Canonical Ordering Rules (Non-Negotiable)

-   Zone iteration: ZoneId asc
-   Session iteration: SessionId asc
-   Entity iteration: EntityId asc
-   Combat resolution: Tick -\> EntityId -\> Sequence
-   Snapshot entity lists: EntityId asc
-   CombatEvents list: deterministic sorted

No unordered Dictionary iteration may influence output.

------------------------------------------------------------------------

# CI Requirements

All of the following must pass in Release:

-   dotnet build -c Release
-   dotnet test -c Release
-   ReplayVerify full suite
-   Multi-zone combat replay stability
-   Restart-from-snapshot stability

------------------------------------------------------------------------

# Result After MVP10

You now have:

-   Deterministic multi-zone MMO core
-   Authoritative combat
-   Skill system foundation
-   Replay-verifiable PvP/PvE combat
-   CI drift protection

At this point, SoulWars becomes mechanically a game without requiring a
graphical client.
