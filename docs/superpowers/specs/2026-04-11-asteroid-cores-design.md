# Asteroid Cores — Design

Iteration: **Iter 1 — `iter/asteroid-cores`** (from `docs/superpowers/roadmap.md`).
Date: 2026-04-11.
Approved: 2026-04-11 via plan-mode gate.

## Problem

After Iter 0, every voxel in a meteor is identical: one HP, pays $1 per kill, interchangeable from the turret's point of view. There is no "target" — the whole meteor is the target. This flattens gameplay and blocks every downstream iteration in the roadmap:

- **Iter 2 (asteroid types)** needs different material layers — you can't have "gold with an ore core" if every cell is the same.
- **Iter 3 (drones + floating cores)** needs a thing that "a core" refers to — something distinct from dirt that a drone can collect.
- **Iter 4 (levels + boss asteroids)** needs a per-asteroid "hardness knob" beyond overall voxel count — boss meteors should feel armored, not just bigger.

Iter 1 introduces the distinction: meteors are built from **dirt** (HP 1, no payout, filler) and **core** (multi-HP, pays money, visually distinct, the real objective). Turrets aim exclusively at cores. Meteors without live cores are ignored and drift off-screen without further damage. This sets the substrate the next three iterations will build on.

## Goals

- Voxel state model that supports kind + HP per cell, deterministic per seed.
- Generator places cores in the inner region of the shape, count and HP both scaled with meteor size.
- Missile blast and railgun tunnel decrement HP instead of instantly clearing cells; cells only become `Empty` when HP reaches 0.
- Turret targeting prefers cores **exclusively**: no-core meteors are not valid targets, turrets hold fire when no valid target exists.
- Money is paid on core destruction only. Dirt destruction pays $0.
- Cores are visually distinct from dirt at a glance.
- Every existing EditMode and PlayMode test stays green (with updates where semantics demand).

## Non-goals

- **Iter 2 asteroid types** (ice/fire/metal/gold/hardened). This iteration ships with *one* dirt palette and *one* core palette. Iter 2 will add per-type palettes and multipliers.
- **Iter 3 floating cores / drones.** Money is still paid directly at the moment a core voxel dies. Iter 3 will replace this with "drops a floating core, drone collects it, deposit at base."
- **Iter 4 boss meteors / level scaling.** Core count and HP formulas are linear functions of the existing `sizeScale` parameter. Levels are not yet a thing.
- **Missile Damage stat affecting HP.** `Damage` still only scales blast radius. A high-HP core takes multiple blasts regardless of Damage upgrade — multi-hit is the mechanic.

## Design

### 1. Voxel state representation

Current `Meteor.voxels` is `bool[GridSize, GridSize]` (true = alive, false = empty). Insufficient for multi-HP cores.

**New state:** two parallel 2D arrays on `Meteor`:

```csharp
private VoxelKind[,] kind; // Empty | Dirt | Core
private int[,] hp;          // HP remaining per cell
```

`VoxelKind` is a new enum defined alongside `VoxelMeteorGenerator`:

```csharp
public enum VoxelKind : byte
{
    Empty = 0,
    Dirt  = 1,
    Core  = 2,
}
```

Access pattern conventions:

- **"Is this cell alive?"** → `kind[x,y] != VoxelKind.Empty`
- **"Deal 1 damage"** → `if (--hp[x,y] <= 0) { kind[x,y] = VoxelKind.Empty; /* clear texture block */ }`
- **"Is this a core?"** → `kind[x,y] == VoxelKind.Core`

Memory: `10×10 × 4 bytes (int) + 10×10 × 1 byte (enum) = 500 bytes per meteor`, ~5 pooled meteors = 2.5 KB total. Negligible.

### 2. `VoxelMeteorGenerator` changes

The generator currently emits `out bool[,] grid, out Texture2D texture, out int aliveCount`. New signature:

```csharp
public static void Generate(
    int seed,
    float sizeScale,
    out VoxelKind[,] kind,
    out int[,] hp,
    out Texture2D texture,
    out int aliveCount);
```

Steps inside `Generate`:

