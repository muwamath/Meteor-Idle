using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    internal class MockEnvironment : ICollectorDroneEnvironment
    {
        public Vector3 BayPosition { get; set; } = Vector3.zero;
        public bool BayDoorsOpen { get; set; }
        public int DoorOpenRequests;
        public int DoorCloseRequests;
        public int TotalDeposited;
        public List<CoreDrop> Drops = new List<CoreDrop>();

        public void RequestOpenDoors() { DoorOpenRequests++; BayDoorsOpen = true; }
        public void RequestCloseDoors() { DoorCloseRequests++; BayDoorsOpen = false; }
        public void Deposit(int value) { TotalDeposited += value; }

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
        public void Pickup_MovesDropIntoCargo_Cargo1ReturnsImmediately()
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

            Assert.AreEqual(DroneState.Returning, drone.State);
            Assert.AreEqual(1, drone.CargoCount);
            Assert.IsFalse(drop.IsAlive, "drop consumed on pickup");
        }

        [Test]
        public void Returning_ReachesBay_TransitionsToDocking_OpensDoors()
        {
            var env = new MockEnvironment();
            env.BayPosition = Vector3.zero;
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.BayPosition;
            drone.Body.Position = env.BayPosition;
            env.BayDoorsOpen = false;
            int openBefore = env.DoorOpenRequests;
            drone.Tick(0.05f);
            Assert.AreEqual(DroneState.Docking, drone.State);
            Assert.AreEqual(openBefore + 1, env.DoorOpenRequests);
        }

        [Test]
        public void Docking_DoorsOpen_TransitionsToDepositing_PaysCargo()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.BayPosition;
            drone.Body.Position = env.BayPosition;
            env.BayDoorsOpen = false;
            drone.Tick(0.05f);
            env.BayDoorsOpen = true;
            drone.Tick(0.05f);

            Assert.AreEqual(DroneState.Depositing, drone.State);
            Assert.AreEqual(5, env.TotalDeposited);
            Assert.AreEqual(0, drone.CargoCount);
        }

        [Test]
        public void Depositing_Completes_ReturnsToIdle_RequestsCloseDoors()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(0.1f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            drone.transform.position = drop.Position;
            drone.Body.Position = drop.Position;
            drone.Tick(0.05f); drone.Tick(0.05f);
            drone.transform.position = env.BayPosition;
            drone.Body.Position = env.BayPosition;
            env.BayDoorsOpen = false;
            drone.Tick(0.05f);
            env.BayDoorsOpen = true;
            drone.Tick(0.05f);
            int closeBefore = env.DoorCloseRequests;
            drone.Tick(0.05f);

            Assert.AreEqual(DroneState.Idle, drone.State);
            Assert.AreEqual(closeBefore + 1, env.DoorCloseRequests);
        }

        [Test]
        public void Seeking_BatteryBelowReserve_TransitionsToReturning_EvenWithClaimedDrop()
        {
            var env = new MockEnvironment();
            var drop = MakeDrop(new Vector3(20f, 0f, 0f), 5);
            env.Drops.Add(drop);
            var drone = MakeDrone(env);

            drone.Tick(0.1f); env.BayDoorsOpen = true; drone.Tick(0.1f);
            // Park drone away from bay so Return can't immediately dock (no physics in Phase 3).
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
