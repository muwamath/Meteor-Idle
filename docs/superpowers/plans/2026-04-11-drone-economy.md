# Iter 3 — Drone Economy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kill instant core payout; core kills spawn CoreDrops; collector drones fly from docked bays to pick them up and deposit on return.

**Architecture:** Custom DroneBody physics integrator (not Rigidbody2D). State-machine-owned CollectorDrone with Idle/Launching/Seeking/Pickup/Returning/Docking/Depositing states. Bay-owned door animation with 4 quantized keyframes. One global DroneUpgradePanel with BAY and DRONE sections (all stats fleet-wide). Paired with `paysOnBreak` flag on VoxelMaterial (default true, false on Core) to isolate the economy change.

**Tech Stack:** Unity 6000.4.1f1, C# MeteorIdle assembly, NUnit EditMode + PlayMode tests, ScriptableObject data assets, procedural PNG art, Unity MCP for editor-side verification.

**Spec:** `docs/superpowers/specs/2026-04-11-drone-economy-design.md`

**Branch:** `iter/drone-economy` (create at start of execution from clean `main`).

---

## Phase 1 — CoreDrop entity + paysOnBreak flag

This phase isolates the economy change. `VoxelMaterial` gets a `paysOnBreak` bool (default `true`). `Core.asset` flips to `false`. `DestroyResult.TotalPayout` is rewritten to respect the flag. `Meteor.ApplyBlast` / `ApplyTunnel` / `DrainPendingDetonations` gain a `SpawnCoreDrop` side-effect branch for Core kills. `CoreDrop.cs` + a global pool on `GameManager` handle the floating drop entity. After this phase, missile/railgun payout for gold/explosive is unchanged, but killing cores produces drops the player cannot yet collect (drones arrive Phase 4).

### Task 1.1: Add `paysOnBreak` field to `VoxelMaterial`

**Files:**
- Modify: `Assets/Scripts/VoxelMaterial.cs`
- Test: `Assets/Tests/EditMode/VoxelMaterialTests.cs` (extend existing fixture)

- [ ] **Step 1: Write the failing test**

Append to `VoxelMaterialTests`:

```csharp
[Test]
public void Dirt_PaysOnBreak_DefaultTrue()
{
    var m = _registry.GetByName("Dirt");
    Assert.IsTrue(m.paysOnBreak, "Dirt defaults to paysOnBreak=true");
}

[Test]
public void Gold_PaysOnBreak_True()
{
    var m = _registry.GetByName("Gold");
    Assert.IsTrue(m.paysOnBreak);
}

[Test]
public void Explosive_PaysOnBreak_True()
{
    var m = _registry.GetByName("Explosive");
    Assert.IsTrue(m.paysOnBreak);
}

[Test]
public void Core_PaysOnBreak_False_Iter3()
{
    var m = _registry.GetByName("Core");
    Assert.IsFalse(m.paysOnBreak,
        "Iter 3: cores go through CoreDrop path, not direct payout");
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=VoxelMaterialTests
```

Expected failures: `Dirt_PaysOnBreak_DefaultTrue`, `Gold_PaysOnBreak_True`, `Explosive_PaysOnBreak_True`, and `Core_PaysOnBreak_False_Iter3` — all fail because the field doesn't exist yet (compile error, then assertion error).

- [ ] **Step 3: Implement**

Add the field to `VoxelMaterial.cs` under the `[Header("Mechanics")]` block:

```csharp
[Tooltip("If true, destroying a cell of this material pays out immediately via GameManager.AddMoney. If false, the caller is responsible for an alternate payout path (e.g. Iter 3 cores spawning CoreDrops).")]
public bool paysOnBreak = true;
```

Then flip the Core asset via execute_code:

```
mcp__UnityMCP__execute_code code=<<EOF
var core = UnityEditor.AssetDatabase.LoadAssetAtPath<VoxelMaterial>(
    "Assets/Data/Materials/Core.asset");
core.paysOnBreak = false;
UnityEditor.EditorUtility.SetDirty(core);
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("[Iter3] Core.paysOnBreak = " + core.paysOnBreak);
EOF
```

Expected console output: `[Iter3] Core.paysOnBreak = False`.

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=VoxelMaterialTests
```

Expected: all `VoxelMaterialTests` pass.

- [ ] **Step 5: Run full EditMode suite to confirm no regressions**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelMaterial.cs Assets/Data/Materials/Core.asset \
        Assets/Tests/EditMode/VoxelMaterialTests.cs
python3 tools/identity-scrub.py
```

