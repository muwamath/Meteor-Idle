# Asteroid Variety Implementation Plan (Iter 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (the Unity MCP state is session-bound, so subagent-per-task wastes tokens). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-voxel material variety inside each asteroid (Stone, Gold, Explosive) on top of Iter 1's dirt+core substrate, with extensible `VoxelMaterial` ScriptableObject data, deterministic placement passes, generalized targeting priority (gold > explosive > core), and chain-reactive explosives.

**Architecture:** New `VoxelMaterial` ScriptableObject + `MaterialRegistry` carry palette, HP, payout, targeting tier, and spawn weight per material. `VoxelMeteorGenerator` runs three new placement passes (stone clumps with 2-deep cap, gold-prefers-stone, explosives-not-adjacent) and emits a parallel `VoxelMaterial[,] material` array alongside Iter 1's `kind[,]` + `hp[,]`. `Meteor` generalizes `HasLiveCore` → `HasAnyTargetable` and `PickRandomCoreVoxel` → `PickPriorityVoxel`. `DestroyResult` becomes per-material counts with `TotalPayout()`. Explosives enqueue into `Meteor.pendingDetonations` on HP-0 and detonate next frame so chains visibly cascade.

**Tech Stack:** Unity 6000.4.1f1 · C# · NUnit (EditMode + PlayMode) · `mcp__UnityMCP__run_tests` for both suites · `mcp__UnityMCP__execute_code` for asset creation and WebGL builds (editor stays open per `feedback_unity_editor_always_open`).

**Spec:** `docs/superpowers/specs/2026-04-11-asteroid-variety-design.md`

**Branch:** `iter/asteroid-variety` (created, spec committed at `4848d0c`).

---

## Phase 1 — Material data layer (`VoxelMaterial`, `MaterialRegistry`, 5 assets)

This phase adds the data substrate without touching any behavior. The generator still emits Dirt + Core as it does today; what changes is that those kinds are now backed by ScriptableObject assets rather than hardcoded constants in the generator. Iter 1's tests stay green throughout.

### Task 1.1: Create `VoxelMaterial.cs` ScriptableObject

**Files:**
- Create: `Assets/Scripts/VoxelMaterial.cs`

- [ ] **Step 1: Write the ScriptableObject class**

```csharp
using UnityEngine;

// Per-cell material data for voxel meteors. Each material kind (Dirt, Stone,
// Core, Gold, Explosive, …) is a single asset under Assets/Data/Materials/.
// Behavior dispatch lives on the asset, not in switch statements throughout
// Meteor.cs. Adding a new inert material is "create asset, register". Adding
// a new behavior is "new MaterialBehavior enum value + handler in Meteor.Update".
[CreateAssetMenu(menuName = "Meteor Idle/Voxel Material", fileName = "VoxelMaterial")]
public class VoxelMaterial : ScriptableObject
{
    [Tooltip("Debug-only display name. Inspector and logs use this.")]
    public string displayName = "Unnamed";

    [Header("Visuals")]
    [Tooltip("Top edge color of the 15x15 voxel block.")]
    public Color topColor = Color.white;
    [Tooltip("Bottom edge color of the 15x15 voxel block.")]
    public Color bottomColor = Color.gray;

    [Header("Mechanics")]
    [Tooltip("HP this material starts with when placed. Cores override this by size.")]
    public int baseHp = 1;

    [Tooltip("Money paid when one cell of this material is destroyed (HP hits 0).")]
    public int payoutPerCell = 0;

    [Tooltip("Behavior verb. Inert = passive filler. Explosive = enqueues neighbor damage on death.")]
    public MaterialBehavior behavior = MaterialBehavior.Inert;

    [Header("Targeting")]
    [Tooltip("Turret targeting priority. 0 = never targeted. Lower positive = higher priority. Gold=1, Explosive=2, Core=3.")]
    public int targetingTier = 0;

    [Header("Placement")]
    [Tooltip("Independent rarity dial. Higher = more common. Generator interprets per-material; see VoxelMeteorGenerator.")]
    public float spawnWeight = 0f;
}

// Behavior verb for a material. Iter 2 introduces the first non-inert kind
// (Explosive). Future iterations can add Magnetic, Frozen, Reactive, etc. by
// extending this enum and adding a handler in Meteor.Update's pending-action
// loop. Each new behavior is a bounded ~30-50 LOC extension.
public enum MaterialBehavior : byte
{
    Inert = 0,
    Explosive = 1,
}
```

- [ ] **Step 2: Compile check**

Run via MCP:
```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero compile errors. The `[CreateAssetMenu]` attribute will surface a new menu entry but no assets created yet.

- [ ] **Step 3: Do not commit yet** — Phase 1 commits as one unit at the end of Task 1.5.

---

### Task 1.2: Create `MaterialRegistry.cs` ScriptableObject

**Files:**
- Create: `Assets/Scripts/MaterialRegistry.cs`

- [ ] **Step 1: Write the registry class**

```csharp
using System.Collections.Generic;
using UnityEngine;

// Single registry asset that holds the list of all VoxelMaterial assets used
// by the game. VoxelMeteorGenerator and Meteor both reference this so they
// can resolve material asset references and enumerate materials by tier or
// behavior. One asset, lives at Assets/Data/MaterialRegistry.asset.
//
// Why: avoids passing 5+ individual material refs into every consumer, and
// gives us a single place to add a new material without touching call sites.
[CreateAssetMenu(menuName = "Meteor Idle/Material Registry", fileName = "MaterialRegistry")]
public class MaterialRegistry : ScriptableObject
{
    [Tooltip("All VoxelMaterial assets used by the generator and Meteor.")]
    public VoxelMaterial[] materials;

    // Convenience accessors used by tests and by code that needs to look up
    // a material by displayName. Linear scan is fine — the list has <10
    // entries and lookups are not on the per-frame hot path.

    public VoxelMaterial GetByName(string displayName)
    {
        if (materials == null) return null;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && materials[i].displayName == displayName)
                return materials[i];
        return null;
    }

    public int IndexOf(VoxelMaterial material)
    {
        if (materials == null || material == null) return -1;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] == material) return i;
        return -1;
    }

    // Materials with targetingTier > 0 in priority order (lowest tier number
    // first = highest priority). Used by Meteor.PickPriorityVoxel.
    public IEnumerable<VoxelMaterial> TargetableInPriorityOrder()
    {
        if (materials == null) yield break;
        // Bubble through possible tiers 1..N; cheap because the list is tiny.
        // Caller iterates "first non-empty tier wins".
        var sorted = new List<VoxelMaterial>();
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && materials[i].targetingTier > 0)
                sorted.Add(materials[i]);
        sorted.Sort((a, b) => a.targetingTier.CompareTo(b.targetingTier));
        foreach (var m in sorted) yield return m;
    }
}
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

- [ ] **Step 3: No commit yet.**

---

### Task 1.3: Create the 6 ScriptableObject assets via Unity MCP

**Files:**
- Create: `Assets/Data/Materials/Dirt.asset`
- Create: `Assets/Data/Materials/Stone.asset`
- Create: `Assets/Data/Materials/Core.asset`
- Create: `Assets/Data/Materials/Gold.asset`
- Create: `Assets/Data/Materials/Explosive.asset`
- Create: `Assets/Data/MaterialRegistry.asset`

- [ ] **Step 1: Create the directory and all assets via execute_code**

The cleanest path is one `execute_code` call that does all six. Use `AssetDatabase.CreateAsset` so the .meta files are created correctly and the assets show up immediately in the inspector.

