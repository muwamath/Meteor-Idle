# Upgrades Expansion — Implementation Plan

**Goal:** Expand the turret upgrade list from 5 flat stats into 6 stats organized under two categories (Launcher, Missile), redesign the upgrade panel as a two-column layout with clear category headers, and bump the UI font size.

**Current state:** `UpgradePanel` iterates `TurretStats.All()` and instantiates one `UpgradeButton` per stat into a single `VerticalLayoutGroup`. Stats are flat — no categorization. 5 stats total: FireRate, MissileSpeed, Damage, Accuracy, BlastRadius.

**Target state:** Same panel, two columns side by side, each with a header and a vertical stack of buttons. 6 stats organized as:

| Category | Stat | Current? | Starting value | Per-level |
|---|---|---|---|---|
| **Launcher** | Fire Rate | existing | 0.5 shots/sec | +0.15 |
| **Launcher** | Rotation Speed | **NEW** — currently hardcoded 45°/sec on Turret | 30°/sec | +15°/sec |
| **Missile** | Missile Speed | existing | 4 u/s | +0.6 |
| **Missile** | Damage | existing | 1 | +1 |
| **Missile** | Blast Radius | existing | 0.10 | +0.25 |
| **Missile** | Homing | **NEW** | 0°/sec | +30°/sec |

## Accuracy — dropped, replaced by Homing

Accuracy is removed entirely. The launch wobble (`(1 - accuracy) * 30°`) is gone. Every missile launches in a precise direction toward a specific target voxel.

Homing picks up where Accuracy left off: instead of making launch *direction* noisy (or not), Homing makes the missile *correct mid-flight* to hit a specific voxel on a specific meteor.

## Design details

### New stat: Rotation Speed

Currently `Turret.cs` has `[SerializeField] private float rotationSpeedDegPerSec = 45f;`. This becomes a stat on `TurretStats`:

```csharp
public Stat rotationSpeed = new Stat {
    id = StatId.RotationSpeed,
    displayName = "Rotation Speed",
    baseValue = 30f,
    perLevelAdd = 15f,
    baseCost = 12
};
```

`Turret.Update` reads `stats.rotationSpeed.CurrentValue` instead of the serialized field. The serialized field is removed. Starting 30°/sec is a bit slower than the current hardcoded 45°/sec — makes the first few upgrades feel meaningful.

### New stat: Homing

Missiles lock onto a **specific voxel** on a specific meteor and steer toward it mid-flight at up to `homingDegPerSec` degrees per second of velocity rotation.

```csharp
public Stat homing = new Stat {
    id = StatId.Homing,
    displayName = "Homing",
    baseValue = 0f,
    perLevelAdd = 30f,
    baseCost = 35
};
```

Starting at 0 means no mid-flight correction. The missile is still *fired at* the selected voxel's position at launch time — but without homing, the missile flies a dead-straight line and the meteor may drift sideways during the missile's travel time, causing the missile to hit a neighboring voxel or miss entirely. Homing compensates for that drift.

Level 1 → 30°/sec, level 5 → 150°/sec, etc.

**Implementation:**

- `Turret.Fire` — after `FindTarget()` returns a meteor, call `target.PickRandomPresentVoxel(out int gx, out int gy)` to pick which square on the meteor the missile aims at. Pass all three (`target`, `gx`, `gy`) plus the homing stat into `Missile.Launch`.

- `Missile.Launch(... Meteor target, int targetGx, int targetGy, float homingDegPerSec)` stores the target reference + voxel coords. Computes the initial velocity as the direction from the muzzle to the voxel's *current world position* (via `target.GetVoxelWorldPosition(gx, gy)`), scaled by missile speed.

- `Missile.Update` each frame (while still alive):
  1. If `target == null`, fly straight. No homing work.
  2. If `!target.IsAlive` or `!target.IsVoxelPresent(targetGx, targetGy)`, the lock is stale. Fly straight.
  3. Otherwise, compute desired direction = `(target.GetVoxelWorldPosition(gx, gy) - transform.position).normalized`.
  4. Rotate `rb.linearVelocity` toward that direction by up to `homingDegPerSec * Time.deltaTime` degrees (preserving magnitude).
  5. Update missile's visual rotation to match new velocity.

