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
        Vector2 d = targetPos - shooterPos;
        float a = Vector2.Dot(targetVelocity, targetVelocity) - projectileSpeed * projectileSpeed;
        float b = 2f * Vector2.Dot(d, targetVelocity);
        float c = Vector2.Dot(d, d);

        float t;
        const float eps = 1e-6f;
        if (Mathf.Abs(a) < eps)
        {
            // Degenerate: target moving at exactly projectile speed. Linear solve.
            if (b >= -eps) return targetPos; // opening trajectory, no intercept
            t = -c / b;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return targetPos;
            float sqrt = Mathf.Sqrt(disc);
            float t1 = (-b - sqrt) / (2f * a);
            float t2 = (-b + sqrt) / (2f * a);
            // Pick smallest positive root. Both can be positive when a < 0
            // (projectile faster than target); t1 is usually smaller.
            if (t1 > 0f && t2 > 0f) t = Mathf.Min(t1, t2);
            else if (t1 > 0f)       t = t1;
            else if (t2 > 0f)       t = t2;
            else                    return targetPos;
        }

        return targetPos + targetVelocity * t;
    }
}
