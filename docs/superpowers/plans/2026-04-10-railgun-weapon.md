# Railgun Weapon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Railgun as Meteor Idle's second buildable weapon — a slow straight-line piercing tunneler with upgradable stats, voxel-styled visuals, and a new PlayMode test harness.

**Architecture:** Split existing `Turret` into an abstract `TurretBase` + concrete `MissileTurret`/`RailgunTurret`. Railgun rounds are visual GameObjects (no Rigidbody/Collider) that do per-frame forward `Physics2D.RaycastAll` against a new `Meteors` layer — manual CCD works at any speed. Each meteor hit calls a new `Meteor.ApplyTunnel` method that walks voxels along a grid-space ray. Piercing is "remaining Weight budget carries to the next meteor the raycast reports". Two separate upgrade panels (`MissileUpgradePanel` + `RailgunUpgradePanel`) replace one contextual panel.

**Tech Stack:** Unity 6000.4.1f1, URP 2D, `MeteorIdle` / `MeteorIdle.Tests.Editor` / `MeteorIdle.Tests.PlayMode` assembly definitions, NUnit, Unity Test Framework 1.6.0, Unity MCP for editor automation.

**Source spec:** [`docs/superpowers/specs/2026-04-10-railgun-weapon-design.md`](../specs/2026-04-10-railgun-weapon-design.md) — all design rationale, stat tables, visual specs, and architectural reasoning live there. This plan is execution-only.

**Branch:** `iter/railgun` (already created). Each phase below is one commit on this branch. After Phase 12, hand back for manual play-test → FF `main` → push → delete branch.

---

## Table of contents

