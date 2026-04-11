# Asteroid Cores Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give meteors a `dirt` vs `core` voxel distinction with per-voxel HP, make cores the only valid target for turrets, and pay money only on core destruction.

**Architecture:** New `VoxelKind` enum alongside a parallel `int[,] hp` array replaces `Meteor.voxels` (`bool[,]`). Generator picks core cells from the innermost live cells using a deterministic shuffle, seeded from the existing rng. `ApplyBlast` and `ApplyTunnel` decrement HP instead of instantly clearing and return a new `DestroyResult` struct. Turrets filter candidates by `HasLiveCore` and pick exclusively from core voxels via the new `PickRandomCoreVoxel` method.

**Tech Stack:** Unity 6000.4.1f1 · C# · NUnit (EditMode + PlayMode) · `mcp__UnityMCP__run_tests` for running the suites · `BuildScripts.BuildWebGLDev`/`BuildWebGL` via `mcp__UnityMCP__execute_code` for the verify/deploy flow (editor stays open per `feedback_unity_editor_always_open`).

**Spec:** `docs/superpowers/specs/2026-04-11-asteroid-cores-design.md`.

**Branch:** `iter/asteroid-cores` (already created, spec already committed at `c818118`).

---

## Phase 1 — Voxel state rewrite: `VoxelKind` enum, `hp` array, generator core placement

The representation change is atomic: the generator's `out` parameters switch from `bool[,] grid` to `VoxelKind[,] kind` + `int[,] hp`, and `Meteor` swaps its internal state at the same time. Existing damage semantics (single-HP dirt, single-hit kill) are preserved — Phase 2 is where HP actually means anything. This keeps Phase 1 self-contained: representation only, no behavior change, all existing Meteor/targeting tests still pass.

### Task 1: Create the `VoxelKind` enum

**Files:**
- Create: `Assets/Scripts/VoxelKind.cs`

- [ ] **Step 1: Create the enum file**

```csharp
// One of three states per voxel cell on a Meteor's 10x10 grid.
// - Empty: no voxel at this cell (outside the shape or destroyed)
// - Dirt: filler material, 1 HP, pays 0 on destruction
// - Core: the prize, multi-HP, pays CoreBaseValue per voxel destroyed
// Backed by byte so the parallel hp[,] array and the kind[,] array pack
// tightly into CPU cache lines during the inner ApplyBlast/ApplyTunnel loops.
public enum VoxelKind : byte
{
    Empty = 0,
    Dirt  = 1,
    Core  = 2,
}
```

- [ ] **Step 2: Compile check via Unity MCP**

Run:
```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors. The new enum file compiles in isolation before anything references it.

- [ ] **Step 3: Do not commit yet** — Phase 1's files commit together at the end of Task 4.

---

### Task 2: Rewrite `VoxelMeteorGenerator.Generate` signature and add core placement

**Files:**
- Modify: `Assets/Scripts/VoxelMeteorGenerator.cs` (full rewrite of `Generate`, add `PaintDirtVoxel`/`PaintCoreVoxel` stubs — actual core palette lands in Phase 4)

- [ ] **Step 1: Replace the generator body**

Replace the entire contents of `Assets/Scripts/VoxelMeteorGenerator.cs` with:

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class VoxelMeteorGenerator
{
    public const int GridSize = 10;
    public const int VoxelPixelSize = 15;
    public const int TextureSize = GridSize * VoxelPixelSize;

    // Dirt palette — unchanged from pre-Iter-1.
    private static readonly Color DirtTopColor    = new Color(0.545f, 0.451f, 0.333f, 1f); // #8B7355
    private static readonly Color DirtBottomColor = new Color(0.290f, 0.227f, 0.165f, 1f); // #4A3A2A

    // Core palette — single baseline hue for Iter 1. Iter 2 will drive multiple
    // palettes off an AsteroidType asset field using exactly this structure.
    private static readonly Color CoreTopColor    = new Color(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Color CoreBottomColor = new Color(0.35f, 0.10f, 0.10f, 1f);

    // New signature: takes sizeScale so the generator can compute the per-
    // meteor core count and HP, and emits a VoxelKind[,] + int[,] hp pair
    // instead of the old bool[,] grid. Callers (Meteor.Spawn, tests) are
    // updated alongside this file.
    public static void Generate(
        int seed,
        float sizeScale,
        out VoxelKind[,] kind,
        out int[,] hp,
        out Texture2D texture,
        out int aliveCount)
    {
        kind = new VoxelKind[GridSize, GridSize];
        hp   = new int[GridSize, GridSize];
        var rng = new System.Random(seed);

        // --- dirt shape (same sin-wave lump algorithm as pre-Iter-1) ---
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
                    aliveCount++;
                }
            }
        }

        // --- core count + HP scale with sizeScale ---
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
        // Deterministic Fisher-Yates shuffle over the top-poolSize innermost
        // cells. Uses the same rng already threaded through the generator so
        // core placement is reproducible per-seed.
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
        }

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
                if (kind[x, y] == VoxelKind.Dirt)      PaintDirtVoxel(texture, x, y);
                else if (kind[x, y] == VoxelKind.Core) PaintCoreVoxel(texture, x, y);
            }
        }

        texture.Apply();
    }

    public static void PaintDirtVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, DirtTopColor, DirtBottomColor);
    }

    public static void PaintCoreVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, CoreTopColor, CoreBottomColor);
    }

    private static void PaintBlockWithPalette(Texture2D tex, int gx, int gy, Color topCol, Color bottomCol)
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

- [ ] **Step 2: Do not run anything yet** — Meteor.cs still calls the old signature and won't compile. Task 3 fixes that.

---

### Task 3: Update `Meteor` to use the new `VoxelKind[,]` + `int[,] hp` storage

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` (switch field, adapt `Spawn`, adapt `ApplyBlast`/`ApplyTunnel` to check `kind != Empty` and instant-clear on hit — HP decrement lands in Phase 2)

- [ ] **Step 1: Replace the voxel storage field**

Find the private field block near the top of `Meteor.cs`:

```csharp
    private bool[,] voxels;
    private Texture2D texture;
    private Sprite sprite;
    private int aliveCount;
```

Replace with:

```csharp
    private VoxelKind[,] kind;
    private int[,] hp;
    private Texture2D texture;
    private Sprite sprite;
    private int aliveCount;
```

- [ ] **Step 2: Update `Spawn` to call the new generator**

Find the `Spawn` method, replace the `VoxelMeteorGenerator.Generate` call line:

```csharp
VoxelMeteorGenerator.Generate(seed, out voxels, out texture, out aliveCount);
```

with:

```csharp
VoxelMeteorGenerator.Generate(seed, sizeScale, out kind, out hp, out texture, out aliveCount);
```

- [ ] **Step 3: Update `ApplyBlast` cell checks**

Find every `voxels[x, y]` read and `voxels[x, y] = false` write inside `ApplyBlast`. For this phase, keep instant-clear semantics — HP decrement is Phase 2. Replace:

```csharp
if (!voxels[x, y]) continue;
```

with:

```csharp
if (kind[x, y] == VoxelKind.Empty) continue;
```

And replace:

```csharp
voxels[x, y] = false;
```

with:

```csharp
kind[x, y] = VoxelKind.Empty;
hp[x, y] = 0;
```

Same change inside `WalkInwardToAliveCell` and `SnapToNearestAliveCell` — any reference to `voxels[x, y]` becomes `kind[x, y] == VoxelKind.Empty` (negated where appropriate).

- [ ] **Step 4: Update `ApplyTunnel` cell checks**

Same pattern — every `voxels[ix, iy]` inside `ApplyTunnel` becomes a `kind[ix, iy]` check. `if (!voxels[ix, iy]) continue;` → `if (kind[ix, iy] == VoxelKind.Empty) continue;`. The write `voxels[ix, iy] = false;` becomes `kind[ix, iy] = VoxelKind.Empty; hp[ix, iy] = 0;`.

- [ ] **Step 5: Update `IsVoxelPresent`**

```csharp
public bool IsVoxelPresent(int gx, int gy)
{
    if (kind == null) return false;
    if (gx < 0 || gy < 0 || gx >= VoxelMeteorGenerator.GridSize || gy >= VoxelMeteorGenerator.GridSize) return false;
    return kind[gx, gy] != VoxelKind.Empty;
}
```

- [ ] **Step 6: Update `PickRandomPresentVoxel`**

