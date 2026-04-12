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