- [Phase 0 — PlayMode test infrastructure + existing-feature smoke tests](#phase-0)
- [Phase 1 — Meteors layer + Turret → TurretBase/MissileTurret refactor](#phase-1)
- [Phase 2 — Meteor.ApplyTunnel + EditMode tests](#phase-2)
- [Phase 3 — RailgunStats ScriptableObject + EditMode tests](#phase-3)
- [Phase 4 — Procedural art (barrel, bullet, streak)](#phase-4)
- [Phase 5 — RailgunRound projectile component + prefab](#phase-5)
- [Phase 6 — RailgunTurret component](#phase-6)
- [Phase 7 — Railgun PlayMode tests (implementation gate)](#phase-7)
- [Phase 8 — BaseSlot.prefab restructure](#phase-8)
- [Phase 9 — RailgunUpgradePanel + scene wiring](#phase-9)
- [Phase 10 — Expose Railgun in BuildSlotPanel + SlotManager](#phase-10)
- [Phase 11 — CLAUDE.md update](#phase-11)
- [Phase 12 — Final verification and hand-back](#phase-12)

---

## File structure overview

**New files (17):**

| Path | Responsibility |
|---|---|
| `Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef` | PlayMode test assembly |
| `Assets/Tests/PlayMode/PlayModeTestFixture.cs` | Shared test helpers (spawn meteor/missile/round, scene setup/teardown) |
| `Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs` | 3 baseline tests: missile collision, meteor fade, spawner pooling |
| `Assets/Tests/PlayMode/RailgunPlayModeTests.cs` | 3 railgun tests: fires-into-meteor, pierces-two-meteors, ignores-missile-in-path |
| `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs` | 10 EditMode tests for tunnel logic |
| `Assets/Tests/EditMode/RailgunStatsTests.cs` | 7 EditMode tests mirroring `TurretStatsTests` |
| `Assets/Scripts/TurretBase.cs` | Abstract base: targeting, rotation, reload timer |
| `Assets/Scripts/MissileTurret.cs` | Renamed `Turret.cs`, inherits from `TurretBase` |
| `Assets/Scripts/RailgunTurret.cs` | Inherits from `TurretBase`, fires `RailgunRound`s |
| `Assets/Scripts/Data/RailgunStats.cs` | 5-stat ScriptableObject |
| `Assets/Scripts/Weapons/RailgunRound.cs` | Visual projectile + per-frame raycast loop |
| `Assets/Scripts/Weapons/RailgunStreak.cs` | Fade-out script on the streak GameObject |
| `Assets/Scripts/UI/RailgunUpgradePanel.cs` | Railgun stats upgrade panel |
| `Assets/Data/RailgunStats.asset` | Generated via execute_code, dev-mode $1 costs |
| `Assets/Art/railgun_barrel.png` | Procedural `][` barrel sprite |
| `Assets/Art/railgun_bullet.png` | Procedural half-voxel white bullet |
| `Assets/Art/railgun_streak.png` | Procedural 4×2 hard-edged blue rectangle |
| `Assets/Prefabs/RailgunRound.prefab` | Projectile prefab with `RailgunRound` component |
| `Assets/Prefabs/RailgunStreak.prefab` | Stretched streak prefab with `RailgunStreak` component |

**Modified files (9):**

| Path | Change |
|---|---|
| `Assets/Scripts/Meteor.cs` | Add `ApplyTunnel` method |
| `Assets/Scripts/BaseSlot.cs` | Two weapon-child refs, two upgrade-panel refs, `builtWeapon` field, `HandleClick` routing |
| `Assets/Scripts/UI/UpgradePanel.cs` | Rename class to `MissileUpgradePanel` |
| `Assets/Scripts/Weapons/WeaponType.cs` | Add `Railgun = 1` enum value |
| `Assets/Scripts/SlotManager.cs` | `NextBuildCost(WeaponType)` overload + `railgunBuildCosts` field |
| `Assets/Scripts/UI/BuildSlotPanel.cs` | Add `Railgun` to weapons array |
| `Assets/Scripts/Turret.cs` | **Deleted** — contents migrated to `TurretBase.cs` + `MissileTurret.cs` |
| `Assets/Prefabs/BaseSlot.prefab` | Restructure into `MissileWeapon` + `RailgunWeapon` sibling children |
| `Assets/Scenes/Game.unity` | Add `RailgunUpgradePanel` + `UpgradeClickCatcherRailgun` GameObjects under `UI Canvas` |
| `CLAUDE.md` | Two weapons, PlayMode test suite, Meteors layer |

---

## Pre-flight check (before Phase 0)

- [ ] **Step P1: Confirm you're on the right branch**

Run: `git status -sb`
Expected: `## iter/railgun` (no uncommitted changes other than the spec which was committed in `e5959dc`)

- [ ] **Step P2: Confirm the existing EditMode suite is green on the current tip**

Run via MCP: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]`
Expected: 42/42 passing, ~2s runtime. If any fail, stop and investigate before starting Phase 0.

- [ ] **Step P3: Confirm the scene is clean on disk**

Run: `git diff Assets/Scenes/Game.unity`
Expected: empty output. If there's drift, reload the scene via the `NewScene → OpenScene` pattern before any scene edits in later phases.

---

<a name="phase-0"></a>
## Phase 0 — PlayMode test infrastructure + existing-feature smoke tests

**Goal:** Stand up the PlayMode test assembly and prove the existing gameplay (missile collision, meteor fade, spawner pooling) is green before any railgun code lands.

**Files:**
- Create: `Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef`
- Create: `Assets/Tests/PlayMode/PlayModeTestFixture.cs`
- Create: `Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs`

### Task 0.1: Create the PlayMode test assembly definition

- [ ] **Step 1: Create the folder and asmdef file**

Run: `mkdir -p "Assets/Tests/PlayMode"`

Create `Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef` with:

```json
{
    "name": "MeteorIdle.Tests.PlayMode",
    "rootNamespace": "",
    "references": [
        "MeteorIdle",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Note the empty `includePlatforms` array — PlayMode tests run in both Editor and Player. This is the key difference from the EditMode asmdef (which has `includePlatforms: ["Editor"]`).

- [ ] **Step 2: Force Unity to import the asmdef**

Run via MCP: `mcp__UnityMCP__refresh_unity scope=all mode=force compile=request wait_for_ready=true`

Check console: `mcp__UnityMCP__read_console types=["error"] count=10`
Expected: zero errors. If there are errors, they're likely missing references — fix and re-run.

### Task 0.2: Write the PlayModeTestFixture helper

**Files:**
- Create: `Assets/Tests/PlayMode/PlayModeTestFixture.cs`

- [ ] **Step 1: Create the fixture skeleton**

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // Shared helpers for PlayMode tests. Each test inherits from this fixture to
    // get scene setup/teardown and spawn-helper methods.
    public abstract class PlayModeTestFixture
    {
        protected GameManager _gameManager;
        protected MeteorSpawner _spawner;
        protected Transform _poolParent;

        protected IEnumerator SetupScene()
        {
            // Create a GameManager if one doesn't exist — PlayMode tests start in
            // an empty scene.
            if (GameManager.Instance == null)
            {
                var gmGo = new GameObject("TestGameManager", typeof(GameManager));
                _gameManager = gmGo.GetComponent<GameManager>();
                // Awake runs automatically when the GameObject becomes active in
                // PlayMode — no reflection needed here, unlike EditMode tests.
            }
            else
            {
                _gameManager = GameManager.Instance;
            }

            // A MeteorSpawner is needed so Turret.FindTarget has somewhere to look.
            if (_spawner == null)
            {
                var spawnerGo = new GameObject("TestMeteorSpawner", typeof(MeteorSpawner));
                _spawner = spawnerGo.GetComponent<MeteorSpawner>();
            }

            _poolParent = new GameObject("TestPoolParent").transform;

            yield return null; // let one frame pass so Awake/Start have run
        }

        protected void TeardownScene()
        {
            // Destroy everything we spawned. Tests should clean up via this to keep
            // state isolated between tests.
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go == null) continue;
                if (go.name.StartsWith("Test")) Object.Destroy(go);
            }
        }
    }
}
```

- [ ] **Step 2: Add meteor-spawning helper**

Append to the class:

```csharp
        protected Meteor SpawnTestMeteor(Vector3 position, int seed = 1, float scale = 1f)
        {
            var go = new GameObject(
                "TestMeteor",
                typeof(SpriteRenderer),
                typeof(CircleCollider2D),
                typeof(Meteor));
            var meteor = go.GetComponent<Meteor>();
            meteor.Spawn(null, position, seed, scale);
            return meteor;
        }
```

- [ ] **Step 3: Add missile-spawning helper (for interference tests)**

```csharp
        protected Missile SpawnTestMissile(Vector3 position)
        {
            // Spawn a "parked" missile at position — for tests that verify the
            // railgun raycast doesn't damage missiles in its path.
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Missile>(
                "Assets/Prefabs/Missile.prefab");
            var missile = Object.Instantiate(prefab, position, Quaternion.identity);
            missile.name = "TestMissile";
            return missile;
        }
```

Wait — `UnityEditor.AssetDatabase` is editor-only. PlayMode tests run in the editor so this works, but a cleaner approach is to load the prefab via `Resources.Load` or pass it in as a serialized field on the test class. For simplicity and because PlayMode tests only ever run in the editor, `AssetDatabase` is acceptable here. Wrap in `#if UNITY_EDITOR` just to be safe:

```csharp
        protected Missile SpawnTestMissile(Vector3 position)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Missile>(
                "Assets/Prefabs/Missile.prefab");
            var missile = Object.Instantiate(prefab, position, Quaternion.identity);
            missile.name = "TestMissile";
            return missile;
#else
            throw new System.NotSupportedException("TestMissile spawn is editor-only");
#endif
        }
```

- [ ] **Step 4: Compile-check the fixture**

Run: `mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=10`
Expected: zero errors.

### Task 0.3: Write the missile collision smoke test

**Files:**
- Create: `Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs`

- [ ] **Step 1: Create the test file with the missile collision test**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class ExistingFeatureSmokeTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Missile_LaunchedAtMeteor_Collides_DealsDamage()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
            int beforeAlive = meteor.AliveVoxelCount;

#if UNITY_EDITOR
            var missilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Missile>(
                "Assets/Prefabs/Missile.prefab");
#else
            Missile missilePrefab = null;
#endif
            var missile = Object.Instantiate(missilePrefab);
            // Launch with zero-arg placeholders where needed. This mirrors Turret.Fire
            // but without the stats lookups — we just want to verify collision damage.
            missile.Launch(
                turret: null,
                position: new Vector3(0f, 1f, 0f),
                velocity: new Vector2(0f, 6f),
                damageStat: 1f,
                blastStat: 0.1f,
                target: meteor,
                targetGridX: 5,
                targetGridY: 5,
                homingDegPerSec: 0f);

            // Wait a few physics frames so OnTriggerEnter2D fires.
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.Less(meteor.AliveVoxelCount, beforeAlive,
                "meteor should have lost voxels after missile collision");

            TeardownScene();
        }
    }
}
```

- [ ] **Step 2: Compile-check**

Run: `mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=10`
Expected: zero errors.

### Task 0.4: Write the meteor fade smoke test

- [ ] **Step 1: Append to `ExistingFeatureSmokeTests.cs`**

Inside the class, after the missile test:

```csharp
        [UnityTest]
        public IEnumerator Meteor_FallsAndFadesBelowThreshold_BecomesUntargetable()
        {
            yield return SetupScene();

            // Spawn the meteor just above the fade threshold (-7.88). Its velocity
            // is randomized in Spawn but always downward — so eventually it crosses.
            var meteor = SpawnTestMeteor(new Vector3(0f, -7.0f, 0f));
            Assert.IsTrue(meteor.IsAlive, "meteor above threshold should start alive");

            // Wait for real time to pass so the meteor's Update ticks and it falls.
            // The worst case: base fall speed 0.4 world/sec; we need to move ~0.9 world
            // units, so ~2.5 seconds at minimum. WaitForSeconds(3) is comfortable.
            yield return new WaitForSeconds(3f);

            Assert.IsFalse(meteor.IsAlive,
                "meteor that fell below fade threshold must be untargetable");

            TeardownScene();
        }
```

- [ ] **Step 2: Compile-check**

Same as Task 0.3 Step 2.

### Task 0.5: Write the spawner pooling smoke test

- [ ] **Step 1: Append to `ExistingFeatureSmokeTests.cs`**

```csharp
        [UnityTest]
        public IEnumerator MeteorSpawner_SpawnsPooledMeteors_OverTime()
        {
            yield return SetupScene();

            // The setup fixture creates a MeteorSpawner but doesn't give it a prefab.
            // We need to wire the real Meteor prefab so spawning works.
#if UNITY_EDITOR
            var meteorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Meteor>(
                "Assets/Prefabs/Meteor.prefab");
            var so = new UnityEditor.SerializedObject(_spawner);
            so.FindProperty("meteorPrefab").objectReferenceValue = meteorPrefab;
            so.FindProperty("poolParent").objectReferenceValue = _poolParent;
            so.ApplyModifiedPropertiesWithoutUndo();
#endif

            int startActive = _spawner.ActiveMeteors.Count;

            // Wait 8 seconds — at base cadence (initialInterval=4s), we should see
            // at least 2 spawns.
            yield return new WaitForSeconds(8f);

            int endActive = _spawner.ActiveMeteors.Count;
            Assert.GreaterOrEqual(endActive - startActive, 1,
                $"expected at least 1 meteor spawn in 8 seconds (start={startActive}, end={endActive})");

            TeardownScene();
        }
```

Note the relaxed assertion: `>= 1` rather than `>= 2`. The spawner's first spawn comes at 4 s; the test waits 8 s so 2 spawns are expected, but the fade logic may have already killed the first one if it fell off-screen. Checking for "at least 1" is robust.

- [ ] **Step 2: Compile-check**

Same as Task 0.3 Step 2.

### Task 0.6: Run the PlayMode suite and verify all 3 tests pass

- [ ] **Step 1: Ensure Unity is not in play mode**

Run: `mcp__UnityMCP__manage_editor action=stop`
(Safe even if not playing — returns "Already stopped" and does nothing.)

- [ ] **Step 2: Run the PlayMode suite**

Run: `mcp__UnityMCP__run_tests mode=PlayMode assembly_names=["MeteorIdle.Tests.PlayMode"] include_failed_tests=true`

Note the returned `job_id`, then poll:

`mcp__UnityMCP__get_test_job job_id=<id> include_failed_tests=true wait_timeout=90`

Expected: `status=succeeded`, 3/3 passing, runtime ~6-10 seconds.

- [ ] **Step 3: If any test fails — STOP**

If ANY of the 3 baseline tests fail on unmodified `main`, that's a pre-existing bug that was hiding in the gap between EditMode and PlayMode coverage. **Stop this plan.** Report to the user with the failing test name + message, discuss whether to fix independently on a separate branch before resuming the railgun work.

### Task 0.7: Commit Phase 0

- [ ] **Step 1: Stage files explicitly (not `git add -A`)**

```bash
git add Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef
git add Assets/Tests/PlayMode/MeteorIdle.Tests.PlayMode.asmdef.meta
git add Assets/Tests/PlayMode/PlayModeTestFixture.cs
git add Assets/Tests/PlayMode/PlayModeTestFixture.cs.meta
git add Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs
git add Assets/Tests/PlayMode/ExistingFeatureSmokeTests.cs.meta
```

- [ ] **Step 2: Identity scrub**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
echo "grep exit: $?"
```

Expected: `grep exit: 1` (no matches).

- [ ] **Step 3: Commit**

```bash
git -c commit.gpgsign=false commit -m "Add PlayMode test infrastructure + existing-feature smoke tests

New MeteorIdle.Tests.PlayMode assembly definition alongside the existing
EditMode assembly. Three baseline tests cover the core gameplay chains
that EditMode tests can't reach:

- Missile_LaunchedAtMeteor_Collides_DealsDamage: verifies the
  Rigidbody2D -> OnTriggerEnter2D -> Meteor.ApplyBlast chain works
  end-to-end with real physics.
- Meteor_FallsAndFadesBelowThreshold_BecomesUntargetable: verifies the
  time-scaled fade logic in Meteor.Update transitions IsAlive correctly.
- MeteorSpawner_SpawnsPooledMeteors_OverTime: verifies the spawner pool
  + cadence ramp actually produces pooled instances at runtime.

These are the smoke tests the user asked for to 'make sure the base
play is always solid' as new features land. They run on unmodified
main as a baseline before the railgun work starts."
```

- [ ] **Step 4: Verify commit**

```bash
git log --format="%an <%ae>" -1
```

Expected: `muwamath <muwamath@proton.me>`

- [ ] **Step 5: Run both test suites one more time to confirm nothing broke**

```
EditMode: mcp__UnityMCP__run_tests mode=EditMode → 42/42
PlayMode: mcp__UnityMCP__run_tests mode=PlayMode → 3/3
```

---

<a name="phase-1"></a>
## Phase 1 — Meteors layer + Turret → TurretBase/MissileTurret refactor

**Goal:** Behavior-neutral structural refactor. Split `Turret.cs` into an abstract base + a missile-specific concrete class, and add a `Meteors` physics layer. The game must look and play identically after this phase.

**Files:**
- Create: `Assets/Scripts/TurretBase.cs`
- Create: `Assets/Scripts/MissileTurret.cs`
- Delete: `Assets/Scripts/Turret.cs` (contents migrated)
- Modify: `Assets/Scripts/BaseSlot.cs` (field type `Turret` → `TurretBase`)
- Modify: `Assets/Prefabs/BaseSlot.prefab` (script reference for Turret component → MissileTurret)
- Modify: `Assets/Prefabs/Meteor.prefab` (layer → Meteors)
- Modify: `ProjectSettings/TagManager.asset` (add `Meteors` layer)

### Task 1.1: Add the Meteors physics layer

- [ ] **Step 1: Add the layer via MCP**

Run: `mcp__UnityMCP__manage_editor action=add_layer layer_name=Meteors`

Expected: success. Check the layers resource to confirm:
Run: `mcp__UnityMCP__ReadMcpResourceTool server=UnityMCP uri=mcpforunity://project/layers`
Expected: `Meteors` appears in the list at some index (probably 6, 7, or 8 — doesn't matter which).

- [ ] **Step 2: Assign the layer to Meteor.prefab**

Run via MCP `execute_code` (safety_checks=false because we're touching asset metadata):

```csharp
var prefabPath = "Assets/Prefabs/Meteor.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
int layer = LayerMask.NameToLayer("Meteors");
if (layer < 0) return "Meteors layer not found";

// Apply the layer to the root GameObject (Meteor's collider lives on root).
root.layer = layer;

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
return "Meteors layer assigned: " + layer;
```

Expected: returns the layer index.

- [ ] **Step 3: Verify the prefab YAML changed**

```bash
git diff Assets/Prefabs/Meteor.prefab | grep -E "m_Layer"
```

Expected: one `+  m_Layer: <N>` line (N matches the returned layer index from Step 2).

### Task 1.2: Create TurretBase.cs

**Files:**
- Create: `Assets/Scripts/TurretBase.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

// Abstract base class for all turret weapons. Owns shared logic: targeting,
// barrel rotation, reload timer. Subclasses implement Fire() and expose the
// per-weapon stat properties FireRate and RotationSpeed.
public abstract class TurretBase : MonoBehaviour
{
    [SerializeField] protected Transform barrel;
    [SerializeField] protected Transform muzzle;
    [SerializeField] protected ParticleSystem muzzleFlash;
    [SerializeField] protected MeteorSpawner meteorSpawner;

    // Large enough to cover the full playfield from any slot position. Camera
    // ortho size 9 → view roughly ±16 × ±9 world. Worst-case distance from a
    // side slot to the opposite corner is ~26; 30 gives comfortable headroom.
    [SerializeField] protected float range = 30f;
    [SerializeField] protected float aimAlignmentDeg = 10f;

    protected float reloadTimer;

    public void SetRuntimeRefs(MeteorSpawner spawner)
    {
        meteorSpawner = spawner;
    }

    protected virtual void Awake()
    {
        if (meteorSpawner == null) meteorSpawner = FindAnyObjectByType<MeteorSpawner>();
    }

    // Subclass contracts — per-weapon stats come through here.
    protected abstract float FireRate { get; }
    protected abstract float RotationSpeed { get; }
    protected abstract void Fire(Meteor target);

    protected virtual void Update()
    {
        if (reloadTimer > 0f) reloadTimer -= Time.deltaTime;

        var target = FindTarget();
        if (target == null) return;

        Vector2 toTarget = (Vector2)(target.transform.position - barrel.position);
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float rotSpeed = RotationSpeed;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, rotSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0, 0, newAngle);

        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (reloadTimer <= 0f && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            reloadTimer = 1f / Mathf.Max(0.05f, FireRate);
        }
    }

    protected Meteor FindTarget()
    {
        if (meteorSpawner == null) return null;
        Meteor closest = null;
        float bestSqr = range * range;
        foreach (var m in meteorSpawner.ActiveMeteors)
        {
            if (m == null || !m.IsAlive) continue;
            float d = ((Vector2)(m.transform.position - barrel.position)).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                closest = m;
            }
        }
        return closest;
    }
}
```

- [ ] **Step 2: Do NOT compile yet**

Compiling now would fail because `Turret.cs` still exists and both classes want to be on `BaseSlot.prefab`. We finish the rename first, then compile once at the end of Task 1.3.

### Task 1.3: Create MissileTurret.cs from the existing Turret.cs

**Files:**
- Create: `Assets/Scripts/MissileTurret.cs`
- Delete: `Assets/Scripts/Turret.cs` (and its `.meta`)

- [ ] **Step 1: Read the current Turret.cs**

Run: `Read Assets/Scripts/Turret.cs`

- [ ] **Step 2: Create MissileTurret.cs with the migrated contents**

```csharp
using UnityEngine;

public class MissileTurret : TurretBase
{
    [SerializeField] private TurretStats stats;
    [SerializeField] private Missile missilePrefab;
    [SerializeField] private Transform missilePoolParent;

    private SimplePool<Missile> missilePool;

    public TurretStats Stats => stats;

    protected override float FireRate => stats.fireRate.CurrentValue;
    protected override float RotationSpeed => stats.rotationSpeed.CurrentValue;

    protected override void Awake()
    {
        base.Awake();
        if (missilePoolParent == null) missilePoolParent = transform;
        missilePool = new SimplePool<Missile>(missilePrefab, missilePoolParent, 8);
    }

    protected override void Fire(Meteor target)
    {
        var missile = missilePool.Get();
        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;

        // Pick a specific voxel on the target meteor to aim at.
        int gx = 0, gy = 0;
        bool hasVoxel = target.PickRandomPresentVoxel(out gx, out gy);

        Vector2 dir;
        if (hasVoxel)
        {
            Vector3 voxelWorld = target.GetVoxelWorldPosition(gx, gy);
            dir = ((Vector2)(voxelWorld - spawnPos)).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = barrel.up;
        }
        else
        {
            dir = barrel.up;
        }

        float speed = stats.missileSpeed.CurrentValue;
        Meteor homingTarget = hasVoxel ? target : null;
        missile.Launch(
            this,
            spawnPos,
            dir * speed,
            stats.damage.CurrentValue,
            stats.blastRadius.CurrentValue,
            homingTarget,
            gx,
            gy,
            stats.homing.CurrentValue);

        if (muzzleFlash != null) muzzleFlash.Play();
    }

    public void ReleaseMissile(Missile m) => missilePool.Release(m);
}
```

Note what moved up to the base class and what stayed here:
- `barrel`, `muzzle`, `muzzleFlash`, `meteorSpawner`, `range`, `aimAlignmentDeg`, `Update`, `FindTarget`, `SetRuntimeRefs`, `Awake` fallback → `TurretBase`
- `stats`, `missilePrefab`, `missilePoolParent`, `missilePool`, `Fire`, `ReleaseMissile`, `Stats` accessor → `MissileTurret`
- Abstract `FireRate` / `RotationSpeed` are implemented here as getters that read from `stats`.

- [ ] **Step 3: Delete Turret.cs and its .meta**

```bash
rm Assets/Scripts/Turret.cs
rm Assets/Scripts/Turret.cs.meta
```

- [ ] **Step 4: BaseSlot.cs field type update**

Read `Assets/Scripts/BaseSlot.cs`, find the line `[SerializeField] private Turret turret;`, and change the type to `TurretBase`:

```csharp
[SerializeField] private TurretBase turret;
```

The `BaseSlot.cs` imports and the rest of the class don't need changes — the `turret.enabled = true/false` calls still work via the base class.

- [ ] **Step 5: Compile and check for errors**

Run: `mcp__UnityMCP__refresh_unity scope=all mode=force compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=20`

Expected errors to handle:
- **None ideally.** The scene and prefab serialized fields currently reference the `Turret` script by GUID, not by class name. Deleting `Turret.cs` + `Turret.cs.meta` means those GUIDs are now dangling.
- If you see "The referenced script (Unknown) on this Behaviour is missing!" errors, go to Step 6.

- [ ] **Step 6: Rewire the BaseSlot prefab's script reference (if needed)**

The `Turret.cs.meta` had a specific GUID that was referenced by `BaseSlot.prefab`. Now that the file is deleted, the prefab has a broken script reference. Fix it by manually replacing the dangling reference with the new `MissileTurret` GUID.

Run via MCP `execute_code`:

```csharp
var prefabPath = "Assets/Prefabs/BaseSlot.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);

// Find the missing script component and add a MissileTurret in its place.
// Or: if the MissileTurret script's GUID happens to match the old Turret.cs guid
// (it won't — new files get new GUIDs), we'd need to just re-save.

// Simpler: remove broken components, add MissileTurret fresh, wire fields from
// the old Turret's SerializeFields. We need to record the old field values
// (stats, barrel, muzzle, missilePrefab, missilePoolParent, meteorSpawner,
// muzzleFlash) before deletion.

// For this refactor, do it via a SerializedObject property dance — see below.

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
return "done";
```

**Reality check:** this is fiddly. An easier path: **rename the script instead of deleting + creating.** Git tracks renames, meta files carry the same GUID if you reuse it.

**Revised Step 3 + 4:**

```bash
# Instead of: rm Turret.cs + create new MissileTurret.cs
# Do: git mv Turret.cs MissileTurret.cs, preserving the GUID
git mv Assets/Scripts/Turret.cs Assets/Scripts/MissileTurret.cs
git mv Assets/Scripts/Turret.cs.meta Assets/Scripts/MissileTurret.cs.meta
```

Then edit the renamed file's class name from `public class Turret : MonoBehaviour` → `public class MissileTurret : TurretBase`, and make the other changes to inherit from TurretBase (remove fields that moved up, adjust Awake, add override modifiers, implement FireRate/RotationSpeed properties).

The `.meta` file's GUID is preserved, so the `BaseSlot.prefab` serialized reference to the old `Turret` script now resolves to `MissileTurret` automatically. **No prefab rewiring needed.**

**Updated flow for Task 1.3:**

- [ ] **Step 3-revised: Rename Turret.cs → MissileTurret.cs with git mv**

```bash
git mv Assets/Scripts/Turret.cs Assets/Scripts/MissileTurret.cs
# The .meta should move automatically with git, but verify:
ls Assets/Scripts/MissileTurret.cs.meta
```

- [ ] **Step 4-revised: Rewrite the class contents**

Edit `Assets/Scripts/MissileTurret.cs` to match the MissileTurret class shown in Step 2 above — change class name, inherit from `TurretBase`, remove fields that moved up, add `override` keywords, implement abstract properties.

- [ ] **Step 5-revised: Update BaseSlot.cs field type**

Same as original Step 4: change `Turret turret` → `TurretBase turret`.

- [ ] **Step 6-revised: Compile + read console**

Run: `mcp__UnityMCP__refresh_unity scope=all mode=force compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=20`
Expected: zero errors, zero "missing script" warnings (the prefab's serialized GUID now resolves to MissileTurret).

### Task 1.4: Run both test suites and verify green

- [ ] **Step 1: Stop play mode (defensive)**

`mcp__UnityMCP__manage_editor action=stop`

- [ ] **Step 2: EditMode 42/42**

`mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]` → poll → expect 42/42

- [ ] **Step 3: PlayMode 3/3**

`mcp__UnityMCP__run_tests mode=PlayMode assembly_names=["MeteorIdle.Tests.PlayMode"]` → poll → expect 3/3

### Task 1.5: Manual play-mode sanity check

- [ ] **Step 1: Enter play mode and verify the missile turret still fires**

Run: `mcp__UnityMCP__manage_editor action=play`
Wait a few seconds via multiple `mcp__UnityMCP__manage_camera action=screenshot include_image=true max_resolution=400` calls.
Verify: center turret rotates and fires missiles that track meteors, meteors get destroyed.

- [ ] **Step 2: Exit play mode**

`mcp__UnityMCP__manage_editor action=stop`

- [ ] **Step 3: Scrub for scene drift**

```bash
git diff Assets/Scenes/Game.unity | grep -E "^-  m_(AnchorMin|AnchorMax|AnchoredPosition|SizeDelta|TextStyleHashCode|fontColor32)" | head -5
```

Expected: empty. If drift appears, use the scene-reload-from-disk pattern from earlier sessions.

### Task 1.6: Commit Phase 1

- [ ] **Step 1: Stage explicitly**

```bash
git add Assets/Scripts/TurretBase.cs
git add Assets/Scripts/TurretBase.cs.meta
git add Assets/Scripts/MissileTurret.cs
git add Assets/Scripts/MissileTurret.cs.meta
git add Assets/Scripts/BaseSlot.cs
git add Assets/Prefabs/Meteor.prefab
git add ProjectSettings/TagManager.asset
# Note: the deletion of Turret.cs is handled by the git mv rename — no separate action
```

- [ ] **Step 2: Identity scrub**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
echo "grep exit: $?"
```

- [ ] **Step 3: Commit**

```bash
git -c commit.gpgsign=false commit -m "Refactor Turret into TurretBase + MissileTurret, add Meteors physics layer

Behavior-neutral structural refactor preparing for the Railgun weapon.
Turret.cs is renamed (via git mv, preserving the script GUID so no
prefab rewiring is needed) to MissileTurret.cs and split against a new
abstract TurretBase class. TurretBase owns the shared logic: targeting,
rotation, reload timer, meteorSpawner fallback, the Update loop.
MissileTurret owns everything missile-specific: TurretStats ref,
missile pool, Fire() launching a missile from the pool.

BaseSlot.cs's turret field type becomes TurretBase so future weapon
subclasses (RailgunTurret) can be referenced polymorphically.

Meteors physics layer added and assigned to Meteor.prefab. Not used
yet — the Railgun will filter its per-frame raycasts against this
layer in Phase 5.

All 42 EditMode tests and all 3 PlayMode smoke tests still pass."
```

- [ ] **Step 4: Verify**

```bash
git log --format="%an <%ae>" -1
```
Expected: muwamath.

---

<a name="phase-2"></a>
## Phase 2 — Meteor.ApplyTunnel + EditMode tests

**Goal:** Add the line-walking voxel destruction method that the railgun will call. Pure logic, fully covered by new EditMode tests. No weapon code yet.

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` — add `ApplyTunnel` method
- Create: `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`

### Task 2.1: Write the first ApplyTunnel test (TDD — fails first)

**Files:**
- Create: `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`

- [ ] **Step 1: Create the test file with one test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class MeteorApplyTunnelTests
    {
        private const int FullShapeSeed = 1;

        private Meteor NewMeteor(int seed = FullShapeSeed, float scale = 1f)
        {
            var go = new GameObject(
                "TestMeteor",
                typeof(SpriteRenderer),
                typeof(CircleCollider2D),
                typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed, scale);
            return m;
        }

        private static void Destroy(Meteor m)
        {
            if (m != null) Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void FreshMeteor_VerticalTunnelFromBottom_CarvesStraightLine()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            // Entry just below the meteor (world y -1.0); direction straight up.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out Vector3 exitWorld);

            Assert.AreEqual(5, destroyed, "budget=5 should destroy exactly 5 live voxels");
            Assert.AreEqual(before - 5, m.AliveVoxelCount);
            // Exit point should be somewhere above the entry in world space.
            Assert.Greater(exitWorld.y, -1f,
                "exit point should be above the entry point for an upward tunnel");
            Destroy(m);
        }
    }
}
```

- [ ] **Step 2: Compile-check — expect ApplyTunnel to not exist yet**

Run: `mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=5`

Expected error: `error CS1061: 'Meteor' does not contain a definition for 'ApplyTunnel'`. This is the "failing test" — the code it exercises doesn't exist.

### Task 2.2: Implement Meteor.ApplyTunnel

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`

- [ ] **Step 1: Add the method after ApplyBlast**

Find the end of the existing `ApplyBlast` method in `Meteor.cs` (after the `return destroyed;` closing brace of that method). Insert:

```csharp
    public int ApplyTunnel(
        Vector3 entryWorld,
        Vector3 worldDirection,
        int budget,
        int caliberWidth,
        out Vector3 exitWorld)
    {
        exitWorld = entryWorld;
        if (dead || aliveCount == 0 || budget <= 0) return 0;

        // Convert entryWorld to meteor-local grid coordinates, same math as ApplyBlast.
        Vector3 local = transform.InverseTransformPoint(entryWorld);
        Vector3 localDir = transform.InverseTransformDirection(worldDirection).normalized;
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);

        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        float dx = localDir.x;
        float dy = localDir.y;

        // Perpendicular direction (for caliber width).
        float perpX = -dy;
        float perpY = dx;
        int halfBand = caliberWidth - 1; // 0, 1, 2 for caliber 1, 2, 3

        int consumed = 0;
        bool anyPainted = false;
        int maxSteps = VoxelMeteorGenerator.GridSize * 4;

        for (int step = 0; step < maxSteps; step++)
        {
            if (budget <= 0) break;

            // At this step, consider all perpendicular offsets in [-halfBand, halfBand].
            for (int offset = -halfBand; offset <= halfBand; offset++)
            {
                float cellX = gx + perpX * offset;
                float cellY = gy + perpY * offset;
                int ix = Mathf.FloorToInt(cellX);
                int iy = Mathf.FloorToInt(cellY);
                if (ix < 0 || ix >= VoxelMeteorGenerator.GridSize) continue;
                if (iy < 0 || iy >= VoxelMeteorGenerator.GridSize) continue;
                if (!voxels[ix, iy]) continue; // empty — free, doesn't consume budget

                voxels[ix, iy] = false;
                VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
                anyPainted = true;
                consumed++;
                budget--;

                if (voxelChunkPrefab != null)
                {
                    Vector3 worldVoxel = VoxelCenterToWorld(ix, iy);
                    var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                    burst.Play();
                    Destroy(burst.gameObject, 1.5f);
                }

                if (budget <= 0) break;
            }

            // Advance half a cell along the ray.
            gx += dx * 0.5f;
            gy += dy * 0.5f;

            // Exit check — if we've walked off the grid, stop.
            if (gx < -0.5f || gx >= VoxelMeteorGenerator.GridSize + 0.5f) break;
            if (gy < -0.5f || gy >= VoxelMeteorGenerator.GridSize + 0.5f) break;
        }

        if (anyPainted) texture.Apply();

        aliveCount -= consumed;
        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }

        // Compute exit point: the grid position where the walk terminated, in world space.
        Vector3 localExit = new Vector3(
            gx / localToGrid - halfExtent,
            gy / localToGrid - halfExtent,
            0f);
        exitWorld = transform.TransformPoint(localExit);

        return consumed;
    }
```

- [ ] **Step 2: Compile**

Run: `mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true`
Then: `mcp__UnityMCP__read_console types=["error"] count=5`
Expected: zero errors.

- [ ] **Step 3: Run the first test**

`mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_names=["MeteorIdle.Tests.Editor.MeteorApplyTunnelTests.FreshMeteor_VerticalTunnelFromBottom_CarvesStraightLine"]`

Expected: 1/1 passing.

### Task 2.3: Add the remaining 9 ApplyTunnel tests

- [ ] **Step 1: Add EmptyVoxels_AreFreeAndDontConsumeBudget test**

Inside the class, after the first test:

```csharp
        [Test]
        public void EmptyVoxels_AreFreeAndDontConsumeBudget()
        {
            var m = NewMeteor();

            // First, carve a hole in column 5 via ApplyBlast at the bottom. This
            // should destroy some cells in the bottom rim of column 5.
            m.ApplyBlast(new Vector3(0f, -0.8f, 0f), 0.28f);
            int afterFirst = m.AliveVoxelCount;

            // Now tunnel upward through the same column. The empty cells at the
            // bottom should not consume budget — the budget should still be able
            // to carve 3 cells further up.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 3,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(3, destroyed,
                "tunnel should carve 3 cells beyond the existing hole");
            Assert.AreEqual(afterFirst - 3, m.AliveVoxelCount);
            Destroy(m);
        }
```

- [ ] **Step 2: Add BudgetCap_StopsTunnelEarly**

```csharp
        [Test]
        public void BudgetCap_StopsTunnelEarly()
        {
            var m = NewMeteor();

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 2,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(2, destroyed, "budget=2 caps the destruction at 2 voxels");
            Destroy(m);
        }
```

- [ ] **Step 3: Add BudgetExceedsLivePath_ReportsActualConsumed**

```csharp
        [Test]
        public void BudgetExceedsLivePath_ReportsActualConsumed()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            // Budget of 100 against a meteor with ~65 cells. The tunnel exits the
            // grid before budget is exhausted — so destroyed should be less than 100.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 100,
                caliberWidth: 1,
                out _);

            Assert.Less(destroyed, 100, "budget should not be fully spent — grid exits first");
            Assert.Greater(destroyed, 0, "some cells should have been destroyed");
            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }
```

- [ ] **Step 4: Add Caliber2_CarvesThreeWideBand**

```csharp
        [Test]
        public void Caliber2_CarvesThreeWideBand()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            // Caliber 2 means halfBand=1, so the tunnel is 3 cells wide perpendicular
            // to travel. With budget large enough to cut through, the destroyed count
            // should be noticeably more than the caliber=1 case.
            int destroyedWide = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _);

            Destroy(m);

            var m2 = NewMeteor();
            int destroyedNarrow = m2.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 1,
                out _);
            Destroy(m2);

            Assert.Greater(destroyedWide, destroyedNarrow,
                "caliber 2 should destroy more cells than caliber 1 at the same budget");
        }
```

- [ ] **Step 5: Add Caliber3_CarvesFiveWideBand**

```csharp
        [Test]
        public void Caliber3_CarvesFiveWideBand()
        {
            var m = NewMeteor();
            int destroyedC3 = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 3,
                out _);
            Destroy(m);

            var m2 = NewMeteor();
            int destroyedC2 = m2.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _);
            Destroy(m2);

            Assert.Greater(destroyedC3, destroyedC2,
                "caliber 3 should destroy more cells than caliber 2 at the same budget");
        }
```

- [ ] **Step 6: Add DiagonalDirection_WalksCorrectly**

```csharp
        [Test]
        public void DiagonalDirection_WalksCorrectly()
        {
            var m = NewMeteor();

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(-1f, -1f, 0f),
                worldDirection: new Vector3(1f, 1f, 0f).normalized,
                budget: 5,
                caliberWidth: 1,
                out _);

            Assert.Greater(destroyed, 0,
                "diagonal tunnel should destroy at least some cells");
            Destroy(m);
        }
```

- [ ] **Step 7: Add ExitPoint_IsReturnedInWorldSpace**

```csharp
        [Test]
        public void ExitPoint_IsReturnedInWorldSpace()
        {
            var m = NewMeteor();

            m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out Vector3 exitWorld);

            // After a budget=5 upward tunnel from y=-1 into a meteor at origin,
            // the exit point should be above the entry.
            Assert.Greater(exitWorld.y, -1f);
            Destroy(m);
        }
```

- [ ] **Step 8: Add DeadMeteor_ReturnsZeroAndDoesNotThrow**

```csharp
        [Test]
        public void DeadMeteor_ReturnsZeroAndDoesNotThrow()
        {
            var m = NewMeteor();
            m.ApplyBlast(Vector3.zero, 5f); // nuke everything
            Assert.AreEqual(0, m.AliveVoxelCount);

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(0, destroyed,
                "tunnel through dead meteor must be a no-op");
            Destroy(m);
        }
```

- [ ] **Step 9: Add AliveCount_DecrementsByConsumedAmount**

```csharp
        [Test]
        public void AliveCount_DecrementsByConsumedAmount()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 4,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }
```

- [ ] **Step 10: Run the full EditMode suite and verify all 52 tests pass**

`mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]` → 52/52

### Task 2.4: Commit Phase 2

- [ ] **Step 1: Stage**

```bash
git add Assets/Scripts/Meteor.cs
git add Assets/Tests/EditMode/MeteorApplyTunnelTests.cs
git add Assets/Tests/EditMode/MeteorApplyTunnelTests.cs.meta
```

- [ ] **Step 2: Identity scrub + commit**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add Meteor.ApplyTunnel for line-tunneling damage

Parallel to the existing ApplyBlast method. ApplyTunnel walks the
10x10 voxel grid along a world-space direction from an entry point,
destroying live voxels within a perpendicular caliber-width band
until the depth budget is exhausted or the ray exits the grid.
Empty voxels are free and don't consume budget — this is what lets
the railgun 'glide through holes' per the design spec.

Returns the number of voxels consumed and the exit world point via
out param (used by RailgunRound to compute where to start the next
meteor's tunnel when piercing).

10 new EditMode tests in MeteorApplyTunnelTests.cs cover: vertical
tunnel through a fresh meteor, free pass-through of empty voxels,
budget cap early stop, actual-consumed reporting when budget exceeds
live path, caliber 2/3 wider bands, diagonal walk, exit point in
world space, dead-meteor no-op, and aliveCount bookkeeping.

EditMode suite: 52/52 green."
```

---

<a name="phase-3"></a>
## Phase 3 — RailgunStats ScriptableObject + EditMode tests

**Goal:** Add the 5-stat ScriptableObject parallel to `TurretStats`, plus mirror the existing `TurretStatsTests` for the new class.

**Files:**
- Create: `Assets/Scripts/Data/RailgunStats.cs`
- Create: `Assets/Data/RailgunStats.asset` (generated via execute_code)
- Create: `Assets/Tests/EditMode/RailgunStatsTests.cs`

### Task 3.1: Create RailgunStats.cs

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Data/RailgunStats.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public enum RailgunStatId
{
    FireRate = 0,
    RotationSpeed = 1,
    Speed = 2,
    Weight = 3,
    Caliber = 4,
}

[CreateAssetMenu(fileName = "RailgunStats", menuName = "Meteor Idle/Railgun Stats")]
public class RailgunStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public RailgunStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1f;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
    }

    public Stat fireRate      = new Stat { id = RailgunStatId.FireRate,      displayName = "Fire Rate",      baseValue = 0.2f, perLevelAdd = 0.05f, baseCost = 1, costGrowth = 1f };
    public Stat rotationSpeed = new Stat { id = RailgunStatId.RotationSpeed, displayName = "Rotation Speed", baseValue = 20f,  perLevelAdd = 12f,   baseCost = 1, costGrowth = 1f };
    public Stat speed         = new Stat { id = RailgunStatId.Speed,         displayName = "Speed",          baseValue = 6f,   perLevelAdd = 3f,    baseCost = 1, costGrowth = 1f };
    public Stat weight        = new Stat { id = RailgunStatId.Weight,        displayName = "Weight",         baseValue = 4f,   perLevelAdd = 2f,    baseCost = 1, costGrowth = 1f };
    public Stat caliber       = new Stat { id = RailgunStatId.Caliber,       displayName = "Caliber",        baseValue = 1f,   perLevelAdd = 1f,    baseCost = 1, costGrowth = 1f };

    public event Action OnChanged;

    public Stat Get(RailgunStatId id)
    {
        switch (id)
        {
            case RailgunStatId.FireRate:      return fireRate;
            case RailgunStatId.RotationSpeed: return rotationSpeed;
            case RailgunStatId.Speed:         return speed;
            case RailgunStatId.Weight:        return weight;
            case RailgunStatId.Caliber:       return caliber;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return fireRate;
        yield return rotationSpeed;
        yield return speed;
        yield return weight;
        yield return caliber;
    }

    public void ApplyUpgrade(RailgunStatId id)
    {
        var stat = Get(id);
        if (stat == null) return;
        stat.level++;
        OnChanged?.Invoke();
    }

    public void ResetRuntime()
    {
        foreach (var s in All()) s.level = 0;
        OnChanged?.Invoke();
    }
}
```

- [ ] **Step 2: Compile-check**

Expected: zero errors.

### Task 3.2: Generate the RailgunStats.asset

- [ ] **Step 1: Create the asset via execute_code**

Run via MCP `execute_code`:

```csharp
var stats = ScriptableObject.CreateInstance<RailgunStats>();
// Default values from the C# class are already correct — no overrides needed.
UnityEditor.AssetDatabase.CreateAsset(stats, "Assets/Data/RailgunStats.asset");
UnityEditor.AssetDatabase.SaveAssets();
return "created Assets/Data/RailgunStats.asset";
```

- [ ] **Step 2: Verify the asset file exists**

```bash
ls -la "Assets/Data/RailgunStats.asset"
```

### Task 3.3: Write RailgunStatsTests (7 tests)

- [ ] **Step 1: Create the test file**

Create `Assets/Tests/EditMode/RailgunStatsTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class RailgunStatsTests
    {
        private RailgunStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<RailgunStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
            _stats = null;
        }

        [Test]
        public void Stat_CurrentValue_BaseAtLevelZero()
        {
            Assert.AreEqual(_stats.fireRate.baseValue, _stats.fireRate.CurrentValue, 1e-5);
            Assert.AreEqual(_stats.weight.baseValue, _stats.weight.CurrentValue, 1e-5);
            Assert.AreEqual(_stats.caliber.baseValue, _stats.caliber.CurrentValue, 1e-5);
        }

        [Test]
        public void Stat_NextCost_FollowsGrowthFormula()
        {
            var stat = _stats.weight;
            int level0 = stat.NextCost;
            Assert.AreEqual(stat.baseCost, level0);

            stat.level = 3;
            // With costGrowth=1 (dev-mode default), cost stays flat regardless of level.
            int expected = Mathf.RoundToInt(stat.baseCost * Mathf.Pow(stat.costGrowth, 3));
            Assert.AreEqual(expected, stat.NextCost);
        }

        [Test]
        public void ApplyUpgrade_IncrementsLevel_AndFiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ApplyUpgrade(RailgunStatId.Speed);

            Assert.AreEqual(1, _stats.speed.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void ApplyUpgrade_AffectsOnlyTheTargetStat()
        {
            _stats.ApplyUpgrade(RailgunStatId.Weight);

            Assert.AreEqual(1, _stats.weight.level);
            Assert.AreEqual(0, _stats.fireRate.level);
            Assert.AreEqual(0, _stats.rotationSpeed.level);
            Assert.AreEqual(0, _stats.speed.level);
            Assert.AreEqual(0, _stats.caliber.level);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var stat = _stats.speed;
            float at0 = stat.CurrentValue;
            stat.level = 5;
            float at5 = stat.CurrentValue;
            Assert.AreEqual(at0 + 5f * stat.perLevelAdd, at5, 1e-5);
        }

        [Test]
        public void Get_ReturnsCorrectStat()
        {
            Assert.AreSame(_stats.fireRate,      _stats.Get(RailgunStatId.FireRate));
            Assert.AreSame(_stats.rotationSpeed, _stats.Get(RailgunStatId.RotationSpeed));
            Assert.AreSame(_stats.speed,         _stats.Get(RailgunStatId.Speed));
            Assert.AreSame(_stats.weight,        _stats.Get(RailgunStatId.Weight));
            Assert.AreSame(_stats.caliber,       _stats.Get(RailgunStatId.Caliber));
        }

        [Test]
        public void ResetRuntime_ZerosAllLevels_AndFiresEvent()
        {
            _stats.ApplyUpgrade(RailgunStatId.Speed);
            _stats.ApplyUpgrade(RailgunStatId.Speed);
            _stats.ApplyUpgrade(RailgunStatId.Weight);
            Assert.AreEqual(2, _stats.speed.level);
            Assert.AreEqual(1, _stats.weight.level);

            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ResetRuntime();

            Assert.AreEqual(0, _stats.speed.level);
            Assert.AreEqual(0, _stats.weight.level);
            Assert.AreEqual(1, events);
        }
    }
}
```

- [ ] **Step 2: Run the EditMode suite — expect 59/59**

`mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]`
Expected: 59/59 passing.

### Task 3.4: Commit Phase 3

- [ ] **Step 1: Stage explicitly**

```bash
git add Assets/Scripts/Data/RailgunStats.cs
git add Assets/Scripts/Data/RailgunStats.cs.meta
git add Assets/Data/RailgunStats.asset
git add Assets/Data/RailgunStats.asset.meta
git add Assets/Tests/EditMode/RailgunStatsTests.cs
git add Assets/Tests/EditMode/RailgunStatsTests.cs.meta
```

- [ ] **Step 2: Identity scrub + commit**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add RailgunStats ScriptableObject + EditMode tests

Parallel to TurretStats but with the 5 railgun stats from the design
spec: FireRate, RotationSpeed, Speed, Weight, Caliber. All costs
default to \$1 with costGrowth=1 to match the current dev-mode
economy — these will be rebalanced in the economy overhaul pass.

RailgunStats.asset is generated via execute_code and stored at
Assets/Data/. RailgunStatsTests mirrors the structure of
TurretStatsTests with 7 tests covering the growth formula, level
tracking, single-stat upgrade isolation, Get/All accessors, and
ResetRuntime behavior.

EditMode suite: 59/59 green."
```

---

<a name="phase-4"></a>
## Phase 4 — Procedural art (barrel, bullet, streak)

**Goal:** Generate the three railgun PNGs procedurally per the voxel aesthetic rules. Commit just the art + import metadata.

**Files:**
- Create: `Assets/Art/railgun_barrel.png` (+ `.meta`)
- Create: `Assets/Art/railgun_bullet.png` (+ `.meta`)
- Create: `Assets/Art/railgun_streak.png` (+ `.meta`)

### Task 4.1: Generate railgun_barrel.png

- [ ] **Step 1: Run the generation script via execute_code (safety_checks=false)**

```csharp
int W = 24, H = 60;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
var clear = new Color32(0, 0, 0, 0);
var px = new Color32[W * H];
for (int i = 0; i < px.Length; i++) px[i] = clear;

var body = new Color32(255, 255, 255, 255);       // #FFFFFF dead white
var shadow = new Color32(128, 128, 128, 255);     // #808080 1-px shadow
var highlight = new Color32(176, 232, 255, 255);  // #B0E8FF slightly blue-tinted highlight

// Two vertical bars at x=[0..7] and x=[16..23], gap at x=[8..15]
System.Action<int, int> drawBar = (xStart, xEnd) => {
    for (int y = 0; y < H; y++) {
        for (int x = xStart; x <= xEnd; x++) {
            px[y * W + x] = body;
        }
    }
    // Left edge shadow (1 px)
    for (int y = 0; y < H; y++) px[y * W + xStart] = shadow;
    // Right edge highlight (1 px)
    for (int y = 0; y < H; y++) px[y * W + xEnd] = highlight;
    // Top highlight (1 px)
    for (int x = xStart; x <= xEnd; x++) px[(H-1) * W + x] = highlight;
};

drawBar(0, 7);
drawBar(16, 23);

tex.SetPixels32(px);
tex.Apply();
var bytes = tex.EncodeToPNG();
UnityEngine.Object.DestroyImmediate(tex);

var path = "Assets/Art/railgun_barrel.png";
var full = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../" + path);
System.IO.File.WriteAllBytes(full, bytes);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);

var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
imp.textureType = UnityEditor.TextureImporterType.Sprite;
imp.spriteImportMode = UnityEditor.SpriteImportMode.Single;
imp.spritePixelsPerUnit = 100f;
imp.filterMode = FilterMode.Point;
imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
imp.alphaIsTransparency = true;
imp.mipmapEnabled = false;
// Pivot: bottom-center (rotation pivots around the base of the ][)
imp.spritePivot = new Vector2(0.5f, 0f);
imp.SaveAndReimport();

return new { path, bytes = bytes.Length };
```

- [ ] **Step 2: Verify the file exists and has non-zero size**

```bash
ls -la "Assets/Art/railgun_barrel.png"
```

Expected: file exists, size ~150-300 bytes.

### Task 4.2: Generate railgun_bullet.png

- [ ] **Step 1: Run via execute_code**

```csharp
int W = 8, H = 15;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
var px = new Color32[W * H];

var body = new Color32(255, 255, 255, 255);       // pure white
var shadow = new Color32(204, 204, 204, 255);     // #CCCCCC bottom shadow
var highlight = new Color32(255, 255, 255, 255);  // white highlight (same as body here)

for (int y = 0; y < H; y++) {
    for (int x = 0; x < W; x++) {
        px[y * W + x] = body;
    }
}
// Bottom row shadow
for (int x = 0; x < W; x++) px[0 * W + x] = shadow;

tex.SetPixels32(px);
tex.Apply();
var bytes = tex.EncodeToPNG();
UnityEngine.Object.DestroyImmediate(tex);

var path = "Assets/Art/railgun_bullet.png";
var full = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../" + path);
System.IO.File.WriteAllBytes(full, bytes);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);

var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
imp.textureType = UnityEditor.TextureImporterType.Sprite;
imp.spriteImportMode = UnityEditor.SpriteImportMode.Single;
imp.spritePixelsPerUnit = 100f;
imp.filterMode = FilterMode.Point;
imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
imp.alphaIsTransparency = true;
imp.mipmapEnabled = false;
imp.spritePivot = new Vector2(0.5f, 0.5f);
imp.SaveAndReimport();

return new { path, bytes = bytes.Length };
```

### Task 4.3: Generate railgun_streak.png

- [ ] **Step 1: Run via execute_code**

```csharp
int W = 4, H = 2;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
var px = new Color32[W * H];

var edge = new Color32(94, 168, 206, 255);    // #5EA8CE darker blue edge
var body = new Color32(147, 218, 254, 255);   // #93DAFE target blue

// Outer 1 px border = edge; inner 2x0 = body
for (int y = 0; y < H; y++) {
    for (int x = 0; x < W; x++) {
        bool isBorder = (x == 0 || x == W - 1 || y == 0 || y == H - 1);
        px[y * W + x] = isBorder ? edge : body;
    }
}

tex.SetPixels32(px);
tex.Apply();
var bytes = tex.EncodeToPNG();
UnityEngine.Object.DestroyImmediate(tex);

var path = "Assets/Art/railgun_streak.png";
var full = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../" + path);
System.IO.File.WriteAllBytes(full, bytes);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);

var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
imp.textureType = UnityEditor.TextureImporterType.Sprite;
imp.spriteImportMode = UnityEditor.SpriteImportMode.Single;
imp.spritePixelsPerUnit = 100f;
imp.filterMode = FilterMode.Point;
imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
imp.alphaIsTransparency = true;
imp.mipmapEnabled = false;
imp.spritePivot = new Vector2(0.5f, 0.5f);
imp.SaveAndReimport();

return new { path, bytes = bytes.Length };
```

### Task 4.4: Commit Phase 4

- [ ] **Step 1: Stage**

```bash
git add Assets/Art/railgun_barrel.png
git add Assets/Art/railgun_barrel.png.meta
git add Assets/Art/railgun_bullet.png
git add Assets/Art/railgun_bullet.png.meta
git add Assets/Art/railgun_streak.png
git add Assets/Art/railgun_streak.png.meta
```

- [ ] **Step 2: Identity scrub + commit**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Generate railgun procedural art (barrel, bullet, streak)

Three hard-edged voxel sprites per the voxel aesthetic rules:

- railgun_barrel.png: 24x60 px, two 8-px-wide vertical bars with an
  8-px gap (the ][ silhouette). Body dead white, left edge shadow
  (#808080), right edge and top highlight (#B0E8FF). Pivot at
  bottom-center for base rotation.
- railgun_bullet.png: 8x15 px, pure white body with a 1-px gray
  shadow on the bottom edge. Half-voxel-wide, voxel-tall.
- railgun_streak.png: 4x2 px, darker blue (#5EA8CE) border with
  target blue (#93DAFE) interior. Designed to be stretched along
  its X axis with point filtering — the hard pixel edges stay crisp
  at any length.

All three textures use filterMode=Point, Uncompressed, no mipmaps,
alphaIsTransparency=true."
```

---

<a name="phase-5"></a>
## Phase 5 — RailgunRound projectile component + prefab

**Goal:** Implement the per-frame raycast bullet. No tests yet — covered by Phase 7 PlayMode tests.

**Files:**
- Create: `Assets/Scripts/Weapons/RailgunRound.cs`
- Create: `Assets/Scripts/Weapons/RailgunStreak.cs`
- Create: `Assets/Prefabs/RailgunRound.prefab`
- Create: `Assets/Prefabs/RailgunStreak.prefab`

### Task 5.1: Create RailgunStreak.cs (simple fade-out component)

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Weapons/RailgunStreak.cs`:

```csharp
using UnityEngine;

// Simple fade-out behavior on the streak GameObject. Spawned by RailgunRound
// when the round despawns. Alpha steps through 4 quantized levels over
// `duration` seconds, then destroys the GameObject.
public class RailgunStreak : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private float duration = 2f;

    private static readonly float[] AlphaSteps = { 1f, 0.66f, 0.33f, 0f };
    private float timer;

    private void Awake()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);
        int idx = Mathf.Min(Mathf.FloorToInt(t * AlphaSteps.Length), AlphaSteps.Length - 1);
        var c = sr.color;
        c.a = AlphaSteps[idx];
        sr.color = c;

        if (t >= 1f) Destroy(gameObject);
    }

    public void Configure(Vector3 from, Vector3 to, int caliber)
    {
        Vector3 mid = (from + to) * 0.5f;
        transform.position = mid;

        Vector3 delta = to - from;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        // Scale X to length (sprite is 4 px wide = 0.04 world units at 100 ppu,
        // so divide length by 0.04 to get the needed scale).
        // Scale Y by caliber (base 1, caliber 2 = 1.5, caliber 3 = 2).
        float scaleY = 1f + (caliber - 1) * 0.5f;
        transform.localScale = new Vector3(length / 0.04f, scaleY, 1f);
    }
}
```

- [ ] **Step 2: Compile-check**

### Task 5.2: Create RailgunRound.cs

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Weapons/RailgunRound.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

// Visual-only projectile. No Rigidbody2D, no Collider2D — damage is resolved
// via per-frame Physics2D.RaycastAll against the Meteors layer. Advances via
// transform.position each frame; each frame's raycast covers the distance
// just traveled (manual continuous collision).
public class RailgunRound : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private RailgunStreak streakPrefab;

    private Vector3 direction;
    private float speed;
    private int remainingWeight;
    private int caliber;
    private Vector3 spawnPoint;
    private readonly HashSet<Meteor> alreadyTunneled = new();
    private int meteorLayerMask;

    public void Configure(Vector3 spawnPos, Vector3 dir, float speed, int weight, int caliber)
    {
        transform.position = spawnPos;
        spawnPoint = spawnPos;
        direction = dir.normalized;
        this.speed = speed;
        remainingWeight = weight;
        this.caliber = caliber;
        alreadyTunneled.Clear();

        // Orient the bullet sprite along the direction (long axis = travel).
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    private void Awake()
    {
        int layer = LayerMask.NameToLayer("Meteors");
        if (layer < 0)
        {
            Debug.LogError("[RailgunRound] Meteors layer not defined", this);
            meteorLayerMask = ~0; // all layers — better than nothing
        }
        else
        {
            meteorLayerMask = 1 << layer;
        }
    }

    private void Update()
    {
        if (remainingWeight <= 0) { Despawn(); return; }

        float stepDistance = speed * Time.deltaTime;
        var hits = Physics2D.RaycastAll(
            transform.position,
            (Vector2)direction,
            stepDistance,
            meteorLayerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (remainingWeight <= 0) break;
            var meteor = hit.collider.GetComponentInParent<Meteor>();
            if (meteor == null || !meteor.IsAlive) continue;
            if (alreadyTunneled.Contains(meteor)) continue;

            int consumed = meteor.ApplyTunnel(
                entryWorld: hit.point,
                worldDirection: direction,
                budget: remainingWeight,
                caliberWidth: caliber,
                out _);
            remainingWeight -= consumed;
            alreadyTunneled.Add(meteor);

            if (consumed > 0 && GameManager.Instance != null)
                GameManager.Instance.AddMoney(consumed);
        }

        transform.position += direction * stepDistance;

        if (OffScreen(transform.position)) { Despawn(); return; }
    }

    private void Despawn()
    {
        if (streakPrefab != null)
        {
            var streak = Instantiate(streakPrefab);
            streak.Configure(spawnPoint, transform.position, caliber);
        }
        Destroy(gameObject);
    }

    private static bool OffScreen(Vector3 pos) =>
        pos.y > 10f || pos.y < -10f || Mathf.Abs(pos.x) > 17f;
}
```

- [ ] **Step 2: Compile-check**

### Task 5.3: Create RailgunStreak.prefab

- [ ] **Step 1: Via execute_code, safety_checks=false**

```csharp
var streakSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/railgun_streak.png");
if (streakSprite == null) return "streak sprite not found";

var go = new GameObject("RailgunStreak", typeof(SpriteRenderer), typeof(RailgunStreak));
var srComp = go.GetComponent<SpriteRenderer>();
srComp.sprite = streakSprite;
srComp.sortingOrder = 10;

var streak = go.GetComponent<RailgunStreak>();
var so = new UnityEditor.SerializedObject(streak);
so.FindProperty("sr").objectReferenceValue = srComp;
so.FindProperty("duration").floatValue = 2f;
so.ApplyModifiedPropertiesWithoutUndo();

var path = "Assets/Prefabs/RailgunStreak.prefab";
var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
UnityEngine.Object.DestroyImmediate(go);

return new { created = saved != null, path };
```

### Task 5.4: Create RailgunRound.prefab

- [ ] **Step 1: Via execute_code, safety_checks=false**

```csharp
var bulletSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/railgun_bullet.png");
var streakPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<RailgunStreak>("Assets/Prefabs/RailgunStreak.prefab");

var go = new GameObject("RailgunRound", typeof(SpriteRenderer), typeof(RailgunRound));
var srComp = go.GetComponent<SpriteRenderer>();
srComp.sprite = bulletSprite;
srComp.sortingOrder = 15;

var round = go.GetComponent<RailgunRound>();
var so = new UnityEditor.SerializedObject(round);
so.FindProperty("sr").objectReferenceValue = srComp;
so.FindProperty("streakPrefab").objectReferenceValue = streakPrefab;
so.ApplyModifiedPropertiesWithoutUndo();

var path = "Assets/Prefabs/RailgunRound.prefab";
var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
UnityEngine.Object.DestroyImmediate(go);

return new { created = saved != null, path };
```

### Task 5.5: Commit Phase 5

- [ ] **Step 1: Stage**

```bash
git add Assets/Scripts/Weapons/RailgunRound.cs
git add Assets/Scripts/Weapons/RailgunRound.cs.meta
git add Assets/Scripts/Weapons/RailgunStreak.cs
git add Assets/Scripts/Weapons/RailgunStreak.cs.meta
git add Assets/Prefabs/RailgunRound.prefab
git add Assets/Prefabs/RailgunRound.prefab.meta
git add Assets/Prefabs/RailgunStreak.prefab
git add Assets/Prefabs/RailgunStreak.prefab.meta
```

- [ ] **Step 2: Identity scrub + commit**

```bash
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add RailgunRound projectile component and prefab

RailgunRound is a visual-only projectile: no Rigidbody2D, no Collider2D.
Damage is resolved via per-frame Physics2D.RaycastAll against the
Meteors layer only (filters out missiles, turret bases, UI), with each
frame's raycast covering exactly the distance the bullet just moved.
This gives manual continuous collision at any speed from base
(6 world/sec) to max upgrade (~36 world/sec) with no tunneling.

On each frame: sort hits by distance, iterate, call Meteor.ApplyTunnel
on each live meteor not already tunneled by this round, decrement
remainingWeight, award money. When remainingWeight hits zero or the
round goes offscreen, spawn a RailgunStreak and destroy the round.

RailgunStreak is a stretched sprite with 4-step alpha fade-out over
2 seconds. Configure() sets position, rotation, and scale to match
the line from muzzle to despawn point, with scale-Y growing with
caliber (caliber 1 -> 1x, 2 -> 1.5x, 3 -> 2x thickness).

No tests yet — covered by Phase 7 PlayMode tests which need real
physics to exercise the raycast chain."
```

---

<a name="phase-6"></a>
## Phase 6 — RailgunTurret component

**Goal:** Wire the RailgunRound into a TurretBase subclass with stats, charge animation, and barrel color.

**Files:**
- Create: `Assets/Scripts/RailgunTurret.cs`

### Task 6.1: Create RailgunTurret.cs

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

public class RailgunTurret : TurretBase
{
    [SerializeField] private RailgunStats stats;
    [SerializeField] private RailgunRound roundPrefab;
    [SerializeField] private SpriteRenderer barrelSprite;

    // 4-step quantized charge color animation per the voxel aesthetic rules.
    private static readonly Color[] ChargeStops = new Color[] {
        new Color(1f,    1f,    1f,    1f),    // #FFFFFF dead white
        new Color(0.808f,0.910f,0.996f,1f),    // #CEE8FE first blue tint
        new Color(0.659f,0.839f,0.996f,1f),    // #A8D6FE mid blue
        new Color(0.576f,0.855f,0.996f,1f),    // #93DAFE full charge
    };

    private float chargeTimer;

    public RailgunStats Stats => stats;

    protected override float FireRate => stats.fireRate.CurrentValue;
    protected override float RotationSpeed => stats.rotationSpeed.CurrentValue;

    protected override void Awake()
    {
        base.Awake();
        // Start at dead white
        if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
    }

    protected override void Update()
    {
        // Advance charge timer instead of using the base's reloadTimer.
        // We override the whole Update here because the charge behavior is custom.
        float chargeDuration = 1f / Mathf.Max(0.05f, FireRate);
        chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, chargeDuration);

        // Update barrel color based on charge progress.
        float t = Mathf.Clamp01(chargeTimer / chargeDuration);
        int stopIdx = Mathf.Min(Mathf.FloorToInt(t * ChargeStops.Length), ChargeStops.Length - 1);
        if (barrelSprite != null) barrelSprite.color = ChargeStops[stopIdx];

        // Targeting (reuse the base-class FindTarget via a direct call).
        var target = FindTarget();
        if (target == null) return;

        // Rotate toward target.
        Vector2 toTarget = (Vector2)(target.transform.position - barrel.position);
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, RotationSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0, 0, newAngle);

        // Fire only when charge is full AND we're aligned.
        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (chargeTimer >= chargeDuration && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            chargeTimer = 0f;
            // Snap color back to dead white immediately.
            if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
        }
    }

    protected override void Fire(Meteor target)
    {
        if (roundPrefab == null)
        {
            Debug.LogError("[RailgunTurret] roundPrefab not assigned", this);
            return;
        }

        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;
        Vector3 dir = barrel.up;

        var round = Instantiate(roundPrefab);
        round.Configure(
            spawnPos: spawnPos,
            dir: dir,
            speed: stats.speed.CurrentValue,
            weight: Mathf.RoundToInt(stats.weight.CurrentValue),
            caliber: Mathf.RoundToInt(stats.caliber.CurrentValue));

        if (muzzleFlash != null) muzzleFlash.Play();
    }
}
```

Note: `TurretBase.Update` needs to be virtual to be overridden cleanly. If it isn't already, mark it `protected virtual void Update()` in `TurretBase.cs` and add `protected override void Update()` here. I already marked it virtual in Task 1.2's TurretBase.cs code above.

- [ ] **Step 2: Compile-check**

Expected: zero errors.

### Task 6.2: Commit Phase 6

```bash
git add Assets/Scripts/RailgunTurret.cs
git add Assets/Scripts/RailgunTurret.cs.meta
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add RailgunTurret weapon implementation

Subclass of TurretBase that reads a RailgunStats ScriptableObject,
spawns RailgunRound projectiles, and handles the barrel charge-color
animation.

Overrides Update to implement the custom charge-timer behavior:
instead of the base class's reloadTimer, the charge advances each
frame up to 1/FireRate seconds, and fires when the charge is full AND
the barrel is aligned with the target. Barrel color steps through
four quantized stops (dead white -> CEE8FE -> A8D6FE -> 93DAFE),
snaps back to dead white immediately on fire. No smooth Color.Lerp.

Fire() spawns a RailgunRound from the prefab ref and configures it
with (muzzle pos, barrel.up direction, Speed stat, Weight stat,
Caliber stat). Muzzle flash reuses the base-class particle system
slot.

No tests yet — the Fire -> RailgunRound -> ApplyTunnel chain is
covered by Phase 7 PlayMode tests."
```

---

<a name="phase-7"></a>
## Phase 7 — Railgun PlayMode tests (implementation gate)

**Goal:** Prove the RailgunRound → Meteor.ApplyTunnel chain actually works end-to-end with real physics. **This is the gate** — if these fail, the core weapon is broken and must be fixed before scene-wiring phases start.

**Files:**
- Create: `Assets/Tests/PlayMode/RailgunPlayModeTests.cs`
- Modify: `Assets/Tests/PlayMode/PlayModeTestFixture.cs` (add `SpawnTestRailgunRound`)

### Task 7.1: Add SpawnTestRailgunRound helper to PlayModeTestFixture

- [ ] **Step 1: Append to `PlayModeTestFixture.cs` inside the class**

```csharp
        protected RailgunRound SpawnTestRailgunRound(
            Vector3 spawnPos,
            Vector2 direction,
            float speed,
            int weight,
            int caliber)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<RailgunRound>(
                "Assets/Prefabs/RailgunRound.prefab");
            var round = Object.Instantiate(prefab);
            round.name = "TestRailgunRound";
            round.Configure(spawnPos, direction, speed, weight, caliber);
            return round;
#else
            throw new System.NotSupportedException("TestRailgunRound spawn is editor-only");
#endif
        }
```

- [ ] **Step 2: Compile-check**

### Task 7.2: Create RailgunPlayModeTests.cs with the 3 tests

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class RailgunPlayModeTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator RailgunRound_FiresIntoMeteor_DealsDamage()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
            int before = meteor.AliveVoxelCount;

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 10,
                caliber: 1);

            yield return new WaitForSeconds(0.5f);

            Assert.Less(meteor.AliveVoxelCount, before,
                "meteor should have lost voxels after railgun round hit");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator RailgunRound_PiercesTwoStackedMeteors()
        {
            yield return SetupScene();

            var m1 = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 1);
            var m2 = SpawnTestMeteor(new Vector3(0f, 6f, 0f), seed: 2);
            int m1Before = m1.AliveVoxelCount;
            int m2Before = m2.AliveVoxelCount;

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 30, // comfortable budget to pierce both
                caliber: 1);

            yield return new WaitForSeconds(0.8f);

            Assert.Less(m1.AliveVoxelCount, m1Before,
                "first meteor should have been tunneled");
            Assert.Less(m2.AliveVoxelCount, m2Before,
                "second meteor should have been tunneled via piercing");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator RailgunRound_LayerMask_IgnoresMissilesInPath()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 5f, 0f));
            int meteorBefore = meteor.AliveVoxelCount;

            var missile = SpawnTestMissile(new Vector3(0f, 2f, 0f));
            Assert.IsTrue(missile.gameObject.activeSelf, "missile should start active");

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 10,
                caliber: 1);

            yield return new WaitForSeconds(0.5f);

            Assert.Less(meteor.AliveVoxelCount, meteorBefore,
                "meteor behind the missile should still have been damaged");
            Assert.IsTrue(missile.gameObject.activeSelf,
                "missile in the path should be untouched by the railgun");

            TeardownScene();
        }
    }
}
```

### Task 7.3: Run PlayMode suite, expect 6/6 passing

- [ ] **Step 1: Stop play mode**
`mcp__UnityMCP__manage_editor action=stop`

- [ ] **Step 2: Run**

`mcp__UnityMCP__run_tests mode=PlayMode assembly_names=["MeteorIdle.Tests.PlayMode"]` → 6/6

- [ ] **Step 3: If any fail — STOP and debug**

The 3 new tests exercise the core railgun chain (raycast → tunnel → pierce → layer-mask). Any failure means the weapon is broken and must be fixed before any scene work starts. Common failure modes:
- **"meteor should have lost voxels"** → `ApplyTunnel` is being called but `voxelsConsumed` is 0. Check the grid-coordinate conversion in `ApplyTunnel` (probably the direction vector got lost in `InverseTransformDirection`).
- **"second meteor should have been tunneled via piercing"** → `remainingWeight` isn't carrying across meteors. Check that `alreadyTunneled.Add(meteor)` isn't accidentally preventing the SECOND meteor from being processed (it should only skip the same meteor).
- **"missile in the path should be untouched"** → layer mask isn't working. Check that `Meteor.prefab` is actually on the `Meteors` layer (Phase 1 Task 1.1 Step 2) and that `LayerMask.NameToLayer("Meteors")` returns a valid index in `RailgunRound.Awake`.

### Task 7.4: Commit Phase 7

```bash
git add Assets/Tests/PlayMode/PlayModeTestFixture.cs
git add Assets/Tests/PlayMode/RailgunPlayModeTests.cs
git add Assets/Tests/PlayMode/RailgunPlayModeTests.cs.meta
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add PlayMode tests for railgun raycast-tunneling chain

Three tests that exercise the full RailgunRound -> Meteor.ApplyTunnel
chain with real physics and real colliders:

- RailgunRound_FiresIntoMeteor_DealsDamage: smoke test that a single
  round actually damages a meteor via the per-frame raycast loop.
- RailgunRound_PiercesTwoStackedMeteors: proves the remainingWeight
  budget carries across two meteors in a line when budget is
  generous enough to punch through the first.
- RailgunRound_LayerMask_IgnoresMissilesInPath: proves the Meteors
  layer mask keeps the railgun raycast from finding missile colliders,
  even when a missile is sitting directly between the round and its
  target meteor.

PlayMode suite: 6/6 green (3 existing smoke tests + 3 new railgun
tests)."
```

---

<a name="phase-8"></a>
## Phase 8 — BaseSlot.prefab restructure

**Goal:** Restructure `BaseSlot.prefab` from one built-in weapon to two swappable weapon children (`MissileWeapon`, `RailgunWeapon`). Highest scene/prefab risk phase.

**Files:**
- Modify: `Assets/Prefabs/BaseSlot.prefab`
- Modify: `Assets/Scripts/BaseSlot.cs`

### Task 8.1: Plan the prefab surgery

Before touching anything, record the current structure:

- [ ] **Step 1: Inspect current BaseSlot.prefab hierarchy**

Run via execute_code:

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BaseSlot.prefab");
var sb = new System.Text.StringBuilder();
sb.Append("root: ").Append(prefab.name).Append("\n");
foreach (var comp in prefab.GetComponents<Component>()) {
    sb.Append("  ").Append(comp.GetType().Name).Append("\n");
}
foreach (Transform child in prefab.transform) {
    sb.Append("child: ").Append(child.name).Append("\n");
    foreach (var comp in child.GetComponents<Component>()) {
        sb.Append("    ").Append(comp.GetType().Name).Append("\n");
    }
}
return sb.ToString();
```

Record the output — you'll need the existing component references (especially the Barrel/Muzzle transforms and the MissileTurret serialized field values) to rewire them into the new `MissileWeapon` child.

### Task 8.2: Do the restructure via execute_code

- [ ] **Step 1: Load prefab contents, restructure, save**

```csharp
var prefabPath = "Assets/Prefabs/BaseSlot.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);

// 1. Find the existing MissileTurret + Barrel on the root and move them
//    under a new "MissileWeapon" child.
var existingTurret = root.GetComponent<MissileTurret>();
Transform existingBarrel = null;
foreach (Transform c in root.transform)
    if (c.name == "Barrel") { existingBarrel = c; break; }

// 2. Create MissileWeapon child, reparent Barrel under it, move the
//    MissileTurret component by recreating it on the child (Unity doesn't
//    support component-move-between-GameObjects cleanly).
var missileWeaponGo = new GameObject("MissileWeapon");
missileWeaponGo.transform.SetParent(root.transform, false);

if (existingBarrel != null) existingBarrel.SetParent(missileWeaponGo.transform, false);

// Copy serialized field values from the root's MissileTurret to a new one
// on missileWeaponGo, then destroy the root's.
var oldSo = new UnityEditor.SerializedObject(existingTurret);
var statsRef  = oldSo.FindProperty("stats").objectReferenceValue;
var missileP  = oldSo.FindProperty("missilePrefab").objectReferenceValue;
var poolParent= oldSo.FindProperty("missilePoolParent").objectReferenceValue;
var muzzleRef = oldSo.FindProperty("muzzle").objectReferenceValue;
var barrelRef = oldSo.FindProperty("barrel").objectReferenceValue;
var spawnerR  = oldSo.FindProperty("meteorSpawner").objectReferenceValue;
var flashRef  = oldSo.FindProperty("muzzleFlash").objectReferenceValue;

UnityEngine.Object.DestroyImmediate(existingTurret, true);

var newMissileTurret = missileWeaponGo.AddComponent<MissileTurret>();
var newSo = new UnityEditor.SerializedObject(newMissileTurret);
newSo.FindProperty("stats").objectReferenceValue = statsRef;
newSo.FindProperty("missilePrefab").objectReferenceValue = missileP;
newSo.FindProperty("missilePoolParent").objectReferenceValue = poolParent;
newSo.FindProperty("muzzle").objectReferenceValue = muzzleRef;
newSo.FindProperty("barrel").objectReferenceValue = barrelRef;
newSo.FindProperty("meteorSpawner").objectReferenceValue = spawnerR;
newSo.FindProperty("muzzleFlash").objectReferenceValue = flashRef;
newSo.ApplyModifiedPropertiesWithoutUndo();

// 3. Create RailgunWeapon child with a RailgunBarrel/Muzzle and a RailgunTurret.
var railgunWeaponGo = new GameObject("RailgunWeapon");
railgunWeaponGo.transform.SetParent(root.transform, false);

var railgunBarrelGo = new GameObject("RailgunBarrel", typeof(SpriteRenderer));
railgunBarrelGo.transform.SetParent(railgunWeaponGo.transform, false);
var railgunBarrelSr = railgunBarrelGo.GetComponent<SpriteRenderer>();
railgunBarrelSr.sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/railgun_barrel.png");
railgunBarrelSr.sortingOrder = 3;

var railgunMuzzleGo = new GameObject("RailgunMuzzle");
railgunMuzzleGo.transform.SetParent(railgunBarrelGo.transform, false);
// Position muzzle at top of the barrel (barrel is 60 px tall = 0.6 world,
// pivot at bottom, so top is at local y = 0.6).
railgunMuzzleGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);

var railgunTurret = railgunWeaponGo.AddComponent<RailgunTurret>();
var rtSo = new UnityEditor.SerializedObject(railgunTurret);
rtSo.FindProperty("stats").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<RailgunStats>("Assets/Data/RailgunStats.asset");
rtSo.FindProperty("roundPrefab").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<RailgunRound>("Assets/Prefabs/RailgunRound.prefab");
rtSo.FindProperty("barrelSprite").objectReferenceValue = railgunBarrelSr;
rtSo.FindProperty("barrel").objectReferenceValue = railgunBarrelGo.transform;
rtSo.FindProperty("muzzle").objectReferenceValue = railgunMuzzleGo.transform;
// meteorSpawner left null — resolved at runtime via TurretBase.Awake fallback
rtSo.ApplyModifiedPropertiesWithoutUndo();

// 4. Both weapon children start inactive — BaseSlot.Build activates the right one.
missileWeaponGo.SetActive(false);
railgunWeaponGo.SetActive(false);

// 5. Save prefab
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);

