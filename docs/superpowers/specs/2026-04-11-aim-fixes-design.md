# Aim Fixes — Design

Iteration: **Iter 0 — `iter/aim-fixes`** (first iteration from `docs/superpowers/roadmap.md`).
Date: 2026-04-11.

## Problem

Two visible targeting problems in the shipped build:

1. **Railgun misses moving meteors.** The railgun aims at the meteor's *current* position. Rounds are straight-line (no homing), so by the time a round reaches the meteor's former position, the meteor has drifted sideways. See the 2026-04-11 screenshot: a railgun streak runs straight up while the meteor is clearly offset from the streak line.

2. **Missile turret has a too-small targeting range.** `BaseSlot.prefab` serializes `MissileTurret.range = 14`, while `RailgunTurret.range = 30` was updated when the railgun was added. The code default is 30, but the missile prefab override was never refreshed. The effect is that missile turrets behave as if they have a hard max range — visible meteors outside ~14 world units are ignored.

Missiles hide the lead-aim problem because mid-flight `Homing` steers toward a specific voxel's current position. But Homing is a stat (starts at 0), so fresh slots still fire dumb projectiles at the meteor's old position. Fixing lead-aim at the turret level means Homing becomes a drift-tracking correction rather than a compensation for naive aim.

## Goals

- Railgun rounds reliably hit moving meteors at all fire rates.
- Both turrets target any live meteor in the spawner's active list, regardless of distance.
- No magic-number range cap in the code or prefabs.
- Missile homing stat keeps working unchanged as a drift-correction behavior.
- Zero regressions on the existing EditMode/PlayMode suites.

## Non-goals

- No changes to Homing, MissileSpeed, FireRate, or any other stat.
- No new UI, no new art, no new stats.
- No changes to how missiles pick their voxel target.
- No changes to projectile collision or damage.

## Design

### 1. Remove the range field entirely

`TurretBase.FindTarget` currently filters by `bestSqr = range * range`. Replace with: pick the closest live meteor in `meteorSpawner.ActiveMeteors` with no distance cap. `Meteor.IsAlive` already excludes faded/despawned meteors, meteors above the spawn Y don't exist yet, and nothing else in the playfield is a valid target — so an unbounded closest-first pick is correct.

- Delete the `range` serialized field from `TurretBase`.
- Delete both `range:` overrides in `Assets/Prefabs/BaseSlot.prefab`.
- `FindTarget` returns the closest meteor by squared distance from `barrel.position`, or `null` if none.

This also removes the bug class where someone could add a new slot position far from center and need to re-tune per-slot range values.

### 2. Intercept math helper

New file: `Assets/Scripts/AimSolver.cs`, one public static method.

```csharp
public static class AimSolver
{
    // Predicts the point in space where a projectile fired from shooterPos at
    // projectileSpeed should aim in order to intercept a target currently at
    // targetPos moving at targetVelocity. Returns targetPos (no lead) if no
    // positive-time solution exists — e.g., target velocity >= projectile
    // speed with an opening trajectory. For this game's stats the fallback
    // path is effectively unreachable (round speed 6+ >> meteor speed ~0.8).
    public static Vector2 PredictInterceptPoint(
        Vector2 shooterPos,
        Vector2 targetPos,
        Vector2 targetVelocity,
        float projectileSpeed);
}
```

**Math:** standard linear-intercept quadratic.

Let `d = targetPos − shooterPos`, `v = targetVelocity`, `s = projectileSpeed`.
We want the smallest `t >= 0` such that `|d + v·t| = s·t`.
Squaring both sides: `(v·v − s²)·t² + 2·(d·v)·t + d·d = 0`.

- Compute coefficients `a = v·v − s*s`, `b = 2 * d·v`, `c = d·d`.
- If `|a| < ε`: degenerate (target and projectile at equal speed). Solve linearly as `t = −c / b` if `b < 0`, else return `targetPos`.
- Else: discriminant `disc = b*b − 4*a*c`. If `disc < 0`, return `targetPos`.
- Compute both roots `t1, t2`. Pick smallest positive; if neither is positive, return `targetPos`.
- Return `targetPos + v * t`.

### 3. Expose meteor velocity

`Meteor.velocity` is currently private. Add a public read-only accessor:

```csharp
public Vector2 Velocity => velocity;
```

No other changes to `Meteor`. Velocity is set once in `Spawn` and held constant until despawn, which matches the intercept math's constant-velocity assumption exactly.

### 4. Apply lead-aim in both turrets

**`TurretBase.Update` (rotation step):** compute the lead point once per frame and use it as the desired-angle target instead of `target.transform.position`.

```csharp
Vector2 leadPoint = AimSolver.PredictInterceptPoint(
    (Vector2)barrel.position,
    (Vector2)target.transform.position,
    target.Velocity,
    ProjectileSpeed);
Vector2 toTarget = leadPoint - (Vector2)barrel.position;
```