```
mcp__UnityMCP__execute_code code=<<EOF
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Data/Materials"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Data", "Materials");

VoxelMaterial Make(string name, string displayName, int hp, int payout,
                   MaterialBehavior behavior, int tier, float weight,
                   float tr, float tg, float tb, float br, float bg, float bb)
{
    var m = UnityEngine.ScriptableObject.CreateInstance<VoxelMaterial>();
    m.displayName = displayName;
    m.baseHp = hp;
    m.payoutPerCell = payout;
    m.behavior = behavior;
    m.targetingTier = tier;
    m.spawnWeight = weight;
    m.topColor = new UnityEngine.Color(tr, tg, tb, 1f);
    m.bottomColor = new UnityEngine.Color(br, bg, bb, 1f);
    UnityEditor.AssetDatabase.CreateAsset(m, $"Assets/Data/Materials/{name}.asset");
    return m;
}

var dirt      = Make("Dirt",      "Dirt",      1, 0, MaterialBehavior.Inert,     0, 0f,
                     0.545f, 0.451f, 0.333f, 0.290f, 0.227f, 0.165f);
var stone     = Make("Stone",     "Stone",     2, 0, MaterialBehavior.Inert,     0, 0.05f,
                     0.55f,  0.55f,  0.58f,  0.28f,  0.28f,  0.30f);
var core      = Make("Core",      "Core",      1, 5, MaterialBehavior.Inert,     3, 0f,
                     0.75f,  0.25f,  0.25f,  0.35f,  0.10f,  0.10f);
var gold      = Make("Gold",      "Gold",      1, 5, MaterialBehavior.Inert,     1, 0.005f,
                     1.00f,  0.85f,  0.20f,  0.70f,  0.50f,  0.05f);
var explosive = Make("Explosive", "Explosive", 1, 1, MaterialBehavior.Explosive, 2, 0.002f,
                     1.00f,  0.30f,  0.10f,  0.55f,  0.10f,  0.05f);

var registry = UnityEngine.ScriptableObject.CreateInstance<MaterialRegistry>();
registry.materials = new VoxelMaterial[] { dirt, stone, core, gold, explosive };
UnityEditor.AssetDatabase.CreateAsset(registry, "Assets/Data/MaterialRegistry.asset");

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();
UnityEngine.Debug.Log("[Iter2] Created 5 materials + registry");
EOF
```

Expected console output: `[Iter2] Created 5 materials + registry`. No errors.

- [ ] **Step 2: Verify assets exist**

```
mcp__UnityMCP__manage_asset action=list path=Assets/Data/Materials filter=t:VoxelMaterial
mcp__UnityMCP__manage_asset action=exists path=Assets/Data/MaterialRegistry.asset
```

Expected: 5 VoxelMaterial assets listed, registry exists.

- [ ] **Step 3: No commit yet.** Iter 1's `Core.asset` value of 5 carries forward; nothing in code reads from it yet. Phase 3 wires it in.

---

### Task 1.4: Write `VoxelMaterialTests.cs` (EditMode)

**Files:**
- Create: `Assets/Tests/EditMode/VoxelMaterialTests.cs`

- [ ] **Step 1: Write the test fixture**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Smoke tests for the Iter 2 material data layer. These verify that the
    // .asset files were created with the right field values, the registry
    // resolves them correctly, and tier filtering returns materials in the
    // expected priority order. Pure data — no scene loading required.
    public class VoxelMaterialTests
    {
        private MaterialRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            Assert.IsNotNull(_registry, "MaterialRegistry.asset must exist (Phase 1 Task 1.3)");
            Assert.IsNotNull(_registry.materials, "registry.materials must be populated");
            Assert.AreEqual(5, _registry.materials.Length, "expected 5 materials in Iter 2");
        }

        [Test]
        public void Registry_GetByName_ResolvesAllFiveMaterials()
        {
            Assert.IsNotNull(_registry.GetByName("Dirt"));
            Assert.IsNotNull(_registry.GetByName("Stone"));
            Assert.IsNotNull(_registry.GetByName("Core"));
            Assert.IsNotNull(_registry.GetByName("Gold"));
            Assert.IsNotNull(_registry.GetByName("Explosive"));
        }

        [Test]
        public void Dirt_HasExpectedFields()
        {
            var m = _registry.GetByName("Dirt");
            Assert.AreEqual(1, m.baseHp);
            Assert.AreEqual(0, m.payoutPerCell);
            Assert.AreEqual(MaterialBehavior.Inert, m.behavior);
            Assert.AreEqual(0, m.targetingTier);
        }

        [Test]
        public void Stone_HasHp2_AndIsNeverTargeted()
        {
            var m = _registry.GetByName("Stone");
            Assert.AreEqual(2, m.baseHp);
            Assert.AreEqual(0, m.payoutPerCell);
            Assert.AreEqual(0, m.targetingTier);
            Assert.Greater(m.spawnWeight, 0f, "stone must have non-zero spawn weight");
        }

        [Test]
        public void Gold_HasTopPriority_AndPositivePayout()
        {
            var m = _registry.GetByName("Gold");
            Assert.AreEqual(1, m.targetingTier, "gold must be tier 1 (top priority)");
            Assert.Greater(m.payoutPerCell, 0);
        }

        [Test]
        public void Explosive_HasExplosiveBehavior_AndPriorityTwo()
        {
            var m = _registry.GetByName("Explosive");
            Assert.AreEqual(MaterialBehavior.Explosive, m.behavior);
            Assert.AreEqual(2, m.targetingTier);
            Assert.AreEqual(1, m.payoutPerCell, "Iter 2 placeholder $1 — see project_explosive_payout_scaling memory");
        }

        [Test]
        public void Core_HasPriorityThree()
        {
            var m = _registry.GetByName("Core");
            Assert.AreEqual(3, m.targetingTier);
        }

        [Test]
        public void TargetableInPriorityOrder_ReturnsGoldThenExplosiveThenCore()
        {
            var ordered = new System.Collections.Generic.List<VoxelMaterial>(
                _registry.TargetableInPriorityOrder());
            Assert.AreEqual(3, ordered.Count, "exactly 3 targetable materials in Iter 2");
            Assert.AreEqual("Gold", ordered[0].displayName);
            Assert.AreEqual("Explosive", ordered[1].displayName);
            Assert.AreEqual("Core", ordered[2].displayName);
        }

        [Test]
        public void IndexOf_ReturnsConsistentIndices()
        {
            var dirt = _registry.GetByName("Dirt");
            int idx = _registry.IndexOf(dirt);
            Assert.GreaterOrEqual(idx, 0);
            Assert.AreSame(dirt, _registry.materials[idx]);
        }
    }
}
```

- [ ] **Step 2: Run the tests**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"] test_filter=VoxelMaterialTests
```

Expected: 8 tests, 8 passed.

- [ ] **Step 3: Run the full EditMode suite to confirm no Iter 1 regression**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all existing tests still pass (Iter 1 EditMode count + 8 new = ~75 total).

---

### Task 1.5: Commit Phase 1

- [ ] **Step 1: Stage and identity-scrub**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelMaterial.cs Assets/Scripts/VoxelMaterial.cs.meta \
        Assets/Scripts/MaterialRegistry.cs Assets/Scripts/MaterialRegistry.cs.meta \
        Assets/Data/Materials Assets/Data/MaterialRegistry.asset Assets/Data/MaterialRegistry.asset.meta \
        Assets/Tests/EditMode/VoxelMaterialTests.cs Assets/Tests/EditMode/VoxelMaterialTests.cs.meta
