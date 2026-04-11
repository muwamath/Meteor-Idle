# Railgun weapon — design spec

**Date:** 2026-04-10
**Status:** Brainstormed, awaiting user review of written spec before implementation-plan phase.
**Branch:** `iter/railgun`

## Context

Meteor Idle currently ships one weapon type: a homing Missile turret. The `WeaponType` enum and `BuildSlotPanel` were built with extensibility in mind (the buy panel already spawns a button per weapon in a serialized list), but there's only one entry today. Picking a second weapon to add validates the extensibility plumbing, gives the player a real strategic choice at slot-build time, and sets up the pattern for future weapons.

The **Railgun** is the chosen second weapon. Its identity: a slow-firing, heavy straight-line weapon that drills **tunnels** through voxel meteors instead of cratering them with a blast radius like the Missile does. Where the Missile is a "spray of little impacts over time" weapon, the Railgun is a "wait, aim, commit, huge satisfying punch-through" weapon. The two weapons should feel mechanically different enough that choosing between them when buying a base slot is a real decision, not a flavor toggle.

Secondary goals for this feature:
- Exercise the voxel aesthetic rules (see `feedback_voxel_aesthetic.md` memory) on a new visual-effect-heavy weapon — barrel, bullet, streak all procedural voxel art, no `LineRenderer` or smooth gradients.
- Stand up a **PlayMode test harness** alongside the existing EditMode suite, because the Railgun's per-frame raycast damage model depends on real physics and can't be covered by EditMode tests alone. The user explicitly asked to "make sure the base play is always solid" as new features land — so baseline PlayMode tests for the existing missile weapon, meteor fade, and spawner pooling ship in the same branch as the first prep phase.

## Gameplay mechanic

### The round

- Fires out of the top center of the `][` barrel in the barrel's current aim direction.
- Travel velocity is a new upgradable **Speed** stat. At base level (6 world/sec), the white bullet is visibly flying across the play area — the player can track it with their eyes. At high levels (multiplicative scaling, level 10 ≈ 346 world/sec), travel is effectively hit-scan.
- The round has **no Rigidbody2D and no Collider2D**. It is a visual `SpriteRenderer` that moves via `transform.position += direction * speed * Time.deltaTime`. Damage is resolved by per-frame `Physics2D.RaycastAll` against the `Meteors` layer only (see Architecture § 5.2).

### Tunneling through a meteor

- When the per-frame raycast reports a meteor intersection, the round calls `Meteor.ApplyTunnel(entryWorld, direction, remainingWeight, caliber, out exitWorld)`.
- `ApplyTunnel` walks the meteor's 10×10 voxel grid along the round's direction, destroying **live voxels only**. Empty voxels (from earlier damage) are free — the round glides past without consuming budget.
- Each live voxel consumed subtracts 1 from the round's **Weight** budget. When the budget hits zero, the round despawns.
- **Caliber** controls the width of the tunnel perpendicular to travel:
  - Caliber 1 (base): 1-cell-wide line
  - Caliber 2: 3-cell-wide band (center + one neighbor on each side)
  - Caliber 3 (max): 5-cell-wide band
- The tunnel continues until budget is exhausted OR the path exits the meteor's voxel grid on the far side. When it exits, the `out exitWorld` parameter reports where — used for piercing to the next meteor.

### Piercing to the next meteor

- After a tunnel call returns, if `remainingWeight > 0`, the round keeps moving. The per-frame raycast picks up the next meteor in its path (if any) and starts a new tunnel there with the leftover budget.
- A `HashSet<Meteor> alreadyTunneled` on the round prevents the same meteor being processed twice in a single round's lifetime (edge case: the round's path briefly re-enters a meteor via a curved collider boundary).
- There is no per-meteor cap on pierces — a round with Weight=20 that encounters three 5-voxel meteors in a line will damage all three.

### Targeting

- `RailgunTurret` uses the same base-class targeting logic as `MissileTurret` (scan `MeteorSpawner.ActiveMeteors`, pick the closest in-range live meteor, rotate the barrel toward its centroid). Range comes from the shared `TurretBase.range` field, default 30 world units (same as Missile — covers the whole playfield).
- At fire time, the turret calls `meteor.PickRandomPresentVoxel()` to pick a specific live cell to aim at. The barrel rotates toward that cell's world position, and fires when aligned within `aimAlignmentDeg`.
- **The round always collides with anything in its straight-line path, whether the turret intended it or not.** If a different meteor is between the turret and the aimed meteor, the round hits it first, tunnels as normal, then continues to the aimed target (and beyond) with whatever Weight remains. This is a direct user requirement — no phasing through meteors to reach a specific target.
- No homing. The round flies a dead-straight line from muzzle forward.

## Upgrade stats (5 total)

Railgun stats live in a new `RailgunStats` ScriptableObject asset (`Assets/Data/RailgunStats.asset`), parallel to the existing `TurretStats` asset for missiles. They are **not** shared with missiles — upgrading the Railgun's Fire Rate does not affect missile turrets and vice versa.

Costs for all stats default to `$1` base with `costGrowth=1`, matching the current dev-mode economy (`iter/everything-costs-one` → `main`, commit `4a2575d`). These values will be rebalanced in the upcoming economy overhaul.

