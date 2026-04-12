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
            // produce exactly expectedCoreCount core cells.
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

        // Linear interpolation across [0.525, 1.2] → [1, 5], rounded to int:
        //   0.525 → 1 | 0.75 → 2 | 1.0 → 4 | 1.2 → 5
        // The spec's HP table listed 3 at size 0.75 by mistake; the formula
        // is the source of truth.
        [TestCase(0.525f, 1)]
        [TestCase(0.75f,  2)]
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
        public void Generate_AlwaysPlacesAtLeastOneCore()
        {
            // Every seed at every size should place at least one core.
            for (int seed = 0; seed < 10; seed++)
            {
                foreach (var size in new[] { 0.525f, 0.75f, 1.0f, 1.2f })
                {
                    VoxelMeteorGenerator.Generate(seed, size, out var kind, out _, out var tex, out _);
                    try
                    {
                        bool anyCore = false;
                        for (int y = 0; y < VoxelMeteorGenerator.GridSize && !anyCore; y++)
                            for (int x = 0; x < VoxelMeteorGenerator.GridSize && !anyCore; x++)
                                if (kind[x, y] == VoxelKind.Core) anyCore = true;
                        Assert.IsTrue(anyCore, $"seed {seed} size {size} should have at least one core");
                    }
                    finally
                    {
                        Object.DestroyImmediate(tex);
                    }
                }
            }
        }

        // ====================================================================
        // Iter 2 — placement pass tests (stone clumps, gold, explosives)
        // ====================================================================

        private static MaterialRegistry LoadRegistry()
        {
            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            Assert.IsNotNull(registry, "MaterialRegistry.asset must exist (Phase 1)");
            return registry;
        }

        [Test]
        public void Generate_WithRegistry_EmitsMaterialArrayMatchingDirtAndCore()
        {
            var registry = LoadRegistry();
            VoxelMeteorGenerator.Generate(
                seed: 42, sizeScale: 1f, registry: registry,
                out var kind, out _, out var material,
                out var tex, out _);
            try
            {
                // Every non-empty cell must have a material reference.
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        if (kind[x, y] != VoxelKind.Empty)
                            Assert.IsNotNull(material[x, y],
                                $"cell ({x},{y}) has kind {kind[x, y]} but no material");
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void Generate_WithRegistry_DeterministicAcrossCalls()
        {
            var registry = LoadRegistry();
            VoxelMeteorGenerator.Generate(12345, 1f, registry,
                out var kindA, out _, out var materialA, out var texA, out _);
            VoxelMeteorGenerator.Generate(12345, 1f, registry,
                out var kindB, out _, out var materialB, out var texB, out _);
            try
            {
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    {
                        Assert.AreEqual(kindA[x, y], kindB[x, y]);
                        Assert.AreSame(materialA[x, y], materialB[x, y]);
                    }
            }
            finally
            {
                Object.DestroyImmediate(texA);
                Object.DestroyImmediate(texB);
            }
        }

        [Test]
        public void Generate_StoneVeinNeverExceedsTwoDeep()
        {
            var registry = LoadRegistry();
            var stoneMat = registry.GetByName("Stone");

            // Sweep many seeds at largest size (most clumps, most growth) to
            // catch any clump that violates the 2-deep cap.
            for (int seed = 1; seed <= 200; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
                    out _, out _, out var material, out var tex, out _);
                try
                {
                    for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    {
                        for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        {
                            if (material[x, y] != stoneMat) continue;
                            // Every stone cell must have a non-stone neighbor
                            // within manhattan distance 2 (or be near OOB).
                            bool hasEscape = false;
                            for (int dy = -2; dy <= 2 && !hasEscape; dy++)
                            {
                                for (int dx = -2; dx <= 2 && !hasEscape; dx++)
                                {
                                    if (Mathf.Abs(dx) + Mathf.Abs(dy) > 2) continue;
                                    if (dx == 0 && dy == 0) continue;
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    if (nx < 0 || ny < 0 ||
                                        nx >= VoxelMeteorGenerator.GridSize ||
                                        ny >= VoxelMeteorGenerator.GridSize)
                                    { hasEscape = true; break; }
                                    if (material[nx, ny] != stoneMat) hasEscape = true;
                                }
                            }
                            Assert.IsTrue(hasEscape,
                                $"seed {seed}: stone cell ({x},{y}) has no non-stone neighbor within 2 manhattan steps — clump exceeds 2-deep cap");
                        }
                    }
                }
                finally { Object.DestroyImmediate(tex); }
            }
        }

        [Test]
        public void Generate_ExplosivesNeverAdjacentToOtherExplosives()
        {
            var registry = LoadRegistry();
            var explosiveMat = registry.GetByName("Explosive");

            for (int seed = 1; seed <= 500; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
                    out _, out _, out var material, out var tex, out _);
                try
                {
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
                                    if (nx < 0 || ny < 0 ||
                                        nx >= VoxelMeteorGenerator.GridSize ||
                                        ny >= VoxelMeteorGenerator.GridSize) continue;
                                    Assert.AreNotSame(explosiveMat, material[nx, ny],
                                        $"seed {seed}: adjacent explosives at ({x},{y}) and ({nx},{ny})");
                                }
                            }
                        }
                    }
                }
                finally { Object.DestroyImmediate(tex); }
            }
        }

        [Test]
        public void Generate_GoldPrefersStoneNeighbors_WhenStonePresent()
        {
            var registry = LoadRegistry();
            var stoneMat = registry.GetByName("Stone");
            var goldMat  = registry.GetByName("Gold");

            int totalGold = 0;
            int goldNextToStone = 0;
            for (int seed = 1; seed <= 1000; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, 1.2f, registry,
                    out _, out _, out var material, out var tex, out _);
                try
                {
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
                                    if (nx < 0 || ny < 0 ||
                                        nx >= VoxelMeteorGenerator.GridSize ||
                                        ny >= VoxelMeteorGenerator.GridSize) continue;
                                    if (material[nx, ny] == stoneMat) nextToStone = true;
                                }
                            }
                            if (nextToStone) goldNextToStone++;
                        }
                    }
                }
                finally { Object.DestroyImmediate(tex); }
            }

            if (totalGold == 0)
            {
                Assert.Inconclusive("no gold rolled across 1000 seeds — increase sample or weight");
                return;
            }
            float ratio = (float)goldNextToStone / totalGold;
            Assert.Greater(ratio, 0.6f,
                $"only {goldNextToStone}/{totalGold} gold cells were adjacent to stone — preference broken");
        }
    }
}