python3 tools/identity-scrub.py
```

Expected: `identity scrub: clean (3 pattern(s) checked against staged diff)`.

- [ ] **Step 2: Commit**

```bash
git commit -m "Iter2 Phase 1: VoxelMaterial SO + MaterialRegistry + 5 base materials"
```

---

## Phase 2 — Generator placement passes (stone, gold, explosives)

`VoxelMeteorGenerator.Generate` learns to take a `MaterialRegistry`, run three new placement passes after the existing dirt-and-cores logic, and emit a parallel `VoxelMaterial[,]` array. The generator does NOT yet change `Meteor.cs` — Phase 3 plumbs the new array through. Iter 1's `kind[,]` and `hp[,]` outputs still match what they emit today for the dirt+core cells; the new array adds material refs for those plus the new stone/gold/explosive cells.

Key constraint: stone clumps must be ≤2 cells thick (every stone cell must have dirt/empty within 2 manhattan steps). Explosives must not be adjacent to other explosives. Both constraints are enforced in the placement loop.

### Task 2.1: Extend `VoxelMeteorGenerator.Generate` signature and add placement passes

**Files:**
- Modify: `Assets/Scripts/VoxelMeteorGenerator.cs`

- [ ] **Step 1: Add `material` out param + the placement passes**

The generator gains:
1. A new `MaterialRegistry registry` parameter (nullable for backward compatibility — if null, behaves as Iter 1)
2. A new `out VoxelMaterial[,] material` parameter
3. Three new placement passes (stone, gold, explosive) after the existing core placement

Replace the `Generate` method body and add helpers. The full new file content:

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class VoxelMeteorGenerator
{
    public const int GridSize = 10;
    public const int VoxelPixelSize = 15;
    public const int TextureSize = GridSize * VoxelPixelSize;

    // Legacy hardcoded palettes — used as fallback when registry is null so
    // existing tests that don't pass a registry still produce identical output.
    private static readonly Color DirtTopColor    = new Color(0.545f, 0.451f, 0.333f, 1f);
    private static readonly Color DirtBottomColor = new Color(0.290f, 0.227f, 0.165f, 1f);
    private static readonly Color CoreTopColor    = new Color(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Color CoreBottomColor = new Color(0.35f, 0.10f, 0.10f, 1f);

    public static void Generate(
        int seed,
        float sizeScale,
        out VoxelKind[,] kind,
        out int[,] hp,
        out Texture2D texture,
        out int aliveCount)
    {
        Generate(seed, sizeScale, null, out kind, out hp, out _, out texture, out aliveCount);
    }

    public static void Generate(
        int seed,
        float sizeScale,
        MaterialRegistry registry,
        out VoxelKind[,] kind,
        out int[,] hp,
        out VoxelMaterial[,] material,
        out Texture2D texture,
        out int aliveCount)
    {
        kind     = new VoxelKind[GridSize, GridSize];
        hp       = new int[GridSize, GridSize];
        material = new VoxelMaterial[GridSize, GridSize];
        var rng = new System.Random(seed);

        // Resolve registry materials once. All five may be null if registry
        // is null (backward-compat path); placement passes early-out then.
        VoxelMaterial dirtMat      = registry?.GetByName("Dirt");
        VoxelMaterial stoneMat     = registry?.GetByName("Stone");
        VoxelMaterial coreMat      = registry?.GetByName("Core");
        VoxelMaterial goldMat      = registry?.GetByName("Gold");
        VoxelMaterial explosiveMat = registry?.GetByName("Explosive");

        // --- dirt shape (unchanged sin-wave lump algorithm) ---
        float phase1 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase2 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase3 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float amp1 = 0.55f + (float)rng.NextDouble() * 0.25f;
        float amp2 = 0.25f + (float)rng.NextDouble() * 0.20f;
        float amp3 = 0.10f + (float)rng.NextDouble() * 0.10f;

        const float baseRadius = 4.5f;
        const float lumpAmp = 0.25f;
        Vector2 center = new Vector2(4.5f, 4.5f);

        aliveCount = 0;
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float theta = Mathf.Atan2(dy, dx);
                float lump =
                    amp1 * Mathf.Sin(theta * 3f + phase1) +
                    amp2 * Mathf.Sin(theta * 5f + phase2) +
                    amp3 * Mathf.Sin(theta * 9f + phase3);
                float radius = baseRadius * (1f + lumpAmp * lump);
                if (dist <= radius)
                {
                    kind[x, y] = VoxelKind.Dirt;
                    hp[x, y] = 1;
                    material[x, y] = dirtMat;
                    aliveCount++;
                }
            }
        }

        // --- core count + HP scale with sizeScale (Iter 1 formulas) ---
        float sizeT = Mathf.Clamp01((sizeScale - 0.525f) / (1.2f - 0.525f));
        int coreCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 4f, sizeT)), 1, 4);
        int coreHp    = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 5f, sizeT)), 1, 5);

        // --- core placement: innermost live cells, deterministic shuffle ---
        var liveCells = new List<(int x, int y, float d2)>(aliveCount);
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                float dx = x + 0.5f - GridSize * 0.5f;
                float dy = y + 0.5f - GridSize * 0.5f;
                liveCells.Add((x, y, dx * dx + dy * dy));
            }
        }
        liveCells.Sort((a, b) => a.d2.CompareTo(b.d2));

        int poolSize = Mathf.Min(Mathf.Max(coreCount * 2, 5), liveCells.Count);
        for (int i = poolSize - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (liveCells[i], liveCells[j]) = (liveCells[j], liveCells[i]);
        }
        int actualCoreCount = Mathf.Min(coreCount, poolSize);
        for (int i = 0; i < actualCoreCount; i++)
        {
            var c = liveCells[i];
            kind[c.x, c.y] = VoxelKind.Core;
            hp[c.x, c.y] = coreHp;
            material[c.x, c.y] = coreMat;
        }

        // --- Iter 2 Pass 1: stone clumps (vein constraint, ≤2 deep) ---
        if (stoneMat != null) PlaceStoneClumps(rng, sizeScale, kind, hp, material, stoneMat);

        // --- Iter 2 Pass 2: gold cells (prefer adjacent to stone) ---
        if (goldMat != null) PlaceGold(rng, sizeScale, kind, hp, material, goldMat, sizeT);

        // --- Iter 2 Pass 3: explosives (never adjacent to other explosives) ---
        if (explosiveMat != null) PlaceExplosives(rng, sizeScale, kind, hp, material, explosiveMat);

        // --- texture paint ---
        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"VoxelMeteor_{seed}"
        };
        var clear = new Color[TextureSize * TextureSize];
        texture.SetPixels(clear);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                if (material[x, y] != null)
                    PaintBlockWithPalette(texture, x, y, material[x, y].topColor, material[x, y].bottomColor);
                else if (kind[x, y] == VoxelKind.Core)
                    PaintBlockWithPalette(texture, x, y, CoreTopColor, CoreBottomColor);
                else
                    PaintBlockWithPalette(texture, x, y, DirtTopColor, DirtBottomColor);
            }
        }
        texture.Apply();
    }

    // ---------- placement helpers ----------

    private static void PlaceStoneClumps(
        System.Random rng, float sizeScale,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial stoneMat)
    {
        // Clump count scales with size: smallest gets 0-1, largest gets 1-3.
        float sizeT = Mathf.Clamp01((sizeScale - 0.525f) / (1.2f - 0.525f));
        int maxClumps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, sizeT)));
        // Stone's spawn weight gates the count: rng < weight means "go",
        // and we roll up to maxClumps independent times.
        int clumpCount = 0;
        for (int i = 0; i < maxClumps; i++)
            if (rng.NextDouble() < stoneMat.spawnWeight * 20.0) // weight 0.05 → 100% per try
                clumpCount++;
        if (clumpCount == 0) return;

        for (int c = 0; c < clumpCount; c++)
        {
            int targetSize = Mathf.RoundToInt(Mathf.Lerp(2f, 6f, (float)rng.NextDouble()));
            GrowOneStoneClump(rng, kind, hp, material, stoneMat, targetSize);
        }
    }

    private static void GrowOneStoneClump(
        System.Random rng,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial stoneMat,
        int targetSize)
    {
        // Pick a random Dirt seed cell.
        var dirtCells = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (kind[x, y] == VoxelKind.Dirt) dirtCells.Add((x, y));
        if (dirtCells.Count == 0) return;
        var seed = dirtCells[rng.Next(dirtCells.Count)];

        var clump = new HashSet<(int x, int y)>();
        var frontier = new List<(int x, int y)>();
        clump.Add(seed);
        frontier.Add(seed);
        material[seed.x, seed.y] = stoneMat;
        kind[seed.x, seed.y] = VoxelKind.Dirt; // structural alive bit stays Dirt; behavior reads material
        hp[seed.x, seed.y] = stoneMat.baseHp;

        while (clump.Count < targetSize && frontier.Count > 0)
        {
            int idx = rng.Next(frontier.Count);
            var cur = frontier[idx];
            // Random neighbor: try up to 4 directions, accept the first one
            // that's a Dirt cell within bounds AND wouldn't violate the
            // 2-deep cap.
            var dirs = new (int dx, int dy)[] { (1,0), (-1,0), (0,1), (0,-1) };
            // Shuffle dirs in place
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }
            bool grew = false;
            foreach (var d in dirs)
            {
                int nx = cur.x + d.dx;
                int ny = cur.y + d.dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize) continue;
                if (kind[nx, ny] != VoxelKind.Dirt) continue; // skip Empty/Core cells
                if (material[nx, ny] == stoneMat) continue;   // already in clump
                if (WouldExceedTwoDeep(nx, ny, material, stoneMat)) continue;

                clump.Add((nx, ny));
                frontier.Add((nx, ny));
                material[nx, ny] = stoneMat;
                hp[nx, ny] = stoneMat.baseHp;
                grew = true;
                break;
            }
            if (!grew) frontier.RemoveAt(idx);
        }
    }

    // Return true if making (x,y) a stone cell would create a stone cell
    // that has no non-stone neighbor within 2 manhattan steps. The
    // constraint: every stone cell must be within 2 cells of a non-stone
    // (dirt/empty/core/gold/explosive) cell, so clumps form veins not blobs.
    private static bool WouldExceedTwoDeep(
        int x, int y, VoxelMaterial[,] material, VoxelMaterial stoneMat)
    {
        // Hypothetically place stone at (x,y). Now check: does (x,y) itself
        // have a non-stone cell within 2 steps?
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 2) continue;
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize)
                    return false; // out-of-bounds counts as "non-stone"
                if (material[nx, ny] != stoneMat)
                    return false;
            }
        }
        return true; // every cell within 2 steps is stone — would exceed
    }

    private static void PlaceGold(
        System.Random rng, float sizeScale,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial goldMat, float sizeT)
    {
        // Gold is rare. Roll once per asteroid, scaled by size: bigger
        // asteroids get more chances.
        int maxGold = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, sizeT)));
        int goldCount = 0;
        for (int i = 0; i < maxGold; i++)
            if (rng.NextDouble() < goldMat.spawnWeight * 100.0) // weight 0.005 → 50% per try
                goldCount++;
        if (goldCount == 0) return;

        // Build list of dirt cells adjacent to existing stone, and a fallback
        // list of any dirt cells (for the standalone path).
        var adjacentToStone = new List<(int x, int y)>();
        var anyDirt = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] != VoxelKind.Dirt) continue;
                if (material[x, y] != null && material[x, y].displayName == "Stone") continue;
                if (material[x, y] != null && material[x, y].displayName != "Dirt") continue;
                anyDirt.Add((x, y));
                if (HasStoneNeighbor(x, y, material))
                    adjacentToStone.Add((x, y));
            }
        }

        for (int g = 0; g < goldCount; g++)
        {
            (int x, int y) cell;
            if (adjacentToStone.Count > 0)
            {
                int idx = rng.Next(adjacentToStone.Count);
                cell = adjacentToStone[idx];
                adjacentToStone.RemoveAt(idx);
                anyDirt.Remove(cell);
            }
            else if (anyDirt.Count > 0)
            {
                int idx = rng.Next(anyDirt.Count);
                cell = anyDirt[idx];
                anyDirt.RemoveAt(idx);
            }
            else
            {
                break;
            }
            material[cell.x, cell.y] = goldMat;
            hp[cell.x, cell.y] = goldMat.baseHp;
        }
    }

    private static bool HasStoneNeighbor(int x, int y, VoxelMaterial[,] material)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize) continue;
                if (material[nx, ny] != null && material[nx, ny].displayName == "Stone")
                    return true;
            }
        }
        return false;
    }

    private static void PlaceExplosives(
        System.Random rng, float sizeScale,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial explosiveMat)
    {
        // Even rarer than gold. Roll up to 2 attempts.
        int maxExplosive = 2;
        int explosiveCount = 0;
        for (int i = 0; i < maxExplosive; i++)
            if (rng.NextDouble() < explosiveMat.spawnWeight * 200.0) // weight 0.002 → 40% per try
                explosiveCount++;
        if (explosiveCount == 0) return;

        var dirtCells = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (kind[x, y] == VoxelKind.Dirt
                    && material[x, y] != null
                    && material[x, y].displayName == "Dirt")
                    dirtCells.Add((x, y));

        for (int e = 0; e < explosiveCount && dirtCells.Count > 0; e++)
        {
            // Try a few times to find a non-adjacent slot.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int idx = rng.Next(dirtCells.Count);
                var cell = dirtCells[idx];
                if (HasExplosiveNeighbor(cell.x, cell.y, material, explosiveMat))
                {
                    dirtCells.RemoveAt(idx);
                    if (dirtCells.Count == 0) break;
                    continue;
                }
                material[cell.x, cell.y] = explosiveMat;
                hp[cell.x, cell.y] = explosiveMat.baseHp;
                dirtCells.RemoveAt(idx);
                break;
            }
        }
    }

    private static bool HasExplosiveNeighbor(int x, int y, VoxelMaterial[,] material, VoxelMaterial explosiveMat)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize) continue;
                if (material[nx, ny] == explosiveMat) return true;
            }
        }
        return false;
    }

    // ---------- legacy paint helpers (still used by Meteor.ApplyBlast for ClearVoxel) ----------

    public static void PaintDirtVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, DirtTopColor, DirtBottomColor);
    }

    public static void PaintCoreVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, CoreTopColor, CoreBottomColor);
    }

    public static void PaintVoxel(Texture2D tex, int gx, int gy)
    {
        PaintDirtVoxel(tex, gx, gy);
    }

    public static void PaintBlockWithPalette(Texture2D tex, int gx, int gy, Color topCol, Color bottomCol)
    {
        int px0 = gx * VoxelPixelSize;
        int py0 = gy * VoxelPixelSize;

        float t = (float)gy / (GridSize - 1);
        Color baseCol = Color.Lerp(bottomCol, topCol, t);
        Color hi = Color.Lerp(baseCol, Color.white, 0.18f);
        Color lo = Color.Lerp(baseCol, Color.black, 0.35f);

        for (int y = 0; y < VoxelPixelSize; y++)
        {
            for (int x = 0; x < VoxelPixelSize; x++)
            {
                Color c = baseCol;
                if (x == 0 || y == VoxelPixelSize - 1) c = hi;
                if (x == VoxelPixelSize - 1 || y == 0) c = lo;
                tex.SetPixel(px0 + x, py0 + y, c);
            }
        }
    }

    public static void ClearVoxel(Texture2D tex, int gx, int gy)
    {
        int px0 = gx * VoxelPixelSize;
        int py0 = gy * VoxelPixelSize;
        for (int y = 0; y < VoxelPixelSize; y++)
            for (int x = 0; x < VoxelPixelSize; x++)
                tex.SetPixel(px0 + x, py0 + y, new Color(0, 0, 0, 0));
    }
}
```