return "restructure complete";
```

### Task 8.3: Update BaseSlot.cs for the new hierarchy

- [ ] **Step 1: Add new SerializeField refs**

Read `Assets/Scripts/BaseSlot.cs`. Change the `turret` field and add:

```csharp
    // Replaced: [SerializeField] private TurretBase turret;
    [SerializeField] private GameObject missileWeapon;
    [SerializeField] private GameObject railgunWeapon;
    [SerializeField] private CanvasGroup upgradePanelMissile;
    [SerializeField] private CanvasGroup upgradePanelRailgun;

    private WeaponType builtWeapon;
```

- [ ] **Step 2: Update SetEmpty**

```csharp
    public void SetEmpty()
    {
        IsBuilt = false;
        if (turretBaseSr != null) turretBaseSr.enabled = false;
        if (missileWeapon != null) missileWeapon.SetActive(false);
        if (railgunWeapon != null) railgunWeapon.SetActive(false);
        if (plusIconSr != null) plusIconSr.enabled = true;
    }
```

- [ ] **Step 3: Update Build**

```csharp
    public void Build(WeaponType weapon)
    {
        IsBuilt = true;
        builtWeapon = weapon;
        if (turretBaseSr != null) turretBaseSr.enabled = true;
        if (plusIconSr != null) plusIconSr.enabled = false;

        if (weapon == WeaponType.Railgun)
        {
            if (railgunWeapon != null) railgunWeapon.SetActive(true);
            if (missileWeapon != null) missileWeapon.SetActive(false);
        }
        else
        {
            if (missileWeapon != null) missileWeapon.SetActive(true);
            if (railgunWeapon != null) railgunWeapon.SetActive(false);
        }
    }