| Stat | Base | Per-level | Scaling | Feel |
|---|---|---|---|---|
| **Fire Rate** | 0.2 shots/sec (5 s between shots) | +0.05/level | Additive | Doubles as the barrel charge-up timer. Barrel color steps from dead white → `#93DAFE` over `1/FireRate` seconds, fires at full blue, snaps back to dead white. Base is deliberately sluggish. |
| **Rotation Speed** | 20 °/sec | +12 °/sec | Additive | Slow default — at base Fire Rate + base Rotation Speed, early railguns feel deliberate and ponderous. Upgrades make them snappy. |
| **Speed** | 6 world/sec | +3/level | Additive | Key "snappiness" stat. At base, several seconds to reach distant targets — the white bullet is clearly visible in flight. At level 10 (36 world/sec), well under a second to reach any target on screen — reads as a blur with a streak. Further reasoning in § 5.3. |
| **Weight** | 4 voxels | +2/level | Additive | Depth budget — number of live voxels the round destroys before despawning. Level 0 punches a small crater; level 10 (Weight=24) reliably pierces two average meteors in a line. |
| **Caliber** | 1 voxel | +1/level, max level 2 (→ width 3) | Discrete, hard-capped | Tunnel width. Only 2 upgrade steps possible — a discrete power-spike stat, not a slow grind. |

Notes:
- **No Damage stat.** Railgun damage is binary per voxel — a cell is destroyed or not. The Missile's "Damage → impactRadius" formula doesn't apply.
- **No Homing.** The round is dead-straight by design. If the player wants homing, they use Missiles.
- **No separate Charge Time.** Fire Rate IS the charge time — one stat, one visible bar on the barrel.

## Build cost and slot economy

**Per current dev-mode policy (everything costs $1 until the economy overhaul), Railgun build cost is $1 per slot**, same as Missile. The `SlotManager` gets a second cost array parallel to the existing missile one:

```csharp
[SerializeField] private int[] missileBuildCosts = { 1, 1 };  // already flat $1
[SerializeField] private int[] railgunBuildCosts = { 1, 1 };  // new, also flat $1
```

`SlotManager.NextBuildCost(WeaponType weapon)` becomes the entry point — the `BuildSlotPanel` queries it per weapon when rendering the buy buttons. The `builtPurchasedCount` counter stays shared across weapons (whichever weapon you buy for your Nth slot, it counts as your Nth purchase for escalation purposes).

When the economy overhaul lands, the intent is for Railgun to be **more expensive than Missile** as a "premium alternative" — somewhere around 2× Missile base cost, with per-slot escalation on top. The data model already supports that; only the numbers need to change.

**`BuildSlotPanel` UX:**
- Title drops the cost (was "BUILD BASE — $X"), becomes just **"BUILD BASE"**.
- Each weapon button shows its own cost on the button label: `"Missile\n$1"`, `"Railgun\n$1"`.
- Each button has its own affordability check — gray out if the player can't afford that specific weapon.
- Clicking a button charges the right cost via `SlotManager.NextBuildCost(weapon)` and builds the slot with that weapon type.

## Visuals (voxel aesthetic)

Every visual element must follow the voxel aesthetic rules captured in `memory/feedback_voxel_aesthetic.md`: hard pixel edges, flat blocks of color, 1-px dark edges and ~20% brightened highlights, no smooth gradients, no `LineRenderer` / `TrailRenderer`, all textures with `filterMode=Point`, `textureCompression=Uncompressed`, `mipmapEnabled=false`.

### Barrel — `Assets/Art/railgun_barrel.png`

- Procedural PNG generated via `execute_code` editor script.
- Dimensions: **24 px × 60 px** (0.24 × 0.6 world units at 100 ppu). Comparable footprint to the existing missile barrel.
- Shape: two vertical bars, each **8 px wide × 60 px tall**, separated by an **8 px gap** — the `][` silhouette.
- Body color at rest: `#FFFFFF` (dead white).
- Per-bar edge treatment: 1 px of `#808080` on the left edge (shadow), 1 px of `#B0E8FF` on the right edge and top (highlight, slightly blue-tinted so the charge animation has somewhere to go).
- Pivot: bottom-center, so rotation pivots around the base of the `][`.
- Sits under a new `Barrel` child transform on the `RailgunWeapon` sub-GameObject of `BaseSlot` (see Architecture § 5.6). A `Muzzle` child transform sits at the top-center of the gap between the bars.

### Barrel charge animation — stepped, not smooth

- Managed by `RailgunTurret`. Uses a quantized 4-frame progression through these exact color stops:
  - `t = 0.00`: `#FFFFFF` (dead white, just after fire or at rest with no charge)
  - `t = 0.33`: `#CEE8FE` (first blue tint)
  - `t = 0.66`: `#A8D6FE` (mid blue)
  - `t = 1.00`: `#93DAFE` (the target color the user specified)
- Implementation: `sr.color = stops[Mathf.FloorToInt(Mathf.Clamp01(t) * stops.Length)]` where `t = chargeTimer / chargeDuration` and `chargeDuration = 1f / FireRate`.
- When the charge reaches `t = 1.0` AND a valid target is in range AND the barrel is aligned within `aimAlignmentDeg`: fire. Snap the color back to `#FFFFFF` instantly.
- When the charge reaches `t = 1.0` but there's no valid target: hold at `#93DAFE` (full blue) until a target appears, then fire on the first frame it's aligned.
- No smooth `Color.Lerp` — the 4 discrete stops are the whole animation. Matches the voxel aesthetic.

### Bullet — `Assets/Art/railgun_bullet.png`

- Procedural PNG, **8 px × 15 px** — half a voxel wide, full voxel tall, "bullet-shaped".
- Pure white body (`#FFFFFF`) with 1 px of `#CCCCCC` shadow on the bottom edge and 1 px of `#FFFFFF` highlight on the top edge.
- Mounted on the `RailgunRound` prefab's `SpriteRenderer`.
- Rotates at spawn time to align its long axis with the round's direction vector, same way `Missile.ApplyVelocityRotation` does today.

### Streak — `Assets/Art/railgun_streak.png`

