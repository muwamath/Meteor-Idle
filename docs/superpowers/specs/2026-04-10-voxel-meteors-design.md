# Voxel Meteors + Voxel Art Pass — Design

**Date:** 2026-04-10
**Status:** Approved for implementation planning
**Replaces/extends:** `2026-04-10-meteor-idle-mvp-design.md`

## 1. Context

The MVP's meteors are smooth lumpy circles with binary HP. Playtesting exposed three things to change:

1. **Spawn rate is too high.** At steady state there are ~20 meteors on screen, which makes the turret feel broken.
2. **The aesthetic doesn't have an identity.** The user wants a voxel/chunky-pixel look — meteors should be brown cubes, the missile trail should be pixel sparks rather than a smooth line, and the other assets should match.
3. **Destruction should be partial and continuous.** Instead of binary kill/no-kill, a meteor should be a cluster of voxels that the missile chews apart. Each missile hit destroys some voxels and pays the player for each one destroyed. Meteors that escape to the ground can still have been partially harvested.

This design covers the minimum work to land all three changes cohesively. Deferred items are listed in §10.

## 2. Goals and Non-Goals

### Goals
- Replace the smooth `Sprite`-based meteor with a grid of voxels that can be partially destroyed.
- Money reward is computed per destroyed voxel at a flat $1/voxel rate.
- The `Damage` and `Blast Radius` turret stats map naturally onto the new mechanic.
- Missile trail becomes a pixel-spark particle effect (no smooth line).
- Other art assets are reworked for a consistent voxel/chunky-pixel aesthetic: turret base, barrel, missile body, explosion / debris / muzzle flash particles, starfield background.
- Cut the steady-state meteor spawn rate roughly 3×.

### Non-Goals (explicitly deferred)
- Per-voxel value variation (gold/rare cells worth more). Planned as part of a future "risk/reward level system."
- Meteor upgrade/leveling system.
- Shrinking or recomputing the `CircleCollider2D` as voxels disappear. The circle stays at its original radius; dead-space hits resolve as zero-voxel destructions.
- Polygon/mesh-based meteor colliders.
- Ground line voxelification.
- Pixel-font UI text (TMP stays as-is).
- Any rebalancing of turret starting stats.

## 3. Voxel Meteor — Data and Rendering

### Grid and shape generation

A meteor holds a **10×10** grid of booleans (`bool[,] voxels`). At spawn time a per-meteor seed picks a noise-perturbed disk, rasterized into the grid:
- Center at (4.5, 4.5), base radius 4.5 cells.
- Radius at angle θ is perturbed by three summed sine waves (same noise style as the current `MeteorShapeGenerator`), amplitude ±25%.
- Each grid cell is "present" if the cell center lies within the perturbed radius.
- Typical filled count: ~40–60 voxels, varying with seed.

The grid is generated once and stored on the meteor. Texture rendering derives from the grid.

### Texture rendering

One `Texture2D` per meteor, sized **150×150** (10 voxels × 15 px/voxel). For each present voxel, the 15×15 pixel block is filled with:
- Flat brown fill (vertical gradient `#8B7355` top → `#4A3A2A` bottom, sampled by voxel row).
- A 1-px highlight on the top-left edges of each voxel (slightly lighter tone).
- A 1-px dark edge on the bottom-right edges.

This yields a Lego-brick / Minecraft-dirt look without needing true 3D.

Texture import: same settings as the current generator (point filter? — no, bilinear is fine at 15px cells since pixels are already large). Pivot 0.5,0.5; 100 pixels/unit → each meteor is 1.5 world units across at `sizeScale=1`.

### Voxel destruction → texture update

When voxels are destroyed, the affected 15×15 blocks are painted transparent (`Color.clear`) and `tex.Apply()` re-uploads. This is cheap — a handful of `SetPixels` calls per hit is well within frame budget. No per-voxel GameObjects.

### Per-meteor state

```csharp
// On Meteor.cs
private bool[,] voxels;            // 10x10, true = present
private Texture2D texture;         // owned and mutated per meteor instance
private Sprite sprite;             // wraps texture, assigned once
private int aliveVoxelCount;       // for pool return when == 0
```

**Note:** Because each meteor mutates its own texture, we cannot cache textures globally by seed (as the current generator does). Each spawn allocates a fresh `Texture2D`. On pool return, the texture is destroyed to avoid leaks. This trades a small per-spawn cost for clean lifecycle.

## 4. Hit Resolution — `Meteor.ApplyBlast`

### Signature

```csharp
// Returns the number of voxels destroyed by this blast.
public int ApplyBlast(Vector3 worldImpactPoint, float worldRadius);
```

### Algorithm

1. Transform `worldImpactPoint` into the meteor's local space → `Vector2 localImpact`.
2. Convert to voxel-grid coordinates (accounting for the sprite pivot, world-to-voxel scale, and `transform.localScale`).
3. Compute the voxel-space radius from `worldRadius`.
4. Iterate a bounding box around the impact in grid coordinates. For each cell whose center is within `worldRadius` of the impact *and* is currently `present`:
   - Mark the cell destroyed (`voxels[x,y] = false`)
   - Paint the cell's 15×15 pixel block transparent in the texture
   - Spawn a small voxel-chunk particle from the cell's world position (see §5 "Debris")
   - Increment the destroyed counter
