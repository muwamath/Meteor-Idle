# Iter 4 — Levels, Progression, and Boss Design

## Overview

A level progression system that turns Meteor Idle from a single-session sandbox into a long-burn idle game. 150 levels to feel powerful. Cores are the universal currency — spent on upgrades AND consumed automatically to advance levels. Boss fights gate every 10th level.

## Core Loop

1. Meteors fall. Player's weapons destroy them. Core voxels spawn CoreDrops.
2. Drones collect CoreDrops and deliver them to the Collector for money.
3. Money accumulates. Player can spend on weapon/drone upgrades at any time.
4. When money reaches the current level's threshold, it's automatically deducted and the player advances.
5. At every 10th level, normal spawning stops. A single boss asteroid spawns. Kill it to advance. Fail and restart the block.

The tension: upgrades and advancement compete for the same pool. Spending on upgrades slows level progression but makes current and future levels easier.

## Level System

### Structure

- **150 levels** to reach "powerful." Progression continues beyond 150 but the 1-150 arc is the designed experience.
- **Blocks of 10.** Levels 1-10 are block 1, 11-20 are block 2, etc.
- **Boss at every 10th level** (10, 20, 30...). Boss levels have no core threshold — kill the boss to advance.
- **Regular levels (non-boss):** automatic advancement when money >= threshold. Threshold is deducted from money on advance.

### Threshold Formula

Exponential growth, tuned so 150 levels feels like a long casual session, not a wall.

```
threshold(level) = baseCost * growthRate ^ (level - 1)
```

Starting values (tuning pass will adjust):
- `baseCost = 10`
- `growthRate = 1.08`

This gives roughly:
- Level 1: 10 cores
- Level 10: ~22 cores
- Level 50: ~470 cores
- Level 100: ~10,700 cores
- Level 150: ~245,000 cores

These numbers need to be balanced against core value scaling so progression feels smooth, not grindy. The tuning pass is a dedicated phase.

### Boss Failure

- Boss meteor reaches the bottom of the screen without being killed.
- All in-flight meteors/projectiles cleared.
- `currentLevel` resets to the start of the current block:
  - Fail boss at level 10 → back to level 1
  - Fail boss at level 20 → back to level 11
  - Fail boss at level 30 → back to level 21
- Money and upgrades are kept. Only level progress is lost.

### Boss Success

- Boss killed → cores rain out (jackpot feel).
- Automatic advancement to first level of next block (11, 21, 31...).
- Normal spawning resumes at the new level's difficulty.

## Difficulty Scaling

Three dials scale with level. Everything starts weak and grows over 150 levels.

### Spawn Rate

- `minInterval` decreases per level (faster spawning at higher levels).
- The existing within-level ramp (`initialInterval` → `minInterval` over 180s) still applies, but both endpoints shift with level.
- Level 1: very slow (maybe 15s initial, 8s min). Level 150: fast and dense.

### Meteor Toughness

- **Size:** Level 1 meteors are 3-6 voxels. Grows with level. By level 50+, full 10x10 grids.
- **Core HP:** Scales with level multiplier on top of the existing size-based scaling.
- **Stone HP:** Same scaling treatment.
- **Core count per meteor:** More cores at higher levels. Level 1: 1 core. Level 50+: 3-4 cores. Level 100+: 5+.

### Core Value

- Scales with level so rewards keep pace with exponential threshold costs.
- Currently hardcoded at `CoreBaseValue = 5`. Becomes `baseValue * levelMultiplier`.
- The multiplier curve must be shallower than the threshold curve — otherwise the player can rush through levels without upgrading.

### What Does NOT Scale

- Material mix (stone/gold/explosive ratios) — stays as-is from Iter 2.
- Meteor fall speed for regular meteors — stays constant. Difficulty comes from quantity and toughness, not reaction speed.
- Drone behavior — drones don't get harder, just the meteors they're collecting from.

## Base Stat Rebalance

All weapon and drone base stats get pulled way back so level 1 feels like poking pebbles.

### Weapons (approximate targets — tuning pass adjusts)

**Missile (base, no upgrades):**
- Speed: ~2 (was 4) — slow travel, you watch it crawl
- Damage: 1 (unchanged) — tiny blast radius
- BlastRadius: 0.05 (was 0.10) — minimal splash
- FireRate: 0.3 (was 0.5) — slow reload
- Homing: 0 (unchanged)

**Railgun (base, no upgrades):**
- FireRate: 0.1 (was 0.2) — very slow charge, 10 seconds between shots
- Speed: 4 (was 6) — visibly slow round
- Weight: 2 (was 4) — shallow tunnel
- Caliber: 1 (unchanged)

**Drone (base, no upgrades):**
- Thrust: lower — sluggish early drones
- Battery: lower — short trips
- Cargo: 1 (unchanged)

### Upgrade Cost Rebalance

All prices are currently flat $1. With 150 levels of progression, upgrade costs need their own exponential curve so early upgrades are cheap (encouraging experimentation) and late upgrades are expensive (competing with level advancement).

The existing `baseCost * costGrowth ^ level` formula in TurretStats/RailgunStats/DroneStats/BayStats already supports this — just need to tune the asset values.

## Boss Asteroid

### Spawn Behavior

