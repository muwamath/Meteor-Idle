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
    }
}
