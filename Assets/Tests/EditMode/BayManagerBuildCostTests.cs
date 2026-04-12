using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class BayManagerBuildCostTests
    {
        private BayManager MakeManager(int[] costs)
        {
            var go = new GameObject("TestBayManager", typeof(BayManager));
            var mgr = go.GetComponent<BayManager>();
            typeof(BayManager)
                .GetField("bayBuildCosts", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(mgr, costs);
            return mgr;
        }

        [Test]
        public void NextBuildCost_UsesFirstEntryWhenUnpurchased()
        {
            var m = MakeManager(new[] { 200, 600 });
            Assert.AreEqual(200, m.NextBuildCost());
            Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void NextBuildCost_AdvancesWithPurchaseCount()
        {
            var m = MakeManager(new[] { 200, 600 });
            typeof(BayManager)
                .GetField("purchasedCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m, 1);
            Assert.AreEqual(600, m.NextBuildCost());
            Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void NextBuildCost_AfterTable_OverflowsFromLastEntry()
        {
            var m = MakeManager(new[] { 200, 600 });
            typeof(BayManager)
                .GetField("purchasedCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(m, 2);
            int cost = m.NextBuildCost();
            Assert.Greater(cost, 600);
            Object.DestroyImmediate(m.gameObject);
        }
    }
}
