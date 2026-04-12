using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class GameManagerDropRegistryTests
    {
        private GameObject _gmGo;
        private GameManager _gm;

        [SetUp]
        public void SetUp()
        {
            _gmGo = new GameObject("TestGM", typeof(GameManager));
            _gm = _gmGo.GetComponent<GameManager>();
            TestHelpers.InvokeAwake(_gm);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_gmGo); }

        [Test]
        public void RegisterDrop_AddsToActiveList()
        {
            var dropGo = new GameObject("Drop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, 5);

            _gm.RegisterDrop(drop);
            Assert.Contains(drop, (System.Collections.ICollection)_gm.ActiveDrops);
            Object.DestroyImmediate(dropGo);
        }

        [Test]
        public void UnregisterDrop_RemovesFromActiveList()
        {
            var dropGo = new GameObject("Drop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(drop);
            drop.Spawn(Vector3.zero, 5);

            _gm.RegisterDrop(drop);
            _gm.UnregisterDrop(drop);
            Assert.AreEqual(0, _gm.ActiveDrops.Count);
            Object.DestroyImmediate(dropGo);
        }
    }
}
