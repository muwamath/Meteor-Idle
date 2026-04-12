# CLAUDE.md — Meteor Idle

Working guidance for Claude Code sessions in this repo.

## What this is

A 2D Unity 6 idle game. Voxel meteors fall; the bottom row has 4 base slots (weapons), 2 drone bays, and 1 collector (rock grinder). Weapons auto-fire to destroy meteor voxels. Core voxels don't pay directly — they spawn CoreDrop entities that collector drones physically retrieve and deliver to the Collector for cash. Regular voxels still pay $1 each on destruction. Two weapon types (Missile and Railgun), collector drones with custom physics, animated grinder. Desktop/laptop target, landscape 16:9. Early development — no persistence, no audio.

## Tech stack

- Unity **6000.4.1f1** (pinned, exact version)
- 2D URP, new Input System, TextMeshPro
- C# game code lives in the `MeteorIdle` assembly (`Assets/Scripts/MeteorIdle.asmdef`).
- EditMode tests in `MeteorIdle.Tests.Editor` (`Assets/Tests/EditMode/`).
- PlayMode tests in `MeteorIdle.Tests.PlayMode` (`Assets/Tests/PlayMode/`).
- Unity MCP (`mcpforunity://` resources, `mcp__UnityMCP__*` tools) drives everything in the editor

## Project layout

```
Assets/
  Art/                          procedurally-generated PNGs + materials
  Data/
    TurretStats.asset           Missile turret upgrade stats (6 stats, 2 categories)
    RailgunStats.asset          Railgun turret upgrade stats (5 stats, single column)
    DroneStats.asset            Drone stats (Thrust, BatteryCapacity, CargoCapacity)
    BayStats.asset              Bay stats (ReloadSpeed, DronesPerBay)
  Prefabs/
    BaseSlot.prefab             slot root with two weapon-child siblings (see UI section)
    Meteor.prefab               voxel meteor on the Meteors physics layer
    Missile.prefab              homing missile with Rigidbody2D + trigger collider
    RailgunRound.prefab         visual-only railgun bullet (no collider, per-frame raycast)
    RailgunStreak.prefab        stretched-sprite trailing line VFX
    CollectorDrone.prefab       plus-shaped drone with 4 thruster trail children
    DroneBay.prefab             bay body with 2 animated door children
    Collector.prefab            rock grinder with 2 animated tooth children
    CoreDrop.prefab             floating red entity spawned by core voxel kills
    Build/Upgrade button prefabs, particle prefabs, FloatingText
  Scenes/Game.unity             the one scene
  Scripts/
    MeteorIdle.asmdef           game code assembly definition
    Meteor.cs                   voxel grid + ApplyBlast (circular) + ApplyTunnel (line)
    VoxelMeteorGenerator.cs     static generator: seed → bool[10,10] + Texture2D
    Missile.cs                  trigger-based collision, target-voxel homing
    TurretBase.cs               abstract base: targeting, rotation, reload timer
    MissileTurret.cs            TurretBase subclass; missile pool + fire
    RailgunTurret.cs            TurretBase subclass; charge color animation + fire
    BaseSlot.cs                 slot root with two weapon-child refs + per-weapon panel routing
    SlotManager.cs              spawns 4 slots, per-weapon NextBuildCost(WeaponType)
    MeteorSpawner.cs            timer + pool, calm starting cadence
    GameManager.cs              money singleton + OnMoneyChanged event, SetMoney for debug, CoreDrop registry
    SimplePool.cs               generic MonoBehaviour pool
    FloatingText.cs             world-space "+$N" tween
    Drones/
      Collector.cs              single rock-grinder deposit point, animated teeth (4-step quantized)
      CollectorDrone.cs         state machine (8 states), DroneBody integration, avoidance, contact push
      CoreDrop.cs               floating entity spawned by core kills, drifts down, claimed by drones
      DroneBay.cs               launch/catch/recharge, 4-keyframe door animation, ICollectorDroneEnvironment
      DroneBody.cs              custom 2D physics integrator (exponential damping, avoidance)
      DroneState.cs             enum: Idle/Launching/Seeking/Pickup/Delivering/Depositing/Returning/Docking
      DroneStats.cs             ScriptableObject: Thrust, BatteryCapacity, CargoCapacity
      BayStats.cs               ScriptableObject: ReloadSpeed, DronesPerBay
      BayManager.cs             spawns 2 bays + wires Collector reference
      ICollectorDroneEnvironment.cs  interface for mock-injectable drone testing
      ThrusterTrail.cs          voxel particle emitter on drone arm tips
    Weapons/
      WeaponType.cs             enum { Missile, Railgun }
      RailgunRound.cs           per-frame Physics2D.RaycastAll on Meteors layer
      RailgunStreak.cs          stretched-sprite line VFX, 4-step alpha fade
    Data/
      TurretStats.cs            6 missile stats across Launcher + Missile categories
      RailgunStats.cs           5 railgun stats: FireRate, RotationSpeed, Speed, Weight, Caliber
    Debug/DebugOverlay.cs       editor-only overlay, backquote toggle, pauses via timeScale
    UI/
      MoneyDisplay.cs           top-center TMP label, listens to money
      MissileUpgradePanel.cs    missile stats, two-column Launcher/Missile layout
      RailgunUpgradePanel.cs    railgun stats, single column
      UpgradeButton.cs          shared prefab; Bind/BindRailgun/BindDrone/BindBay
      DroneUpgradePanel.cs      BAY + DRONE two-column upgrade panel
      BuildSlotPanel.cs         modal for buying a new base slot (per-weapon costs)
      BuildWeaponButton.cs      one per weapon type in the build modal
      ModalClickCatcher.cs      click-outside-to-close behind any modal CanvasGroup
      PanelManager.cs           static exclusive-panel manager (only one overlay at a time)
Tests/
  EditMode/
    MeteorIdle.Tests.Editor.asmdef
    MeteorApplyBlastTests.cs    crater/walk-inward logic on the voxel grid
    MeteorApplyTunnelTests.cs   line-walking voxel destruction for railgun
    MeteorVoxelApiTests.cs      IsVoxelPresent/GetVoxelWorldPosition/PickRandomPresentVoxel
    VoxelMeteorGeneratorTests.cs  determinism + aliveCount consistency
    GameManagerTests.cs         TrySpend/AddMoney/SetMoney + OnMoneyChanged
    TurretStatsTests.cs         NextCost/CurrentValue formulas, ApplyUpgrade
    RailgunStatsTests.cs        same shape as TurretStatsTests but for RailgunStats
    SimplePoolTests.cs          prewarm/Get/Release/reuse cycle
    SlotManagerBuildCostTests.cs  NextBuildCost in-table + overflow, both weapons
    MeteorSpawnerIntervalTests.cs ramp lerp from initialInterval to minInterval
    DroneBodyTests.cs           integrator: damping, thrust, push kick, limp-home, avoidance
    DroneStateMachineTests.cs   full state machine transitions including Delivering/Depositing
    DroneStatsTests.cs          NextCost/CurrentValue/ApplyUpgrade for drone stats
    BayStatsTests.cs            same for bay stats, maxLevel cap on DronesPerBay
    DroneBayDoorsTests.cs       4-keyframe quantized door animation
    CoreDropTests.cs            drift, claim, consume lifecycle
    DestroyResultPayoutTests.cs paysOnBreak flag isolation
    GameManagerDropRegistryTests.cs  RegisterDrop/UnregisterDrop/ActiveDrops
    MeteorCoreDropsSpawnTests.cs  core kills spawn CoreDrop entities
    VoxelMaterialTests.cs       paysOnBreak flag on VoxelMaterial
    TestHelpers.cs              reflection-invoked Awake/Update for EditMode tests
  PlayMode/
    MeteorIdle.Tests.PlayMode.asmdef
    PlayModeTestFixture.cs      shared spawn helpers for meteor/missile/railgun/drone
    ExistingFeatureSmokeTests.cs  missile collision, meteor fade, spawner pooling
    RailgunPlayModeTests.cs     fires-into-meteor, pierces-two, layer-mask filter
    TurretTargetingTests.cs     TurretBase.FindTarget via TestTurret subclass
    MissileHomingTests.cs       RotateTowards steering, dumb case, target-lost
    RailgunChargeAnimationTests.cs  4-stop quantized barrel color via chargeTimer
    FloatingTextTests.cs        rise, alpha fade, auto-destruction
    DroneCollectionEndToEndTests.cs  core kill → drop → drone → collector → money
    DroneAvoidanceTests.cs      drone skirts meteor outside safety radius
tools/
  identity-scrub.py             pre-commit identity-leak check (see "Identity scrub" section)
  build-webgl.sh                headless Unity CLI wrapper: BuildScripts.BuildWebGL -> build/WebGL/ (prod)
  build-webgl-dev.sh            same, DEVELOPMENT_BUILD flavor -> build/WebGL-dev/ (writes .dev-build-marker sentinel)
  serve-webgl-dev.sh            python http.server on :8000 serving build/WebGL-dev/
  deploy-webgl.sh               rsyncs build/WebGL/ to the gh-pages worktree, scrubs, commits (refuses if sentinel found)
Assets/Editor/BuildScripts.cs   Editor-side BuildWebGL + BuildWebGLDev entry points (invoked via -executeMethod)
build/                          gitignored Unity build output; WebGL lands under build/WebGL/ (prod) or build/WebGL-dev/ (dev)
docs/superpowers/
  specs/                        design docs (spec per iteration)
  plans/                        implementation plans (task-by-task)
```

