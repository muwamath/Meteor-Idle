# Meteor Idle — Iteration Roadmap

Living document. Last revised: 2026-04-11 (Iter 0 shipped).

This is the ordered plan for the next several iterations, pulled from `todo.md` (sections above `# Future`). Each iteration is a branch (`iter/<name>`) with its own spec + plan when it's large enough to warrant one. Sized for the project's per-branch overhead (tests, play verify, code-reviewer, WebGL deploy).

## Sizing rule of thumb (applies to every iteration below)

**A phase should touch ≤ 3 files or ≤ ~200 lines of code + tests. If a phase would exceed that, split it.**

This is the hard constraint that shapes phase counts below. When estimates feel "one big phase," I've split it into two or three smaller phases to respect the rule. Phase counts are therefore higher than a naive estimate would suggest — that's intentional.

## Iteration ordering and dependencies

```
Iter 0: Aim fixes (Bundle A)           ──┐
                                         │  independent, quick win
Iter 1: Asteroid cores                 ──┼──┐
                                         │  │  cores are the substrate
Iter 2: Asteroid types                 ──┤  │  rides on cores
                                         │  │
Iter 3: Economy rework (drones)        ──┤  │  needs cores (cores float, drones collect)
                                         │  │
Iter 4: Levels / progression / boss    ──┘  │  needs cores (boss core) + economy (reward curve)
                                             │
                              ── ship order ─┘
```

Order rationale:
- **Iter 0** is a half-day quick win, unblocks visible bugs, no dependencies.
- **Iter 1 (cores)** is the architectural substrate that Iters 2/3/4 all depend on.
- **Iter 2 (types)** is cheap once cores exist, adds variety before the economy rework lands.
- **Iter 3 (economy)** must land before levels because the level reward curve assumes drone-gated money flow. Doing levels first means tuning levels twice.
- **Iter 4 (levels)** is last and assumes everything before it.

---

## Iter 0 — Aim fixes (Bundle A) ✅ shipped 2026-04-11

**Branch:** `iter/aim-fixes`
**Size:** half-day, no formal spec needed
**Depends on:** nothing

### Goal

Fix two visible targeting problems:
1. Railgun doesn't lead moving meteors — rounds trail behind and miss (see screenshot in conversation 2026-04-11).
2. Turrets only target meteors within a hardcoded range; they should aim at any on-screen asteroid.

### Scope

- `TurretBase.FindTarget` — widen target pool to anything on-screen.
- `TurretBase` aim logic + `RailgunTurret` — compute lead/intercept for straight-line weapons given meteor fall velocity and round speed.
- Missiles are unaffected because `Homing` compensates mid-flight. (If Homing=0, the intercept fix applies to missiles too — free win.)
- EditMode tests for the intercept math (pure function, trivial to test).

### Phases (3, respecting the sizing rule)

1. **Intercept math + unit tests.** New static helper (maybe `AimSolver.LeadTarget` or inline in `TurretBase`) + EditMode test covering stationary, straight-falling, edge case where round is slower than target. ~1 file + 1 test file.
2. **Wire intercept into `TurretBase`/`RailgunTurret` aim + remove range cap in `FindTarget`.** Play verify the railgun actually hits drifting meteors and that turrets lock onto anything on-screen. ~2 files.
3. **Code-reviewer agent + manual play verify + merge.** Per the "code review before final verification" rule.

### Manual play-verify gates

- Railgun at base fire-rate hits a falling meteor more than 90% of the time at mid-screen.
- Both weapons lock onto a meteor near the top edge of the screen, not just near the turret.
- No regressions on missile homing or railgun tunneling.

---

## Iter 1 — Asteroid cores

**Branch:** `iter/asteroid-cores`
**Size:** full spec + plan, ~6–8 phases
**Depends on:** Iter 0 (so aim fixes ship first and the "weapons aim at cores" requirement lands cleanly on top)

### Goal

