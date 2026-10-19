using UnityEngine;
using UnityEngine.SceneManagement;

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

    public Vector3 SampleGround(float minimumRadius, float maximumRadius) =>
        TrySampleGround(minimumRadius, maximumRadius, out var point) ? point : GroundPosition;

    public bool TrySampleGround(float minimumRadius, float maximumRadius, out Vector3 result)
    {
        Vector3 center = GroundPosition;
        result = center;
        for (int i = 0; i < 24; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Random.Range(minimumRadius * minimumRadius, maximumRadius * maximumRadius));
            result = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            // The Ariia floor uses Ground layer 7. Reject points beyond the floor edge.
            if (gameObject.scene.GetPhysicsScene().Raycast(result + Vector3.up * 100f, Vector3.down,
                    out var hit, 200f, 1 << 7, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.7f)
            {
                result = hit.point;
                return true;
            }
        }
        return false;
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
        Vector3 position = ground + Vector3.up * (item.position.y - bottom);
        item.position = position;
        // Interpolated dynamic bodies retain a separate physics pose. Teleport both poses
        // before the first simulation step, otherwise interpolation restores the prefab position.
        if (item.TryGetComponent<Rigidbody>(out var body))
        {
            body.position = position;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }
    }
}