## Conventions you must follow

**Prefer EditMode unit tests for pure logic; final manual verification runs against a local WebGL dev build, not Unity Editor play mode.** Tests live in `Assets/Tests/EditMode/`. They are fast (a few ms each), run without entering play mode, and have caught real bugs in `Meteor.ApplyBlast`. When you add or change logic in one of the tested modules (`Meteor`, `VoxelMeteorGenerator`, `GameManager`, `TurretStats`, `SimplePool`), update or add a test in the same change. Run the suite via `mcp__UnityMCP__run_tests` (or Window → General → Test Runner in the editor) and expect zero failures before committing. Do not push to `origin/main` without also having verified the change end-to-end via a local WebGL dev build — the editor doesn't run the same code path players will see (input handling, loader, compression, framerate all differ).

**Editor play mode is still useful for fast iterative debugging** during active development. It's just not the final gate. The final gate — the last check before sign-off, merge, or push — is the local WebGL dev build.

The fast iterative loop (in-editor, during development):
1. Edit C# / mutate via `execute_code`
2. `refresh_unity` (`scope=scripts` or `all`, `compile=request`)
3. `read_console` — expect zero errors
4. Run `mcp__UnityMCP__run_tests` if the change touches tested logic
5. `manage_editor play`
6. Wait briefly, then `manage_camera screenshot include_image=true`
7. `read_console` again
8. `manage_editor stop`

The final verification loop (local WebGL dev build, run once per branch before handing back to the user). **The Unity Editor stays open for the whole flow** — we drive everything through MCP against the live editor session.

