using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneBodyTests
    {
        [Test]
        public void Integrate_WithZeroDesired_AppliesDamping()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 2f);
            body.Velocity = new Vector2(3f, 0f);
            body.DesiredThrust = Vector2.zero;
            body.Integrate(dt: 0.5f);
            Assert.Less(body.Velocity.magnitude, 3f, "damping reduces speed");
            Assert.Greater(body.Velocity.magnitude, 0f);
        }

        [Test]
        public void Integrate_WithDesired_AcceleratesTowardThrustCap()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
            body.DesiredThrust = Vector2.right;
            body.Integrate(dt: 10f);
            Assert.AreEqual(4f, body.Velocity.x, 0.01f);
            Assert.AreEqual(0f, body.Velocity.y, 0.01f);
        }

        [Test]
        public void Integrate_AdvancesPositionByVelocity()
        {
            var body = new DroneBody(new Vector2(1f, 2f), thrustCap: 4f, dampingPerSec: 0f);
            body.Velocity = new Vector2(2f, 0f);
            body.DesiredThrust = Vector2.zero;
            body.Integrate(dt: 0.5f);
            Assert.AreEqual(2f, body.Position.x, 0.01f);
            Assert.AreEqual(2f, body.Position.y, 0.01f);
        }

        [Test]
        public void ApplyPushKick_AddsToVelocity()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 4f, dampingPerSec: 0f);
            body.Velocity = new Vector2(1f, 0f);
            body.ApplyPushKick(new Vector2(0f, 3f));
            Assert.AreEqual(1f, body.Velocity.x, 0.01f);
            Assert.AreEqual(3f, body.Velocity.y, 0.01f);
        }

        [Test]
        public void LimpHomeMode_CutsEffectiveThrustCapTo25Percent()
        {
            var body = new DroneBody(Vector2.zero, thrustCap: 8f, dampingPerSec: 0f);
            body.LimpHomeMode = true;
            body.DesiredThrust = Vector2.right;
            body.Integrate(dt: 10f);
            Assert.AreEqual(2f, body.Velocity.x, 0.01f,
                "limp-home clamps velocity to 25% of thrustCap");
        }
    }
}