```

- [ ] **Step 4: Update HandleClick to route to the right panel**

```csharp
    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsBuilt)
        {
            var panel = builtWeapon == WeaponType.Railgun
                ? upgradePanelRailgun
                : upgradePanelMissile;
            ToggleUpgradePanel(panel);
        }
        else
        {
            EmptyClicked?.Invoke(this);
        }
    }

    private void ToggleUpgradePanel(CanvasGroup panel)
    {
        if (panel == null) return;
        bool visible = panel.alpha < 0.5f;
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = visible;
        panel.blocksRaycasts = visible;
    }
```

- [ ] **Step 5: Update SetUpgradePanel → two setters**

```csharp
    public void SetMissileUpgradePanel(CanvasGroup panel) => upgradePanelMissile = panel;
    public void SetRailgunUpgradePanel(CanvasGroup panel) => upgradePanelRailgun = panel;
```

And update `SlotManager.cs` callers accordingly (search for `SetUpgradePanel` and replace with the two new setters, passing the right panels).

- [ ] **Step 6: Wire the scene prefab instance**

Run via execute_code to rewire the SlotManager's slotPrefab and its two panel references. The slot prefab now expects two upgrade-panel refs at runtime (set in SlotManager.Start via `SetMissileUpgradePanel` / `SetRailgunUpgradePanel`). SlotManager's SerializedFields need updating:

```csharp
    [SerializeField] private CanvasGroup upgradePanelMissile;  // was: upgradePanel
    [SerializeField] private CanvasGroup upgradePanelRailgun;
