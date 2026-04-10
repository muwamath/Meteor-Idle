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

- Meteors spawn from above and drift downward.
- The turret auto-targets and auto-fires — at starting stats it's deliberately slow and wobbly.
- Each missile that hits a meteor chews out a small cluster of voxel cubes. You're paid **$1 per voxel destroyed**.
- Click the turret base at the bottom of the screen to open the upgrade panel.
- Spend money on five stats: Fire Rate, Missile Speed, Damage, Accuracy, Blast Radius.
- Meteors that escape to the ground don't penalize you (yet).

## Technology

- Unity 6000.4.1f1
- 2D Universal Render Pipeline (URP)
- C# (no assembly definitions, single Assembly-CSharp)
- New Input System
- TextMeshPro for UI

All art is procedurally generated at edit time by C# editor scripts — there are no bitmap files authored in external tools. The voxel meteors, turret, missile, starfield, and particle sprites are all PNGs written by Unity at build time from procedural code.

## Design and plan documents

- [Voxel meteors design spec](docs/superpowers/specs/2026-04-10-voxel-meteors-design.md) — current voxel destruction model
- [Voxel meteors implementation plan](docs/superpowers/plans/2026-04-10-voxel-meteors.md) — task-by-task breakdown
- [MVP design spec](docs/superpowers/specs/2026-04-10-meteor-idle-mvp-design.md) — original smooth-sprite MVP design (superseded by the voxel spec)

## License

MIT — see [LICENSE](LICENSE).
