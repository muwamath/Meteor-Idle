using UnityEngine;

public class DroneBody
{
    public Vector2 Position;
    public Vector2 Velocity;
    public Vector2 DesiredThrust;
    public float ThrustCap;
    public float DampingPerSec;
    public bool LimpHomeMode;

    public DroneBody(Vector2 position, float thrustCap, float dampingPerSec)
    {
        Position = position;
        Velocity = Vector2.zero;
        DesiredThrust = Vector2.zero;
        ThrustCap = thrustCap;
        DampingPerSec = dampingPerSec;
        LimpHomeMode = false;
    }

    public void Integrate(float dt)
    {
        Position += Velocity * dt;

        float effectiveCap = LimpHomeMode ? ThrustCap * 0.25f : ThrustCap;
        float mag = DesiredThrust.magnitude;
        if (mag > 0f)
        {
            Vector2 desired = (mag > 1f)
                ? (DesiredThrust / mag) * effectiveCap
                : DesiredThrust * effectiveCap;
            Velocity = Vector2.MoveTowards(Velocity, desired, effectiveCap * dt);
        }

        Velocity *= Mathf.Exp(-DampingPerSec * dt);
    }

    public void ApplyPushKick(Vector2 deltaVelocity)
    {
        Velocity += deltaVelocity;
    }

    public void ApplyAvoidance(Vector2 obstaclePosition, float obstacleRadius, float safetyMargin)
    {
        Vector2 away = Position - obstaclePosition;
        float dist = away.magnitude;
        float safety = obstacleRadius + safetyMargin;
        if (dist >= safety || dist < 0.0001f) return;
        float t = 1f - (dist / safety);
        float intensity = t * t * 3f;
        DesiredThrust += (away / dist) * intensity;
    }
}
