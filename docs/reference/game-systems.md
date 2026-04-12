# Game Systems Reference

Detailed reference for Meteor Idle's game systems. Read this when working on the relevant system — not needed every session.

## Voxel meteor model (the core mechanic)

Each meteor owns a `bool[10,10]` voxel grid and a 150x150 `Texture2D`. On missile hit, `Meteor.ApplyBlast(worldImpactPoint, worldRadius)` converts the impact to grid coordinates, iterates cells inside the blast circle, marks destroyed cells false, paints their 15x15 pixel blocks transparent, spawns a debris particle per cell, and returns the destroyed count.

**Key math:**
- Grid is 10x10, voxel block is 15 px, texture is 150x150.
- Sprite is 1.5 world units across at scale=1 (150 px @ 100 ppu).
- `halfExtent = 0.75` local units.
- `localToGrid = 10 / 1.5 ~ 6.667`.
- `impactRadius = 0.14 + 0.04 * Damage` (world units). Starting Damage=1 -> 0.18 world.
- `gridRadius = worldRadius * localToGrid` — **scale-invariant**. Earlier iterations divided by `transform.localScale.x` and got inconsistent destruction counts across meteor sizes; that was a bug, not a feature.
- `gx`/`gy` are clamped to `[0.5, GridSize - 0.5]` so rim-edge impacts snap onto the nearest valid column/row.
- **A missile hitting through a hole in a partially-destroyed meteor returns 0 — that's on-spec, not a bug.** Don't add a "always destroy at least one" fallback; that was tried, violated the design, and was reverted.

**Lifecycle:** Meteors are pool-backed. Each `Spawn` allocates a fresh `Texture2D` and `Sprite` — `OnDisable` -> `ReleaseTexture` destroys both. Don't cache textures globally by seed; they're mutable per-instance.

**Public voxel API (for homing):**
- `Meteor.GetVoxelWorldPosition(int gx, int gy)` — world position of a voxel's center.
- `Meteor.IsVoxelPresent(int gx, int gy)` — bounds-checked grid lookup.
- `Meteor.PickRandomPresentVoxel(out int gx, out int gy)` — picks a random live cell.

## Weapons, slots, and upgrades

**3 base slots** along the bottom, equidistant. Center starts pre-built with Missile; sides start empty (build modal). Two weapon types: Missile and Railgun.

**Architecture:**
- `TurretBase` (abstract) -> `MissileTurret` / `RailgunTurret` (concrete). Shared targeting/rotation/reload.
- `BaseSlot.prefab` has two sibling weapon children (MissileWeapon, RailgunWeapon), both start inactive. `BaseSlot.Build(weapon)` activates one.
- `SlotManager` instantiates slots, wires panels, routes empty clicks to `BuildSlotPanel`.

### Missile (`TurretStats`, `Assets/Data/TurretStats.asset`)

6 stats across 2 categories:

**Launcher:** FireRate (base 0.5, +0.15/lvl), RotationSpeed (base 30, +15/lvl).
**Missile:** MissileSpeed (base 4, +0.6/lvl), Damage (base 1, +1/lvl, feeds `impactRadius`), BlastRadius (base 0.10, +0.25/lvl), Homing (base 0, +30/lvl deg/sec).

**Accuracy has been removed.** Homing replaced it — missiles fire straight at a target voxel, Homing compensates for drift.

**Homing:** On fire, turret picks a random present voxel on the target. `Missile.Update` steers via `RotateTowards`. Guarded by `target != null && target.IsAlive && target.IsVoxelPresent(gx, gy)` — fails gracefully to dumb projectile. Collision is trigger-based (`OnTriggerEnter2D`), not homing-filtered.

### Railgun (`RailgunStats`, `Assets/Data/RailgunStats.asset`)

5 stats: FireRate (base 0.2, +0.05/lvl), RotationSpeed (base 20, +12/lvl), Speed (base 6, +3/lvl), Weight (base 4, +2/lvl voxel budget), Caliber (base 1, +1/lvl up to 5 cells wide).

