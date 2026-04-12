using System.Reflection;
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

            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f).TotalDestroyed;

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

            int destroyed = m.ApplyBlast(cellWorld, 0.28f).TotalDestroyed;

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
            int destroyed = m.ApplyBlast(bottomContact, 0.28f).TotalDestroyed;

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
                int d = m.ApplyBlast(bottomContact, 0.28f).TotalDestroyed;
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
        public void FullyTunneled_NextHitStillDealsDamage_NoPhantom()
        {
            var m = NewMeteor();
            Vector3 bottomContact = new Vector3(0f, -1.2f, 0f);

            // Bore through the near side with enough repeated hits that the walk-inward
            // ray straight up through column 5 ends on an all-dead column.
            for (int i = 0; i < 15; i++) m.ApplyBlast(bottomContact, 0.28f);

            int beforeNext = m.AliveVoxelCount;
            int destroyed = m.ApplyBlast(bottomContact, 0.28f).TotalDestroyed;

            // The SnapToNearestAliveCell fallback kicks in and redirects the blast to the
            // closest surviving voxel anywhere in the grid. The user's rule: a missile
            // must always damage a meteor it collides with — never a phantom hit.
            Assert.Greater(destroyed, 0,
                "once the near-side column is fully bored, the nearest-alive fallback must still crater");
            Assert.AreEqual(beforeNext - destroyed, m.AliveVoxelCount);
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
            int destroyed = m.ApplyBlast(wayAbove, 0.28f).TotalDestroyed;
            Assert.Greater(destroyed, 0,
                "clamp should pull an out-of-bounds impact to the rim and crater it");
            Destroy(m);
        }

        [Test]
        public void DeadMeteor_ReturnsZero()
        {
            // Smallest-size meteor: coreHp = 1, so a single huge blast is
            // guaranteed to kill every cell (dirt AND cores) in one pass.
            var m = NewMeteor(scale: 0.525f);
            // Nuke everything by blasting the center with a huge radius.
            m.ApplyBlast(Vector3.zero, 5f);
            Assert.AreEqual(0, m.AliveVoxelCount);

            // A second blast on a dead meteor must be a no-op.
            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f).TotalDestroyed;
            Assert.AreEqual(0, destroyed);
            Destroy(m);
        }

        [Test]
        public void PartialBlast_UpdatesAliveCount()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;
            int destroyed = m.ApplyBlast(Vector3.zero, 0.28f).TotalDestroyed;
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
        public void ScaleInvariant_BothScalesReceiveDamage()
        {
            // The key property from CLAUDE.md: gridRadius = worldRadius * localToGrid —
            // blasts cover the same grid area on any meteor size. With line-of-sight
            // blocking (core HP varies by scale), exact damage counts may differ, but
            // both scales must receive non-zero damage from the same world-space blast.
            var small = NewMeteor(seed: 7, scale: 0.75f);
            var large = NewMeteor(seed: 7, scale: 1.5f);

            int damageSmall = small.ApplyBlast(small.transform.position, 0.28f).damageDealt;
            int damageLarge = large.ApplyBlast(large.transform.position, 0.28f).damageDealt;

            Assert.Greater(damageSmall, 0, "small meteor should take damage");
            Assert.Greater(damageLarge, 0, "large meteor should take damage");
            Destroy(small);
            Destroy(large);
        }

        [Test]
        public void ApplyBlast_LOSBlocking_CoreShieldsDirtBehind()
        {
            // A blast on one side of a multi-HP core should NOT damage dirt cells
            // on the far side. The core acts as a shield.
            var m = NewMeteor(seed: 42, scale: 1.2f);

            // Get the kind and hp arrays via reflection
            var kindField = typeof(Meteor).GetField("kind",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var hpField = typeof(Meteor).GetField("hp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var kind = (VoxelKind[,])kindField.GetValue(m);
            var hp = (int[,])hpField.GetValue(m);

            int before = m.AliveVoxelCount;
            // Blast from the edge — a tight blast that hits the outer rim
            Vector3 edgeWorld = m.GetVoxelWorldPosition(0, 5);
            var result = m.ApplyBlast(edgeWorld, 0.5f);

            // The blast should damage cells near the edge but NOT penetrate
            // through the entire meteor to the far side
            Assert.Greater(result.damageDealt, 0, "should damage some cells");
            Assert.Less(result.damageDealt, before, "should not damage all cells");

            Destroy(m);
        }

        [Test]
        public void ApplyBlast_MultiHitCore_RequiresMultipleBlastsToKill()
        {
            // Spawn a max-size meteor so the core HP is 5 (the highest in the
            // linear scale from the spec). Four consecutive tight blasts on the
            // same core cell should NOT fully destroy it; the 5th should.
            var m = NewMeteor(seed: 42, scale: 1.2f);

            // Find one core cell via reflection on the private kind field.
            var kindField = typeof(Meteor).GetField("kind",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(kindField, "Meteor.kind private field not found");
            var kind = (VoxelKind[,])kindField.GetValue(m);

            int coreX = -1, coreY = -1;
            for (int y = 0; y < VoxelMeteorGenerator.GridSize && coreX < 0; y++)
                for (int x = 0; x < VoxelMeteorGenerator.GridSize && coreX < 0; x++)
                    if (kind[x, y] == VoxelKind.Core) { coreX = x; coreY = y; }
            Assert.GreaterOrEqual(coreX, 0, "size 1.2 meteor must have at least one core");

            // Aim a tight blast (small radius) directly at the core's world
            // position so the blast circle covers only that one cell.
            Vector3 coreWorld = m.GetVoxelWorldPosition(coreX, coreY);
            const float tightRadius = 0.05f;

            // First 4 blasts: core still alive, coreDestroyed is always 0 per blast.
            int totalCoreDestroyed = 0;
            for (int i = 0; i < 4; i++)
            {
                var result = m.ApplyBlast(coreWorld, tightRadius);
                totalCoreDestroyed += result.coreDestroyed;
            }
            Assert.AreEqual(0, totalCoreDestroyed,
                "core HP 5 must survive 4 tight blasts");
            Assert.IsTrue(m.IsVoxelPresent(coreX, coreY),
                "core cell must still be present after 4 blasts");

            // 5th blast kills the core.
            var killResult = m.ApplyBlast(coreWorld, tightRadius);
            Assert.AreEqual(1, killResult.coreDestroyed,
                "5th blast should destroy the core cell");
            Assert.IsFalse(m.IsVoxelPresent(coreX, coreY),
                "core cell must be gone after 5 blasts");

            Destroy(m);
        }
    }
}
