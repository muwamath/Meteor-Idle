using UnityEngine;

public interface ICollectorDroneEnvironment
{
    Vector3 BayPosition { get; }
    bool BayDoorsOpen { get; }

    void RequestOpenDoors();
    void RequestCloseDoors();

    CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance);

    void Deposit(int value);
}
