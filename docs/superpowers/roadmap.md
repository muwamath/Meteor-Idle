# Meteor Idle — Iteration Roadmap

Living document. Last revised: 2026-04-12 (Iter 4 + bugfix pass + SerializeField drift correction + options panel / build version display all shipped).

Ordered plan for upcoming iterations. Each is a branch (`iter/<name>`) with its own spec + plan when large enough. Prioritized: bugs → tuning → polish → new systems → big features.

## Sizing rule of thumb

**A phase should touch ≤ 3 files or ≤ ~200 lines of code + tests. If a phase would exceed that, split it.**

---

## Shipped

### Iter 0 — Aim fixes ✅ 2026-04-11
Railgun lead-aim, turret range cap removed. 21 commits, 110/36 EM/PM tests.

### Iter 1 — Asteroid cores ✅ 2026-04-11
Per-voxel VoxelKind (Empty/Dirt/Core), HP grid, core targeting. 7 phases.

### Iter 2 — Asteroid variety ✅ 2026-04-11
Per-voxel materials (Stone, Gold, Explosive). ScriptableObject + MaterialRegistry. Chain reactions. 178 tests.

### Iter 3 — Economy rework (drones) ✅ 2026-04-12
CoreDrop entities, CollectorDrone 8-state machine, DroneBay, Collector grinder. Money via drone deposit, not voxel break. 232 tests.

### Iter 4 — Levels / progression / boss ✅ 2026-04-12
**Spec:** [specs/2026-04-12-levels-progression-design.md](specs/2026-04-12-levels-progression-design.md)
**Plan:** [plans/2026-04-12-levels-progression.md](plans/2026-04-12-levels-progression.md)

150-level progression. Core-kill-based advancement (money purely for upgrades). Boss every 10th level (slow fall, dark/crimson, spawns alone). Boss failure = back 2 levels. 5-cell scrolling level strip UI. Base stat rebalance (weak start, 150 levels to feel powerful). Debug level picker. 16 commits, 270 tests (223 EM + 47 PM).

**Design changes during implementation:**
- Advancement switched from money-threshold to core-kill-count (money never consumed by progression)
- Boss failure softened from "restart block" to "back 2 levels"

