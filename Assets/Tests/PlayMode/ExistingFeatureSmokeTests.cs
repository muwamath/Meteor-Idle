using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // Baseline PlayMode coverage for the existing features (missile collision,
    // meteor fade, spawner pooling). These run on unmodified main as a smoke
    // check that the base game still works before the railgun work lands on
    // top. The user's rule: "make sure the base play is always solid".
    public class ExistingFeatureSmokeTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Missile_LaunchedAtMeteor_Collides_DealsDamage()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
            int beforeAlive = meteor.AliveVoxelCount;

#if UNITY_EDITOR
            var missilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Missile>(
                "Assets/Prefabs/Missile.prefab");
#else
            Missile missilePrefab = null;
#endif
            var missile = Object.Instantiate(missilePrefab);
            missile.Launch(
                turret: null,
                position: new Vector3(0f, 1f, 0f),
                velocity: new Vector2(0f, 6f),
                damageStat: 1f,
                blastStat: 0.1f,
                target: meteor,
                targetGridX: 5,
                targetGridY: 5,
                homingDegPerSec: 0f);

            // Wait ~30 physics frames so OnTriggerEnter2D has a chance to fire.
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.Less(meteor.AliveVoxelCount, beforeAlive,
                "meteor should have lost voxels after missile collision");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator Meteor_FallsAndFadesBelowThreshold_BecomesUntargetable()
        {
            yield return SetupScene();

            // Spawn just above the fade threshold (-7.88).
            var meteor = SpawnTestMeteor(new Vector3(0f, -7.0f, 0f));
            Assert.IsTrue(meteor.IsAlive, "meteor above threshold should start alive");

            // Base fall speed 0.4 world/sec, need to drop ~0.9 units → ~2.5 s.
            // Wait 3 seconds for comfortable margin.
            yield return new WaitForSeconds(3f);

            Assert.IsFalse(meteor.IsAlive,
                "meteor that fell below fade threshold must be untargetable");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator MeteorSpawner_SpawnsPooledMeteors_OverTime()
        {
            yield return SetupScene();

            var spawner = SpawnTestSpawner();
            int startActive = spawner.ActiveMeteors.Count;

            // Base cadence: initialInterval=4s. Wait 8 seconds for at least one spawn.
            yield return new WaitForSeconds(8f);

            int endActive = spawner.ActiveMeteors.Count;
            Assert.GreaterOrEqual(endActive - startActive, 1,
                $"expected at least 1 meteor spawn in 8 seconds (start={startActive}, end={endActive})");

            TeardownScene();
        }
    }
}
