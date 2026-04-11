using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class MeteorApplyBlastTests
    {
        // Some seeds produce meteors with empty cells along the cardinal rim; tests that
        // want a guaranteed live cell at a specific coordinate should pick a seed that
        // gives a filled rim in that direction. Seed 42 leaves column 5 empty at rows 0–1.
        private const int FullShapeSeed = 1;

        private Meteor NewMeteor(int seed = FullShapeSeed, float scale = 1f)
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
        public void FreshMeteor_CenterImpact_DestroysAliveCluster()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f);

            Assert.Greater(destroyed, 0, "center-hit should always destroy voxels");
            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void ContactPointOnLiveCell_DoesNotWalkInward()
        {
            // Pick a live cell near the outside rim and aim straight at it.
            var m = NewMeteor();
            Vector3 cellWorld = Vector3.zero;
            int tx = -1, ty = -1;
            for (int y = 0; y < 10 && tx < 0; y++)
            {
                for (int x = 0; x < 10 && tx < 0; x++)
                {
                    if (m.IsVoxelPresent(x, y))
                    {
                        tx = x; ty = y;
                        cellWorld = m.GetVoxelWorldPosition(x, y);
                    }
                }
            }
            Assert.GreaterOrEqual(tx, 0);

            int destroyed = m.ApplyBlast(cellWorld, 0.28f);

            Assert.Greater(destroyed, 0);
            Assert.IsFalse(m.IsVoxelPresent(tx, ty),
                "the cell we aimed at should be destroyed");
            Destroy(m);
        }

        [Test]
        public void BottomRimImpact_CratersBottomOfShape_NotInterior()
        {
            var m = NewMeteor();

            // Impact comes from below the meteor, well outside the voxel grid vertically.
            Vector3 bottomContact = new Vector3(0f, -1.2f, 0f);
            int destroyed = m.ApplyBlast(bottomContact, 0.28f);

            Assert.Greater(destroyed, 0, "outside-bottom hit must crater something");

            // After the blast, verify the damage is in the lower half of the grid, not
            // the upper half. That's the whole point of the walk-inward fix: craters
            // anchor to the near side of the contact, they don't punch holes in the
            // middle or the far side.
            int lowerGone = 0, upperGone = 0;
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 5; y++) if (!m.IsVoxelPresent(x, y)) lowerGone++;
                for (int y = 5; y < 10; y++) if (!m.IsVoxelPresent(x, y)) upperGone++;
            }
            Assert.Greater(lowerGone, upperGone,
                "bottom impact should erode more of the bottom half than the top");
            Destroy(m);
        }

        [Test]
        public void RepeatedBottomHits_CraterEachTime_UntilTunneled()
        {
            var m = NewMeteor();
            Vector3 bottomContact = new Vector3(0f, -1.2f, 0f);

            int totalDestroyed = 0;
            int hitsBeforeZero = 0;
            for (int i = 0; i < 10; i++)
            {
                int d = m.ApplyBlast(bottomContact, 0.28f);
                if (d == 0) break;
                hitsBeforeZero++;
                totalDestroyed += d;
            }

            // We expect at least a couple hits that all crater before the near side is
            // fully bored through. The exact count depends on the meteor shape, but the
            // first two hits should definitely both produce damage.
            Assert.GreaterOrEqual(hitsBeforeZero, 2,
                $"expected >=2 craters before tunneling out; got {hitsBeforeZero} (total destroyed={totalDestroyed})");
            Destroy(m);
        }

        [Test]
        public void FullyTunneled_FarSideRemainsIntact()
        {
            var m = NewMeteor();
            Vector3 bottomContact = new Vector3(0f, -1.2f, 0f);

            // Hit enough times to bore through near side.
            for (int i = 0; i < 20; i++) m.ApplyBlast(bottomContact, 0.28f);

            // Top-row cells (y = 9) should still have at least one survivor for a fresh
            // seed. If the walk-inward were reaching the far rim, this would be empty.
            int topAlive = 0;
            for (int x = 0; x < 10; x++) if (m.IsVoxelPresent(x, 9)) topAlive++;
            Assert.Greater(topAlive, 0,
                "far-side (top) voxels must survive repeated bottom hits — walk must not reach through");
            Destroy(m);
        }

        [Test]
        public void ImpactFarOutsideCollider_ClampsAndStillCraters()
        {
            var m = NewMeteor();
            // Far above the meteor (the game would never trigger this via physics, but
            // we verify the clamp + walk behaves): the clamp snaps gy to the top row,
            // then walk-inward finds the nearest alive cell along the ray to center.
            Vector3 wayAbove = new Vector3(0f, 10f, 0f);
            int destroyed = m.ApplyBlast(wayAbove, 0.28f);
            Assert.Greater(destroyed, 0,
                "clamp should pull an out-of-bounds impact to the rim and crater it");
            Destroy(m);
        }

        [Test]
        public void DeadMeteor_ReturnsZero()
        {
            var m = NewMeteor();
            // Nuke everything by blasting the center with a huge radius.
            m.ApplyBlast(Vector3.zero, 5f);
            Assert.AreEqual(0, m.AliveVoxelCount);

            // A second blast on a dead meteor must be a no-op.
            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f);
            Assert.AreEqual(0, destroyed);
            Destroy(m);
        }

        [Test]
        public void PartialBlast_UpdatesAliveCount()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;
            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f);
            Assert.AreEqual(before - destroyed, m.AliveVoxelCount,
                "AliveVoxelCount must track exactly what ApplyBlast reports");
            Destroy(m);
        }

        [Test]
        public void FadeThreshold_AboveThreshold_MeteorIsAlive()
        {
            var m = NewMeteor();
            // Spawn places the meteor at (0,0), well above fadeStartY (-7.88).
            m.transform.position = new Vector3(0f, -7.0f, 0f);
            TestHelpers.InvokeUpdate(m);
            Assert.IsTrue(m.IsAlive,
                "meteor above the fade threshold must stay alive");
            Destroy(m);
        }

        [Test]
        public void FadeThreshold_BelowThreshold_BecomesUntargetable()
        {
            var m = NewMeteor();
            // Drop the meteor below fadeStartY and tick Update once.
            m.transform.position = new Vector3(0f, -8.0f, 0f);
            TestHelpers.InvokeUpdate(m);
            Assert.IsFalse(m.IsAlive,
                "meteor below the fade threshold must drop out of turret targeting");
            Destroy(m);
        }

        [Test]
        public void ScaleInvariant_SameGridDestructionRegardlessOfScale()
        {
            // The key property from CLAUDE.md: gridRadius = worldRadius * localToGrid —
            // blasts cover the same cell count on any meteor size. We hit two identical
            // meteors at different scales with the same world blast radius and expect
            // roughly the same destroyed count.
            var small = NewMeteor(seed: 7, scale: 0.75f);
            var large = NewMeteor(seed: 7, scale: 1.5f);

            int dSmall = small.ApplyBlast(small.transform.position, 0.28f);
            int dLarge = large.ApplyBlast(large.transform.position, 0.28f);

            Assert.AreEqual(dSmall, dLarge,
                "same seed + same world blast radius should destroy the same cell count");
            Destroy(small);
            Destroy(large);
        }
    }
}
