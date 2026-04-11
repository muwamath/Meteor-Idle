using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode tests for TurretBase.FindTarget — the shared closest-meteor
    // targeting primitive that both MissileTurret and RailgunTurret rely on.
    // The base class is abstract, so the tests define a trivial concrete
    // subclass (TestTurret) that stubs Fire, FireRate, and RotationSpeed.
    // Meteors are pushed into the real MeteorSpawner's pool via reflection
    // (into the private active list of SimplePool<Meteor>) — that's the list
    // TurretBase.FindTarget enumerates through spawner.ActiveMeteors.
    public class TurretTargetingTests : PlayModeTestFixture
    {
        // Stubbed concrete TurretBase. FireRate/RotationSpeed come back as
        // benign defaults; Fire is a no-op because these tests don't care
        // about the reload→fire cycle, only the target selection.
        private class TestTurret : TurretBase
        {
            public int FireCalls;
            public Meteor LastFireTarget;

            protected override float FireRate => 1f;
            protected override float RotationSpeed => 360f;

            protected override void Fire(Meteor target)
            {
                FireCalls++;
                LastFireTarget = target;
            }

            // Expose the protected FindTarget so tests can assert it directly.
            public Meteor FindTargetForTest() => FindTarget();
        }

        private TestTurret _turret;
        private MeteorSpawner _spawner;
        private List<Meteor> _injectedActive;

        private IEnumerator SetupTurretScene()
        {
            yield return SetupScene();

            _spawner = SpawnTestSpawner();
            _injectedActive = GetSpawnerActiveList(_spawner);

            // Place the turret at world origin with a barrel child at the
            // same position. FindTarget measures distance from barrel.position.
            // Create the turret GameObject INACTIVE so Awake can't fire before
            // we inject the barrel reference and spawner; otherwise an Update
            // tick with a null barrel would NRE on the first targeted frame.
            var turretGo = new GameObject("TestTurret");
            turretGo.SetActive(false);
            turretGo.transform.position = Vector3.zero;
            var barrelGo = new GameObject("TestBarrel");
            barrelGo.transform.SetParent(turretGo.transform);
            barrelGo.transform.localPosition = Vector3.zero;

            _turret = turretGo.AddComponent<TestTurret>();
            SetPrivateField(_turret, "barrel", barrelGo.transform);
            _turret.SetRuntimeRefs(_spawner);
            turretGo.SetActive(true); // Awake fires now with every field valid
        }

        [UnityTest]
        public IEnumerator FindTarget_NoMeteors_ReturnsNull()
        {
            yield return SetupTurretScene();

            Assert.IsNull(_turret.FindTargetForTest());

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator FindTarget_PicksClosestLiveMeteor()
        {
            yield return SetupTurretScene();

            // Three meteors along the +Y axis at distances 3, 5, 7 — the
            // closest (3 units) should win.
            var near = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 11);
            var mid  = SpawnTestMeteor(new Vector3(0f, 5f, 0f), seed: 22);
            var far  = SpawnTestMeteor(new Vector3(0f, 7f, 0f), seed: 33);
            _injectedActive.Add(near);
            _injectedActive.Add(mid);
            _injectedActive.Add(far);

            Assert.AreSame(near, _turret.FindTargetForTest());

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator FindTarget_IgnoresMeteorsBeyondRange()
        {
            yield return SetupTurretScene();

            // Default TurretBase.range = 30. Place a single meteor at distance
            // 40 — it must be ignored entirely (FindTarget returns null).
            var farAway = SpawnTestMeteor(new Vector3(0f, 40f, 0f), seed: 44);
            _injectedActive.Add(farAway);

            Assert.IsNull(_turret.FindTargetForTest());

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator FindTarget_IgnoresDeadMeteors()
        {
            yield return SetupTurretScene();

            // Two meteors at the same distance — the closer one is destroyed
            // by a big blast, so the farther one must now be selected.
            var closer = SpawnTestMeteor(new Vector3(0f, 3f, 0f), seed: 55);
            var farther = SpawnTestMeteor(new Vector3(0f, 5f, 0f), seed: 66);
            _injectedActive.Add(closer);
            _injectedActive.Add(farther);

            // Nuke the closer meteor — ApplyBlast with a huge radius destroys
            // every live voxel, flipping IsAlive to false.
            closer.ApplyBlast(closer.transform.position, 10f);
            Assert.IsFalse(closer.IsAlive, "closer meteor should now be dead");

            Assert.AreSame(farther, _turret.FindTargetForTest());

            TeardownScene();
        }

        // --- reflection helpers --------------------------------------------

        // Reach into the MeteorSpawner → private SimplePool<Meteor> pool →
        // private List<Meteor> active. Tests push their SpawnTestMeteor
        // results into that list so spawner.ActiveMeteors returns them.
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

        private static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
            {
                // Walk the base chain — `barrel` lives on TurretBase, not TestTurret.
                var bt = target.GetType().BaseType;
                while (bt != null && f == null)
                {
                    f = bt.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                    bt = bt.BaseType;
                }
            }
            Assert.IsNotNull(f, $"field '{name}' not found on {target.GetType().Name}");
            f.SetValue(target, value);
        }
    }
}
