using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // Integration tests for turret aim behavior. Unlike TurretTargetingTests
    // which stubs Fire and only asserts target selection, these tests actually
    // build a real turret from BaseSlot.prefab, spawn a meteor at a controlled
    // position/velocity, force the turret to fire, advance time, and assert
    // the projectile actually hits (meteor took damage).
    //
    // They exist to catch end-to-end aim regressions — the missing coverage
    // that allowed the iter/aim-fixes railgun over-lead bug to ship to the
    // dev WebGL build.
    public class TurretAimIntegrationTests : PlayModeTestFixture
    {
        // Instantiates the real BaseSlot prefab, positions it at the given
        // world location, and builds the requested weapon into it. Returns
        // the built BaseSlot for further setup. Uses AssetDatabase which is
        // editor-only — fine for PlayMode tests.
        private BaseSlot SpawnTestSlot(Vector3 position, WeaponType weapon)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/BaseSlot.prefab");
            Assert.IsNotNull(prefab, "Assets/Prefabs/BaseSlot.prefab failed to load");
            var go = Object.Instantiate(prefab, position, Quaternion.identity);
            go.name = "TestSlot";
            var slot = go.GetComponent<BaseSlot>();
            slot.Build(weapon, 0);
            return slot;
#else
            throw new System.NotSupportedException("Editor-only");
#endif
        }

        // Reflection helper — reach into the meteor and set its private
        // velocity field to a deterministic value so aim tests aren't at
        // the mercy of Random.Range inside Spawn.
        private static void SetMeteorVelocity(Meteor meteor, Vector2 velocity)
        {
            var f = typeof(Meteor).GetField("velocity",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "Meteor.velocity private field not found");
            f.SetValue(meteor, velocity);
        }

        // Reflection helper — force the railgun's charge timer to "full" so
        // it fires on the next Update tick instead of waiting the base
        // FireRate-derived 5 seconds. The Update code clamps this to
        // chargeDuration so setting it larger is safe.
        private static void ForceRailgunReady(RailgunTurret turret)
        {
            var f = typeof(RailgunTurret).GetField("chargeTimer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "RailgunTurret.chargeTimer not found");
            f.SetValue(turret, 999f);
        }

        // Reach into the MeteorSpawner → private SimplePool<Meteor> pool →
        // private List<Meteor> active. Tests push their SpawnTestMeteor
        // results into that list so spawner.ActiveMeteors returns them.
        // Mirrors the helper in TurretTargetingTests.
        private static List<Meteor> GetSpawnerActiveList(MeteorSpawner spawner)
        {
            var poolField = typeof(MeteorSpawner).GetField(
                "pool",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(poolField, "MeteorSpawner.pool field not found");
            object pool = poolField.GetValue(spawner);
            Assert.IsNotNull(pool, "MeteorSpawner.pool should be initialized by Awake");

            var activeField = pool.GetType().GetField(
                "active",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(activeField, "SimplePool.active field not found");
            return (List<Meteor>)activeField.GetValue(pool);
        }

        [UnityTest]
        public IEnumerator Railgun_BaseSpeed_HitsDriftingMeteor()
        {
            yield return SetupScene();

            // Build a railgun slot at the bottom of the playfield.
            var slot = SpawnTestSlot(new Vector3(0f, -5f, 0f), WeaponType.Railgun);
            var turret = (RailgunTurret)slot.ActiveTurret;
            Assert.IsNotNull(turret, "Building Railgun into slot should expose a RailgunTurret");

            // Connect the turret to a test spawner so it can find targets.
            var spawner = SpawnTestSpawner();
            turret.SetRuntimeRefs(spawner);

            // Spawn a meteor above the turret with a deterministic velocity
            // (drift right + falling). Values match realistic MeteorSpawner
            // output (driftMax 0.4, fallSpeed 0.4–0.67).
            var meteor = SpawnTestMeteor(new Vector3(0f, 5f, 0f));
            SetMeteorVelocity(meteor, new Vector2(0.3f, -0.5f));
            int initialVoxels = meteor.AliveVoxelCount;
            Assert.Greater(initialVoxels, 0, "meteor should start with live voxels");

            // Inject the meteor into the spawner's active list so
            // TurretBase.FindTarget sees it through spawner.ActiveMeteors.
            GetSpawnerActiveList(spawner).Add(meteor);

            // Force the railgun's charge timer to "ready" so Fire happens on
            // the next Update tick rather than after the normal 5-second wait.
            ForceRailgunReady(turret);

            // 10-world-unit gap (shooter y=-5, meteor y=5). Base railgun speed
            // is 6 world/sec, so naive flight time is ~1.67s. Add headroom for
            // barrel rotation to converge and for the round spawn delay.
            yield return new WaitForSeconds(3f);

            Assert.Less(
                meteor.AliveVoxelCount, initialVoxels,
                $"railgun round should have hit the drifting meteor within 3s " +
                $"(initial={initialVoxels}, now={meteor.AliveVoxelCount})");

            TeardownScene();
        }
    }
}
