using UnityEngine;

/// <summary>
/// The shared wind rule. Every consumer (player, creature motors, passive bodies, thrown flights) reads wind
/// through these two functions so one area behaves the same for all of them.
///
/// Offset form (things with their own locomotion): a wind velocity eased toward
/// sum(area speed x direction x falloff) x (1 - resistance), added on top of the body's own velocity.
/// Moving against the wind, the result along the wind axis is ownSpeedAgainst - windSpeed, so a wall stops
/// walking and lets anything faster through; the part across the wind is untouched.
///
/// Passive form (no own locomotion): velocity along each area's direction is pulled up to that area's speed.
/// </summary>
public static class WindField
{
    /// <summary>Release rate used once a body has never been in wind (1/s).</summary>
    public const float DefaultGrip = 6f;

    /// <summary>Wind target at a point for a body with this resistance. Grip is the strongest containing area's.</summary>
    public static Vector3 Target(Vector3 position, float resistance, out float grip, out bool inside)
    {
        grip = 0f;
        inside = false;
        Vector3 sum = Vector3.zero;
        float carried = 1f - Mathf.Clamp01(resistance);
        var areas = WindArea.Active;
        for (int i = 0; i < areas.Count; i++)
        {
            WindArea area = areas[i];
            float weight = area.Weight(position);
            if (weight <= 0f) continue;
            inside = true;
            sum += area.Direction * (area.Speed * weight * carried);
            if (area.Grip > grip) grip = area.Grip;
        }
        return sum;
    }

    /// <summary>
    /// Advances a wind offset by one step. The offset eases toward the target inside wind and back to zero
    /// outside it, at the grip of the last area the body was in, so leaving a gust keeps a little drift.
    /// </summary>
    public static Vector3 StepOffset(Vector3 offset, Vector3 position, float resistance, ref float grip, float dt,
        out bool inside)
    {
        Vector3 target = Target(position, resistance, out float areaGrip, out inside);
        if (inside) grip = areaGrip;
        float rate = grip > 0f ? grip : DefaultGrip;
        Vector3 next = Vector3.Lerp(offset, target, 1f - Mathf.Exp(-rate * dt));
        return !inside && next.sqrMagnitude < 1e-4f ? Vector3.zero : next;
    }

    /// <summary>
    /// Pulls a passive velocity toward each containing area's speed along its direction. Never brakes a body
    /// already moving with the wind faster than the wind, and never touches the across-wind part.
    /// </summary>
    public static Vector3 PullPassive(Vector3 velocity, Vector3 position, float resistance, float dt)
    {
        float carried = 1f - Mathf.Clamp01(resistance);
        if (carried <= 0f) return velocity;
        var areas = WindArea.Active;
        for (int i = 0; i < areas.Count; i++)
        {
            WindArea area = areas[i];
            float weight = area.Weight(position);
            if (weight <= 0f) continue;
            Vector3 direction = area.Direction;
            float target = area.Speed * weight * carried;
            float along = Vector3.Dot(velocity, direction);
            if (along >= target) continue;
            velocity += direction * ((target - along) * (1f - Mathf.Exp(-area.Grip * dt)));
        }
        return velocity;
    }

    /// <summary>Whether any active area covers this point (cheap test before doing work).</summary>
    public static bool Inside(Vector3 position)
    {
        var areas = WindArea.Active;
        for (int i = 0; i < areas.Count; i++)
            if (areas[i].Weight(position) > 0f) return true;
        return false;
    }

    public static float ResistanceOf(Component component)
    {
        if (component == null) return 0f;
        var resistant = component.GetComponentInParent<IWindResistant>();
        return resistant != null ? Mathf.Clamp01(resistant.WindResistance) : 0f;
    }
}
