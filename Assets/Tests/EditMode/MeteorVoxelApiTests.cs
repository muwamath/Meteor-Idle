using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class MeteorVoxelApiTests
    {
        private Meteor NewMeteor(int seed = 1, float scale = 1f)
        {
            var go = new GameObject(
                "TestMeteor",
                typeof(SpriteRenderer),
                typeof(CircleCollider2D),
                typeof(Meteor));
            var m = go.GetComponent<Meteor>();
            TestHelpers.InvokeAwake(m);
            m.Spawn(null, Vector3.zero, seed, scale);
            return m;
        }

        private static void Destroy(Meteor m)
        {
            if (m != null) Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void IsVoxelPresent_OutOfBounds_ReturnsFalse()
        {
            var m = NewMeteor();
            Assert.IsFalse(m.IsVoxelPresent(-1, 0));
            Assert.IsFalse(m.IsVoxelPresent(0, -1));
            Assert.IsFalse(m.IsVoxelPresent(10, 0));
            Assert.IsFalse(m.IsVoxelPresent(0, 10));
            Destroy(m);
        }

        [Test]
        public void IsVoxelPresent_AfterDestruction_ReturnsFalse()
        {
            var m = NewMeteor();
            int tx = -1, ty = -1;
            for (int y = 0; y < 10 && tx < 0; y++)
                for (int x = 0; x < 10 && tx < 0; x++)
                    if (m.IsVoxelPresent(x, y)) { tx = x; ty = y; }

            Assert.IsTrue(m.IsVoxelPresent(tx, ty));
            m.ApplyBlast(m.GetVoxelWorldPosition(tx, ty), 0.05f);
            Assert.IsFalse(m.IsVoxelPresent(tx, ty));
            Destroy(m);
        }

        [Test]
        public void GetVoxelWorldPosition_RoundTripsThroughInverseTransform()
        {
            var m = NewMeteor(scale: 1.3f);
            // The world position returned by GetVoxelWorldPosition should be inside the
            // meteor's world-space voxel extent (half-width 0.75 × 1.3 = 0.975 around
            // the meteor's transform position).
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    if (!m.IsVoxelPresent(x, y)) continue;
                    Vector3 p = m.GetVoxelWorldPosition(x, y);
                    Vector3 rel = p - m.transform.position;
                    Assert.LessOrEqual(Mathf.Abs(rel.x), 0.975f + 1e-4f);
                    Assert.LessOrEqual(Mathf.Abs(rel.y), 0.975f + 1e-4f);
                }
            }
            Destroy(m);
        }

        [Test]
        public void PickRandomPresentVoxel_FreshMeteor_AlwaysPicksAliveCell()
        {
            var m = NewMeteor();
            // Seeded RNG inside Unity's Random is shared, so we just run a bunch and
            // make sure every pick lands on a live cell.
            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(m.PickRandomPresentVoxel(out int gx, out int gy));
                Assert.IsTrue(m.IsVoxelPresent(gx, gy),
                    $"iteration {i}: picked ({gx},{gy}) which is not alive");
            }
            Destroy(m);
        }

        [Test]
        public void PickRandomPresentVoxel_DeadMeteor_ReturnsFalse()
        {
            // Smallest-size meteor: coreHp = 1 so a single huge blast kills
            // every cell in one pass.
            var m = NewMeteor(scale: 0.525f);
            m.ApplyBlast(Vector3.zero, 5f); // nuke everything
            Assert.AreEqual(0, m.AliveVoxelCount);

            bool picked = m.PickRandomPresentVoxel(out int gx, out int gy);
            Assert.IsFalse(picked);
            Destroy(m);
        }

        [Test]
        public void IsAlive_TracksDestructionState()
        {
            // Smallest-size meteor: coreHp = 1 so a single huge blast kills
            // every cell in one pass and flips IsAlive to false.
            var m = NewMeteor(scale: 0.525f);
            Assert.IsTrue(m.IsAlive);

            m.ApplyBlast(Vector3.zero, 5f);
            Assert.AreEqual(0, m.AliveVoxelCount);
            Assert.IsFalse(m.IsAlive);
            Destroy(m);
        }
    }
}