- Procedural PNG, **4 px × 2 px** base texture.
- Outer 1 px border: `#5EA8CE` (20% darker blue edge for the voxel shadow treatment).
- Inner 2×0 pixels: `#93DAFE` (the target blue).
- Used as a stretched sprite: a GameObject with a `SpriteRenderer` referencing this texture, positioned at the midpoint of the round's trajectory, rotated to align with its direction, and scaled along its X axis to match the line length in world units.
- **Scale Y (perpendicular thickness) scales with Caliber:** base (Caliber 1) = scale y 1, Caliber 2 = scale y 1.5, Caliber 3 = scale y 2. At point filtering, the 2-px-thick source becomes 2/3/4 px visible.
- **Fade-out:** 2-second lifetime. Alpha steps through 4 quantized levels: 1.0 → 0.66 → 0.33 → 0.0. Each step is a visible chunky drop. Then the GameObject is destroyed. No smooth fade.
- **When it spawns:** at the moment the round itself despawns (budget exhausted or left the screen), not during flight. For slow-level railguns, you watch the bullet fly, and at the moment of final death the blue streak appears showing the entire path from muzzle to death point. For max-level near-hit-scan, bullet travel and streak spawn are functionally the same frame.
- **Pierce behavior:** one single streak per shot, spanning muzzle to final despawn point. Not per-meteor segments.

### Muzzle flash

- Reuses the existing `muzzleFlash` `ParticleSystem` SerializeField slot on `TurretBase`.
- `RailgunRound` weapon prefab's muzzle flash particle system uses the existing `square.png` / `particle.png` voxel particle textures (which are already voxel-style), tinted `#93DAFE`.
- Short burst: ~0.1 s duration, 3–5 particles, no smooth falloff.

### Voxel chunk bursts on tunnel hits

- `ApplyTunnel` reuses the existing `voxelChunkPrefab` ParticleSystem reference on the meteor (same system used by `ApplyBlast`). Every destroyed voxel spawns a burst. No new VFX needed — the existing pipeline already matches the voxel aesthetic.

## Architecture

### 5.1 — `TurretBase` abstract class, `MissileTurret`/`RailgunTurret` subclasses

Split the existing `Turret.cs` into:

- **`TurretBase.cs`** (abstract) — holds all the shared logic: targeting (`FindTarget`), rotation, reload timer, the `Update` loop that scans/rotates/fires. Has protected serialized fields for `barrel`, `muzzle`, `muzzleFlash`, `meteorSpawner`, `range`, `aimAlignmentDeg`. Exposes two abstract properties (`FireRate`, `RotationSpeed`) and one abstract method (`Fire(Meteor target)`), all implemented by subclasses.
- **`MissileTurret.cs`** — renamed current `Turret.cs`. Owns the missile-specific state: `TurretStats stats`, `Missile missilePrefab`, `SimplePool<Missile> missilePool`, `missilePoolParent`. Its `Fire()` pulls a missile from the pool and launches it exactly like today. Its `FireRate`/`RotationSpeed` read from `stats.fireRate.CurrentValue` / `stats.rotationSpeed.CurrentValue`.
- **`RailgunTurret.cs`** — new. Owns `RailgunStats stats`, `RailgunRound roundPrefab`, barrel color state. Its `Fire()` spawns a `RailgunRound` and configures it. Its `FireRate`/`RotationSpeed` read from the `RailgunStats` asset.

The `BaseSlot.cs` field type changes from `Turret turret` to `TurretBase turret` (or two explicit references, see § 5.6) so it can hold either concrete subclass via polymorphism.

### 5.2 — `RailgunRound.Update` per-frame forward raycast (NOT fire-time raycast)

**This is the key implementation decision.** The round is a visual-only GameObject that does its own per-frame raycasts against the `Meteors` physics layer. No `Rigidbody2D`, no `Collider2D`, no `OnTriggerEnter2D`. Full code sketch:

```csharp
public class RailgunRound : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private GameObject streakPrefab;

    private Vector3 direction;
    private float speed;
    private int remainingWeight;
    private int caliber;
    private Vector3 spawnPoint;
    private readonly HashSet<Meteor> alreadyTunneled = new();
    private static readonly int meteorLayerMask = -1; // resolved in Awake

    public void Configure(Vector3 spawnPos, Vector3 dir, float speed, int weight, int caliber)
    {
        transform.position = spawnPos;
        spawnPoint = spawnPos;
        direction = dir.normalized;
        this.speed = speed;
        remainingWeight = weight;
        this.caliber = caliber;
        alreadyTunneled.Clear();

        // rotate sprite to match direction (long axis along travel)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    private void Awake()
    {
        // cache layer mask — fails loudly if the Meteors layer isn't set up
        int layer = LayerMask.NameToLayer("Meteors");
        if (layer < 0) Debug.LogError("[RailgunRound] Meteors layer not defined", this);
    }

    private void Update()
    {
        if (remainingWeight <= 0) { Despawn(); return; }

        float stepDistance = speed * Time.deltaTime;
        var hits = Physics2D.RaycastAll(
            origin: transform.position,
            direction: direction,
            distance: stepDistance,
            layerMask: LayerMask.GetMask("Meteors"));
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (remainingWeight <= 0) break;
            var meteor = hit.collider.GetComponentInParent<Meteor>();
            if (meteor == null || !meteor.IsAlive) continue;
            if (alreadyTunneled.Contains(meteor)) continue;

            int consumed = meteor.ApplyTunnel(
                entryWorld: hit.point,
                worldDirection: direction,
                budget: remainingWeight,
                caliberWidth: caliber,
                out _);
            remainingWeight -= consumed;
            alreadyTunneled.Add(meteor);

            if (consumed > 0 && GameManager.Instance != null)
                GameManager.Instance.AddMoney(consumed);
        }

        transform.position += direction * stepDistance;

        if (OffScreen(transform.position)) { Despawn(); return; }
    }

    private void Despawn()
    {
        // Spawn the stretched streak sprite from spawnPoint to current position
        SpawnStreak(spawnPoint, transform.position);
        Destroy(gameObject);
    }

    private bool OffScreen(Vector3 pos) =>
        pos.y > 10f || pos.y < -10f || Mathf.Abs(pos.x) > 17f;
}
```