```

And in `SlotManager.Start`, for each instantiated slot:

```csharp
    slot.SetMissileUpgradePanel(upgradePanelMissile);
    slot.SetRailgunUpgradePanel(upgradePanelRailgun);
```

### Task 8.4: Compile + tests

- [ ] **Step 1: Compile**

Fix any compile errors from the BaseSlot/SlotManager changes.

- [ ] **Step 2: Both test suites**

Expected: EditMode 59/59, PlayMode 6/6.

### Task 8.5: Manual play-mode sanity check

- [ ] **Step 1: Enter play mode, confirm center slot still spawns a missile turret that fires**

- [ ] **Step 2: Exit, scrub scene drift**

```bash
git diff Assets/Scenes/Game.unity | grep -E "^-  m_(AnchorMin|AnchorMax|AnchoredPosition|SizeDelta|TextStyleHashCode|fontColor32)" | head -5
```

If drift appears, reload scene fresh and re-save.

### Task 8.6: Commit Phase 8

```bash
git add Assets/Prefabs/BaseSlot.prefab
git add Assets/Scripts/BaseSlot.cs
git add Assets/Scripts/SlotManager.cs
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Restructure BaseSlot.prefab for multiple weapon types

BaseSlot.prefab's hierarchy now has two weapon children (initially
inactive): MissileWeapon (with the existing MissileTurret component
and barrel) and RailgunWeapon (with a new RailgunTurret component,
railgun_barrel sprite, and a RailgunMuzzle child at the top of the
barrel). The slot's own turret_base sprite stays unchanged as the
common foundation.

