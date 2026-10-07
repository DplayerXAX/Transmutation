using UnityEngine;

/// <summary>
/// Makes a music layer audible when the listener gets close to this object or zone.
/// With a collider assigned, distance is measured to the collider, so 0 inside a zone.
/// Creatures don't need this; AudioManager maps them by type from the event bank.
/// </summary>
public class MusicChannel : MonoBehaviour
{
    public string layerEvent = "BGM_fuzzy";
    [Min(0f)] public float innerRadius = 4f;
    [Min(0.1f)] public float outerRadius = 20f;
    [Tooltip("Optional zone shape. Use a convex or primitive collider.")]
    public Collider zone;

    protected virtual void OnEnable() => AudioManager.RegisterChannel(this);
    protected virtual void OnDisable() => AudioManager.UnregisterChannel(this);

    /// <summary>How loud this channel's layer should be for a listener at this point (0..1).</summary>
    public virtual float Weight(Vector3 listener) =>
        AudioManager.Falloff(DistanceTo(listener), innerRadius, outerRadius);

    public float DistanceTo(Vector3 point)
    {
        if (zone != null && zone.enabled)
            return Vector3.Distance(point, zone.ClosestPoint(point));
        return Vector3.Distance(point, transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, innerRadius);
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, outerRadius);
    }
}
