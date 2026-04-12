using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    internal class MockEnvironment : ICollectorDroneEnvironment
    {
        public Vector3 BayPosition { get; set; } = Vector3.zero;
        public Vector3 CollectorPosition { get; set; } = new Vector3(0f, -3f, 0f);
        public bool BayDoorsOpen { get; set; }
        public float ReloadSpeed { get; set; } = 1f;
        public int DoorOpenRequests;
        public int DoorCloseRequests;
        public List<CoreDrop> Drops = new List<CoreDrop>();

        public void RequestOpenDoors() { DoorOpenRequests++; BayDoorsOpen = true; }
        public void RequestCloseDoors() { DoorCloseRequests++; BayDoorsOpen = false; }

        public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
        {
            CoreDrop best = null;
            float bestD = float.MaxValue;
            foreach (var d in Drops)
            {
                if (d == null || d.IsClaimed || !d.IsAlive) continue;
                float dist = Vector3.Distance(from, d.Position);
                if (dist > maxDistance) continue;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }
    }

    public class DroneStateMachineTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private CoreDrop MakeDrop(Vector3 pos, int value)
        {
            var go = new GameObject("TestDrop", typeof(SpriteRenderer), typeof(CoreDrop));
            _spawned.Add(go);
            var d = go.GetComponent<CoreDrop>();
            TestHelpers.InvokeAwake(d);
            d.Spawn(pos, value);
            return d;
        }

        private CollectorDrone MakeDrone(ICollectorDroneEnvironment env)
        {
            var go = new GameObject("TestDrone", typeof(CollectorDrone));
            _spawned.Add(go);
            var drone = go.GetComponent<CollectorDrone>();
            TestHelpers.InvokeAwake(drone);
            drone.Initialize(env,
                thrust: 4f, damping: 1f,
                batteryCapacity: 10f, cargoCapacity: 1,
                reserveThresholdFraction: 0.4f,
                pickupRadius: 0.3f, dockRadius: 0.4f);
            return drone;
        }

        [Test]
        public void StartsInIdleState()
        {
            var env = new MockEnvironment();
            var drone = MakeDrone(env);
            Assert.AreEqual(DroneState.Idle, drone.State);
        }

        [Test]
        public void Idle_WithBatteryFullAndDropAvailable_TransitionsToLaunching()
        {
            var env = new MockEnvironment();
            env.Drops.Add(MakeDrop(new Vector3(3f, 0f, 0f), 5));
            var drone = MakeDrone(env);

            drone.Tick(0.1f);

            Assert.AreEqual(DroneState.Launching, drone.State);
            Assert.AreEqual(1, env.DoorOpenRequests);
        }

        [Test]
        public void Idle_WithNoDrops_StaysIdle()
        {
            var env = new MockEnvironment();
            var drone = MakeDrone(env);
            drone.Tick(0.1f);
            Assert.AreEqual(DroneState.Idle, drone.State);
            Assert.AreEqual(0, env.DoorOpenRequests);
        }

        [Test]
        public void Launching_DoorsOpen_TransitionsToSeeking()
        {
            var env = new MockEnvironment();
            env.Drops.Add(MakeDrop(new Vector3(3f, 0f, 0f), 5));
            var drone = MakeDrone(env);
            drone.Tick(0.1f);

            env.BayDoorsOpen = true;
            drone.Tick(0.1f);

            Assert.AreEqual(DroneState.Seeking, drone.State);
            Assert.IsNotNull(drone.TargetDrop);
        }

        [Test]
        public void Seeking_ReachesDrop_TransitionsToPickup()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f);
            env.BayDoorsOpen = true;
            drone.Tick(0.1f);

            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f);

            Assert.AreEqual(DroneState.Pickup, drone.State);
        }

        [Test]
        public void Pickup_MovesDropIntoCargo_TransitionsToDelivering()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f);
            env.BayDoorsOpen = true;
            drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f);
            drone.Tick(0.05f);

            Assert.AreEqual(DroneState.Delivering, drone.State);
            Assert.AreEqual(1, drone.CargoCount);
            Assert.IsFalse(drop.IsAlive, "drop consumed on pickup");
        }

        [Test]
        public void Delivering_ReachesCollector_TransitionsToDepositing()
        {
            var env = new MockEnvironment();
            env.CollectorPosition = new Vector3(0f, -3f, 0f);
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            // Get to Delivering state
            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Delivering, drone.State);

            // Move drone to collector
            drone.transform.position = env.CollectorPosition;
            drone.Body.Position = env.CollectorPosition;
            drone.Tick(0.05f);

            Assert.AreEqual(DroneState.Depositing, drone.State);
        }

        [Test]
        public void Depositing_ClearsCargo_TransitionsToReturning_WhenNoDrop()
        {
            var env = new MockEnvironment();
            env.CollectorPosition = new Vector3(0f, -3f, 0f);
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            // Get to Depositing state
            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.CollectorPosition;
            drone.Body.Position = env.CollectorPosition;
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Depositing, drone.State);

            // Tick Depositing — no more drops, should go Returning
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Returning, drone.State);
            Assert.AreEqual(0, drone.CargoCount);
        }

        [Test]
        public void Returning_ReachesBay_TransitionsToDocking_OpensDoors()
        {
            var env = new MockEnvironment();
            env.BayPosition = Vector3.zero;
            env.CollectorPosition = new Vector3(0f, -3f, 0f);
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            // Get to Returning
            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.CollectorPosition;
            drone.Body.Position = env.CollectorPosition;
            drone.Tick(0.05f); drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Returning, drone.State);

            // Move to bay
            drone.transform.position = env.BayPosition;
            drone.Body.Position = env.BayPosition;
            env.BayDoorsOpen = false;
            int openBefore = env.DoorOpenRequests;
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Docking, drone.State);
            Assert.AreEqual(openBefore + 1, env.DoorOpenRequests);
        }

        [Test]
        public void Docking_DoorsOpen_SnapsToPosition_ReturnsToIdle()
        {
            var env = new MockEnvironment();
            env.BayPosition = new Vector3(1f, -5f, 0f);
            env.CollectorPosition = new Vector3(0f, -3f, 0f);
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            // Get to Docking
            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.CollectorPosition;
            drone.Body.Position = env.CollectorPosition;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.BayPosition;
            drone.Body.Position = env.BayPosition;
            env.BayDoorsOpen = false;
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Docking, drone.State);

            // Doors open → snap + idle
            env.BayDoorsOpen = true;
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Idle, drone.State);
            Assert.AreEqual(env.BayPosition, drone.transform.position, "drone snapped to bay");
            Assert.AreEqual(Vector2.zero, drone.Body.Velocity, "velocity zeroed on dock");
        }

        [Test]
        public void Seeking_BatteryBelowReserve_TransitionsToReturning()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(20f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = new Vector3(5f, 0f, 0f);
            drone.Body.Position = new Vector2(5f, 0f);
            for (int i = 0; i < 70; i++)
            {
                drone.Tick(0.1f);
                if (drone.State == DroneState.Returning) break;
            }
            Assert.AreEqual(DroneState.Returning, drone.State);
        }

        [Test]
        public void Idle_ReloadSpeed2x_RechargesTwiceAsFast()
        {
            var env = new MockEnvironment { ReloadSpeed = 2f };
            var drone = MakeDrone(env);
            // Drain battery so it's not full
            typeof(CollectorDrone).GetField("battery",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(drone, 5f);
            float before = drone.Battery;
            drone.Tick(1f);
            float gained = drone.Battery - before;
            // With ReloadSpeed=2, should gain 2.0 per second, not 1.0
            Assert.AreEqual(2f, gained, 0.01f, "ReloadSpeed multiplies recharge rate");
        }

        [Test]
        public void LimpHome_TriggeredWhenBatteryHitsZero_DuringReturning()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(20f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = new Vector3(5f, 0f, 0f);
            drone.Body.Position = new Vector2(5f, 0f);
            for (int i = 0; i < 200; i++) drone.Tick(0.1f);
            Assert.IsTrue(drone.Body.LimpHomeMode, "limp-home engages at battery 0");
            Assert.AreEqual(DroneState.Returning, drone.State, "still returning, just slower");
        }
    }
}
