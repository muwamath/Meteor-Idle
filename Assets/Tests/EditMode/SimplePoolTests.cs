using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class SimplePoolTests
    {
        private GameObject _parent;
        private Transform _parentT;
        private FloatingText _prefab;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("TestParent");
            _parentT = _parent.transform;
            // FloatingText is a thin MonoBehaviour — cheap to instantiate, no runtime
            // dependencies we need in this test.
            var prefabGo = new GameObject("TestPrefab", typeof(FloatingText));
            _prefab = prefabGo.GetComponent<FloatingText>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null) Object.DestroyImmediate(_prefab.gameObject);
            if (_parent != null) Object.DestroyImmediate(_parent);
            _prefab = null;
            _parent = null;
        }

        [Test]
        public void Prewarm_CreatesInactiveInstancesUnderParent()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 3);

            // Active list is empty (all prewarmed instances are inactive).
            Assert.AreEqual(0, pool.Active.Count);
            // Parent has 3 children (the prewarmed instances).
            Assert.AreEqual(3, _parentT.childCount);
            for (int i = 0; i < 3; i++)
                Assert.IsFalse(_parentT.GetChild(i).gameObject.activeSelf);
        }

        [Test]
        public void Get_ActivatesAnInstance_AddsToActiveList()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 2);

            var a = pool.Get();
            Assert.IsNotNull(a);
            Assert.IsTrue(a.gameObject.activeSelf);
            Assert.AreEqual(1, pool.Active.Count);
            Assert.AreSame(a, pool.Active[0]);
        }

        [Test]
        public void Get_OnEmptyPool_InstantiatesNew()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 0);
            int childrenBefore = _parentT.childCount;

            var a = pool.Get();

            Assert.IsNotNull(a);
            Assert.AreEqual(childrenBefore + 1, _parentT.childCount);
            Assert.AreEqual(1, pool.Active.Count);
        }

        [Test]
        public void Release_DeactivatesAndReturnsToPool()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 1);
            var a = pool.Get();
            Assert.AreEqual(1, pool.Active.Count);

            pool.Release(a);

            Assert.AreEqual(0, pool.Active.Count);
            Assert.IsFalse(a.gameObject.activeSelf);
        }

        [Test]
        public void Get_AfterRelease_ReusesSameInstance()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 1);
            var a = pool.Get();
            pool.Release(a);

            var b = pool.Get();

            Assert.AreSame(a, b, "the released instance should come back out of the pool");
            Assert.AreEqual(1, _parentT.childCount, "no new instance should have been created");
        }

        [Test]
        public void Release_UnknownInstance_IsNoOp()
        {
            var pool = new SimplePool<FloatingText>(_prefab, _parentT, prewarm: 1);
            var orphanGo = new GameObject("Orphan", typeof(FloatingText));
            var orphan = orphanGo.GetComponent<FloatingText>();

            // Releasing something that was never Get()'d from this pool should not
            // crash and should leave the active list untouched.
            pool.Release(orphan);
            Assert.AreEqual(0, pool.Active.Count);

            Object.DestroyImmediate(orphanGo);
        }
    }
}
