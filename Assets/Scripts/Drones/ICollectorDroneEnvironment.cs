using UnityEngine;

public interface ICollectorDroneEnvironment
{
    Vector3 BayPosition { get; }
    Vector3 CollectorPosition { get; }
    bool BayDoorsOpen { get; }

    void RequestOpenDoors();
    void RequestCloseDoors();

    CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance);
}