`ProjectileSpeed` is a new `protected abstract float` on `TurretBase`. Subclasses implement it:
- `MissileTurret.ProjectileSpeed => statsInstance.missileSpeed.CurrentValue`
- `RailgunTurret.ProjectileSpeed => statsInstance.speed.CurrentValue`

**`RailgunTurret.Fire`:** recompute the lead point at fire time (the turret may have just finished rotating; using the same-frame value keeps the fire direction in sync with the final aim). Fire along `(leadPoint − spawnPos).normalized` instead of `barrel.up`.

**`MissileTurret.Fire`:** when `PickRandomPresentVoxel` succeeds, compute the lead point for **the voxel's world position**, not the meteor center. The lead math takes the voxel's world position as `targetPos` and the meteor's velocity as `targetVelocity`. This keeps missile aim and voxel homing coherent. Homing behavior in `Missile.Update` stays exactly as-is.

`RailgunTurret` currently overrides `Update` to run its charge animation and its own rotation loop. It must also use the lead-aim math, so its rotation block gets the same `PredictInterceptPoint` call as `TurretBase.Update`. Refactor opportunity: pull the aim-target computation into a shared helper (`TurretBase.ComputeAimPoint(target)`) so both `TurretBase.Update` and `RailgunTurret.Update` use it. This avoids duplicating the lead math in two places.

### 5. Deliberate non-changes

- `TurretBase.aimAlignmentDeg` stays at 10° — the tolerance is for the rotation catching up, not for aim accuracy.
- `Missile.Update` steering stays unchanged. Homing still tracks the voxel's *current* position.
- `RailgunRound` per-frame raycast stays unchanged.
- No changes to how the railgun charge animation ticks.

## Test plan

### EditMode (new)

`Assets/Tests/EditMode/AimSolverTests.cs` — pure math, no MonoBehaviour setup:

1. **Stationary target** — velocity zero → lead point equals target position.
2. **Perpendicular motion** — target directly above shooter moving right at 1 u/s, projectile speed 10 → lead point is to the right of target by a small positive amount; `t` is positive.
3. **Target moving away parallel to fire direction** — target directly above shooter moving up at 1 u/s, projectile 10 → lead point is farther above target, `t > 0`.
4. **No solution edge case** — target velocity (10, 0), projectile speed 1 → returns target position (fallback path).
5. **Equal speeds, opening trajectory** — degenerate `a ≈ 0`, test returns something sane (either target position or the linear solution, documented).

### EditMode (updated)

- `TurretTargetingTests` already exists in PlayMode. No EditMode changes needed to existing suites — the range-removal affects only the FindTarget loop, which is tested through `TurretTargetingTests.FindsClosestLiveMeteor` etc.

### PlayMode (updated)

- `TurretTargetingTests.FindTarget_IgnoresMeteorsBeyondRange` (lines 98–111) explicitly asserts that a meteor at distance 40 is ignored because it exceeds `TurretBase.range = 30`. With the range cap removed, this test's premise no longer exists and the test must be **deleted**, not rewritten. Its coverage ("closest-first selection") is already provided by the other tests in the same file.
- All other `TurretTargetingTests` (closest-wins, dead-meteor filter, empty-spawner) remain valid and should pass unchanged.

### Manual play verify

- Railgun at base fire rate hits a meteor 80%+ of the time across the screen.
- A brand-new missile turret (Homing = 0) hits a falling meteor without wobble.
- Side-slot turrets lock onto meteors near the opposite edge of the screen.
- No regressions on tunneling, homing, charge animation, or UI.

## Risk

- **Low.** The change is localized to targeting/aim, with a pure-math helper and one serialized field removal. The existing test suite covers turret targeting, missile homing, and railgun charge animation. The only failure mode is a subtle off-by-one in the intercept math, which the EditMode tests catch.
- **Watch:** deleting the `range` serialized field from `TurretBase` may trigger a Unity "field removed" serialization warning for any scene/prefab referencing it. The prefab YAML must be cleaned in the same commit. No scene references expected.

## Sizing

Per the roadmap's ≤3 files or ≤200 lines rule:

- **Phase 1:** `AimSolver.cs` + `AimSolverTests.cs`. 2 files, ~120 lines total (math + 5 tests).
- **Phase 2:** `Meteor.cs` velocity accessor + `TurretBase.cs` FindTarget range removal + unified aim helper + `BaseSlot.prefab` YAML cleanup. 3 files (prefab counts as one), ~50 lines of changes.
- **Phase 3:** `MissileTurret.cs` and `RailgunTurret.cs` wire `ProjectileSpeed` + use shared aim helper + voxel-specific lead for missile fire. 2 files, ~40 lines of changes.
- **Phase 4:** Code-reviewer agent, manual play verify, merge.

Three implementation phases + one review phase. Each well under the file/line cap.
