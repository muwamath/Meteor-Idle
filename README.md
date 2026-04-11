# Meteor Idle

A 2D desktop idle game built in Unity 6. Voxel meteors rain from the top of the screen; up to 3 base slots along the bottom auto-fire weapons that chew chunks out of them. Each destroyed voxel pays out. Spend the money to buy new slots, choose weapons, and upgrade them.

## Play it

**<https://muwamath.github.io/Meteor-Idle/>**

Runs in any modern browser. The WebGL build is Brotli-compressed with Unity's decompression fallback enabled, so it works on GitHub Pages without any server-side `Content-Encoding` configuration. Total transfer is roughly 14 MB; expect a short initial load on the first visit, instant on subsequent visits via the browser cache.

## Status

Early in development. **3 base slots, 2 weapons (Missile and Railgun)**, no persistence, no audio. Core loop is playable end-to-end: buy slots, build either weapon into them, fire at meteors, upgrade individual stats per weapon. The economy is currently flattened to **$1 per purchase** for fast development iteration; balance pass comes later.

## Running

1. Install **Unity 6000.4.1f1** via Unity Hub (exact version — the project is pinned).
2. Clone the repo:
   ```
   git clone git@github.com:muwamath/Meteor-Idle.git
   ```
3. Open the project in Unity Hub → **Add project from disk** → select the cloned folder.
4. Once the editor opens, load `Assets/Scenes/Game.unity`.
5. Hit **Play**.

### Building and deploying the WebGL player

The live build at <https://muwamath.github.io/Meteor-Idle/> is produced locally — no CI. Four scripts drive the pipeline, all in `tools/`:

- **`tools/build-webgl.sh`** — headless Unity prod build via `Unity -batchmode -executeMethod BuildScripts.BuildWebGL`. Writes to `build/WebGL/` (gitignored). Close the Unity Editor before running; the script refuses to start while the editor holds the project lock. On success, deletes any stale `.dev-build-marker` sentinel so the output is unambiguously deployable.
- **`tools/build-webgl-dev.sh`** — same pipeline, but invokes `BuildScripts.BuildWebGLDev` which sets `BuildOptions.Development`. Unity auto-defines `DEVELOPMENT_BUILD`, which unlocks the debug overlay and any other `#if UNITY_EDITOR || DEVELOPMENT_BUILD`-gated surfaces. Writes to `build/WebGL-dev/`. On success, touches a `.dev-build-marker` sentinel inside the output directory — this is the deploy pipeline's "is this a dev build?" signal.
- **`tools/serve-webgl-dev.sh`** — `python3 -m http.server 8000 --directory build/WebGL-dev/` with a port-availability precheck. Used during manual verification. Override the port with `PORT=<n>`.
- **`tools/deploy-webgl.sh`** — runs the identity scrub on the build output, `rsync`s it into a `gh-pages` linked worktree at `../Meteor-Idle-gh-pages`, writes `.nojekyll`, and makes a commit. The script does **not** push — it prints the exact `git push` command for manual review. **Refuses to deploy if it finds a `.dev-build-marker` sentinel**, making it impossible to accidentally ship a development build to GitHub Pages.

All scripts must be run from the repo root. The deploy fires after a branch has been fast-forwarded to `main` and verified end-to-end via a local WebGL dev build (not editor play mode — editor play mode doesn't run the same code path real players see, so it's only used for fast iterative debugging during development).

## How to play

