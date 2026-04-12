using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class DroneCollectionEndToEndTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator CoreKill_SpawnsDrop_DroneCollectsAndDepositsAtCollector()
        {
            yield return SetupScene();

            int startingMoney = _gameManager.Money;

            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 77);
            ForceMaterial(meteor, 5, 5, "Core");

            meteor.ApplyBlast(meteor.GetVoxelWorldPosition(5, 5), 0.05f);
            Assert.AreEqual(1, _gameManager.ActiveDrops.Count,
                "core kill produced a CoreDrop");
            Assert.AreEqual(startingMoney, _gameManager.Money,
                "core kill did NOT pay directly (Iter3 gate)");

            // Create a Collector at origin
            var collectorGo = new GameObject("TestCollector", typeof(Collector));
            collectorGo.transform.position = new Vector3(0f, -3f, 0f);
            var collector = collectorGo.GetComponent<Collector>();

            var bay = new Vector3(0f, -5f, 0f);
            var (drone, env) = SpawnTestDroneWithEnv(bay, bay, collectorGo.transform.position);
            drone.SetCollector(collector);

            float timeout = 15f;
            while (timeout > 0f && _gameManager.Money <= startingMoney)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.Greater(_gameManager.Money, startingMoney,
                "GameManager money increased via Collector deposit path");
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
