# Meteor Idle

A 2D desktop idle game built in Unity 6. Voxel meteors rain from the top of the screen; a base at the bottom auto-fires missiles that chew chunks out of them. Each destroyed voxel pays out. Spend the money to upgrade the turret.

## Status

Early in development. Single base, single weapon, no persistence, no audio. Core loop is playable.

## Running

1. Install **Unity 6000.4.1f1** via Unity Hub (exact version — the project is pinned).
2. Clone the repo:
   ```
   git clone git@github.com:muwamath/Meteor-Idle.git
   ```
3. Open the project in Unity Hub → **Add project from disk** → select the cloned folder.
4. Once the editor opens, load `Assets/Scenes/Game.unity`.
5. Hit **Play**.

## How to play

- Meteors spawn from above and drift downward. They're made of small cube voxels on a 10×10 grid.
- The turret at the bottom auto-targets the nearest meteor and auto-fires missiles toward a specific voxel on it.
- Each missile that hits a meteor destroys a small cluster of voxels. You earn **$1 per voxel destroyed** — partial destruction pays out, so every hit counts, even if the meteor isn't fully cleared.
- **Click the turret base** to open the upgrade panel (centered on screen). Click again to close.
- Upgrades are split into two categories:

  **Launcher**
  - **Fire Rate** — shots per second
  - **Rotation Speed** — how quickly the barrel rotates to track targets

  **Missile**
  - **Missile Speed** — how fast missiles travel
  - **Damage** — how wide the direct impact radius is
  - **Blast Radius** — extra splash destruction around the impact
  - **Homing** — how aggressively the missile steers mid-flight toward its target voxel

- Meteors that escape to the ground don't penalize you (yet).
- Press **`` ` ``** (backquote) in the editor while playing to open a debug overlay that pauses the game and lets you tweak values (currently: set current money). The debug overlay only exists in editor play mode — it's stripped from player builds.

## Technology

- Unity 6000.4.1f1
- 2D Universal Render Pipeline (URP)
- C# (no assembly definitions, single Assembly-CSharp)
- New Input System
- TextMeshPro for UI

All art is procedurally generated at edit time by C# editor scripts — there are no bitmap files authored in external tools. The voxel meteors, turret, missile, starfield, and particle sprites are all PNGs written by Unity at build time from procedural code.

## Design and plan documents

- [Upgrades expansion plan](docs/superpowers/plans/2026-04-10-upgrades-expansion.md) — 6 stats across Launcher/Missile categories, homing, rotation speed
- [Voxel meteors design spec](docs/superpowers/specs/2026-04-10-voxel-meteors-design.md) — voxel destruction model
- [Voxel meteors implementation plan](docs/superpowers/plans/2026-04-10-voxel-meteors.md) — task-by-task breakdown
- [MVP design spec](docs/superpowers/specs/2026-04-10-meteor-idle-mvp-design.md) — original smooth-sprite MVP design (superseded by the voxel spec)

## License

MIT — see [LICENSE](LICENSE).
