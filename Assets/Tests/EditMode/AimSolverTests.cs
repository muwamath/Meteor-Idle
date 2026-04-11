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

        // Parameterized sweep verifying the core invariant: the lead point
        // returned by the solver, treated as the destination of a projectile
        // moving at projectileSpeed from the shooter, lands at exactly the
        // target's future position after the travel time. If this holds across
        // a wide parameter space, the solver math is right regardless of edge
        // cases. This is the regression gate that catches the "over-lead /
        // under-lead at specific speed" class of bug the user flagged on
        // 2026-04-11 during the iter/aim-fixes dev-WebGL verify.
        //
        // Parameters: (shooterX, shooterY, targetX, targetY, vx, vy, projectileSpeed)
        [TestCase( 0f,  0f,   0f,  8f,    0f,    -0.5f,    6f)]   // straight-down meteor, base railgun
        [TestCase( 0f,  0f,   0f,  8f,    0.3f,  -0.5f,    6f)]   // drifting meteor
        [TestCase( 0f,  0f,   0f,  8f,    0f,    -0.5f,   36f)]   // upgraded railgun speed
        [TestCase( 0f,  0f,   0f,  8f,    0.3f,  -0.5f,   36f)]   // both upgraded
        [TestCase( 0f,  0f,   5f,  8f,    0f,    -0.5f,    6f)]   // off-axis meteor
        [TestCase(-8f,  0f,   8f,  5f,   -0.4f,  -0.6f,    6f)]   // side slot, meteor on far side
        [TestCase( 0f,  0f,   0f,  8f,   -0.4f,  -0.4f,  153f)]   // level-49 railgun speed (user's session)
        [TestCase( 0f,  0f,   0f, 12f,    0.4f,  -0.67f,   4f)]   // base missile, slow drift
        [TestCase( 0f,  0f,   0f, 12f,    0.4f,  -0.67f,  20f)]   // upgraded missile
        public void InterceptPointSelfConsistent(
            float sx, float sy, float tx, float ty,
            float vx, float vy, float projectileSpeed)
        {
            Vector2 shooter = new Vector2(sx, sy);
            Vector2 target  = new Vector2(tx, ty);
            Vector2 velocity = new Vector2(vx, vy);

            Vector2 lead = AimSolver.PredictInterceptPoint(shooter, target, velocity, projectileSpeed);

            float distanceFromShooter = (lead - shooter).magnitude;
            float predictedTime = distanceFromShooter / projectileSpeed;
            Vector2 targetFuturePos = target + velocity * predictedTime;

            Assert.AreEqual(targetFuturePos.x, lead.x, 0.001f,
                "lead X must equal target X at predicted intercept time");
            Assert.AreEqual(targetFuturePos.y, lead.y, 0.001f,
                "lead Y must equal target Y at predicted intercept time");
        }
    }
}