Asteroids have a **core** (1+ voxels) surrounded by **dirt**. Dirt destruction is free; money comes from the core. Core voxels are visually distinct, harder to break (HP > 1), and weapons prefer to target core voxels.

### Scope

- Voxel grid: `bool[10,10]` → `VoxelKind[10,10]` (or parallel `int[,] hp`). Kinds: `Empty`, `Dirt`, `Core`.
- `VoxelMeteorGenerator.Generate` picks core cell(s) at center-ish region, surrounds with dirt per seed.
- Per-voxel HP: dirt = 1, core = N (configurable). `ApplyBlast`/`ApplyTunnel` decrement HP; only transition to `Empty` when HP hits 0.
- Visuals: core cells use a distinct color palette that reads clearly against dirt. Follows the voxel aesthetic rule — flat blocks, hard edges, 1-px dark outline.
- Targeting: `PickRandomPresentVoxel` → `PickCoreVoxel` (fallback to any live if no core left). Missiles home to core; railgun aims through dirt to hit core.
- Economy: `Meteor.ApplyBlast`/`ApplyTunnel` return `(destroyedDirt, destroyedCore)`. Money only paid for core destruction. (Exact payout formula locked in Iter 3.)
- Tests: every existing `Meteor*Tests.cs` file needs updates. New tests for core HP, core targeting, core-only payout.

### Phases (7, respecting the sizing rule)

1. **Voxel kind enum + grid representation + generator change.** `VoxelMeteorGenerator` and `Meteor` grid storage. EditMode tests for determinism and core placement. ~3 files.
2. **HP model + `ApplyBlast` HP decrement.** `Meteor.ApplyBlast` walks the blast circle, decrements HP, only clears on 0. Tests for multi-hit kills. ~2 files.
3. **`ApplyTunnel` HP decrement + weight-budget interaction.** Railgun tunneling consumes weight on *damage dealt*, not voxel-cleared (design call to confirm in spec). Tests for partial core destruction. ~2 files.
4. **Core visuals — procedural art + renderer paint.** Update the 15×15 block paint in `Meteor` so core cells pull from a core palette. Procedural PNG regen via `execute_code`. Visual verify (read the generated PNG, pause for user inspection per the voxel aesthetic rule). ~2 files + art assets.
5. **Targeting picks core voxels.** `Meteor.PickCoreVoxel`, `MissileTurret.Fire` uses it, `RailgunTurret` aim uses it. Tests for core-priority, dirt-fallback when coreless. ~3 files.
6. **Economy split (dirt=0, core=value).** `Meteor` returns core-destroyed count, missile/railgun payout uses that. Tests updated. Tuning pass so the game still feels playable with the new flow. ~3 files.
7. **Code-reviewer agent + manual play verify + merge + WebGL deploy.**

### Manual play-verify gates