- Collision behavior **unchanged**. `OnTriggerEnter2D` → `ApplyBlast(transform.position, impactRadius + blastRadius)` fires on *any* meteor the missile touches, not just the target. So if another meteor gets in the missile's way, the missile explodes on that other meteor at the impact point — the homing target is just a steering hint, not a collision filter.

- **Meteor.cs new public API:**
  - `public bool IsVoxelPresent(int gx, int gy)` — bounds-checked lookup into the grid.
  - `public Vector3 GetVoxelWorldPosition(int gx, int gy)` — promotes the existing private `VoxelCenterToWorld` to public.
  - `public bool PickRandomPresentVoxel(out int gx, out int gy)` — iterate the grid, collect present cells, pick a random index. Returns false if no cells remain (meteor is effectively dead, shouldn't happen during Fire since FindTarget filters on `IsAlive`).

### Drop: Accuracy (assuming (A))

Changes:
- Remove `StatId.Accuracy`, `TurretStats.accuracy`, and all references in `All()`, `Get()`.
- Remove accuracy logic from `Turret.Fire`: no more aim wobble, missiles launch in the exact direction `barrel.up`.
- Remove `maxWobbleDeg` serialized field from `Turret.cs`.
- Update the existing `TurretStats.asset` via `SerializedObject` to remove the `accuracy` field.

### StatId enum changes

```csharp
public enum StatId
{
    FireRate = 0,
    MissileSpeed = 1,
    Damage = 2,
    BlastRadius = 3,
    RotationSpeed = 4,
    Homing = 5,
}
```

`Accuracy = 3` is removed. `BlastRadius` stays at 3 (old slot) for ScriptableObject migration simplicity — but actually, Unity ScriptableObjects serialize by field name not by enum value, so the enum int values don't matter for migration. I'll reorder them cleanly.

### UI: two-column layout with bigger font

New scene hierarchy for `UpgradePanel`:

```
UpgradePanel (Image, CanvasGroup — existing GameObject, resized)
├── Title (TMP "UPGRADES", bold, 28pt, centered)
└── Columns (HorizontalLayoutGroup, spacing 12)
    ├── LauncherColumn (VerticalLayoutGroup, spacing 8)
    │   ├── LauncherHeader (TMP "LAUNCHER", bold, 20pt, centered, dim accent color)
    │   ├── (FireRate button, instantiated at runtime)
    │   └── (RotationSpeed button, instantiated at runtime)
    └── MissileColumn (VerticalLayoutGroup, spacing 8)
        ├── MissileHeader (TMP "MISSILE", bold, 20pt, centered, dim accent color)
        ├── (MissileSpeed button, instantiated at runtime)
        ├── (Damage button, instantiated at runtime)
        ├── (BlastRadius button, instantiated at runtime)
        └── (Homing button, instantiated at runtime)
```

**Panel size:** `360×420` → `520×460` to fit two columns and bigger fonts comfortably.

**Font bumps:**
- Upgrade button label: `16pt` → `20pt`
- Upgrade button height: `54px` → `68px`
- Panel title: new at `28pt` bold (currently there's no title)
- Column headers: new at `20pt` bold, color `#7aa6ff` (dim blue accent)

**Code changes in `UpgradePanel.cs`:**
- Add two new serialized fields: `Transform launcherColumnParent`, `Transform missileColumnParent`.
- Keep `buttonParent` for backward compat but use the column-specific ones.
- Instead of iterating `stats.All()` into a single parent, branch on stat category:
  ```csharp
  foreach (var stat in stats.All())
  {
      Transform parent = IsLauncherStat(stat.id) ? launcherColumnParent : missileColumnParent;
      var btn = Instantiate(buttonPrefab, parent);
      btn.Bind(stats, stat.id, OnClicked);
      buttons.Add(btn);
  }
  ```
- Add a helper `IsLauncherStat(StatId id) => id == StatId.FireRate || id == StatId.RotationSpeed;`

### UpgradeButton prefab tweaks

Current `UpgradeButton.prefab` — TMP_Text label at 16pt, button container 260×54. Bumping:
- Container: 230×68 (narrower, because two columns need to fit side by side in ~520 width)
- Label font size: 20pt
- Label content unchanged: `"{name}\nLvl {lvl} — ${cost}"`

### Missile re-targeting edge cases

Two cases where the lock becomes stale mid-flight:

1. **Target meteor dies entirely** (other missile chewed the last voxel) — `target.IsAlive == false`. Missile stops homing and flies straight from its current velocity. No re-acquisition.
2. **Target voxel destroyed but meteor still alive** — `target.IsVoxelPresent(gx, gy) == false`. Same behavior: stop homing, fly straight. The missile will still hit *something* because it was already on course for that region of the meteor.

No nearest-meteor re-acquisition in this iteration. Homing is "aim at a specific square," and if the square is gone, the missile reverts to a dumb projectile. Can be made smarter later if it feels missing.

## Files touched

- **Modify:** `Assets/Scripts/Data/TurretStats.cs` — add `RotationSpeed` + `Homing` stats, remove `Accuracy`.
- **Modify:** `Assets/Data/TurretStats.asset` — update serialized stat list via `execute_code` (remove accuracy, add rotationSpeed, add homing).
- **Modify:** `Assets/Scripts/Turret.cs` — read rotation speed from stats, pass target + homing to `missile.Launch`, remove wobble code.
- **Modify:** `Assets/Scripts/Missile.cs` — accept target + homing in `Launch`, implement mid-flight steering in `Update`.
- **Modify:** `Assets/Scripts/UI/UpgradePanel.cs` — branch on stat category, add category-parent refs.
- **Modify:** `Assets/Prefabs/UpgradeButton.prefab` — font size 20, container 230×68.
- **Modify:** `Assets/Scenes/Game.unity` — rebuild UpgradePanel child hierarchy with Columns/LauncherColumn/MissileColumn + headers, wire new serialized refs, resize panel.

## Verification checklist

- [ ] Compile clean after the script + asset changes.
- [ ] Clicking the turret base opens a centered panel with two clearly-labeled columns (LAUNCHER on left, MISSILE on right).
- [ ] Launcher column has exactly 2 buttons: Fire Rate, Rotation Speed.
- [ ] Missile column has exactly 4 buttons: Missile Speed, Damage, Blast Radius, Homing.
- [ ] Upgrade button text is visibly larger than before (20pt vs 16pt).
- [ ] Buying Rotation Speed makes the barrel visibly rotate faster toward targets.
- [ ] Buying Homing: first level gives subtle mid-flight curving toward targets; high levels make missiles track aggressively.
- [ ] Buying Fire Rate, Missile Speed, Damage, Blast Radius — all behave as before.
- [ ] No accuracy-related wobble on fresh launch (missiles fly straight from barrel).
- [ ] Debug overlay (backquote) still works.
- [ ] No console errors during a 2-minute play session.

## Branch strategy

This plan lives on branch `iter/expand-upgrades`. Implementation commits will land on the same branch. Final merge to `main` is fast-forward, after user verifies in play mode.

## Steps (for execution phase, after plan approval)

1. Edit `TurretStats.cs` — add `RotationSpeed` and `Homing`, remove `Accuracy`.
2. `execute_code` edit of `TurretStats.asset` to match the new schema.
3. Refresh Unity, verify compile.
4. Edit `Missile.cs` — add target + homing params to `Launch`, add steering logic in `Update`.
5. Edit `Turret.cs` — read rotation speed from stats, drop wobble code, pass target to missile.
6. Edit `UpgradePanel.cs` — dual-column branching logic.
7. `execute_code` — rebuild UpgradeButton prefab with new size/font.
8. `execute_code` — rebuild `UpgradePanel` scene hierarchy with two columns and headers, wire serialized refs, resize panel.
9. Refresh, console clean check.
10. Play-mode verify all checklist items, screenshot.
11. Push branch updates, hand off for user verification.