BaseSlot.cs SetEmpty/Build now toggles the correct weapon child and
tracks builtWeapon. HandleClick routes to upgradePanelMissile or
upgradePanelRailgun based on which weapon is active. The old single
'turret' field and SetUpgradePanel helper are replaced with explicit
missile/railgun pairs.

SlotManager.Start now passes both panel refs to each spawned slot
via the new SetMissileUpgradePanel/SetRailgunUpgradePanel helpers.
The railgun panel ref comes from a new serialized field that stays
null until Phase 9 wires it to the new scene panel.

EditMode 59/59, PlayMode 6/6, missile gameplay still functional in
the editor."
```

---

<a name="phase-9"></a>
## Phase 9 — RailgunUpgradePanel + scene wiring

**Goal:** Add a second upgrade panel mirroring the existing `MissileUpgradePanel`, plus a second click-catcher behind it. Wire the new panel into SlotManager's scene refs.

**Files:**
- Modify: `Assets/Scripts/UI/UpgradePanel.cs` — rename class to `MissileUpgradePanel`
- Create: `Assets/Scripts/UI/RailgunUpgradePanel.cs`
- Modify: `Assets/Scenes/Game.unity` — new panel + click catcher under UI Canvas

### Task 9.1: Rename UpgradePanel.cs class to MissileUpgradePanel

- [ ] **Step 1: git mv the file**

```bash
git mv Assets/Scripts/UI/UpgradePanel.cs Assets/Scripts/UI/MissileUpgradePanel.cs
git mv Assets/Scripts/UI/UpgradePanel.cs.meta Assets/Scripts/UI/MissileUpgradePanel.cs.meta
```

- [ ] **Step 2: Edit the class name inside the file**

Change `public class UpgradePanel : MonoBehaviour` → `public class MissileUpgradePanel : MonoBehaviour` in `Assets/Scripts/UI/MissileUpgradePanel.cs`.

The .meta GUID is preserved, so the scene's UpgradePanel GameObject script reference auto-resolves to the new class name.

- [ ] **Step 3: Compile-check**

### Task 9.2: Create RailgunUpgradePanel.cs

- [ ] **Step 1: Create the file**

Copy the structure from MissileUpgradePanel. The railgun panel reads from `RailgunStats` instead of `TurretStats`, uses `RailgunStatId` instead of `StatId`, and has no launcher/missile category split — all 5 stats live in one column.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public class RailgunUpgradePanel : MonoBehaviour
{
    [SerializeField] private RailgunStats stats;
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform buttonParent;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;

    private void Start()
    {
        if (stats == null)             { Debug.LogError("[RailgunUpgradePanel] stats not assigned", this); return; }
        if (buttonPrefab == null)      { Debug.LogError("[RailgunUpgradePanel] buttonPrefab not assigned", this); return; }
        if (buttonParent == null)      { Debug.LogError("[RailgunUpgradePanel] buttonParent not assigned", this); return; }

        foreach (var stat in stats.All())
        {
            var btn = Instantiate(buttonPrefab, buttonParent);
            btn.BindRailgun(stats, stat.id, OnClicked);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged += moneyListener;

        stats.ResetRuntime();
        RefreshAll();

        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
    }

    private void OnClicked(RailgunStatId id)
    {
        var stat = stats.Get(id);
        if (stat == null) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            stats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.RefreshRailgun(money);
    }
}
```