Expected: `identity scrub: clean`.

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase1: VoxelMaterial.paysOnBreak flag + Core flip"
```

---

### Task 1.2: Rewrite `DestroyResult.TotalPayout` to respect `paysOnBreak`

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` (DestroyResult struct + AccumulateDestroyed)
- Test: `Assets/Tests/EditMode/DestroyResultPayoutTests.cs` (new)

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Iter 3: DestroyResult.TotalPayout must sum payoutPerCell only for
    // materials with paysOnBreak=true. Cores (paysOnBreak=false) must not
    // contribute to TotalPayout — their value flows through CoreDrops.
    public class DestroyResultPayoutTests
    {
        private MaterialRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
        }

        [Test]
        public void BlastHittingOnlyDirt_PayoutZero()
        {
            var go = new GameObject("TestMeteor",
                typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            InjectRegistry(m);
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed: 42, sizeScale: 1f);

            // Blast far off-center so it only clips dirt cells.
            var result = m.ApplyBlast(
                m.GetVoxelWorldPosition(0, 0), 0.05f);

            Assert.AreEqual(0, result.TotalPayout, "dirt-only blast pays 0");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void BlastKillingCoreCell_DoesNotContributeToTotalPayout()
        {
            var go = new GameObject("TestMeteor",
                typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            InjectRegistry(m);
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed: 7, sizeScale: 1f);

            // Force a core cell at (5,5) via reflection — same pattern as ExplosiveChainTests.
            ForceMaterial(m, 5, 5, "Core");

            var result = m.ApplyBlast(m.GetVoxelWorldPosition(5, 5), 0.05f);

            // Legacy shim still counts the core kill for Iter 1 tests.
            Assert.GreaterOrEqual(result.coreDestroyed, 1);
            // But TotalPayout must NOT include core value (paysOnBreak=false).
            // Only dirt (payout 0) could have been in the tiny blast radius;
            // confirm the core's payoutPerCell=5 did not land in the sum.
            Assert.AreEqual(0, result.TotalPayout,
                "core kill must not contribute to TotalPayout (paysOnBreak=false)");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void BlastKillingGoldCell_ContributesGoldPayoutToTotalPayout()
        {
            var go = new GameObject("TestMeteor",
                typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            InjectRegistry(m);
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed: 11, sizeScale: 1f);

            ForceMaterial(m, 5, 5, "Gold");
            var gold = _registry.GetByName("Gold");
            int expected = gold.payoutPerCell;

            var result = m.ApplyBlast(m.GetVoxelWorldPosition(5, 5), 0.05f);
            Assert.GreaterOrEqual(result.TotalPayout, expected,
                "gold (paysOnBreak=true) must contribute payoutPerCell to TotalPayout");
            Object.DestroyImmediate(go);
        }

        private void InjectRegistry(Meteor m)
        {
            var f = typeof(Meteor).GetField("materialRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(m, _registry);
        }

        private void ForceMaterial(Meteor m, int gx, int gy, string name)
        {
            var matField = typeof(Meteor).GetField("material",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var hpField = typeof(Meteor).GetField("hp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mat = (VoxelMaterial[,])matField.GetValue(m);
            var kind = (VoxelKind[,])kindField.GetValue(m);
            var hp = (int[,])hpField.GetValue(m);
            var target = _registry.GetByName(name);
            mat[gx, gy] = target;
            kind[gx, gy] = name == "Core" ? VoxelKind.Core : VoxelKind.Dirt;
            hp[gx, gy] = target.baseHp;
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=DestroyResultPayoutTests
```

Expected: `BlastKillingCoreCell_DoesNotContributeToTotalPayout` fails — current `AccumulateDestroyed` adds `mat.payoutPerCell` unconditionally, so core's 5 lands in `totalPayout`.

- [ ] **Step 3: Implement**

In `Meteor.cs`, modify `AccumulateDestroyed`:

```csharp
private void AccumulateDestroyed(ref DestroyResult result, VoxelMaterial mat)
{
    if (mat == null || materialRegistry == null) return;
    if (result.countByMaterialIndex == null)
        result.countByMaterialIndex = new int[materialRegistry.materials.Length];
    int idx = materialRegistry.IndexOf(mat);
    if (idx < 0) return;
    result.countByMaterialIndex[idx]++;
    // Iter 3: only materials flagged paysOnBreak=true contribute to the
    // direct-payout sum. Cores (paysOnBreak=false) go through SpawnCoreDrop
    // and deposit via the collector drone loop.
    if (mat.paysOnBreak) result.totalPayout += mat.payoutPerCell;
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=DestroyResultPayoutTests
```

Expected: all 3 pass.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green. `ExplosiveChainTests.IsolatedExplosive_DestroysOnlyItself_PaysOne` still passes because Explosive's `paysOnBreak=true` is unchanged.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Meteor.cs Assets/Tests/EditMode/DestroyResultPayoutTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase1: DestroyResult.TotalPayout respects paysOnBreak"
```

---

### Task 1.3: `CoreDrop.cs` entity + prefab + global pool on `GameManager`

**Files:**
- Create: `Assets/Scripts/Drones/CoreDrop.cs`
- Create: `Assets/Prefabs/CoreDrop.prefab` (via execute_code)
- Modify: `Assets/Scripts/GameManager.cs` (add drop registry + pool helpers)
- Test: `Assets/Tests/EditMode/CoreDropTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class CoreDropTests
    {
        [Test]
        public void Spawn_SetsPositionAndValue_AndStartsUnclaimed()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(1.5f, 2f, 0f), value: 5);

            Assert.AreEqual(new Vector3(1.5f, 2f, 0f), drop.transform.position);
            Assert.AreEqual(5, drop.Value);
            Assert.IsFalse(drop.IsClaimed);
            Assert.IsTrue(drop.IsAlive);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Drift_MovesDownwardSlowlyOnTick()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(0f, 0f, 0f), value: 5);

            float y0 = drop.transform.position.y;
            drop.TickDrift(dt: 1f);
            Assert.Less(drop.transform.position.y, y0, "drop drifts downward");
            // Drift rate should be < 0.15 world/sec (10-20% of base meteor fall 0.4..0.67)
            Assert.Greater(drop.transform.position.y, y0 - 0.2f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TickDrift_PastDespawnY_MarksForDespawn()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(0f, -9f, 0f), value: 5);

            drop.TickDrift(dt: 10f); // should push well below despawnY
            Assert.IsFalse(drop.IsAlive, "drop despawns when below despawnY");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Claim_MarksClaimed_BlocksDoubleClaim()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, value: 5);

            Assert.IsTrue(drop.TryClaim(), "first claim succeeds");
            Assert.IsTrue(drop.IsClaimed);
            Assert.IsFalse(drop.TryClaim(), "second claim fails");
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=CoreDropTests
```

Expected: compile error (type `CoreDrop` does not exist).

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/CoreDrop.cs`:

```csharp
using UnityEngine;

// A pooled floating entity spawned when a Core voxel is destroyed. Drifts
// downward at roughly 15% of base meteor speed until a drone grabs it or it
// falls off the bottom of the screen. Pays `value` when a drone deposits it
// at a bay — not on break.
[RequireComponent(typeof(SpriteRenderer))]
public class CoreDrop : MonoBehaviour
{
    [SerializeField] private float driftSpeed = 0.08f;
    [SerializeField] private float despawnY = -9.2f;
    [SerializeField] private Color dropColor = new Color(0.75f, 0.25f, 0.25f, 1f);

    private SpriteRenderer sr;
    private int value;
    private bool claimed;
    private bool alive;

    public int Value => value;
    public bool IsClaimed => claimed;
    public bool IsAlive => alive && gameObject.activeInHierarchy;
    public Vector3 Position => transform.position;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite == null)
        {
            // Fallback sprite so EditMode tests don't need a prefab: a single
            // red pixel stretched to ~0.3 world units.
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, dropColor);
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 3f);
        }
    }

    public void Spawn(Vector3 position, int value)
    {
        transform.position = position;
        this.value = value;
        claimed = false;
        alive = true;
        if (sr != null) sr.color = Color.white;
    }

    // Called once per frame from GameManager.Update for every active drop.
    // Drops drift down, and despawn when they cross despawnY.
    public void TickDrift(float dt)
    {
        if (!alive) return;
        transform.position += new Vector3(0f, -driftSpeed * dt, 0f);
        if (transform.position.y < despawnY) alive = false;
    }

    // Single-winner claim: first drone to call TryClaim gets the drop.
    // Later callers see IsClaimed==true and pick a different target.
    public bool TryClaim()
    {
        if (claimed || !alive) return false;
        claimed = true;
        return true;
    }

    // Called by a drone once the drop has been physically delivered to the
    // bay. Pays out and returns the drop to the pool.
    public void Consume()
    {
        alive = false;
        claimed = false;
        gameObject.SetActive(false);
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=CoreDropTests
```

Expected: 4 passed.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/CoreDrop.cs Assets/Tests/EditMode/CoreDropTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase1: CoreDrop entity + EditMode drift/claim tests"
```

---

### Task 1.4: `GameManager` drop registry + `RegisterDrop` API

**Files:**
- Modify: `Assets/Scripts/GameManager.cs`
- Test: `Assets/Tests/EditMode/GameManagerDropRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class GameManagerDropRegistryTests
    {
        private GameObject _gmGo;
        private GameManager _gm;

        [SetUp]
        public void SetUp()
        {
            _gmGo = new GameObject("TestGM", typeof(GameManager));
            _gm = _gmGo.GetComponent<GameManager>();
            TestHelpers.InvokeAwake(_gm);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_gmGo); }

        [Test]
        public void RegisterDrop_AddsToActiveList()
        {
            var dropGo = new GameObject("Drop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, 5);

            _gm.RegisterDrop(drop);
            Assert.Contains(drop, (System.Collections.ICollection)_gm.ActiveDrops);
            Object.DestroyImmediate(dropGo);
        }

        [Test]
        public void UnregisterDrop_RemovesFromActiveList()
        {
            var dropGo = new GameObject("Drop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, 5);

            _gm.RegisterDrop(drop);
            _gm.UnregisterDrop(drop);
            Assert.AreEqual(0, _gm.ActiveDrops.Count);
            Object.DestroyImmediate(dropGo);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=GameManagerDropRegistryTests
```

Expected: compile error — `RegisterDrop`, `ActiveDrops`, `UnregisterDrop` don't exist.

- [ ] **Step 3: Implement**

Add to `GameManager.cs`:

```csharp
// Iter 3: global registry of active CoreDrops. Drones iterate this list
// to find targets. Drops register themselves when spawned by Meteor and
// unregister when consumed or despawned.
private readonly System.Collections.Generic.List<CoreDrop> activeDrops
    = new System.Collections.Generic.List<CoreDrop>();

public System.Collections.Generic.IReadOnlyList<CoreDrop> ActiveDrops => activeDrops;

public void RegisterDrop(CoreDrop drop)
{
    if (drop == null) return;
    if (!activeDrops.Contains(drop)) activeDrops.Add(drop);
}

public void UnregisterDrop(CoreDrop drop)
{
    if (drop == null) return;
    activeDrops.Remove(drop);
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=GameManagerDropRegistryTests
```

Expected: 2 passed.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/GameManager.cs Assets/Tests/EditMode/GameManagerDropRegistryTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase1: GameManager.RegisterDrop/ActiveDrops API"
```

---

### Task 1.5: `Meteor.SpawnCoreDrop` branch on Core kills

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`
- Create: `Assets/Prefabs/CoreDrop.prefab` (via execute_code)
- Test: `Assets/Tests/EditMode/MeteorCoreDropsSpawnTests.cs`

- [ ] **Step 1: Create the CoreDrop prefab via execute_code**

```
mcp__UnityMCP__execute_code code=<<EOF
var go = new UnityEngine.GameObject("CoreDrop");
var sr = go.AddComponent<UnityEngine.SpriteRenderer>();
sr.sortingOrder = 5;
var drop = go.AddComponent<CoreDrop>();
// Build a 7x7 red-core sprite inline for the prefab default.
var tex = new UnityEngine.Texture2D(7, 7);
tex.filterMode = UnityEngine.FilterMode.Point;
for (int y = 0; y < 7; y++)
    for (int x = 0; x < 7; x++)
    {
        bool edge = (x == 0 || y == 0 || x == 6 || y == 6);
        tex.SetPixel(x, y, edge
            ? new UnityEngine.Color(0.35f, 0.10f, 0.10f, 1f)
            : new UnityEngine.Color(0.75f, 0.25f, 0.25f, 1f));
    }
tex.Apply();
var pngBytes = tex.EncodeToPNG();
System.IO.Directory.CreateDirectory("Assets/Art");
System.IO.File.WriteAllBytes("Assets/Art/CoreDrop.png", pngBytes);
UnityEditor.AssetDatabase.ImportAsset("Assets/Art/CoreDrop.png");
var importer = (UnityEditor.TextureImporter)UnityEditor.TextureImporter.GetAtPath("Assets/Art/CoreDrop.png");
importer.textureType = UnityEditor.TextureImporterType.Sprite;
importer.filterMode = UnityEngine.FilterMode.Point;
importer.spritePixelsPerUnit = 30f;
importer.SaveAndReimport();
var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/CoreDrop.png");
sr.sprite = sprite;
System.IO.Directory.CreateDirectory("Assets/Prefabs");
UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/CoreDrop.prefab");
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Debug.Log("[Iter3] CoreDrop.prefab created");
EOF
```

Expected console: `[Iter3] CoreDrop.prefab created`, no errors.

- [ ] **Step 2: Write the failing test**

```csharp
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class MeteorCoreDropsSpawnTests
    {
        private MaterialRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
        }

        [Test]
        public void Blast_KillingCoreCell_SpawnsCoreDropViaGameManager()
        {
            var gmGo = new GameObject("TestGM", typeof(GameManager));
            var gm = gmGo.GetComponent<GameManager>();
            TestHelpers.InvokeAwake(gm);

            var go = new GameObject("TestMeteor",
                typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            InjectRegistry(m);
            InjectDropPrefab(m);
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed: 123, sizeScale: 1f);

            ForceMaterial(m, 5, 5, "Core");
            int dropsBefore = gm.ActiveDrops.Count;

            m.ApplyBlast(m.GetVoxelWorldPosition(5, 5), 0.05f);

            Assert.AreEqual(dropsBefore + 1, gm.ActiveDrops.Count,
                "core kill should register one CoreDrop");
            var drop = gm.ActiveDrops[dropsBefore];
            Assert.AreEqual(_registry.GetByName("Core").payoutPerCell, drop.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(gmGo);
        }

        [Test]
        public void Blast_KillingGoldCell_DoesNotSpawnCoreDrop()
        {
            var gmGo = new GameObject("TestGM", typeof(GameManager));
            var gm = gmGo.GetComponent<GameManager>();
            TestHelpers.InvokeAwake(gm);

            var go = new GameObject("TestMeteor",
                typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            InjectRegistry(m);
            InjectDropPrefab(m);
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed: 9, sizeScale: 1f);

            ForceMaterial(m, 5, 5, "Gold");
            m.ApplyBlast(m.GetVoxelWorldPosition(5, 5), 0.05f);

            Assert.AreEqual(0, gm.ActiveDrops.Count,
                "gold kill should not spawn a CoreDrop");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(gmGo);
        }

        private void InjectRegistry(Meteor m)
        {
            var f = typeof(Meteor).GetField("materialRegistry",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(m, _registry);
        }

        private void InjectDropPrefab(Meteor m)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<CoreDrop>("Assets/Prefabs/CoreDrop.prefab");
            var f = typeof(Meteor).GetField("coreDropPrefab",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(m, prefab);
        }

        private void ForceMaterial(Meteor m, int gx, int gy, string name)
        {
            var matField = typeof(Meteor).GetField("material",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var hpField = typeof(Meteor).GetField("hp",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var mat = (VoxelMaterial[,])matField.GetValue(m);
            var kind = (VoxelKind[,])kindField.GetValue(m);
            var hp = (int[,])hpField.GetValue(m);
            var target = _registry.GetByName(name);
            mat[gx, gy] = target;
            kind[gx, gy] = name == "Core" ? VoxelKind.Core : VoxelKind.Dirt;
            hp[gx, gy] = target.baseHp;
        }
    }
}
```

- [ ] **Step 3: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=MeteorCoreDropsSpawnTests
```

Expected: fails — `Meteor` has no `coreDropPrefab` field yet, and core kills don't spawn drops.

- [ ] **Step 4: Implement**

Add to `Meteor.cs` (near `materialRegistry`):

```csharp
[Tooltip("Iter 3: prefab instantiated when a Core voxel is destroyed. Leave null to skip drop spawning (Iter 1/2 tests).")]
[SerializeField] private CoreDrop coreDropPrefab;
```

Add a helper method on `Meteor`:

```csharp
// Iter 3: spawn a floating CoreDrop at the world position of the cell that
// was just killed. Registers with GameManager so drones can find it. Safe
// with null prefab (Iter 1/2 test path), safe with no GameManager (EditMode
// tests that don't need the registry bookkeeping — the caller passes one in
// explicitly if they care).
private void SpawnCoreDrop(int gx, int gy, int value)
{
    if (coreDropPrefab == null) return;
    Vector3 worldPos = VoxelCenterToWorld(gx, gy);
    var drop = Instantiate(coreDropPrefab, worldPos, Quaternion.identity);
    drop.Spawn(worldPos, value);
    if (GameManager.Instance != null) GameManager.Instance.RegisterDrop(drop);
}
```

Then in `ApplyBlast`, `ApplyTunnel`, and `DrainPendingDetonations` — at the 3 sites that handle a destroyed cell — add the core-drop branch after `AccumulateDestroyed`:

```csharp
// Iter 3: core kills go through the drop path, not direct payout.
// paysOnBreak=false on Core steers TotalPayout away from the value, and
// here we spawn a floating drop for a drone to pick up later.
if (matHere != null && !matHere.paysOnBreak)
    SpawnCoreDrop(x, y, matHere.payoutPerCell);
```

(In `ApplyTunnel` use `ix, iy`; in `DrainPendingDetonations` use `nx, ny`.)

- [ ] **Step 5: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=MeteorCoreDropsSpawnTests
```

Expected: both tests pass.

- [ ] **Step 6: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green. Existing `ExplosiveChainTests` and `MeteorApplyBlastTests` still pass because gold/explosive keep `paysOnBreak=true` and the legacy shim fields are untouched.

Also assign the prefab in the scene `Meteor.prefab`:

```
mcp__UnityMCP__execute_code code=<<EOF
var prefabPath = "Assets/Prefabs/Meteor.prefab";
var dropPath = "Assets/Prefabs/CoreDrop.prefab";
var meteor = UnityEditor.AssetDatabase.LoadAssetAtPath<Meteor>(prefabPath);
var drop = UnityEditor.AssetDatabase.LoadAssetAtPath<CoreDrop>(dropPath);
var so = new UnityEditor.SerializedObject(meteor);
so.FindProperty("coreDropPrefab").objectReferenceValue = drop;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(meteor);
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("[Iter3] Meteor.prefab.coreDropPrefab wired");
EOF
```

- [ ] **Step 7: Identity scrub + commit**

```bash
git add Assets/Scripts/Meteor.cs Assets/Prefabs/CoreDrop.prefab Assets/Prefabs/Meteor.prefab \
        Assets/Art/CoreDrop.png Assets/Art/CoreDrop.png.meta \
        Assets/Tests/EditMode/MeteorCoreDropsSpawnTests.cs
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase1: Meteor.SpawnCoreDrop on core kills + CoreDrop prefab"
```

---

## Phase 2 — DroneBody custom physics integrator

Plain C# class (not a MonoBehaviour) so it's unit-testable without a scene. Fields: position, velocity, thrustCap, dampingPerSec, DesiredThrust, LimpHomeMode. `Integrate(dt)` moves velocity toward `desired × effective thrustCap`, then applies damping. `ApplyPushKick` adds velocity. EditMode tests for thrust, damping, push, limp-home.

### Task 2.1: `DroneBody.cs` — fields, `Integrate`, `ApplyPushKick`

**Files:**
- Create: `Assets/Scripts/Drones/DroneBody.cs`
- Test: `Assets/Tests/EditMode/DroneBodyTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneBodyTests
    {
        [Test]
        public void Integrate_WithZeroDesired_AppliesDamping()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 2f);
            body.Velocity = new Vector2(3f, 0f);
            body.DesiredThrust = Vector2.zero;
            body.Integrate(dt: 0.5f);
            Assert.Less(body.Velocity.magnitude, 3f, "damping reduces speed");
            Assert.Greater(body.Velocity.magnitude, 0f);
        }

        [Test]
        public void Integrate_WithDesired_AcceleratesTowardThrustCap()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
            body.DesiredThrust = Vector2.right;
            body.Integrate(dt: 10f);
            // With zero damping and unit desired, velocity should reach thrustCap.
            Assert.AreEqual(4f, body.Velocity.x, 0.01f);
            Assert.AreEqual(0f, body.Velocity.y, 0.01f);
        }

        [Test]
        public void Integrate_AdvancesPositionByVelocity()
        {
            var body = new DroneBody(new Vector2(1f, 2f), thrustCap: 4f, dampingPerSec: 0f);
            body.Velocity = new Vector2(2f, 0f);
            body.DesiredThrust = Vector2.zero;
            body.Integrate(dt: 0.5f);
            Assert.AreEqual(2f, body.Position.x, 0.01f);
            Assert.AreEqual(2f, body.Position.y, 0.01f);
        }

        [Test]
        public void ApplyPushKick_AddsToVelocity()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
            body.Velocity = new Vector2(1f, 0f);
            body.ApplyPushKick(new Vector2(0f, 3f));
            Assert.AreEqual(1f, body.Velocity.x, 0.01f);
            Assert.AreEqual(3f, body.Velocity.y, 0.01f);
        }

        [Test]
        public void LimpHomeMode_CutsEffectiveThrustCapTo25Percent()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 8f, dampingPerSec: 0f);
            body.LimpHomeMode = true;
            body.DesiredThrust = Vector2.right;
            body.Integrate(dt: 10f);
            Assert.AreEqual(2f, body.Velocity.x, 0.01f,
                "limp-home clamps velocity to 25% of thrustCap");
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBodyTests
```

Expected: compile error — `DroneBody` type missing.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/DroneBody.cs`:

```csharp
using UnityEngine;

// Custom 2D physics integrator for collector drones. Not a MonoBehaviour —
// the CollectorDrone MonoBehaviour owns an instance and ticks it per frame.
// Keeping it plain C# means EditMode tests can exercise it without scene
// setup. See spec §7 for the rationale (control over soft-brake + thrust
// cap + avoidance without fighting Rigidbody2D defaults).
public class DroneBody
{
    public Vector2 Position;
    public Vector2 Velocity;
    public Vector2 DesiredThrust;
    public float ThrustCap;
    public float DampingPerSec;
    public bool LimpHomeMode;

    public DroneBody(Vector2 position, float thrustCap, float dampingPerSec)
    {
        Position = position;
        Velocity = Vector2.zero;
        DesiredThrust = Vector2.zero;
        ThrustCap = thrustCap;
        DampingPerSec = dampingPerSec;
        LimpHomeMode = false;
    }

    // Advance the body one step. Order:
    //   1. Compute effective thrust cap (25% in limp-home mode).
    //   2. Desired target velocity = DesiredThrust.normalized * effective cap.
    //      DesiredThrust magnitude >1 is clamped; magnitude <1 scales linearly
    //      (lets the state machine cruise at partial throttle).
    //   3. MoveTowards current velocity toward target, at thrustCap * dt rate.
    //   4. Apply linear damping: velocity *= (1 - dampingPerSec * dt), clamped.
    //   5. Advance position by velocity * dt.
    public void Integrate(float dt)
    {
        float effectiveCap = LimpHomeMode ? ThrustCap * 0.25f : ThrustCap;

        Vector2 desired;
        float mag = DesiredThrust.magnitude;
        if (mag > 1f) desired = (DesiredThrust / mag) * effectiveCap;
        else          desired = DesiredThrust * effectiveCap;

        // Steer velocity toward desired, rate-limited by thrust cap.
        Velocity = Vector2.MoveTowards(Velocity, desired, effectiveCap * dt);

        // Linear damping — the soft brake. Applied after steering so drones
        // coast to a halt when DesiredThrust is zero.
        float retain = Mathf.Max(0f, 1f - DampingPerSec * dt);
        Velocity *= retain;

        Position += Velocity * dt;
    }

    // Instantaneous velocity change from a meteor contact. The state machine
    // will keep thrusting against it, so the kick feels like a bump rather
    // than a permanent deflection.
    public void ApplyPushKick(Vector2 deltaVelocity)
    {
        Velocity += deltaVelocity;
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBodyTests
```

Expected: 5 passed.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/DroneBody.cs Assets/Tests/EditMode/DroneBodyTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase2: DroneBody custom integrator + EditMode coverage"
```

---

### Task 2.2: Meteor avoidance repulsion in `DroneBody`

**Files:**
- Modify: `Assets/Scripts/Drones/DroneBody.cs`
- Test: `Assets/Tests/EditMode/DroneBodyTests.cs` (extend)

- [ ] **Step 1: Write the failing test**

Append to `DroneBodyTests`:

```csharp
[Test]
public void ApplyAvoidance_AddsRepulsionAwayFromObstacle()
{
    var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
    body.DesiredThrust = Vector2.zero;
    // Obstacle at (1,0) radius 0.5, drone at origin → push leftward.
    body.ApplyAvoidance(new Vector2(1f, 0f), obstacleRadius: 0.5f, safetyMargin: 1f);
    Assert.Less(body.DesiredThrust.x, 0f, "avoidance pushes drone away from obstacle");
    Assert.AreEqual(0f, body.DesiredThrust.y, 0.01f);
}

[Test]
public void ApplyAvoidance_OutsideSafetyRadius_NoChange()
{
    var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
    body.DesiredThrust = Vector2.right;
    body.ApplyAvoidance(new Vector2(10f, 0f), obstacleRadius: 0.5f, safetyMargin: 1f);
    Assert.AreEqual(1f, body.DesiredThrust.x, 0.01f, "far obstacles do not alter thrust");
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBodyTests
```

Expected: compile error — `ApplyAvoidance` missing.

- [ ] **Step 3: Implement**

Add to `DroneBody.cs`:

```csharp
// Accumulate repulsion into DesiredThrust from a single obstacle. The state
// machine calls this once per meteor per frame BEFORE Integrate. Repulsion
// magnitude scales inversely with distance so close obstacles dominate.
// Obstacles beyond obstacleRadius + safetyMargin contribute nothing.
public void ApplyAvoidance(Vector2 obstaclePosition, float obstacleRadius, float safetyMargin)
{
    Vector2 away = Position - obstaclePosition;
    float dist = away.magnitude;
    float safety = obstacleRadius + safetyMargin;
    if (dist >= safety || dist < 0.0001f) return;
    float intensity = 1f - (dist / safety); // 0 at edge, 1 at center
    DesiredThrust += (away / dist) * intensity;
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBodyTests
```

Expected: 7 passed (5 prior + 2 new).

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/DroneBody.cs Assets/Tests/EditMode/DroneBodyTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase2: DroneBody.ApplyAvoidance repulsion"
```

---

## Phase 3 — CollectorDrone state machine (no physics yet)

`DroneState` enum, `CollectorDrone` MonoBehaviour with `Tick(dt)` driving transitions using an injected body interface. Transitions match spec §10 exactly. Reserve threshold 40%, limp-home 0%. EditMode tests for every transition in isolation.

### Task 3.1: `DroneState` enum + `ICollectorDroneEnvironment` interface

**Files:**
- Create: `Assets/Scripts/Drones/DroneState.cs`
- Create: `Assets/Scripts/Drones/ICollectorDroneEnvironment.cs`

- [ ] **Step 1: Write the stub files**

Create `DroneState.cs`:

```csharp
// State of a single CollectorDrone. Transitions are driven by Tick(dt) in
// CollectorDrone; see spec §10 for the full diagram.
public enum DroneState
{
    Idle = 0,         // docked, recharging
    Launching = 1,    // doors opening, drone still in bay
    Seeking = 2,      // in flight toward a claimed CoreDrop
    Pickup = 3,       // reached drop, claiming + stashing in cargo
    Returning = 4,    // flying back to home bay
    Docking = 5,      // at bay, doors opening, drone drifting in
    Depositing = 6,   // inside bay, cargo paying out
}
```

Create `ICollectorDroneEnvironment.cs`:

```csharp
using UnityEngine;

// Dependency boundary for CollectorDrone. The real implementation lives on
// DroneBay (runtime) and a mock in tests. Keeps the state-machine tests free
// of GameManager + scene setup.
public interface ICollectorDroneEnvironment
{
    // Home bay world position — drones return here.
    Vector3 BayPosition { get; }

    // True iff the bay's doors are open enough for a drone to exit/enter.
    bool BayDoorsOpen { get; }

    // State machine requests — bay answers by driving its door animation.
    void RequestOpenDoors();
    void RequestCloseDoors();

    // Pick the nearest unclaimed CoreDrop within the given radius. Returns
    // null if nothing is in range. Real impl iterates GameManager.ActiveDrops;
    // tests provide a fixed list.
    CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance);

    // Deposit `value` into the money pool. Real impl calls GameManager.AddMoney.
    void Deposit(int value);
}
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

- [ ] **Step 3: No commit yet — Phase 3 commits end of Task 3.3.**

---

### Task 3.2: `CollectorDrone` MonoBehaviour skeleton + Idle→Launching→Seeking transitions

**Files:**
- Create: `Assets/Scripts/Drones/CollectorDrone.cs`
- Test: `Assets/Tests/EditMode/DroneStateMachineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Mock environment used by all DroneStateMachineTests.
    internal class MockEnvironment : ICollectorDroneEnvironment
    {
        public Vector3 BayPosition { get; set; } = Vector3.zero;
        public bool BayDoorsOpen { get; set; }
        public int DoorOpenRequests;
        public int DoorCloseRequests;
        public int TotalDeposited;
        public List<CoreDrop> Drops = new List<CoreDrop>();

        public void RequestOpenDoors() { DoorOpenRequests++; BayDoorsOpen = true; }
        public void RequestCloseDoors() { DoorCloseRequests++; BayDoorsOpen = false; }
        public void Deposit(int value) { TotalDeposited += value; }

        public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
        {
            CoreDrop best = null;
            float bestD = float.MaxValue;
            foreach (var d in Drops)
            {
                if (d == null || d.IsClaimed || !d.IsAlive) continue;
                float dist = Vector3.Distance(from, d.Position);
                if (dist > maxDistance) continue;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }
    }

    public class DroneStateMachineTests
    {
        private CoreDrop MakeDrop(Vector3 pos, int value)
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var d = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(d);
            d.Spawn(pos, value);
            return d;
        }

        private CollectorDrone MakeDrone(ICollectorDroneEnvironment env)
        {
            var go = new GameObject("TestDrone", typeof(CollectorDrone));
            var drone = go.GetComponent<CollectorDrone>();
            TestHelpers.InvokeAwake(drone);
            drone.Initialize(env,
                thrust: 4f, damping: 1f,
                batteryCapacity: 10f, cargoCapacity: 1,
                reserveThresholdFraction: 0.4f,
                pickupRadius: 0.3f, dockRadius: 0.4f);
            return drone;
        }

        [Test]
        public void StartsInIdleState()
        {
            var env = new MockEnvironment();
            var drone = MakeDrone(env);
            Assert.AreEqual(DroneState.Idle, drone.State);
        }

        [Test]
        public void Idle_WithBatteryFullAndDropAvailable_TransitionsToLaunching()
        {
            var env = new MockEnvironment();
            env.Drops.Add(MakeDrop(new Vector3(3f, 0f, 0f), 5));
            var drone = MakeDrone(env);

            drone.Tick(0.1f); // full battery from Initialize, drop in range

            Assert.AreEqual(DroneState.Launching, drone.State);
            Assert.AreEqual(1, env.DoorOpenRequests);
        }

        [Test]
        public void Idle_WithNoDrops_StaysIdle()
        {
            var env = new MockEnvironment();
            var drone = MakeDrone(env);
            drone.Tick(0.1f);
            Assert.AreEqual(DroneState.Idle, drone.State);
            Assert.AreEqual(0, env.DoorOpenRequests);
        }

        [Test]
        public void Launching_DoorsOpen_TransitionsToSeeking()
        {
            var env = new MockEnvironment();
            env.Drops.Add(MakeDrop(new Vector3(3f, 0f, 0f), 5));
            var drone = MakeDrone(env);
            drone.Tick(0.1f); // -> Launching

            env.BayDoorsOpen = true;
            drone.Tick(0.1f);

            Assert.AreEqual(DroneState.Seeking, drone.State);
            Assert.IsNotNull(drone.TargetDrop);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStateMachineTests
```

Expected: compile error — `CollectorDrone` type missing.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/CollectorDrone.cs`:

```csharp
using UnityEngine;

// A single collector drone. Owns a DroneBody for physics and a state machine
// driving it through the Idle → Launching → Seeking → Pickup → Returning →
// Docking → Depositing → Idle loop. Phase 3 wires up the state transitions
// in isolation with a mock environment; Phase 4 connects the body to real
// meteors and a real bay.
public class CollectorDrone : MonoBehaviour
{
    private ICollectorDroneEnvironment env;
    private DroneBody body;

    private float battery;
    private float batteryCapacity;
    private int cargoCapacity;
    private int cargoCount;
    private int cargoValue;

    private float reserveThresholdFraction;
    private float pickupRadius;
    private float dockRadius;
    private float seekMaxRange = 30f;

    public DroneState State { get; private set; } = DroneState.Idle;
    public CoreDrop TargetDrop { get; private set; }
    public float Battery => battery;
    public int CargoCount => cargoCount;
    public DroneBody Body => body;

    private void Awake()
    {
        body = new DroneBody(transform.position, thrustCap: 4f, dampingPerSec: 1f);
    }

    // Injected constructor used by tests and DroneBay at runtime. Separate
    // from Awake so the environment isn't required until after the drone
    // exists as a GameObject.
    public void Initialize(
        ICollectorDroneEnvironment env,
        float thrust,
        float damping,
        float batteryCapacity,
        int cargoCapacity,
        float reserveThresholdFraction,
        float pickupRadius,
        float dockRadius)
    {
        this.env = env;
        if (body == null) body = new DroneBody(transform.position, thrust, damping);
        body.ThrustCap = thrust;
        body.DampingPerSec = damping;
        this.batteryCapacity = batteryCapacity;
        this.battery = batteryCapacity;
        this.cargoCapacity = Mathf.Max(1, cargoCapacity);
        this.reserveThresholdFraction = reserveThresholdFraction;
        this.pickupRadius = pickupRadius;
        this.dockRadius = dockRadius;
        this.cargoCount = 0;
        this.cargoValue = 0;
        State = DroneState.Idle;
    }

    // Phase 3 drives the state machine logic. Phase 4 adds body.Integrate
    // and real-meteor avoidance. Tick(dt) is safe to call from either a
    // test harness or MonoBehaviour.Update.
    public void Tick(float dt)
    {
        if (env == null) return;
        switch (State)
        {
            case DroneState.Idle: TickIdle(dt); break;
            case DroneState.Launching: TickLaunching(dt); break;
            case DroneState.Seeking: TickSeeking(dt); break;
            case DroneState.Pickup: TickPickup(dt); break;
            case DroneState.Returning: TickReturning(dt); break;
            case DroneState.Docking: TickDocking(dt); break;
            case DroneState.Depositing: TickDepositing(dt); break;
        }
    }

    private void TickIdle(float dt)
    {
        // Recharge while idle.
        battery = Mathf.Min(batteryCapacity, battery + dt);
        if (battery < batteryCapacity) return;
        // Ask environment for a drop in range.
        var drop = env.FindNearestUnclaimedDrop(env.BayPosition, seekMaxRange);
        if (drop == null) return;
        // Door open + handoff.
        env.RequestOpenDoors();
        State = DroneState.Launching;
    }

    private void TickLaunching(float dt)
    {
        if (!env.BayDoorsOpen) return;
        // Pick a drop again — the one we saw in TickIdle may have been
        // claimed by another drone or despawned.
        var drop = env.FindNearestUnclaimedDrop(env.BayPosition, seekMaxRange);
        if (drop == null || !drop.TryClaim())
        {
            env.RequestCloseDoors();
            State = DroneState.Idle;
            return;
        }
        TargetDrop = drop;
        State = DroneState.Seeking;
    }

    private void TickSeeking(float dt)
    {
        // Phase 3 stubs — Phase 4 wires body + drop-distance check.
        if (TargetDrop == null || !TargetDrop.IsAlive)
        {
            // Drop fell off or was stolen; pick another or return.
            var replacement = env.FindNearestUnclaimedDrop(transform.position, seekMaxRange);
            if (replacement != null && replacement.TryClaim()) { TargetDrop = replacement; return; }
            State = DroneState.Returning;
            return;
        }
        battery -= dt;
        if (battery <= batteryCapacity * reserveThresholdFraction) State = DroneState.Returning;
    }

    private void TickPickup(float dt)   { /* Phase 4 */ }
    private void TickReturning(float dt) { /* Phase 4 */ }
    private void TickDocking(float dt)   { /* Phase 4 */ }
    private void TickDepositing(float dt) { /* Phase 4 */ }
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStateMachineTests
```

Expected: 4 tests pass.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: No commit yet.**

---

### Task 3.3: Pickup → Returning → Docking → Depositing → Idle transitions + reserve/limp-home

**Files:**
- Modify: `Assets/Scripts/Drones/CollectorDrone.cs`
- Test: `Assets/Tests/EditMode/DroneStateMachineTests.cs`

- [ ] **Step 1: Append failing tests**

```csharp
[Test]
public void Seeking_ReachesDrop_TransitionsToPickup()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); // idle->launching
    env.BayDoorsOpen = true;
    drone.Tick(0.1f); // launching->seeking

    // Teleport drone to the drop for test speed.
    drone.transform.position = drop.Position;
    drone.Body.Position = drop.Position;
    drone.Tick(0.05f);

    Assert.AreEqual(DroneState.Pickup, drone.State);
}

[Test]
public void Pickup_MovesDropIntoCargo_Cargo1ReturnsImmediately()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f);
    env.BayDoorsOpen = true;
    drone.Tick(0.1f);
    drone.transform.position = drop.Position;
    drone.Body.Position = drop.Position;
    drone.Tick(0.05f); // seeking->pickup
    drone.Tick(0.05f); // pickup->returning (cargoCapacity=1)

    Assert.AreEqual(DroneState.Returning, drone.State);
    Assert.AreEqual(1, drone.CargoCount);
    Assert.IsFalse(drop.IsAlive, "drop consumed on pickup");
}

[Test]
public void Returning_ReachesBay_TransitionsToDocking_OpensDoors()
{
    var env = new MockEnvironment();
    env.BayPosition = Vector3.zero;
    var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
    drone.transform.position = drop.Position;
    drone.Body.Position = drop.Position;
    drone.Tick(0.05f); drone.Tick(0.05f);
    // Teleport back to bay and request dock.
    drone.transform.position = env.BayPosition;
    drone.Body.Position = env.BayPosition;
    env.BayDoorsOpen = false; // reset; drone must re-ask
    int openBefore = env.DoorOpenRequests;
    drone.Tick(0.05f);
    Assert.AreEqual(DroneState.Docking, drone.State);
    Assert.AreEqual(openBefore + 1, env.DoorOpenRequests);
}

[Test]
public void Docking_DoorsOpen_TransitionsToDepositing_PaysCargo()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
    drone.transform.position = drop.Position;
    drone.Body.Position = drop.Position;
    drone.Tick(0.05f); drone.Tick(0.05f);
    drone.transform.position = env.BayPosition;
    drone.Body.Position = env.BayPosition;
    env.BayDoorsOpen = false;
    drone.Tick(0.05f); // returning->docking
    env.BayDoorsOpen = true;
    drone.Tick(0.05f); // docking->depositing

    Assert.AreEqual(DroneState.Depositing, drone.State);
    Assert.AreEqual(5, env.TotalDeposited);
    Assert.AreEqual(0, drone.CargoCount);
}

[Test]
public void Depositing_Completes_ReturnsToIdle_RequestsCloseDoors()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
    drone.transform.position = drop.Position;
    drone.Body.Position = drop.Position;
    drone.Tick(0.05f); drone.Tick(0.05f);
    drone.transform.position = env.BayPosition;
    drone.Body.Position = env.BayPosition;
    env.BayDoorsOpen = false;
    drone.Tick(0.05f);
    env.BayDoorsOpen = true;
    drone.Tick(0.05f); // -> depositing
    int closeBefore = env.DoorCloseRequests;
    drone.Tick(0.05f); // depositing -> idle

    Assert.AreEqual(DroneState.Idle, drone.State);
    Assert.AreEqual(closeBefore + 1, env.DoorCloseRequests);
}

[Test]
public void Seeking_BatteryBelowReserve_TransitionsToReturning_EvenWithClaimedDrop()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(20f, 0f, 0f), 5); // far away
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
    // Drain battery below reserve threshold (40% of 10 = 4).
    for (int i = 0; i < 70; i++) drone.Tick(0.1f);
    Assert.AreEqual(DroneState.Returning, drone.State);
}

