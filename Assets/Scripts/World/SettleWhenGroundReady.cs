using UnityEngine;

/// <summary>
/// Holds a freshly placed physics object still until the generated ground under it exists, then sets it down on
/// that ground and lets physics take over. Without it, something placed before its terrain chunk is built falls
/// straight through the world. Removes itself once settled.
/// </summary>
public sealed class SettleWhenGroundReady : MonoBehaviour
{
    [Tooltip("Height above the ground it is set down at.")]
    public float lift = 0.4f;

    private Rigidbody body;
    private bool wasKinematic;
    private float nextCheck;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body != null) wasKinematic = body.isKinematic;
    }

    private void Update()
    {
        // Kept still every frame (its own start-up code may switch physics back on).
        if (body != null) { if (!body.isKinematic) body.linearVelocity = Vector3.zero; body.isKinematic = true; }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        Vector3 from = transform.position + Vector3.up * 20f;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 60f, 1 << 7, QueryTriggerInteraction.Ignore)) return;
        if (hit.collider.transform.IsChildOf(transform)) return;
        transform.position = hit.point + Vector3.up * lift;
        if (body != null)
        {
            body.position = transform.position;
            body.isKinematic = wasKinematic;
        }
        Destroy(this);
    }
}