Critical: the legacy `Generate` overload (no registry, no material out) stays so existing tests/`Meteor.cs` keep compiling. The 6-arg version is the Iter 2 path; Phase 3 will switch `Meteor.Spawn` over to it.

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

- [ ] **Step 3: Run existing EditMode suite to confirm legacy generator path still works**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all existing tests still green (the legacy `Generate` overload is unchanged in behavior — it just delegates to the 6-arg version with `registry=null` and discards the material array).

- [ ] **Step 4: No commit yet — Phase 2 commits at end of Task 2.2.**

---

### Task 2.2: Extend `VoxelMeteorGeneratorTests.cs` for new placement passes

**Files:**
- Modify: `Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs`

- [ ] **Step 1: Add new test cases**

Add these tests at the bottom of the existing test class. They use `AssetDatabase.LoadAssetAtPath` to grab the registry, then call the new 6-arg `Generate` overload.

```csharp
[Test]
public void Generate_WithRegistry_EmitsMaterialArrayMatchingDirtAndCore()
{
    var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
        "Assets/Data/MaterialRegistry.asset");
    Assert.IsNotNull(registry);
    VoxelMeteorGenerator.Generate(
        seed: 42, sizeScale: 1f, registry: registry,
        out var kind, out var hp, out var material,
        out var tex, out int aliveCount);
    UnityEngine.Object.DestroyImmediate(tex);

    // Every non-empty cell must have a material reference.
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            if (kind[x, y] != VoxelKind.Empty)
                Assert.IsNotNull(material[x, y], $"cell ({x},{y}) has kind {kind[x,y]} but no material");
}

[Test]
public void Generate_DeterministicAcrossSeeds()
{
    var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
        "Assets/Data/MaterialRegistry.asset");
    VoxelMeteorGenerator.Generate(seed: 12345, sizeScale: 1f, registry: registry,
        out var kindA, out _, out var materialA, out var texA, out _);
    VoxelMeteorGenerator.Generate(seed: 12345, sizeScale: 1f, registry: registry,
        out var kindB, out _, out var materialB, out var texB, out _);
    UnityEngine.Object.DestroyImmediate(texA);
    UnityEngine.Object.DestroyImmediate(texB);

    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            Assert.AreEqual(kindA[x, y], kindB[x, y]);
            Assert.AreSame(materialA[x, y], materialB[x, y]);
        }
}

[Test]
public void Generate_StoneVeinNeverExceedsTwoDeep()
{
    var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
        "Assets/Data/MaterialRegistry.asset");
    var stoneMat = registry.GetByName("Stone");

    // Sweep many seeds to catch any clump that violates the 2-deep cap.
    for (int seed = 1; seed <= 200; seed++)
    {
        VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
            out var kind, out _, out var material, out var tex, out _);
        UnityEngine.Object.DestroyImmediate(tex);

        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (material[x, y] != stoneMat) continue;
                // Every stone cell must have a non-stone neighbor within
                // manhattan distance 2.
                bool hasEscape = false;
                for (int dy = -2; dy <= 2 && !hasEscape; dy++)
                {
                    for (int dx = -2; dx <= 2 && !hasEscape; dx++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) > 2) continue;
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= VoxelMeteorGenerator.GridSize || ny >= VoxelMeteorGenerator.GridSize)
                        { hasEscape = true; break; }
                        if (material[nx, ny] != stoneMat) hasEscape = true;
                    }
                }
                Assert.IsTrue(hasEscape, $"seed {seed}: stone cell ({x},{y}) has no non-stone neighbor within 2 manhattan steps — clump exceeds 2-deep cap");
            }
        }
    }
}

[Test]
public void Generate_ExplosivesNeverAdjacentToOtherExplosives()
{
    var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
        "Assets/Data/MaterialRegistry.asset");
    var explosiveMat = registry.GetByName("Explosive");

    for (int seed = 1; seed <= 500; seed++)
    {
        VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
            out var kind, out _, out var material, out var tex, out _);
        UnityEngine.Object.DestroyImmediate(tex);

        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (material[x, y] != explosiveMat) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= VoxelMeteorGenerator.GridSize || ny >= VoxelMeteorGenerator.GridSize) continue;
                        Assert.AreNotSame(explosiveMat, material[nx, ny],
                            $"seed {seed}: adjacent explosives at ({x},{y}) and ({nx},{ny})");
                    }
                }
            }
        }
    }
}

[Test]
public void Generate_GoldPrefersStoneNeighbors_WhenStonePresent()
{
    var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
        "Assets/Data/MaterialRegistry.asset");
    var stoneMat = registry.GetByName("Stone");
    var goldMat  = registry.GetByName("Gold");

    int totalGold = 0;
    int goldNextToStone = 0;
    for (int seed = 1; seed <= 1000; seed++)
    {
        VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
            out _, out _, out var material, out var tex, out _);
        UnityEngine.Object.DestroyImmediate(tex);

        // Only count seeds where stone exists — pure-gold standalone is fine
        // and tested separately.
        bool stonePresent = false;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize && !stonePresent; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize && !stonePresent; x++)
                if (material[x, y] == stoneMat) stonePresent = true;
        if (!stonePresent) continue;

        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (material[x, y] != goldMat) continue;
                totalGold++;
                bool nextToStone = false;
                for (int dy = -1; dy <= 1 && !nextToStone; dy++)
                {
                    for (int dx = -1; dx <= 1 && !nextToStone; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= VoxelMeteorGenerator.GridSize || ny >= VoxelMeteorGenerator.GridSize) continue;
                        if (material[nx, ny] == stoneMat) nextToStone = true;
                    }
                }
                if (nextToStone) goldNextToStone++;
            }
        }
    }

    if (totalGold == 0)
    {
        Assert.Inconclusive("no gold rolled across 1000 seeds — increase sample or weight");
        return;
    }
    // When stone is present and gold rolls, the placement preference should
    // make MOST gold cells adjacent to stone. Allow some standalone for cases
    // where the placement loop ran out of stone-adjacent slots.
    float ratio = (float)goldNextToStone / totalGold;
    Assert.Greater(ratio, 0.6f, $"only {goldNextToStone}/{totalGold} gold cells were adjacent to stone — preference broken");
}
```