1. **Dirt shape generation** — unchanged. The existing sin-wave lump algorithm produces a bool[,] grid of live cells; rename the local to `isLive` for clarity.
2. **Compute `coreCount` and `coreHp`** from `sizeScale`:

   ```csharp
   float sizeT = Mathf.Clamp01((sizeScale - 0.525f) / (1.2f - 0.525f));
   int coreCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 4f, sizeT)), 1, 4);
   int coreHp    = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 5f, sizeT)), 1, 5);
   ```

   | Size | coreCount | coreHp |
   |------|-----------|--------|
   | 0.525 | 1 | 1 |
   | 0.75  | 2 | 3 |
   | 1.0   | 3 | 4 |
   | 1.2   | 4 | 5 |

3. **Pick core cells.** Deterministic placement using the same `System.Random(seed)` already threaded through the generator:

   ```csharp
   // Collect live cells and sort by distance from grid center (4.5, 4.5)
   var liveCells = new List<(int x, int y, float d2)>();
   for (int y = 0; y < GridSize; y++)
       for (int x = 0; x < GridSize; x++)
           if (isLive[x, y])
           {
               float dx = x + 0.5f - GridSize * 0.5f;
               float dy = y + 0.5f - GridSize * 0.5f;
               liveCells.Add((x, y, dx*dx + dy*dy));
           }
   liveCells.Sort((a, b) => a.d2.CompareTo(b.d2));

   // Candidate pool = top 2*coreCount (or 5, whichever is larger) innermost cells
   int poolSize = Mathf.Min(Mathf.Max(coreCount * 2, 5), liveCells.Count);
   var pool = liveCells.GetRange(0, poolSize);

   // Deterministic Fisher-Yates shuffle using the generator's rng
   for (int i = pool.Count - 1; i > 0; i--)
   {
       int j = rng.Next(i + 1);
       (pool[i], pool[j]) = (pool[j], pool[i]);
   }

   // First coreCount become cores
   int actualCoreCount = Mathf.Min(coreCount, pool.Count);
   ```

4. **Populate `kind` and `hp`.** Every live cell is `Dirt` with `hp = 1`. The picked core cells are overwritten to `Core` with `hp = coreHp`.

5. **Paint the texture.** Existing `PaintVoxel(tex, x, y)` becomes `PaintDirtVoxel`. New `PaintCoreVoxel(tex, x, y)` with the core palette. Inside the loop, branch on `kind[x,y]`.

**Core palette** (baseline for Iter 1 — a single muted-red hue):

```csharp
private static readonly Color CoreTopColor    = new Color(0.75f, 0.25f, 0.25f, 1f);
private static readonly Color CoreBottomColor = new Color(0.35f, 0.10f, 0.10f, 1f);
```

Both colors match the saturation/value band of dirt's brown (`#8B7355` → `#4A3A2A`), just hue-shifted. The `hi`/`lo` 1-pixel edge treatment reuses the existing `PaintVoxel` block layout — `PaintDirtVoxel` and `PaintCoreVoxel` share a private helper that takes the palette pair and applies the same hi/lo logic.

### 3. `Meteor` changes

#### New state
- `private VoxelKind[,] kind;`
- `private int[,] hp;`
- `private int coreVoxelCount;` — cached total core cells alive, maintained on every HP zero-cross

#### Removed
- `private bool[,] voxels;`

#### Updated public API
- `IsAlive` — unchanged semantics (governs pool lifecycle).
- `AliveVoxelCount` — now counts all non-Empty cells (dirt + core). Kept for backwards compat with tests.
- `IsVoxelPresent(int gx, int gy)` — returns `kind[gx, gy] != Empty`.
- `GetVoxelWorldPosition(int gx, int gy)` — unchanged.
- `PickRandomPresentVoxel(out int gx, out int gy)` — kept for legacy callers (tests) but **no longer used by turrets** (see below).

#### New public API
- `public bool HasLiveCore { get; }` — `coreVoxelCount > 0` (cached). No loop needed at read time.
- `public int CoreVoxelCount => coreVoxelCount;` — for tests that want to assert "cores all destroyed."
- `public bool PickRandomCoreVoxel(out int gx, out int gy)` — mirrors `PickRandomPresentVoxel` but filters to `kind == Core`. Returns false if `coreVoxelCount == 0`.

#### `ApplyBlast`

Current signature: `public int ApplyBlast(Vector3 worldImpactPoint, float worldRadius)`. New signature:

```csharp
public struct DestroyResult
{
    public int dirtDestroyed;
    public int coreDestroyed;
    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
}

public DestroyResult ApplyBlast(Vector3 worldImpactPoint, float worldRadius);
```

(Using a struct instead of a tuple keeps call sites cleaner and makes the meaning explicit at each read site.)

