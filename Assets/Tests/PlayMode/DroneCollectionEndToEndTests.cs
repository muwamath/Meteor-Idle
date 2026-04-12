using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class DroneCollectionEndToEndTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator CoreKill_SpawnsDrop_DroneCollectsAndDeposits()
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

            var bay = new Vector3(0f, -5f, 0f);
            var (drone, env) = SpawnTestDroneWithEnv(bay, bay);

            float timeout = 15f;
            while (timeout > 0f && env.TotalDeposited == 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.Greater(env.TotalDeposited, 0, "drone deposited core value");
            Assert.Greater(_gameManager.Money, startingMoney,
                "GameManager money increased via deposit path");
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