- [ ] **Step 2: Run the new test cases**

```
mcp__UnityMCP__run_tests mode=EditMode test_filter=VoxelMeteorGeneratorTests
```

Expected: all generator tests green (Iter 1 originals + 5 new = 5+ extra). The gold-prefers-stone test may take a couple of seconds (1000 seeds).

- [ ] **Step 3: Run full EditMode suite**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: 100% green. No regressions.

- [ ] **Step 4: Stage and commit Phase 2**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelMeteorGenerator.cs Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 2: Generator placement passes (stone vein + gold + explosive)"
```

Expected: clean identity scrub, commit succeeds.

---

## Phase 3a — Meteor.cs storage + DestroyResult generalization

`Meteor` gains a `VoxelMaterial[,] material` field, generalizes `HasLiveCore` → `HasAnyTargetable`, generalizes `PickRandomCoreVoxel` → `PickPriorityVoxel`, and `DestroyResult` switches to per-material counts. The Iter 1 fields and methods stay as deprecated thin shims so test code that still uses them keeps working until Phase 3b sweeps the call sites.

This phase touches `Meteor.cs` and adds a small new helper file. Expected size: ~150 LOC of changes within the 200-line cap.

### Task 3a.1: Add `material[,]` field, generalize properties, add `PickPriorityVoxel`

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`

- [ ] **Step 1: Add registry serialized field + material array + new properties**

Inside the `Meteor` class, add:

```csharp
[SerializeField] private MaterialRegistry materialRegistry;
private VoxelMaterial[,] material;
```

Then update `Spawn` to call the 6-arg generator and capture the material array:

```csharp
// Old:
// VoxelMeteorGenerator.Generate(seed, sizeScale, out kind, out hp, out texture, out aliveCount);
// New:
VoxelMeteorGenerator.Generate(
    seed, sizeScale, materialRegistry,
    out kind, out hp, out material, out texture, out aliveCount);
```

- [ ] **Step 2: Add `HasAnyTargetable` and `PickPriorityVoxel`**

Below `CoreVoxelCount`:

```csharp
// Iter 2 generalization of HasLiveCore: true if any cell on this meteor
// is targetable (its material has targetingTier > 0). Used by TurretBase
// to decide if this meteor is worth aiming at.
public bool HasAnyTargetable
{
    get
    {
        if (kind == null || material == null) return false;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                if (kind[x, y] != VoxelKind.Empty
                    && material[x, y] != null
                    && material[x, y].targetingTier > 0)
                    return true;
        return false;
    }
}

// Iter 2 generalization of PickRandomCoreVoxel: pick a random live cell
// from the highest-priority tier present on this meteor. Walks tiers
// gold→explosive→core (lowest tier number first) and returns true on the
// first tier with at least one live cell. Returns false only when no
// targetable cell exists at all.
public bool PickPriorityVoxel(out int gx, out int gy)
{
    gx = 0; gy = 0;
    if (kind == null || material == null) return false;

    // Find the lowest tier > 0 that has any live cell on this meteor.
    int bestTier = int.MaxValue;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            if (kind[x, y] == VoxelKind.Empty) continue;
            var mat = material[x, y];
            if (mat == null || mat.targetingTier <= 0) continue;
            if (mat.targetingTier < bestTier) bestTier = mat.targetingTier;
        }
    if (bestTier == int.MaxValue) return false;

    // Pick uniformly across all live cells at that tier.
    int tierCount = 0;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            if (kind[x, y] == VoxelKind.Empty) continue;
            var mat = material[x, y];
            if (mat == null || mat.targetingTier != bestTier) continue;
            tierCount++;
        }
    int targetIndex = Random.Range(0, tierCount);
    int seen = 0;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
    {
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            if (kind[x, y] == VoxelKind.Empty) continue;
            var mat = material[x, y];
            if (mat == null || mat.targetingTier != bestTier) continue;
            if (seen == targetIndex) { gx = x; gy = y; return true; }
            seen++;
        }
    }
    return false;
}
```

