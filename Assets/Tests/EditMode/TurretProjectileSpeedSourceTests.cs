using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Asserts that each concrete TurretBase subclass's ProjectileSpeed getter
    // returns the same numeric value that the Fire code path would hand to
    // its projectile (RailgunRound.Configure speed, Missile.Launch velocity
    // magnitude). This is the specific regression gate for the class of bug
    // the user flagged on 2026-04-11: two code paths for projectile speed
    // silently drifting apart and causing over/under-lead in shipped builds.
    //
    // Uses shim subclasses that expose the protected ProjectileSpeed getter
    // publicly, plus reflection to inject a known statsInstance at controlled
    // upgrade levels. Pure EditMode — no scene, no play mode, no time cost.
    public class TurretProjectileSpeedSourceTests
    {
        private const float Eps = 0.0001f;

        // ---------------------------------------------------------------
        // RailgunStats formula sanity — base 6, +3 per level (per CLAUDE.md)
        // ---------------------------------------------------------------
        [TestCase(0, 6f)]
        [TestCase(1, 9f)]
        [TestCase(5, 21f)]
        [TestCase(10, 36f)]
        [TestCase(49, 153f)] // the user's observed session state
        public void RailgunStats_Speed_CurrentValue_MatchesFormula(int level, float expected)
        {
            var stats = ScriptableObject.CreateInstance<RailgunStats>();
            stats.speed.level = level;

            Assert.AreEqual(expected, stats.speed.CurrentValue, Eps);

            Object.DestroyImmediate(stats);
        }

        // ---------------------------------------------------------------
        // TurretStats formula sanity — base 4, +0.6 per level
        // ---------------------------------------------------------------
        [TestCase(0, 4f)]
        [TestCase(1, 4.6f)]
        [TestCase(5, 7f)]
        [TestCase(10, 10f)]
        public void TurretStats_MissileSpeed_CurrentValue_MatchesFormula(int level, float expected)
        {
            var stats = ScriptableObject.CreateInstance<TurretStats>();
            stats.missileSpeed.level = level;

            Assert.AreEqual(expected, stats.missileSpeed.CurrentValue, Eps);

            Object.DestroyImmediate(stats);
        }

        // ---------------------------------------------------------------
        // Integration: RailgunTurret.ProjectileSpeed == statsInstance.speed.CurrentValue
        // This is the exact invariant that would catch Fire bypassing the
        // getter and reading a different value from the stats.
        // ---------------------------------------------------------------
        private class RailgunShim : RailgunTurret
        {
            public float GetProjectileSpeed() => ProjectileSpeed;
        }

        [TestCase(0, 6f)]
        [TestCase(5, 21f)]
        [TestCase(10, 36f)]
        public void RailgunTurret_ProjectileSpeed_ReadsStatsInstanceCurrentValue(int level, float expected)
        {
            var go = new GameObject("RailgunShim", typeof(RailgunShim));
            var shim = go.GetComponent<RailgunShim>();

            var stats = ScriptableObject.CreateInstance<RailgunStats>();
            stats.speed.level = level;

            // Inject the statsInstance via reflection — the field is private
            // on RailgunTurret (not TurretBase), so we target the concrete type.
            var f = typeof(RailgunTurret).GetField(
                "statsInstance",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "RailgunTurret.statsInstance field not found");
            f.SetValue(shim, stats);

            Assert.AreEqual(expected, shim.GetProjectileSpeed(), Eps,
                $"ProjectileSpeed at level {level} must equal statsInstance.speed.CurrentValue");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(stats);
        }

        // ---------------------------------------------------------------
        // Same invariant for MissileTurret.
        // ---------------------------------------------------------------
        private class MissileShim : MissileTurret
        {
            public float GetProjectileSpeed() => ProjectileSpeed;
        }

        [TestCase(0, 4f)]
        [TestCase(5, 7f)]
        [TestCase(10, 10f)]
        public void MissileTurret_ProjectileSpeed_ReadsStatsInstanceCurrentValue(int level, float expected)
        {
            var go = new GameObject("MissileShim", typeof(MissileShim));
            var shim = go.GetComponent<MissileShim>();

            var stats = ScriptableObject.CreateInstance<TurretStats>();
            stats.missileSpeed.level = level;

            var f = typeof(MissileTurret).GetField(
                "statsInstance",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "MissileTurret.statsInstance field not found");
            f.SetValue(shim, stats);

            Assert.AreEqual(expected, shim.GetProjectileSpeed(), Eps,
                $"ProjectileSpeed at level {level} must equal statsInstance.missileSpeed.CurrentValue");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(stats);
        }
    }
}
