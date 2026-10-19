using UnityEngine;

/// <summary>Marks a tree trunk as a nest and food spawning anchor.</summary>
[DisallowMultipleComponent]
public sealed class SquirrelTree : MonoBehaviour
{
    public Vector3 GroundPosition
    {
        get
        {
            Vector3 point = transform.position;
            var trunk = GetComponent<Collider>();
            if (trunk != null) point.y = trunk.bounds.min.y;
            return point;
        }
    }

    public Vector3 SampleGround(float minimumRadius, float maximumRadius)
    {
        Vector3 center = GroundPosition;
        Vector3 result = center;
        for (int i = 0; i < 24; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Random.Range(minimumRadius * minimumRadius, maximumRadius * maximumRadius));
            result = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            // The Ariia floor uses Ground layer 7. Reject points beyond the floor edge.
            if (Physics.Raycast(result + Vector3.up * 100f, Vector3.down, out var hit, 200f, 1 << 7))
                return hit.point;
        }
        // Supports scenes without a Ground-layer collider while keeping the point near this tree.
        return center;
    }

    public static void PlaceOnGround(Transform item, Vector3 ground)
    {
        var renderers = item.GetComponentsInChildren<Renderer>();
        float bottom = item.position.y;
        if (renderers.Length > 0)
        {
            bottom = renderers[0].bounds.min.y;
            foreach (var renderer in renderers) bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        }
        item.position = ground + Vector3.up * (item.position.y - bottom);
    }
}