[Test]
public void LimpHome_TriggeredWhenBatteryHitsZero_DuringReturning()
{
    var env = new MockEnvironment();
    var drop = MakeDrop(new Vector3(20f, 0f, 0f), 5);
    env.Drops.Add(drop);
    var drone = MakeDrone(env);

    drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
    for (int i = 0; i < 200; i++) drone.Tick(0.1f); // drain battery to zero
    Assert.IsTrue(drone.Body.LimpHomeMode, "limp-home engages at battery 0");
    Assert.AreEqual(DroneState.Returning, drone.State, "still returning, just slower");
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStateMachineTests
```

Expected: the 7 new tests fail — the `Tick*` methods after `Seeking` are empty.

- [ ] **Step 3: Implement**

Replace the empty `TickSeeking`/`TickPickup`/`TickReturning`/`TickDocking`/`TickDepositing` bodies in `CollectorDrone.cs`:

```csharp
private void TickSeeking(float dt)
{
    if (TargetDrop == null || !TargetDrop.IsAlive)
    {
        var replacement = env.FindNearestUnclaimedDrop(transform.position, seekMaxRange);
        if (replacement != null && replacement.TryClaim()) { TargetDrop = replacement; return; }
        State = DroneState.Returning;
        return;
    }
    battery -= dt;
    if (battery <= batteryCapacity * reserveThresholdFraction)
    {
        State = DroneState.Returning;
        return;
    }
    // Check distance to target drop.
    float dist = Vector3.Distance(transform.position, TargetDrop.Position);
    if (dist <= pickupRadius) State = DroneState.Pickup;
}

private void TickPickup(float dt)
{
    if (TargetDrop != null && TargetDrop.IsAlive)
    {
        cargoCount++;
        cargoValue += TargetDrop.Value;
        TargetDrop.Consume();
        if (GameManager.Instance != null) GameManager.Instance.UnregisterDrop(TargetDrop);
        TargetDrop = null;
    }
    // Loiter iff cargo has room AND another drop is reachable AND battery > reserve.
    bool roomInCargo = cargoCount < cargoCapacity;
    bool aboveReserve = battery > batteryCapacity * reserveThresholdFraction;
    if (roomInCargo && aboveReserve)
    {
        var next = env.FindNearestUnclaimedDrop(transform.position, seekMaxRange);
        if (next != null && next.TryClaim()) { TargetDrop = next; State = DroneState.Seeking; return; }
    }
    State = DroneState.Returning;
}

private void TickReturning(float dt)
{
    battery -= dt;
    if (battery <= 0f)
    {
        battery = 0f;
        body.LimpHomeMode = true;
    }
    float dist = Vector3.Distance(transform.position, env.BayPosition);
    if (dist <= dockRadius)
    {
        env.RequestOpenDoors();
        State = DroneState.Docking;
    }
}

private void TickDocking(float dt)
{
    if (!env.BayDoorsOpen) return;
    // Drone has drifted inside bay; pay out and move to Depositing.
    State = DroneState.Depositing;
    env.Deposit(cargoValue);
    cargoCount = 0;
    cargoValue = 0;
}

private void TickDepositing(float dt)
{
    // One-frame transit — could add a small delay for visual flourish later.
    env.RequestCloseDoors();
    body.LimpHomeMode = false;
    State = DroneState.Idle;
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStateMachineTests
```

Expected: 11 tests pass.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/DroneState.cs Assets/Scripts/Drones/ICollectorDroneEnvironment.cs \
        Assets/Scripts/Drones/CollectorDrone.cs \
        Assets/Tests/EditMode/DroneStateMachineTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase3: CollectorDrone state machine + isolated transition tests"
```

---

## Phase 4 — Wire DroneBody into CollectorDrone (PlayMode end-to-end)

`CollectorDrone.Update` now drives real `DroneBody.Integrate`, a single drone + minimal mock bay. A PlayMode test fires a meteor → blasts a core → asserts the drop appears → waits for the drone loop → asserts money increases.

### Task 4.1: `CollectorDrone.Update` drives `DroneBody` thrust toward target

**Files:**
- Modify: `Assets/Scripts/Drones/CollectorDrone.cs`

- [ ] **Step 1: No new test — extend Phase 5 avoidance + Task 4.2 end-to-end test cover this.**

- [ ] **Step 2: Implement**

Add to `CollectorDrone.cs`:

```csharp
private void Update()
{
    float dt = Time.deltaTime;
    if (body == null || env == null) { Tick(dt); return; }

    // 1) Compute desired thrust from state.
    body.DesiredThrust = ComputeDesiredThrust();

    // 2) State machine work (may transition on distance reached etc.).
    Tick(dt);

    // 3) Integrate physics and sync transform.
    body.Integrate(dt);
    transform.position = new Vector3(body.Position.x, body.Position.y, 0f);
}

private Vector2 ComputeDesiredThrust()
{
    switch (State)
    {
        case DroneState.Seeking:
        case DroneState.Pickup:
            if (TargetDrop != null && TargetDrop.IsAlive)
                return ((Vector2)(TargetDrop.Position - transform.position)).normalized;
            return Vector2.zero;
        case DroneState.Returning:
        case DroneState.Docking:
            return ((Vector2)(env.BayPosition - transform.position)).normalized;
        default:
            return Vector2.zero;
    }
}
```

- [ ] **Step 3: Run EditMode suite to verify no regression**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green (state-machine tests still pass because they call `Tick` directly, not `Update`).

- [ ] **Step 4: Identity scrub + commit**

```bash
git add Assets/Scripts/Drones/CollectorDrone.cs
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase4: CollectorDrone.Update drives DroneBody integration"
```

---

### Task 4.2: PlayMode end-to-end test — meteor → drop → drone loop → money

**Files:**
- Create: `Assets/Tests/PlayMode/DroneCollectionEndToEndTests.cs`
- Extend: `Assets/Tests/PlayMode/PlayModeTestFixture.cs` (helper `SpawnTestDrone`)

- [ ] **Step 1: Write the failing test**

Add to `PlayModeTestFixture.cs`:

```csharp
// Iter 3: spawn a headless CollectorDrone at `position` wired to a minimal
// mock environment. The `bayPosition` is where the drone will return; the
// test controls door state directly via the returned env.
protected (CollectorDrone drone, DroneTestEnvironment env) SpawnTestDroneWithEnv(
    Vector3 position,
    Vector3 bayPosition)
{
    var env = new DroneTestEnvironment { BayPosition = bayPosition, BayDoorsOpen = true };
    var go = new GameObject("TestDrone", typeof(CollectorDrone));
    go.transform.position = position;
    var drone = go.GetComponent<CollectorDrone>();
    drone.Initialize(env,
        thrust: 8f, damping: 0.5f,
        batteryCapacity: 60f, cargoCapacity: 1,
        reserveThresholdFraction: 0.4f,
        pickupRadius: 0.4f, dockRadius: 0.5f);
    return (drone, env);
}

// Public test environment for PlayMode drone tests. Mirrors MockEnvironment
// from EditMode but uses GameManager.ActiveDrops so the real Meteor->drop
// path populates it.
public class DroneTestEnvironment : ICollectorDroneEnvironment
{
    public Vector3 BayPosition { get; set; }
    public bool BayDoorsOpen { get; set; }
    public int TotalDeposited;
    public void RequestOpenDoors()  { BayDoorsOpen = true; }
    public void RequestCloseDoors() { BayDoorsOpen = false; }
    public void Deposit(int value)
    {
        TotalDeposited += value;
        if (GameManager.Instance != null) GameManager.Instance.AddMoney(value);
    }
    public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
    {
        if (GameManager.Instance == null) return null;
        CoreDrop best = null;
        float bestD = float.MaxValue;
        foreach (var d in GameManager.Instance.ActiveDrops)
        {
            if (d == null || d.IsClaimed || !d.IsAlive) continue;
            float dist = Vector3.Distance(from, d.Position);
            if (dist > maxDistance) continue;
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }
}
```

Create `Assets/Tests/PlayMode/DroneCollectionEndToEndTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class DroneCollectionEndToEndTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator CoreKill_SpawnsDrop_DroneCollectsAndDeposits()
        {
            yield return SetupScene();

            int startingMoney = _gameManager.Money;

            // 1) Spawn a meteor and force a core cell at (5,5).
            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 77);
            ForceMaterial(meteor, 5, 5, "Core");

            // 2) Blast the core, then the GameManager should have 1 drop.
            meteor.ApplyBlast(meteor.GetVoxelWorldPosition(5, 5), 0.05f);
            Assert.AreEqual(1, _gameManager.ActiveDrops.Count,
                "core kill produced a CoreDrop");
            Assert.AreEqual(startingMoney, _gameManager.Money,
                "core kill did NOT pay directly (Iter3 gate)");

            // 3) Spawn a drone at the bay, pointed at the drop.
            var bay = new Vector3(0f, -5f, 0f);
            var (drone, env) = SpawnTestDroneWithEnv(bay, bay);

            // 4) Let the real game loop run until the drone deposits.
            float timeout = 15f;
            while (timeout > 0f && env.TotalDeposited == 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.Greater(env.TotalDeposited, 0, "drone deposited core value");
            Assert.Greater(_gameManager.Money, startingMoney,
                "GameManager money increased via deposit path");
            TeardownScene();
        }

        private static void ForceMaterial(Meteor meteor, int gx, int gy, string name)
        {
            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            var matField = typeof(Meteor).GetField("material",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var hpField = typeof(Meteor).GetField("hp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mat = (VoxelMaterial[,])matField.GetValue(meteor);
            var kind = (VoxelKind[,])kindField.GetValue(meteor);
            var hp = (int[,])hpField.GetValue(meteor);
            var target = registry.GetByName(name);
            mat[gx, gy] = target;
            kind[gx, gy] = VoxelKind.Core;
            hp[gx, gy] = target.baseHp;
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=PlayMode test_filter=DroneCollectionEndToEndTests
```

Expected: fails on first run — likely a transform/initialization ordering issue.

- [ ] **Step 3: Implement fixes as needed**

Fix any bugs surfaced by the test: ensure `CollectorDrone.Initialize` syncs `body.Position` from `transform.position`, ensure `Update` runs even when `Time.timeScale` is nonzero, etc. Iterate until the test goes green.

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=PlayMode test_filter=DroneCollectionEndToEndTests
```

Expected: passes.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/CollectorDrone.cs \
        Assets/Tests/PlayMode/PlayModeTestFixture.cs \
        Assets/Tests/PlayMode/DroneCollectionEndToEndTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase4: PlayMode end-to-end drone collection test"
```

---

## Phase 5 — Meteor avoidance + contact push

`CollectorDrone.Update` reads `MeteorSpawner.ActiveMeteors` and applies `DroneBody.ApplyAvoidance` per meteor. A `MonoBehaviour` trigger on the drone gameobject delivers `ApplyPushKick` on contact. PlayMode test: drone skirts a stationary meteor.

### Task 5.1: Avoidance wiring in `CollectorDrone.Update`

**Files:**
- Modify: `Assets/Scripts/Drones/CollectorDrone.cs`

- [ ] **Step 1: Write the failing PlayMode test**

Create `Assets/Tests/PlayMode/DroneAvoidanceTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class DroneAvoidanceTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator DroneThrustingPastMeteor_NeverEntersSafetyRadius()
        {
            yield return SetupScene();

            // Stationary meteor at origin.
            var meteor = SpawnTestMeteor(Vector3.zero, seed: 5);
            // Force the meteor to stop falling so we can measure cleanly.
            var velField = typeof(Meteor).GetField("velocity",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            velField.SetValue(meteor, Vector2.zero);

            // Drone far to the left, target far to the right (straight line
            // through the meteor).
            var bay = new Vector3(-5f, 0f, 0f);
            var (drone, env) = SpawnTestDroneWithEnv(bay, bay);

            // Plant a dummy drop on the far side to pull the drone through.
            var dropGo = new GameObject("FarDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            drop.Spawn(new Vector3(5f, 0f, 0f), 5);
            GameManager.Instance.RegisterDrop(drop);

            float closestApproach = float.MaxValue;
            float timeout = 6f;
            while (timeout > 0f && !drop.IsClaimed)
            {
                closestApproach = Mathf.Min(closestApproach,
                    Vector3.Distance(drone.transform.position, meteor.transform.position));
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.Greater(closestApproach, 0.5f,
                "drone should skirt meteor outside its safety radius");
            TeardownScene();
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=PlayMode test_filter=DroneAvoidanceTests
```

Expected: fails — no avoidance yet, drone flies straight through.

- [ ] **Step 3: Implement avoidance**

In `CollectorDrone.cs`, add a field and update `Update`:

```csharp
[SerializeField] private float avoidanceSafetyMargin = 0.35f;
private MeteorSpawner cachedSpawner;

public void SetMeteorSpawner(MeteorSpawner spawner) { cachedSpawner = spawner; }

// Modify Update() after computing desired thrust:
private void Update()
{
    float dt = Time.deltaTime;
    if (body == null || env == null) { Tick(dt); return; }

    body.DesiredThrust = ComputeDesiredThrust();

    // Iter 3 Phase 5: meteor avoidance pass. Reads active meteors from the
    // spawner (injected via SetMeteorSpawner; falls back to FindFirstObject
    // in play mode so the prefab wiring is forgiving).
    ApplyMeteorAvoidance();

    Tick(dt);
    body.Integrate(dt);
    transform.position = new Vector3(body.Position.x, body.Position.y, 0f);
}

private void ApplyMeteorAvoidance()
{
    if (cachedSpawner == null)
    {
#if UNITY_2023_1_OR_NEWER
        cachedSpawner = Object.FindFirstObjectByType<MeteorSpawner>();
#else
        cachedSpawner = Object.FindObjectOfType<MeteorSpawner>();
#endif
    }
    if (cachedSpawner == null) return;
    foreach (var m in cachedSpawner.ActiveMeteors)
    {
        if (m == null || !m.IsAlive) continue;
        float radius = 0.75f * m.transform.localScale.x;
        body.ApplyAvoidance((Vector2)m.transform.position, radius, avoidanceSafetyMargin);
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=PlayMode test_filter=DroneAvoidanceTests
```

Expected: passes.

- [ ] **Step 5: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/CollectorDrone.cs Assets/Tests/PlayMode/DroneAvoidanceTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase5: meteor avoidance via DroneBody.ApplyAvoidance"
```

---

### Task 5.2: Contact push kick on meteor trigger

**Files:**
- Modify: `Assets/Scripts/Drones/CollectorDrone.cs`

- [ ] **Step 1: Add the trigger handler**

Add a `CircleCollider2D` requirement and trigger handler to `CollectorDrone`:

```csharp
[RequireComponent(typeof(CircleCollider2D))]
public class CollectorDrone : MonoBehaviour
{
    // ... existing fields ...
    [SerializeField] private float contactPushMagnitude = 2.5f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null) return;
        Vector2 away = ((Vector2)(transform.position - meteor.transform.position)).normalized;
        if (away.sqrMagnitude < 0.001f) away = Vector2.up;
        body?.ApplyPushKick(away * contactPushMagnitude);
    }
}
```

- [ ] **Step 2: Run full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green (avoidance test still passes; contact is a visual flourish that the avoidance radius normally prevents).

- [ ] **Step 3: Identity scrub + commit**

```bash
git add Assets/Scripts/Drones/CollectorDrone.cs
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase5: drone contact push kick on meteor trigger"
```

---

## Phase 6 — DroneStats + BayStats ScriptableObjects

Mirror `TurretStats`/`RailgunStats` exactly. New assets, EditMode tests for NextCost/CurrentValue/ApplyUpgrade.

### Task 6.1: `DroneStats.cs` + `DroneStats.asset` + tests

**Files:**
- Create: `Assets/Scripts/Drones/DroneStats.cs`
- Create: `Assets/Data/DroneStats.asset`
- Test: `Assets/Tests/EditMode/DroneStatsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneStatsTests
    {
        private DroneStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<DroneStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
        }

        [Test]
        public void NextCost_MatchesGrowthFormula()
        {
            var stat = _stats.thrust;
            Assert.AreEqual(stat.baseCost, stat.NextCost);
            stat.level = 2;
            int expected = Mathf.RoundToInt(stat.baseCost * Mathf.Pow(stat.costGrowth, 2));
            Assert.AreEqual(expected, stat.NextCost);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var stat = _stats.thrust;
            float at0 = stat.CurrentValue;
            stat.level = 3;
            Assert.AreEqual(at0 + 3f * stat.perLevelAdd, stat.CurrentValue, 1e-5);
        }

        [Test]
        public void ApplyUpgrade_IncrementsOnlyTargetStat_FiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;
            _stats.ApplyUpgrade(DroneStatId.Thrust);
            Assert.AreEqual(1, _stats.thrust.level);
            Assert.AreEqual(0, _stats.batteryCapacity.level);
            Assert.AreEqual(0, _stats.cargoCapacity.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void Get_ReturnsCorrectStat()
        {
            Assert.AreSame(_stats.thrust,          _stats.Get(DroneStatId.Thrust));
            Assert.AreSame(_stats.batteryCapacity, _stats.Get(DroneStatId.BatteryCapacity));
            Assert.AreSame(_stats.cargoCapacity,   _stats.Get(DroneStatId.CargoCapacity));
        }

        [Test]
        public void ResetRuntime_ZerosAllLevels()
        {
            _stats.ApplyUpgrade(DroneStatId.Thrust);
            _stats.ApplyUpgrade(DroneStatId.CargoCapacity);
            _stats.ResetRuntime();
            Assert.AreEqual(0, _stats.thrust.level);
            Assert.AreEqual(0, _stats.cargoCapacity.level);
        }

        [Test]
        public void TotalSpentOnUpgrades_MatchesSumOfNextCostAcrossLevels()
        {
            int expected = 0;
            for (int i = 0; i < 3; i++) { expected += _stats.thrust.NextCost; _stats.ApplyUpgrade(DroneStatId.Thrust); }
            Assert.AreEqual(expected, _stats.TotalSpentOnUpgrades());
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStatsTests
```

Expected: compile error — `DroneStats` missing.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/DroneStats.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public enum DroneStatId
{
    Thrust = 0,
    BatteryCapacity = 1,
    CargoCapacity = 2,
}

[CreateAssetMenu(fileName = "DroneStats", menuName = "Meteor Idle/Drone Stats")]
public class DroneStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public DroneStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1.6f;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
    }

    public Stat thrust          = new Stat { id = DroneStatId.Thrust,          displayName = "Thrust",           baseValue = 4f,  perLevelAdd = 1.0f, baseCost = 25 };
    public Stat batteryCapacity = new Stat { id = DroneStatId.BatteryCapacity, displayName = "Battery Capacity", baseValue = 10f, perLevelAdd = 3f,   baseCost = 30 };
    public Stat cargoCapacity   = new Stat { id = DroneStatId.CargoCapacity,   displayName = "Cargo Capacity",   baseValue = 1f,  perLevelAdd = 1f,   baseCost = 50 };

    public event Action OnChanged;

    public Stat Get(DroneStatId id)
    {
        switch (id)
        {
            case DroneStatId.Thrust: return thrust;
            case DroneStatId.BatteryCapacity: return batteryCapacity;
            case DroneStatId.CargoCapacity: return cargoCapacity;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return thrust;
        yield return batteryCapacity;
        yield return cargoCapacity;
    }

    public void ApplyUpgrade(DroneStatId id)
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

    public int TotalSpentOnUpgrades()
    {
        int total = 0;
        foreach (var s in All())
            for (int lv = 0; lv < s.level; lv++)
                total += Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, lv));
        return total;
    }
}
```

Create the asset:

```
mcp__UnityMCP__execute_code code=<<EOF
var asset = UnityEngine.ScriptableObject.CreateInstance<DroneStats>();
UnityEditor.AssetDatabase.CreateAsset(asset, "Assets/Data/DroneStats.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("[Iter3] DroneStats.asset created");
EOF
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneStatsTests
```

Expected: 6 passed.

- [ ] **Step 5: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/DroneStats.cs Assets/Data/DroneStats.asset \
        Assets/Data/DroneStats.asset.meta Assets/Tests/EditMode/DroneStatsTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase6: DroneStats SO + asset + EditMode tests"
```

---

### Task 6.2: `BayStats.cs` + `BayStats.asset` + tests

**Files:**
- Create: `Assets/Scripts/Drones/BayStats.cs`
- Create: `Assets/Data/BayStats.asset`
- Test: `Assets/Tests/EditMode/BayStatsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class BayStatsTests
    {
        private BayStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<BayStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
        }

        [Test]
        public void DronesPerBay_MaxLevelTwo()
        {
            Assert.AreEqual(2, _stats.dronesPerBay.maxLevel);
        }

        [Test]
        public void DronesPerBay_CannotExceedMaxLevel()
        {
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            Assert.AreEqual(2, _stats.dronesPerBay.level);
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            Assert.AreEqual(2, _stats.dronesPerBay.level, "IsMaxed blocks further upgrades");
        }

        [Test]
        public void ReloadSpeed_UncappedByDefault()
        {
            Assert.AreEqual(0, _stats.reloadSpeed.maxLevel);
        }

        [Test]
        public void NextCost_MatchesGrowthFormula()
        {
            var s = _stats.reloadSpeed;
            Assert.AreEqual(s.baseCost, s.NextCost);
            s.level = 2;
            int expected = Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, 2));
            Assert.AreEqual(expected, s.NextCost);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var s = _stats.reloadSpeed;
            float at0 = s.CurrentValue;
            s.level = 4;
            Assert.AreEqual(at0 + 4f * s.perLevelAdd, s.CurrentValue, 1e-5);
        }

        [Test]
        public void ApplyUpgrade_IncrementsOnlyTargetStat_FiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;
            _stats.ApplyUpgrade(BayStatId.ReloadSpeed);
            Assert.AreEqual(1, _stats.reloadSpeed.level);
            Assert.AreEqual(0, _stats.dronesPerBay.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void TotalSpentOnUpgrades_MatchesSumOfNextCostAcrossLevels()
        {
            int expected = 0;
            for (int i = 0; i < 3; i++) { expected += _stats.reloadSpeed.NextCost; _stats.ApplyUpgrade(BayStatId.ReloadSpeed); }
            Assert.AreEqual(expected, _stats.TotalSpentOnUpgrades());
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=BayStatsTests
```

Expected: compile error — `BayStats` missing.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/BayStats.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public enum BayStatId
{
    ReloadSpeed = 0,
    DronesPerBay = 1,
}

[CreateAssetMenu(fileName = "BayStats", menuName = "Meteor Idle/Bay Stats")]
public class BayStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public BayStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1.6f;
        public int maxLevel; // 0 = uncapped
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
        public bool IsMaxed => maxLevel > 0 && level >= maxLevel;
    }

    public Stat reloadSpeed  = new Stat { id = BayStatId.ReloadSpeed,  displayName = "Reload Speed",   baseValue = 1f, perLevelAdd = 0.25f, baseCost = 40, maxLevel = 0 };
    public Stat dronesPerBay = new Stat { id = BayStatId.DronesPerBay, displayName = "Drones Per Bay", baseValue = 1f, perLevelAdd = 1f,    baseCost = 150, maxLevel = 2 };

    public event Action OnChanged;

    public Stat Get(BayStatId id)
    {
        switch (id)
        {
            case BayStatId.ReloadSpeed: return reloadSpeed;
            case BayStatId.DronesPerBay: return dronesPerBay;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return reloadSpeed;
        yield return dronesPerBay;
    }

    public void ApplyUpgrade(BayStatId id)
    {
        var stat = Get(id);
        if (stat == null) return;
        if (stat.IsMaxed) return;
        stat.level++;
        OnChanged?.Invoke();
    }

    public void ResetRuntime()
    {
        foreach (var s in All()) s.level = 0;
        OnChanged?.Invoke();
    }

    public int TotalSpentOnUpgrades()
    {
        int total = 0;
        foreach (var s in All())
            for (int lv = 0; lv < s.level; lv++)
                total += Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, lv));
        return total;
    }
}
```

Create the asset:

```
mcp__UnityMCP__execute_code code=<<EOF
var asset = UnityEngine.ScriptableObject.CreateInstance<BayStats>();
UnityEditor.AssetDatabase.CreateAsset(asset, "Assets/Data/BayStats.asset");
UnityEditor.AssetDatabase.SaveAssets();
UnityEngine.Debug.Log("[Iter3] BayStats.asset created");
EOF
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=BayStatsTests
```

Expected: 7 passed.

- [ ] **Step 5: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/BayStats.cs Assets/Data/BayStats.asset \
        Assets/Data/BayStats.asset.meta Assets/Tests/EditMode/BayStatsTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase6: BayStats SO + asset + EditMode tests"
```

---

## Phase 7 — Drone visual (plus-shape sprite + thruster trails)

Procedural 21×21 PNG. Body grey, arm tips red. `ThrusterTrail.cs` emits tiny voxel-style particles on thrust. Prefab wires child transforms. Explicit visual verify pause before commit.

### Task 7.1: Generate `CollectorDrone.png` procedurally

**Files:**
- Create: `Assets/Art/CollectorDrone.png`

- [ ] **Step 1: Generate the sprite**

```
mcp__UnityMCP__execute_code code=<<EOF
int W = 21;
var tex = new UnityEngine.Texture2D(W, W);
tex.filterMode = UnityEngine.FilterMode.Point;
var clear = new UnityEngine.Color(0f, 0f, 0f, 0f);
for (int y = 0; y < W; y++)
    for (int x = 0; x < W; x++)
        tex.SetPixel(x, y, clear);

var body = new UnityEngine.Color(0.55f, 0.55f, 0.60f, 1f);
var bodyEdge = new UnityEngine.Color(0.30f, 0.30f, 0.35f, 1f);
var tip = new UnityEngine.Color(0.95f, 0.20f, 0.15f, 1f);
var tipEdge = new UnityEngine.Color(0.50f, 0.08f, 0.05f, 1f);

void FillRect(int x0, int y0, int w, int h, UnityEngine.Color fill, UnityEngine.Color edge)
{
    for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            bool onEdge = (x == x0 || y == y0 || x == x0 + w - 1 || y == y0 + h - 1);
            tex.SetPixel(x, y, onEdge ? edge : fill);
        }
}

// Center body cell (3x3 around center 10,10).
FillRect(9, 9, 3, 3, body, bodyEdge);

// 4 arms, each 3 wide × 6 long extending outward.
FillRect(9, 3, 3, 6, body, bodyEdge);     // down arm
FillRect(9, 12, 3, 6, body, bodyEdge);    // up arm
FillRect(3, 9, 6, 3, body, bodyEdge);     // left arm
FillRect(12, 9, 6, 3, body, bodyEdge);    // right arm

// Red tips at each arm end (2 cells deep).
FillRect(9, 3, 3, 2, tip, tipEdge);
FillRect(9, 16, 3, 2, tip, tipEdge);
FillRect(3, 9, 2, 3, tip, tipEdge);
FillRect(16, 9, 2, 3, tip, tipEdge);

tex.Apply();
var png = tex.EncodeToPNG();
System.IO.File.WriteAllBytes("Assets/Art/CollectorDrone.png", png);
UnityEditor.AssetDatabase.ImportAsset("Assets/Art/CollectorDrone.png");
var importer = (UnityEditor.TextureImporter)UnityEditor.TextureImporter.GetAtPath("Assets/Art/CollectorDrone.png");
importer.textureType = UnityEditor.TextureImporterType.Sprite;
importer.filterMode = UnityEngine.FilterMode.Point;
importer.spritePixelsPerUnit = 30f;
importer.SaveAndReimport();
UnityEngine.Debug.Log("[Iter3] CollectorDrone.png generated");
EOF
```

- [ ] **Step 2: VISUAL VERIFY PAUSE — ask the user to inspect**

Open the PNG via the Read tool:

```
Read Assets/Art/CollectorDrone.png
```

Then pause with: "Please eyeball the drone sprite. Voxel aesthetic (hard edges, 1-px dark outline, red thruster tips)? Reply 'approved' to continue, or tell me what to tweak." Do NOT advance until the user responds.

- [ ] **Step 3: Apply any tweaks requested by the user and re-pause.** Repeat until approved.

- [ ] **Step 4: Identity scrub + commit**

```bash
git add Assets/Art/CollectorDrone.png Assets/Art/CollectorDrone.png.meta
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase7: procedural CollectorDrone.png sprite"
```

---

### Task 7.2: `ThrusterTrail.cs` voxel particle emitter

**Files:**
- Create: `Assets/Scripts/Drones/ThrusterTrail.cs`

- [ ] **Step 1: Implement — no test (pure visual)**

```csharp
using UnityEngine;

// Tiny voxel-aesthetic particle trail for a drone arm tip. Emits square
// white quads that fade over ~0.3 seconds. Not a TrailRenderer (smooth lines
// violate the voxel rule). Emission rate scales with parent body speed.
[RequireComponent(typeof(SpriteRenderer))]
public class ThrusterTrail : MonoBehaviour
{
    [SerializeField] private CollectorDrone owner;
    [SerializeField] private float emitIntervalAtFullThrust = 0.05f;
    [SerializeField] private float particleLifetime = 0.3f;
    [SerializeField] private Sprite particleSprite;

    private float emitTimer;

    private void Update()
    {
        if (owner == null || owner.Body == null) return;
        float velocityMagnitude = owner.Body.Velocity.magnitude;
        if (velocityMagnitude < 0.1f) return;
        float interval = emitIntervalAtFullThrust
            * Mathf.Max(0.2f, owner.Body.ThrustCap / Mathf.Max(0.01f, velocityMagnitude));
        emitTimer += Time.deltaTime;
        if (emitTimer < interval) return;
        emitTimer = 0f;
        Emit();
    }

    private void Emit()
    {
        var go = new GameObject("TrailParticle");
        go.transform.position = transform.position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = particleSprite;
        sr.color = Color.white;
        sr.sortingOrder = 3;
        var p = go.AddComponent<TrailParticle>();
        p.lifetime = particleLifetime;
    }
}

// Self-destructing particle: square sprite fades from 1 → 0 alpha and
// destroys itself at lifetime. Inline MonoBehaviour keeps the file self
// contained.
public class TrailParticle : MonoBehaviour
{
    public float lifetime = 0.3f;
    private float t;
    private SpriteRenderer sr;
    private void Awake() { sr = GetComponent<SpriteRenderer>(); }
    private void Update()
    {
        t += Time.deltaTime;
        if (sr != null)
        {
            var c = sr.color;
            c.a = Mathf.Clamp01(1f - t / lifetime);
            sr.color = c;
        }
        if (t >= lifetime) Destroy(gameObject);
    }
}
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

- [ ] **Step 3: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 4: Identity scrub + commit**

```bash
git add Assets/Scripts/Drones/ThrusterTrail.cs
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase7: ThrusterTrail voxel particle emitter"
```

---

### Task 7.3: `CollectorDrone.prefab` — wire sprite + trail children

**Files:**
- Create: `Assets/Prefabs/CollectorDrone.prefab` (via execute_code)

- [ ] **Step 1: Build the prefab**

```
mcp__UnityMCP__execute_code code=<<EOF
var go = new UnityEngine.GameObject("CollectorDrone");
var sr = go.AddComponent<UnityEngine.SpriteRenderer>();
sr.sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/CollectorDrone.png");
sr.sortingOrder = 7;
var col = go.AddComponent<UnityEngine.CircleCollider2D>();
col.radius = 0.25f;
col.isTrigger = true;
var drone = go.AddComponent<CollectorDrone>();

// Build a 1x1 white pixel sprite for trail particles.
var pTex = new UnityEngine.Texture2D(1, 1) { filterMode = UnityEngine.FilterMode.Point };
pTex.SetPixel(0, 0, UnityEngine.Color.white);
pTex.Apply();
var pPng = pTex.EncodeToPNG();
System.IO.File.WriteAllBytes("Assets/Art/TrailPixel.png", pPng);
UnityEditor.AssetDatabase.ImportAsset("Assets/Art/TrailPixel.png");
var pImporter = (UnityEditor.TextureImporter)UnityEditor.TextureImporter.GetAtPath("Assets/Art/TrailPixel.png");
pImporter.textureType = UnityEditor.TextureImporterType.Sprite;
pImporter.filterMode = UnityEngine.FilterMode.Point;
pImporter.spritePixelsPerUnit = 30f;
pImporter.SaveAndReimport();
var pSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/TrailPixel.png");

// 4 thruster children at the arm tips (local coords based on 21px sprite @ 30ppu = 0.7 world units across).
System.Action<string, UnityEngine.Vector3> MakeTrail = (name, localPos) => {
    var child = new UnityEngine.GameObject(name);
    child.transform.SetParent(go.transform, false);
    child.transform.localPosition = localPos;
    var csr = child.AddComponent<UnityEngine.SpriteRenderer>();
    csr.enabled = false;
    var trail = child.AddComponent<ThrusterTrail>();
    var so = new UnityEditor.SerializedObject(trail);
    so.FindProperty("owner").objectReferenceValue = drone;
    so.FindProperty("particleSprite").objectReferenceValue = pSprite;
    so.ApplyModifiedPropertiesWithoutUndo();
};
MakeTrail("ThrusterTrail_Down",  new UnityEngine.Vector3(0f, -0.3f, 0f));
MakeTrail("ThrusterTrail_Up",    new UnityEngine.Vector3(0f,  0.3f, 0f));
MakeTrail("ThrusterTrail_Left",  new UnityEngine.Vector3(-0.3f, 0f, 0f));
MakeTrail("ThrusterTrail_Right", new UnityEngine.Vector3(0.3f,  0f, 0f));

UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/CollectorDrone.prefab");
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Debug.Log("[Iter3] CollectorDrone.prefab created");
EOF
```

- [ ] **Step 2: Full suite + compile check**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all green.

- [ ] **Step 3: Identity scrub + commit**

```bash
git add Assets/Prefabs/CollectorDrone.prefab Assets/Art/TrailPixel.png Assets/Art/TrailPixel.png.meta
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase7: CollectorDrone.prefab with 4 thruster-trail children"
```

---

## Phase 8 — DroneBay + quantized door animation

Procedural bay body + single door sprite. `DroneBay.cs` has `DoorState` enum with discrete timer-driven steps. PlayMode test asserts the 4 keyframe rotations fire in order. Visual verify pause.

### Task 8.1: Generate bay body + door PNGs

**Files:**
- Create: `Assets/Art/DroneBay.png`
- Create: `Assets/Art/DroneBayDoor.png`

- [ ] **Step 1: Generate sprites**

```
mcp__UnityMCP__execute_code code=<<EOF
// Bay body: 30x24 dark metallic box with open top.
int BW = 30, BH = 24;
var body = new UnityEngine.Texture2D(BW, BH);
body.filterMode = UnityEngine.FilterMode.Point;
var fill = new UnityEngine.Color(0.35f, 0.38f, 0.42f, 1f);
var edge = new UnityEngine.Color(0.15f, 0.15f, 0.18f, 1f);
var clear = new UnityEngine.Color(0f, 0f, 0f, 0f);
for (int y = 0; y < BH; y++)
    for (int x = 0; x < BW; x++)
    {
        // Leave the top 4 rows clear so doors close over them.
        if (y >= BH - 4) { body.SetPixel(x, y, clear); continue; }
        bool onEdge = (x == 0 || x == BW - 1 || y == 0);
        body.SetPixel(x, y, onEdge ? edge : fill);
    }
body.Apply();
System.IO.File.WriteAllBytes("Assets/Art/DroneBay.png", body.EncodeToPNG());

// Door: 14x4 single door (two per bay, mirrored).
int DW = 14, DH = 4;
var door = new UnityEngine.Texture2D(DW, DH);
door.filterMode = UnityEngine.FilterMode.Point;
for (int y = 0; y < DH; y++)
    for (int x = 0; x < DW; x++)
    {
        bool onEdge = (x == 0 || x == DW - 1 || y == 0 || y == DH - 1);
        door.SetPixel(x, y, onEdge ? edge : fill);
    }
door.Apply();
System.IO.File.WriteAllBytes("Assets/Art/DroneBayDoor.png", door.EncodeToPNG());

foreach (var p in new[] { "Assets/Art/DroneBay.png", "Assets/Art/DroneBayDoor.png" })
{
    UnityEditor.AssetDatabase.ImportAsset(p);
    var imp = (UnityEditor.TextureImporter)UnityEditor.TextureImporter.GetAtPath(p);
    imp.textureType = UnityEditor.TextureImporterType.Sprite;
    imp.filterMode = UnityEngine.FilterMode.Point;
    imp.spritePixelsPerUnit = 30f;
    imp.SaveAndReimport();
}
UnityEngine.Debug.Log("[Iter3] Drone bay sprites generated");
EOF
```

- [ ] **Step 2: VISUAL VERIFY PAUSE**

Open both PNGs via Read and pause: "Please inspect `DroneBay.png` and `DroneBayDoor.png`. Voxel aesthetic — dark metallic box, hard edges, 1-px dark outline? Reply 'approved' to continue, or tell me what to tweak." Wait for explicit approval.

- [ ] **Step 3: Apply tweaks, re-pause. Repeat until approved.**

- [ ] **Step 4: Identity scrub + commit**

```bash
git add Assets/Art/DroneBay.png Assets/Art/DroneBay.png.meta \
        Assets/Art/DroneBayDoor.png Assets/Art/DroneBayDoor.png.meta
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase8: procedural DroneBay + door sprites"
```

---

### Task 8.2: `DroneBay.cs` — doors state + 4-keyframe animation

**Files:**
- Create: `Assets/Scripts/Drones/DroneBay.cs`
- Test: `Assets/Tests/EditMode/DroneBayDoorsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneBayDoorsTests
    {
        private DroneBay MakeBay()
        {
            var go = new GameObject("TestBay", typeof(DroneBay));
            var leftGo = new GameObject("LeftDoor", typeof(SpriteRenderer));
            leftGo.transform.SetParent(go.transform, false);
            var rightGo = new GameObject("RightDoor", typeof(SpriteRenderer));
            rightGo.transform.SetParent(go.transform, false);
            var bay = go.GetComponent<DroneBay>();
            var so = new UnityEditor.SerializedObject(bay);
            so.FindProperty("leftDoor").objectReferenceValue = leftGo.transform;
            so.FindProperty("rightDoor").objectReferenceValue = rightGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            TestHelpers.InvokeAwake(bay);
            return bay;
        }

        [Test]
        public void StartsInClosedState_DoorsFlat()
        {
            var bay = MakeBay();
            Assert.AreEqual(DroneBay.DoorState.Closed, bay.Doors);
            Assert.IsFalse(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void RequestOpenDoors_SteppedThrough4Keyframes()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            Assert.AreEqual(DroneBay.DoorState.Opening, bay.Doors);

            // Step past the opening timer (use a small dt each tick).
            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            Assert.AreEqual(DroneBay.DoorState.Open, bay.Doors);
            Assert.IsTrue(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void RequestCloseDoors_FromOpen_SteppedThroughClosingBackToClosed()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            bay.RequestCloseDoors();
            Assert.AreEqual(DroneBay.DoorState.Closing, bay.Doors);
            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            Assert.AreEqual(DroneBay.DoorState.Closed, bay.Doors);
            Assert.IsFalse(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void DoorKeyframe_RotationsAreQuantized_NoLerp()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            // Snapshot rotations across the transition; each observed Z must
            // equal one of the 4 allowed values (0, 45, 90, 45-back) — no
            // intermediate lerps.
            var seenLeft = new System.Collections.Generic.HashSet<float>();
            for (int i = 0; i < 40; i++)
            {
                bay.Tick(0.025f);
                seenLeft.Add(Mathf.Round(bay.LeftDoorLocalRotationZ));
            }
            foreach (var z in seenLeft)
                Assert.IsTrue(z == 0f || z == 45f || z == 90f,
                    $"left door rotation {z} is not one of the 4 keyframes (0/45/90)");
            Object.DestroyImmediate(bay.gameObject);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBayDoorsTests
```

Expected: compile error — `DroneBay` missing.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/DroneBay.cs`:

```csharp
using UnityEngine;

// A single drone bay. Owns: the bay body sprite, two door children animated
// through 4 quantized keyframes, and (Phase 9) the CollectorDrone children
// that launch from it. Implements ICollectorDroneEnvironment so the drones
// living inside it can treat it as their world environment without knowing
// about GameManager or MeteorSpawner directly.
public class DroneBay : MonoBehaviour, ICollectorDroneEnvironment
{
    public enum DoorState
    {
        Closed  = 0,
        Opening = 1,
        Open    = 2,
        Closing = 3,
    }

    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField] private float openingDuration = 0.4f;
    [SerializeField] private float closingDuration = 0.4f;

    public DoorState Doors { get; private set; } = DoorState.Closed;
    public bool IsOpen => Doors == DoorState.Open;
    public Vector3 BayPosition => transform.position;
    public bool BayDoorsOpen => IsOpen;

    // Exposed for tests — discrete rotation of the left door around Z.
    public float LeftDoorLocalRotationZ => leftDoor != null
        ? leftDoor.localRotation.eulerAngles.z : 0f;

    private float doorTimer;

    // Keyframes per state. Left door rotates negative (counterclockwise),
    // right door positive. Values chosen for clear discrete snap at 0/45/90.
    private static readonly float[] LeftOpenKeyframes   = { 0f, 45f, 90f };
    private static readonly float[] LeftClosingKeyframes = { 90f, 45f, 0f };

    private void Awake()
    {
        ApplyDoorRotation(0f);
    }

    public void Tick(float dt)
    {
        if (Doors == DoorState.Opening || Doors == DoorState.Closing)
        {
            doorTimer += dt;
            StepAnimation();
        }
    }

    private void Update() { Tick(Time.deltaTime); }

    public void RequestOpenDoors()
    {
        if (Doors == DoorState.Open || Doors == DoorState.Opening) return;
        Doors = DoorState.Opening;
        doorTimer = 0f;
        ApplyDoorRotation(LeftOpenKeyframes[0]);
    }

    public void RequestCloseDoors()
    {
        if (Doors == DoorState.Closed || Doors == DoorState.Closing) return;
        Doors = DoorState.Closing;
        doorTimer = 0f;
        ApplyDoorRotation(LeftClosingKeyframes[0]);
    }

    private void StepAnimation()
    {
        if (Doors == DoorState.Opening)
        {
            float third = openingDuration / 3f;
            int idx = Mathf.Clamp(Mathf.FloorToInt(doorTimer / third), 0, LeftOpenKeyframes.Length - 1);
            ApplyDoorRotation(LeftOpenKeyframes[idx]);
            if (doorTimer >= openingDuration)
            {
                ApplyDoorRotation(LeftOpenKeyframes[LeftOpenKeyframes.Length - 1]);
                Doors = DoorState.Open;
            }
        }
        else if (Doors == DoorState.Closing)
        {
            float third = closingDuration / 3f;
            int idx = Mathf.Clamp(Mathf.FloorToInt(doorTimer / third), 0, LeftClosingKeyframes.Length - 1);
            ApplyDoorRotation(LeftClosingKeyframes[idx]);
            if (doorTimer >= closingDuration)
            {
                ApplyDoorRotation(LeftClosingKeyframes[LeftClosingKeyframes.Length - 1]);
                Doors = DoorState.Closed;
            }
        }
    }

    // Negative rotation for left door, positive for right — snapped, no lerp.
    private void ApplyDoorRotation(float magnitude)
    {
        if (leftDoor != null)  leftDoor.localRotation  = Quaternion.Euler(0f, 0f, magnitude);
        if (rightDoor != null) rightDoor.localRotation = Quaternion.Euler(0f, 0f, -magnitude);
    }

    // Phase 9 wires these up to GameManager + child drones + cargo deposit.
    public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
    {
        if (GameManager.Instance == null) return null;
        CoreDrop best = null;
        float bestD = float.MaxValue;
        foreach (var d in GameManager.Instance.ActiveDrops)
        {
            if (d == null || d.IsClaimed || !d.IsAlive) continue;
            float dist = Vector3.Distance(from, d.Position);
            if (dist > maxDistance) continue;
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    public void Deposit(int value)
    {
        if (GameManager.Instance != null) GameManager.Instance.AddMoney(value);
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=DroneBayDoorsTests
```

Expected: 4 passed.

- [ ] **Step 5: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/DroneBay.cs Assets/Tests/EditMode/DroneBayDoorsTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase8: DroneBay + 4-keyframe quantized door animation"
```

---

### Task 8.3: `DroneBay.prefab` — body + 2 door children + click collider

**Files:**
- Create: `Assets/Prefabs/DroneBay.prefab`

- [ ] **Step 1: Build the prefab**

```
mcp__UnityMCP__execute_code code=<<EOF
var go = new UnityEngine.GameObject("DroneBay");
var sr = go.AddComponent<UnityEngine.SpriteRenderer>();
sr.sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/DroneBay.png");
sr.sortingOrder = 2;
var col = go.AddComponent<UnityEngine.BoxCollider2D>();
col.size = new UnityEngine.Vector2(1f, 0.8f);
var bay = go.AddComponent<DroneBay>();

var doorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>("Assets/Art/DroneBayDoor.png");

var leftGo = new UnityEngine.GameObject("LeftDoor");
leftGo.transform.SetParent(go.transform, false);
leftGo.transform.localPosition = new UnityEngine.Vector3(-0.25f, 0.35f, 0f);
var lsr = leftGo.AddComponent<UnityEngine.SpriteRenderer>();
lsr.sprite = doorSprite;
lsr.sortingOrder = 3;

var rightGo = new UnityEngine.GameObject("RightDoor");
rightGo.transform.SetParent(go.transform, false);
rightGo.transform.localPosition = new UnityEngine.Vector3(0.25f, 0.35f, 0f);
var rsr = rightGo.AddComponent<UnityEngine.SpriteRenderer>();
rsr.sprite = doorSprite;
rsr.sortingOrder = 3;

var so = new UnityEditor.SerializedObject(bay);
so.FindProperty("leftDoor").objectReferenceValue = leftGo.transform;
so.FindProperty("rightDoor").objectReferenceValue = rightGo.transform;
so.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/DroneBay.prefab");
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Debug.Log("[Iter3] DroneBay.prefab created");
EOF
```

- [ ] **Step 2: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 3: Identity scrub + commit**

```bash
git add Assets/Prefabs/DroneBay.prefab
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase8: DroneBay.prefab with body + 2 door children"
```

---

## Phase 9 — BayManager + UI (DroneUpgradePanel + BuildBayPanel + layout shift)

`BayManager.cs` mirrors `SlotManager.cs`. 3 bays in a row to the right of the weapon row. Start with 1 built + 1 drone, 2 empty placeholders. Escalating build cost. `DroneUpgradePanel` mirrors `MissileUpgradePanel` two-column layout. `UpgradeButton.BindBay` / `BindDrone`. `BuildBayPanel` mirrors `BuildSlotPanel`.

### Task 9.1: `BayManager.cs` + `BayManagerBuildCostTests`

**Files:**
- Create: `Assets/Scripts/Drones/BayManager.cs`
- Test: `Assets/Tests/EditMode/BayManagerBuildCostTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class BayManagerBuildCostTests
    {
        private BayManager MakeManager(int[] costs)
        {
            var go = new GameObject("TestBayManager", typeof(BayManager));
            var mgr = go.GetComponent<BayManager>();
            typeof(BayManager)
                .GetField("bayBuildCosts", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(mgr, costs);
            return mgr;
        }

        [Test]
        public void NextBuildCost_UsesFirstEntryWhenUnpurchased()
        {
            var m = MakeManager(new[] { 200, 600 });
            Assert.AreEqual(200, m.NextBuildCost());
            Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void NextBuildCost_AdvancesWithPurchaseCount()
        {
            var m = MakeManager(new[] { 200, 600 });
            typeof(BayManager)
                .GetField("purchasedCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m, 1);
            Assert.AreEqual(600, m.NextBuildCost());
            Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void NextBuildCost_AfterTable_OverflowsFromLastEntry()
        {
            var m = MakeManager(new[] { 200, 600 });
            typeof(BayManager)
                .GetField("purchasedCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m, 2);
            int cost = m.NextBuildCost();
            Assert.Greater(cost, 600);
            Object.DestroyImmediate(m.gameObject);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=BayManagerBuildCostTests
```

Expected: compile error.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Drones/BayManager.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

public class BayManager : MonoBehaviour
{
    [SerializeField] private DroneBay bayPrefab;
    [SerializeField] private CollectorDrone dronePrefab;
    [SerializeField] private int bayCount = 3;
    [SerializeField] private float bayY = -8.26f;
    [SerializeField] private float bayStartX = 6f;
    [SerializeField] private float baySpacing = 2.5f;
    [SerializeField] private int prebuiltIndex = 0;
    [SerializeField] private int[] bayBuildCosts = { 200, 600 };

    [SerializeField] private DroneUpgradePanel upgradePanel;
    [SerializeField] private BuildBayPanel buildPanel;
    [SerializeField] private MeteorSpawner meteorSpawner;

    [SerializeField] private DroneStats droneStats;
    [SerializeField] private BayStats bayStats;

    private int purchasedCount;
    private readonly List<DroneBay> bays = new List<DroneBay>();

    private void Start()
    {
        if (bayPrefab == null) { Debug.LogError("[BayManager] bayPrefab not assigned", this); return; }
        for (int i = 0; i < bayCount; i++)
        {
            var pos = new Vector3(bayStartX + i * baySpacing, bayY, 0f);
            var bay = Instantiate(bayPrefab, pos, Quaternion.identity, transform);
            bays.Add(bay);
            if (i == prebuiltIndex)
            {
                SpawnDroneFor(bay);
            }
            else
            {
                // Empty-state handling (shows + placeholder). Phase 10 polish
                // can extend this — for now, an inactive bay body.
                bay.gameObject.SetActive(false);
            }
        }
    }

    private void SpawnDroneFor(DroneBay bay)
    {
        if (dronePrefab == null) return;
        var drone = Instantiate(dronePrefab, bay.transform.position, Quaternion.identity, bay.transform);
        float thrust = droneStats != null ? droneStats.thrust.CurrentValue : 4f;
        float battery = droneStats != null ? droneStats.batteryCapacity.CurrentValue : 10f;
        int cargo = droneStats != null ? Mathf.RoundToInt(droneStats.cargoCapacity.CurrentValue) : 1;
        drone.Initialize(
            env: bay,
            thrust: thrust,
            damping: 1f,
            batteryCapacity: battery,
            cargoCapacity: cargo,
            reserveThresholdFraction: 0.4f,
            pickupRadius: 0.35f,
            dockRadius: 0.45f);
        drone.SetMeteorSpawner(meteorSpawner);
    }

    // Per-bay cost lookup. purchasedCount is shared across the 2 purchasable
    // slots so the Nth bay always costs `bayBuildCosts[N-1]`.
    public int NextBuildCost()
    {
        if (bayBuildCosts == null || bayBuildCosts.Length == 0) return 0;
        if (purchasedCount < bayBuildCosts.Length) return bayBuildCosts[purchasedCount];
        int overflow = purchasedCount - bayBuildCosts.Length + 2;
        return bayBuildCosts[bayBuildCosts.Length - 1] * overflow;
    }

    public DroneStats DroneStats => droneStats;
    public BayStats BayStats => bayStats;
}
```

- [ ] **Step 4: Run test, expect PASS**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=BayManagerBuildCostTests
```

Expected: 3 passed.

- [ ] **Step 5: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green.

- [ ] **Step 6: Identity scrub**

```bash
git add Assets/Scripts/Drones/BayManager.cs Assets/Tests/EditMode/BayManagerBuildCostTests.cs
python3 tools/identity-scrub.py
```

- [ ] **Step 7: Commit**

```bash
git commit -m "Iter3 Phase9: BayManager + build-cost tests"
```

---

### Task 9.2: Extend `UpgradeButton` with `BindBay` / `BindDrone`

**Files:**
- Modify: `Assets/Scripts/UI/UpgradeButton.cs`

- [ ] **Step 1: Extend the script**

Add two new state blocks alongside the existing missile/railgun bindings:

```csharp
// Drone stat binding
private DroneStats droneStats;
private DroneStatId droneStatId;
private System.Action<DroneStatId> onDroneClick;

// Bay stat binding
private BayStats bayStats;
private BayStatId bayStatId;
private System.Action<BayStatId> onBayClick;

public void BindDrone(DroneStats stats, DroneStatId statId, System.Action<DroneStatId> onClick)
{
    droneStats = stats;
    droneStatId = statId;
    onDroneClick = onClick;
    missileStats = null; onMissileClick = null;
    railgunStats = null; onRailgunClick = null;
    bayStats = null; onBayClick = null;
    WireButtonClick();
}

public void BindBay(BayStats stats, BayStatId statId, System.Action<BayStatId> onClick)
{
    bayStats = stats;
    bayStatId = statId;
    onBayClick = onClick;
    missileStats = null; onMissileClick = null;
    railgunStats = null; onRailgunClick = null;
    droneStats = null; onDroneClick = null;
    WireButtonClick();
}
```

Update `WireButtonClick` to also dispatch to drone/bay handlers, and update `Refresh(int money)` to handle the new bindings:

```csharp
private void WireButtonClick()
{
    if (button == null) return;
    button.onClick.RemoveAllListeners();
    button.onClick.AddListener(() => {
        if (onMissileClick != null) onMissileClick.Invoke(missileStatId);
        else if (onRailgunClick != null) onRailgunClick.Invoke(railgunStatId);
        else if (onDroneClick != null) onDroneClick.Invoke(droneStatId);
        else if (onBayClick != null) onBayClick.Invoke(bayStatId);
    });
}

public void Refresh(int money)
{
    if (label == null) return;
    if (missileStats != null)
    {
        var stat = missileStats.Get(missileStatId);
        if (stat == null) return;
        label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
        if (button != null) button.interactable = money >= stat.NextCost;
    }
    else if (railgunStats != null)
    {
        var stat = railgunStats.Get(railgunStatId);
        if (stat == null) return;
        if (stat.IsMaxed)
        {
            label.text = $"{stat.displayName}\nLvl {stat.level} — MAX";
            if (button != null) button.interactable = false;
        }
        else
        {
            label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
            if (button != null) button.interactable = money >= stat.NextCost;
        }
    }
    else if (droneStats != null)
    {
        var stat = droneStats.Get(droneStatId);
        if (stat == null) return;
        label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
        if (button != null) button.interactable = money >= stat.NextCost;
    }
    else if (bayStats != null)
    {
        var stat = bayStats.Get(bayStatId);
        if (stat == null) return;
        if (stat.IsMaxed)
        {
            label.text = $"{stat.displayName}\nLvl {stat.level} — MAX";
            if (button != null) button.interactable = false;
        }
        else
        {
            label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
            if (button != null) button.interactable = money >= stat.NextCost;
        }
    }
}
```

- [ ] **Step 2: Compile + full suite**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: zero errors, all green.

- [ ] **Step 3: Identity scrub + commit**

```bash
git add Assets/Scripts/UI/UpgradeButton.cs
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase9: UpgradeButton.BindBay + BindDrone"
```

---

### Task 9.3: `DroneUpgradePanel.cs` + scene GameObject

**Files:**
- Create: `Assets/Scripts/UI/DroneUpgradePanel.cs`
- Scene: add `DroneUpgradePanel` GameObject under the Canvas (via execute_code)

- [ ] **Step 1: Implement**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

// Global upgrade panel for drones + bays. Two columns: BAY (ReloadSpeed,
// DronesPerBay) on the left, DRONE (Thrust, BatteryCapacity, CargoCapacity)
// on the right. All stats are fleet-wide per spec §6.
public class DroneUpgradePanel : MonoBehaviour
{
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform bayColumnParent;
    [SerializeField] private Transform droneColumnParent;
    [SerializeField] private BayManager bayManager;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;
    private Action OnStatsChanged;

    private void Start()
    {
        if (buttonPrefab == null) { Debug.LogError("[DroneUpgradePanel] buttonPrefab missing", this); return; }
        if (bayManager == null)   { Debug.LogError("[DroneUpgradePanel] bayManager missing", this); return; }

        // BAY column
        foreach (BayStatId id in Enum.GetValues(typeof(BayStatId)))
        {
            var btn = Instantiate(buttonPrefab, bayColumnParent);
            btn.BindBay(bayManager.BayStats, id, OnBayClicked);
            buttons.Add(btn);
        }
        // DRONE column
        foreach (DroneStatId id in Enum.GetValues(typeof(DroneStatId)))
        {
            var btn = Instantiate(buttonPrefab, droneColumnParent);
            btn.BindDrone(bayManager.DroneStats, id, OnDroneClicked);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null) GameManager.Instance.OnMoneyChanged += moneyListener;

        OnStatsChanged = RefreshAll;
        if (bayManager.BayStats != null)   bayManager.BayStats.OnChanged   += OnStatsChanged;
        if (bayManager.DroneStats != null) bayManager.DroneStats.OnChanged += OnStatsChanged;

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
        if (OnStatsChanged != null && bayManager != null)
        {
            if (bayManager.BayStats != null)   bayManager.BayStats.OnChanged   -= OnStatsChanged;
            if (bayManager.DroneStats != null) bayManager.DroneStats.OnChanged -= OnStatsChanged;
        }
    }

    public void Toggle()
    {
        var cg = GetComponent<CanvasGroup>();
        bool visible = cg != null && cg.alpha > 0.5f;
        SetVisible(!visible);
        if (!visible) RefreshAll();
    }

    private void SetVisible(bool visible)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    private void OnBayClicked(BayStatId id)
    {
        var stat = bayManager.BayStats.Get(id);
        if (stat == null) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            bayManager.BayStats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void OnDroneClicked(DroneStatId id)
    {
        var stat = bayManager.DroneStats.Get(id);
        if (stat == null) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            bayManager.DroneStats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.Refresh(money);
    }
}
```

- [ ] **Step 2: Create the scene GameObject via execute_code**

```
mcp__UnityMCP__execute_code code=<<EOF
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
var canvas = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Canvas>();
var panelGo = new UnityEngine.GameObject("DroneUpgradePanel", typeof(UnityEngine.RectTransform), typeof(UnityEngine.CanvasGroup), typeof(DroneUpgradePanel));
panelGo.transform.SetParent(canvas.transform, false);
var rt = panelGo.GetComponent<UnityEngine.RectTransform>();
rt.sizeDelta = new UnityEngine.Vector2(520f, 460f);
rt.anchoredPosition = UnityEngine.Vector2.zero;

// BAY column
var bayCol = new UnityEngine.GameObject("BayColumn", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup));
bayCol.transform.SetParent(panelGo.transform, false);
var bayRt = bayCol.GetComponent<UnityEngine.RectTransform>();
bayRt.anchoredPosition = new UnityEngine.Vector2(-120f, 0f);
bayRt.sizeDelta = new UnityEngine.Vector2(230f, 420f);

// DRONE column
var droneCol = new UnityEngine.GameObject("DroneColumn", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup));
droneCol.transform.SetParent(panelGo.transform, false);
var droneRt = droneCol.GetComponent<UnityEngine.RectTransform>();
droneRt.anchoredPosition = new UnityEngine.Vector2(120f, 0f);
droneRt.sizeDelta = new UnityEngine.Vector2(230f, 420f);

var panel = panelGo.GetComponent<DroneUpgradePanel>();
var btnPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UpgradeButton>("Assets/Prefabs/UpgradeButton.prefab");
var bayMgr = UnityEngine.Object.FindFirstObjectByType<BayManager>();
var so = new UnityEditor.SerializedObject(panel);
so.FindProperty("buttonPrefab").objectReferenceValue = btnPrefab;
so.FindProperty("bayColumnParent").objectReferenceValue = bayCol.transform;
so.FindProperty("droneColumnParent").objectReferenceValue = droneCol.transform;
so.FindProperty("bayManager").objectReferenceValue = bayMgr;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
UnityEngine.Debug.Log("[Iter3] DroneUpgradePanel scene wiring done");
EOF
```

- [ ] **Step 3: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 4: Identity scrub + commit**

```bash
git add Assets/Scripts/UI/DroneUpgradePanel.cs Assets/Scenes/Game.unity
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase9: DroneUpgradePanel (BAY + DRONE columns)"
```

---

### Task 9.4: `BuildBayPanel.cs` + `DroneBay` click routing + scene layout shift

**Files:**
- Create: `Assets/Scripts/UI/BuildBayPanel.cs`
- Modify: `Assets/Scripts/Drones/DroneBay.cs` (IPointerClickHandler)
- Modify: `Assets/Scripts/SlotManager.cs` (shift slotStartX slightly left to make room — data-only field edit)

- [ ] **Step 1: Implement `BuildBayPanel`**

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class BuildBayPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text costLabel;
    [SerializeField] private Button confirmButton;

    private DroneBay targetBay;
    private Func<int> costLookup;
    private Action<DroneBay> onConfirm;
    private Action<int> moneyListener;

    private void Start()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
        moneyListener = _ => Refresh();
        if (GameManager.Instance != null) GameManager.Instance.OnMoneyChanged += moneyListener;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
    }

    public void Open(DroneBay bay, Func<int> costLookup, Action<DroneBay> onConfirm)
    {
        targetBay = bay;
        this.costLookup = costLookup;
        this.onConfirm = onConfirm;
        if (titleLabel != null) titleLabel.text = "BUILD BAY";
        SetVisible(true);
        Refresh();
    }

    public void Close()
    {
        targetBay = null;
        costLookup = null;
        onConfirm = null;
        SetVisible(false);
    }

    public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.5f;

    private void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    private void OnConfirmClicked()
    {
        if (targetBay == null || onConfirm == null) return;
        onConfirm(targetBay);
    }

    private void Refresh()
    {
        int cost = costLookup != null ? costLookup() : 0;
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        if (costLabel != null) costLabel.text = $"${cost}";
        if (confirmButton != null) confirmButton.interactable = money >= cost;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
```

- [ ] **Step 2: Make `DroneBay` clickable**

Add to `DroneBay.cs`:

```csharp
// IPointerClickHandler-style hook; use the new Input System click handler.
// Real routing goes through BayManager.HandleBayClicked which decides between
// "open upgrade panel" and "open build modal" based on whether the bay is
// built.
public event System.Action<DroneBay> Clicked;

private void OnMouseDown() // physics raycaster path
{
    Clicked?.Invoke(this);
}
```

And in `BayManager.cs`, wire click handlers in `Start`:

```csharp
bay.Clicked += HandleBayClicked;
```

```csharp
private void HandleBayClicked(DroneBay bay)
{
    // If the bay has an active drone child, treat as "open upgrade panel".
    // Otherwise, open the build panel.
    bool hasDrone = bay.GetComponentInChildren<CollectorDrone>(true) != null;
    if (hasDrone)
    {
        if (upgradePanel != null) upgradePanel.Toggle();
    }
    else
    {
        if (buildPanel != null) buildPanel.Open(bay, NextBuildCost, OnConfirmBuild);
    }
}

private void OnConfirmBuild(DroneBay bay)
{
    int cost = NextBuildCost();
    if (GameManager.Instance == null || !GameManager.Instance.TrySpend(cost)) return;
    bay.gameObject.SetActive(true);
    SpawnDroneFor(bay);
    purchasedCount++;
    if (buildPanel != null) buildPanel.Close();
}
```

- [ ] **Step 3: Shift weapon row to make room**

In `SlotManager.cs` change the default `slotSpacing` or `slotY` values via execute_code asset-only edit. Per spec §4 "shifts the weapon row a bit left of center" — set `slotStartX` via a new field or via the prefab:

Add a `slotStartX` field (default `-2f`) to `SlotManager.cs` and use it in `Start`:

```csharp
[SerializeField] private float slotStartX = -2f;
// ...
float startX = slotStartX + (-slotSpacing * (slotCount - 1) * 0.5f);
```

Rebalance by editing the scene's SlotManager to `slotStartX = -2f` so the 3 bays (at +6, +8.5, +11) don't collide with the weapon row.

- [ ] **Step 4: Scene wiring via execute_code**

```
mcp__UnityMCP__execute_code code=<<EOF
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");

// Create BayManager GameObject
var mgrGo = new UnityEngine.GameObject("BayManager", typeof(BayManager));
var mgr = mgrGo.GetComponent<BayManager>();
var mgrSo = new UnityEditor.SerializedObject(mgr);
mgrSo.FindProperty("bayPrefab").objectReferenceValue =
    UnityEditor.AssetDatabase.LoadAssetAtPath<DroneBay>("Assets/Prefabs/DroneBay.prefab");
mgrSo.FindProperty("dronePrefab").objectReferenceValue =
    UnityEditor.AssetDatabase.LoadAssetAtPath<CollectorDrone>("Assets/Prefabs/CollectorDrone.prefab");
mgrSo.FindProperty("droneStats").objectReferenceValue =
    UnityEditor.AssetDatabase.LoadAssetAtPath<DroneStats>("Assets/Data/DroneStats.asset");
mgrSo.FindProperty("bayStats").objectReferenceValue =
    UnityEditor.AssetDatabase.LoadAssetAtPath<BayStats>("Assets/Data/BayStats.asset");
mgrSo.FindProperty("meteorSpawner").objectReferenceValue =
    UnityEngine.Object.FindFirstObjectByType<MeteorSpawner>();
mgrSo.ApplyModifiedPropertiesWithoutUndo();

// Shift SlotManager row left
var slotMgr = UnityEngine.Object.FindFirstObjectByType<SlotManager>();
var slotSo = new UnityEditor.SerializedObject(slotMgr);
slotSo.FindProperty("slotStartX").floatValue = -2f;
slotSo.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
UnityEngine.Debug.Log("[Iter3] BayManager in scene, SlotManager shifted left");
EOF
```

(BuildBayPanel scene GameObject is created via a similar execute_code block — same pattern as `DroneUpgradePanel` in Task 9.3; wire `bayManager`, `buttonPrefab`, labels.)

- [ ] **Step 5: Full suite**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 6: Identity scrub + commit**

```bash
git add Assets/Scripts/UI/BuildBayPanel.cs Assets/Scripts/Drones/DroneBay.cs \
        Assets/Scripts/Drones/BayManager.cs Assets/Scripts/SlotManager.cs \
        Assets/Scenes/Game.unity
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase9: BuildBayPanel + bay click routing + weapon row shift"
```

---

## Phase 10 — End-to-end verification + code review + ship

### Task 10.1: Integration sweep + tuning pass

**Files:**
- Modify (data only): `Assets/Data/DroneStats.asset`, `Assets/Data/BayStats.asset`, `Assets/Data/Materials/Core.asset` (if tuning)

- [ ] **Step 1: Dev build via MCP**

```
mcp__UnityMCP__execute_code code="BuildScripts.BuildWebGLDev();"
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: build succeeds, `.dev-build-marker` created.

- [ ] **Step 2: Serve + browser smoke**

```
tools/serve-webgl-dev.sh (run_in_background=true)
chrome-devtools-mcp new_page http://localhost:8000/
chrome-devtools-mcp wait_for text="Meteor Idle"
chrome-devtools-mcp take_screenshot
chrome-devtools-mcp list_console_messages
```

Exercise: kill a few asteroids, watch drops form, drone launch/pickup/return cycle, deposit money, check console is clean.

- [ ] **Step 3: Tune data-only**

If drones feel too fast/slow or battery runs out too often, edit `DroneStats.asset` / `BayStats.asset` via execute_code. Retest. No code changes.

- [ ] **Step 4: Close tab + kill server**

```
chrome-devtools-mcp close_page
```

Kill the background serve process.

- [ ] **Step 5: Identity scrub + commit tuning**

```bash
git add Assets/Data/DroneStats.asset Assets/Data/BayStats.asset
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase10: data-only tuning pass after dev-build playtest"
```

(Skip commit if no tuning was needed.)

---

### Task 10.2: Code review dispatch

- [ ] **Step 1: Dispatch `superpowers:code-reviewer` subagent**

Dispatch with:
- spec path: `docs/superpowers/specs/2026-04-11-drone-economy-design.md`
- plan path: `docs/superpowers/plans/2026-04-11-drone-economy.md`
- branch range: `main..HEAD`
- focus: custom DroneBody physics correctness, state-machine correctness, paysOnBreak isolation, 4-keyframe quantized door animation purity (no lerps), bay click routing not leaking into weapon slot routing.

- [ ] **Step 2: Address findings inline**

Fix high-severity issues. Skip findings that conflict with the spec (e.g., reviewer suggests per-drone stats — spec explicitly specifies fleet-wide).

- [ ] **Step 3: Re-run both test suites**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: both green.

- [ ] **Step 4: Identity scrub + commit fixes**

```bash
git add <fixed files>
python3 tools/identity-scrub.py
git commit -m "Iter3 Phase10: code review fixes"
```

---

### Task 10.3: Final dev verify + user sign-off gate

- [ ] **Step 1: Rebuild dev + re-verify via chrome-devtools-mcp**

Same loop as Task 10.1 Step 1-4, but from scratch after the review fixes.

- [ ] **Step 2: PAUSE for user sign-off**

"Iter 3 ready to ship. Please play it through the dev build and approve. I'll fast-forward main and push to prod gh-pages on your go-ahead."

Do NOT advance without explicit user approval.

---

### Task 10.4: Merge + prod build + deploy + roadmap update

- [ ] **Step 1: Fast-forward main**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git checkout main
git merge --ff-only iter/drone-economy
git push origin main
```

- [ ] **Step 2: Prod WebGL build**

```
mcp__UnityMCP__execute_code code="BuildScripts.BuildWebGL();"
mcp__UnityMCP__read_console types=["log","error"] count=10
```

Expected: success log, zero errors, `.dev-build-marker` absent.

- [ ] **Step 3: Deploy to gh-pages**

```bash
tools/deploy-webgl.sh
```

Expected: clean run, prints manual `git push` command.

- [ ] **Step 4: Push gh-pages**

```bash
git -C ../Meteor-Idle-gh-pages push origin gh-pages
```

- [ ] **Step 5: Smoke-test live URL**

```
chrome-devtools-mcp new_page https://muwamath.github.io/Meteor-Idle/
chrome-devtools-mcp wait_for text="Meteor Idle"
chrome-devtools-mcp list_console_messages
chrome-devtools-mcp take_screenshot
chrome-devtools-mcp close_page
```

Expected: live site loads, no console errors, screenshot shows Iter 3 build with bays.

- [ ] **Step 6: Update roadmap + CLAUDE.md + README.md**

Edit `docs/superpowers/roadmap.md` to mark Iter 3 shipped. Per the `feedback_update_both_claude_md_and_readme_md` rule, update both `CLAUDE.md` and `README.md` to mention the drone economy (new scripts in `Assets/Scripts/Drones/`, new `DroneStats`/`BayStats` data assets, `paysOnBreak` flag semantics).

```bash
git add docs/superpowers/roadmap.md CLAUDE.md README.md
python3 tools/identity-scrub.py
git commit -m "Roadmap: Iter 3 shipped (drone economy)"
git push origin main
```

- [ ] **Step 7: Handoff note via `remember:remember` skill**

Record Iter 3 ship state to `/Users/matt/dev/Unity/Meteor Idle/.remember/remember.md`.

---

## Summary

| Phase | Tasks | Files touched | Tests added |
|---|---|---|---|
| 1 — CoreDrop + paysOnBreak | 5 | 5 code + 2 assets | 9 |
| 2 — DroneBody integrator | 2 | 1 | 7 |
| 3 — State machine (isolated) | 3 | 3 | 11 |
| 4 — Body wired end-to-end | 2 | 2 | 1 PlayMode |
| 5 — Avoidance + contact push | 2 | 1 | 1 PlayMode |
| 6 — DroneStats + BayStats | 2 | 2 code + 2 assets | 13 |
| 7 — Drone visual | 3 | 2 code + 2 art + 1 prefab | 0 |
| 8 — DroneBay + doors | 3 | 1 code + 2 art + 1 prefab | 4 |
| 9 — BayManager + UI | 4 | 3 new + 3 modified | 3 |
| 10 — Verify + ship | 4 | docs only | — |

**Total tasks:** 30. **Total new tests:** 48 (EditMode) + 3 PlayMode. **Total new files:** 11 code + 5 assets + 3 prefabs + 4 art = 23.