- Meteors spawn from above and drift downward. They're made of small cube voxels on a 10×10 grid.
- The game starts with 3 base slots along the bottom of the screen. The center slot is pre-built with a Missile turret. The two side slots start empty, shown as a `+` icon.
- **Click an empty slot** to open the build modal. Pick a weapon (Missile or Railgun) and pay its cost to install it.
- **Click a built turret** to open its upgrade panel — there's a separate panel for each weapon type because their stats are different. **Click outside the panel** (or press Escape) to close it.
- Each missile/railgun shot that destroys voxels earns **$1 per voxel destroyed**, and partial destruction pays out — every hit counts even if the meteor isn't fully cleared.
- Meteors that drift past the base level fade out without penalty (yet).
- Press **`` ` ``** (backquote) in the editor while playing, or in a local WebGL dev build served via `tools/serve-webgl-dev.sh`, to open a debug overlay that pauses the game and lets you tweak values (currently: set current money, full game reset). The debug overlay is gated on `UNITY_EDITOR || DEVELOPMENT_BUILD` and is stripped from the production build deployed to GitHub Pages.

### Weapons

**Missile turret** — fast, light, homing.

- Auto-targets the nearest meteor and picks a specific live voxel to aim at.
- Fires a missile that homes mid-flight (if Homing is upgraded) toward the target voxel.
- On contact: blasts a circular crater. The blast walks inward to the nearest live voxel if the contact point is on an eroded rim.
- 6 stats across 2 categories:
  - **Launcher:** Fire Rate, Rotation Speed
  - **Missile:** Missile Speed, Damage, Blast Radius, Homing

**Railgun turret** — slow, heavy, straight-line piercing.

- Auto-targets the nearest meteor like the missile turret, but fires a dead-straight round (no homing).
- Spends a "charge time" between shots, with the barrel visibly filling from white to bright blue. Fires the instant the charge completes if a target is in alignment.
- The round is a fast-moving white bullet with a blue trailing streak. On hitting a meteor it **carves a tunnel** along its trajectory rather than blasting a circular crater. Empty voxels in the tunnel path are free — the round glides through holes carved by earlier shots.
- If the round still has tunneling budget after exiting one meteor, it **pierces through to the next meteor** in its path.
- 5 stats:
  - **Fire Rate** — shots per second (also the visible charge time)
  - **Rotation Speed** — barrel slew speed
  - **Speed** — round travel velocity (slow at base, near-instant at high upgrade)
  - **Weight** — depth budget; how many voxels the round can destroy before despawning
  - **Caliber** — tunnel width perpendicular to travel (1 / 3 / 5 cells, capped at level 2)

The two weapons feel mechanically different: missiles are spammy and area-of-effect, railguns are slow ponderous burst damage with line-of-sight piercing. Mid- and late-game you'll likely have a mix of both across the 3 slots.

## Technology

- Unity 6000.4.1f1
- 2D Universal Render Pipeline (URP)
- C# game code in the **`MeteorIdle`** assembly definition
- New Input System
- TextMeshPro for UI
- **Unity Test Framework**: 67 EditMode tests + 20 PlayMode tests covering voxel destruction logic, the per-weapon stats, build-cost escalation, spawner cadence ramp, the per-frame raycast railgun chain, turret targeting, missile homing steering, the quantized railgun charge color animation, the floating-text rise/fade curve, missile collision, and meteor fade

All art is procedurally generated at edit time by C# editor scripts — there are no bitmap files authored in external tools. The voxel meteors, both turret barrels, the missile, the railgun bullet, the railgun streak, the starfield, and the particle sprites are all PNGs written by Unity at build time from procedural code. Hard pixel edges, 1-pixel dark/light edges, no smooth gradients — strict voxel aesthetic throughout.

## Project layout

```
Assets/
  Art/                          procedurally-generated PNGs
  Data/
    TurretStats.asset           Missile stats (6)
    RailgunStats.asset          Railgun stats (5)
  Prefabs/                      BaseSlot, Meteor, Missile, RailgunRound, RailgunStreak, ...
  Scenes/Game.unity             the one scene
  Scripts/
    MeteorIdle.asmdef           game-code assembly
    Meteor.cs, MeteorSpawner.cs, GameManager.cs, ...
    TurretBase.cs               abstract base class for weapon turrets
    MissileTurret.cs, RailgunTurret.cs
    BaseSlot.cs, SlotManager.cs
    Weapons/                    WeaponType enum, RailgunRound, RailgunStreak
    Data/                       TurretStats.cs, RailgunStats.cs
    UI/                         MissileUpgradePanel, RailgunUpgradePanel, BuildSlotPanel, ...
    Debug/DebugOverlay.cs       editor-only money setter + reset
Tests/
  EditMode/                     67 logic tests (~2s runtime)
  PlayMode/                     20 physics/integration tests (~16s runtime)
tools/
  identity-scrub.py             pre-commit identity-leak check
  build-webgl.sh                headless Unity CLI wrapper for the prod WebGL build
  build-webgl-dev.sh            same, DEVELOPMENT_BUILD flavor with debug overlay unlocked
  serve-webgl-dev.sh            python http.server on :8000 serving build/WebGL-dev/
  deploy-webgl.sh               rsyncs build/WebGL/ to the gh-pages worktree, scrubs, commits
Assets/Editor/BuildScripts.cs   editor-side BuildWebGL + BuildWebGLDev entry points
docs/superpowers/
  specs/                        design docs (one per iteration)
  plans/                        implementation plans (task-by-task)
```

## Design and plan documents

- [Railgun weapon design](docs/superpowers/specs/2026-04-10-railgun-weapon-design.md) — second weapon: tunneling straight-line piercer with charge animation
- [Railgun implementation plan](docs/superpowers/plans/2026-04-10-railgun-weapon.md) — 13-phase rollout
- [Upgrades expansion plan](docs/superpowers/plans/2026-04-10-upgrades-expansion.md) — 6 missile stats across Launcher/Missile categories, homing, rotation speed
- [Voxel meteors design spec](docs/superpowers/specs/2026-04-10-voxel-meteors-design.md) — voxel destruction model
- [Voxel meteors implementation plan](docs/superpowers/plans/2026-04-10-voxel-meteors.md) — task-by-task breakdown
- [MVP design spec](docs/superpowers/specs/2026-04-10-meteor-idle-mvp-design.md) — original smooth-sprite MVP (superseded by the voxel spec)

## License

MIT — see [LICENSE](LICENSE).