Why this interacts cleanly with missiles:
- `LayerMask.GetMask("Meteors")` means missile colliders (on the `Default` layer) are never returned by the raycast. Missiles are invisible to railgun rounds.
- Missile collision stays unchanged — they continue to use `Rigidbody2D` + `CircleCollider2D` + `OnTriggerEnter2D`. The railgun doesn't touch that path at all.
- A meteor hit by both a missile (via trigger in `FixedUpdate`) and a railgun (via raycast in `Update`) processes them sequentially within a frame. Unity frame order: `FixedUpdate` → physics triggers → `Update`. So the railgun's `Update` sees the post-missile state of the meteor. No double-counting, no race conditions.
- Continuous-collision tuning is irrelevant because the round is manually advancing in frame-sized chunks, each chunk doing a raycast across the full chunk distance. At any speed from 1 world/sec to 10,000 world/sec, every meteor the round crosses gets picked up in the corresponding frame's raycast. Manual CCD.

### 5.3 — `RailgunStats` ScriptableObject

New class, parallel shape to `TurretStats`, five stats. Not a subclass — clean separation:

```csharp
[CreateAssetMenu(fileName = "RailgunStats", menuName = "Meteor Idle/Railgun Stats")]
public class RailgunStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public RailgunStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1f;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
    }

    public Stat fireRate      = new Stat { id = RailgunStatId.FireRate,      displayName = "Fire Rate",      baseValue = 0.2f, perLevelAdd = 0.05f, baseCost = 1, costGrowth = 1f };
    public Stat rotationSpeed = new Stat { id = RailgunStatId.RotationSpeed, displayName = "Rotation Speed", baseValue = 20f,  perLevelAdd = 12f,   baseCost = 1, costGrowth = 1f };
    public Stat speed         = new Stat { id = RailgunStatId.Speed,         displayName = "Speed",          baseValue = 6f,   perLevelAdd = 3f,    baseCost = 1, costGrowth = 1f };
    public Stat weight        = new Stat { id = RailgunStatId.Weight,        displayName = "Weight",         baseValue = 4f,   perLevelAdd = 2f,    baseCost = 1, costGrowth = 1f };
    public Stat caliber       = new Stat { id = RailgunStatId.Caliber,       displayName = "Caliber",        baseValue = 1f,   perLevelAdd = 1f,    baseCost = 1, costGrowth = 1f };

    public event Action OnChanged;

    public Stat Get(RailgunStatId id) { /* switch */ }
    public IEnumerable<Stat> All() { /* yield all 5 */ }
    public void ApplyUpgrade(RailgunStatId id) { /* increment, fire OnChanged */ }
    public void ResetRuntime() { /* zero all levels, fire OnChanged */ }
}

public enum RailgunStatId { FireRate, RotationSpeed, Speed, Weight, Caliber }
```

Speed uses **additive `perLevelAdd = 3f`**, not multiplicative. Level 0 = 6, level 10 = 36. Earlier in brainstorming I described multiplicative ×1.5 scaling reaching ~346 at level 10, but (a) the rest of the stat system is additive, (b) 36 world/sec is still "very fast" relative to base missile 4 world/sec, and (c) the manual-CCD raycast approach means we don't actually need to push into the 300+ range to get a convincing hit-scan feel — 36 world/sec crosses the screen in under a second, which reads as "very fast" already. Keeping the stat system uniform is worth more than squeezing the last few multiples of speed out of the upgrade track.

An asset instance at `Assets/Data/RailgunStats.asset` is created via `execute_code` at implementation time.

### 5.4 — Weapon-contextual upgrade panels

Two separate panels rather than one contextual panel with runtime button swapping:

- **`MissileUpgradePanel.cs`** — the existing `UpgradePanel.cs`, renamed. Handles `TurretStats`. No behavior change.
- **`RailgunUpgradePanel.cs`** — new. Parallel shape, handles `RailgunStats`. Same button-building pattern, same `ModalClickCatcher` sibling for click-outside-to-close.

`BaseSlot.cs` gains two panel references:

```csharp
[SerializeField] private CanvasGroup upgradePanelMissile;
[SerializeField] private CanvasGroup upgradePanelRailgun;
```

`HandleClick` routes based on `builtWeapon`:

```csharp
if (IsBuilt) {
    var panel = builtWeapon == WeaponType.Railgun ? upgradePanelRailgun : upgradePanelMissile;
    ToggleUpgradePanel(panel);
}
```

Each panel has its own `UpgradeClickCatcher*` sibling under `UI Canvas`. Two catchers total — reuses the existing `ModalClickCatcher` component unchanged.

Rationale for two panels instead of one: simpler mental model, no runtime button rebuilding, panels can have totally different layouts (missile has Launcher + Missile columns; railgun might want different grouping). Code duplication is ~60 lines in the panel class — acceptable for 2 weapons. Abstraction can wait until a 3rd weapon ships.

### 5.5 — `Meteor.ApplyTunnel` new method

