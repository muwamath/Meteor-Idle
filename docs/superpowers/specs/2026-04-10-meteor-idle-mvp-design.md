# Meteor Idle — MVP Design

**Date:** 2026-04-10
**Status:** Approved for implementation planning
**Engine:** Unity 6000.4.1f1, 2D URP
**Target:** Desktop / laptop, landscape 16:9

## 1. Overview

Meteor Idle is a landscape 2D idle game. Meteors fall from the top of the screen; a single base at the bottom with an auto-firing missile turret shoots them down. Every successful kill rewards money. Money is spent on upgrades that improve the turret. Deliberately, the turret starts *bad* — slow fire rate, slow missiles, wobbly aim, weak damage, no splash — so that upgrading feels impactful.

This MVP is the first playable iteration. No persistence, no health/loss system, no offline earnings, no audio, no multi-base. Just the core shoot → earn → upgrade loop, with satisfying feel.

## 2. Goals and Non-Goals

### Goals
- A single base with one missile turret that auto-targets and auto-fires.
- Meteors spawn from above, fall with slight drift, procedurally generated lumpy shapes.
- 5 upgrade stats: Fire Rate, Missile Speed, Damage, Aim Accuracy, Blast Radius.
- Clear visual feedback: missile trails, muzzle flash, debris particles on kill, floating "+$N" money text.
- Simple but interesting visuals — procedural starfield background, procedural meteor textures.
- Clean architecture: small single-purpose files, communication via events and ScriptableObject data, no god objects.

### Non-Goals (explicitly deferred)
- Persistence / save-load.
- Offline earnings.
- Multiple bases, multiple weapon types, weapon unlocks.
- Base health or player loss condition.
- Audio / music / SFX.
- Prestige, achievements, tech tree.
- Settings menu, pause screen, main menu.
- Mobile / touch input.

## 3. Project Cleanup

The project is a fresh Unity 6 2D URP template. Remove files that are template leftovers and not needed for Meteor Idle.

### Delete
- `Assets/Scenes/SampleScene.unity` (replaced by `Game.unity`).
- `Assets/InputSystem_Actions.inputactions` — unused; game is mouse-only click-to-upgrade, no input map needed.
- `Assets/Settings/Lit2DSceneTemplate.scenetemplate` — unused scene template.
- `Assets/Settings/Scenes/` — default scene template leftovers (verify empty of real content first).

### Keep
- `Assets/Settings/UniversalRP.asset` and `Renderer2D.asset` — URP 2D pipeline.
- `Assets/DefaultVolumeProfile.asset` — URP requires it.
- `Assets/UniversalRenderPipelineGlobalSettings.asset`.
- `Assets/Plugins/UnityCodeMcpServer` — MCP tooling, not game code.
- All `com.unity.modules.*` entries in `Packages/manifest.json` — trimming built-in modules is risk without reward.

## 4. Scene Layout

**Scene:** `Assets/Scenes/Game.unity`

**Playfield:** ~32 units wide × 18 units tall (16:9), orthographic camera centered on origin.

### Hierarchy
```
--- Game ---
  Main Camera              (Orthographic, size ~9, background dark)
  Global Light 2D          (soft bluish, required by 2D Renderer)
  Background               (starfield sprite quad behind everything)
  GroundLine               (thin horizontal sprite at y ≈ -7)
  GameManager              (empty GO, GameManager.cs)
  MeteorSpawner            (empty GO, MeteorSpawner.cs)
  Pools                    (empty GO, parent for pooled objects)
  Base_01                  (prefab; has Turret child)
  UI Canvas                (Screen Space Overlay, contains MoneyDisplay + UpgradePanel)
```

### Zones
- **Spawn zone:** y = +10, random x ∈ [-15, +15]. Meteors spawn above the visible area.
- **Kill zone:** y < -7 (ground line). Meteor silently returns to pool. No player penalty in MVP.
- **Turret range:** configurable radius around the base; targets outside range are ignored.