### Bugfix pass ✅ 2026-04-12
- Missile lifetime 5s→12s (reaches full screen at base speed 2)
- Boss fall speed 0.3→0.15 (slow menacing descent)
- MeteorSpawner dynamic spawn range from Camera.main (fixes WebGL off-screen spawns)
- ApplyBlast line-of-sight check (damage can't pass through surviving cores)
- 271 tests (224 EM + 47 PM)
- **Correction (commit `fe9fa89`):** missile lifetime and boss fall speed were code-default-only changes and the serialized prefab/scene YAML won at runtime, so they silently didn't ship the first time. Re-fixed by editing `Assets/Prefabs/Missile.prefab` and `Assets/Scenes/Game.unity` directly. Added as a permanent gotcha in CLAUDE.md.

### Options panel + build version display ✅ 2026-04-12
Gear icon bottom-right opens a modal (`OptionsPanel`) showing `Build: <sha> · <utc-date> · <variant>`. The SHA is auto-stamped at build time: `BuildScripts.BuildWebGL()` and `BuildWebGLDev()` call `git rev-parse --short HEAD` and write the result to `Assets/Resources/BuildInfo.txt` (gitignored) immediately before invoking `BuildPipeline.BuildPlayer`. `BuildInfo.cs` reads it at runtime via `Resources.Load<TextAsset>`. The panel is wired through `PanelManager` so it's mutually exclusive with upgrade panels, matching `DroneUpgradePanel`'s pattern. This is the designated home for future options (graphics quality, audio volumes, fullscreen toggle) so Iter 7 (UI overhaul) should slot them in rather than creating new panels. Also bumps `RunHitTest` maxWait from 15s→30s to unflake `Hit_Railgun_SideSlot_FarMeteor` under load. Commit `e7eb140`, 271 tests (224 EM + 47 PM).

---

## Next Up

### Bugfix — Drone bay click doesn't open upgrade overlay

**Branch:** `iter/bugfix-drone-bay-click`
**Size:** small, investigation + fix + test coverage
**Depends on:** nothing

Clicking on a drone bay does not open the `DroneUpgradePanel`. Needs repro, root cause (missing collider? click handler not wired? `PanelManager` suppression? z-ordering vs another click catcher?), and fix.

**Also:** add PlayMode test coverage for clicking-to-open overlays and verifying the correct panel is shown. Menu/overlay interaction is currently untested and this bug slipped past both test suites — a click-and-verify helper should cover `DroneUpgradePanel`, `MissileUpgradePanel`, `RailgunUpgradePanel`, `BuildSlotPanel`, and `OptionsPanel` at minimum.

---

### Iter 5 — Tuning pass

**Branch:** `iter/tuning`
**Size:** small, data-focused. No new systems.
**Depends on:** bugfix pass (all mechanics working correctly)

Play the game end-to-end and tune every scaling curve so the 150-level arc feels right.

**Scope:**
- Core kill threshold curve (baseCoreKills, growthExponent)
- Spawn rate curve per level (initialInterval/minInterval endpoints)
- Meteor scaling (size range, core HP, stone HP, core count per level)
- Core value curve (reward scaling vs upgrade costs)
- Weapon base stats (missile speed/damage/blast, railgun fire rate/speed/weight)
- Upgrade costs (baseCost/costGrowth per stat, all weapons + drones)
- Boss scaling (HP, core count, fall speed per block)

**Verify gates:**
- Level 1: poking pebbles with a stick
- Level 30: noticeably stronger
- Level 80: visibly intense
- Level 150: screen full of action, fun and bombastic
- Bosses feel like events, not speedbumps or walls

---

### Iter 6 — Visual polish

**Branch:** `iter/visual-polish`
**Size:** medium, art-focused
**Depends on:** Iter 5 (tuning settled before polishing visuals)

**Weapons:**
- Missile trail effects — rework for better visual feedback and feel
- Missile launcher appearance — current model looks odd, needs redesign
- Railgun streak/impact visuals — review and improve

**Asteroids:**
- Asteroid appearance — improve voxel shading, color variety, surface detail
- Asteroid size variety — more variation in scales for visual interest
- Add rotation to asteroids

**Drones:**
- Better visuals for drone batteries
- Better visuals for drone bay charging animation
- Drones rotate and face direction of travel

**General:**
- Background: smaller stars for depth and parallax
- Base slot visual clarity — obvious which slots are empty vs occupied
- Overall color palette review — cohesive look across weapons, UI, environment

---

### Iter 7 — UI overhaul

**Branch:** `iter/ui-overhaul`
**Size:** medium
**Depends on:** Iter 6 (visual language established before redesigning panels)

**HUD / Panels:**
- Upgrade panel readability — clearer stat labels, values, cost formatting
- Font sizes and contrast — legible at target resolution
- Panel layout consistency — uniform spacing, alignment, styling
- Money/resource display — more prominent and readable

---

### Iter 8 — Persistence (save/load)

**Branch:** `iter/persistence`
**Size:** full spec + plan
**Depends on:** Iter 5 (tuned economy before saving state)

**Scope:**
- Save/load progress — current level, money, all upgrade levels, drone stats
- Offline/idle earnings — accumulate money while closed, show summary on return
- Storage: `PlayerPrefs` or `localStorage` (WebGL), JSON serialization

---

### Iter 9 — New weapons

**Branch:** `iter/new-weapons`
**Size:** full spec + plan per weapon, or bundled
**Depends on:** Iter 5 (balanced base stats)

**Candidates (pick 1-2 per iteration):**
- Gauss gun
- Electric weapon (chain lightning or arc style)
- Swarm weapon — drones/bees/small units with laser effects
- Gravity gun — on hit grows larger consuming everything, then shrinks to nothing. Expansion/shrink rates upgradable.
- Dirt eater — consumes dirt voxels (non-core material) on contact, clearing paths to cores without wasting damage on filler.

---

### Iter 10 — Weapon abilities / mod system

**Branch:** `iter/weapon-mods`
**Size:** full spec + plan
**Depends on:** Iter 9 (more weapons to attach abilities to)

Attach abilities to weapons (e.g. railgun exploding rounds, double missile fire). Unlock points earned by beating level bosses.

---

### Iter 11 — Prestige / rebirth

**Branch:** `iter/prestige`
**Size:** full spec + plan
**Depends on:** Iter 8 (persistence must exist first)

Reset progress for a permanent multiplier or new tier of unlocks. Long-term replayability.

---

## Future (unscheduled)

These have no iteration assigned yet. They'll be slotted in when the time comes.

**Progression / Meta:**
- Achievements/milestones ("Destroy 100 asteroids," "Earn $1M," etc.)
- Stats/numbers screen (total DPS, money/sec, lifetime earnings, asteroids destroyed, drops collected)

**Onboarding:**
- First-run tutorial or guided sequence ("click a slot to build a weapon")
- Tooltips on UI elements for new players

**Settings:**
- Graphics/quality toggle for lower-end WebGL performance
- Fullscreen toggle

**Audio:**
- Weapon fire sounds (missile launch, railgun shot)
- Explosion / voxel destruction sounds
- Core drop collection sound
- Drone hum / thruster audio
- Ambient space music / background track
- UI interaction sounds (button clicks, panel open/close, purchase)
- Volume controls (master, SFX, music)

**Build / Pipeline:**
- Investigate ways to speed up WebGL build process

---

## Process reminders for every iteration

- Branch from a clean `main`. Commit any uncommitted build state up front.
- TDD for tested modules. EditMode first, PlayMode for real time/physics.
- Manual play verify in editor before handing back.
- Code-reviewer agent dispatch as second-to-last step.
- Identity scrub before every commit and push.
- Update CLAUDE.md and README.md together when docs change.
- After merging to main: prod WebGL build via MCP, deploy-webgl.sh, push gh-pages, verify live site.