```csharp
public int ApplyTunnel(
    Vector3 entryWorld,
    Vector3 worldDirection,
    int budget,
    int caliberWidth,
    out Vector3 exitWorld)
{
    exitWorld = entryWorld;
    if (dead || aliveCount == 0 || budget <= 0) return 0;

    // Convert entryWorld to meteor-local grid coordinates (same math as ApplyBlast)
    Vector3 localEntry = transform.InverseTransformPoint(entryWorld);
    Vector3 localDir = transform.InverseTransformDirection(worldDirection).normalized;
    const float halfExtent = 0.75f;
    float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);

    float gx = (localEntry.x + halfExtent) * localToGrid;
    float gy = (localEntry.y + halfExtent) * localToGrid;
    float dx = localDir.x; // grid-space direction is the same as local direction
    float dy = localDir.y;

    // Step the ray cell-by-cell. At each step, destroy all live cells within a
    // perpendicular half-width band of (caliberWidth - 1) cells (so caliber=1 = 1 cell,
    // caliber=2 = 3 cells, caliber=3 = 5 cells).
    int consumed = 0;
    int halfBand = caliberWidth - 1;  // 0, 1, 2
    // Perpendicular to (dx, dy) is (-dy, dx)
    float perpX = -dy;
    float perpY = dx;

    // Walk in ~0.5-cell steps along the ray until we exit the grid or run out of budget.
    int maxSteps = VoxelMeteorGenerator.GridSize * 4;  // safe upper bound
    for (int step = 0; step < maxSteps; step++)
    {
        if (budget <= 0) break;

        // For each perpendicular offset in [-halfBand, halfBand], try to destroy the cell
        for (int offset = -halfBand; offset <= halfBand; offset++)
        {
            float cellX = gx + perpX * offset;
            float cellY = gy + perpY * offset;
            int ix = Mathf.FloorToInt(cellX);
            int iy = Mathf.FloorToInt(cellY);
            if (ix < 0 || ix >= VoxelMeteorGenerator.GridSize) continue;
            if (iy < 0 || iy >= VoxelMeteorGenerator.GridSize) continue;
            if (!voxels[ix, iy]) continue; // empty — free, don't consume budget

            // Live cell — destroy it, consume 1 budget
            voxels[ix, iy] = false;
            VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
            consumed++;
            budget--;
            // spawn chunk particle (same as ApplyBlast)
            if (voxelChunkPrefab != null)
            {
                Vector3 worldVoxel = VoxelCenterToWorld(ix, iy);
                var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                burst.Play();
                Destroy(burst.gameObject, 1.5f);
            }
            if (budget <= 0) break;
        }

        // Advance half a cell along the ray
        gx += dx * 0.5f;
        gy += dy * 0.5f;

        // Check if we exited the grid on this step
        if (gx < 0 || gx >= VoxelMeteorGenerator.GridSize || gy < 0 || gy >= VoxelMeteorGenerator.GridSize)
            break;
    }

    if (consumed > 0) texture.Apply();
    aliveCount -= consumed;
    if (aliveCount <= 0)
    {
        dead = true;
        owner?.Release(this);
    }

    // Compute exit world point — the point in world space where the ray exits the grid
    // or the last step position, whichever came first
    Vector3 localExit = new Vector3(gx / localToGrid - halfExtent, gy / localToGrid - halfExtent, 0f);
    exitWorld = transform.TransformPoint(localExit);

    return consumed;
}
```

Reuses: `transform.InverseTransformPoint` for scale/rotation-aware coord conversion, `VoxelMeteorGenerator.ClearVoxel` for texture painting, `voxelChunkPrefab` for debris burst, `owner.Release` for pool return when the meteor dies.

Key invariants:
- Empty voxels don't consume budget (pass-through holes are free).
- Budget is the only reason the tunnel stops early. If the ray exits the grid with budget remaining, that budget is returned to the caller via `budget - consumed` and the round pierces to the next meteor.
- `out exitWorld` reports the point where the walk terminated — either the far grid edge or the last step before budget ran out. The round's raycast picks up from just past this point on the next frame.

### 5.6 — `BaseSlot.prefab` restructure

Current hierarchy:
- `BaseSlot` (root, has `Turret` component + its own barrel + muzzle children)
- `PlusIcon` (empty-state indicator)

New hierarchy:
- `BaseSlot` (root, no weapon component — just the slot base sprite and the common `BaseSlot` / `BaseClickHandler` / `Collider2D` components)
  - `PlusIcon` (empty-state indicator)
  - `MissileWeapon` (child GameObject, initially inactive)
    - `MissileTurret` component
    - `MissileBarrel` child with the existing `turret_barrel.png` sprite
      - `Muzzle` child
  - `RailgunWeapon` (child GameObject, initially inactive)
    - `RailgunTurret` component
    - `RailgunBarrel` child with the new `railgun_barrel.png` sprite
      - `Muzzle` child

`BaseSlot.cs` tracks both weapon-child references:

```csharp
[SerializeField] private GameObject missileWeapon;
[SerializeField] private GameObject railgunWeapon;
```

- `SetEmpty()` deactivates both weapon children, activates `PlusIcon`.
- `Build(WeaponType.Missile)` activates `missileWeapon`, deactivates `railgunWeapon` and `PlusIcon`.
- `Build(WeaponType.Railgun)` activates `railgunWeapon`, deactivates `missileWeapon` and `PlusIcon`.
- `builtWeapon` is stored as a field so `HandleClick` can route to the correct upgrade panel.

This is the **highest-risk scene-edit phase** in the rollout because the current `BaseSlot.prefab` has many serialized references that the restructure will need to rewire (click handler, sprite renderer, turret fields, collider). Implementation will reload the scene fresh, do the restructure via `execute_code`, and scrub the scene/prefab diff for drift before save — same pattern used on every scene edit this session.

### 5.7 — `BuildSlotPanel` already supports multiple weapons

Minimal change: `BuildSlotPanel.weapons` array gains `WeaponType.Railgun`. The existing per-weapon button instantiation loop handles the rest. Each button's cost is looked up via `SlotManager.NextBuildCost(weapon)` at panel open time. Affordability refresh (already wired to `GameManager.OnMoneyChanged`) handles gray-out.

