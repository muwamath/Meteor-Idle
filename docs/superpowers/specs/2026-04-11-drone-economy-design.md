# Iter 3 — Drone Economy — Design Spec

**Branch:** `iter/drone-economy`
**Date:** 2026-04-11
**Depends on:** Iter 1 (cores — substrate), Iter 2 (materials — gold provides instant-cash counterweight)

## Context

Iter 1 made cores the sole money source; Iter 2 added gold as an instant-cash counterweight. Iter 3 completes the economy rework by **killing instant money-on-voxel-break for cores**. Destroyed cores no longer pay directly — they drop as floating `CoreDrop` entities that collector drones must physically retrieve and return to base. Money arrives on drone dock.

Intended outcome:
- Early game is slow but satisfying (one drone, short trips, visible loop).
- Mid-late game feels like a fleet — up to 3 bays, each holding multiple drones, each drone richer in thrust/battery/cargo capacity.
- Gold's instant payout from Iter 2 is the strategic counterweight: slow drone economy for cores, instant gold for liquidity.
- Physics-y drones with inertia and meteor avoidance are the screenshot-moment this iteration is really about.

The chill/rewarding philosophy from Iter 2 carries forward — no lose-a-drone traps, no cargo spill penalties, generous fallbacks (limp-home mode) when things go wrong.

## Design decisions

### 1. Trip shape — hybrid one-drop/loiter (capacity unlock)

A drone's default trip is **one drop per trip**: launch → seek the nearest CoreDrop → pickup → return → deposit. The `Cargo Capacity` upgrade raises the capacity ceiling — level 0 = 1 slot, level 1 = 2 slots, etc. At capacity > 1, after pickup the drone switches to **loiter mode**: seek the next nearest drop until (a) capacity is full, (b) no drops remain in battery range, or (c) battery hits reserve threshold → return.

Early game visual: out, grab, back. Late game visual: out, grab, hop, grab, hop, grab, back — a real collector.

### 2. Battery — reserve-to-return with limp-home fallback

