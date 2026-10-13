using UnityEngine;

/// <summary>
/// Trigger volume with a slow updraft: the player falls gently through it instead of dropping hard.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShaftDraft : MonoBehaviour
{
    [Tooltip("Fastest the player may fall inside, m/s.")]
    [Min(0.5f)] public float maxFallSpeed = 7f;
    [Tooltip("How quickly sideways speed fades inside.")]
    [Min(0f)] public float sideDrag = 0.6f;
    [Tooltip("Upward speed the draft lifts the player to, m/s (0 = it only slows falls).")]
    [Min(0f)] public float lift;

    private void OnTriggerStay(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null || body.isKinematic || body.GetComponent<SmoothFirstPersonController>() == null) return;
        Vector3 velocity = body.linearVelocity;
        if (velocity.y < -maxFallSpeed) velocity.y = Mathf.MoveTowards(velocity.y, -maxFallSpeed, 40f * Time.fixedDeltaTime);
        // A strong draft carries the player up (gravity is cancelled on top of the push).
        if (lift > 0.01f && velocity.y < lift)
            velocity.y = Mathf.MoveTowards(velocity.y, lift, (12f + Physics.gravity.magnitude) * Time.fixedDeltaTime);
        float keep = Mathf.Exp(-sideDrag * Time.fixedDeltaTime);
        velocity.x *= keep;
        velocity.z *= keep;
        body.linearVelocity = velocity;
    }
}
