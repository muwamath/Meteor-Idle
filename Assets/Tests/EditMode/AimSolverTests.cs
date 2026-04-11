using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class AimSolverTests
    {
        private const float Eps = 0.001f;

        [Test]
        public void StationaryTarget_LeadEqualsTargetPosition()
        {
            Vector2 shooter = new Vector2(0f, 0f);
            Vector2 target  = new Vector2(0f, 5f);
            Vector2 targetVel = Vector2.zero;
            float projectileSpeed = 10f;

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, targetVel, projectileSpeed);

            Assert.AreEqual(target.x, lead.x, Eps);
            Assert.AreEqual(target.y, lead.y, Eps);
        }

        [Test]
        public void PerpendicularTarget_LeadAheadOfTarget()
        {
            // Shooter at origin. Target directly above (y=5) moving right at 1 u/s.
            // Projectile speed 10. The exact intercept time solves
            // (1 - 100)·t² + 0·t + 25 = 0  →  t² = 25/99  →  t ≈ 0.5025.
            // Lead point = target + vel·t = (0.5025, 5).
            Vector2 shooter = Vector2.zero;
            Vector2 target  = new Vector2(0f, 5f);
            Vector2 targetVel = new Vector2(1f, 0f);
            float projectileSpeed = 10f;

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, targetVel, projectileSpeed);

            Assert.AreEqual(0.5025f, lead.x, 0.005f, "lead X should be ahead of target along velocity");
            Assert.AreEqual(5f,      lead.y, Eps);
        }

        [Test]
        public void TargetMovingAway_LeadBeyondTarget()
        {
            // Shooter at origin. Target at (0, 5) moving straight up at 1 u/s.
            // Projectile speed 10. Target runs away along the fire line, so
            // intercept time t solves (1 - 100)·t² + (2·5·1)·t + 25 = 0
            // → -99·t² + 10·t + 25 = 0 → t ≈ 0.555.
            // Lead point = (0, 5 + 0.555) = (0, 5.555).
            Vector2 shooter = Vector2.zero;
            Vector2 target  = new Vector2(0f, 5f);
            Vector2 targetVel = new Vector2(0f, 1f);
            float projectileSpeed = 10f;

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, targetVel, projectileSpeed);

            Assert.AreEqual(0f, lead.x, Eps);
            Assert.Greater(lead.y, 5f, "lead should be farther from shooter than target");
            Assert.AreEqual(5.555f, lead.y, 0.01f);
        }

        [Test]
        public void TargetFasterThanProjectile_ReturnsTargetPosition()
        {
            // Target escapes the projectile — no positive-time solution.
            // Shooter at origin, target at (10, 0) moving at (10, 0), projectile 1.
            Vector2 shooter = Vector2.zero;
            Vector2 target  = new Vector2(10f, 0f);
            Vector2 targetVel = new Vector2(10f, 0f);
            float projectileSpeed = 1f;

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, targetVel, projectileSpeed);

            Assert.AreEqual(target.x, lead.x, Eps);
            Assert.AreEqual(target.y, lead.y, Eps);
        }

        [Test]
        public void ZeroVelocityAndZeroProjectile_FallsBackToTarget()
        {
            // Degenerate: a = 0 and b = 0, every coefficient is zero. The solver
            // must not divide by zero — it should fall back to target position.
            Vector2 shooter = Vector2.zero;
            Vector2 target  = new Vector2(3f, 4f);

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, Vector2.zero, 0f);

            Assert.AreEqual(3f, lead.x, Eps);
            Assert.AreEqual(4f, lead.y, Eps);
        }
    }
}
