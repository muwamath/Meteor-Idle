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

## Open question: what happens to Accuracy?

The current game has an **Accuracy** stat — it controls a one-time launch wobble (random aim error applied at missile fire time). The wobble angle is `(1 - accuracy) * 30°`.

Your new list doesn't mention Accuracy, so I'm reading this as "Accuracy gets dropped" — the launch wobble is removed entirely (missiles always fly straight out of the barrel), and homing replaces it as the "make missiles better at hitting" upgrade axis.

Three ways to interpret your intent, ordered by how I'd recommend them:

- **(A) Drop Accuracy, add Homing.** Missiles launch perfectly straight. Homing controls mid-flight steering. Simpler and cleaner — I think you forgot Accuracy was there and probably didn't intend to keep it. **Recommended.**
- **(B) Keep Accuracy as a Missile stat (hidden 7th upgrade).** Accuracy stays in code and stats but isn't in the upgrade panel. Feels bad — you can't buy it.
- **(C) Keep Accuracy and show all 7 upgrades** (Missile column gets 5 buttons instead of 4). Undoes the symmetry of your 2+4 layout.

**If you want anything other than (A), tell me and I'll amend this plan before implementation.**

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

Missiles can steer toward a target mid-flight at up to `homingDegPerSec` degrees per second of rotation.

```csharp
public Stat homing = new Stat {
    id = StatId.Homing,
    displayName = "Homing",
    baseValue = 0f,
    perLevelAdd = 30f,
    baseCost = 35
};
```

Starting at 0 means no homing at all (missiles fly straight, same as today). Level 1 → 30°/sec, level 5 → 150°/sec, etc.

**Implementation:**
- `Missile.Launch(...)` gains a new parameter `Meteor target` (passed from `Turret.Fire`), plus `float homingDegPerSec`.
- `Missile.Update` — if `target != null && target.IsAlive && homingDegPerSec > 0.01f`:
  1. Compute desired direction = `(target.transform.position - transform.position).normalized`
  2. Current direction = `rb.linearVelocity.normalized`
  3. Use `Vector2.MoveTowards` or manual angle math to rotate current direction toward desired by `homingDegPerSec * Time.deltaTime` degrees
  4. Reapply as `rb.linearVelocity = newDir * currentSpeed` (preserves magnitude)
  5. Also rotate the missile's visual transform to match the new velocity.
- Target reference becomes stale on impact or meteor death — guarded by `target.IsAlive` check. Once invalid, the missile flies straight from that point onward.
- `Turret.Fire` — passes `target` (the selected Meteor from `FindTarget()`) to `missile.Launch`.

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

### Missile re-targeting behavior (edge case)

When a homing missile's target meteor is destroyed (by itself or another missile), the target reference becomes invalid. Options:

- **(a) Fly straight** — once target dies, stop steering. Missile continues on its current heading. Simplest.
- **(b) Re-acquire** — find the nearest alive meteor and steer toward it. More "smart," but might feel weird when missiles suddenly pivot.

**Recommendation: (a)** for this iteration. Re-acquire can be added later if it feels missing.

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
