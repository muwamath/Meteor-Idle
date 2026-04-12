using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class DroneAvoidanceTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator DroneThrustingPastMeteor_NeverEntersSafetyRadius()
        {
            yield return SetupScene();

            var meteor = SpawnTestMeteor(Vector3.zero, seed: 5);
            var velField = typeof(Meteor).GetField("velocity",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            velField.SetValue(meteor, Vector2.zero);

            var bay = new Vector3(-5f, 0f, 0f);
            var (drone, env) = SpawnTestDroneWithEnv(bay, bay);

            var dropGo = new GameObject("TestFarDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            var drop = dropGo.GetComponent<CoreDrop>();
            drop.Spawn(new Vector3(5f, 0f, 0f), 5);
            GameManager.Instance.RegisterDrop(drop);

            float closestApproach = float.MaxValue;
            float timeout = 6f;
            while (timeout > 0f && !drop.IsClaimed)
            {
                closestApproach = Mathf.Min(closestApproach,
                    Vector3.Distance(drone.transform.position, meteor.transform.position));
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.Greater(closestApproach, 0.5f,
                "drone should skirt meteor outside its safety radius");
            TeardownScene();
        }
    }
}