## 5. Components (Scripts)

All scripts live under `Assets/Scripts/`. Each file has one clear purpose. Estimated ~13 files.

### Gameplay
- **`GameManager.cs`** — singleton. Holds `Money` (int). Exposes `AddMoney(int)` and `TrySpend(int) -> bool`. Raises `OnMoneyChanged` event. No spawning, no shooting logic.
- **`MeteorSpawner.cs`** — timer-based spawner. Pulls from `MeteorPool`, positions at spawn zone, assigns a random seed. Spawn rate follows a gentle fixed ramp curve over elapsed game time.
- **`Meteor.cs`** — falls under constant downward velocity plus small horizontal drift. Has HP scaled from size. `TakeDamage(int)`; on HP ≤ 0 → `Die()` (debris + floating text + money reward + pool return). On crossing ground line → silent pool return.
- **`MeteorShapeGenerator.cs`** — static utility. `Generate(int seed) -> Texture2D`. Creates a lumpy circle texture (see §6). Caches results by seed.
- **`Turret.cs`** — mounted on Base_01. Scans active meteors for nearest within range. Rotates barrel toward target at a fixed rotation speed (not an upgraded stat in MVP). Fires missiles on reload timer when aligned. Reads all fire stats from its `TurretStats` SO.
- **`Missile.cs`** — straight-line projectile. Velocity set at launch from turret forward × `MissileSpeed`, rotated by aim error. On trigger-enter with meteor: deals damage; if `BlastRadius > 0`, applies splash via `Physics2D.OverlapCircleAll`. Spawns explosion particle. Returns to pool. Lifetime timeout ~5s.
- **`MissileTrail.cs`** — attaches `TrailRenderer` + child smoke `ParticleSystem` to a missile.
- **`Debris.cs`** — on meteor death, spawns a particle burst of small rock chunks (gravity-affected).
- **`FloatingText.cs`** — world-space TMP label. Tweens upward ~1.5 units over 1s while fading alpha 1 → 0.

### Data
- **`TurretStats.cs`** (ScriptableObject) — holds the 5 stats. Each stat has: `level` (int), `baseValue`, `perLevelAdd` (or curve), `baseCost`, `costGrowth`. Exposes `CurrentValue(statId)` and `NextUpgradeCost(statId)` and `ApplyUpgrade(statId)`. Runtime mutation only — no save.
  - **Stats:** `FireRate` (shots/sec), `MissileSpeed` (units/sec), `Damage` (int), `Accuracy` (0..1, where 1 = perfect), `BlastRadius` (units; starts at 0).

### Pools
- **`SimplePool<T>.cs`** — generic MonoBehaviour pool with prewarm and an inactive queue. Used for `Meteor`, `Missile`, `Debris`, `FloatingText`.

### UI
- **`UIRoot.cs`** — holds references to the UI subcomponents; created once in the Canvas.
- **`MoneyDisplay.cs`** — listens to `GameManager.OnMoneyChanged`, updates a TMP label.
- **`UpgradePanel.cs`** — 5 buttons (one per stat). Each shows stat name, current level, next cost. Click → `GameManager.TrySpend(cost)`; on success → `TurretStats.ApplyUpgrade(statId)`. Buttons greyed out when unaffordable.

### Boundaries
- `Turret` knows nothing about `UpgradePanel`.
- `MeteorSpawner` knows nothing about `Turret`.
- Everything coordinates through `GameManager` events and the `TurretStats` SO.

## 6. Visual and Feel Details

### Meteor shape generation
- Target `Texture2D` 128×128, transparent background.
- Base radius ≈ 50 px. For each pixel at angle θ from center: `radius(θ) = baseR * (1 + 0.25 * noise(θ))` where `noise` is a sum of 3 sine waves at different frequencies seeded by the meteor seed. Produces a lumpy non-circular silhouette.
- Fill: vertical gradient `#8b7355` (warm brown, top) → `#4a3a2a` (dark brown, bottom), modulated by a fake lighting term brighter in the upper-left.
- 5–10 darker circular "craters" scattered at random interior positions.
- 1–2 px darker rim around the silhouette.
- Wrapped in a `Sprite` and assigned to the meteor's `SpriteRenderer`. Cached by seed.