1. **Kick off a dev build inside the running editor.** Use `mcp__UnityMCP__execute_code` with a body that calls `BuildScripts.BuildWebGLDev();`. The build runs synchronously in the editor's AppDomain (the editor UI freezes during the build, which is expected) and takes a couple of minutes. Unity auto-defines `DEVELOPMENT_BUILD`, which unlocks `DebugOverlay` and any other `#if UNITY_EDITOR || DEVELOPMENT_BUILD`-gated surfaces. Output lands in `build/WebGL-dev/` and the method writes the `.dev-build-marker` sentinel on success. (Alternative for the human user: click `Meteor Idle → Build → WebGL (Dev)` from the editor menu bar — same method, no MCP.)
2. Run `tools/serve-webgl-dev.sh` in the background via `run_in_background: true`. It runs a plain `python3 -m http.server 8000 --directory build/WebGL-dev/` after checking the port is free.
3. Navigate to `http://localhost:8000/` via `chrome-devtools-mcp` (`new_page` → `navigate_page` → `wait_for` the Unity loader → `take_screenshot`).
4. Exercise the change: interact with the game, press `` ` `` to confirm the debug overlay is present, watch the feature in action.
5. `list_console_messages` — expect zero JS errors or runtime exceptions from Unity's loader.
6. Close the tab (`close_page`) per the chrome-devtools-mcp hygiene rule.
7. Kill the serve process.

Tests catch logic regressions; the WebGL dev verify catches everything else (scene drift, UI layout, loader, input handling, frame timing).

**The shell scripts `tools/build-webgl-dev.sh` and `tools/build-webgl.sh` still exist**, but they are only for rare headless / CI-style scenarios where the editor is not running. They will refuse to start while the editor has the project open (Unity holds an exclusive lock). The primary path is always MCP `execute_code` or the menu item — the user's editor stays open.

**Procedural art only.** Every `.png` in `Assets/Art/` is generated by an `execute_code` editor script that calls `Texture2D.EncodeToPNG()` and writes it to disk. Do not author bitmaps in external tools. The voxel aesthetic comes from per-pixel `SetPixel` calls with 1-pixel dark edges and ~20% brighten highlights.

**Commit after each logical change, work on a branch, never push main directly.** The branch workflow is a hard rule: every change lands on `iter/<short-description>` first, gets user verification in the editor, and only then fast-forwards to `main`. See `~/.claude/projects/-Users-matt-dev-Unity-Meteor-Idle/memory/feedback_branch_workflow.md` for the full rule. The only exception is "fast-forwarding main to an already-verified branch tip."

**Never touch global git config.** The repo-local git identity is intentionally set per-repo (see `git config user.name` / `user.email` in this repo). The machine's global identity is a different person and must not be associated with any commit, push, or credential handoff for this repo. Repo-local config sets `credential.helper=""` and `commit.gpgsign=false` defensively. If you find yourself reaching for `--global`, stop. See `memory/feedback_identity_leaks.md` for the full rule, including a pre-commit grep checklist.

**Remote uses an SSH host alias.** `git@github-muwamath:muwamath/Meteor-Idle.git`. The host alias lives in `~/.ssh/config` and points at a dedicated key (`~/.ssh/id_ed25519_muwamath`). Don't replace the remote URL with `git@github.com:...` — that would route credentials through macOS keychain and leak the main GitHub account's token.

**No `Co-Authored-By:` trailers on this repo**, ever. The anonymous GitHub identity must not be linked to Claude Code in commit metadata.

## Working with the Unity MCP

### Editor-script gotchas

- `execute_code` runs as a method body in CodeDom (C# 6). **No `using` statements inside the body** — use fully-qualified types (`UnityEditor.SceneManagement.EditorSceneManager`, etc.).
- `Object` is ambiguous between `System.Object` and `UnityEngine.Object` inside execute_code. Always write `UnityEngine.Object.DestroyImmediate(...)` not `Object.DestroyImmediate(...)`.
- Don't call `MarkSceneDirty` / `SaveScene` while play mode is active — Unity throws `InvalidOperationException: This cannot be used during play mode`. Stop play first.
- `ScriptableSingleton<T>` for editor singletons requires reflection: use `typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance")`.
- Unity's `selectedSizeIndex` property on `GameView` is declared on the `PlayModeView` base class — `gvType.GetProperty("selectedSizeIndex", flags)` returns null unless you walk the type chain. Simpler: use the `m_SelectedSizes` field directly (it's an `int[]` indexed by `GameViewSizeGroupType`).

### Screen Space Overlay canvases don't appear in camera screenshots

`manage_camera screenshot` captures from the Camera's perspective, which excludes overlay UI. If you take a play-mode screenshot and the money display, upgrade panel, or debug overlay looks missing, it's there in the live Game view — it's just not in camera-rendered PNGs. Don't "fix" a missing UI in a screenshot; verify via a direct CanvasGroup state check with `execute_code` instead.

### runInBackground must stay on

`PlayerSettings.runInBackground = true` so the game keeps running when Unity loses focus. If you see play mode pausing the moment you switch to chat, it's been regressed.

## Voxel meteor model (the core mechanic)

Each meteor owns a `bool[10,10]` voxel grid and a 150×150 `Texture2D`. On missile hit, `Meteor.ApplyBlast(worldImpactPoint, worldRadius)` converts the impact to grid coordinates, iterates cells inside the blast circle, marks destroyed cells false, paints their 15×15 pixel blocks transparent, spawns a debris particle per cell, and returns the destroyed count. The missile pays `GameManager.AddMoney(destroyed)` and spawns a `+$N` floating text.

