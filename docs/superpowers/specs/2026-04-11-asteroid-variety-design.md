# Iter 2 — Asteroid Variety (per-voxel materials) — Design Spec

**Branch:** `iter/asteroid-variety`
**Date:** 2026-04-11
**Depends on:** Iter 1 (asteroid cores, shipped on `main@a3cb33a`)

## Context

Iter 1 gave asteroids a core/dirt split: cores have HP > 1, pay money on destruction, and are the only valid turret target. Iter 2 layers **additional voxel materials** on top of that substrate without changing what an asteroid fundamentally *is*. Most cells (~95%+) stay dirt; stone clumps form thin veins through some asteroids, gold ore deposits hide inside those veins, and rare explosives create chain-reaction screenshot moments.

This spec replaces the original "asteroid types" roadmap entry. The roadmap had whole-asteroid type reskins (ice / fire / metal / gold / hardened); during brainstorming the user pivoted to per-voxel material variety inside each asteroid. The new direction is meaningfully stronger:

- Each asteroid becomes a small puzzle (where are cores, where's the gold, watch for explosives) — more interesting than "this rock is ice, that rock is metal."
- Gold-as-instant-cash creates a strategic counterweight to Iter 3's slow drone-collected core money. When drones become the slow path, gold becomes the player's emergency liquidity.
- Explosives introduce the project's first non-inert behavior verb. Chain reactions are screenshot-worthy moments.
- Independent per-material spawn weights are exactly the right shape for the future "gold run for 10 seconds" event system.

The roadmap doc (`docs/superpowers/roadmap.md`) will be updated post-merge to reflect the pivot.

## Design philosophy

The user explicitly framed this game as casual/chill: **always err on more rewarding to the player than not.** If a numbers tweak goes wrong, flatten an exponential curve before reaching for friction. Every decision in this spec biases toward "fun toy" over "punishing trap."

## Core decisions

### 1. Voxel material model — `VoxelMaterial` ScriptableObject

Each material kind is a `VoxelMaterial` ScriptableObject asset. The current `VoxelKind` enum (`Empty | Dirt | Core`) stays for the alive-check fast path, but **behavior dispatch moves to material assets**. A parallel `VoxelMaterial[,] material` array carries the per-cell material reference.

`VoxelMaterial` fields:

| Field | Type | Notes |
|---|---|---|
| `displayName` | string | Debug only |
| `topColor`, `bottomColor` | Color | Palette for the existing `PaintBlockWithPalette` path |
| `baseHp` | int | Default HP when this material is placed (cores override by size) |
| `payoutPerCell` | int | Money paid when this cell's HP hits 0 |
| `behavior` | enum `MaterialBehavior` | `Inert | Explosive` — extension point for future behaviors |
| `targetingTier` | int | Turret targeting priority. `0 = never target`. Lower positive = higher priority. |
| `spawnWeight` | float | Independent rarity dial (see placement section) |

Materials live in `Assets/Data/Materials/` as named assets. **Adding a sixth material in a future iteration is "create a new `VoxelMaterial` asset, register it in `MaterialRegistry`"** — no edits to `Meteor.cs`, `ApplyBlast`, `TurretBase.FindTarget`, etc. Materials with truly new behaviors (the way Explosive is new in this iter) need a new `MaterialBehavior` enum value plus a handler in `Meteor.Update`'s pending-action loop — bounded extension, ~30-50 lines per behavior.

`MaterialRegistry` is a single ScriptableObject holding the list of all materials. `VoxelMeteorGenerator` and `Meteor` both reference it. The `VoxelKind` enum remains for hot loops (alive check) but is no longer load-bearing for behavior dispatch.

### 2. Initial material data

| Material | HP | Payout/cell | Behavior | Targeting tier | Spawn weight | Palette (top → bottom) |
|---|---|---|---|---|---|---|
| Dirt | 1 | $0 | Inert | 0 (never) | n/a (fills the rest) | (0.545, 0.451, 0.333) → (0.290, 0.227, 0.165) (existing) |
| Stone | 2 | $0 | Inert | 0 (never) | 0.05 | (0.55, 0.55, 0.58) → (0.28, 0.28, 0.30) — cool dark grey |
| Core | size-scaled (1-5) | size-scaled (existing $5) | Inert | 3 | n/a (fixed by size formula) | (0.75, 0.25, 0.25) → (0.35, 0.10, 0.10) (existing) |
| Gold | 1 | $5 (matches a core voxel) | Inert | **1 (top)** | 0.005 | (1.00, 0.85, 0.20) → (0.70, 0.50, 0.05) — bright saturated yellow |
| Explosive | 1 | $1 | Explosive | **2** | 0.002 | (1.00, 0.30, 0.10) → (0.55, 0.10, 0.05) — hot orange-red |

Spawn weights are starting values. The whole point of `spawnWeight` being on the asset is that they're trivial to retune. The Iter 4 levels system and the future "gold run" event system both modulate these weights at spawn time.

### 3. Targeting priority — gold > explosive > core > nothing

Turrets aim at any cell whose material has `targetingTier > 0`, picking the **lowest** numbered tier present. Within a tier, picks a random cell on the closest meteor (existing closest-meteor logic from Iter 1 generalizes naturally).

Priority chain:
1. **Gold** (tier 1) — top, instant cash, "grab it before something else clips it"
2. **Explosive** (tier 2) — expected net-positive due to chain payouts; players want to find these
3. **Core** (tier 3) — main progression
4. **Stone, Dirt** (tier 0) — never targeted; only hit incidentally by missile blasts and railgun tunnels

Implementation:
- `Meteor.HasLiveCore` from Iter 1 generalizes to `Meteor.HasAnyTargetable` (true if any cell's material has `targetingTier > 0`).
- `Meteor.PickRandomCoreVoxel` from Iter 1 generalizes to `Meteor.PickPriorityVoxel(out int gx, out int gy)` — returns a random cell from the highest-priority tier present on this meteor. Returns false if `HasAnyTargetable` is false.
- `TurretBase.FindTarget` filter changes from `m.HasLiveCore` to `m.HasAnyTargetable`. Cross-meteor priority sorting (e.g., "always shoot the meteor with gold even if a closer one has only cores") is intentionally NOT done — adds complexity for marginal feel benefit, and the chill-game preference makes the closest-meteor heuristic fine.
- `MissileTurret.Fire` and `RailgunTurret.RefreshAimVoxel` both call `PickPriorityVoxel`.
- All of Iter 1's freeze-at-last-aim behavior carries forward unchanged.

### 4. Placement algorithm

Three deterministic passes after Iter 1's dirt+core placement, all using the existing `System.Random(seed)` already threaded through `VoxelMeteorGenerator`:

**Pass 1 — Stone clumps.**
- Compute `stoneClumpCount` from asteroid size and stone's `spawnWeight`. Clump count scales with size: small asteroids get 0-1 clumps, large get 1-3.
- For each clump: pick a random dirt cell as the seed, then random-walk outward to a target clump size (rolled per-clump, 1-N cells where N also scales with asteroid size).
- **Hard constraint: stone is never more than 2 cells thick.** Equivalently, every stone cell must be within 2 cells of dirt or empty. Enforcement: when growing a clump, reject any candidate cell that would become the third concentric layer of stone. This makes clumps form veins/seams, not solid blobs — players can always see and reach what's inside.

**Pass 2 — Gold cells.**
- Compute `goldCount` from gold's `spawnWeight`. Most asteroids get 0; gold-bearing rocks are notable.
- For each gold cell: **prefer** a dirt cell adjacent to existing stone (so visually gold appears as ore embedded inside a stone vein). If no adjacent-to-stone cell is available, fall back to a standalone dirt cell. The standalone path is the "pure gold event" hook for later — pure-gold asteroids will work because gold's standalone placement is built in from day one.

**Pass 3 — Explosives.**
- Compute `explosiveCount` from explosive's `spawnWeight`. Even rarer than gold.
- Place anywhere in dirt cells. **Hard constraint: explosives may not be placed adjacent to other explosives.** This prevents accidental at-spawn chain detonation and keeps explosive placements visually clean.

### 5. Explosive behavior — pending-detonation queue

When an explosive cell is destroyed (its HP hits 0):
- All 8 neighbor cells take 1 damage (1-cell ring radius — 9 cells affected total).
- If any of those neighbors are also Explosive, they detonate the **next frame** (1-frame delay between chain links so the player can *see* the cascade — bombastic first).
- Each chain link cascades the same way until no more explosives remain.
- **All destroyed cells in the chain pay their normal `payoutPerCell`** — chain explosions count as kills for money. Always net-positive, never punishing.
- The explosive cell itself pays $1 (placeholder; will scale with level during economy overhaul — see Future Notes).

Implementation:
- `Meteor` owns a `Queue<(int gx, int gy)> pendingDetonations`.
- When `ApplyBlast` or `ApplyTunnel` reduces an Explosive cell's HP to 0, that cell goes onto the queue *before* being cleared from the texture.
- `Meteor.Update` processes the queue at the **start** of the next frame: for each queued cell, apply 1 damage to all 8 neighbors (which may queue more pending detonations for the frame after).
- A single `Update` tick processes the snapshot of the queue at the start of the tick, so chains naturally span multiple frames. The 1-frame delay is intrinsic, not a deliberate sleep.

`DestroyResult` (the struct from Iter 1) generalizes from `(dirtDestroyed, coreDestroyed)` to per-material counts. **Decision:** use a small `int[] countByMaterialIndex` paired with the registry rather than a `Dictionary<VoxelMaterial, int>` — `ApplyBlast`/`ApplyTunnel` are on the hot path and the array version avoids hashing per cell. Public surface: `result.GetCount(material)` and `result.TotalPayout()`. Missile and railgun call `result.TotalPayout()` instead of summing themselves.

## Existing Iter 1 behavior preserved

- Core count and core HP scaling by asteroid size — unchanged.
- `VoxelKind` enum stays for the alive-check fast path.
- `kind[x,y]` + `hp[x,y]` parallel arrays — the new `material[x,y]` array runs alongside; `kind` derives from `material` (any material with HP > 0 is `Filled`, otherwise `Empty`).
- Iter 1's freeze-at-last-aim behavior — unchanged.
- Iter 1's spawn rate (12s → 4.5s ramp) — unchanged.

## Files to touch

### New code
- `Assets/Scripts/VoxelMaterial.cs` — `ScriptableObject` class + `MaterialBehavior` enum
- `Assets/Scripts/MaterialRegistry.cs` — `ScriptableObject` holding the material list + lookup helpers

### Modified code
- `Assets/Scripts/VoxelMeteorGenerator.cs` — accept `MaterialRegistry`, run new placement passes, emit `VoxelMaterial[,]` alongside `kind` + `hp`
- `Assets/Scripts/Meteor.cs` — store `VoxelMaterial[,] material`, generalize `HasLiveCore` → `HasAnyTargetable`, `PickRandomCoreVoxel` → `PickPriorityVoxel`, queue/process `pendingDetonations` in `Update`, change `DestroyResult` shape
- `Assets/Scripts/TurretBase.cs`, `MissileTurret.cs`, `RailgunTurret.cs` — targeting filter + priority pick
- `Assets/Scripts/Missile.cs`, `Assets/Scripts/Weapons/RailgunRound.cs` — pay `result.TotalPayout()`

### New data assets
- `Assets/Data/Materials/{Dirt,Stone,Core,Gold,Explosive}.asset`
- `Assets/Data/MaterialRegistry.asset`

### New / extended tests
- `Assets/Tests/EditMode/VoxelMaterialTests.cs` — new
- `Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs` — extend
- `Assets/Tests/EditMode/MeteorApplyBlastTests.cs` — extend
- `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs` — extend
- `Assets/Tests/PlayMode/ExplosiveChainTests.cs` — new
- `Assets/Tests/PlayMode/TurretAimIntegrationTests.cs` — extend

## Verification

### Per-phase
- `mcp__UnityMCP__run_tests mode=EditMode` — green before each commit
- `mcp__UnityMCP__run_tests mode=PlayMode` — green before each commit
- Zero compile errors after every refresh

### End-to-end
- WebGL dev build via `BuildScripts.BuildWebGLDev()`
- Browser smoke via `chrome-devtools-mcp`
- **User hands-on verification before merge**

### Manual play-verify gates
- Most asteroids look like Iter 1 (dirt + cores). A subset have visible stone veins.
- Stone is never more than 2 cells thick anywhere on any asteroid.
- Gold cells appear inside stone veins when stone is present, standalone when not.
- Explosives are rare enough that finding one feels like a moment.
- Hitting an explosive triggers a visible chain when neighbors include other explosives — clearly readable as a cascade across frames, not a single instant burst.
- Turrets prioritize gold > explosive > core; never aim at stone or dirt.
- Frame rate holds with 5-10 mixed-material meteors on screen.
- Money flow feels generous — no asteroid hits feel "wasted."

## Future iterations (notes carried forward)

- **Economy overhaul (Iter 3+):** explosive cell payout should scale with level. Today it's $1; the user said "it should probably change as the level progresses." Likely shape: keep the asset value as a base and apply a runtime level-modifier multiplier at consumption time. Captured in `memory/project_explosive_payout_scaling.md`.
- **Gold run event (post-Iter-4):** the `spawnWeight` field is the hook. A runtime event multiplies any material's effective spawn weight by N for a duration. Pure-gold asteroids work today because gold's standalone-placement fallback is built in.
- **More inert materials (anytime):** create asset, register in `MaterialRegistry`. Zero code changes needed.
- **More behaviors (anytime):** new `MaterialBehavior` enum value + handler in `Meteor.Update`'s pending-action loop. ~30-50 lines per behavior.
