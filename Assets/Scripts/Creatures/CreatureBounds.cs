using UnityEngine;

/// <summary>Measures the model, not a manually authored size field or its effects.</summary>
public static class CreatureBounds
{
    public static bool BelongsTo(Transform child, Transform root)
    {
        for (Transform current = child; current != null && current != root; current = current.parent)
            if (current.GetComponent<Creature>() != null || current.GetComponent<SmoothFirstPersonController>() != null)
                return false;
        return child == root || child.IsChildOf(root);
    }

    public static bool TryMeasure(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer ||
                !BelongsTo(renderer.transform, root)) continue;
            Include(ref bounds, ref found, renderer.bounds);
        }
        // The player's procedural body is separate from the player hierarchy. Its capsule is authoritative.
        if (root.GetComponent<SmoothFirstPersonController>() != null || !found)
        {
            found = false;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || !BelongsTo(collider.transform, root)) continue;
                Include(ref bounds, ref found, collider.bounds);
            }
        }
        return found;
    }

    private static void Include(ref Bounds bounds, ref bool found, Bounds next)
    {
        if (found) bounds.Encapsulate(next);
        else bounds = next;
        found = true;
    }

    public static float LargestSide(Bounds bounds) => Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
}