**Key math:**
- Grid is 10×10, voxel block is 15 px, texture is 150×150.
- Sprite is 1.5 world units across at scale=1 (150 px @ 100 ppu).
- `halfExtent = 0.75` local units.
- `localToGrid = 10 / 1.5 ≈ 6.667`.
- `impactRadius = 0.14 + 0.04 * Damage` (world units). Starting Damage=1 → 0.18 world.
- `gridRadius = worldRadius * localToGrid` — **scale-invariant**. Earlier iterations divided by `transform.localScale.x` and got inconsistent destruction counts across meteor sizes; that was a bug, not a feature.
- `gx`/`gy` are clamped to `[0.5, GridSize - 0.5]` so rim-edge impacts snap onto the nearest valid column/row. Pass-through-hole misses still return 0 because the cell check still requires `voxels[x,y] == true`.
- **A missile hitting through a hole in a partially-destroyed meteor returns 0 — that's on-spec, not a bug.** Don't add a "always destroy at least one" fallback; that was tried, violated the design, and was reverted.

**Lifecycle:** Meteors are pool-backed. Each `Spawn` allocates a fresh `Texture2D` and `Sprite` — `OnDisable` → `ReleaseTexture` destroys both. Don't cache textures globally by seed; they're mutable per-instance.

**Public voxel API (for homing):**
- `Meteor.GetVoxelWorldPosition(int gx, int gy)` — world position of a voxel's center.
- `Meteor.IsVoxelPresent(int gx, int gy)` — bounds-checked grid lookup.
- `Meteor.PickRandomPresentVoxel(out int gx, out int gy)` — picks a random live cell. Computes a fresh local live-count inside rather than trusting the cached `aliveCount` — defensive against any future drift.

## Weapons, slots, and upgrades

The game has **3 base slots** along the bottom of the screen, equidistant. The center slot starts pre-built with a Missile turret; the two side slots start empty (showing a `+` icon) and can be purchased through the **build modal**. There are two weapon types — Missile and Railgun — and the player picks which weapon to install when buying a slot. Each weapon has its own stats asset and its own upgrade panel.

**Architecture overview:**
- `TurretBase` is an abstract MonoBehaviour that owns the shared targeting/rotation/reload-timer logic. `MissileTurret` and `RailgunTurret` are concrete subclasses that implement `Fire(target)` and the `FireRate`/`RotationSpeed` abstract properties.
- `BaseSlot.prefab` has two sibling weapon children — `MissileWeapon` and `RailgunWeapon` — each containing one of the concrete turret components plus its own barrel and muzzle. Both children start inactive; `BaseSlot.Build(weapon)` activates the matching one.
- `SlotManager` instantiates the slot prefab N times, hands each spawned slot a reference to the right upgrade panel for both weapons (`SetMissileUpgradePanel` + `SetRailgunUpgradePanel`), and routes empty-slot clicks to `BuildSlotPanel`.

### Missile (`TurretStats`, `Assets/Data/TurretStats.asset`)

6 stats across 2 categories:

**Launcher:**
- `FireRate` — shots per second. Base 0.5, +0.15 per level.
- `RotationSpeed` — degrees/sec for barrel aim. Base 30, +15 per level.

**Missile:**
- `MissileSpeed` — world units/sec. Base 4, +0.6 per level.
- `Damage` — feeds into `impactRadius = 0.14 + 0.04 * Damage`. Base 1, +1 per level.
- `BlastRadius` — additive splash radius in world units. Base 0.10, +0.25 per level.
- `Homing` — mid-flight steering in degrees/sec. Base 0 (no homing), +30 per level.

**Accuracy has been removed.** An earlier iteration had an "Accuracy" stat that applied a random launch wobble. That was dropped when Homing was added — missiles now fire straight at a specific target voxel every time, and Homing compensates for meteor drift mid-flight.

**Homing details:**
- On `MissileTurret.Fire`, the turret calls `target.PickRandomPresentVoxel()` to select a specific cell on the target meteor. If the pick succeeds, it passes the `(target, gx, gy, homingDegPerSec)` tuple to `Missile.Launch`. **If the pick fails (meteor died between `FindTarget` and `Fire`), the missile gets `null` as its homing target** and flies as a dumb projectile.
- `Missile.Update` steers velocity toward `target.GetVoxelWorldPosition(gx, gy)` using `Vector3.RotateTowards`, preserving speed magnitude. Guarded by `target != null && target.IsAlive && target.IsVoxelPresent(gx, gy)` — if any of those fail, the missile flies straight from that point.
- Collision behavior: `OnTriggerEnter2D` → `Meteor.ApplyBlast` at the missile's current position. The homing target is a steering hint, not a collision filter. Missiles explode on any meteor they touch along the way.

### Railgun (`RailgunStats`, `Assets/Data/RailgunStats.asset`)

5 stats, single column. Slow-firing straight-line piercing tunneler — opposite playstyle to the missile.

- `FireRate` — shots per second. Base 0.2 (5s between shots), +0.05 per level. Doubles as the barrel charge time.
- `RotationSpeed` — degrees/sec for barrel aim. Base 20, +12 per level.
- `Speed` — projectile world velocity. Base 6 world/sec, +3 per level. At base it's clearly visible; at high levels it's near-instant.
- `Weight` — depth budget in voxels. Base 4, +2 per level. Each live voxel destroyed consumes 1 from the budget; empty voxels are free.
- `Caliber` — tunnel width perpendicular to travel. Base 1 cell wide; +1/level up to 5 cells wide at level 2 (3-step discrete).

