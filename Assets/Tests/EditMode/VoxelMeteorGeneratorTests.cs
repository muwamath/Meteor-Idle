using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class VoxelMeteorGeneratorTests
    {
        [Test]
        public void Generate_SameSeed_ProducesIdenticalGrid()
        {
            VoxelMeteorGenerator.Generate(1234, out var a, out var texA, out int countA);
            VoxelMeteorGenerator.Generate(1234, out var b, out var texB, out int countB);

            try
            {
                Assert.AreEqual(countA, countB);
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        Assert.AreEqual(a[x, y], b[x, y], $"cell ({x},{y}) diverged");
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
            VoxelMeteorGenerator.Generate(1, out var a, out var texA, out _);
            VoxelMeteorGenerator.Generate(2, out var b, out var texB, out _);

            try
            {
                bool differs = false;
                for (int y = 0; y < VoxelMeteorGenerator.GridSize && !differs; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize && !differs; x++)
                        if (a[x, y] != b[x, y]) differs = true;
                Assert.IsTrue(differs, "distinct seeds should produce different grids");
            }
            finally
            {
                Object.DestroyImmediate(texA);
                Object.DestroyImmediate(texB);
            }
        }

        [Test]
        public void Generate_AliveCountMatchesGrid()
        {
            VoxelMeteorGenerator.Generate(7, out var grid, out var tex, out int reported);
            try
            {
                int actual = 0;
                for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                    for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                        if (grid[x, y]) actual++;
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
            // A meteor with near-zero or completely filled cells would be a bug.
            // Expect something like 30–90 cells out of 100 for a normal seed.
            for (int seed = 0; seed < 10; seed++)
            {
                VoxelMeteorGenerator.Generate(seed, out _, out var tex, out int count);
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
            VoxelMeteorGenerator.Generate(0, out _, out var tex, out _);
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
    }
}
