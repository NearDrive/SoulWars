# SoulWars -- Design Pillars (Core Systems & Philosophy)

*Last updated: 2026-02-17*

------------------------------------------------------------------------

# 1. World & Narrative Pillars

## 1.1 Perspective Defines Reality

-   The player only sees reality from their faction's perspective.
-   No omniscient narrator exists.
-   The world fracture is experienced firsthand.
-   Absolute truth is never fully revealed.

## 1.2 Order, Power, Entropy

The world is structured around three conceptual forces:

-   **Cel** → Absolute Order, immobilized by balance.
-   **Infern** → Competitive Power, immobilized by distrust.
-   **Void** → Entropy without inherent will.

No system is morally absolute. Each represents a structural philosophy.

## 1.3 Evolution Through Events

The world may evolve gradually over time. Changes must feel organic, not
patch-driven.

------------------------------------------------------------------------

# 2. Combat Philosophy

## 2.1 Active Loops

Each class must have a clear internal loop: - Generation - Conversion -
Decision

Example (Paladin): Damage → Divine Shield → Faith → Skill usage → Loop
reset

## 2.2 No Infinite Scaling

-   All percentage systems have caps (90%).
-   No immortal builds.
-   No easily achievable hard caps.

## 2.3 Identity Over Complexity

Specializations adjust focus, not role. - Tank remains tank. - Offtank
remains tank-oriented.

------------------------------------------------------------------------

# 3. Stat System Philosophy

## 3.1 Six Core Attributes Only

-   CON
-   STR
-   DEX
-   INT
-   WIS
-   VOL

Simplicity over inflation.

## 3.2 Role Separation

  Physical                 Magical
  ------------------------ ---------------------
  STR (damage)             INT (damage)
  DEX (crit rating)        WIS (crit rating)
  CON (physical defense)   VOL (magic defense)

## 3.3 Rating-Based Scaling

All percentage systems (crit, defense, resistances) use the same
logarithmic conversion:

    p(r) = cap * r / (r + K)

-   cap = 90%
-   r = rating
-   K = tuning constant

30% is achievable but requires meaningful investment. 60% requires
advanced equipment. 90% is practically unreachable.

## 3.4 Gear Over Level

-   Levels unlock skills.
-   Gear defines power.
-   Base stat growth per level is moderate.
-   Equipment drives progression.

------------------------------------------------------------------------

# 4. Class Design Pillars

## 4.1 Resource Systems

Classes may use internal resources (Faith, Mana, etc.). Resources
must: - Be generated actively. - Require decisions. - Prevent infinite
spam.

## 4.2 Risk--Reward Mechanics

Specializations must create meaningful trade-offs. Example: Offtank
converts defense into offensive pressure.

------------------------------------------------------------------------

# 5. Equipment System Philosophy

## 5.1 Limited Slots, High Impact

6 Equipment Slots: 1. Main Hand 2. Off Hand 3. Helmet 4. Armor 5.
Accessory 1 6. Accessory 2

Fewer slots → clearer power scaling.

## 5.2 Materials Define Identity

-   Cloth → Magical stats
-   Leather → Crit / Agility stats
-   Plate → Defensive stats

70--80% stat budget from material. 20--30% adjustable via crafting
minigame.

## 5.3 Quality System

Ranks: E, D, C, B, A, S, SS, SSS

-   C = baseline
-   B--A = market standard
-   S+ = extremely rare
-   SSS = exceptional craftsmanship

Quality improves power but does not break balance.

## 5.4 Crafting Skill Matters

Item quality determined by: - Material quality - Minigame performance -
Difficulty taken by the artisan

High skill artisans produce superior gear.

------------------------------------------------------------------------

# 6. Economy Pillars

## 6.1 Market Bell Curve

Most items should fall within B--A range. C remains baseline. S+ remains
aspirational.

## 6.2 Player-Driven Power

Power requires: - Rare materials - Skill - Coordination - Time

No trivial vertical inflation.

------------------------------------------------------------------------

# 7. Scaling Philosophy

## 7.1 Exponential Feel, Controlled Math

Progression must feel exponential while remaining mathematically
controlled.

## 7.2 Hard Caps Protect the System

-   Defense cap: 90%
-   Crit cap: 90%
-   Resist cap: 90%

Caps ensure long-term balance.

------------------------------------------------------------------------

# Core Principle

SoulWars is built on: - Structural simplicity - Emergent depth - Clear
identity - Controlled scaling - Player-driven economy - Meaningful
decisions
