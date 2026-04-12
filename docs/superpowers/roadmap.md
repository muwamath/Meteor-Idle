# Meteor Idle — Iteration Roadmap

Living document. Last revised: 2026-04-12 (Iter 3 + polish shipped).

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

## Iter 1 — Asteroid cores ✅ shipped 2026-04-11

**Branch:** `iter/asteroid-cores` (merged to `main`)

### What shipped

Asteroids now have a **core** (1+ voxels) surrounded by **dirt**. Per-voxel `VoxelKind` enum (`Empty`, `Dirt`, `Core`) + `int[,] hp` grid. Core voxels are visually distinct (red accent), harder to break (HP > 1), and weapons prefer to target core voxels via `PickCoreVoxel`. `ApplyBlast`/`ApplyTunnel` decrement HP and only clear on 0. Economy split: dirt = $0, core = value (later rerouted through drones in Iter 3). 7 phases shipped.

---

## Iter 2 — Asteroid variety (per-voxel materials) ✅ shipped 2026-04-11

**Branch:** `iter/asteroid-variety` (merged to `main@85ec469`)
**Spec:** [docs/superpowers/specs/2026-04-11-asteroid-variety-design.md](specs/2026-04-11-asteroid-variety-design.md)
**Plan:** [docs/superpowers/plans/2026-04-11-asteroid-variety.md](plans/2026-04-11-asteroid-variety.md)

### What shipped

The original "asteroid types" plan called for whole-asteroid type reskins (ice / fire / metal / gold / hardened). During brainstorming the user pivoted to a much stronger model: **per-voxel material variety inside each asteroid**, not whole-asteroid types. Each meteor still has cores and dirt, but now also:

- **Stone** — cool grey, HP 2, never targeted, forms thin veins (≤2 cells deep) through some asteroids.
- **Gold** — bright yellow, instant cash ($5), top targeting priority. Prefers to spawn embedded in stone veins; falls back to standalone for the future "gold run" event.
- **Explosive** — hot orange-red, second priority, **chains across frames** when adjacent explosives are damaged. Always net-positive (chain pays normally).

### Architecture

`VoxelMaterial` ScriptableObject + `MaterialRegistry` data layer. Adding a 6th material in Iter 3 is "create asset, register it" — no code edits to `Meteor.cs`, `ApplyBlast`, or `TurretBase`. New behavior verbs (chain reactions, etc.) extend a `MaterialBehavior` enum + handler in `Meteor.Update`'s pending-action loop.

### Test coverage

138 EditMode + 40 PlayMode = 178 tests, all green. New tests: material asset/registry smoke, generator placement determinism, stone 2-deep cap (200-seed sweep), gold-prefers-stone (1000-seed sweep), explosive non-adjacency (500-seed sweep), 3 PlayMode chain-reaction tests (isolated, two-adjacent, explosive→core kill).

### What changed from the original plan

- 6 phases instead of 4 (added explosive chain mechanics — a new behavior verb).
- ScriptableObject-per-material instead of enum + data table — explicit user requirement for extensibility.
- Targeting priority is gold > explosive > core (not just "is core").
- Spawn weights moved to per-material independent dials (the hook for the future "gold run for 10 seconds" event).

---

## Iter 3 — Economy rework (drones + floating cores) ✅ shipped 2026-04-12

**Branch:** `iter/drone-economy` (merged to `main@36dbf87`)
**Spec:** [docs/superpowers/specs/2026-04-11-drone-economy-design.md](specs/2026-04-11-drone-economy-design.md)
**Plan:** [docs/superpowers/plans/2026-04-11-drone-economy.md](plans/2026-04-11-drone-economy.md)

### What shipped

Killed the "instant money on voxel break" flow. Core voxels now spawn floating **CoreDrop** entities. **Collector drones** launch from **DroneBays**, fly to CoreDrops, pick them up, deliver them to the **Collector** (a rock-grinder with animated gold teeth at center of the bottom row), then loop for more until battery runs low. Money is paid on deposit at the Collector — never on voxel break.

