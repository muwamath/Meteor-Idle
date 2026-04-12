using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneBayDoorsTests
    {
        private DroneBay MakeBay()
        {
            var go = new GameObject("TestBay", typeof(DroneBay));
            var leftGo = new GameObject("LeftDoor", typeof(SpriteRenderer));
            leftGo.transform.SetParent(go.transform, false);
            var rightGo = new GameObject("RightDoor", typeof(SpriteRenderer));
            rightGo.transform.SetParent(go.transform, false);
            var bay = go.GetComponent<DroneBay>();
            var so = new UnityEditor.SerializedObject(bay);
            so.FindProperty("leftDoor").objectReferenceValue = leftGo.transform;
            so.FindProperty("rightDoor").objectReferenceValue = rightGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            TestHelpers.InvokeAwake(bay);
            return bay;
        }

        [Test]
        public void StartsInClosedState_DoorsFlat()
        {
            var bay = MakeBay();
            Assert.AreEqual(DroneBay.DoorState.Closed, bay.Doors);
            Assert.IsFalse(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void RequestOpenDoors_SteppedThrough4Keyframes()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            Assert.AreEqual(DroneBay.DoorState.Opening, bay.Doors);

            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            Assert.AreEqual(DroneBay.DoorState.Open, bay.Doors);
            Assert.IsTrue(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void RequestCloseDoors_FromOpen_SteppedThroughClosingBackToClosed()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            bay.RequestCloseDoors();
            Assert.AreEqual(DroneBay.DoorState.Closing, bay.Doors);
            for (int i = 0; i < 20; i++) bay.Tick(0.05f);
            Assert.AreEqual(DroneBay.DoorState.Closed, bay.Doors);
            Assert.IsFalse(bay.IsOpen);
            Object.DestroyImmediate(bay.gameObject);
        }

        [Test]
        public void DoorKeyframe_RotationsAreQuantized_NoLerp()
        {
            var bay = MakeBay();
            bay.RequestOpenDoors();
            var seenLeft = new System.Collections.Generic.HashSet<float>();
            for (int i = 0; i < 40; i++)
            {
                bay.Tick(0.025f);
                seenLeft.Add(Mathf.Round(bay.LeftDoorLocalRotationZ));
            }
            foreach (var z in seenLeft)
                Assert.IsTrue(z == 0f || z == 45f || z == 90f,
                    $"left door rotation {z} is not one of the 4 keyframes (0/45/90)");
            Object.DestroyImmediate(bay.gameObject);
        }
    }
}