- At boss levels (10, 20, 30...), `MeteorSpawner` stops normal spawning.
- A single boss meteor spawns at the top of the screen.
- Falls **very slowly** — dramatically slower than normal meteors. Player has real time to work on it.
- All weapons focus fire on the boss (it's the only target).

### Scaling

- Boss HP and core count scale with block number.
- Block 1 boss (level 10): manageable. ~8x8 grid, extra HP, 4-5 cores. Beatable with minimal upgrades.
- Block 5 boss (level 50): serious. Full grid, high HP, 8+ cores.
- Block 15 boss (level 150): monster. Massive HP, packed with cores. Requires late-game weapons.
- Fall speed gets slightly faster at higher blocks but always stays slow enough to feel like a focused encounter, not a race.

### Visual Treatment

- **Distinct from regular meteors** — darker color palette, red/crimson vein accents through the voxels. Immediately recognizable.
- **Voxel aesthetic maintained** — hard pixels, no smooth effects. The "boss" feel comes from size, color, and the fact that it spawns alone.
- Procedurally generated like all art. New color palette for the generator when `isBoss = true`.

### Death

- Boss killed → all remaining cores spawn as CoreDrops (jackpot shower).
- Drones collect the drops. Money from boss cores counts toward the next level's threshold.
- Brief visual payoff — floating text, maybe a burst of particles. Fun and bombastic.

## Level Progress Strip UI

### Layout

Single horizontal row at the very top of the screen, full width. Five cells visible. The current level is always centered and **3x the width** of neighboring cells. Money display moves directly below the strip.

```
┌─────────┬─────────┬─────────────────────────────────┬─────────┬─────────┐
│  Lv 6   │  Lv 7   │           Lv 8                  │  Lv 9   │ Lv 10 ⚠ │
│ (faded) │ (faded) │  ████████░░░░░  ← green overlay │ (solid) │ (solid) │
│         │         │  rotating target anim            │         │  boss   │
│         │         │         47 / 100                 │         │  icon   │
└─────────┴─────────┴─────────────────────────────────┴─────────┴─────────┘
                              $ 2,847
```

### Cell States

| State | Visual |
|-------|--------|
| **Beaten** (left of current) | Faded/dimmed, muted colors. Clearly "done." |
| **Current** (center) | 3x wide. Vibrant color. Rotating target animation (slow, looping). Green transparent overlay filling left-to-right showing progress toward threshold. Numerical count at bottom of cell (`47 / 100`). |
| **Upcoming** (right of current) | Solid colors, normal brightness. Clearly "next." |
| **Boss (upcoming)** | Same as upcoming + warning/hazard icon. |
| **Boss (current)** | 3x wide, vibrant. Warning icon stays. No progress bar (kill-gated). Text like "DEFEAT THE BOSS" or enlarged boss icon. |

### Scrolling

- Strip scrolls left as the player advances. New current level expands to 3x, old current shrinks and fades.
- Smooth scroll animation on level transition.

### Edge Cases

- **Level 1-2:** Empty space on the left. Strip does not compress or shift — current stays centered with blank space where beaten levels would be.
- **Boss level active:** No progress overlay. Different active cell treatment (boss icon, "DEFEAT" text).
- **After boss kill:** Celebration state briefly, then scroll to next block's first level.

### Render Order

- Strip background is semi-transparent or low sorting order so **meteors visually pass through/over it**. The strip is HUD, not a barrier.

## `LevelState` Singleton

New script, follows the `GameManager` pattern.

### Public API

```
int CurrentLevel { get; }
int CurrentBlock => (CurrentLevel - 1) / 10;
int LevelInBlock => (CurrentLevel - 1) % 10 + 1;  // 1-10
bool IsBossLevel => LevelInBlock == 10;
int Threshold => IsBossLevel ? 0 : CalculateThreshold(CurrentLevel);
int Progress { get; }  // cores collected toward threshold this level
event Action OnLevelChanged;
event Action OnBossSpawned;
event Action OnBossFailed;
```

### Integration Points

- **GameManager.AddMoney:** After adding money, checks `LevelState` threshold. If met, deducts and advances.
- **MeteorSpawner:** Reads `CurrentLevel` for spawn rate, size, HP multipliers. Stops normal spawning during boss levels.
- **Meteor / VoxelMeteorGenerator:** Reads level multipliers for HP, core count, core value.
- **Boss spawn:** `LevelState` signals `OnBossSpawned`, `MeteorSpawner` spawns the boss meteor.
- **Boss death:** `Meteor` (or a new `BossMeteor` subclass) signals kill → `LevelState` advances.
- **Boss failure:** `Meteor` falls offscreen → `LevelState.BossFailed()` → reset to block start.

## What This Iteration Does NOT Include

- Save/load persistence (Future)
- Prestige/reset mechanics (Future)
- Multiple boss types or boss abilities (Future — bosses are just big tough meteors for now)
- Sound effects or music (Future)
- Asteroid rotation (Future)

## Tuning Philosophy

Err on fun and bombastic over realism. The player should feel good about every upgrade. Bosses should feel like events. The 150-level arc should be a slow burn where you notice yourself getting stronger every ~10 levels.

**This iteration ships the system with reasonable defaults, not final tuning.** All scaling curves (threshold, HP, core value, spawn rate, weapon base stats) are exposed as `[SerializeField]` or ScriptableObject fields. The spec provides approximate targets and ratios, not final values. A dedicated tuning iteration (Iter 5 on the roadmap) follows after playtesting the mechanics end-to-end.