- [ ] **Step 3: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors. (`HasLiveCore` and `PickRandomCoreVoxel` still exist — they're shimmed in Phase 3b.)

- [ ] **Step 4: No commit yet.**

---

### Task 3a.2: Generalize `DestroyResult` to per-material counts

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`

- [ ] **Step 1: Replace `DestroyResult` struct with the per-material version**

At the top of `Meteor.cs`, replace the existing `DestroyResult` struct with:

```csharp
// Iter 2: per-material destruction counts so callers can compute payout
// across mixed materials in one hit. Backed by a small array indexed by
// MaterialRegistry index (lookup is O(1)) plus the legacy fields kept as
// shims for any test code that still asserts dirtDestroyed/coreDestroyed.
public struct DestroyResult
{
    // Legacy shim fields — populated alongside the new per-material counts
    // so Iter 1 test assertions keep working until they're swept in Phase 3b.
    public int dirtDestroyed;
    public int coreDestroyed;
    public int damageDealt;

    // Iter 2 per-material counts. Index into MaterialRegistry.materials.
    // Allocated lazily on first increment to keep the zero-result path
    // allocation-free. Sized by the registry length passed in.
    public int[] countByMaterialIndex;
    public int totalPayout;

    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
    public int TotalPayout => totalPayout;

    public int GetCount(VoxelMaterial m, MaterialRegistry registry)
    {
        if (countByMaterialIndex == null || registry == null) return 0;
        int idx = registry.IndexOf(m);
        if (idx < 0 || idx >= countByMaterialIndex.Length) return 0;
        return countByMaterialIndex[idx];
    }
}
```

- [ ] **Step 2: Update `ApplyBlast` and `ApplyTunnel` to populate per-material counts**

In `ApplyBlast`, where the existing code does `if (wasCore) result.coreDestroyed++; else result.dirtDestroyed++;`, replace with:

```csharp
bool wasCore = kind[x, y] == VoxelKind.Core;
var matHere = material != null ? material[x, y] : null;
kind[x, y] = VoxelKind.Empty;
VoxelMeteorGenerator.ClearVoxel(texture, x, y);
anyPainted = true;
aliveCount--;

// Legacy shim fields — keeps Iter 1 test assertions valid.
if (wasCore) result.coreDestroyed++;
else         result.dirtDestroyed++;

// Iter 2 per-material counts and payout sum.
if (matHere != null && materialRegistry != null)
{
    if (result.countByMaterialIndex == null)
        result.countByMaterialIndex = new int[materialRegistry.materials.Length];
    int idx = materialRegistry.IndexOf(matHere);
    if (idx >= 0)
    {
        result.countByMaterialIndex[idx]++;
        result.totalPayout += matHere.payoutPerCell;
    }
}
```

Apply the same change to the equivalent block in `ApplyTunnel`.

- [ ] **Step 3: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors. The legacy `result.coreDestroyed * Meteor.CoreBaseValue` payout in `Missile.cs` and `RailgunRound.cs` still works because those fields are still populated.

- [ ] **Step 4: Run full EditMode + PlayMode suites**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all green. The `Meteor.materialRegistry` field is null on test-spawned meteors (they create the GameObject manually without setting the field), so `material[,]` will also be null, which means the new per-material count path is silently skipped — but the legacy dirt/core counts still populate correctly. Iter 1 tests still pass.

- [ ] **Step 5: Commit Phase 3a**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 3a: Meteor material[,] storage + PickPriorityVoxel + per-material DestroyResult"
```

---

## Phase 3b — Targeting + payout call sites

Sweep `TurretBase`, `MissileTurret`, `RailgunTurret`, `Missile`, and `RailgunRound` to use `HasAnyTargetable`, `PickPriorityVoxel`, and `result.TotalPayout`. Remove the `Meteor.CoreBaseValue * coreDestroyed` payout path. The Iter 1 thin shims stay until Phase 4.

### Task 3b.1: Update `TurretBase.FindTarget`

**Files:**
- Modify: `Assets/Scripts/TurretBase.cs`

- [ ] **Step 1: Replace the `HasLiveCore` filter**

In `FindTarget`, replace `!m.HasLiveCore` with `!m.HasAnyTargetable`:

```csharp
if (m == null || !m.IsAlive || !m.HasAnyTargetable) continue;
```

Update the comment block to reflect the new behavior — turrets now lock onto any meteor with a targetable cell (gold, explosive, or core), not just cores.

- [ ] **Step 2: Compile check + run tests**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: zero compile errors. Tests stay green: `HasAnyTargetable` returns true for any meteor with cores in the test registry path, AND for meteors created by tests without a registry (`material` is null → property returns false → meteor not targeted, but those tests rely on real meteors with cores, so this is fine). Watch for any test that constructs a meteor with no registry and expects it to be targeted — that needs a fix.

If the `TurretAimIntegrationTests` or `TurretTargetingTests` break here, the fix is to update `PlayModeTestFixture.SpawnTestMeteor` to inject the registry into the meteor before `Spawn`. See Phase 3b.4 for the helper update.

- [ ] **Step 3: No commit yet — Phase 3b commits at the end of Task 3b.5.**

---

### Task 3b.2: Update `MissileTurret.Fire` to use `PickPriorityVoxel`

**Files:**
- Modify: `Assets/Scripts/MissileTurret.cs`

- [ ] **Step 1: Replace `PickRandomCoreVoxel` with `PickPriorityVoxel`**

In `Fire(Meteor target)`, change:
```csharp
if (!target.PickRandomCoreVoxel(out gx, out gy)) return;
```
to:
```csharp
if (!target.PickPriorityVoxel(out gx, out gy)) return;
```

Update the surrounding comment to reflect that the missile now aims at gold, explosive, or core (whichever has the highest priority on the target meteor).

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 3b.3: Update `RailgunTurret.RefreshAimVoxel`

**Files:**
- Modify: `Assets/Scripts/RailgunTurret.cs`

- [ ] **Step 1: Replace `PickRandomCoreVoxel` with `PickPriorityVoxel`**

In `RefreshAimVoxel(Meteor target)`, change:
```csharp
hasAimVoxel = target.PickRandomCoreVoxel(out aimVoxelGx, out aimVoxelGy);
```
to:
```csharp
hasAimVoxel = target.PickPriorityVoxel(out aimVoxelGx, out aimVoxelGy);
```

Update the comment block to reflect the new tier-based pick.

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 3b.4: Update `Missile.cs` and `RailgunRound.cs` to use `result.TotalPayout`

**Files:**
- Modify: `Assets/Scripts/Missile.cs`
- Modify: `Assets/Scripts/Weapons/RailgunRound.cs`

- [ ] **Step 1: Missile.cs payout**

In `OnTriggerEnter2D`, replace:
```csharp
int payout = result.coreDestroyed * Meteor.CoreBaseValue;
```
with:
```csharp
int payout = result.TotalPayout;
```

The "no money no floating text on dirt-only hit" rule still works because dirt's payout is 0 — `TotalPayout` will be 0 when only dirt is destroyed.

- [ ] **Step 2: RailgunRound.cs payout**

In the per-meteor processing loop in `Update`, replace:
```csharp
int payout = result.coreDestroyed * Meteor.CoreBaseValue;
```
with:
```csharp
int payout = result.TotalPayout;
```

- [ ] **Step 3: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 3b.5: Update `PlayModeTestFixture.SpawnTestMeteor` to inject the registry

**Files:**
- Modify: `Assets/Tests/PlayMode/PlayModeTestFixture.cs`

- [ ] **Step 1: Read the helper to confirm the spawn signature**

Read `Assets/Tests/PlayMode/PlayModeTestFixture.cs` and find `SpawnTestMeteor`. It currently does `m.Spawn(...)` directly. For Iter 2 it must also set `materialRegistry` via reflection (private field) before calling `Spawn` so the new material array gets populated.

- [ ] **Step 2: Inject the registry in `SpawnTestMeteor`**

Add this line just before the existing `m.Spawn(...)` call inside `SpawnTestMeteor`:

```csharp
var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
    "Assets/Data/MaterialRegistry.asset");
if (registry != null)
{
    var f = typeof(Meteor).GetField("materialRegistry",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    f?.SetValue(m, registry);
}
```

If `PlayModeTestFixture` already has `using UnityEditor;` at the top, omit the `UnityEditor.` qualifier. PlayMode test assemblies allow `UnityEditor` references because they only run in the editor.

- [ ] **Step 3: Run both test suites**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all green. Existing tests now spawn meteors with registry-driven materials. Most tests will see meteors with cores + dirt (the rare gold/explosive rolls won't fire on every test seed) so the targeting filter sees the same effective behavior as Iter 1.

- [ ] **Step 4: Commit Phase 3b**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/TurretBase.cs Assets/Scripts/MissileTurret.cs Assets/Scripts/RailgunTurret.cs \
        Assets/Scripts/Missile.cs Assets/Scripts/Weapons/RailgunRound.cs \
        Assets/Tests/PlayMode/PlayModeTestFixture.cs
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 3b: TurretBase + projectiles use PickPriorityVoxel + TotalPayout"
```

- [ ] **Step 5: Wire registry into the Meteor prefab**

The serialized `Meteor.materialRegistry` field defaults to null on the prefab. Use Unity MCP to assign it:

```
mcp__UnityMCP__execute_code code=<<EOF
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
    "Assets/Prefabs/Meteor.prefab");
var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
    "Assets/Data/MaterialRegistry.asset");
var meteorComp = prefab.GetComponent<Meteor>();
var so = new UnityEditor.SerializedObject(meteorComp);
so.FindProperty("materialRegistry").objectReferenceValue = registry;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.PrefabUtility.SavePrefabAsset(prefab);
UnityEngine.Debug.Log("[Iter2] Wired registry into Meteor.prefab");
EOF
```

Stage the prefab change:
```bash
git add Assets/Prefabs/Meteor.prefab
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 3b: wire MaterialRegistry into Meteor prefab"
```

---

## Phase 4 — Explosive behavior (pending detonation queue + 1-frame chain)

Add the `pendingDetonations` queue. When `ApplyBlast` or `ApplyTunnel` reduces an Explosive cell to HP 0, that cell goes onto the queue. `Meteor.Update` drains the queue at the start of each tick, applying 1 damage to each of the 8 neighbors of each pending cell. Chains span multiple frames naturally.

### Task 4.1: Add the queue + Update drain logic to `Meteor.cs`

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`

- [ ] **Step 1: Add the queue field and `EnqueueDetonation` helper**

