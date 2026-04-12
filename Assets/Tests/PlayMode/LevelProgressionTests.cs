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

            if (LevelState.Instance != null)
                Object.Destroy(LevelState.Instance.gameObject);
            yield return null;

            _lsGo = new GameObject("TestLevelState", typeof(LevelState));
            yield return null;
        }

        private void TeardownWithLevelState()
        {
            TeardownScene();
            if (_lsGo != null) Object.Destroy(_lsGo);
        }

        [UnityTest]
        public IEnumerator CoreKill_AtThreshold_AdvancesLevel()
        {
            yield return SetupWithLevelState();

            Assert.AreEqual(1, LevelState.Instance.CurrentLevel);

            int threshold = LevelState.Instance.Threshold; // 3 at level 1
            for (int i = 0; i < threshold; i++)
                LevelState.Instance.RecordCoreKill();

            Assert.AreEqual(2, LevelState.Instance.CurrentLevel);
            Assert.AreEqual(0, LevelState.Instance.CoreKillsThisBlock);

            TeardownWithLevelState();
        }

        [UnityTest]
        public IEnumerator CoreKill_OnBossLevel_IsNoOp()
        {
            yield return SetupWithLevelState();

            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(LevelState.Instance, 10);
            Assert.IsTrue(LevelState.Instance.IsBossLevel);

            LevelState.Instance.RecordCoreKill();
            Assert.AreEqual(0, LevelState.Instance.CoreKillsThisBlock);
            Assert.AreEqual(10, LevelState.Instance.CurrentLevel);

            TeardownWithLevelState();
        }

        [UnityTest]
        public IEnumerator AddMoney_DoesNotAdvanceLevel()
        {
            yield return SetupWithLevelState();

            GameManager.Instance.SetMoney(0);
            GameManager.Instance.AddMoney(999999);

            // Money should NOT cause level advancement
            Assert.AreEqual(1, LevelState.Instance.CurrentLevel);
            Assert.AreEqual(999999, GameManager.Instance.Money);

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
        public IEnumerator BossFailed_GoesBack2Levels()
        {
            yield return SetupWithLevelState();

            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(LevelState.Instance, 20);

            LevelState.Instance.BossFailed();

            Assert.AreEqual(18, LevelState.Instance.CurrentLevel);
            Assert.AreEqual(0, LevelState.Instance.CoreKillsThisBlock);

            TeardownWithLevelState();
        }
    }
}
