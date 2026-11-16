using UnityEngine;

/// <summary>A scene marker for one meditation seat on the world route.</summary>
public sealed class WorldMapRestSite : MonoBehaviour
{
    public string Kind = "Seat";

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 0.95f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, 0.6f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.3f, transform.forward * 2f);
    }
}