5. Call `tex.Apply()` once (not per voxel).
6. Decrement `aliveVoxelCount` by the destroyed count. If it reaches 0, pool-return the meteor.
7. Return the destroyed count.

### Missile-side contract

`Missile.OnTriggerEnter2D` now does:

```csharp
var meteor = other.GetComponentInParent<Meteor>();
if (meteor == null || meteor.AliveVoxelCount == 0) return;

float totalRadius = impactRadius + blastRadius; // see §7
int destroyed = meteor.ApplyBlast(transform.position, totalRadius);

if (destroyed > 0)
{
    GameManager.Instance.AddMoney(destroyed); // $1 per voxel
    Spawn(floatingTextPrefab, $"+${destroyed}", transform.position);
}

PlayExplosion();
Despawn();
```

The splash loop that finds *other* meteors in `Physics2D.OverlapCircleAll` is removed. Each missile affects at most one meteor. (Multi-meteor splash is a future feature.)

### Collider

Remains a single `CircleCollider2D` on the meteor root, unchanged radius. This over-covers destroyed cells. A missile that enters through a hole will still trigger, but `ApplyBlast` will find 0 cells in range and return 0 — the missile despawns without earning money. Looks like "missile missed the voxels and passed through."

## 5. Visual Effects Updated for Voxel Aesthetic

### Missile pixel-spark trail

Replace the `TrailRenderer` entirely (do not keep both). Add a `ParticleSystem` child on the missile prefab configured as:

- **Shape:** Point (single emission point at the missile's tail)
- **Emission:** `rateOverTime = 60` while missile is alive
- **Start lifetime:** 0.3s
- **Start size:** random 0.08–0.12 world units (crisp small squares)
- **Start speed:** 1–3 world units/sec in local −Y (drift backward relative to missile)
- **Velocity inheritance:** 0 (sparks don't inherit missile velocity; they lag)
- **Start color:** random between `#FFE040` and `#FFB020` (bright yellow-orange)
- **Color over lifetime:** full → orange → dark red → transparent
- **Rotation over lifetime:** 0 (axis-aligned squares, no spin — that's what keeps them reading as pixels)
- **Renderer material:** reuse `ParticleMat` but swap to square sprite (see below)

### Particle material swap

Current `ParticleMat` uses `Assets/Art/particle.png` (a soft round gradient dot). Swap to a hard-edged square: either reuse existing `square.png` or add a new `pixel.png` (a flat white square with 1-px dark border). All particle systems (`DebrisBurst`, `ExplosionBurst`, `MuzzleFlash`, missile sparks) share the same material, so this is a one-line material swap that voxelifies every particle effect at once.

### DebrisBurst (meteor voxel destruction)

Used for per-voxel chunks spawned when a meteor is chewed. Tuning:
- One burst spawned per destroyed voxel, at the voxel's world position.
- Burst count: 1 particle per voxel (so each destroyed voxel emits exactly one flying cube chunk — feels like the cube *is* the chunk).
- Start size: 0.12 world units (matches voxel size).
- Start velocity: random outward from meteor center + slight upward kick.
- Gravity: 1.5.
- Lifetime: 0.8s.
- Color: brown, matches meteor palette.

### ExplosionBurst (missile impact flash)

- 12 square particles, bright yellow-orange, no gravity.
- Lifetime 0.4s, radial outward velocity.
- Shares the new square material.

### MuzzleFlash

- 6 square particles, bright white-yellow.
- Lifetime 0.12s.

## 6. Voxel Art Pass

All art assets are regenerated as PNGs via the editor-script approach used for the MVP. No runtime art generation beyond what already exists for meteors.

### turret_base.png — 64×48 @ 100 ppu

Blocky stack with visible voxels:
- Base row: dark grey `#303038`, 4-pixel-tall brick row.
- Middle row: mid grey `#484858`, 4-pixel-tall row.
- Top row: lighter `#5A6070`, 4-pixel crenellated top (alternating filled/empty blocks).
- Each 8×4 block outlined with a 1-pixel dark border so the "bricks" are readable.

### turret_barrel.png — 16×48 @ 100 ppu

A 2×6 voxel stack (8×8 pixel voxels):
- Bottom voxel: dark grey `#303038` (base).
- Middle 4 voxels: mid grey `#484858`.
- Top voxel: bright grey `#808090` (the "tip").
- 1-pixel dark outline on each voxel.

### missile.png — 12×20 @ 100 ppu

A 2×4 voxel arrangement (6×5 pixel voxels):
- Top voxel pair: bright `#FFE040` (warhead).
- Middle voxel pair: orange `#FF8030`.
- Bottom voxel pair: dark red `#80200A` (tail).
- 1-pixel dark outline.

### background.png — 512×288, unchanged gradient, voxelified stars

Same navy-to-black gradient, but stars become 2×2 or 3×3 pixel clumps (random roll per star) at varying brightness. Keeps the density of ~200 stars.

### particle.png / square.png

Keep the existing `square.png` (4×4 flat white) as the single particle texture. Update `ParticleMat.mainTexture` to reference it. Delete or leave `particle.png` in place — it's unreferenced after the swap; keep for now in case we want a round variant later.

### ground.png

Unchanged. The ground line is cosmetic and the current thin gradient reads fine against the voxel aesthetic.

## 7. Stat Reinterpretation

The 5 `TurretStats` stats stay in place with starting values unchanged. Reinterpretation:

| Stat | Before | After |
|---|---|---|
| Fire Rate | shots/sec | shots/sec (unchanged) |
| Missile Speed | units/sec | units/sec (unchanged) |
| **Damage** | HP per hit | **Impact radius in world units** — formula: `impactRadius = 0.05 + 0.02 * CurrentValue`. At starting `Damage=1` → 0.07 units → destroys ~1 voxel at the point of impact. At `Damage=10` → 0.25 units → ~3×3 cluster. |
| **Blast Radius** | Splash damage radius | **Additive splash radius** — added to `impactRadius`. Total voxel destruction radius = `impactRadius + blastRadius`. Starting `blastRadius=0` → no splash beyond the impact. |
| Accuracy | Miss angle wobble | unchanged |

The `Damage` and `Blast Radius` upgrade buttons now feel like "bigger hits" and "wider hits" respectively — natural mental model.

## 8. Spawn Rate

`MeteorSpawner` default tuning:

| Field | Before | After |
|---|---|---|
| `initialInterval` | 2.5s | 4.0s |
| `minInterval` | 0.5s | 1.5s |
| `rampDurationSeconds` | 120s | 180s |

Cuts steady-state meteor rate roughly 3× and slows the ramp. These are the new starting values in the `[SerializeField]` defaults; can still be overridden per scene if needed.

## 9. Files Touched

### Rewrite
- `Assets/Scripts/Meteor.cs` — voxel grid, `ApplyBlast`, texture ownership/disposal. Remove `hp`, `maxHp`, `reward`, `TakeDamage`.
- `Assets/Scripts/MeteorShapeGenerator.cs` → **rename** to `VoxelMeteorGenerator.cs`. New static: `Generate(seed, out bool[,] voxels, out Texture2D texture)`. No caching (each meteor owns its texture).

### Modify
- `Assets/Scripts/Missile.cs` — replace HP-based call with `ApplyBlast`, remove the splash `OverlapCircleAll` loop.
- `Assets/Scripts/MeteorSpawner.cs` — update default spawn timing.

### Regenerate art + particles (via editor script)
- `Assets/Art/turret_base.png`
- `Assets/Art/turret_barrel.png`
- `Assets/Art/missile.png`
- `Assets/Art/background.png`
- `Assets/Art/ParticleMat.mat` — swap `_BaseMap` / `mainTexture` to `square.png`

### Rebuild prefabs
- `Assets/Prefabs/Missile.prefab` — remove `TrailRenderer` child, remove `TrailMat.mat` asset, add sparks `ParticleSystem` child.
- `Assets/Prefabs/Meteor.prefab` — update if the `Meteor.cs` serialized field surface changes.
- `Assets/Prefabs/DebrisBurst.prefab`, `ExplosionBurst.prefab`, `MuzzleFlash.prefab` — retune to square sprites & new sizes (handled by the shared material swap plus per-system tuning).

### Revert
- Remove the `// TEMP: 1-HP meteors` block in `Meteor.cs` — it's being replaced entirely.

## 10. Deferred (explicitly not in this iteration)

- Meteor level/upgrade system (risk/reward scaling per-voxel value).
- Gold/rare voxel variants.
- Shrinking collider as voxels disappear.
- Multi-meteor splash from a single missile.
- Voxelified ground line.
- Pixel UI font.
- Audio pass.

## 11. Verification

Manual in-editor, same as the MVP spec.

- [ ] Meteors spawn as chunky voxel grids, visibly different shape per seed.
- [ ] Firing a missile at a meteor removes a cluster of voxels at the impact point.
- [ ] The remainder of the meteor keeps falling with the hole intact.
- [ ] Money counter increases by exactly the number of destroyed voxels per hit.
- [ ] Floating `+$N` text shows the destroyed count.
- [ ] Upgrading Damage visibly destroys more voxels per hit.
- [ ] Upgrading Blast Radius visibly widens the destruction.
- [ ] Missile trail is a stream of square sparks, no smooth line.
- [ ] Turret base, barrel, and missile body are visibly blocky (pixel voxels, not smooth shapes).
- [ ] Starfield stars are 2×2/3×3 clumps, not single pixels.
- [ ] Spawn rate feels calm at game start; doesn't reach overwhelming density in the first 3 minutes.
- [ ] No console errors or warnings during a 2-minute play session.