- Core voxels are immediately, obviously distinguishable from dirt at a glance.
- Missiles home to core through dirt; railgun tunnels to core.
- Destroying a meteor still pays out; destroying only dirt pays nothing (placeholder for Iter 3's floating-core change).
- No test regressions in either suite.

---

## Iter 2 — Asteroid types

**Branch:** `iter/asteroid-types`
**Size:** lightweight plan, ~4 phases
**Depends on:** Iter 1 (cores exist, HP model exists)

### Goal

Multiple asteroid "materials" — ice, fire, metal, gold, hardened — that reskin + retune the base cores-and-dirt model. Different payouts, different HP, different visuals. No new behavior verbs, just variants.

### Scope

- `AsteroidType` ScriptableObject (or enum + data table): color palette, HP multiplier, core-value multiplier, spawn weight.
- `VoxelMeteorGenerator` rolls type per seed and returns it alongside the grid.
- `Meteor` applies the type's palette + HP + payout multiplier at `Spawn`.
- `MeteorSpawner` picks types weighted by level (weight is trivially `level → type weights` for now, deeper tuning in Iter 4).
- Tests: generator determinism for each type, payout math.

### Phases (4, respecting the sizing rule)

1. **`AsteroidType` data asset + enum + stub data for 5 types.** Create the assets, wire into `VoxelMeteorGenerator`. Tests for deterministic type roll. ~3 files.
2. **Palette + HP application in `Meteor.Spawn`.** Visual verify each type's palette (pause for user inspection). ~2 files.
3. **Spawn-weighted picking + initial tuning.** `MeteorSpawner` picks types, early levels biased toward plain/ice, metal/gold rarer. ~2 files.
4. **Code-reviewer + play verify + merge + WebGL deploy.**

### Manual play-verify gates

- Each of the 5 types is visibly distinct.
- Gold feels rare and rewarding; hardened feels genuinely harder to kill.
- Frame rate holds with mixed types on screen.

---

## Iter 3 — Economy rework (drones + floating cores)

**Branch:** `iter/drone-economy`
**Size:** full spec + plan, ~10 phases
**Depends on:** Iter 1 (cores), Iter 2 optional

### Goal

Kill the "instant money on voxel break" flow. When a core is destroyed, it doesn't pay — it **floats slowly down the screen**, and a **collector drone** flies out from base, picks it up, evades live meteors on the way, and brings it back. Money is paid on drop-off at base.

### Scope

- New `CoreDrop` entity — physics-free floating object, slow downward drift, sprite = dead core visual.
- New `CollectorDrone` entity — pathing from base to target `CoreDrop`, evasion against live `Meteor` colliders, pickup, return to base, deposit. Voxel-aesthetic sprite with puff-of-gas particles.
- Drone upgrades: speed, capacity, cooldown between missions. New `DroneStats` ScriptableObject + `DroneUpgradePanel` UI.
- Base visualization: two drones sit between the three weapon slots. `SlotManager` or a new `DroneBay` owns them.
- Meteor → drop flow: `Meteor.ApplyBlast`/`ApplyTunnel` on a core-kill spawns a `CoreDrop` at the core's world position instead of paying directly.
- Starting state: player always has one drone. Drones are not sellable.
- Tests: EditMode for pathing math, upgrade formulas; PlayMode for the full pickup-return-deposit loop.

### Phases (10, respecting the sizing rule)

1. **`CoreDrop` entity + pool + slow downward drift.** No visuals beyond a placeholder color. Core-kill in `Meteor` spawns a `CoreDrop` instead of paying. EditMode test for drift math. ~3 files.
2. **`CollectorDrone` MonoBehaviour — straight-line pathing + pickup state machine (Idle → Seek → Carry → Return → Deposit → Idle).** No evasion yet. Deposit calls `GameManager.AddMoney`. EditMode test on the state machine. ~2 files.
3. **Evasion against live meteors.** Simple steer-away within a safety radius. EditMode test for avoidance math, PlayMode smoke test. ~2 files.
4. **Drone visuals (voxel-style sprite) + puff-of-gas particles.** Procedural PNG generation. Visual verify pause. ~2 files + art.
5. **`DroneStats` ScriptableObject + stats formulas (speed, capacity, cooldown).** Mirror the `TurretStats`/`RailgunStats` pattern. EditMode tests for `NextCost`/`CurrentValue`/`ApplyUpgrade`. ~2 files.
6. **`DroneUpgradePanel` UI + click routing.** Opens from clicking a drone in the base bay. Follows the `MissileUpgradePanel`/`RailgunUpgradePanel` pattern. ~3 files.
7. **`DroneBay` / integration into `SlotManager`.** Two drones sit visually between the weapon slots. Cooldowns gate new missions. ~2 files.
8. **Tuning pass — starting money flow feels slow but satisfying.** Data-only changes. Manual play verify.
9. **PlayMode end-to-end test:** spawn meteor → kill core → drop floats → drone picks up → drone deposits → money increases. ~1 new test file.
10. **Code-reviewer + manual play verify + merge + WebGL deploy.**

### Manual play-verify gates

- Money only goes up when a drone deposits — never on voxel break.
- Drones visibly dodge meteors on the way to pickups.
- Drone upgrades meaningfully change money-per-minute curve.
- The "early game is slow, mid game accelerates" feel is present.
- No frame drops with 2 drones + 20 meteors + 10 drops on screen.

---

## Iter 4 — Levels / progression / boss

**Branch:** `iter/levels`
**Size:** full spec + plan, ~8 phases
**Depends on:** Iter 1 (core-bearing boss), Iter 3 (money curve to tune against)

### Goal

A level progression system. Levels group into blocks of 10, every 10th level is a boss asteroid. Beating the boss advances the block. Failing the boss teleports you to the start of the block to farm. Player can manually step levels back/forward via top-bar arrows.

### Scope

- `LevelState` singleton (or ScriptableObject + runtime) — current level, current block, kill count toward next level.
- Top-bar UI — level number with left/right arrows. Left always works (farm easier); right is gated by kill count.
- `MeteorSpawner` reads level to scale cadence, count, HP, core value.
- Boss asteroid — special `AsteroidType` with huge HP, giant grid, multi-core. Spawns alone at level-10-of-block. Failure = teleport to block start.
- Kill-count gating — N core-kills to advance a level. N scales by level.
- Tests: level progression math, boss spawn gating, fallback teleport.
- Still no persistence (that's in `# Future`).

### Phases (8, respecting the sizing rule)

1. **`LevelState` + current-level spawner scaling (cadence, HP, core value).** No UI yet. EditMode tests for scaling curves. ~3 files.
2. **Top-bar UI with left/right arrows + level label.** Manual level-step buttons wired to `LevelState`. ~2 files.
3. **Kill-count gating for forward progression.** Right arrow enabled only after N kills this level. ~2 files.
4. **Boss asteroid data + spawn at level 10 of each block.** Big grid, multi-core, high HP. Riffs on `AsteroidType` from Iter 2. ~3 files.
5. **Boss failure teleport back to block start.** Triggered when the boss falls off-screen un-killed. Clears in-flight meteors. ~2 files.
6. **Tuning pass — level 1 feels gentle, level 10 boss feels climactic.** Data-only.
7. **Tests — level progression, boss gating, fallback. PlayMode smoke test of a full block.** ~2 test files.
8. **Code-reviewer + manual play verify + merge + WebGL deploy.**

### Manual play-verify gates

- Level 1 feels slow and easy (per the user's tuning preference).
- Level-up happens at a satisfying pace, not a grind.
- Bosses feel different from normal asteroids.
- Fallback teleport is visible and not jarring.
- Top-bar level UI is readable at 16:9 without overlapping other UI.

---

## What stays in `# Future`

These are explicitly deferred past this roadmap and remain in `todo.md`:

- Asteroid rotation
- More weapons (gauss, electric, swarm drones-as-weapon)
- Ability unlocks tied to boss kills (railgun exploding rounds, missile multi-fire)
- Save progress / persistence

When those come up, extend this roadmap with new iterations and bump the revision date at the top.

## Process reminders for every iteration

Pulled from `CLAUDE.md` and `memory/` — not optional:

- Branch from a clean `main`. Commit any uncommitted build state up front.
- TDD for anything in the tested modules. EditMode first, PlayMode for behavior that needs real time/physics.
- Manual play verify in the editor before handing back — tests don't catch scene drift or feel.
- Code-reviewer agent dispatch as the second-to-last step of every plan.
- Identity scrub before every commit and before every push.
- Update `CLAUDE.md` and `README.md` together when documentation changes.
- After merging to `main`, close Unity, run `tools/build-webgl.sh` and `tools/deploy-webgl.sh`, smoke-test the gh-pages worktree, push, verify the live site.
- End every response with an explicit "next step" line.
