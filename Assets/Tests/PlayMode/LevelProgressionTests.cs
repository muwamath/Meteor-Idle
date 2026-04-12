using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class LevelProgressionTests : PlayModeTestFixture
    {
        private GameObject _lsGo;

        private IEnumerator SetupWithLevelState()
        {
            yield return SetupScene();

            // Clean up any existing LevelState singleton
            if (LevelState.Instance != null)
                Object.Destroy(LevelState.Instance.gameObject);
            yield return null;

            _lsGo = new GameObject("TestLevelState", typeof(LevelState));
            yield return null; // let Awake fire
        }

        private void TeardownWithLevelState()
        {
            TeardownScene();
            if (_lsGo != null) Object.Destroy(_lsGo);
        }

        [UnityTest]
        public IEnumerator AddMoney_AboveThreshold_AdvancesLevel()
        {
            yield return SetupWithLevelState();

            Assert.AreEqual(1, LevelState.Instance.CurrentLevel);

            GameManager.Instance.SetMoney(0);
            GameManager.Instance.AddMoney(10); // threshold at level 1 = 10

            Assert.AreEqual(2, LevelState.Instance.CurrentLevel);
            Assert.AreEqual(0, GameManager.Instance.Money);

            TeardownWithLevelState();
        }

        [UnityTest]
        public IEnumerator BossLevel_DoesNotAdvanceOnMoney()
        {
            yield return SetupWithLevelState();

            // Set to boss level via reflection
            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(LevelState.Instance, 10);
            Assert.IsTrue(LevelState.Instance.IsBossLevel);

            GameManager.Instance.SetMoney(0);
            GameManager.Instance.AddMoney(999999);

            // Should still be on level 10 — boss blocks advancement
            Assert.AreEqual(10, LevelState.Instance.CurrentLevel);

            TeardownWithLevelState();
        }

        [UnityTest]
        public IEnumerator BossDefeated_AdvancesToNextBlock()
        {
            yield return SetupWithLevelState();

            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(LevelState.Instance, 10);

            LevelState.Instance.BossDefeated();

            Assert.AreEqual(11, LevelState.Instance.CurrentLevel);

            TeardownWithLevelState();
        }

        [UnityTest]
        public IEnumerator BossFailed_ResetsToBlockStart()
        {
            yield return SetupWithLevelState();

            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(LevelState.Instance, 20);

            LevelState.Instance.BossFailed();

            Assert.AreEqual(11, LevelState.Instance.CurrentLevel);

            TeardownWithLevelState();
        }
    }
}
