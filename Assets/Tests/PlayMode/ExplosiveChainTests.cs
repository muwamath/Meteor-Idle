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
    //
    // Why reflection: the generator's spawn weights make explosives rare and
    // scattered, so seeded asteroid generation can't deterministically place
    // a chain. Forcing the grid lets each test set up the exact topology it
    // wants without depending on RNG luck.
    public class ExplosiveChainTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator IsolatedExplosive_DestroysOnlyItself_PaysOne()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(Vector3.zero, seed: 1);
            ForceMaterial(meteor, 5, 5, "Explosive");
            // Wipe the 8 neighbors so the chain has nothing to consume.
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0) ClearCell(meteor, 5 + dx, 5 + dy);

            // Hit the explosive directly with a tiny blast.
            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(5, 5), 0.05f);
            Assert.AreEqual(1, result.totalPayout, "isolated explosive should pay $1");

            // No chains queued — drain pass next frame is a no-op.
            yield return null;

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator TwoAdjacentExplosives_ChainAcrossFrames()
        {
            yield return SetupScene();
            var meteor = SpawnTestMeteor(Vector3.zero, seed: 2);

            // Place two explosives at (4,5) and (5,5). They are adjacent so
            // the first explosion's 8-neighbor ring will queue the second
            // for detonation NEXT frame, not the same frame.
            ForceMaterial(meteor, 4, 5, "Explosive");
            ForceMaterial(meteor, 5, 5, "Explosive");

            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(4, 5), 0.05f);
            Assert.AreEqual(1, result.totalPayout,
                "first explosive pays $1 immediately");

            // The second explosive must still be alive THIS frame — chains
            // are 1-frame-delayed by design (bombastic cascade).
            Assert.IsTrue(meteor.IsVoxelPresent(5, 5),
                "second explosive should still be alive same frame");

            // Advance one frame so Update drains the queue.
            yield return null;

            // The second explosive should have chain-detonated.
            Assert.IsFalse(meteor.IsVoxelPresent(5, 5),
                "second explosive should have chain-detonated next frame");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator ExplosiveAdjacentToOneHpCore_ChainKillsCore()
        {
            yield return SetupScene();
            var meteor = SpawnTestMeteor(Vector3.zero, seed: 3);

            // Place an explosive next to a 1-HP core. The first hit kills the
            // explosive (pays $1), then the next-frame drain damages the core
            // by 1 HP — at HP=1 that destroys it and pays the core's $5.
            ForceMaterial(meteor, 4, 5, "Explosive");
            ForceMaterial(meteor, 5, 5, "Core", hp: 1);

            int coresBefore = meteor.CoreVoxelCount;
            Assert.GreaterOrEqual(coresBefore, 1);

            // SetupScene guarantees a GameManager — assert unconditionally so a
            // missing instance fails loud rather than silently skipping.
            Assert.IsNotNull(GameManager.Instance, "SetupScene must create a GameManager");
            int moneyBefore = GameManager.Instance.Money;

            var result = meteor.ApplyBlast(meteor.GetVoxelWorldPosition(4, 5), 0.05f);
            Assert.AreEqual(1, result.totalPayout, "explosive pays $1 same frame");

            // Same-frame: core should still be present (chain hasn't fired yet).
            Assert.IsTrue(meteor.IsVoxelPresent(5, 5),
                "forced core should still be alive same frame");

            // Next frame: Update drains the queue, damages the core, kills it.
            yield return null;

            Assert.IsFalse(meteor.IsVoxelPresent(5, 5),
                "core should have chain-detonated next frame");
            Assert.Less(meteor.CoreVoxelCount, coresBefore,
                "core count dropped after chain");

            // Drain pass pays the core's value via GameManager.AddMoney.
            int moneyAfter = GameManager.Instance.Money;
            Assert.Greater(moneyAfter, moneyBefore,
                "drain should have paid out the chain-killed core");

            TeardownScene();
        }

        // ---- helpers ----------------------------------------------------

        // Force a single cell on the meteor to a specific material via
        // reflection. Used to set up known explosive/core layouts without
        // relying on the generator's random rolls.
        private static void ForceMaterial(Meteor meteor, int gx, int gy, string materialName, int? hp = null)
        {
            var registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            var mat = registry.GetByName(materialName);
            Assert.IsNotNull(mat, $"material {materialName} not found in registry");

            GetGrids(meteor, out var matArr, out var kindArr, out var hpArr);
            // If the cell was previously empty, bumping it back to alive
            // requires also bumping aliveCount via reflection so the meteor
            // doesn't think it's still in its original count.
            bool wasEmpty = kindArr[gx, gy] == VoxelKind.Empty;

            matArr[gx, gy]  = mat;
            kindArr[gx, gy] = materialName == "Core" ? VoxelKind.Core : VoxelKind.Dirt;
            hpArr[gx, gy]   = hp ?? mat.baseHp;

            if (wasEmpty)
            {
                var aliveField = typeof(Meteor).GetField("aliveCount",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                int n = (int)aliveField.GetValue(meteor);
                aliveField.SetValue(meteor, n + 1);
            }
        }

        // Clear a single cell. Decrements aliveCount via reflection so the
        // meteor's bookkeeping stays correct.
        private static void ClearCell(Meteor meteor, int gx, int gy)
        {
            if (gx < 0 || gy < 0
                || gx >= VoxelMeteorGenerator.GridSize
                || gy >= VoxelMeteorGenerator.GridSize) return;

            GetGrids(meteor, out var matArr, out var kindArr, out var hpArr);
            if (kindArr[gx, gy] == VoxelKind.Empty) return;

            matArr[gx, gy]  = null;
            kindArr[gx, gy] = VoxelKind.Empty;
            hpArr[gx, gy]   = 0;

            var aliveField = typeof(Meteor).GetField("aliveCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            int n = (int)aliveField.GetValue(meteor);
            aliveField.SetValue(meteor, Mathf.Max(0, n - 1));
        }

        private static void GetGrids(Meteor meteor,
            out VoxelMaterial[,] mat, out VoxelKind[,] kind, out int[,] hp)
        {
            var matField  = typeof(Meteor).GetField("material",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var kindField = typeof(Meteor).GetField("kind",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var hpField   = typeof(Meteor).GetField("hp",
                BindingFlags.NonPublic | BindingFlags.Instance);
            mat  = (VoxelMaterial[,])matField.GetValue(meteor);
            kind = (VoxelKind[,])kindField.GetValue(meteor);
            hp   = (int[,])hpField.GetValue(meteor);
        }
    }
}
