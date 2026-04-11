using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class MeteorApplyTunnelTests
    {
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
        public void FreshMeteor_VerticalTunnelFromBottom_CarvesStraightLine()
        {
            // Smallest size (coreHp=1) so the weight-per-damage accounting
            // matches the legacy "5 cells destroyed" expectation.
            var m = NewMeteor(scale: 0.525f);
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out Vector3 exitWorld).TotalDestroyed;

            Assert.AreEqual(5, destroyed, "budget=5 should destroy exactly 5 live voxels (all HP 1)");
            Assert.AreEqual(before - 5, m.AliveVoxelCount);
            Assert.Greater(exitWorld.y, -1f,
                "exit point should be above the entry point for an upward tunnel");
            Destroy(m);
        }

        [Test]
        public void EmptyVoxels_AreFreeAndDontConsumeBudget()
        {
            // Smallest size so all cells are HP 1 and weight-per-damage is
            // identical to the pre-Iter-1 "per-cell" accounting the test
            // originally used.
            var m = NewMeteor(scale: 0.525f);

            // Pre-erode column 5 at the bottom via a small blast so the first
            // few voxels in the path are already dead before the tunnel fires.
            m.ApplyBlast(new Vector3(0f, -0.8f, 0f), 0.28f);
            int afterFirst = m.AliveVoxelCount;

            // Now tunnel upward through the same column. Empty cells at the
            // bottom should not consume budget — the full budget of 3 should
            // still carve 3 cells further up.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 3,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.AreEqual(3, destroyed,
                "tunnel should carve 3 cells beyond the existing hole");
            Assert.AreEqual(afterFirst - 3, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void BudgetCap_StopsTunnelEarly()
        {
            var m = NewMeteor(scale: 0.525f);

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 2,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.AreEqual(2, destroyed, "budget=2 caps destruction at 2 voxels (HP 1 each)");
            Destroy(m);
        }

        [Test]
        public void BudgetExceedsLivePath_ReportsActualConsumed()
        {
            var m = NewMeteor(scale: 0.525f);
            int before = m.AliveVoxelCount;

            // Budget 100 against a 10-cell-tall column. The tunnel must exit
            // the grid before budget is exhausted; destroyed should be less
            // than 100 but greater than 0 and match the alive-count delta.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 100,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.Less(destroyed, 100,
                "budget should not be fully spent — grid exits first");
            Assert.Greater(destroyed, 0, "some cells should have been destroyed");
            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void Caliber2_CarvesThreeWideBand()
        {
            var mWide = NewMeteor(scale: 0.525f);
            int destroyedWide = mWide.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _).TotalDestroyed;
            Destroy(mWide);

            var mNarrow = NewMeteor(scale: 0.525f);
            int destroyedNarrow = mNarrow.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 1,
                out _).TotalDestroyed;
            Destroy(mNarrow);

            Assert.Greater(destroyedWide, destroyedNarrow,
                "caliber 2 should destroy more cells than caliber 1 at the same budget");
        }

        [Test]
        public void Caliber3_CarvesFiveWideBand()
        {
            var mC3 = NewMeteor(scale: 0.525f);
            int destroyedC3 = mC3.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 3,
                out _).TotalDestroyed;
            Destroy(mC3);

            var mC2 = NewMeteor(scale: 0.525f);
            int destroyedC2 = mC2.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _).TotalDestroyed;
            Destroy(mC2);

            Assert.Greater(destroyedC3, destroyedC2,
                "caliber 3 should destroy more cells than caliber 2 at the same budget");
        }

        [Test]
        public void DiagonalDirection_WalksCorrectly()
        {
            var m = NewMeteor(scale: 0.525f);

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(-1f, -1f, 0f),
                worldDirection: new Vector3(1f, 1f, 0f).normalized,
                budget: 5,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.Greater(destroyed, 0,
                "diagonal tunnel should destroy at least some cells");
            Destroy(m);
        }

        [Test]
        public void ExitPoint_IsReturnedInWorldSpace()
        {
            var m = NewMeteor(scale: 0.525f);

            m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out Vector3 exitWorld);

            Assert.Greater(exitWorld.y, -1f,
                "exit point should be above the entry for an upward tunnel");
            Destroy(m);
        }

        [Test]
        public void DeadMeteor_ReturnsZeroAndDoesNotThrow()
        {
            // Smallest-size meteor: coreHp = 1, so a single huge blast kills
            // every cell (dirt AND cores) in one pass.
            var m = NewMeteor(scale: 0.525f);
            m.ApplyBlast(Vector3.zero, 5f); // nuke everything
            Assert.AreEqual(0, m.AliveVoxelCount);

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.AreEqual(0, destroyed,
                "tunnel through dead meteor must be a no-op");
            Destroy(m);
        }

        [Test]
        public void AliveCount_DecrementsByConsumedAmount()
        {
            var m = NewMeteor(scale: 0.525f);
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 4,
                caliberWidth: 1,
                out _).TotalDestroyed;

            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void ApplyTunnel_CoreWeightCost_ConsumesBudgetPerDamagePoint()
        {
            // Spawn a size-1.2 meteor so at least one core exists with HP 5.
            // Fire a narrow tunnel with budget 5 in a direction that starts
            // outside the grid and passes through the meteor center. The
            // walker consumes 1 budget per HP point dealt, so a direct hit
            // on a HP 5 core consumes 5 budget by itself.
            var m = NewMeteor(seed: 7, scale: 1.2f);

            // Sanity: at size 1.2 the generator guarantees at least one core.
            Assert.Greater(m.AliveVoxelCount, 0);

            var result = m.ApplyTunnel(
                entryWorld: new Vector3(-2f, 0f, 0f),
                worldDirection: Vector3.right,
                budget: 5,
                caliberWidth: 1,
                out _);

            // Walker must never exceed budget.
            Assert.LessOrEqual(result.damageDealt, 5,
                "walker must not exceed the budget");
            // Sanity: damageDealt >= TotalDestroyed always (damage counts
            // each HP point, TotalDestroyed counts only cells that reached 0).
            Assert.GreaterOrEqual(result.damageDealt, result.TotalDestroyed,
                "damageDealt must be >= TotalDestroyed");

            Destroy(m);
        }

        [Test]
        public void ApplyTunnel_EmptyCellsAreFree_SecondShotCostsZero()
        {
            // Small meteor (HP 1 everywhere) so the first shot cleanly
            // removes cells along its path. The second shot along the
            // SAME path should find only empty cells and consume zero
            // budget, proving empty cells are free.
            var m = NewMeteor(seed: 1, scale: 0.525f);

            // First shot: carve a wide tunnel through the middle.
            m.ApplyTunnel(
                entryWorld: new Vector3(-2f, 0f, 0f),
                worldDirection: Vector3.right,
                budget: 50,
                caliberWidth: 1,
                out _);

            // Second shot along exactly the same line.
            var result2 = m.ApplyTunnel(
                entryWorld: new Vector3(-2f, 0f, 0f),
                worldDirection: Vector3.right,
                budget: 50,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(0, result2.damageDealt,
                "empty-cell walk must not consume any budget");
            Assert.AreEqual(0, result2.TotalDestroyed,
                "empty-cell walk must not destroy anything");
            Destroy(m);
        }
    }
}