Inside `Meteor`, add:

```csharp
// Iter 2: explosive cells that need to detonate next frame. Populated by
// ApplyBlast/ApplyTunnel when an Explosive material's HP hits 0. Drained
// at the start of Update so chain reactions span multiple frames and the
// cascade is visible to the player.
private readonly System.Collections.Generic.Queue<(int gx, int gy)> pendingDetonations
    = new System.Collections.Generic.Queue<(int, int)>();

private void EnqueueDetonation(int gx, int gy)
{
    pendingDetonations.Enqueue((gx, gy));
}
```

Update `Spawn` to clear the queue (since it's a pooled object):

```csharp
// At the end of Spawn, after texture setup:
pendingDetonations.Clear();
```

- [ ] **Step 2: Drain the queue at the start of `Update`**

At the very top of `Update()` (before the dead-check), add:

```csharp
if (!dead && pendingDetonations.Count > 0)
{
    DrainPendingDetonations();
}
```

And add the helper method:

```csharp
// Apply 1 damage to all 8 neighbors of each cell in the pending queue.
// New explosives knocked to HP 0 by this pass go on the queue for the
// next frame, so chains naturally span multiple frames.
private void DrainPendingDetonations()
{
    int snapshot = pendingDetonations.Count;
    bool anyPainted = false;
    var totalResult = new DestroyResult();

    for (int i = 0; i < snapshot; i++)
    {
        var cell = pendingDetonations.Dequeue();
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cell.gx + dx;
                int ny = cell.gy + dy;
                if (nx < 0 || ny < 0 || nx >= VoxelMeteorGenerator.GridSize || ny >= VoxelMeteorGenerator.GridSize) continue;
                if (kind[nx, ny] == VoxelKind.Empty) continue;

                hp[nx, ny]--;
                totalResult.damageDealt++;
                if (hp[nx, ny] > 0) continue;

                var matHere = material != null ? material[nx, ny] : null;
                bool wasCore = kind[nx, ny] == VoxelKind.Core;
                kind[nx, ny] = VoxelKind.Empty;
                VoxelMeteorGenerator.ClearVoxel(texture, nx, ny);
                anyPainted = true;
                aliveCount--;

                if (wasCore) totalResult.coreDestroyed++;
                else         totalResult.dirtDestroyed++;

                if (matHere != null && materialRegistry != null)
                {
                    if (totalResult.countByMaterialIndex == null)
                        totalResult.countByMaterialIndex = new int[materialRegistry.materials.Length];
                    int idx = materialRegistry.IndexOf(matHere);
                    if (idx >= 0)
                    {
                        totalResult.countByMaterialIndex[idx]++;
                        totalResult.totalPayout += matHere.payoutPerCell;
                    }
                }

                // Chain: a destroyed Explosive enqueues for next frame.
                if (matHere != null && matHere.behavior == MaterialBehavior.Explosive)
                    EnqueueDetonation(nx, ny);

                if (voxelChunkPrefab != null)
                {
                    Vector3 worldVoxel = VoxelCenterToWorld(nx, ny);
                    var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                    burst.Play();
                    Destroy(burst.gameObject, 1.5f);
                }
            }
        }
    }

    if (anyPainted) texture.Apply();

    // Pay out for everything destroyed in this drain pass.
    if (totalResult.totalPayout > 0 && GameManager.Instance != null)
        GameManager.Instance.AddMoney(totalResult.totalPayout);

    if (aliveCount <= 0)
    {
        dead = true;
        owner?.Release(this);
    }
}
```

- [ ] **Step 3: Update `ApplyBlast` and `ApplyTunnel` to enqueue on Explosive HP-0**

In both methods, where the cell is cleared (right after `kind[x, y] = VoxelKind.Empty`), add:

```csharp
// Enqueue chain detonation if this cell was an Explosive.
if (matHere != null && matHere.behavior == MaterialBehavior.Explosive)
    EnqueueDetonation(x, y); // (use ix, iy in ApplyTunnel)
```

Note: `matHere` must be captured *before* `kind[x, y]` is cleared.

- [ ] **Step 4: Compile check + run EditMode**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request wait_for_ready=true
mcp__UnityMCP__read_console types=["error"] count=10
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: zero errors, all green.

- [ ] **Step 5: No commit yet — bundle with the chain test in 4.2.**

---

### Task 4.2: Write `ExplosiveChainTests.cs` (PlayMode)

**Files:**
- Create: `Assets/Tests/PlayMode/ExplosiveChainTests.cs`

- [ ] **Step 1: Write the chain test fixture**

```csharp
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode tests for Iter 2 explosive chain reactions. Forces a meteor's
    // material[,] grid into a known shape via reflection, hits one explosive,
    // and asserts that adjacent explosives detonate on subsequent frames.
    public class ExplosiveChainTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator IsolatedExplosive_DestroysOnlyItself_PaysOne()
        {
            yield return SetupScene();
            int startMoney = GameManager.Instance != null ? GameManager.Instance.Money : 0;

            var meteor = SpawnTestMeteor(Vector3.zero, seed: 1);
            ForceMaterial(meteor, 5, 5, "Explosive");
            // Wipe neighbors so nothing else takes incidental damage.
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0) ClearCell(meteor, 5 + dx, 5 + dy);

            // Hit the explosive directly.
            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(5, 5), 0.05f);
            Assert.AreEqual(1, result.totalPayout, "isolated explosive should pay $1");

            // No chains were queued — drain pass next frame should be a no-op.
            yield return null;

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator TwoAdjacentExplosives_ChainAcrossFrames()
        {
            yield return SetupScene();
            var meteor = SpawnTestMeteor(Vector3.zero, seed: 2);
            // Place two explosives at (4,5) and (5,5). They are adjacent so the
            // first explosion's 8-neighbor ring will queue the second for next
            // frame.
            ForceMaterial(meteor, 4, 5, "Explosive");
            ForceMaterial(meteor, 5, 5, "Explosive");

            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(4, 5), 0.05f);
            Assert.AreEqual(1, result.totalPayout, "first explosive pays $1 immediately");

            // The second explosive must still be alive THIS frame — chains
            // are 1-frame-delayed by design.
            Assert.IsTrue(meteor.IsVoxelPresent(5, 5), "second explosive should still be alive same frame");

            // Advance one frame so Update drains the queue.
            yield return null;

            // The second explosive should have detonated.
            Assert.IsFalse(meteor.IsVoxelPresent(5, 5), "second explosive should have chain-detonated next frame");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator ExplosiveAdjacentToCore_ChainKillsCore()
        {
            yield return SetupScene();
            var meteor = SpawnTestMeteor(Vector3.zero, seed: 3);
            // Place an explosive next to a 1-HP core (use scale 0.525 in
            // SpawnTestMeteor — but the seed's natural cores may not be at
            // a known location, so force one).
            ForceMaterial(meteor, 4, 5, "Explosive");
            ForceMaterial(meteor, 5, 5, "Core", hp: 1);

            int coresBefore = meteor.CoreVoxelCount;
            Assert.GreaterOrEqual(coresBefore, 1);

            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(4, 5), 0.05f);

            // Same-frame: explosive paid $1. Next frame: core dies in chain
            // and pays $5 via Meteor.Update's GameManager.AddMoney call.
            int startMoney = result.totalPayout;
            yield return null;

            // After the drain pass, the core should be gone.
            Assert.IsFalse(meteor.IsVoxelPresent(5, 5), "core should have chain-detonated");
            Assert.Less(meteor.CoreVoxelCount, coresBefore, "core count dropped");

            TeardownScene();
        }

        // ---- helpers ----

        private static void ForceMaterial(Meteor meteor, int gx, int gy, string materialName, int? hp = null)
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            var mat = registry.GetByName(materialName);
            Assert.IsNotNull(mat, $"material {materialName} not found in registry");

            var matField = typeof(Meteor).GetField("material",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var hpField = typeof(Meteor).GetField("hp",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var matArr  = (VoxelMaterial[,])matField.GetValue(meteor);
            var kindArr = (VoxelKind[,])kindField.GetValue(meteor);
            var hpArr   = (int[,])hpField.GetValue(meteor);

            matArr[gx, gy]  = mat;
            kindArr[gx, gy] = materialName == "Core" ? VoxelKind.Core : VoxelKind.Dirt;
            hpArr[gx, gy]   = hp ?? mat.baseHp;
        }

        private static void ClearCell(Meteor meteor, int gx, int gy)
        {
            if (gx < 0 || gy < 0 || gx >= VoxelMeteorGenerator.GridSize || gy >= VoxelMeteorGenerator.GridSize) return;

            var matField  = typeof(Meteor).GetField("material",  BindingFlags.NonPublic | BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",      BindingFlags.NonPublic | BindingFlags.Instance);
            var hpField   = typeof(Meteor).GetField("hp",        BindingFlags.NonPublic | BindingFlags.Instance);

            var matArr  = (VoxelMaterial[,])matField.GetValue(meteor);
            var kindArr = (VoxelKind[,])kindField.GetValue(meteor);
            var hpArr   = (int[,])hpField.GetValue(meteor);

            matArr[gx, gy]  = null;
            kindArr[gx, gy] = VoxelKind.Empty;
            hpArr[gx, gy]   = 0;
        }
    }
}
```

- [ ] **Step 2: Run the chain tests**

```
mcp__UnityMCP__run_tests mode=PlayMode test_filter=ExplosiveChainTests
```

Expected: 3 tests, 3 passed.

- [ ] **Step 3: Run full PlayMode + EditMode suites**

```
mcp__UnityMCP__run_tests mode=PlayMode
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all green, no Iter 1 regressions.

- [ ] **Step 4: Commit Phase 4**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs Assets/Tests/PlayMode/ExplosiveChainTests.cs
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 4: Explosive chain detonation queue + PlayMode chain tests"
```

---

## Phase 5 — Visual verification gate

Per the voxel aesthetic mandatory rule and the visual-verification-for-art memory: pause for the user to inspect the new palettes in a real meteor before merging.

### Task 5.1: WebGL dev build + browser smoke + user pause

- [ ] **Step 1: Trigger the WebGL dev build via MCP execute_code**

```
mcp__UnityMCP__execute_code code="BuildScripts.BuildWebGLDev();"
```

The build runs synchronously inside the editor and takes ~3-4 minutes. Watch the console for `[BuildScripts] WebGL build result: Succeeded`.

```
mcp__UnityMCP__read_console types=["log","error"] count=10
```

Expected: success log, zero errors.

- [ ] **Step 2: Serve and open in chrome-devtools-mcp**

```
Bash run_in_background=true: tools/serve-webgl-dev.sh
mcp__plugin_chrome-devtools-mcp_chrome-devtools__new_page url=http://localhost:8000/
mcp__plugin_chrome-devtools-mcp_chrome-devtools__wait_for text="Meteor Idle" timeout=60000
mcp__plugin_chrome-devtools-mcp_chrome-devtools__take_screenshot
```

Expected: game loads, screenshot shows asteroids with the new material variety.

- [ ] **Step 3: Inspect for stone, gold, explosive presence**

Take 3-5 screenshots over ~30s of gameplay so multiple meteors spawn. Look for:
- Cool grey stone veins on some meteors
- Bright yellow gold cells (rare)
- Hot orange-red explosive cells (very rare)
- Cores still readable as muted red

If gold/explosive don't appear in any of the screenshots, the spawn weights may be too low — note this and consider bumping `Gold.spawnWeight` and `Explosive.spawnWeight` for the verification round before final commit.

- [ ] **Step 4: PAUSE for user verification**

Send the screenshots to the user and explicitly ask: "Do the new material palettes (stone grey, gold yellow, explosive orange) read correctly against dirt and cores? Approve to proceed to Phase 6, or call out tweaks."

**DO NOT advance past this gate without explicit user approval.** The voxel aesthetic rule and the visual-verification-for-art rule both require this.

- [ ] **Step 5: Close the test browser tab + kill the server**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__close_page
KillShell <serve script shell id>
```

Per the close-test-browser-tabs rule.

- [ ] **Step 6: If user requested palette tweaks**

Use `execute_code` to update the affected `VoxelMaterial` asset(s):

```
mcp__UnityMCP__execute_code code=<<EOF
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<VoxelMaterial>(
    "Assets/Data/Materials/Stone.asset");
mat.topColor    = new UnityEngine.Color(<new top>);
mat.bottomColor = new UnityEngine.Color(<new bottom>);
UnityEditor.EditorUtility.SetDirty(mat);
UnityEditor.AssetDatabase.SaveAssets();
EOF
```

Then re-run from Step 1 (rebuild → re-screenshot → re-pause).

- [ ] **Step 7: Once approved, commit any tweaks (if applicable)**

```bash
git add Assets/Data/Materials/
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 5: visual verification — palette tweaks per user approval"
```

(Skip the commit if no tweaks were needed.)

---

## Phase 6 — Code review + merge + WebGL prod deploy

### Task 6.1: Dispatch code-reviewer agent

- [ ] **Step 1: Dispatch the code-reviewer subagent against the full Iter 2 diff**

Use the `Agent` tool with `subagent_type="superpowers:code-reviewer"`:

Prompt the subagent with: the spec path, the iter branch range (`main..HEAD`), and the design constraints to validate (extensibility via VoxelMaterial assets, deterministic placement, explosive chain timing, generous economy biased toward fun-over-friction).

- [ ] **Step 2: Address findings**

Read the report. Fix any high-severity issues inline. Skip findings that conflict with the spec (e.g., a reviewer who suggests cross-meteor priority sorting — the spec explicitly defers that as scope creep).

- [ ] **Step 3: Re-run both test suites after any fixes**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all green.

- [ ] **Step 4: Commit any review fixes**

```bash
git add <fixed files>
python3 tools/identity-scrub.py
git commit -m "Iter2 Phase 6: code review fixes"
```

---

### Task 6.2: Final user verify on dev WebGL

- [ ] **Step 1: Dev rebuild + browser smoke**

Same as Phase 5 Step 1-3, but this time the user is checking gameplay (not just visuals): hit explosives, watch chains, confirm gold cells get prioritized by turrets, etc.

- [ ] **Step 2: PAUSE for user sign-off**

"Iter 2 ready to ship. Please play it through the dev build and approve. I'll fast-forward main + push to prod gh-pages on your go-ahead."

---

### Task 6.3: Merge to main + prod build + deploy

- [ ] **Step 1: Fast-forward main**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git checkout main
git merge --ff-only iter/asteroid-variety
git push origin main
```

- [ ] **Step 2: Prod WebGL build**

```
mcp__UnityMCP__execute_code code="BuildScripts.BuildWebGL();"
mcp__UnityMCP__read_console types=["log","error"] count=10
```

Expected: success log, zero errors.

- [ ] **Step 3: Deploy to gh-pages**

```bash
tools/deploy-webgl.sh
```

Expected: clean deploy script run, prints the manual `git push` command.

- [ ] **Step 4: Push gh-pages**

```bash
git -C ../Meteor-Idle-gh-pages push origin gh-pages
```

- [ ] **Step 5: Smoke-test live URL**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__new_page url=https://muwamath.github.io/Meteor-Idle/
mcp__plugin_chrome-devtools-mcp_chrome-devtools__wait_for text="Meteor Idle" timeout=60000
mcp__plugin_chrome-devtools-mcp_chrome-devtools__list_console_messages
mcp__plugin_chrome-devtools-mcp_chrome-devtools__take_screenshot
mcp__plugin_chrome-devtools-mcp_chrome-devtools__close_page
```

Expected: live site loads, no console errors, screenshot shows current build.

- [ ] **Step 6: Update the roadmap doc to reflect the Iter 2 pivot**

Edit `docs/superpowers/roadmap.md` to replace the old "asteroid types" Iter 2 entry with the per-voxel-materials version that shipped. Mark Iter 2 as ✅ shipped with the date and main commit SHA.

```bash
git add docs/superpowers/roadmap.md
python3 tools/identity-scrub.py
git commit -m "Roadmap: Iter 2 shipped (asteroid variety / per-voxel materials)"
git push origin main
```

- [ ] **Step 7: Save handoff note**

Use the `remember:remember` skill to write `/Users/matt/dev/Unity/Meteor Idle/.remember/remember.md` with the Iter 2 ship state.

---

## Summary

| Phase | Task count | Files touched | Tests added |
|---|---|---|---|
| 1 — Material data layer | 5 | 4 code + 6 assets | 8 |
| 2 — Generator placement | 2 | 2 | 5 |
| 3a — Meteor storage + DestroyResult | 2 | 1 | (existing pass) |
| 3b — Targeting + payout call sites | 5 | 6 | (existing pass) |
| 4 — Explosive behavior | 2 | 2 | 3 |
| 5 — Visual gate | 1 | 0 (or palette tweaks) | — |
| 6 — Review + deploy | 3 | 1 (roadmap) | — |

**Total new tests:** 16. **Total new files:** 4 code + 7 data + 2 test = 13. **Total modified:** 8 code + 1 doc + 1 prefab.