### 5.8 — File summary

**New files:**
- `Assets/Scripts/TurretBase.cs`
- `Assets/Scripts/MissileTurret.cs` (split from current `Turret.cs`)
- `Assets/Scripts/RailgunTurret.cs`
- `Assets/Scripts/Data/RailgunStats.cs`
- `Assets/Scripts/Weapons/RailgunRound.cs`
- `Assets/Scripts/UI/RailgunUpgradePanel.cs`
- `Assets/Data/RailgunStats.asset`
- `Assets/Art/railgun_barrel.png` (+ `.meta`)
- `Assets/Art/railgun_bullet.png` (+ `.meta`)
- `Assets/Art/railgun_streak.png` (+ `.meta`)
- `Assets/Prefabs/RailgunRound.prefab` (+ `.meta`)
- `Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef` (+ `.meta`)
- `Assets/Tests/PlayMode/PlayModeTestFixture.cs`
- `Assets/Tests/PlayMode/RailgunPlayModeTests.cs`
- `Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs`
- `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`
- `Assets/Tests/EditMode/RailgunStatsTests.cs`

**Modified files:**
- `Assets/Scripts/Meteor.cs` — add `ApplyTunnel` method
- `Assets/Scripts/BaseSlot.cs` — two weapon-child refs, two upgrade-panel refs, `builtWeapon` field, `HandleClick` routing
- `Assets/Scripts/UI/UpgradePanel.cs` — rename to `MissileUpgradePanel.cs`
- `Assets/Scripts/Weapons/WeaponType.cs` — add `Railgun = 1`
- `Assets/Scripts/SlotManager.cs` — `NextBuildCost(WeaponType)` overload + `railgunBuildCosts` field
- `Assets/Scripts/UI/BuildSlotPanel.cs` — add `Railgun` to `weapons` array
- `Assets/Prefabs/BaseSlot.prefab` — restructure into two weapon children
- `Assets/Scenes/Game.unity` — add `RailgunUpgradePanel` + `UpgradeClickCatcherRailgun` GameObjects under `UI Canvas`
- `CLAUDE.md` — two weapons, PlayMode tests, new physics layer, updated project layout

## Tests

### EditMode — new tests

**`Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`** (~10 tests)

Pattern: `NewMeteor()` helper (reuses the existing `TestHelpers.InvokeAwake`), direct `ApplyTunnel` calls, grid-state inspection.

| Test | Assertion |
|---|---|
| `FreshMeteor_VerticalTunnelFromBottom_CarvesStraightLine` | Budget=5, caliber=1, direction `(0,1)`, entry at bottom rim. Exactly 5 live voxels destroyed in a vertical column. |
| `EmptyVoxels_AreFreeAndDontConsumeBudget` | Pre-erode a column via `ApplyBlast`, then tunnel through the same column with budget=3. Budget still carves 3 cells on the far side of the hole. |
| `BudgetCap_StopsTunnelEarly` | Budget=2 through a thick column. Exactly 2 voxels destroyed, returned. |
| `BudgetExceedsLivePath_ReportsActualConsumed` | Budget=100 against a 5-voxel-thick column. Returns `consumed == 5`. |
| `Caliber2_CarvesThreeWideBand` | Caliber=2, budget large enough to cut through. Center line + 1 neighbor on each side are destroyed. |
| `Caliber3_CarvesFiveWideBand` | Caliber=3. 5-cell-wide band destroyed. |
| `DiagonalDirection_WalksCorrectly` | Direction `(1,1).normalized`, entry at bottom-left corner. Destroyed cells lie along the diagonal. |
| `ExitPoint_IsReturnedInWorldSpace` | `out Vector3 exitWorld` is populated with the terminating grid cell's world position. |
| `DeadMeteor_ReturnsZeroAndDoesNotThrow` | Mirror of the existing dead-meteor `ApplyBlast` test. |
| `AliveCount_DecrementsByConsumedAmount` | `AliveVoxelCount` before/after differs by exactly `consumed`. |

**`Assets/Tests/EditMode/RailgunStatsTests.cs`** (~7 tests)

Exact mirror of `TurretStatsTests.cs`, pointed at `RailgunStats`:

- `Stat_CurrentValue_BaseAtLevelZero`
- `Stat_NextCost_FollowsGrowthFormula` — at `costGrowth=1`, stays flat at `baseCost` forever (the dev-mode case); if `costGrowth > 1`, matches `baseCost × pow(growth, level)` rounded.
- `ApplyUpgrade_IncrementsLevel_AndFiresOnChanged`
- `ApplyUpgrade_AffectsOnlyTheTargetStat`
- `CurrentValue_GrowsLinearlyWithLevel`
- `Get_ReturnsCorrectStat` — all 5 stat IDs.
- `ResetRuntime_ZerosAllLevels_AndFiresOnChanged`

### PlayMode — new tests

**`Assets/Tests/PlayMode/PlayModeTestFixture.cs`** (shared helper)

- `SpawnTestMeteor(Vector3 pos, int seed=1, float scale=1f)` → fully-initialized meteor GameObject with a real Rigidbody2D + collider on the `Meteors` layer.
- `SpawnTestMissile(Vector3 pos)` → a parked missile with a collider, non-moving.
- `SpawnTestRound(Vector3 pos, Vector2 dir, float speed, int weight, int caliber)` → a `RailgunRound` configured and active.
- Setup: creates a temporary empty scene via `EditorSceneManager.NewScene`, ensures a `GameManager` and `MeteorSpawner` exist.
- Teardown: destroys all spawned test objects.

**`Assets/Tests/PlayMode/RailgunPlayModeTests.cs`** (3 tests)

