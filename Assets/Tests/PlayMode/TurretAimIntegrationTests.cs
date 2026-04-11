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

        // -------------------------------------------------------------------
        // Hit-test matrix — the regression gate the user asked for on
        // 2026-04-11. Each test builds a real BaseSlot+turret, spawns a
        // meteor at a controlled position/velocity, upgrades the relevant
        // stat to the requested level, forces the turret to fire, advances
        // time long enough for flight + rotation, and asserts the meteor
        // actually took damage. Split into 12 individual UnityTest methods
        // (not parameterized TestCase) because [UnityTest] + [TestCase] has
        // NUnit compatibility issues — NUnit treats the IEnumerator return
        // as a value expectation. Each method delegates to the shared
        // RunHitTest helper.
        // -------------------------------------------------------------------

        // Railgun base speed — straight-down meteor, no drift
        [UnityTest] public IEnumerator Hit_Railgun_BaseSpeed_StraightDown() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0f, -0.5f), speedLevel: 0, homingLevel: 0);

        // Railgun base speed — drifting right (original bug scenario)
        [UnityTest] public IEnumerator Hit_Railgun_BaseSpeed_DriftRight() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 0, homingLevel: 0);

        // Railgun base speed — drifting left + max fall
        [UnityTest] public IEnumerator Hit_Railgun_BaseSpeed_DriftLeftMaxFall() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(-0.3f, -0.6f), speedLevel: 0, homingLevel: 0);

        // Railgun speed level 5 (21 world/sec)
        [UnityTest] public IEnumerator Hit_Railgun_SpeedLvl5() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 5, homingLevel: 0);

        // Railgun speed level 10 (36 world/sec) — near-instant
        [UnityTest] public IEnumerator Hit_Railgun_SpeedLvl10() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 10, homingLevel: 0);

        // Railgun speed level 49 (153 world/sec) — matches user's observed session state
        [UnityTest] public IEnumerator Hit_Railgun_SpeedLvl49() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 49, homingLevel: 0);

        // Railgun side slot — meteor far across the playfield
        [UnityTest] public IEnumerator Hit_Railgun_SideSlot_FarMeteor() =>
            RunHitTest(WeaponType.Railgun, new Vector3(-6f, -5f, 0f), new Vector3(6f, 6f, 0f), new Vector2(0f, -0.5f), speedLevel: 0, homingLevel: 0);

        // Railgun off-axis meteor
        [UnityTest] public IEnumerator Hit_Railgun_OffAxis() =>
            RunHitTest(WeaponType.Railgun, new Vector3(0f, -5f, 0f), new Vector3(3f, 6f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 0, homingLevel: 0);

        // Missile base speed — drifting, no homing
        [UnityTest] public IEnumerator Hit_Missile_BaseSpeed_NoHoming() =>
            RunHitTest(WeaponType.Missile, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 0, homingLevel: 0);

        // Missile base speed — with homing level 2 (60 deg/sec steer)
        [UnityTest] public IEnumerator Hit_Missile_BaseSpeed_Homing2() =>
            RunHitTest(WeaponType.Missile, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.4f, -0.6f), speedLevel: 0, homingLevel: 2);

        // Missile speed level 3 (5.8 world/sec)
        [UnityTest] public IEnumerator Hit_Missile_SpeedLvl3() =>
            RunHitTest(WeaponType.Missile, new Vector3(0f, -5f, 0f), new Vector3(0f, 5f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 3, homingLevel: 0);

        // Missile off-axis meteor
        [UnityTest] public IEnumerator Hit_Missile_OffAxis() =>
            RunHitTest(WeaponType.Missile, new Vector3(0f, -5f, 0f), new Vector3(5f, 8f, 0f), new Vector2(0.3f, -0.5f), speedLevel: 0, homingLevel: 0);

        // User-reported scenario 2026-04-11: FireRate level 34 + Speed level 43
        // observed in-WebGL. User hypothesis: "the round is travelling so fast
        // that it goes past the intended target". FireRate 34 -> fires per sec =
        // 0.2 + 0.05*34 = 1.9 Hz. Speed 43 -> world/sec = 6 + 3*43 = 135. At that
        // speed a 10-unit shot takes 0.074s (~4.4 frames at 60 fps). The meteor
        // drifts ~0.02 world units during that time, so the lead is essentially
        // zero. This test confirms whether the miss is reproducible in isolation
        // or is WebGL-runtime specific.
        [UnityTest] public IEnumerator Hit_Railgun_UserReported_FireRate34_Speed43() =>
            RunHitTestWithFireRate(
                WeaponType.Railgun,
                new Vector3(0f, -5f, 0f),
                new Vector3(0f, 5f, 0f),
                new Vector2(0.3f, -0.5f),
                speedLevel: 43,
                fireRateLevel: 34,
                homingLevel: 0);

        // Same user scenario but with no drift — isolates whether the drift
        // direction matters at this speed.
        [UnityTest] public IEnumerator Hit_Railgun_UserReported_FireRate34_Speed43_NoDrift() =>
            RunHitTestWithFireRate(
                WeaponType.Railgun,
                new Vector3(0f, -5f, 0f),
                new Vector3(0f, 5f, 0f),
                new Vector2(0f, -0.5f),
                speedLevel: 43,
                fireRateLevel: 34,
                homingLevel: 0);

        // Same scenario off-axis — the meteor is neither directly above nor
        // perfectly aligned. Exercises the angled flight path where raycast
        // aliasing would be most visible at high stepDistance.
        [UnityTest] public IEnumerator Hit_Railgun_UserReported_FireRate34_Speed43_OffAxis() =>
            RunHitTestWithFireRate(
                WeaponType.Railgun,
                new Vector3(0f, -5f, 0f),
                new Vector3(4f, 6f, 0f),
                new Vector2(0.3f, -0.5f),
                speedLevel: 43,
                fireRateLevel: 34,
                homingLevel: 0);

        // Overload that also sets FireRate (or equivalent) for tests that
        // want to match a specific user-session upgrade state. FireRate only
        // affects the single-shot scenario indirectly (via charge duration),
        // so setting it exercises the code path even though we still force
        // chargeTimer to ready before the first Update.
        private IEnumerator RunHitTestWithFireRate(
            WeaponType weapon,
            Vector3 slotPos,
            Vector3 meteorPos,
            Vector2 meteorVel,
            int speedLevel,
            int fireRateLevel,
            int homingLevel)
        {
            yield return SetupScene();

            var slot = SpawnTestSlot(slotPos, weapon);
            var turret = slot.ActiveTurret;
            Assert.IsNotNull(turret, $"{weapon} slot must expose an ActiveTurret");

            var spawner = SpawnTestSpawner();
            turret.SetRuntimeRefs(spawner);

            if (weapon == WeaponType.Railgun)
            {
                var railgun = (RailgunTurret)turret;
                railgun.Stats.speed.level = speedLevel;
                railgun.Stats.fireRate.level = fireRateLevel;
            }
            else
            {
                var missile = (MissileTurret)turret;
                missile.Stats.missileSpeed.level = speedLevel;
                missile.Stats.fireRate.level = fireRateLevel;
                missile.Stats.homing.level = homingLevel;
            }

            var meteor = SpawnTestMeteor(meteorPos);
            SetMeteorVelocity(meteor, meteorVel);
            int initialVoxels = meteor.AliveVoxelCount;
            Assert.Greater(initialVoxels, 0, "meteor must have live voxels at spawn");
            GetSpawnerActiveList(spawner).Add(meteor);

            if (weapon == WeaponType.Railgun)
                ForceRailgunReady((RailgunTurret)turret);

            float projectileSpeed = weapon == WeaponType.Railgun
                ? 6f + 3f * speedLevel
                : 4f + 0.6f * speedLevel;
            float distance = Vector3.Distance(slotPos, meteorPos);
            float flight = distance / projectileSpeed;
            float budget = flight + 2.5f;
            yield return new WaitForSeconds(budget);

            Assert.Less(
                meteor.AliveVoxelCount, initialVoxels,
                $"{weapon} at speedLvl={speedLevel} fireRateLvl={fireRateLevel} " +
                $"homingLvl={homingLevel} should have hit meteor (pos={meteorPos}, " +
                $"vel={meteorVel}) within {budget:F2}s " +
                $"(initial={initialVoxels}, now={meteor.AliveVoxelCount})");

            TeardownScene();
        }

        // Shared helper — executes one hit test with the given parameters.
        private IEnumerator RunHitTest(
            WeaponType weapon,
            Vector3 slotPos,
            Vector3 meteorPos,
            Vector2 meteorVel,
            int speedLevel,
            int homingLevel)
        {
            yield return SetupScene();

            var slot = SpawnTestSlot(slotPos, weapon);
            var turret = slot.ActiveTurret;
            Assert.IsNotNull(turret, $"{weapon} slot must expose an ActiveTurret");

            var spawner = SpawnTestSpawner();
            turret.SetRuntimeRefs(spawner);

            // Upgrade the relevant stat(s) to the requested level via the
            // public Stats getter on each concrete turret.
            if (weapon == WeaponType.Railgun)
            {
                var railgun = (RailgunTurret)turret;
                railgun.Stats.speed.level = speedLevel;
            }
            else
            {
                var missile = (MissileTurret)turret;
                missile.Stats.missileSpeed.level = speedLevel;
                missile.Stats.homing.level = homingLevel;
            }

            // Spawn the meteor with a controlled velocity.
            var meteor = SpawnTestMeteor(meteorPos);
            SetMeteorVelocity(meteor, meteorVel);
            int initialVoxels = meteor.AliveVoxelCount;
            Assert.Greater(initialVoxels, 0, "meteor must have live voxels at spawn");
            GetSpawnerActiveList(spawner).Add(meteor);

            // Force the railgun's charge timer to ready. Missile turret's
            // reloadTimer starts at 0 so no forcing is needed.
            if (weapon == WeaponType.Railgun)
                ForceRailgunReady((RailgunTurret)turret);

            // Time budget: projectile flight + rotation headroom + margin.
            float projectileSpeed = weapon == WeaponType.Railgun
                ? 6f + 3f * speedLevel
                : 4f + 0.6f * speedLevel;
            float distance = Vector3.Distance(slotPos, meteorPos);
            float flight = distance / projectileSpeed;
            float budget = flight + 2.5f;
            yield return new WaitForSeconds(budget);

            Assert.Less(
                meteor.AliveVoxelCount, initialVoxels,
                $"{weapon} at speedLvl={speedLevel} homingLvl={homingLevel} should have hit " +
                $"meteor (pos={meteorPos}, vel={meteorVel}) within {budget:F2}s " +
                $"(initial={initialVoxels}, now={meteor.AliveVoxelCount})");

            TeardownScene();
        }
    }
}
