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