**Tunneling model (`Meteor.ApplyTunnel`):**
- The round walks the meteor's voxel grid along its world direction in half-cell steps.
- At each step it destroys all live voxels within a perpendicular band of width `caliberWidth` (1 → 1 cell, 2 → 3 cells, 3 → 5 cells).
- **Empty voxels are free** — they don't consume `Weight` budget. This is what lets the round glide through holes carved by earlier shots.
- Stops when budget hits zero OR the ray exits the grid. Returns the voxels consumed and the world-space exit point (used by `RailgunRound` to start tunneling the next meteor on a piercing shot).

**Round model (`RailgunRound`):**
- Visual-only GameObject — **no Rigidbody2D, no Collider2D**. Damage is resolved via per-frame `Physics2D.RaycastAll` against the `Meteors` physics layer.
- Each frame the round advances by `speed * deltaTime`, and that exact same delta is the raycast distance — manual continuous collision that works at any speed (base 6 to upgraded ~36 world/sec) without tunneling between physics ticks.
- The `Meteors` layer mask is critical: missiles are on the Default layer and are never returned by the raycast, so railgun shots can't accidentally damage missiles in their path.
- A `HashSet<Meteor> alreadyTunneled` prevents the same meteor being processed twice in one round's lifetime (curved collider edge case).
- On budget exhausted OR offscreen, spawns a `RailgunStreak` (stretched sprite, not a `LineRenderer` — voxel aesthetic forbids smooth lines) and destroys itself.

**Charge animation:** the `RailgunTurret` overrides `Update` to advance a charge timer up to `1/FireRate` seconds and step the barrel sprite color through 4 quantized stops (white → `#CEE8FE` → `#A8D6FE` → `#93DAFE`). Fires when fully charged AND aligned with the target. Snap back to dead white instantly on fire. No smooth `Color.Lerp` — quantized steps to match the voxel aesthetic.

### `Meteors` physics layer

Added at slot 8 via `manage_editor add_layer`. Assigned to the `Meteor.prefab` root GameObject. The `RailgunRound` raycasts against this layer only, which is how it filters out missiles, turret bases, and UI colliders. Unity's default Physics2D collision matrix allows all-vs-all, so missile-meteor trigger collisions still work unchanged.

**When spawning a meteor in test code:** assign the `Meteors` layer to the GameObject before calling `Spawn`. The `PlayModeTestFixture.SpawnTestMeteor` helper does this automatically — without it, railgun raycasts wouldn't find the test meteor.

## UI layout (current)