| Test | Assertion |
|---|---|
| `RailgunRound_FiresIntoMeteor_DealsDamage` | Spawn a meteor at (0,3,0), a round at (0,0,0) with Speed=20 Weight=10 Caliber=1 direction up. `yield return new WaitForSeconds(0.4f)`. Meteor's `AliveVoxelCount` decreased. |
| `RailgunRound_PiercesTwoStackedMeteors` | Two meteors at (0,3) and (0,6), round at (0,0) with Weight=20. Wait 0.6s. Both meteors lost voxels. |
| `RailgunRound_LayerMask_IgnoresMissilesInPath` | Meteor at (0,5), parked missile at (0,2), round at (0,0) aimed up. Wait 0.4s. Meteor damaged; missile untouched and still `activeSelf==true`. |

**`Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs`** (3 tests)

Baseline coverage of existing mechanics. Landing in the same commit as the PlayMode infrastructure (Phase 0) to verify the existing game is still solid before any railgun code is touched.

| Test | Assertion |
|---|---|
| `Missile_LaunchedAtMeteor_Collides_DealsDamage` | Spawn meteor, spawn missile aimed at it via `Missile.Launch`. Wait until collision. Meteor lost voxels, missile despawned. |
| `Meteor_FallsAndFadesBelowThreshold_BecomesUntargetable` | Spawn meteor just above `fadeStartY=-7.88`, wait while it falls under its own velocity. Before crossing: `IsAlive==true`. After crossing + a frame: `IsAlive==false`. |
| `MeteorSpawner_SpawnsPooledMeteors_OverTime` | Run spawner for ~8s real time at base cadence. At least 2 meteors spawn. No extra `Instantiate` calls beyond the prewarm count (verified via `Transform.childCount` on the pool parent). |

### Total test count after railgun lands

| Mode | Before | Added | After |
|---|---|---|---|
| EditMode | 42 | 17 | 59 |
| PlayMode | 0 | 6 | 6 |

Expected runtime: EditMode ~2.5 s, PlayMode ~10 s. Both suites must pass before any commit during railgun development, and both must pass before `main` fast-forward.

## Implementation rollout

**13 commits on `iter/railgun`** (Phase 0 prep + 12 railgun phases), in this order. Each commit keeps the game buildable and playable.

### Phase 0 — PlayMode test infrastructure + baseline existing-feature tests
- Create `Assets/Tests/PlayMode/` asmdef + fixture + 3 existing-feature smoke tests
- Run `run_tests mode=PlayMode` → 3/3 green on unmodified `main`
- Commit: *"Add PlayMode test infrastructure + existing-feature smoke tests"*
- If any of the 3 baseline tests fail, **stop** — that's a pre-existing bug that has to be fixed independently before railgun work starts.

### Phase 1 — `Meteors` physics layer + `Turret`/`TurretBase` refactor (behavior-neutral)
- Add `Meteors` physics layer via `manage_editor add_layer`; assign to `Meteor.prefab`
- Split `Turret.cs` into `TurretBase.cs` (abstract) + `MissileTurret.cs` (concrete)
- `BaseSlot.cs` field type changes from `Turret` to `TurretBase`
- Scene/prefab reference verification (the class rename can invalidate serialized component references — fix if it does)
- Run both suites: EditMode 42/42, PlayMode 3/3
- Commit: *"Refactor Turret into TurretBase + MissileTurret, add Meteors physics layer"*

### Phase 2 — `Meteor.ApplyTunnel` + `MeteorApplyTunnelTests`
- Extend `Meteor.cs` with `ApplyTunnel` method
- Add `MeteorApplyTunnelTests.cs` (10 tests)
- Run EditMode: 52/52
- Commit: *"Add Meteor.ApplyTunnel for line-tunneling damage"*

### Phase 3 — `RailgunStats` ScriptableObject + tests
- Create `RailgunStats.cs` + `RailgunStatId` enum + `RailgunStats.asset` (via `execute_code`, dev-mode $1 costs)
- Add `RailgunStatsTests.cs` (7 tests)
- Run EditMode: 59/59
- Commit: *"Add RailgunStats ScriptableObject + tests"*

### Phase 4 — Procedural art assets
- `railgun_barrel.png`, `railgun_bullet.png`, `railgun_streak.png` via `execute_code`, per § Visuals
- All `filterMode=Point`, `textureCompression=Uncompressed`, `alphaIsTransparency=true`
- Load each back and spot-check dimensions
- Commit: *"Generate railgun procedural art (barrel, bullet, streak)"*

### Phase 5 — `RailgunRound` component + prefab
- `RailgunRound.cs` with the per-frame raycast loop
- `RailgunRound.prefab` with `SpriteRenderer` + `RailgunRound` component, no Rigidbody2D/Collider2D
- No tests yet (PlayMode tests for the raycast chain land in Phase 7)
- Commit: *"Add RailgunRound projectile component and prefab"*

### Phase 6 — `RailgunTurret` component
- `RailgunTurret.cs` — `TurretBase` subclass, reads `RailgunStats`, fires `RailgunRound`, handles barrel charge color animation
- No tests yet (covered by Phase 7 PlayMode tests)
- Commit: *"Add RailgunTurret weapon implementation"*

### Phase 7 — Railgun PlayMode tests (gate)
- `RailgunPlayModeTests.cs` — the 3 railgun PlayMode tests from § Tests
- Run PlayMode: 6/6 green
- **This is the gate.** If any of the 3 railgun tests fail, the core raycast-tunneling chain is broken — fix it here before wiring it into the scene.
- Commit: *"Add PlayMode tests for railgun raycast-tunneling chain"*

