using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode tests for Missile homing steering — the mid-flight RotateTowards
    // logic in Missile.Update. The unique mechanic: velocity direction rotates
    // toward a target voxel at most homingDegPerSec per second, preserving
    // speed, and only while the target voxel is still present.
    public class MissileHomingTests : PlayModeTestFixture
    {
        // Missile velocity angle starts at 0° (pointing +X). Target sits far
        // along +Y so its direction is ~90° away from the initial velocity
        // vector. Homing can never overshoot that, so any non-zero rotation
        // is toward the target. Meteor is placed very far away (y=200) so
        // the missile cannot physically reach it inside the test window —
        // otherwise OnTriggerEnter2D would despawn the missile and zero its
        // velocity before the assertions run.
        private const float InitialSpeed = 5f;
        private const float HomingDegPerSec = 180f;

        [UnityTest]
        public IEnumerator Homing_RotatesVelocityTowardTarget()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 200f, 0f), seed: 101);
            Assert.IsTrue(meteor.PickRandomPresentVoxel(out int gx, out int gy),
                "meteor should have at least one live voxel at spawn");

            var missile = SpawnTestMissile(new Vector3(0f, 0f, 0f));
            missile.Launch(
                turret: null,
                position: new Vector3(0f, 0f, 0f),
                velocity: new Vector2(InitialSpeed, 0f),
                damageStat: 1f,
                blastStat: 0f,
                target: meteor,
                targetGridX: gx,
                targetGridY: gy,
                homingDegPerSec: HomingDegPerSec);

            yield return new WaitForSeconds(0.2f);

            var rb = missile.GetComponent<Rigidbody2D>();
            Vector2 v = rb.linearVelocity;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

            // Upper bound: homingDegPerSec * elapsed plus frame-timing slack.
            // Lower bound: meaningfully nonzero. Homing at 180°/s over 0.2s
            // should yield something in the ballpark of 20°–40° depending on
            // actual frame cadence, easily distinguishable from the dumb case.
            Assert.Greater(angle, 5f,
                $"missile should have rotated meaningfully toward target; got {angle}°");
            Assert.Less(angle, 80f,
                $"rotation should still be bounded by homingDegPerSec; got {angle}°");

            // Speed magnitude is preserved by RotateTowards — check within 1%.
            Assert.AreEqual(InitialSpeed, v.magnitude, InitialSpeed * 0.01f);

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator NoHoming_VelocityUnchanged()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(new Vector3(0f, 200f, 0f), seed: 202);
            Assert.IsTrue(meteor.PickRandomPresentVoxel(out int gx, out int gy));

            var missile = SpawnTestMissile(new Vector3(0f, 0f, 0f));
            missile.Launch(
                turret: null,
                position: new Vector3(0f, 0f, 0f),
                velocity: new Vector2(InitialSpeed, 0f),
                damageStat: 1f,
                blastStat: 0f,
                target: meteor,
                targetGridX: gx,
                targetGridY: gy,
                homingDegPerSec: 0f); // <— dumb projectile

            yield return new WaitForSeconds(0.2f);

            var rb = missile.GetComponent<Rigidbody2D>();
            Vector2 v = rb.linearVelocity;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            Assert.AreEqual(0f, angle, 0.1f,
                $"dumb missile should hold its initial angle; got {angle}°");
            Assert.AreEqual(InitialSpeed, v.magnitude, 1e-3);

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator Homing_StopsWhenTargetVoxelRemoved()
        {
            yield return SetupScene();

            // Smallest-size meteor so coreHp = 1 and the blanket blast below
            // (radius 10) is guaranteed to kill every cell — including cores —
            // in one call. Needed after Iter 1 introduced multi-HP cores.
            var meteor = SpawnTestMeteor(new Vector3(0f, 200f, 0f), seed: 303, scale: 0.525f);
            Assert.IsTrue(meteor.PickRandomPresentVoxel(out int gx, out int gy));

            var missile = SpawnTestMissile(new Vector3(0f, 0f, 0f));
            missile.Launch(
                turret: null,
                position: new Vector3(0f, 0f, 0f),
                velocity: new Vector2(InitialSpeed, 0f),
                damageStat: 1f,
                blastStat: 0f,
                target: meteor,
                targetGridX: gx,
                targetGridY: gy,
                homingDegPerSec: HomingDegPerSec);

            // Let homing rotate velocity for a few frames, then annihilate
            // the meteor so the guard (target.IsAlive && IsVoxelPresent)
            // flips to false and steering must cease.
            yield return new WaitForSeconds(0.1f);
            meteor.ApplyBlast(meteor.transform.position, 10f);
            Assert.IsFalse(meteor.IsAlive, "meteor should be dead after blanket blast");

            var rb = missile.GetComponent<Rigidbody2D>();
            Vector2 frozenVelocity = rb.linearVelocity;

            // Now wait further — with no live target, velocity must not drift.
            yield return new WaitForSeconds(0.1f);

            Vector2 later = rb.linearVelocity;
            Assert.AreEqual(frozenVelocity.x, later.x, 1e-3,
                "velocity.x should stop changing once target voxel is gone");
            Assert.AreEqual(frozenVelocity.y, later.y, 1e-3,
                "velocity.y should stop changing once target voxel is gone");

            TeardownScene();
        }
    }
}