**Tunneling (`Meteor.ApplyTunnel`):** Walks grid in half-cell steps, destroys perpendicular band. Empty voxels are free (don't consume Weight). Returns voxels consumed + exit point.

**Round (`RailgunRound`):** Visual-only GO — no Rigidbody2D, no Collider2D. Per-frame `Physics2D.RaycastAll` on `Meteors` layer. `HashSet<Meteor> alreadyTunneled` prevents double-processing. Spawns `RailgunStreak` on budget exhausted/offscreen.

**Charge animation:** 4 quantized color stops (white -> `#CEE8FE` -> `#A8D6FE` -> `#93DAFE`). No smooth lerp.

### `Meteors` physics layer

Slot 8. Assigned to `Meteor.prefab`. Railgun raycasts against this layer only. When spawning meteors in test code, assign this layer before `Spawn`.

## UI layout

- **Money display:** top-center TMP `$N`.
- **Upgrade panels:** MissileUpgradePanel (520x460, two-column Launcher+Missile), RailgunUpgradePanel (280x460, single column). Hidden via `CanvasGroup alpha=0` (GameObjects stay active for `Start()`).
- **Click routing:** `BaseSlot.OnPointerClick` -> panel toggle by weapon type. Empty slot -> `BuildSlotPanel`.
- **Click-outside-to-close:** sibling `ModalClickCatcher` per panel, `raycastTarget` gated by alpha in `LateUpdate`.
- **Build modal:** per-weapon-type buttons with `SlotManager.NextBuildCost`. X close + Escape + click-outside.
- **Upgrade button:** `UpgradeButton` prefab, text `{name}\nLvl {lvl} — ${cost}`. `Bind()` / `BindRailgun()` / `BindDrone()` / `BindBay()`.
- **Click detection:** requires `Physics2DRaycaster` on Camera + `Collider2D` on slot + `IPointerClickHandler`. New Input System only (`activeInputHandler: 1`).

## Spawning

`MeteorSpawner`: `initialInterval=12s`, `minInterval=4.5s`, `rampDurationSeconds=180s`. Tuned down deliberately — fewer asteroids, each matters.

## WebGL build pipeline

Game ships as WebGL on `gh-pages` -> <https://muwamath.github.io/Meteor-Idle/>. Local builds only, no CI.

### Tools
- `tools/build-webgl.sh` — prod build via `BuildScripts.BuildWebGL`. Refuses if editor is open.
- `tools/build-webgl-dev.sh` — dev build (`DEVELOPMENT_BUILD`). Output: `build/WebGL-dev/`, writes `.dev-build-marker`.
- `tools/serve-webgl-dev.sh` — `python3 -m http.server 8000` on `build/WebGL-dev/`.
- `tools/deploy-webgl.sh` — rsyncs prod build to `../Meteor-Idle-gh-pages` worktree. Does NOT push. Refuses on: missing build, `.dev-build-marker` present, missing patterns file, uncommitted Assets/ProjectSettings changes, identity scrub match.
- `Assets/Editor/BuildScripts.cs` — `BuildWebGL` (prod) + `BuildWebGLDev` (dev). Single scene: `Game.unity`.

### Key settings
- `PlayerSettings.WebGL.compressionFormat = Brotli` + `decompressionFallback = true`. Don't switch to Disabled — that ships 63 MB vs ~14 MB.
- `deploy-webgl.sh` strips blank lines from patterns file before `grep -f` (blank = match-everything bug).
- `grep -I` skips binaries — known gap, text files cover the high-risk leak vectors.

### gh-pages
Bootstrap already done. Worktree at `../Meteor-Idle-gh-pages`. **Never use `gh` CLI on this repo** — it's authed as the wrong identity.

## Testing details

### EditMode coverage
- `Meteor.ApplyBlast` — crater logic, rim erosion, tunnel-through, direct-hit, aliveCount
- `Meteor.ApplyTunnel` — line-walking, empty-free, budget cap, exit-point, caliber widths, diagonal
- Voxel API — IsVoxelPresent, GetVoxelWorldPosition, PickRandomPresentVoxel
- `VoxelMeteorGenerator` — determinism, aliveCount, texture dims
- `GameManager` — TrySpend/AddMoney/SetMoney, OnMoneyChanged
- `TurretStats` / `RailgunStats` — NextCost/CurrentValue, ApplyUpgrade, ResetRuntime
- `SimplePool` — prewarm/Get/Release, active-list
- `SlotManager.NextBuildCost` — per-weapon escalation, overflow
- `MeteorSpawner.CurrentInterval` — ramp lerp, clamp
- Drone suite — DroneBody physics, state machine, DroneStats/BayStats, door animation, CoreDrop lifecycle, paysOnBreak, drop registry, core-drop spawning

### PlayMode coverage
- Missile collision, meteor fade, spawner pooling
- Railgun fires-into-meteor, pierces-two, layer-mask
- Turret targeting (FindTarget via TestTurret subclass)
- Missile homing (RotateTowards, dumb, target-lost)
- Railgun charge animation (4 color stops)
- Floating text (rise, fade, auto-destroy)
- Drone collection end-to-end, drone avoidance

### Not tested (verify by play mode)
- Full turret Update reload->fire cycles
- SlotManager.Start slot spawning
- UI panel layouts
- DebugOverlay
- RailgunStreak fade

### Test helper patterns

**EditMode:**
```csharp
var go = new GameObject("TestMeteor", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
var m = go.GetComponent<Meteor>();
TestHelpers.InvokeAwake(m);  // reflect-invoke; Awake doesn't fire reliably in EditMode
m.Spawn(null, Vector3.zero, seed: 42, sizeScale: 1f);
// ... assertions ...
Object.DestroyImmediate(go);
```

**PlayMode:**
```csharp
[UnityTest]
public IEnumerator MyTest()
{
    yield return SetupScene();
    var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
    yield return new WaitForSeconds(0.5f);
    Assert.Less(meteor.AliveVoxelCount, 65);
    TeardownScene();
}
```

Inherit from `PlayModeTestFixture`. Helpers: `SpawnTestMeteor` (auto-assigns Meteors layer), `SpawnTestMissile`, `SpawnTestRailgunRound`, `SpawnTestSpawner`.