### Phase 8 — `BaseSlot.prefab` restructure
- Split `BaseSlot.prefab` into `MissileWeapon` + `RailgunWeapon` sibling children per § 5.6
- Update `BaseSlot.cs` with both weapon-child refs, `builtWeapon` field, activation in `Build`/`SetEmpty`
- Reload scene fresh, do restructure via `execute_code`, scrub diff for drift
- Run both suites
- Manual play-test: buy a missile slot, confirm firing + upgrade panel still work. (No railgun available yet in the UI — Phase 10.)
- Commit: *"Restructure BaseSlot.prefab for multiple weapon types"*

### Phase 9 — `RailgunUpgradePanel` + scene wiring
- `RailgunUpgradePanel.cs` (parallel to renamed `MissileUpgradePanel`)
- Scene: add `RailgunUpgradePanel` GameObject under `UI Canvas` + `UpgradeClickCatcherRailgun` sibling with `ModalClickCatcher`
- `BaseSlot.cs` picks the right panel on click based on `builtWeapon`
- Commit: *"Add RailgunUpgradePanel with click-outside-to-close catcher"*

### Phase 10 — Expose Railgun as a buildable weapon
- `BuildSlotPanel.weapons` array gains `Railgun`
- `SlotManager.railgunBuildCosts = { 1, 1 }` + `NextBuildCost(WeaponType)` overload
- Now the buy panel shows "Missile $1" and "Railgun $1" buttons
- Manual play-test: start fresh, buy a railgun into a side slot, watch it charge/fire/tunnel/pierce, upgrade its stats, verify each upgrade has the right effect
- Commit: *"Expose Railgun in BuildSlotPanel and SlotManager"*

### Phase 11 — `CLAUDE.md` update
- Two weapon types documented
- PlayMode test suite documented in the Testing section
- `Meteors` physics layer noted in project layout
- Workflow updated: both test modes must pass before merging to `main`
- Commit: *"Update CLAUDE.md for railgun + PlayMode tests"*

### Phase 12 — Final verification and hand-back
- Run both test suites one final time: 59/59 EditMode, 6/6 PlayMode
- Manual play-test checklist:
  - Buy a missile slot → fires as before, upgrade panel opens, click-outside-closes
  - Buy a railgun slot → charges (visible color steps), fires straight, tunnels, pierces two stacked meteors, upgrade panel opens, click-outside-closes
  - Upgrade each railgun stat — visible effect (Speed faster, Weight deeper, Caliber wider, Fire Rate faster, Rotation Speed snappier)
  - Railgun shot past an in-flight missile — missile unaffected
  - Debug overlay reset still works
- Scene drift scrub one last time
- Identity scrub on the full branch diff
- Hand back to user for sign-off → FF `main` → push → delete branch

### Rollback targets

Each commit is independently green. Clean rollback points:
- **Phase 0** — test infrastructure only. If PlayMode tests are flaky, roll back to `main` and rethink.
- **Phase 2** — `ApplyTunnel` logic + tests. Pure logic, fully covered.
- **Phase 7** — core raycast chain tested end-to-end. Safe state before scene editing.

If something explodes between Phase 8 and Phase 12, rolling back to Phase 7 means the railgun code exists and is tested but isn't wired into the scene — the game still works with only missile turrets.

## Non-goals

- **Balance tuning.** All costs are $1; all numeric values in the stat table are starting proposals. The economy overhaul pass is where balance happens.
- **Third+ weapons.** The data model (two separate cost arrays, two separate stats classes, two separate upgrade panels) is intentionally not over-abstracted. When a third weapon ships, that's the time to build a generic `WeaponDefinition` abstraction.
- **Sound.** No SFX for firing, impact, or purchase. That's a separate feature cycle.
- **Visual polish beyond the voxel aesthetic.** No screen shake, no camera punch, no impact flash animations beyond the existing voxel-chunk particles. Can layer on later.
- **Ground-level splash damage.** Railgun rounds that reach the bottom of the screen simply despawn — no ground crater or area damage. Meteors that reach the fade zone handle themselves via the existing `Meteor.fadeStartY` logic.
- **Capacitor / magazine mechanics.** No burst fire, no overheat, no charge-drain. Single-shot slow-fire is the whole design.

## Open tuning dials (can be tweaked after implementation)

- Base Fire Rate (0.2 shots/sec may feel too slow or too fast in practice)
- Speed perLevelAdd (starting at +3/level; if max level 10 = 36 world/sec feels sluggish, bump this)
- Weight base (4 may be too stingy; a 4-cell crater on a 60-cell meteor is small)
- Barrel dimensions (`8×60` px per bar, `8` px gap, total `24×60` px sprite — may need to be bigger or smaller to read well on screen)
- Charge animation step count (4 steps may feel too chunky; 6 or 8 is an option)
- Streak fade duration (2 s; "a few seconds" per user) and alpha step count (4)
- Railgun PlayMode test `WaitForSeconds` values (0.4 s / 0.6 s — may need tuning if tests get flaky)

## References

- Voxel aesthetic rule: `/Users/matt/.claude/projects/-Users-matt-dev-Unity-Meteor-Idle/memory/feedback_voxel_aesthetic.md`
- Branch workflow rule: `/Users/matt/.claude/projects/-Users-matt-dev-Unity-Meteor-Idle/memory/feedback_branch_workflow.md`
- Identity-leak rule: `/Users/matt/.claude/projects/-Users-matt-dev-Unity-Meteor-Idle/memory/feedback_identity_leaks.md`
- `feedback_next_step_reminders.md` — always end responses with an explicit next-step line
- Dev-mode $1 cost baseline: commit `4a2575d`, branch `iter/everything-costs-one` → `main`
- Existing `TurretStats` / `TurretStatsTests` — the pattern this spec's `RailgunStats` and its tests mirror
- Existing `Meteor.ApplyBlast` — the pattern this spec's `Meteor.ApplyTunnel` is shaped against
- Existing `ModalClickCatcher` — reused unchanged for the new railgun upgrade panel