- **Money display:** top-center of the Canvas, TMP `$N` with `horizontalAlignment = Center`.
- **Two upgrade panels** (one per weapon, contextual to the clicked turret):
  - `UpgradePanel` (the GameObject for the missile panel — keeps its old name; the C# class is now `MissileUpgradePanel`). 520×460 centered, two-column layout: `LAUNCHER` column (FireRate, RotationSpeed) + `MISSILE` column (MissileSpeed, Damage, BlastRadius, Homing).
  - `RailgunUpgradePanel`. 280×460 centered, single-column layout for the 5 railgun stats. Title "RAILGUN" in `#93DAFE`.
- Both panels start hidden via `CanvasGroup alpha=0`. The GameObjects stay active so `Start()` can create buttons — do not `SetActive(false)` them.
- **`BaseSlot.OnPointerClick`** routes to the right panel based on `BuiltWeapon`. Clicking a built turret toggles the matching panel's CanvasGroup. Clicking an empty slot fires `EmptyClicked` which `SlotManager` routes to `BuildSlotPanel`.
- **Click-outside-to-close**: each upgrade panel has a sibling `UpgradeClickCatcher*` (full-screen transparent `Image` with `ModalClickCatcher`) rendered behind it. The catcher's `raycastTarget` is gated by `LateUpdate` to the panel's alpha, so it only catches clicks while the modal is actually open. Clicking the catcher closes the panel.
- **Build modal** (`BuildSlotPanel`): centered modal with one button per `WeaponType` in its `weapons` array. Each button shows the weapon-specific cost via `SlotManager.NextBuildCost(WeaponType)`. Has its own X close button + Escape key + click-outside-to-close.
- **Upgrade button:** `UpgradeButton` prefab, 230×68, label fontSize 20. Text format: `{name}\nLvl {lvl} — ${cost}`. Single prefab serves both weapons via `Bind()` (missile, takes a `TurretStats` + `StatId`) or `BindRailgun()` (railgun, takes a `RailgunStats` + `RailgunStatId`). Internal branching on which stats ref is set.
- **Click detection on the turret base** requires `Physics2DRaycaster` on `Main Camera` + `Collider2D` on the slot root + `IPointerClickHandler` on `BaseSlot`. `OnMouseDown` does not fire because the project is on the new Input System only (`activeInputHandler: 1`).

## Spawning

`MeteorSpawner` has a calm ramp: `initialInterval=12s`, `minInterval=4.5s`, `rampDurationSeconds=180s`. Don't casually regress these to faster — the user explicitly tuned them down (Iter 1 cut spawn rate to 33% of pre-cores values once dirt stopped paying out — fewer asteroids, but each one matters).

## Debug overlay (editor-only)

An editor-only overlay lets you tweak runtime values without grinding money. Key binding: **backquote (`` ` ``)**. When opened, the overlay pauses the game via `Time.timeScale = 0f` and shows a `DEBUG` panel with editable fields. First (and only) field today is a money setter.

- Script: `Assets/Scripts/Debug/DebugOverlay.cs`
- Scene GameObject: `DebugCanvas` (separate Canvas with `sortingOrder = 1000`)
- Editor-only enforcement: `#if UNITY_EDITOR` wraps all behavior; an `#else` stub class disables the GameObject in player builds so scene references don't break
- Toggle key detection: `UnityEngine.InputSystem.Keyboard.current.backquoteKey.wasPressedThisFrame`

**When adding a new debug control, extend this existing overlay** — don't create a new debug system. Full pattern guidance in `memory/reference_debug_overlay.md`.

## Before committing / pushing

- `git status` should show only files you expect.
- **Run the identity scrub before every commit:** `python3 tools/identity-scrub.py` against the staged diff. The script reads patterns from `.claude-identity-scrub` (gitignored, must exist at repo root) and exits 0 on clean / 1 on match / 2 on missing-patterns-file. Treat exit-1 and exit-2 as hard blockers. **Do not hand-write the identity tokens anywhere** — not in commit messages, not in docs, not in scratch scripts. The gitignored patterns file is the one place they live.
- If `.claude-identity-scrub` is missing on a fresh clone, copy the tokens from `~/.claude/projects/-Users-matt-dev-Unity-Meteor-Idle/memory/feedback_identity_leaks.md` (the durable memory, outside the repo) into the new file, one per line. The scrub script's error message tells you how.
- `git log --format="%an <%ae>" -5` should show only the repo-local identity (whatever `git config user.name` reports) on every recent commit. If any show the global/machine identity, something's broken — stop and diagnose.
- Never force-push main without explicit user permission.
- No `Co-Authored-By:` trailers on this repo.
- Work on a branch, not on main. Branch names: `iter/<kebab-description>`.

### Identity scrub tool

`tools/identity-scrub.py` — committed to the repo, contains zero identity tokens. Takes one of:

```bash
python3 tools/identity-scrub.py                  # scan staged diff (default, run before every commit)
python3 tools/identity-scrub.py main..HEAD       # scan a commit range (run before push)
python3 tools/identity-scrub.py --working-tree   # scan entire working tree vs HEAD
```

On match, it prints *"IDENTITY LEAK DETECTED"* and a count — but **not the tokens themselves**, to avoid echoing them into logs or chat transcripts. Find which pattern matched by reading `.claude-identity-scrub` manually.

On first use in a fresh clone, the script exits with code 2 and instructions. Populate `.claude-identity-scrub` with the tokens from the feedback-identity-leaks memory file, then re-run.

## WebGL build and GitHub Pages deploy

The game ships as a WebGL build hosted on `gh-pages` → <https://muwamath.github.io/Meteor-Idle/>. Builds are produced locally (no CI), and deploys are wired into the "promote a branch to main" flow below. No Unity license secrets, no GitHub Actions, no automated push.

### Pipeline

1. **`tools/build-webgl.sh`** — Unity CLI wrapper that runs `Unity -batchmode -nographics -quit -projectPath … -buildTarget WebGL -executeMethod BuildScripts.BuildWebGL`. Deletes `build/WebGL/` first, writes Unity's log to `build/webgl-build.log`, fails loudly if Unity isn't installed or the build exits non-zero. **Refuses to run while the Unity Editor has the project open** because the editor holds an exclusive lock — close Unity before calling this script. On success, deletes any stale `.dev-build-marker` sentinel (see below) so the output is unambiguously prod.
2. **`tools/build-webgl-dev.sh`** — the development-build variant. Same pipeline, but passes `BuildScripts.BuildWebGLDev` which sets `BuildOptions.Development` → Unity defines `DEVELOPMENT_BUILD` → `DebugOverlay` and any other `#if UNITY_EDITOR || DEVELOPMENT_BUILD`-wrapped debug surfaces are included. Output goes to `build/WebGL-dev/`. On success, **touches a `.dev-build-marker` sentinel** inside the output directory; this is how the deploy pipeline knows the build is dev and refuses to ship it.
3. **`tools/serve-webgl-dev.sh`** — runs `python3 -m http.server 8000 --directory build/WebGL-dev/`. Checks the port is free first; fails loudly with the holding PID if busy. Override via `PORT=<n>`. Foreground by default; run in the background from Claude via `run_in_background: true`.
4. **`Assets/Editor/BuildScripts.cs`** — contains `BuildWebGL` (prod) and `BuildWebGLDev` (dev). Both hardcode `Assets/Scenes/Game.unity` as the only scene and call `EditorApplication.Exit(1)` on failure so the shell wrappers can detect it.
5. **`tools/deploy-webgl.sh`** — after a successful prod build, rsyncs `build/WebGL/` into a git worktree at `../Meteor-Idle-gh-pages` on the `gh-pages` branch. Writes `.nojekyll` so Pages doesn't filter the `Build/` subdir. Stages and commits, but **does not push** — it prints the exact `git push` command for manual review. Refuses to run if the build output is missing, **if `.dev-build-marker` is present in the build output** (hard dev-build gate), if the patterns file is missing, if there are uncommitted changes in `Assets/` or `ProjectSettings/`, or if the identity scrub finds even a single match.

### Player settings the pipeline depends on

- `PlayerSettings.WebGL.compressionFormat = Brotli (0)`
- `PlayerSettings.WebGL.decompressionFallback = true (1)`

This pairing is the critical bit. GitHub Pages can't emit `Content-Encoding: br` headers, so a naked Brotli build refuses to load. The fallback embeds Unity's Brotli decoder in `WebGL.loader.js` (which bloats it from 27 KB → 118 KB) and renames artifacts to `WebGL.*.unityweb` so browsers don't auto-decompress by extension. The loader decompresses in JavaScript during startup. Net effect: ~14 MB total transfer (vs ~63 MB uncompressed) and still works on Pages with zero server config. **Don't "fix" this by switching to Disabled** — that was tried, shipped 63 MB, and the smaller build with fallback is strictly better.

### Identity scrub on the build output

The build directory is gitignored, so `identity-scrub.py`'s git-based modes don't cover it. `deploy-webgl.sh` falls back to a direct `grep -rIFif` against the filesystem. Two subtle requirements:

- **Strip comment and blank lines from the patterns file before feeding grep**, or `grep -f` interprets the blank line as "match every line" and every file becomes a 100% false positive. (This bug was shipped and reverted — see commit `72aba68`.)
- **Wrap the grep in `set +e` / `set -e`** so `pipefail` doesn't kill the script when grep exits with code 1 for "no matches found" (grep's normal success path for a clean build).

Known gap: `grep -I` skips binary files, so `Build/*.wasm`, `Build/*.data`, and `Build/*.framework.js.unityweb` are not scanned. In practice the high-risk leak vectors (`productName`, `companyName`, file paths in `index.html` / `WebGL.loader.js`) live in text files, and the scrub catches them. If you ever need to close the binary gap, add a `strings "${f}" | grep -Ff` pass for files `grep -I` skips.

### First-time gh-pages bootstrap

Already done once. For reference, the bootstrap steps were:

```
git worktree add --orphan -b gh-pages ../Meteor-Idle-gh-pages
cd ../Meteor-Idle-gh-pages
touch .nojekyll
# … initial placeholder README.md …
git add -A && git commit -m "Initial gh-pages branch"
git push -u origin gh-pages
```

Then Pages was enabled manually in the repo Settings UI (`Build and deployment → Source → Deploy from a branch → gh-pages / (root)`). **Never use `gh api` or `gh pr …` on this repo** — the `gh` CLI is authenticated as the user's main GitHub identity, and calling it against `muwamath/Meteor-Idle` routes API requests through the wrong account. All repo-level operations beyond plain `git push` via the `github-muwamath` SSH alias must be done manually in the browser.

## Testing

Two test assemblies in this project:

- **`MeteorIdle.Tests.Editor`** (`Assets/Tests/EditMode/`) — runs without entering play mode. Fast (~2 seconds for the whole suite). Targets pure game logic that doesn't need physics, time, or scene loading. **67 tests today.**
- **`MeteorIdle.Tests.PlayMode`** (`Assets/Tests/PlayMode/`) — runs inside a temporary play session. Slower (~16 seconds). Targets behavior that depends on real `Physics2D`, `Time.deltaTime`, or live `MonoBehaviour` lifecycles. **20 tests today.**

Run via `mcp__UnityMCP__run_tests` with `mode: "EditMode"` or `"PlayMode"` (and `assembly_names` to target one). Both modes must be green before promoting a branch to `main`.

### EditMode coverage

- `Meteor.ApplyBlast` — walk-inward crater logic, rim erosion, tunnel-through case, direct-hit case, aliveCount bookkeeping
- `Meteor.ApplyTunnel` — line-walking voxel destruction, empty-cells-are-free, budget cap, exit-point reporting, caliber 1/2/3 widths, diagonal direction
- `Meteor.IsVoxelPresent` / `GetVoxelWorldPosition` / `PickRandomPresentVoxel` — homing/targeting API
- `VoxelMeteorGenerator.Generate` — determinism (same seed → same grid), `aliveCount` matches grid truth, non-trivial shape range, texture dimensions
- `GameManager.TrySpend` / `AddMoney` / `SetMoney` plus `OnMoneyChanged` event firing
- `TurretStats` and `RailgunStats` — `NextCost` / `CurrentValue` formulas, `ApplyUpgrade` level tracking, single-stat isolation, `ResetRuntime`
- `SimplePool<T>` — prewarm/Get/Release cycle and active-list bookkeeping
- `SlotManager.NextBuildCost` — per-weapon cost escalation: in-table lookup, overflow multiplier, shared slot tier across weapons
- `MeteorSpawner.CurrentInterval` — cadence ramp lerp from `initialInterval` to `minInterval`, including clamp past full ramp

### PlayMode coverage

- **Existing-feature smoke tests** (`ExistingFeatureSmokeTests.cs`):
  - `Missile_LaunchedAtMeteor_Collides_DealsDamage` — Rigidbody2D → OnTriggerEnter2D → ApplyBlast chain
  - `Meteor_FallsAndFadesBelowThreshold_BecomesUntargetable` — fade logic in `Meteor.Update` over real time
  - `MeteorSpawner_SpawnsPooledMeteors_OverTime` — spawner cadence + pool prewarm
- **Railgun chain tests** (`RailgunPlayModeTests.cs`):
  - `RailgunRound_FiresIntoMeteor_DealsDamage` — per-frame raycast → `ApplyTunnel` end-to-end
  - `RailgunRound_PiercesTwoStackedMeteors` — `Weight` budget carries across multiple meteors in a line
  - `RailgunRound_LayerMask_IgnoresMissilesInPath` — `Meteors` layer filter excludes missile colliders
- **Turret targeting** (`TurretTargetingTests.cs`) — `TurretBase.FindTarget` via a `TestTurret` subclass: closest-live-meteor pick, out-of-range, empty spawner, dead-meteor filter. Meteors are injected into the spawner's `SimplePool.active` list via reflection.
- **Missile homing** (`MissileHomingTests.cs`) — `Missile.Update` steering: `RotateTowards` rotates velocity toward the target voxel bounded by `homingDegPerSec`, dumb projectile (homing=0) holds its line, steering stops when the target voxel is destroyed mid-flight.
- **Railgun charge animation** (`RailgunChargeAnimationTests.cs`) — instantiates `BaseSlot.prefab`, `Build(Railgun)`, and advances the private `chargeTimer` via reflection to hit each of the 4 quantized color stops. Also asserts `InitializeForBuild` resets to dead white.
- **Floating text** (`FloatingTextTests.cs`) — linear rise, alpha fade to 0 across `lifetime`, auto-destruction at `t >= 1`, and that `Show` resets alpha to 1 synchronously.

### What is *not* tested

- `MissileTurret` and `RailgunTurret` full `Update` reload→fire cycles — need real time + scene state. Verify by play mode.
- `SlotManager.Start` slot spawning — needs scene refs. Verify by play mode.
- All `UI/*` panel layouts — visual, can only be judged in-editor.
- `DebugOverlay` — editor-only, no runtime logic worth asserting.
- `RailgunStreak` fade animation — visual, eyeball-test in play mode.

### EditMode helper pattern

```csharp
var go = new GameObject("TestMeteor", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
var m = go.GetComponent<Meteor>();
TestHelpers.InvokeAwake(m);  // Awake doesn't reliably fire in EditMode tests; reflect-invoke it
m.Spawn(null, Vector3.zero, seed: 42, sizeScale: 1f);
// ... assertions ...
Object.DestroyImmediate(go);  // releases the texture the meteor allocated
```

`TestHelpers.InvokeAwake` and `TestHelpers.InvokeUpdate` are reflection-based shims because Unity doesn't reliably fire those methods on components added via `new GameObject(typeof(T))` in EditMode tests.

### PlayMode helper pattern

```csharp
[UnityTest]
public IEnumerator MyTest()
{
    yield return SetupScene();
    var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
    // ... do things ...
    yield return new WaitForSeconds(0.5f);
    Assert.Less(meteor.AliveVoxelCount, 65);
    TeardownScene();
}
```

Inherit from `PlayModeTestFixture`. `SpawnTestMeteor` automatically assigns the `Meteors` physics layer, `SpawnTestMissile` loads the missile prefab via `AssetDatabase`, `SpawnTestRailgunRound` loads + configures the railgun round prefab. `SpawnTestSpawner` creates an inactive GameObject, sets its prefab field via `SerializedObject`, then activates — necessary because `MeteorSpawner.Awake` prewarms the pool and would throw on a null prefab if the spawner were added the naive way.

### Before promoting a branch to `main`

1. Run **both** test suites (`mcp__UnityMCP__run_tests mode=EditMode` and `mode=PlayMode`).
2. **Verify via local WebGL dev build, not editor play mode.** Invoke `BuildScripts.BuildWebGLDev()` via `mcp__UnityMCP__execute_code` (or the `Meteor Idle → Build → WebGL (Dev)` menu item — same method). Run `tools/serve-webgl-dev.sh` in the background, open `http://localhost:8000/` via `chrome-devtools-mcp`, exercise the change, check the devtools console for JS errors, take a screenshot, close the tab, kill the server. The debug overlay (`` ` `` key) is available in dev builds for live inspection. **Do not ask the user to close Unity** — everything runs against the live editor.
3. Run `python3 tools/identity-scrub.py` against the staged diff (and again against the full branch range before push).
4. Hand back to user for sign-off. Only after explicit approval, fast-forward `main` to the branch tip.
5. **After fast-forwarding, produce and ship a fresh prod WebGL build.** Invoke `BuildScripts.BuildWebGL()` via `execute_code` (or the `Meteor Idle → Build → WebGL (Prod)` menu item) — the method deletes any stale `.dev-build-marker` on success so the output is unambiguously deployable. Then run `tools/deploy-webgl.sh`. The deploy script refuses to run if it finds a dev-build sentinel, so the prod build step is non-optional. Smoke-test the staged `gh-pages` worktree if the change touched anything user-visible, then `git -C ../Meteor-Idle-gh-pages push origin gh-pages`. Confirm <https://muwamath.github.io/Meteor-Idle/> is serving the new build before closing the loop.

Tests alone are not sufficient — they don't catch scene drift, panel-click routing, UI layout, or timing issues.

## Useful reference

- Design specs and implementation plans live under `docs/superpowers/specs/` and `docs/superpowers/plans/`. The most recent iterations:
  - [Railgun weapon design](docs/superpowers/specs/2026-04-10-railgun-weapon-design.md) — second weapon: tunneling straight-line piercer with charge animation, full architectural rationale for `TurretBase`/`MissileTurret`/`RailgunTurret` split, raycast-driven projectile model
  - [Railgun implementation plan](docs/superpowers/plans/2026-04-10-railgun-weapon.md) — 13-phase task-by-task execution plan for the railgun
  - [Upgrades expansion plan](docs/superpowers/plans/2026-04-10-upgrades-expansion.md) — 6 stats across 2 categories, homing, rotation speed
  - [Voxel meteors design](docs/superpowers/specs/2026-04-10-voxel-meteors-design.md) — the current voxel destruction model
  - [Voxel meteors implementation plan](docs/superpowers/plans/2026-04-10-voxel-meteors.md) — task-by-task breakdown of the voxel work
  - [Original MVP spec](docs/superpowers/specs/2026-04-10-meteor-idle-mvp-design.md) — superseded but useful context