### Missile trail
- `TrailRenderer` with ~0.3s lifetime, width 0.15 → 0, gradient orange → transparent.
- Child `ParticleSystem` emitting tiny grey smoke puffs drifting slightly upward — adds depth beyond a flat line.

### Meteor death — debris + floating text
- `ParticleSystem` burst: 12–20 small brown rock chunks (4×4 px sprites), random outward velocity, gravity on, ~1s lifetime.
- `FloatingText`: `+$N` TMP label, world-space, gold color `#ffd76a`, tween upward and fade over 1s.

### Turret visual
- Flat-sprite base (rectangular dark grey) with a rotating child barrel (thin lighter-tipped rectangle).
- Only the barrel rotates; the base stays fixed.
- Muzzle flash: small quick particle burst at the barrel tip on fire.

### Background
- Dark vertical gradient background quad: deep navy at top → near-black at bottom.
- One-time-generated 512×512 starfield texture: ~200 random white pixels at varying alpha, stretched/tiled across the background quad.

### Audio
- **None in MVP.** Explicitly called out so it is not missed in planning.

## 7. Starting Values and Feel

To make upgrading feel impactful, the turret starts deliberately weak.

| Stat | Start | Notes |
|---|---|---|
| Fire Rate | 0.5 shots/sec | One shot every 2 seconds — slow |
| Missile Speed | 4 units/sec | Visibly slow |
| Damage | 1 | Most meteors take several hits |
| Accuracy | 0.5 | Heavy wobble — missiles often miss |
| Blast Radius | 0 | No splash — must hit directly |

Meteor HP scales with size (small ≈ 1, medium ≈ 3, large ≈ 6). Money reward scales with HP. Spawn rate ramps gently so the early game is survivable despite the weak turret.

Exact numbers will be tuned during implementation. These are starting points, not balance decisions.

## 8. Data Flow Summary

**Meteor lifecycle:** Spawner timer → pool get → shape generator → fall → damaged → die (debris + floating text + money) or cross ground (silent return).

**Turret firing:** Scan meteors → pick nearest in range → rotate barrel → when aligned + reload ready → pool get missile → set velocity with aim error → reset reload.

**Missile lifecycle:** Fly straight → trigger-enter meteor → damage + optional splash → explosion particle → pool return. Or: lifetime timeout → pool return.

**Economy:** Meteor death → `GameManager.AddMoney` → `OnMoneyChanged` event → `MoneyDisplay` updates. Upgrade click → `UpgradePanel` → `GameManager.TrySpend` → `TurretStats.ApplyUpgrade` → turret reads new value on next frame.

## 9. Testing Strategy

MVP testing is **manual in-editor**, not automated. Unity Test Framework is available for later iterations, but the MVP's value is feel and visuals — both of which need eyes, not asserts.

Manual verification checklist per iteration build:
- Meteors spawn, fall, and are visually varied (lumpy shapes differ).
- Turret acquires nearest target and rotates toward it.
- Missiles fly with visible trail and can miss due to accuracy wobble.
- Hit → damage → kill → debris burst + floating `+$N` + money total increases.
- Miss → meteor crosses ground → silently despawns.
- Each of the 5 upgrades visibly changes turret behavior.
- Upgrade buttons grey out when unaffordable and re-enable when affordable.
- No console errors or warnings during a 2-minute play session.

## 10. Out of Scope for This Design

Future iterations (not part of this spec, not part of the first implementation plan):
- Save/load and offline earnings.
- Additional bases and weapon types.
- Base health and player loss conditions.
- Audio pass.
- Main menu / settings / pause.
- Balance tuning as a formal pass (MVP numbers are first-guess).
- Prestige system.

Each of these gets its own design cycle when the time comes.