Body changes:
- Keep walk-inward + nearest-alive-cell fallback logic unchanged.
- For each cell in the blast circle: `if (kind[x,y] == Empty) continue;`
- `hp[x,y] -= 1;`
- `if (hp[x,y] <= 0) { var wasCore = kind[x,y] == VoxelKind.Core; kind[x,y] = VoxelKind.Empty; aliveCount--; if (wasCore) { result.coreDestroyed++; coreVoxelCount--; } else { result.dirtDestroyed++; } /* clear texture block, spawn chunk particle */ }`
- If `hp[x,y] > 0` after the decrement: **no texture update** (the cell is still alive, visibly the same color — we don't currently render HP bars). This is by design — the visual for "took damage but not dead" is the next shot landing on it.

#### `ApplyTunnel`

Same return shape change. Walker loop:
- `if (kind[ix,iy] == Empty) continue;`
- `hp[ix,iy] -= 1; budget -= 1;` (weight budget consumed per HP decrement, not per voxel cleared)
- `if (hp[ix,iy] <= 0) { /* same dead-cell handling as ApplyBlast */ }`
- Break out of the budget loop when `budget <= 0`.

Weight consumption: a core HP 5 costs 5 weight to fully destroy. Dirt HP 1 costs 1. This matches the user's `todo.md` intuition ("different sell costs, but also more difficult to break") and preserves the "glide through prior tunnels" behavior because empty cells still don't consume weight.

### 4. `TurretBase.FindTarget`

One-line change:

```csharp
if (m == null || !m.IsAlive || !m.HasLiveCore) continue;
```

The existing `null`-return path in both `TurretBase.Update` and `RailgunTurret.Update` handles "no target, hold fire" with no additional code.

### 5. `MissileTurret.Fire`

- Replace `PickRandomPresentVoxel` with `PickRandomCoreVoxel`.
- Remove the `dir = barrel.up;` fallback path — a meteor without a pickable core would have been filtered out by `FindTarget` already, so reaching `Fire` with a non-targetable meteor is a bug, not a state to silently paper over.
- On the rare race where the target's last core dies between `FindTarget` and `Fire` in the same frame: `PickRandomCoreVoxel` returns false, `Fire` returns early without spawning a missile. Next tick picks a new target.
- Payout: after `Missile` impact, `ApplyBlast` returns `DestroyResult`. `Missile` passes `result.coreDestroyed` to `GameManager.AddMoney(result.coreDestroyed * CoreBaseValue)`. `CoreBaseValue = 5` lives as a `const` on `GameManager` (or a dedicated `Economy` static class if we prefer a home).

### 6. `RailgunTurret.RefreshAimVoxel` and `Fire`

- `RefreshAimVoxel` uses `PickRandomCoreVoxel` instead of `PickRandomPresentVoxel`. If false, `hasAimVoxel = false` and the barrel waits.
- **Remove the post-fire `hasAimVoxel = false` invalidation.** In Iter 0 this was the fix for the "keep firing through the dead center tunnel" bug. With multi-HP cores, we WANT consecutive shots to hammer the same core until it dies. The `RefreshAimVoxel` → `IsVoxelPresent` check handles "voxel died, re-pick" naturally: once a core's HP reaches 0 and it flips to `Empty`, the very next Update tick sees `!IsVoxelPresent(...)` and picks a new core. Safe because the aim target is now "this specific live core" rather than "the meteor center."
- Payout: `RailgunRound` currently calls `GameManager.AddMoney(consumed)` directly inside its tunnel loop. Change to `GameManager.AddMoney(result.coreDestroyed * CoreBaseValue)` — meaning `RailgunRound.Update` accumulates a `DestroyResult` over its lifetime and pays at despawn (or per-meteor), not per tunnel step. **Simpler alternative:** pay per-meteor at the moment `ApplyTunnel` returns, since that's where we have the result in hand.

### 7. Texture repaint on HP = 0 only

Currently `Meteor.ApplyBlast` calls `VoxelMeteorGenerator.ClearVoxel(texture, x, y)` when a voxel is destroyed. Under HP semantics, `ClearVoxel` is still called — but only when `hp[x,y] <= 0` after a decrement. Cells that took damage but survived are NOT repainted. `texture.Apply()` at end of frame only flushes actually-changed pixels (which is the current behavior already).

### 8. Visual verification for the core palette

Phase 4 is art-adjacent and triggers the **`feedback_visual_verification_for_art`** rule from memory: always pause for user to inspect any generated art via `manage_camera screenshot include_image=true` (editor) or a chrome-devtools screenshot of the dev build before committing or advancing. The phase will regenerate a fresh meteor via `execute_code`, screenshot the game view, and wait for user approval on the "does it read as a core?" question before proceeding.

## Deliberate non-changes

- `Meteor.IsAlive` stays broad — it's about pool lifecycle (fade, despawn, trigger collider), not targeting. `HasLiveCore` is the new layered filter.
- Missile `Damage` stat still only scales blast radius. No HP-per-blast scaling.
- Railgun `Weight` stat still means "total HP budget per shot." Wider `Caliber` doesn't change per-cell damage; it just widens the band of cells affected per step.
- Existing `PickRandomPresentVoxel` stays on `Meteor` as a public method for legacy test callers (specifically `TurretTargetingTests` uses it indirectly via the existing fixture). Targets no longer use it, but removing it would force more test churn than the fix is worth.

## Known breakage in existing tests

The "strictly core" targeting rule changes semantics for several Iter 0 tests. Each phase updates the tests it touches — there's no separate cleanup phase.

- **`Railgun_MultipleShots_DrainsStationaryMeteor`** (PlayMode). Asserts `destroyed > 20` voxels after ~25 shots. Under cores: only 1-4 cores die, dirt is untouched. **New assertion:** `meteor.CoreVoxelCount == 0` within the time budget.
- **`TurretAimIntegrationTests.Hit_*` matrix** (12 + 3 FireRate-specific cases). Asserts `AliveVoxelCount < initialVoxels`. Under cores: alive count only drops by the destroyed-core count. **New assertion:** `meteor.CoreVoxelCount < initialCores`. The helper captures `initialCores = meteor.CoreVoxelCount` before firing.
- **`TurretTargetingTests.FindTarget_PicksClosestLiveMeteor` and siblings** (PlayMode). Spawns meteors via `SpawnTestMeteor`, which calls `Meteor.Spawn` with a seed. Under the new generator, these meteors will always have ≥1 core (smallest size = 1 core). No assertion change needed; the tests still find and filter targets correctly. The fixture should add a one-line sanity assert that every spawned test meteor has `HasLiveCore` at spawn time.
- **`MeteorApplyBlastTests`, `MeteorApplyTunnelTests`, `MeteorVoxelApiTests`** (EditMode). Multiple tests read the old `int ApplyBlast(...)` return and check `aliveCount` pre/post. Updates: switch to `DestroyResult`, add cases for multi-hit core kills, add cases for the `(dirt, core)` split.

Phase 1 of the implementation (voxel state + generator) rewrites the test fixtures to use `VoxelKind` and `HasLiveCore` throughout. Subsequent phases add the phase-specific test cases.

## File inventory

### New
- `Assets/Scripts/VoxelKind.cs` — the enum (or defined inline in `VoxelMeteorGenerator.cs` — decide in Phase 1)

### Modified
- `Assets/Scripts/VoxelMeteorGenerator.cs` — generator signature, core placement, core palette, `PaintDirtVoxel` / `PaintCoreVoxel`
- `Assets/Scripts/Meteor.cs` — `kind`/`hp` grids, `DestroyResult` struct, `HasLiveCore`, `CoreVoxelCount`, `PickRandomCoreVoxel`, HP-aware `ApplyBlast` and `ApplyTunnel`
- `Assets/Scripts/Missile.cs` — handles `DestroyResult` from `ApplyBlast`, pays `coreDestroyed * CoreBaseValue`
- `Assets/Scripts/MissileTurret.cs` — `PickRandomCoreVoxel`, remove `barrel.up` fallback
- `Assets/Scripts/RailgunTurret.cs` — `PickRandomCoreVoxel`, remove post-fire invalidation
- `Assets/Scripts/Weapons/RailgunRound.cs` — handles `DestroyResult` from `ApplyTunnel`, pays `coreDestroyed * CoreBaseValue`
- `Assets/Scripts/TurretBase.cs` — `FindTarget` filters by `HasLiveCore`
- `Assets/Scripts/GameManager.cs` — `const int CoreBaseValue = 5;`

### Tests (modified and new)
- `Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs` — determinism for `(kind, hp)`, core count formula, core placement inside shape
- `Assets/Tests/EditMode/MeteorApplyBlastTests.cs` — `DestroyResult` return, multi-hit core kills, dirt vs core split
- `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs` — weight budget on damage-dealt, partial core destruction
- `Assets/Tests/EditMode/MeteorVoxelApiTests.cs` — `HasLiveCore`, `CoreVoxelCount`, `PickRandomCoreVoxel`
- `Assets/Tests/PlayMode/TurretTargetingTests.cs` — filter by `HasLiveCore`
- `Assets/Tests/PlayMode/TurretAimIntegrationTests.cs` — matrix assertions updated to `CoreVoxelCount`; drain test asserts cores reach 0

## Phases (7, respecting the ≤ 3 files / ≤ ~200 lines sizing rule)

1. **Voxel state + generator: `VoxelKind` enum, `(kind, hp)` grids, core placement.** `VoxelMeteorGenerator.cs` + `Meteor.cs` + `VoxelMeteorGeneratorTests.cs`. Tests for determinism, core count formula, core placement inside shape. Does NOT yet handle damage — `ApplyBlast`/`ApplyTunnel` still work because they just check `kind != Empty`.
2. **`ApplyBlast` HP decrement + `DestroyResult` return.** `Meteor.cs` (ApplyBlast body) + `Missile.cs` (handle new return shape) + `MeteorApplyBlastTests.cs`. Multi-hit core kill test. Missile payout unchanged for now (still uses `TotalDestroyed`).
3. **`ApplyTunnel` HP decrement + weight-on-damage.** `Meteor.cs` (ApplyTunnel body) + `RailgunRound.cs` (handle new return shape) + `MeteorApplyTunnelTests.cs`. Weight-budget-per-damage test, core HP drain test.
4. **Core visuals.** `VoxelMeteorGenerator.cs` (`PaintCoreVoxel` + palette) + `Meteor.cs` (paint branch). **Visual verify gate** — screenshot in dev WebGL, pause for user to approve "does it read as a core?" before advancing.
5. **Targeting picks core voxels.** `TurretBase.cs` (`FindTarget` filter) + `MissileTurret.cs` (core-voxel pick) + `RailgunTurret.cs` (core-voxel pick, remove post-fire invalidation) + test updates.
6. **Economy split.** `GameManager.cs` (`CoreBaseValue`) + `Missile.cs` + `RailgunRound.cs` + test for dirt-only hits pay $0 and core hits pay `coreDestroyed * 5`.
7. **Code-reviewer agent + WebGL dev verify + merge + gh-pages prod deploy.**

Each phase respects the sizing rule. Phases 2 and 3 touch a test file and two source files each — within the rule. Phase 1 and 5 touch three source files each — right at the rule, so any one of them getting long pushes a split.

## Verification

### Automated (every phase)
- `mcp__UnityMCP__run_tests mode=EditMode` — must be green before each commit
- `mcp__UnityMCP__run_tests mode=PlayMode` — must be green before each commit
- `mcp__UnityMCP__read_console types=["error"]` — zero errors after every compile request

### End-to-end (Phase 7)
- `BuildScripts.BuildWebGLDev()` via `mcp__UnityMCP__execute_code` (editor stays open, per the always-open MCP rule)
- `tools/serve-webgl-dev.sh` in background with `dangerouslyDisableSandbox: true`
- `chrome-devtools-mcp`: new_page → localhost:8000 → wait for Unity loader → screenshot → list_console_messages → close_page
- **User hands-on verification in their browser** — ship only after explicit approval

### Manual play-verify gates (from roadmap)
- Cores are immediately, obviously distinguishable from dirt at a glance
- Missiles home to core through dirt; railgun tunnels to core
- Destroying a meteor still pays out; destroying only dirt pays nothing
- Coreless meteors drift off-screen without being targeted
- No test regressions in either suite

## Risk

- **Low-medium.** The largest risk is the rewrite of `ApplyBlast` and `ApplyTunnel` — both have edge cases (walk-inward nearest-alive fallback, half-cell walker steps, max-step termination) that the current Iter 0 tests cover. Keeping those tests green through the HP rewrite is the gate.
- **Watch:** the PlayMode hit matrix is parameterized; a careless update to the helper could mass-break all 12 cases. Prefer updating the helper once to assert `CoreVoxelCount` rather than tweaking each case.
- **Watch:** removing the railgun's post-fire aim invalidation is a deliberate partial reversal of an Iter 0 mitigation. The multi-HP cores make it safe, but the `Railgun_MultipleShots_DrainsStationaryMeteor` test needs to keep asserting progressive damage — if it regresses, the reversal is wrong.
