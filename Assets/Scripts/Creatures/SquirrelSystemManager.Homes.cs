using System.Collections.Generic;
using UnityEngine;

public sealed partial class SquirrelSystemManager
{
    [Header("Spread homes across the forest")]
    [SerializeField] private bool spreadNestLocations;
    [SerializeField, Min(1f)] private float minimumNestSeparation = 28f;

    private bool TryPlanSpreadHomes(int count, out List<Vector3> homes)
    {
        homes = new List<Vector3>();
        Vector3 center = Vector3.zero;
        foreach (var tree in trees) center += tree.GroundPosition;
        center /= trees.Count;
        var candidates = new List<Vector3>();
        foreach (var tree in trees)
        {
            Vector3 inward = Vector3.ProjectOnPlane(center - tree.GroundPosition, Vector3.up).normalized;
            if (inward.sqrMagnitude < 0.01f) inward = Vector3.forward;
            Vector3 point = tree.GroundPosition + inward * nestTreeDistance;
            if (gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * 100f, Vector3.down,
                    out var hit, 200f, 1 << 7, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.7f)
                candidates.Add(hit.point);
        }
        // Farthest-point sampling covers the whole habitat instead of choosing adjacent trees.
        while (homes.Count < count)
        {
            int best = -1; float bestDistance = -1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                float distance = homes.Count == 0 ? FlatDistanceSquared(candidates[i], center) : float.PositiveInfinity;
                foreach (var home in homes) distance = Mathf.Min(distance, FlatDistanceSquared(candidates[i], home));
                if (homes.Count > 0 && distance < minimumNestSeparation * minimumNestSeparation) continue;
                if (distance > bestDistance) { best = i; bestDistance = distance; }
            }
            if (best < 0) return false;
            homes.Add(candidates[best]); candidates.RemoveAt(best);
        }
        return true;
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).sqrMagnitude;

    public bool IsNestLocationAvailable(Vector3 point, SquirrelNest movingNest)
    {
        if (!spreadNestLocations) return true;
        foreach (var home in nests)
            if (home != null && home != movingNest && !home.IsDead && home.gameObject.scene == gameObject.scene &&
                FlatDistanceSquared(point, home.transform.position) < minimumNestSeparation * minimumNestSeparation) return false;
        foreach (var home in SquirrelNest.All)
            if (home != null && home != movingNest && !home.IsDead && home.gameObject.scene == gameObject.scene &&
                FlatDistanceSquared(point, home.transform.position) < minimumNestSeparation * minimumNestSeparation) return false;
        return true;
    }
}
