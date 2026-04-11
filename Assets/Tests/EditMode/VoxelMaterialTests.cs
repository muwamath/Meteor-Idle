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
