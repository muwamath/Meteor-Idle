# CLAUDE.md — Meteor Idle

Working guidance for Claude Code sessions in this repo.

## What this is

A 2D Unity 6 idle game. Voxel meteors fall; up to 3 base slots along the bottom auto-fire weapons that destroy meteor voxels. Destroyed voxels pay $1 each; money buys per-weapon upgrades. Two weapon types today (Missile and Railgun). Desktop/laptop target, landscape 16:9. Early development — no persistence, no audio.

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
  Prefabs/
    BaseSlot.prefab             slot root with two weapon-child siblings (see UI section)
    Meteor.prefab               voxel meteor on the Meteors physics layer
    Missile.prefab              homing missile with Rigidbody2D + trigger collider
    RailgunRound.prefab         visual-only railgun bullet (no collider, per-frame raycast)
    RailgunStreak.prefab        stretched-sprite trailing line VFX
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
    SlotManager.cs              spawns 3 slots, per-weapon NextBuildCost(WeaponType)
    MeteorSpawner.cs            timer + pool, calm starting cadence
    GameManager.cs              money singleton + OnMoneyChanged event, SetMoney for debug
    SimplePool.cs               generic MonoBehaviour pool
    FloatingText.cs             world-space "+$N" tween
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
      UpgradeButton.cs          shared prefab; Bind() for missile, BindRailgun() for railgun
      BuildSlotPanel.cs         modal for buying a new base slot (per-weapon costs)
      BuildWeaponButton.cs      one per weapon type in the build modal
      ModalClickCatcher.cs      click-outside-to-close behind any modal CanvasGroup
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
    TestHelpers.cs              reflection-invoked Awake/Update for EditMode tests
  PlayMode/
    MeteorIdle.Tests.PlayMode.asmdef
    PlayModeTestFixture.cs      shared spawn helpers for meteor/missile/railgun
    ExistingFeatureSmokeTests.cs  missile collision, meteor fade, spawner pooling
    RailgunPlayModeTests.cs     fires-into-meteor, pierces-two, layer-mask filter
    TurretTargetingTests.cs     TurretBase.FindTarget via TestTurret subclass
    MissileHomingTests.cs       RotateTowards steering, dumb case, target-lost
    RailgunChargeAnimationTests.cs  4-stop quantized barrel color via chargeTimer
    FloatingTextTests.cs        rise, alpha fade, auto-destruction
tools/
  identity-scrub.py             pre-commit identity-leak check (see "Identity scrub" section)
docs/superpowers/
  specs/                        design docs (spec per iteration)
  plans/                        implementation plans (task-by-task)
```

## Conventions you must follow

**Prefer EditMode unit tests for pure logic; manual play-mode verification is required before anything reaches remote `main`.** Tests live in `Assets/Tests/EditMode/`. They are fast (a few ms each), run without entering play mode, and have caught real bugs in `Meteor.ApplyBlast`. When you add or change logic in one of the tested modules (`Meteor`, `VoxelMeteorGenerator`, `GameManager`, `TurretStats`, `SimplePool`), update or add a test in the same change. Run the suite via `mcp__UnityMCP__run_tests` (or Window → General → Test Runner in the editor) and expect zero failures before committing. Do not push to `origin/main` without also having play-tested the change end-to-end in the editor — tests verify individual modules, not feel or scene wiring.

The manual verification loop (use for anything scene- or UI-related, and as the final check before promoting a branch):
1. Edit C# / mutate via `execute_code`
2. `refresh_unity` (`scope=scripts` or `all`, `compile=request`)
3. `read_console` — expect zero errors
4. Run `mcp__UnityMCP__run_tests` if the change touches tested logic
5. `manage_editor play`
6. Wait briefly, then `manage_camera screenshot include_image=true`
7. `read_console` again
8. `manage_editor stop`

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

`MeteorSpawner` has a calm ramp: `initialInterval=4s`, `minInterval=1.5s`, `rampDurationSeconds=180s`. Don't casually regress these to faster — the user explicitly tuned them down.

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
2. Run a manual play-mode session in the editor and verify the change end-to-end.
3. Run `python3 tools/identity-scrub.py` against the staged diff (and again against the full branch range before push).
4. Hand back to user for sign-off. Only after explicit approval, fast-forward `main` to the branch tip.

Tests alone are not sufficient — they don't catch scene drift, panel-click routing, UI layout, or timing issues.

## Useful reference

- Design specs and implementation plans live under `docs/superpowers/specs/` and `docs/superpowers/plans/`. The most recent iterations:
  - [Railgun weapon design](docs/superpowers/specs/2026-04-10-railgun-weapon-design.md) — second weapon: tunneling straight-line piercer with charge animation, full architectural rationale for `TurretBase`/`MissileTurret`/`RailgunTurret` split, raycast-driven projectile model
  - [Railgun implementation plan](docs/superpowers/plans/2026-04-10-railgun-weapon.md) — 13-phase task-by-task execution plan for the railgun
  - [Upgrades expansion plan](docs/superpowers/plans/2026-04-10-upgrades-expansion.md) — 6 stats across 2 categories, homing, rotation speed
  - [Voxel meteors design](docs/superpowers/specs/2026-04-10-voxel-meteors-design.md) — the current voxel destruction model
  - [Voxel meteors implementation plan](docs/superpowers/plans/2026-04-10-voxel-meteors.md) — task-by-task breakdown of the voxel work
  - [Original MVP spec](docs/superpowers/specs/2026-04-10-meteor-idle-mvp-design.md) — superseded but useful context
