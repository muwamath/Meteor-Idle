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
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 5,
                caliberWidth: 1,
                out Vector3 exitWorld);

            Assert.AreEqual(5, destroyed, "budget=5 should destroy exactly 5 live voxels");
            Assert.AreEqual(before - 5, m.AliveVoxelCount);
            Assert.Greater(exitWorld.y, -1f,
                "exit point should be above the entry point for an upward tunnel");
            Destroy(m);
        }

        [Test]
        public void EmptyVoxels_AreFreeAndDontConsumeBudget()
        {
            var m = NewMeteor();

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
                out _);

            Assert.AreEqual(3, destroyed,
                "tunnel should carve 3 cells beyond the existing hole");
            Assert.AreEqual(afterFirst - 3, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void BudgetCap_StopsTunnelEarly()
        {
            var m = NewMeteor();

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 2,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(2, destroyed, "budget=2 caps destruction at 2 voxels");
            Destroy(m);
        }

        [Test]
        public void BudgetExceedsLivePath_ReportsActualConsumed()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            // Budget 100 against a 10-cell-tall column. The tunnel must exit
            // the grid before budget is exhausted; destroyed should be less
            // than 100 but greater than 0 and match the alive-count delta.
            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 100,
                caliberWidth: 1,
                out _);

            Assert.Less(destroyed, 100,
                "budget should not be fully spent — grid exits first");
            Assert.Greater(destroyed, 0, "some cells should have been destroyed");
            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }

        [Test]
        public void Caliber2_CarvesThreeWideBand()
        {
            var mWide = NewMeteor();
            int destroyedWide = mWide.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _);
            Destroy(mWide);

            var mNarrow = NewMeteor();
            int destroyedNarrow = mNarrow.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 1,
                out _);
            Destroy(mNarrow);

            Assert.Greater(destroyedWide, destroyedNarrow,
                "caliber 2 should destroy more cells than caliber 1 at the same budget");
        }

        [Test]
        public void Caliber3_CarvesFiveWideBand()
        {
            var mC3 = NewMeteor();
            int destroyedC3 = mC3.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 3,
                out _);
            Destroy(mC3);

            var mC2 = NewMeteor();
            int destroyedC2 = mC2.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 50,
                caliberWidth: 2,
                out _);
            Destroy(mC2);

            Assert.Greater(destroyedC3, destroyedC2,
                "caliber 3 should destroy more cells than caliber 2 at the same budget");
        }

        [Test]
        public void DiagonalDirection_WalksCorrectly()
        {
            var m = NewMeteor();

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(-1f, -1f, 0f),
                worldDirection: new Vector3(1f, 1f, 0f).normalized,
                budget: 5,
                caliberWidth: 1,
                out _);

            Assert.Greater(destroyed, 0,
                "diagonal tunnel should destroy at least some cells");
            Destroy(m);
        }

        [Test]
        public void ExitPoint_IsReturnedInWorldSpace()
        {
            var m = NewMeteor();

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
                out _);

            Assert.AreEqual(0, destroyed,
                "tunnel through dead meteor must be a no-op");
            Destroy(m);
        }

        [Test]
        public void AliveCount_DecrementsByConsumedAmount()
        {
            var m = NewMeteor();
            int before = m.AliveVoxelCount;

            int destroyed = m.ApplyTunnel(
                entryWorld: new Vector3(0f, -1f, 0f),
                worldDirection: Vector3.up,
                budget: 4,
                caliberWidth: 1,
                out _);

            Assert.AreEqual(before - destroyed, m.AliveVoxelCount);
            Destroy(m);
        }
    }
}