```csharp
public bool PickRandomPresentVoxel(out int gx, out int gy)
{
    gx = 0; gy = 0;
    if (kind == null) return false;

    int liveCount = 0;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            if (kind[x, y] != VoxelKind.Empty) liveCount++;

    if (liveCount == 0) return false;

    int targetIndex = Random.Range(0, liveCount);
    int seen = 0;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
    {
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            if (kind[x, y] == VoxelKind.Empty) continue;
            if (seen == targetIndex)
            {
                gx = x; gy = y;
                return true;
            }
            seen++;
        }
    }
    return false;
}
```

- [ ] **Step 7: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors. If tests referencing `voxels` field via reflection show up, they are not expected in the current test suite — the current tests use `AliveVoxelCount`, `IsVoxelPresent`, `PickRandomPresentVoxel`, and `ApplyBlast`/`ApplyTunnel`, all of which stay stable in signature for Phase 1.

- [ ] **Step 8: Do not commit yet** — run the test fixture update first (Task 4).

---

### Task 4: Update `VoxelMeteorGeneratorTests` to the new signature and add core-placement tests

**Files:**
- Modify: `Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs`

- [ ] **Step 1: Replace the test file contents**

Replace the entire contents of `Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs` with:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class VoxelMeteorGeneratorTests
    {
        private const float DefaultSize = 1.0f;

        [Test]
        public void Generate_SameSeed_ProducesIdenticalKindAndHp()
        {
            VoxelMeteorGenerator.Generate(1234, DefaultSize, out var kindA, out var hpA, out var texA, out int countA);
            VoxelMeteorGenerator.Generate(1234, DefaultSize, out var kindB, out var hpB, out var texB, out int countB);

            try
            {
                Assert.AreEqual(countA, countB);
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                {
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    {
                        Assert.AreEqual(kindA[x, y], kindB[x, y], $"kind ({x},{y}) diverged");
                        Assert.AreEqual(hpA[x, y], hpB[x, y], $"hp ({x},{y}) diverged");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(texA);
                Object.DestroyImmediate(texB);
            }
        }

        [Test]
        public void Generate_DifferentSeeds_DifferentGrids()
        {
            VoxelMeteorGenerator.Generate(1, DefaultSize, out var kindA, out _, out var texA, out _);
            VoxelMeteorGenerator.Generate(2, DefaultSize, out var kindB, out _, out var texB, out _);

            try
            {
                bool differs = false;
                for (int y = 0; y < VoxelMeteorGenerator.GridSize && !differs; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize && !differs; x++)
                        if (kindA[x, y] != kindB[x, y]) differs = true;
                Assert.IsTrue(differs, "distinct seeds should produce different grids");
            }
            finally
            {
                Object.DestroyImmediate(texA);
                Object.DestroyImmediate(texB);
            }
        }

        [Test]
        public void Generate_AliveCountMatchesNonEmptyCells()
        {
            VoxelMeteorGenerator.Generate(7, DefaultSize, out var kind, out _, out var tex, out int reported);
            try
            {
                int actual = 0;
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        if (kind[x, y] != VoxelKind.Empty) actual++;
                Assert.AreEqual(actual, reported);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Generate_ProducesNonTrivialShape()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, DefaultSize, out _, out _, out var tex, out int count);
                try
                {
                    Assert.Greater(count, 20, $"seed {seed}: meteor too small ({count} cells)");
                    Assert.Less(count, 100, $"seed {seed}: meteor filled the entire grid");
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }
        }

        [Test]
        public void Generate_TextureIsCorrectSize()
        {
            VoxelMeteorGenerator.Generate(0, DefaultSize, out _, out _, out var tex, out _);
            try
            {
                Assert.AreEqual(VoxelMeteorGenerator.TextureSize, tex.width);
                Assert.AreEqual(VoxelMeteorGenerator.TextureSize, tex.height);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [TestCase(0.525f, 1)]
        [TestCase(0.75f,  2)]
        [TestCase(1.0f,   3)]
        [TestCase(1.2f,   4)]
        public void Generate_CoreCountMatchesSize(float sizeScale, int expectedCoreCount)
        {
            // Across several seeds, every meteor at the given size should
            // produce exactly expectedCoreCount core cells (assuming the pool
            // of innermost live cells is >= expectedCoreCount, which is true
            // for normal seeds since the shape always covers the center).
            for (int seed = 0; seed < 5; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, sizeScale, out var kind, out _, out var tex, out _);
                try
                {
                    int cores = 0;
                    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                            if (kind[x, y] == VoxelKind.Core) cores++;
                    Assert.AreEqual(expectedCoreCount, cores,
                        $"seed {seed} size {sizeScale}: expected {expectedCoreCount} cores, got {cores}");
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }
        }

        [TestCase(0.525f, 1)]
        [TestCase(0.75f,  3)]
        [TestCase(1.0f,   4)]
        [TestCase(1.2f,   5)]
        public void Generate_CoreHpMatchesSize(float sizeScale, int expectedCoreHp)
        {
            VoxelMeteorGenerator.Generate(42, sizeScale, out var kind, out var hp, out var tex, out _);
            try
            {
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                {
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    {
                        if (kind[x, y] == VoxelKind.Core)
                            Assert.AreEqual(expectedCoreHp, hp[x, y],
                                $"core at ({x},{y}) size {sizeScale} had hp {hp[x, y]}, expected {expectedCoreHp}");
                        else if (kind[x, y] == VoxelKind.Dirt)
                            Assert.AreEqual(1, hp[x, y],
                                $"dirt at ({x},{y}) had hp {hp[x, y]}, expected 1");
                        else
                            Assert.AreEqual(0, hp[x, y],
                                $"empty cell at ({x},{y}) had hp {hp[x, y]}, expected 0");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Generate_CoresAreLiveCells()
        {
            // Every Core cell is also a live cell (kind != Empty obviously, but
            // also: the cell must be inside the dirt shape, meaning it was
            // originally generated as part of the lump before being promoted
            // to Core).
            VoxelMeteorGenerator.Generate(99, 1.0f, out var kind, out _, out var tex, out _);
            try
            {
                bool anyCore = false;
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        if (kind[x, y] == VoxelKind.Core)
                            anyCore = true;
                Assert.IsTrue(anyCore, "seed 99 at size 1.0 should have at least one core");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
```

- [ ] **Step 2: Run the EditMode suite**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]
```

Expected: all existing EditMode tests pass (including the new core-count/core-hp cases). 110 tests + 8 new TestCase rows + 1 new test = 119 total.

- [ ] **Step 3: Run the PlayMode suite**

```
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all 36 PlayMode tests pass. Phase 1 preserves every public API `Meteor` behavior that PlayMode tests rely on.

- [ ] **Step 4: Commit Phase 1 as one atomic unit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelKind.cs Assets/Scripts/VoxelKind.cs.meta Assets/Scripts/VoxelMeteorGenerator.cs Assets/Scripts/Meteor.cs Assets/Tests/EditMode/VoxelMeteorGeneratorTests.cs
python3 tools/identity-scrub.py
git commit -m "Voxel state rewrite: VoxelKind enum + hp[,] array + size-scaled cores

Replaces bool[,] voxels with parallel VoxelKind[,] kind + int[,] hp on
Meteor. Generator.Generate takes a new sizeScale parameter and emits the
new state arrays, computing core count (1-4) and core HP (1-5) via
linear interpolation across the spawn size range. Core placement picks
from the top-(2*count) innermost live cells using a deterministic
Fisher-Yates shuffle keyed off the existing rng seed.

Damage semantics unchanged for Phase 1: dirt still dies in one hit, and
ApplyBlast/ApplyTunnel instantly clear cells on impact. HP-aware damage
lands in Phase 2. All existing tests stay green; VoxelMeteorGeneratorTests
rewritten for the new signature with 9 new parameterized TestCases for
core count and core HP by size."
```

---

## Phase 2 — `ApplyBlast` HP decrement + `DestroyResult` return

With the representation in place, damage now decrements HP instead of instant-clearing. Cells only flip to `Empty` when HP reaches 0. `ApplyBlast` returns a new `DestroyResult` struct breaking out dirt vs core kill counts. `Missile.cs` adapts to the new return type but still pays on total destroyed — Phase 6 is where it switches to core-only payout.

### Task 5: Add `DestroyResult` struct and update `ApplyBlast` body

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` (add struct, rewrite ApplyBlast body)

- [ ] **Step 1: Add the `DestroyResult` struct**

Near the top of `Meteor.cs`, above the `public class Meteor : MonoBehaviour` declaration, add:

```csharp
// Returned by Meteor.ApplyBlast and Meteor.ApplyTunnel so callers can pay
// money based on core destruction while still reporting total for visuals.
// Struct instead of tuple so call sites read naturally at every use.
public struct DestroyResult
{
    public int dirtDestroyed;
    public int coreDestroyed;
    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
}
```

- [ ] **Step 2: Change `ApplyBlast` return type and rewrite the body**

Find the `ApplyBlast` method (signature currently `public int ApplyBlast(Vector3 worldImpactPoint, float worldRadius)`). Replace it with:

```csharp
public DestroyResult ApplyBlast(Vector3 worldImpactPoint, float worldRadius)
{
    var result = new DestroyResult();
    if (dead || aliveCount == 0) return result;

    Vector3 local = transform.InverseTransformPoint(worldImpactPoint);
    const float halfExtent = 0.75f;
    float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);
    float gx = (local.x + halfExtent) * localToGrid;
    float gy = (local.y + halfExtent) * localToGrid;
    gx = Mathf.Clamp(gx, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);
    gy = Mathf.Clamp(gy, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);

    WalkInwardToAliveCell(ref gx, ref gy);

    int snapX = Mathf.Clamp(Mathf.FloorToInt(gx), 0, VoxelMeteorGenerator.GridSize - 1);
    int snapY = Mathf.Clamp(Mathf.FloorToInt(gy), 0, VoxelMeteorGenerator.GridSize - 1);
    if (kind[snapX, snapY] == VoxelKind.Empty) SnapToNearestAliveCell(ref gx, ref gy);

    float gridRadius = worldRadius * localToGrid;
    int minX = Mathf.Max(0, Mathf.FloorToInt(gx - gridRadius));
    int maxX = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gx + gridRadius));
    int minY = Mathf.Max(0, Mathf.FloorToInt(gy - gridRadius));
    int maxY = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gy + gridRadius));

    float r2 = gridRadius * gridRadius;
    bool anyPainted = false;

    for (int y = minY; y <= maxY; y++)
    {
        for (int x = minX; x <= maxX; x++)
        {
            if (kind[x, y] == VoxelKind.Empty) continue;
            float cx = x + 0.5f;
            float cy = y + 0.5f;
            float d2 = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
            if (d2 > r2) continue;

            // One point of damage per blast coverage. Blast radius is the
            // damage-scaling mechanism — wider blasts hit more cells, not
            // harder. A core with HP > 1 survives a single-cell overlap.
            hp[x, y]--;
            if (hp[x, y] > 0) continue;

            bool wasCore = kind[x, y] == VoxelKind.Core;
            kind[x, y] = VoxelKind.Empty;
            VoxelMeteorGenerator.ClearVoxel(texture, x, y);
            anyPainted = true;
            aliveCount--;

            if (wasCore) result.coreDestroyed++;
            else         result.dirtDestroyed++;

            if (voxelChunkPrefab != null)
            {
                Vector3 worldVoxel = VoxelCenterToWorld(x, y);
                var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                burst.Play();
                Destroy(burst.gameObject, 1.5f);
            }
        }
    }

    if (anyPainted) texture.Apply();

    if (aliveCount <= 0)
    {
        dead = true;
        owner?.Release(this);
    }
    return result;
}
```

- [ ] **Step 3: Do not compile yet** — `Missile.cs` still expects `int ApplyBlast(...)`. Task 6 fixes that.

---

### Task 6: Update `Missile.cs` to handle `DestroyResult`

**Files:**
- Modify: `Assets/Scripts/Missile.cs` (handle new ApplyBlast return)

- [ ] **Step 1: Update the collision handler**

Find the block inside `OnTriggerEnter2D` that reads:

```csharp
float totalRadius = impactRadius + blastRadius;
int destroyed = meteor.ApplyBlast(transform.position, totalRadius);

if (destroyed > 0)
{
    if (GameManager.Instance != null)
        GameManager.Instance.AddMoney(destroyed);

    if (floatingTextPrefab != null)
    {
        var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
        ft.Show($"+${destroyed}");
    }
}
```

Replace with:

```csharp
float totalRadius = impactRadius + blastRadius;
var result = meteor.ApplyBlast(transform.position, totalRadius);

// Phase 2 still pays on total destroyed so gameplay balance is unchanged
// until Phase 6 flips to core-only payouts. This keeps every commit
// runnable/playable.
if (result.TotalDestroyed > 0)
{
    if (GameManager.Instance != null)
        GameManager.Instance.AddMoney(result.TotalDestroyed);

    if (floatingTextPrefab != null)
    {
        var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
        ft.Show($"+${result.TotalDestroyed}");
    }
}
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 7: Update `MeteorApplyBlastTests` for `DestroyResult` return and add multi-hit core test

**Files:**
- Modify: `Assets/Tests/EditMode/MeteorApplyBlastTests.cs`

- [ ] **Step 1: Read the current test file**

Read `Assets/Tests/EditMode/MeteorApplyBlastTests.cs` to see which assertions use the current `int` return value.

- [ ] **Step 2: Update return-type assertions**

For every `Assert.AreEqual(expected, meteor.ApplyBlast(...))` pattern that was checking the old `int destroyed` return, change it to `Assert.AreEqual(expected, meteor.ApplyBlast(...).TotalDestroyed)`. Every test that stores the return in a local `int` variable: change the type to `var` (the result is now a `DestroyResult`) and use `.TotalDestroyed`.

- [ ] **Step 3: Add the multi-hit core kill test**

Append this new test method to the class (inside the existing namespace):

```csharp
[Test]
public void ApplyBlast_MultiHitCore_RequiresMultipleBlastsToKill()
{
    // Spawn a max-size meteor so the core HP is 5 (the highest in the
    // linear scale from the spec). Four consecutive blasts at the same
    // point should NOT fully destroy a core cell, five should.
    var go = new GameObject("TestMeteor", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
    var m = go.GetComponent<Meteor>();
    TestHelpers.InvokeAwake(m);
    m.Spawn(null, Vector3.zero, seed: 42, sizeScale: 1.2f);

    // Find one core cell using reflection on the private kind field.
    var kindField = typeof(Meteor).GetField("kind",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Assert.IsNotNull(kindField);
    var kind = (VoxelKind[,])kindField.GetValue(m);

    int coreX = -1, coreY = -1;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize && coreX < 0; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize && coreX < 0; x++)
            if (kind[x, y] == VoxelKind.Core) { coreX = x; coreY = y; }
    Assert.GreaterOrEqual(coreX, 0, "size 1.2 meteor must have at least one core");

    // Aim a tight blast (small radius) directly at the core's world
    // position so the blast circle covers only that one cell.
    Vector3 coreWorld = m.GetVoxelWorldPosition(coreX, coreY);
    const float tightRadius = 0.1f;

    // First 4 blasts: core still alive, dirtDestroyed may be 0 or small,
    // coreDestroyed is always 0.
    int totalCoreDestroyed = 0;
    for (int i = 0; i < 4; i++)
    {
        var result = m.ApplyBlast(coreWorld, tightRadius);
        totalCoreDestroyed += result.coreDestroyed;
    }
    Assert.AreEqual(0, totalCoreDestroyed,
        "core HP 5 must survive 4 tight blasts");
    Assert.IsTrue(m.IsVoxelPresent(coreX, coreY),
        "core cell must still be present after 4 blasts");

    // 5th blast kills the core.
    var killResult = m.ApplyBlast(coreWorld, tightRadius);
    Assert.AreEqual(1, killResult.coreDestroyed,
        "5th blast should destroy the core cell");
    Assert.IsFalse(m.IsVoxelPresent(coreX, coreY),
        "core cell must be gone after 5 blasts");

    Object.DestroyImmediate(go);
}
```

- [ ] **Step 4: Run the EditMode suite**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]
```

Expected: all EditMode tests pass, including the new `ApplyBlast_MultiHitCore_RequiresMultipleBlastsToKill` case. Total now 120.

- [ ] **Step 5: Run PlayMode suite**

```
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all 36 PlayMode tests pass. Missile behavior is unchanged from the player's perspective — it still pays total destroyed.

- [ ] **Step 6: Commit Phase 2**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs Assets/Scripts/Missile.cs Assets/Tests/EditMode/MeteorApplyBlastTests.cs
python3 tools/identity-scrub.py
git commit -m "HP-aware ApplyBlast + DestroyResult return

Meteor.ApplyBlast now decrements hp[,] by 1 per cell within the blast
circle, and only transitions the cell to Empty (clearing the texture
block) when hp reaches 0. The return type is a new DestroyResult struct
breaking out dirt vs core destruction counts, with TotalDestroyed as a
convenience for callers that still want the pre-Iter-1 total.

Missile.OnTriggerEnter2D adapts to the new return shape and continues
to pay AddMoney(TotalDestroyed) — Phase 6 will flip it to core-only.

New EditMode test: ApplyBlast_MultiHitCore_RequiresMultipleBlastsToKill
spawns a size-1.2 meteor (coreHp=5), finds a core cell via reflection
on the private kind field, and fires 5 tight blasts at the core's
position, asserting the first 4 leave it alive and the 5th kills it."
```

---

## Phase 3 — `ApplyTunnel` HP decrement + weight-on-damage

Same shape as Phase 2 for the railgun path. Weight budget consumes on damage dealt (per HP decrement), not per voxel cleared. A core HP 5 costs 5 weight to fully destroy; a dirt HP 1 costs 1.

### Task 8: Rewrite `ApplyTunnel` body for HP decrement + `DestroyResult`

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` (rewrite ApplyTunnel body)

- [ ] **Step 1: Replace the `ApplyTunnel` method**

Find the `ApplyTunnel` method. Replace the entire body with:

```csharp
public DestroyResult ApplyTunnel(
    Vector3 entryWorld,
    Vector3 worldDirection,
    int budget,
    int caliberWidth,
    out Vector3 exitWorld)
{
    var result = new DestroyResult();
    exitWorld = entryWorld;
    if (dead || aliveCount == 0 || budget <= 0) return result;

    Vector3 local = transform.InverseTransformPoint(entryWorld);
    Vector3 localDir = transform.InverseTransformDirection(worldDirection).normalized;
    const float halfExtent = 0.75f;
    float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);

    float gx = (local.x + halfExtent) * localToGrid;
    float gy = (local.y + halfExtent) * localToGrid;
    float dx = localDir.x;
    float dy = localDir.y;

    float perpX = -dy;
    float perpY = dx;
    int halfBand = Mathf.Max(0, caliberWidth - 1);

    bool anyPainted = false;
    int maxSteps = VoxelMeteorGenerator.GridSize * 4;
    bool hasEnteredGrid = false;

    for (int step = 0; step < maxSteps; step++)
    {
        if (budget <= 0) break;

        bool inGridNow =
            gx >= 0f && gx < VoxelMeteorGenerator.GridSize &&
            gy >= 0f && gy < VoxelMeteorGenerator.GridSize;
        if (inGridNow) hasEnteredGrid = true;

        for (int offset = -halfBand; offset <= halfBand; offset++)
        {
            float cellX = gx + perpX * offset;
            float cellY = gy + perpY * offset;
            int ix = Mathf.FloorToInt(cellX);
            int iy = Mathf.FloorToInt(cellY);
            if (ix < 0 || ix >= VoxelMeteorGenerator.GridSize) continue;
            if (iy < 0 || iy >= VoxelMeteorGenerator.GridSize) continue;
            if (kind[ix, iy] == VoxelKind.Empty) continue;

            // Weight is consumed per point of damage dealt, not per cell
            // cleared. A core HP 5 costs 5 weight to fully destroy.
            hp[ix, iy]--;
            budget--;

            if (hp[ix, iy] > 0)
            {
                if (budget <= 0) break;
                continue;
            }

            bool wasCore = kind[ix, iy] == VoxelKind.Core;
            kind[ix, iy] = VoxelKind.Empty;
            VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
            anyPainted = true;
            aliveCount--;

            if (wasCore) result.coreDestroyed++;
            else         result.dirtDestroyed++;

            if (voxelChunkPrefab != null)
            {
                Vector3 worldVoxel = VoxelCenterToWorld(ix, iy);
                var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                burst.Play();
                Destroy(burst.gameObject, 1.5f);
            }

            if (budget <= 0) break;
        }

        gx += dx * 0.5f;
        gy += dy * 0.5f;

        if (hasEnteredGrid)
        {
            if (gx < -0.5f || gx >= VoxelMeteorGenerator.GridSize + 0.5f) break;
            if (gy < -0.5f || gy >= VoxelMeteorGenerator.GridSize + 0.5f) break;
        }
    }

    if (anyPainted) texture.Apply();

    if (aliveCount <= 0)
    {
        dead = true;
        owner?.Release(this);
    }

    Vector3 localExit = new Vector3(
        gx / localToGrid - halfExtent,
        gy / localToGrid - halfExtent,
        0f);
    exitWorld = transform.TransformPoint(localExit);

    return result;
}
```

- [ ] **Step 2: Do not compile yet** — `RailgunRound` still expects the old `int` return.

---

### Task 9: Update `RailgunRound.cs` to handle `DestroyResult`

**Files:**
- Modify: `Assets/Scripts/Weapons/RailgunRound.cs`

- [ ] **Step 1: Update the `ApplyTunnel` call and payout**

Find the `foreach (var hit in hits)` loop body. Inside the loop, the current block reads:

```csharp
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
```

Replace with:

```csharp
var result = meteor.ApplyTunnel(
    entryWorld: hit.point,
    worldDirection: direction,
    budget: remainingWeight,
    caliberWidth: caliber,
    out _);
int damageDealt = result.TotalDestroyed;
remainingWeight -= damageDealt;
alreadyTunneled.Add(meteor);

// Phase 3 still pays on total destroyed — Phase 6 will flip to core-only.
if (damageDealt > 0 && GameManager.Instance != null)
    GameManager.Instance.AddMoney(damageDealt);
```

**Important caveat:** `damageDealt = result.TotalDestroyed` is a temporary approximation. `DestroyResult.TotalDestroyed` only counts cells that flipped to Empty — it doesn't count partial damage to a core with HP > 1. The railgun used to decrement `remainingWeight` by `consumed` (voxels cleared). For Phase 3 the budget tracking inside `ApplyTunnel` already drains correctly per HP point — the `remainingWeight -= damageDealt` outside is now the round's *view* of its remaining budget and is slightly wrong for multi-HP cores that didn't fully die in this raycast. This mismatch is acceptable as an intermediate state because (a) next frame's raycast re-enters the same meteor with the updated `remainingWeight` and the loop's `if (!meteor.IsAlive)` check will retry, and (b) the hit matrix tests catch aim regressions; drain tests catch damage regressions.

Actually — the simpler and correct fix is to have `Meteor.ApplyTunnel` return an extra field on `DestroyResult`: `damageDealt` = total HP decrement regardless of whether cells cleared. The round subtracts that from `remainingWeight` instead of `TotalDestroyed`. Do that now.

- [ ] **Step 2: Add `damageDealt` to `DestroyResult`**

In `Assets/Scripts/Meteor.cs`, update the `DestroyResult` struct:

```csharp
public struct DestroyResult
{
    public int dirtDestroyed;
    public int coreDestroyed;
    public int damageDealt; // total HP points subtracted, regardless of cell kills
    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
}
```

- [ ] **Step 3: Increment `damageDealt` in `ApplyBlast`**

In `Assets/Scripts/Meteor.cs` inside `ApplyBlast`, find the `hp[x, y]--;` line and add a line below it:

```csharp
hp[x, y]--;
result.damageDealt++;
if (hp[x, y] > 0) continue;
```

- [ ] **Step 4: Increment `damageDealt` in `ApplyTunnel`**

In the same file, inside `ApplyTunnel`, find the `hp[ix, iy]--; budget--;` pair and add a `damageDealt` increment:

```csharp
hp[ix, iy]--;
budget--;
result.damageDealt++;
```

- [ ] **Step 5: Update `RailgunRound.cs` to use `damageDealt`**

Replace the `int damageDealt = result.TotalDestroyed;` line (added in Task 9 Step 1) with:

```csharp
int damageDealt = result.damageDealt;
```

Everything else in `RailgunRound` stays as written in Step 1 — the `remainingWeight -= damageDealt` subtraction, the `AddMoney(damageDealt)` call. Phase 6 will flip `AddMoney(damageDealt)` to `AddMoney(result.coreDestroyed * CoreBaseValue)`.

- [ ] **Step 6: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 10: Update `MeteorApplyTunnelTests` and add core-HP-drains-weight test

**Files:**
- Modify: `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`

- [ ] **Step 1: Read the current test file to find int-typed ApplyTunnel callers**

Read `Assets/Tests/EditMode/MeteorApplyTunnelTests.cs`. Every test that stored the return in an `int` variable needs a type change.

- [ ] **Step 2: Update return-type assertions**

For every `int consumed = meteor.ApplyTunnel(...)` → `var result = meteor.ApplyTunnel(...)` and the following assertions use `result.TotalDestroyed` (or `result.damageDealt` where the test cared about weight consumption).

- [ ] **Step 3: Add the core-weight-drain test**

Append:

```csharp
[Test]
public void ApplyTunnel_CoreWeightCost_ConsumesBudgetPerDamagePoint()
{
    // Spawn a size-1.2 meteor so coreHp = 5. Fire a tunnel with budget
    // exactly 5 straight through a known core cell. After the shot, the
    // core should be destroyed (exactly 5 weight consumed on HP).
    var go = new GameObject("TestMeteor", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
    var m = go.GetComponent<Meteor>();
    TestHelpers.InvokeAwake(m);
    m.Spawn(null, Vector3.zero, seed: 7, sizeScale: 1.2f);

    // Find a core cell.
    var kindField = typeof(Meteor).GetField("kind",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var kind = (VoxelKind[,])kindField.GetValue(m);
    int coreX = -1, coreY = -1;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize && coreX < 0; y++)
        for (int x = 0; x < VoxelMeteorGenerator.GridSize && coreX < 0; x++)
            if (kind[x, y] == VoxelKind.Core) { coreX = x; coreY = y; }
    Assert.GreaterOrEqual(coreX, 0);

    // Fire a narrow tunnel from below the meteor (caliber 1) straight up
    // toward the core. Give it a tight budget of 5 — matching the core HP.
    Vector3 entry = m.GetVoxelWorldPosition(coreX, coreY) + new Vector3(0f, -2f, 0f);
    var result = m.ApplyTunnel(
        entryWorld: entry,
        worldDirection: Vector3.up,
        budget: 5,
        caliberWidth: 1,
        out _);

    // Multiple dirt cells may have died on the way in, but the core (HP 5)
    // is guaranteed to have consumed all its damage if hit. Assert the
    // walker dealt damageDealt points and that the core is dead OR the
    // walker ran out of budget before reaching it.
    Assert.LessOrEqual(result.damageDealt, 5, "walker must not exceed budget");
    // Sanity: if any cell died, damageDealt >= TotalDestroyed.
    Assert.GreaterOrEqual(result.damageDealt, result.TotalDestroyed);

    Object.DestroyImmediate(go);
}

[Test]
public void ApplyTunnel_EmptyCellsAreStillFree()
{
    // A shot with high budget through a meteor with lots of empty cells
    // (partially pre-destroyed) should not lose budget on the empty cells.
    var go = new GameObject("TestMeteor", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Meteor));
    var m = go.GetComponent<Meteor>();
    TestHelpers.InvokeAwake(m);
    m.Spawn(null, Vector3.zero, seed: 1, sizeScale: 0.525f);
    int initialAlive = m.AliveVoxelCount;

    // First shot: tunnel through with high budget, destroy everything in the band.
    Vector3 entry = new Vector3(-1.5f, 0f, 0f);
    m.ApplyTunnel(entry, Vector3.right, budget: 50, caliberWidth: 1, out _);

    // Second shot along the same line: should find only empty cells and
    // consume zero budget, leaving damageDealt = 0.
    var result2 = m.ApplyTunnel(entry, Vector3.right, budget: 50, caliberWidth: 1, out _);
    Assert.AreEqual(0, result2.damageDealt, "empty-cell walk must be free");

    Object.DestroyImmediate(go);
}
```

- [ ] **Step 4: Run EditMode suite**

```
mcp__UnityMCP__run_tests mode=EditMode assembly_names=["MeteorIdle.Tests.Editor"]
```

Expected: all pass.

- [ ] **Step 5: Run PlayMode suite**

```
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all 36 PlayMode tests pass. RailgunRound now hands `damageDealt` to `AddMoney` instead of `consumed`, but for single-HP dirt the two values are identical, so the hit matrix and drain tests still behave the same.

- [ ] **Step 6: Commit Phase 3**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs Assets/Scripts/Weapons/RailgunRound.cs Assets/Tests/EditMode/MeteorApplyTunnelTests.cs
python3 tools/identity-scrub.py
git commit -m "HP-aware ApplyTunnel + weight-on-damage-dealt

Meteor.ApplyTunnel walks its perpendicular band, decrements hp[,] per
cell-step, and consumes 1 unit of budget per HP point dealt. Cells only
flip to Empty when hp reaches 0 — partial damage to a high-HP core
leaves the cell present for the next shot to finish off. Empty cells
remain free (don't consume budget), preserving the 'glide through
prior tunnels' feel.

DestroyResult gains a damageDealt field (total HP points subtracted)
so RailgunRound can track its real remaining budget even when the
walker hit a core for partial damage without fully clearing it.

Two new EditMode tests:
  - ApplyTunnel_CoreWeightCost_ConsumesBudgetPerDamagePoint: size-1.2
    meteor with coreHp=5, budget=5, asserts walker stays within budget.
  - ApplyTunnel_EmptyCellsAreStillFree: second shot along a cleared
    tunnel consumes zero budget."
```

---

## Phase 4 — Core visuals: palette + visual verify gate

Cells painted as cores already use the `CoreTopColor`/`CoreBottomColor` palette from Task 2, so the code path is already live after Phase 1. This phase is a visual verification pause, not new code — but the voxel aesthetic memory (`feedback_visual_verification_for_art`) requires a screenshot-and-pause before advancing, and the user should eyeball the read.

### Task 11: Visual verify gate — screenshot the core palette

**Files:** none (verify-only phase)

- [ ] **Step 1: Kick off a dev WebGL build via execute_code**

```
mcp__UnityMCP__execute_code action="execute" code="BuildScripts.BuildWebGLDev(); return \"dev build dispatched\";"
```

Expected: build runs for ~30–150 seconds inside the editor. Unity MCP becomes unresponsive during the build; wait and poll:

```
mcp__UnityMCP__read_console count=5 filter_text="BuildScripts"
```

Expected eventual log: `[BuildScripts] WebGL dev build result: Succeeded, size: ~14 MB, duration: ...`.

- [ ] **Step 2: Serve the dev build**

```
tools/serve-webgl-dev.sh
```

Use Bash with `run_in_background: true` AND `dangerouslyDisableSandbox: true` (the Claude Code sandbox blocks socket bind; this is documented in the Iter 0 shipping notes and in memory).

Verify with:
```bash
curl -s -o /dev/null -w "server: %{http_code}\n" http://localhost:8000/
```

Expected: `server: 200`.

- [ ] **Step 3: Navigate via chrome-devtools-mcp**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__new_page url="http://localhost:8000/"
mcp__plugin_chrome-devtools-mcp_chrome-devtools__wait_for text=["Meteor Idle"] timeout=20000
mcp__plugin_chrome-devtools-mcp_chrome-devtools__take_screenshot
```

Expected: a screenshot showing falling meteors with visibly distinct **muted red core cells** embedded in the brown dirt mass, and a "Development Build" watermark bottom-right.

- [ ] **Step 4: Hand back to user for approval**

Present the screenshot to the user with the specific question: "Do cores read as cores at a glance? Any tweaks to the palette before we wire in targeting?"

Wait for explicit approval. If the user wants palette tweaks, update `CoreTopColor`/`CoreBottomColor` in `VoxelMeteorGenerator.cs`, re-run the dev build, re-screenshot. No commit needed for the verify gate itself — any palette tweak is a one-line edit that commits in Task 12 if accepted.

- [ ] **Step 5: Close the browser tab and kill the server**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__close_page pageId=2
```

Then kill the background `python3 -m http.server 8000` process with `pkill`. Per the `feedback_close_test_browser_tabs` memory.

- [ ] **Step 6: (Conditional) Commit palette tweak if the user requested one**

Only if the user rejected the baseline and requested a specific palette change:

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelMeteorGenerator.cs
python3 tools/identity-scrub.py
git commit -m "Tune core palette per visual verify"
```

If the user approved the baseline, no commit for this phase.

---

## Phase 5 — Targeting: core-only, with hold-fire when no valid target

Strictly-core aim. Turrets only target meteors with `HasLiveCore == true`. Missile and railgun pick exclusively from core voxels via the new `PickRandomCoreVoxel`. The railgun's post-fire aim invalidation from Iter 0 is removed so consecutive shots hammer the same core until it dies.

### Task 12: Add `HasLiveCore`, `CoreVoxelCount`, `PickRandomCoreVoxel` to `Meteor`

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`

- [ ] **Step 1: Add a `coreVoxelCount` cached field**

In the private field block (near `int aliveCount`), add:

```csharp
private int coreVoxelCount;
```

- [ ] **Step 2: Maintain `coreVoxelCount` on core placement in `Spawn`**

After the `VoxelMeteorGenerator.Generate` call in `Spawn`, count live cores and cache:

```csharp
VoxelMeteorGenerator.Generate(seed, sizeScale, out kind, out hp, out texture, out aliveCount);
coreVoxelCount = 0;
for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        if (kind[x, y] == VoxelKind.Core) coreVoxelCount++;
```

- [ ] **Step 3: Decrement `coreVoxelCount` when a core dies**

In `ApplyBlast`, find the `if (wasCore) result.coreDestroyed++;` line and add:

```csharp
if (wasCore) { result.coreDestroyed++; coreVoxelCount--; }
else         { result.dirtDestroyed++; }
```

Same pattern in `ApplyTunnel` where `wasCore` is checked.

- [ ] **Step 4: Add the three new public members**

Near the existing `public int AliveVoxelCount => aliveCount;` property, add:

```csharp
public int CoreVoxelCount => coreVoxelCount;
public bool HasLiveCore => coreVoxelCount > 0;

public bool PickRandomCoreVoxel(out int gx, out int gy)
{
    gx = 0; gy = 0;
    if (kind == null || coreVoxelCount == 0) return false;

    int targetIndex = Random.Range(0, coreVoxelCount);
    int seen = 0;
    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
    {
        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
        {
            if (kind[x, y] != VoxelKind.Core) continue;
            if (seen == targetIndex)
            {
                gx = x; gy = y;
                return true;
            }
            seen++;
        }
    }
    return false;
}
```

- [ ] **Step 5: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=10
```

Expected: zero errors.

---

### Task 13: `TurretBase.FindTarget` filters by `HasLiveCore`

**Files:**
- Modify: `Assets/Scripts/TurretBase.cs`

- [ ] **Step 1: Update the filter**

Find the `foreach (var m in meteorSpawner.ActiveMeteors)` block inside `FindTarget`. Replace:

```csharp
if (m == null || !m.IsAlive) continue;
```

with:

```csharp
if (m == null || !m.IsAlive || !m.HasLiveCore) continue;
```

That's the only change.

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 14: `MissileTurret.Fire` picks exclusively from core voxels

**Files:**
- Modify: `Assets/Scripts/MissileTurret.cs`

- [ ] **Step 1: Replace the voxel pick and remove the fallback path**

Find the `Fire` method. Replace the block:

```csharp
int gx = 0, gy = 0;
bool hasVoxel = target.PickRandomPresentVoxel(out gx, out gy);
```

with:

```csharp
int gx = 0, gy = 0;
bool hasVoxel = target.PickRandomCoreVoxel(out gx, out gy);
if (!hasVoxel)
{
    // Target's last core died in the same frame between FindTarget and
    // Fire — rare race. Skip this shot entirely; next tick picks a new
    // target. The dumb barrel.up straight-up shot from the pre-Iter-1
    // fallback path is gone — Iter 1 promises core-only aim.
    return;
}
```

Then remove the remaining `if (hasVoxel) { ... } else { dir = barrel.up; }` branch (which referenced the fallback) so the following lines read straight through:

```csharp
float speed = ProjectileSpeed;

Vector3 voxelWorld = target.GetVoxelWorldPosition(gx, gy);
Vector2 leadPoint = AimSolver.PredictInterceptPoint(
    (Vector2)spawnPos,
    (Vector2)voxelWorld,
    target.Velocity,
    speed);
Vector2 dir = (leadPoint - (Vector2)spawnPos).normalized;
if (dir.sqrMagnitude < 0.0001f) dir = barrel.up;

missile.Launch(
    this,
    spawnPos,
    dir * speed,
    statsInstance.damage.CurrentValue,
    statsInstance.blastRadius.CurrentValue,
    target, // homingTarget
    gx,
    gy,
    statsInstance.homing.CurrentValue);

if (muzzleFlash != null) muzzleFlash.Play();
```

Notice that `homingTarget` is now always `target` (no longer conditional on `hasVoxel`) — we already early-returned if there was no voxel to pick.

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 15: `RailgunTurret.RefreshAimVoxel` picks cores + remove post-fire invalidation

**Files:**
- Modify: `Assets/Scripts/RailgunTurret.cs`

- [ ] **Step 1: Change `RefreshAimVoxel` to call `PickRandomCoreVoxel`**

Find the `RefreshAimVoxel` method. Replace:

```csharp
aimVoxelTarget = target;
hasAimVoxel = target.PickRandomPresentVoxel(out aimVoxelGx, out aimVoxelGy);
```

with:

```csharp
aimVoxelTarget = target;
hasAimVoxel = target.PickRandomCoreVoxel(out aimVoxelGx, out aimVoxelGy);
```

- [ ] **Step 2: Remove the post-fire aim invalidation**

Find the block inside `Update` that reads:

```csharp
if (chargeTimer >= chargeDuration && alignmentErr <= aimAlignmentDeg)
{
    Fire(target);
    chargeTimer = 0f;
    if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
    // Invalidate the cached aim voxel so the next Update picks a
    // fresh one. Without this, subsequent shots would re-target the
    // same voxel (or, if it's now dead, the meteor center — which is
    // exactly the tunnel-through bug we're avoiding).
    aimVoxelTarget = null;
    hasAimVoxel = false;
}
```

Remove the last three lines and the comment, so it reads:

```csharp
if (chargeTimer >= chargeDuration && alignmentErr <= aimAlignmentDeg)
{
    Fire(target);
    chargeTimer = 0f;
    if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
    // Do NOT invalidate aimVoxel* here. With multi-HP cores, we want
    // consecutive shots to hammer the same core until it dies. The
    // RefreshAimVoxel → IsVoxelPresent check handles "voxel died,
    // re-pick" naturally when a core's HP drops to 0 and it flips
    // to Empty. See docs/superpowers/specs/2026-04-11-asteroid-cores-design.md
    // "Targeting priority" section for rationale.
}
```

- [ ] **Step 3: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 16: Update existing PlayMode tests for core-only targeting semantics

**Files:**
- Modify: `Assets/Tests/PlayMode/TurretAimIntegrationTests.cs`

- [ ] **Step 1: Update the `RunHitTest` and `RunHitTestWithFireRate` helpers**

Currently these helpers assert `meteor.AliveVoxelCount < initialVoxels`. Change both helpers to assert `meteor.CoreVoxelCount < initialCores`:

In `RunHitTest`, find:

```csharp
var meteor = SpawnTestMeteor(meteorPos);
SetMeteorVelocity(meteor, meteorVel);
int initialVoxels = meteor.AliveVoxelCount;
Assert.Greater(initialVoxels, 0, "meteor must have live voxels at spawn");
```

Replace with:

```csharp
var meteor = SpawnTestMeteor(meteorPos);
SetMeteorVelocity(meteor, meteorVel);
int initialCores = meteor.CoreVoxelCount;
Assert.Greater(initialCores, 0,
    "meteor must have live cores at spawn (generator guarantees >=1 for any size)");
```

And find the final assertion:

```csharp
Assert.Less(
    meteor.AliveVoxelCount, initialVoxels,
    $"{weapon} at speedLvl={speedLevel} homingLvl={homingLevel} should have hit " +
    $"meteor (pos={meteorPos}, vel={meteorVel}) within {budget:F2}s " +
    $"(initial={initialVoxels}, now={meteor.AliveVoxelCount})");
```

Replace with:

```csharp
Assert.Less(
    meteor.CoreVoxelCount, initialCores,
    $"{weapon} at speedLvl={speedLevel} homingLvl={homingLevel} should have dropped a core " +
    $"(meteor pos={meteorPos}, vel={meteorVel}) within {budget:F2}s " +
    $"(initialCores={initialCores}, now={meteor.CoreVoxelCount})");
```

Same change in `RunHitTestWithFireRate` (duplicate the pattern).

- [ ] **Step 2: Update the `Railgun_BaseSpeed_HitsDriftingMeteor` standalone test**

This test is defined separately from the `Hit_*` helpers. Find its final assertion:

```csharp
Assert.Less(
    meteor.AliveVoxelCount, initialVoxels,
    $"railgun round should have hit the drifting meteor within 3s " +
    $"(initial={initialVoxels}, now={meteor.AliveVoxelCount})");
```

Replace with:

```csharp
Assert.Less(
    meteor.CoreVoxelCount, initialCores,
    $"railgun round should have dropped a core on the drifting meteor within 3s " +
    $"(initialCores={initialCores}, now={meteor.CoreVoxelCount})");
```

And change the earlier `int initialVoxels = meteor.AliveVoxelCount;` to `int initialCores = meteor.CoreVoxelCount;`, with the same "cores > 0 at spawn" precondition assert.

- [ ] **Step 3: Update the `Railgun_MultipleShots_DrainsStationaryMeteor` test**

Find the current assertion:

```csharp
int finalVoxels = meteor.AliveVoxelCount;
int destroyed = initialVoxels - finalVoxels;

Assert.Greater(
    destroyed, 20,
    $"railgun should destroy more than 20 voxels over ~25 shots at weight 4/shot " +
    ...);
```

Replace with:

```csharp
int finalCores = meteor.CoreVoxelCount;

Assert.AreEqual(
    0, finalCores,
    $"railgun should drop all cores of a stationary meteor over the time budget " +
    $"(initialCores={initialCores}, finalCores={finalCores}). If this is stuck > 0 the " +
    $"railgun is missing cores or the post-fire aim invalidation wasn't removed.");
```

And the earlier `int initialVoxels = meteor.AliveVoxelCount;` becomes `int initialCores = meteor.CoreVoxelCount;`.

Also change the early precondition:

```csharp
Assert.Greater(initialVoxels, 30, "meteor should have plenty of voxels for this test");
```

to:

```csharp
Assert.Greater(initialCores, 0, "meteor should have at least one live core");
```

- [ ] **Step 4: Run PlayMode suite**

```
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all 36 tests pass. The hit matrix now asserts core drops; the drain test asserts all cores eventually die.

- [ ] **Step 5: Run EditMode suite (sanity)**

```
mcp__UnityMCP__run_tests mode=EditMode
```

Expected: all pass.

- [ ] **Step 6: Commit Phase 5**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs Assets/Scripts/TurretBase.cs Assets/Scripts/MissileTurret.cs Assets/Scripts/RailgunTurret.cs Assets/Tests/PlayMode/TurretAimIntegrationTests.cs
python3 tools/identity-scrub.py
git commit -m "Core-only targeting: HasLiveCore filter, PickRandomCoreVoxel, hold fire

TurretBase.FindTarget now filters by HasLiveCore as well as IsAlive.
A meteor with no live cores drifts off-screen untargeted.

MissileTurret.Fire calls PickRandomCoreVoxel and returns early (no
shot) if the target's last core died in the same frame before Fire
could pick. The barrel.up straight-shot fallback is removed — core-
only aim is a hard promise in Iter 1.

RailgunTurret.RefreshAimVoxel calls PickRandomCoreVoxel. The post-fire
aim invalidation added in Iter 0 is REMOVED here — with multi-HP cores,
we want consecutive shots to hammer the same core until it dies, and
the RefreshAimVoxel → IsVoxelPresent check handles the re-pick naturally
once HP reaches 0 and the cell flips to Empty. This is a deliberate
partial reversal of the Iter 0 fix, safe because the aim target is now
a specific live core rather than the meteor center.

Meteor gains CoreVoxelCount, HasLiveCore, and PickRandomCoreVoxel public
API, plus an internal cached coreVoxelCount field decremented on core
kills in both ApplyBlast and ApplyTunnel.

TurretAimIntegrationTests updated: RunHitTest / RunHitTestWithFireRate
helpers assert CoreVoxelCount decreased (not AliveVoxelCount).
Railgun_BaseSpeed_HitsDriftingMeteor and
Railgun_MultipleShots_DrainsStationaryMeteor updated likewise; the
drain test now asserts all cores drop to 0."
```

---

## Phase 6 — Economy split: core-only payouts

Dirt pays $0. Core destruction pays `coreDestroyed * CoreBaseValue`. `CoreBaseValue = 5` lives as a `const` on `GameManager` so Iter 2 can lift it into a type-multiplier field later.

### Task 17: Add `CoreBaseValue` constant to `GameManager`

**Files:**
- Modify: `Assets/Scripts/GameManager.cs`

- [ ] **Step 1: Add the constant**

Near the top of the class (after `public int Money { get; private set; }`), add:

```csharp
// Base dollar value paid per core voxel destroyed. Iter 2 will make this
// a per-AsteroidType field; Iter 3 will re-route the payout through drone
// collection; for Iter 1 it's a direct AddMoney multiplier.
public const int CoreBaseValue = 5;
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 18: `Missile.cs` pays core-only on impact

**Files:**
- Modify: `Assets/Scripts/Missile.cs`

- [ ] **Step 1: Switch the payout formula**

Find the block updated in Task 6:

```csharp
var result = meteor.ApplyBlast(transform.position, totalRadius);

if (result.TotalDestroyed > 0)
{
    if (GameManager.Instance != null)
        GameManager.Instance.AddMoney(result.TotalDestroyed);

    if (floatingTextPrefab != null)
    {
        var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
        ft.Show($"+${result.TotalDestroyed}");
    }
}
```

Replace with:

```csharp
var result = meteor.ApplyBlast(transform.position, totalRadius);

int payout = result.coreDestroyed * GameManager.CoreBaseValue;
if (payout > 0)
{
    if (GameManager.Instance != null)
        GameManager.Instance.AddMoney(payout);

    if (floatingTextPrefab != null)
    {
        var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
        ft.Show($"+${payout}");
    }
}
```

Note the floating text now shows the dollar payout, not the voxel count. Previously `+$destroyed` happened to coincidentally be the dollar count because dirt paid $1 each. Now it's explicit.

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 19: `RailgunRound.cs` pays core-only per meteor hit

**Files:**
- Modify: `Assets/Scripts/Weapons/RailgunRound.cs`

- [ ] **Step 1: Switch the payout formula**

Find the block updated in Task 9:

```csharp
int damageDealt = result.damageDealt;
remainingWeight -= damageDealt;
alreadyTunneled.Add(meteor);

// Phase 3 still pays on total destroyed — Phase 6 will flip to core-only.
if (damageDealt > 0 && GameManager.Instance != null)
    GameManager.Instance.AddMoney(damageDealt);
```

Replace with:

```csharp
int damageDealt = result.damageDealt;
remainingWeight -= damageDealt;
alreadyTunneled.Add(meteor);

int payout = result.coreDestroyed * GameManager.CoreBaseValue;
if (payout > 0 && GameManager.Instance != null)
    GameManager.Instance.AddMoney(payout);
```

- [ ] **Step 2: Compile check**

```
mcp__UnityMCP__refresh_unity scope=scripts compile=request
mcp__UnityMCP__read_console types=["error"] count=5
```

Expected: zero errors.

---

### Task 20: Add PlayMode test for dirt-only hit pays $0 and core hit pays `CoreBaseValue`

**Files:**
- Modify: `Assets/Tests/PlayMode/TurretAimIntegrationTests.cs`

- [ ] **Step 1: Add a PlayMode test for payout**

Append this test to the `TurretAimIntegrationTests` class:

```csharp
[UnityTest]
public IEnumerator Railgun_CoreHit_PaysCoreBaseValue()
{
    yield return SetupScene();

    // GameManager may already exist; capture starting money.
    int startMoney = _gameManager.Money;

    var slot = SpawnTestSlot(new Vector3(0f, -5f, 0f), WeaponType.Railgun);
    var turret = (RailgunTurret)slot.ActiveTurret;
    var spawner = SpawnTestSpawner();
    turret.SetRuntimeRefs(spawner);

    // High speed + high fire rate so a shot lands in the window. Weight
    // high enough to chew through core HP 5 + any incidental dirt.
    turret.Stats.speed.level = 20;
    turret.Stats.rotationSpeed.level = 100;
    turret.Stats.fireRate.level = 100;
    turret.Stats.weight.level = 10;

    var meteor = SpawnTestMeteor(new Vector3(0f, 5f, 0f));
    SetMeteorVelocity(meteor, Vector2.zero);
    int initialCores = meteor.CoreVoxelCount;
    Assert.Greater(initialCores, 0);
    GetSpawnerActiveList(spawner).Add(meteor);

    ForceRailgunReady(turret);

    yield return new WaitForSeconds(6f);

    int finalCores = meteor.CoreVoxelCount;
    int coresDestroyed = initialCores - finalCores;
    int expectedPayout = coresDestroyed * GameManager.CoreBaseValue;
    int actualPayout = _gameManager.Money - startMoney;

    Assert.Greater(coresDestroyed, 0, "test is moot if no cores died");
    Assert.AreEqual(expectedPayout, actualPayout,
        $"railgun should pay exactly coresDestroyed * CoreBaseValue " +
        $"(cores={coresDestroyed}, expected={expectedPayout}, actual={actualPayout})");

    TeardownScene();
}
```

- [ ] **Step 2: Run both suites**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Expected: all green.

- [ ] **Step 3: Commit Phase 6**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/GameManager.cs Assets/Scripts/Missile.cs Assets/Scripts/Weapons/RailgunRound.cs Assets/Tests/PlayMode/TurretAimIntegrationTests.cs
python3 tools/identity-scrub.py
git commit -m "Core-only economy: \$5 per core voxel, dirt pays zero

GameManager.CoreBaseValue is a new public const set to 5 for Iter 1;
Iter 2 will lift it into a per-type multiplier via AsteroidType.

Missile.OnTriggerEnter2D computes payout = result.coreDestroyed *
CoreBaseValue and only calls AddMoney / shows floating text when
payout > 0. Dirt destruction pays 0 and shows no floater — the
visual reward for dirt break is the explosion + chunks, not dollars.

RailgunRound.Update does the same per-meteor-hit in its raycast loop:
payout = result.coreDestroyed * CoreBaseValue.

New PlayMode test Railgun_CoreHit_PaysCoreBaseValue asserts the exact
dollar math (coresDestroyed * CoreBaseValue) against GameManager.Money
delta over a railgun drain."
```

---

## Phase 7 — Review, verify, ship

### Task 21: Dispatch the code-reviewer agent

**Files:** none directly.

- [ ] **Step 1: Dispatch `feature-dev:code-reviewer`**

Use the `Agent` tool with `subagent_type: "feature-dev:code-reviewer"`, `description: "Review iter/asteroid-cores branch"`, and a prompt that includes:
- Spec path: `docs/superpowers/specs/2026-04-11-asteroid-cores-design.md`
- Plan path: `docs/superpowers/plans/2026-04-11-asteroid-cores.md`
- Branch list from `git diff main...iter/asteroid-cores --stat`
- Specific asks:
  - Does `ApplyBlast`/`ApplyTunnel` correctly handle the HP decrement + core-death path?
  - Is `coreVoxelCount` maintained correctly across both damage paths (nothing drifts?)
  - Is the core placement deterministic and within-shape across seeds?
  - Does the railgun's removed post-fire invalidation regress the Iter 0 tunnel-through bug?
  - Does the payout math (`coreDestroyed * CoreBaseValue`) wire correctly in both turret paths?
  - Are the existing Iter 0 tests correctly updated to core-count assertions?
- Report format: high-confidence issues only, under 600 words.

- [ ] **Step 2: Address findings**

For each real issue flagged, make a targeted commit. Do not batch.

---

### Task 22: Dev WebGL verify

**Files:** none directly.

- [ ] **Step 1: Build dev WebGL**

```
mcp__UnityMCP__execute_code action="execute" code="BuildScripts.BuildWebGLDev(); return \"dev build dispatched\";"
```

Wait for `[BuildScripts] WebGL dev build result: Succeeded` in the console.

- [ ] **Step 2: Serve locally**

Run `tools/serve-webgl-dev.sh` via Bash with `run_in_background: true` AND `dangerouslyDisableSandbox: true`.

Verify HTTP 200 with curl.

- [ ] **Step 3: Navigate via chrome-devtools-mcp, take gameplay screenshot, check console**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__new_page url="http://localhost:8000/"
mcp__plugin_chrome-devtools-mcp_chrome-devtools__wait_for text=["Meteor Idle"]
mcp__plugin_chrome-devtools-mcp_chrome-devtools__take_screenshot
mcp__plugin_chrome-devtools-mcp_chrome-devtools__list_console_messages types=["error","warn"]
```

Expected: screenshot shows meteors with visibly red cores, missiles/rounds hitting cores and ignoring coreless meteors, money ticking up on core kills. Zero runtime errors in the console.

- [ ] **Step 4: Close tab, kill server**

```
mcp__plugin_chrome-devtools-mcp_chrome-devtools__close_page pageId=<id>
pkill -f "python3 -m http.server 8000"
```

- [ ] **Step 5: Hand screenshot + summary to user for hands-on verify**

Present the results and wait for explicit approval before Task 23.

---

### Task 23: Ship — merge, prod build, deploy, push, verify live

**Files:** `docs/superpowers/roadmap.md` (mark Iter 1 shipped).

- [ ] **Step 1: Mark roadmap**

Edit `docs/superpowers/roadmap.md` — change the `Iter 1 — Asteroid cores` header to append ` ✅ shipped YYYY-MM-DD` (today's date). Bump the "Last revised" date at the top of the file.

- [ ] **Step 2: Commit roadmap update**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add docs/superpowers/roadmap.md
python3 tools/identity-scrub.py
git commit -m "Mark Iter 1 asteroid cores as shipped"
```

- [ ] **Step 3: Full branch identity scrub**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
python3 tools/identity-scrub.py main..HEAD
```

Expected: `identity scrub: clean`.

- [ ] **Step 4: Fast-forward main**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git checkout main
git merge --ff-only iter/asteroid-cores
git push origin main
```

- [ ] **Step 5: Build prod WebGL via execute_code**

```
mcp__UnityMCP__execute_code action="execute" code="BuildScripts.BuildWebGL(); return \"prod build dispatched\";"
```

Wait for `[BuildScripts] WebGL build result: Succeeded` in the Unity console. The method deletes any stale `.dev-build-marker` on success so the output is unambiguously deployable.

- [ ] **Step 6: Run deploy-webgl.sh**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
tools/deploy-webgl.sh
```

Run with `dangerouslyDisableSandbox: true` because the script uses `mktemp` on a path the sandbox blocks. Expected: identity scrub clean, worktree commit staged, prints the push command.

- [ ] **Step 7: Push gh-pages**

```bash
git -C "/Users/matt/dev/Unity/Meteor Idle/../Meteor-Idle-gh-pages" push origin gh-pages
```

- [ ] **Step 8: Verify live URL**

```bash
curl -s -o /dev/null -w "live: %{http_code}\n" "https://muwamath.github.io/Meteor-Idle/?cachebust=$(date +%s)"
curl -sI "https://muwamath.github.io/Meteor-Idle/Build/WebGL.wasm.unityweb?cachebust=$(date +%s)" | grep -iE "content-length|last-modified"
```

Expected: `live: 200`, `last-modified` within the last minute, `content-length` matching the fresh wasm.

- [ ] **Step 9: Delete shipped branch**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git branch -d iter/asteroid-cores
```

---

## Summary

- **Phase 1 (Tasks 1–4):** Voxel state rewrite. 4 files (1 new), ~350 LOC. One atomic commit.
- **Phase 2 (Tasks 5–7):** `ApplyBlast` HP + `DestroyResult`. 3 files, ~180 LOC. One commit.
- **Phase 3 (Tasks 8–10):** `ApplyTunnel` HP + weight-on-damage. 3 files, ~160 LOC. One commit.
- **Phase 4 (Task 11):** Visual verify gate. 0 code commits (unless palette tweak).
- **Phase 5 (Tasks 12–16):** Core-only targeting + test updates. 5 files, ~200 LOC. One commit.
- **Phase 6 (Tasks 17–20):** Core-only economy + payout test. 4 files, ~80 LOC. One commit.
- **Phase 7 (Tasks 21–23):** Review, verify, ship.

**Total:** 23 tasks, ~7 commits on `iter/asteroid-cores` (+ the spec commit already there). Test coverage grows from 110 EditMode + 36 PlayMode = 146 today to ~158+ after Iter 1 (9 new core tests + 1 payout integration test + existing test updates).
