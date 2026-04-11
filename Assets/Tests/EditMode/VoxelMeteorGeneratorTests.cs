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
    }
}
