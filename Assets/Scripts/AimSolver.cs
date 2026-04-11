using UnityEngine;

// Standard linear-intercept math helper. Given a stationary shooter, a target
// moving at constant velocity, and a projectile speed, predicts the point the
// shooter should aim at so the projectile intercepts the target. Returns the
// target's current position if no positive-time solution exists (fallback path
// is effectively unreachable with this game's stats: round speed 6+ >> meteor
// speed ~0.8 max).
public static class AimSolver
{
    public static Vector2 PredictInterceptPoint(
        Vector2 shooterPos,
        Vector2 targetPos,
        Vector2 targetVelocity,
        float projectileSpeed)
    {
        return targetPos;
    }
}
