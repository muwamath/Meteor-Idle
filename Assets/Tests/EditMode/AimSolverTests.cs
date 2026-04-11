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
    }
}