**Bottom row layout:** `[base][bay][base][collector][base][bay][base]` — 4 weapon slots, 2 drone bays (both pre-built with 1 drone each), 1 Collector.

### Architecture

- **CoreDrop** (`Assets/Scripts/Drones/CoreDrop.cs`) — floating entity, drifts down, claimed by drones, consumed on pickup.
- **CollectorDrone** (`Assets/Scripts/Drones/CollectorDrone.cs`) — 8-state machine (Idle → Launching → Seeking → Pickup → Delivering → Depositing → Returning → Docking). Custom physics via `DroneBody` (exponential damping, explicit Euler integration). Meteor avoidance, contact push kick. Hides behind bay (sortingOrder 1) when idle.
- **Collector** (`Assets/Scripts/Drones/Collector.cs`) — single rock-grinder deposit point. Animated teeth (4-step quantized rotation). `IPointerClickHandler` stub for future upgrade panel.
- **DroneBay** (`Assets/Scripts/Drones/DroneBay.cs`) — launch/catch/recharge only (no deposits). 4-keyframe quantized door animation. `ICollectorDroneEnvironment` implementation. Drone count label (SerializeField exists, TMP child not yet wired in prefab).
- **DroneBody** (`Assets/Scripts/Drones/DroneBody.cs`) — plain C# physics integrator. Exponential damping, thrust steering, avoidance repulsion, push kick, limp-home mode.
- **DroneStats/BayStats** — ScriptableObject upgrade stats mirroring TurretStats/RailgunStats pattern.
- **BayManager** — spawns 2 pre-built bays, wires Collector reference, responds to DronesPerBay upgrades.
- **PanelManager** — static exclusive-panel manager, only one overlay visible at a time.
- **`VoxelMaterial.paysOnBreak`** flag — Core material has `paysOnBreak=false`, directing payout through drones.

### Economy

All prices flattened to **$1 per purchase** (baseCost=1, costGrowth=1) across all stats + build costs. Placeholder until economy overhaul.

### Test coverage

190 EditMode + 42 PlayMode = 232 tests, all green. New tests: DroneBody physics, state machine transitions (including Delivering/Depositing flow), DroneStats/BayStats formulas, DroneBay door animation, CoreDrop lifecycle, paysOnBreak isolation, GameManager drop registry, Meteor core-drop spawning, end-to-end drone collection, drone meteor avoidance.

### Polish pass (same branch, shipped same day)

All deferred items from code review were addressed:

- **Stat propagation to existing drones** — `BayManager` subscribes to `droneStats.OnChanged`, calls non-destructive `CollectorDrone.UpdateStats()` that hot-swaps thrust/damping/battery/cargo without resetting state or position.
- **ReloadSpeed affects recharge** — `ICollectorDroneEnvironment.ReloadSpeed` property, `DroneBay` implements it, `CollectorDrone.TickIdle` uses `battery += dt * env.ReloadSpeed`.
- **Braking upgrade stat added** — `DroneStats` now has 4 stats (Thrust, BatteryCapacity, CargoCapacity, Braking). Damping=2 at base gives responsive stops without killing speed.
- **ThrusterTrail pooling** — static `Queue<TrailParticle>` replaces per-particle `new GameObject()` / `Destroy()`.
- **CoreDrop pooling** — `SimplePool<CoreDrop>` on `GameManager`, `LateUpdate` returns dead drops to pool (fixed leak where offscreen despawn skipped Release).
- **Unique trail color per drone** — static 6-color palette, all 4 arms same color, auto-assigned on Awake.
- **Bay drone count TMP label** wired in DroneBay prefab.
- **`BuildBayPanel.cs` deleted** (dead code).

### Deferred to Iter 4

- Collector upgrades (deposit multiplier, attraction range) — `IPointerClickHandler` stub wired, no panel yet.

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