- Battery drains continuously while a drone is undocked. Thrust bursts cost extra.
- Soft threshold (say **40% of capacity**) = "return now." Drone drops any in-progress loiter plan, thrusts straight home.
- If battery somehow reaches 0% before reaching base (blocked by avoidance, pushed off course, etc.), drone enters **limp-home mode at 25% thrust**. It will always make it home, just slowly.
- At dock, battery recharges on a timer (modulated by the bay's `Reload Speed` upgrade). Drone can't launch again until recharged.
- No lose-a-drone failure state. Ever.

### 3. CoreDrop lifespan — very slow drift, offscreen despawn

- When a core is destroyed, a `CoreDrop` spawns at the core's world position with a **very slow downward drift** (significantly slower than meteor fall — 10-20% of base meteor speed). Sprite = dead-core voxel palette.
- Drop lives forever until (a) a drone grabs it, or (b) it drifts off the bottom of the screen, at which point it despawns silently.
- The slow drift is deliberate — it creates gentle urgency without being a panic mechanic. On-the-edge missions should be rare misses, not the common case.

### 4. Bay count, placement, and starting state

- **Max 3 bays**, mirroring the 3 weapon slots. Start with **1 bay built + 1 drone inside**. The other 2 bay slots start empty with a `+` placeholder, purchasable through a `BuildBayPanel` modal (mirrors `BuildSlotPanel`).
- Bays are placed **to the right of the weapon slots**, as their own row of 3. This shifts the weapon row a bit left of center to make room.
- Within a bay, the `Drones Per Bay` upgrade adds additional drones (max 2 per bay, for a late-game fleet ceiling of 6 drones).
- Drones are **fungible** — no per-drone state. All drones in all bays share the same global stats.

### 5. Drone vs meteor collision — physics bump only

- Meteors exert avoidance steering force on drones within a safety radius.
- On actual contact, meteor imparts a velocity kick via simple inelastic push. Drone tumbles briefly, corrects with thrust, keeps going.
- **No damage. No cargo spill. No drone loss.** The avoidance AI handles most hits; the rare bump is a visual gag, not a punishment.

### 6. Upgrade UI — one global panel

- One shared `DroneUpgradePanel` with two sections, accessed by clicking any bay:
  - **BAY** section — `Reload Speed`, `Drones Per Bay` (affect all bays equally)
  - **DRONE** section — `Thrust`, `Battery Capacity`, `Cargo Capacity` (affect all drones equally)
- All stats are **fleet-wide** — no per-bay drift, no per-drone drift.
- Visuals follow the existing `MissileUpgradePanel` two-column pattern.

### 7. Drone physics — custom integrator with space inertia

- Drones are **not** Rigidbody2D. A custom `DroneBody` component owns `position`, `velocity`, and applies:
  - Desired thrust vector from the state machine (toward target / toward home / avoidance steer)
  - A mild **linear damping** term that acts as the "slow brake" — drones eventually stop rather than coast forever, which matches the user's stated preference and avoids the "drone stuck at constant velocity" physical failure mode.
  - Push forces from meteor contacts (one-shot velocity kicks, no ongoing interaction).
- Steering uses `Vector2.MoveTowards` on velocity to respect thrust cap.
- Avoidance is a simple repulsion from any meteor within `safetyRadius` (scales with meteor size + a small margin). Repulsion magnitude scales with proximity.
- Why not Rigidbody2D: custom integrator is ~30 lines and gives perfect control over the "soft brake + thrust cap + avoidance" feel. Rigidbody2D would need constant force-tuning and angular-drag fighting. The Iter 2 explosive chain pattern already demonstrated that custom per-frame logic in `Update` is acceptable.

### 8. Drone visual — voxel plus-shape with red thruster tips

Procedural PNG at 15×15 or 21×21 (matching voxel aesthetic).

```
    ##
    ##
 ##    ##
 ##  █ ##     ← center cell is body, arms extend 2 cells each direction
 ##    ##
    ##
    ##
```

- Plus shape: center body cell + 4 arms of ~2 cells each.
- Arm-tip cells are painted bright red — these are the "thrusters."
- When thrust > 0, each arm-tip emits a small white particle trail (tiny voxel-aesthetic particles, not a `TrailRenderer`). Trail length scales with thrust magnitude.
- Body cell gets the standard 1-px dark outline + highlight treatment.
- Color palette TBD during the visual phase — think neutral grey body, red tips, white trails.

### 9. Bay visual — box with animated top doors

Procedural PNG for the bay body, procedural animation for the doors.

- Bay body: simple voxel-style box (3×3 or 4×4 cells). Neutral grey or dark metallic palette.
- **Top doors** animate open/close through 4 keyframes:

```
Closed:  __       (flat, doors meet in the middle)
Opening: /\       (doors angled up and outward)
Open:    | |      (doors fully vertical, out of the way)
Closing: /\       (reverse)
Closed:  __
```

- Animation runs on **drone launch** (doors open → drone exits → doors close) and on **drone dock** (doors open → drone enters → doors close).
- Implementation: two sprite children per bay, one per door, rotated via `transform.rotation` through the 4 keyframes. No Animator component — direct `Update` tick advancing through discrete states to match the voxel aesthetic (quantized motion, not smooth lerp).
- This animation system is the first non-projectile, non-turret animation in the project. Worth treating as a reusable pattern (`QuantizedSpriteAnimator` or inline in `DroneBay`).

### 10. Drone state machine

```
Idle (docked, recharging) ─┐
                           │
         ↓ battery full + drops exist + bay has launch capacity
   Launch (doors opening)
                           │
         ↓ doors open
   Seek (thrusting toward nearest drop, avoiding meteors)
                           │
         ↓ contact drop
   Pickup (drop removed from world, added to cargo)
                           │
         ↓ cargo full OR no drops in range OR battery ≤ reserve
   Return (thrusting toward home bay, avoiding meteors)
                           │
         ↓ close to bay
   Dock (doors opening, drone drifts in)
                           │
         ↓ drone inside bay
   Deposit (cargo count × core base value → GameManager.AddMoney)
                           │
         ↓ doors closing
   Idle (battery recharging)
                           │
         ↑
         └─── battery < full — recharge timer
```

Failure-ish branches:
- **Loiter cancel:** while Seeking a second drop, if battery ≤ reserve → flip to Return immediately.
- **Limp-home:** while Returning, if battery reaches 0 → thrust cap drops to 25% but Return continues.
- **Drop disappears mid-seek:** target drop falls offscreen before pickup → drone either picks a new nearest drop (if in range) or Returns.

### 11. Economy change — core kills no longer pay directly

- `Meteor.ApplyBlast` / `ApplyTunnel` on a Core-cell kill: instead of calling `GameManager.AddMoney`, **spawn a `CoreDrop` at the core's world position** carrying the destroyed core's payout value. The `DestroyResult.countByMaterialIndex` still tracks the kill for telemetry/tests, but `result.TotalPayout()` only sums cells that pay *directly* (gold, explosive).
- Implementation:
  - Add a new `VoxelMaterial` flag (`paysOnBreak : bool`) — default `true`, set to `false` on `Core.asset`.
  - `DestroyResult.TotalPayout()` only sums cells whose material has `paysOnBreak = true`.
  - Core-cell breaks go through a new `Meteor.SpawnCoreDrop(gx, gy, value)` side-effect.
- Gold and explosive continue to pay on break (their materials have `paysOnBreak = true`). Only cores go through the drop path.
- This isolates the Iter 3 change to one asset flag + one branch in the material payout loop, avoiding an invasive rewrite.

## Files to touch

### New code
- `Assets/Scripts/Drones/CoreDrop.cs` — pooled entity, slow downward drift, sprite paint, OnDisable cleanup
- `Assets/Scripts/Drones/DroneBody.cs` — custom physics integrator (position, velocity, thrust, damping, push)
- `Assets/Scripts/Drones/CollectorDrone.cs` — MonoBehaviour owning the state machine + cargo list
- `Assets/Scripts/Drones/DroneStats.cs` — ScriptableObject mirroring `TurretStats`/`RailgunStats`
- `Assets/Scripts/Drones/DroneBay.cs` — per-bay MonoBehaviour: owns drone children, doors animation, click routing
- `Assets/Scripts/Drones/BayStats.cs` — ScriptableObject: reload speed, drones per bay
- `Assets/Scripts/Drones/BayManager.cs` — spawns/manages up to 3 bays, `NextBuildCost`, parallel to `SlotManager`
- `Assets/Scripts/UI/DroneUpgradePanel.cs` — two-section (BAY / DRONE) upgrade panel
- `Assets/Scripts/UI/BuildBayPanel.cs` — modal for buying a new bay (parallel to `BuildSlotPanel`)

### Modified code
- `Assets/Scripts/Meteor.cs` — core kill spawns `CoreDrop` instead of paying directly; `DestroyResult.TotalPayout` respects `paysOnBreak`
- `Assets/Scripts/VoxelMaterial.cs` — add `paysOnBreak` field (default `true`)
- `Assets/Data/Materials/Core.asset` — set `paysOnBreak = false`
- `Assets/Scripts/GameManager.cs` — `RegisterDrop(CoreDrop)` hook for global queries (tests, drone target search)
- `Assets/Scripts/UI/` layout — room made to the right of the weapon row for a 3-bay row

### New data assets
- `Assets/Data/DroneStats.asset`
- `Assets/Data/BayStats.asset`

### New prefabs
- `Assets/Prefabs/CollectorDrone.prefab`
- `Assets/Prefabs/CoreDrop.prefab`
- `Assets/Prefabs/DroneBay.prefab`

### New / extended tests
- `Assets/Tests/EditMode/CoreDropTests.cs` — drift math, offscreen despawn trigger
- `Assets/Tests/EditMode/DroneBodyTests.cs` — integrator behavior: thrust, damping, push, limp-home thrust cap
- `Assets/Tests/EditMode/DroneStateMachineTests.cs` — idle → launch → seek → return → dock transitions (state-machine in isolation, no physics)
- `Assets/Tests/EditMode/DroneStatsTests.cs` + `BayStatsTests.cs` — standard `NextCost`/`CurrentValue`/`ApplyUpgrade` coverage
- `Assets/Tests/EditMode/BayManagerBuildCostTests.cs`
- `Assets/Tests/EditMode/MeteorCoreDropsSpawnTests.cs` — core kill → CoreDrop spawn, gold/explosive still pay directly
- `Assets/Tests/PlayMode/DroneCollectionEndToEndTests.cs` — meteor kill → drop → drone launch → pickup → return → deposit → money increases
- `Assets/Tests/PlayMode/DroneAvoidanceTests.cs` — drone thrusts past a stationary meteor without colliding (or bumps and recovers)
- `Assets/Tests/PlayMode/BayDoorsAnimationTests.cs` — doors step through closed → opening → open → closing → closed keyframes on launch/dock

## Verification

### Per-phase
- `mcp__UnityMCP__run_tests mode=EditMode` — green before each commit
- `mcp__UnityMCP__run_tests mode=PlayMode` — green before each commit
- `mcp__UnityMCP__read_console types=["error"]` — zero errors after every refresh

### End-to-end
- `BuildScripts.BuildWebGLDev()` via `mcp__UnityMCP__execute_code`
- `tools/serve-webgl-dev.sh` background
- `chrome-devtools-mcp` navigates to `http://localhost:8000/`, exercises drone launch/pickup/return cycle, screenshots, confirms console-clean
- **User hands-on verification** in their browser — ship only after explicit approval

### Manual play-verify gates
- Money only goes up when a drone deposits — never on core break.
- Gold and explosive still pay instantly (Iter 2 behavior preserved).
- Drones visibly avoid meteors; occasional bumps tumble and recover without damage.
- Bay doors animate through 4 discrete keyframes on launch and dock — clearly quantized, not smooth.
- Drone plus-shape is legible at gameplay distance; thruster trails read as pushing motion.
- Reload Speed and Drones Per Bay upgrades feel meaningful at 1-2 levels.
- Thrust / Battery / Cargo upgrades each produce a visibly different money-per-minute curve.
- Limp-home mode triggers only rarely (most missions complete on reserve-to-return), but when it does, the drone *always* makes it back.
- Frame rate holds with 2-3 bays × 2 drones = 6 drones + 8 CoreDrops + 15 meteors on screen.
- Early game feels slow but satisfying; mid game accelerates as upgrades compound.

## Future iterations (carried forward)

- **Iter 4 (levels):** money curve from Iter 3 is the input to level reward scaling. Boss-level cores should drop multiple CoreDrops.
- **Drone variants (post-Iter-4):** the `DroneStats` pattern is per-archetype-extensible the same way materials are. A "fast harvester" archetype or "tank collector" would mean adding a second SO + a drone-type selector on bays, not rewriting the state machine.
- **Explosive payout level-scaling (deferred from Iter 2):** the `paysOnBreak` + level-multiplier approach fits cleanly — a `payoutLevelCurve` on `VoxelMaterial` multiplying the base value at consumption time.

## Brainstorming decisions log

- **Q1 Trip shape:** C — hybrid, one-drop default with capacity upgrade unlocking loiter
- **Q2 Battery failure:** B + limp-home — reserve-to-return at 40%, 25%-thrust limp if somehow hits 0
- **Q3 CoreDrop lifespan:** B (very slow drift) — slow downward drift, offscreen despawn
- **Q4 Bay count + placement:** B — start 1 bay, max 3 bays, dedicated row to the right of weapons
- **Q5 Drone vs meteor collision:** A — physics bump only, no damage or cargo spill
- **Q6 Upgrade UI:** C — one global panel with BAY + DRONE sections; all stats fleet-wide