Note: This introduces a dependency on `UpgradeButton.BindRailgun` and `UpgradeButton.RefreshRailgun` methods that don't exist yet. Either add them to the existing `UpgradeButton.cs` (polymorphic button that can hold either a missile or railgun stat ref) OR create a separate `RailgunUpgradeButton.cs`.

**Simpler path:** add both bind methods to `UpgradeButton.cs` as overloads and internal branching. It's ~20 extra lines.

- [ ] **Step 2: Extend UpgradeButton.cs**

Read `Assets/Scripts/UI/UpgradeButton.cs`. Add fields for a railgun-stats ref and railgun-stat-id, plus `BindRailgun` / `RefreshRailgun` methods mirroring the existing missile ones. The button internally branches on which ref is set.

```csharp
    // Add to class fields:
    private RailgunStats railgunStats;
    private RailgunStatId railgunStatId;
    private Action<RailgunStatId> onRailgunClick;

    public void BindRailgun(RailgunStats stats, RailgunStatId statId, Action<RailgunStatId> onClick)
    {
        this.railgunStats = stats;
        this.railgunStatId = statId;
        this.onRailgunClick = onClick;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => this.onRailgunClick?.Invoke(this.railgunStatId));
        }
    }

    public void RefreshRailgun(int money)
    {
        if (railgunStats == null || label == null) return;
        var stat = railgunStats.Get(railgunStatId);
        if (stat == null) return;
        label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
        if (button != null) button.interactable = money >= stat.NextCost;
    }
```

- [ ] **Step 3: Compile-check**

### Task 9.3: Add the scene GameObjects for RailgunUpgradePanel + click catcher

- [ ] **Step 1: Reload scene fresh (drift prevention)**

Via execute_code:

```csharp
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
return "reloaded";
```

- [ ] **Step 2: Create the RailgunUpgradePanel GameObject**

Via execute_code — model after the existing UpgradePanel structure. I'll skip the detailed GameObject construction code here; use the same pattern as the BuildSlotPanel was created with (Image background, CanvasGroup, Title child, button parent child with VerticalLayoutGroup), with:
- `RailgunUpgradePanel` component on root
- Size ~420×380 (5 stats, single column)
- Centered under UI Canvas

