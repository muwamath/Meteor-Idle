using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class CoreDropTests
    {
        [Test]
        public void Spawn_SetsPositionAndValue_AndStartsUnclaimed()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(1.5f, 2f, 0f), value: 5);

            Assert.AreEqual(new Vector3(1.5f, 2f, 0f), drop.transform.position);
            Assert.AreEqual(5, drop.Value);
            Assert.IsFalse(drop.IsClaimed);
            Assert.IsTrue(drop.IsAlive);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Drift_MovesDownwardSlowlyOnTick()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(0f, 0f, 0f), value: 5);

            float y0 = drop.transform.position.y;
            drop.TickDrift(dt: 1f);
            Assert.Less(drop.transform.position.y, y0, "drop drifts downward");
            // Drift rate should be < 0.15 world/sec (10-20% of base meteor fall 0.4..0.67)
            Assert.Greater(drop.transform.position.y, y0 - 0.2f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TickDrift_PastDespawnY_MarksForDespawn()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(new Vector3(0f, -9f, 0f), value: 5);

            drop.TickDrift(dt: 10f); // should push well below despawnY
            Assert.IsFalse(drop.IsAlive, "drop despawns when below despawnY");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Claim_MarksClaimed_BlocksDoubleClaim()
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, value: 5);

            Assert.IsTrue(drop.TryClaim(), "first claim succeeds");
            Assert.IsTrue(drop.IsClaimed);
            Assert.IsFalse(drop.TryClaim(), "second claim fails");
            Object.DestroyImmediate(go);
        }
    }
}
