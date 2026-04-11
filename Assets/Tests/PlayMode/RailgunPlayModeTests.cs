using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode tests for the railgun raycast → ApplyTunnel → pierce chain.
    // These exercise the part of the weapon that EditMode tests can't reach
    // because it depends on real Physics2D colliders and the Meteors layer
    // mask. If these fail, the core railgun is broken — fix here before any
    // scene-wiring phase touches the prefab.
    public class RailgunPlayModeTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator RailgunRound_FiresIntoMeteor_DealsDamage()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
            int beforeAlive = meteor.AliveVoxelCount;

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 10,
                caliber: 1);

            // 0.5s at speed 20 = 10 world units of travel — plenty to reach
            // a meteor at (0, 3) and tunnel through it.
            yield return new WaitForSeconds(0.5f);

            Assert.Less(meteor.AliveVoxelCount, beforeAlive,
                "meteor should have lost voxels after railgun round hit");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator RailgunRound_PiercesTwoStackedMeteors()
        {
            yield return SetupScene();

            var m1 = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 1);
            var m2 = SpawnTestMeteor(new Vector3(0f, 6f, 0f), seed: 2);
            int m1Before = m1.AliveVoxelCount;
            int m2Before = m2.AliveVoxelCount;

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 30, // generous budget so the round can pierce both
                caliber: 1);

            yield return new WaitForSeconds(0.8f);

            Assert.Less(m1.AliveVoxelCount, m1Before,
                "first meteor should have been tunneled");
            Assert.Less(m2.AliveVoxelCount, m2Before,
                "second meteor should have been tunneled via piercing");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator RailgunRound_LayerMask_IgnoresMissilesInPath()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 5f, 0f));
            int meteorBefore = meteor.AliveVoxelCount;

            // Park a missile directly between the round and the meteor.
            // The railgun's raycast filters to the Meteors layer, so this
            // missile should NOT show up in the hit results.
            var missile = SpawnTestMissile(new Vector3(0f, 2f, 0f));
            Assert.IsTrue(missile.gameObject.activeSelf, "missile should start active");

            SpawnTestRailgunRound(
                spawnPos: new Vector3(0f, 0f, 0f),
                direction: Vector2.up,
                speed: 20f,
                weight: 10,
                caliber: 1);

            yield return new WaitForSeconds(0.5f);

            Assert.Less(meteor.AliveVoxelCount, meteorBefore,
                "meteor behind the missile should still have been damaged");
            Assert.IsTrue(missile.gameObject.activeSelf,
                "missile in the path should be untouched by the railgun");

            TeardownScene();
        }
    }
}