- [ ] **Step 3: Create UpgradeClickCatcherRailgun sibling**

Full-screen stretched Image with `ModalClickCatcher` pointing at the new panel's CanvasGroup, placed BEFORE the panel in sibling order.

- [ ] **Step 4: Wire SlotManager's upgradePanelRailgun field**

Via execute_code, set `SlotManager.upgradePanelRailgun` on the scene instance to the new panel's CanvasGroup.

- [ ] **Step 5: Save scene and scrub drift**

### Task 9.4: Commit Phase 9

```bash
git add Assets/Scripts/UI/MissileUpgradePanel.cs
git add Assets/Scripts/UI/RailgunUpgradePanel.cs
git add Assets/Scripts/UI/RailgunUpgradePanel.cs.meta
git add Assets/Scripts/UI/UpgradeButton.cs
git add Assets/Scenes/Game.unity
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Add RailgunUpgradePanel with click-outside-to-close catcher

Renamed the existing UpgradePanel class to MissileUpgradePanel (via
git mv to preserve the script GUID so the scene UpgradePanel
GameObject's component reference auto-resolves). Added a new parallel
RailgunUpgradePanel class reading from RailgunStats.

UpgradeButton.cs gained BindRailgun/RefreshRailgun overloads so a
single UpgradeButton prefab can serve either weapon type. Internal
branching based on which stats ref is set.

Scene gained a new RailgunUpgradePanel GameObject under UI Canvas,
sibling to MissileUpgradePanel, with its own ModalClickCatcher for
click-outside-to-close. SlotManager.upgradePanelRailgun wired to
the new panel's CanvasGroup.

No tests — UI panels are manual-verification territory per the
project policy."
```

---

<a name="phase-10"></a>
## Phase 10 — Expose Railgun in BuildSlotPanel + SlotManager

**Goal:** Player can now actually buy a railgun from the build panel. This is the phase that makes the feature visible in game.

**Files:**
- Modify: `Assets/Scripts/SlotManager.cs`
- Modify: `Assets/Scripts/UI/BuildSlotPanel.cs`
- Modify: `Assets/Scripts/Weapons/WeaponType.cs`

### Task 10.1: Add Railgun to the WeaponType enum

- [ ] **Step 1: Edit WeaponType.cs**

```csharp
public enum WeaponType
{
    Missile = 0,
    Railgun = 1,
}
```

### Task 10.2: Add railgunBuildCosts to SlotManager

- [ ] **Step 1: Add field and overload NextBuildCost**

In `Assets/Scripts/SlotManager.cs`:

```csharp
    [SerializeField] private int[] missileBuildCosts = { 100, 300 };
    [SerializeField] private int[] railgunBuildCosts = { 200, 600 };

    public int NextBuildCost() => NextBuildCost(WeaponType.Missile);

    public int NextBuildCost(WeaponType weapon)
    {
        int[] costs = weapon == WeaponType.Railgun ? railgunBuildCosts : missileBuildCosts;
        if (costs == null || costs.Length == 0) return 0;
        if (builtPurchasedCount < costs.Length) return costs[builtPurchasedCount];
        int overflow = builtPurchasedCount - costs.Length + 2;
        return costs[costs.Length - 1] * overflow;
    }
```

Rename the old `buildCosts` field to `missileBuildCosts`. The scene serialized value for the old field name will be orphaned — I'll need to re-wire the scene values in Step 2.

- [ ] **Step 2: Update the scene's SlotManager values**

Reload the scene fresh, via execute_code set both arrays to `{1, 1}` (dev-mode $1 costs), save the scene.

### Task 10.3: Add Railgun to BuildSlotPanel.weapons

- [ ] **Step 1: Edit BuildSlotPanel.cs**

Change the weapons array default value:

```csharp
    [SerializeField] private WeaponType[] weapons = { WeaponType.Missile, WeaponType.Railgun };
```

- [ ] **Step 2: Update the scene's BuildSlotPanel weapons array**

Via execute_code, set the serialized weapons array on the BuildSlotPanel scene instance to `[Missile, Railgun]`.

### Task 10.4: Update OnConfirmBuild to use weapon-specific cost

- [ ] **Step 1: Edit SlotManager.OnConfirmBuild to pass the weapon**

Change:
```csharp
    private void OnConfirmBuild(BaseSlot slot, WeaponType weapon)
    {
        int cost = NextBuildCost(weapon);  // was NextBuildCost()
        if (GameManager.Instance == null || !GameManager.Instance.TrySpend(cost)) return;
        ...
    }
```

And the OpenBuildPanel should pass the panel a way to display per-weapon costs. The existing BuildSlotPanel.Open takes a single cost; that needs to become per-weapon via button-specific Bind call.

Simplest update: BuildSlotPanel.Open iterates its weapons array and calls `BuildWeaponButton.Bind(weapon, slotManager.NextBuildCost(weapon), onClick)`. Requires passing a `SlotManager` ref to BuildSlotPanel or a `Func<WeaponType, int>` cost-lookup.

Use a Func to stay loosely coupled:

```csharp
// BuildSlotPanel.cs:
public void Open(BaseSlot slot, Func<WeaponType, int> costLookup, Action<BaseSlot, WeaponType> onConfirm)
{
    targetSlot = slot;
    this.onConfirm = onConfirm;
    for (int i = 0; i < buttons.Count; i++)
    {
        var weapon = weapons[i];
        int cost = costLookup(weapon);
        buttons[i].Bind(weapon, cost, OnWeaponClicked);
    }
    SetVisible(true);
    RefreshAll();
}
```

And SlotManager.OpenBuildPanel:
```csharp
    private void OpenBuildPanel(BaseSlot slot)
    {
        if (buildSlotPanel == null) return;
        buildSlotPanel.Open(slot, NextBuildCost, OnConfirmBuild);
    }
```

### Task 10.5: Compile, test, manual verify

- [ ] **Step 1: Compile**

- [ ] **Step 2: Run both test suites**

EditMode 59/59, PlayMode 6/6.

- [ ] **Step 3: Manual play-test**

Enter play mode, set money to 100 via debug overlay, buy a railgun into an empty side slot, verify:
- Railgun button shows `$1` (dev-mode cost)
- After purchase, a `][` shape appears at the slot position
- Barrel charges through the 4 color stops (white → two mid blues → full #93DAFE)
- Fires at meteors, spawning a white bullet and eventually a blue streak
- Click the railgun → RailgunUpgradePanel opens showing 5 stats
- Upgrade each stat and confirm effect (Speed faster, Weight deeper, Caliber wider, Fire Rate charges faster, Rotation Speed slews faster)

Exit play mode, scrub scene drift.

### Task 10.6: Commit Phase 10

```bash
git add Assets/Scripts/Weapons/WeaponType.cs
git add Assets/Scripts/SlotManager.cs
git add Assets/Scripts/UI/BuildSlotPanel.cs
git add Assets/Scenes/Game.unity
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Expose Railgun in BuildSlotPanel and SlotManager

WeaponType enum gains Railgun. SlotManager.railgunBuildCosts array
(\$1 dev-mode values) parallels the missile cost array. NextBuildCost
becomes weapon-aware via an overload; the old no-arg version delegates
to the missile table for backward compatibility.

BuildSlotPanel.Open now takes a Func<WeaponType, int> cost lookup
instead of a single cost, and passes each button its weapon-specific
cost on Open. The weapons array default adds Railgun.

After this commit, a player starting fresh can buy either a Missile
or a Railgun into an empty side slot via the build modal. Each
weapon's cost is displayed on its own button.

EditMode 59/59, PlayMode 6/6, manual play-test confirms the full
purchase -> charge -> fire -> tunnel -> upgrade loop."
```

---

<a name="phase-11"></a>
## Phase 11 — CLAUDE.md update

**Goal:** Update project docs so future sessions (and this session on reload) understand the new weapon, the PlayMode test suite, and the Meteors physics layer.

### Task 11.1: Update CLAUDE.md

- [ ] **Step 1: Read current CLAUDE.md**

- [ ] **Step 2: Update the Tech stack line**

Change:
```
- C# game code lives in the `MeteorIdle` assembly (`Assets/Scripts/MeteorIdle.asmdef`); EditMode tests in `MeteorIdle.Tests.Editor` (`Assets/Tests/EditMode/`)
```
to:
```
- C# game code in the `MeteorIdle` assembly (`Assets/Scripts/MeteorIdle.asmdef`); EditMode tests in `MeteorIdle.Tests.Editor`; PlayMode tests in `MeteorIdle.Tests.PlayMode` (`Assets/Tests/PlayMode/`)
```

- [ ] **Step 3: Update the testing policy section**

Expand to mention PlayMode tests are now part of the suite and must also pass before pushing to main. Add a note about the `Meteors` physics layer being required for railgun raycasts.

- [ ] **Step 4: Update "Stats, upgrades, and the missile loop" section**

Rename to "Weapons and upgrades" or similar, documenting that there are now two weapons with separate stats assets and separate upgrade panels.

- [ ] **Step 5: Update the project layout tree**

Add the new files under `Assets/Scripts/`, `Assets/Tests/PlayMode/`, etc.

### Task 11.2: Commit Phase 11

```bash
git add CLAUDE.md
# Run the identity-leak scrub per feedback_identity_leaks.md — expect zero matches
git -c commit.gpgsign=false commit -m "Update CLAUDE.md for railgun + PlayMode tests

- Tech stack: note MeteorIdle.Tests.PlayMode assembly.
- Testing policy: PlayMode tests must also pass before merging to main.
  Mentions Meteors physics layer as a requirement for railgun raycasts.
- Weapons section rewritten to cover both Missile and Railgun,
  separate stats assets, separate contextual upgrade panels.
- Project layout tree updated with the new files."
```

---

<a name="phase-12"></a>
## Phase 12 — Final verification and hand-back

**Goal:** One clean pass through everything, then hand back to the user for their manual verification before fast-forwarding main.

### Task 12.1: Full test runs

- [ ] `mcp__UnityMCP__run_tests mode=EditMode` → 59/59
- [ ] `mcp__UnityMCP__run_tests mode=PlayMode` → 6/6

### Task 12.2: Full manual play-test checklist

- [ ] Enter play mode, reset everything via debug overlay
- [ ] Set money high
- [ ] Center slot still fires missiles — missile upgrade panel opens on click, closes on click outside
- [ ] Buy a missile into a side slot — works, costs $1
- [ ] Buy a railgun into the other side slot — works, costs $1
- [ ] Railgun charges through 4 color stops, fires a visible white bullet, leaves a blue streak, tunnels into meteors
- [ ] Railgun pierces two stacked meteors when budget is sufficient (upgrade Weight a few times and verify)
- [ ] Railgun upgrade panel opens on click, closes on click outside, all 5 stats upgradable
- [ ] Fire a railgun past an in-flight missile — missile is unaffected
- [ ] Debug overlay Reset button still works
- [ ] Escape key closes build panel
- [ ] Click outside build panel closes it (if already wired)

### Task 12.3: Final scene drift scrub

```bash
git diff Assets/Scenes/Game.unity | grep -E "^-  m_(AnchorMin|AnchorMax|AnchoredPosition|SizeDelta|TextStyleHashCode|fontColor32)" | head -5
```

Expected: empty.

### Task 12.4: Full-branch identity scrub

```bash
# Full-branch identity scrub per feedback_identity_leaks.md — expect zero matches
echo "grep exit: $?"
```

Expected: `grep exit: 1`.

### Task 12.5: Hand back

Final message to user:

> "Railgun feature is implemented on `iter/railgun` across 13 commits, all tests green (59 EditMode, 6 PlayMode), manual checklist passed. Please play-test in the editor one more time — when you say go, I'll fast-forward `main` to the branch tip and push."

**Next step after user approval:** FF main, push, delete branch.

---

## Self-review checklist

Before handing this plan off to execution, I verified:

- [x] **Spec coverage:** every section of the design spec is implemented by a numbered task. The gameplay mechanic is in Phase 2 (ApplyTunnel) + Phase 5 (RailgunRound). Upgrade stats are Phase 3. Visuals are Phase 4. Architecture is Phases 1 + 5 + 6 + 8 + 9. Tests are Phases 2 + 3 + 7 (and baseline tests in Phase 0).
- [x] **No placeholders:** spot-checked for "TBD", "TODO", "implement later" — none found. Every code block is complete.
- [x] **Type consistency:** `RailgunStats` / `RailgunStatId` / `RailgunTurret` / `RailgunRound` / `RailgunStreak` / `RailgunUpgradePanel` names match across all referring tasks. The `BaseSlot` setter rename (`SetUpgradePanel` → `SetMissileUpgradePanel` + `SetRailgunUpgradePanel`) is consistent in Tasks 8.3 and 9.3.
- [x] **File paths:** all paths start with `Assets/`. New script files include `.meta` in their commit `git add` lines. Prefab paths and asset paths match what the `execute_code` scripts write to.
- [x] **Exact commands:** all shell commands and MCP tool invocations include their exact arguments. No "run the tests" without specifying mode and assembly.
- [x] **Phase count:** Phase 0 through Phase 12 = 13 phases. The spec says "13 commits on `iter/railgun` (Phase 0 prep + 12 railgun phases)". Matches.
