using UnityEngine;

/// <summary>
/// Keeps the first-person camera out of walls, ceilings and ledges.
/// Every frame, after the controller has moved the camera holder, a small sphere is cast from a safe
/// point inside the player's collider to the camera. If it hits something, the camera is pulled back
/// in front of the hit, then eases out again once the way is clear. Also sets a short near clip plane,
/// since the default 0.3 m reaches almost to the edge of the player's capsule.
///
/// Put it on the Player object (the one with SmoothFirstPersonController).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1150)] // after HandClimber (1050), before FirstPersonBody (1200) poses the arms
public sealed class CameraWallGuard : MonoBehaviour
{
    [Header("References (found automatically when empty)")]
    [SerializeField] private Camera viewCamera;
    [Tooltip("The object the controller moves to the eye point every frame (CameraHolder).")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private Collider bodyCollider;

    [Header("Probe")]
    [Tooltip("Smallest gap kept between the camera and any surface.")]
    [Min(0.02f)] [SerializeField] private float probeRadius = 0.18f;
    [Tooltip("Height of the safe point above the collider centre.")]
    [SerializeField] private float anchorHeight = 0.2f;
    [SerializeField] private LayerMask blockingLayers = (1 << 0) | (1 << 7) | (1 << 8);
    [Tooltip("How fast the camera returns to the eye point once the way is clear (metres per second).")]
    [Min(0.1f)] [SerializeField] private float releaseSpeed = 1.5f;

    [Header("Camera")]
    [Min(0.01f)] [SerializeField] private float nearClipPlane = 0.05f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private float pulledBack;

    private readonly RaycastHit[] hits = new RaycastHit[16];

    private void Awake()
    {
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
        if (viewCamera == null && transform.parent != null) viewCamera = transform.parent.GetComponentInChildren<Camera>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (cameraHolder == null && transform.parent != null) cameraHolder = transform.parent.Find("CameraHolder");
        if (cameraHolder == null && viewCamera != null) cameraHolder = viewCamera.transform.parent;
        if (viewCamera != null) viewCamera.nearClipPlane = nearClipPlane;
    }

    private void LateUpdate()
    {
        if (cameraHolder == null || bodyCollider == null) return;

        Vector3 anchor = bodyCollider.bounds.center + Vector3.up * anchorHeight;
        Vector3 eye = cameraHolder.position;
        Vector3 toEye = eye - anchor;
        float distance = toEye.magnitude;
        if (distance < 1e-4f) return;
        Vector3 direction = toEye / distance;

        // Nearest blocking hit between the safe point and the eye.
        float allowed = distance;
        int count = Physics.SphereCastNonAlloc(anchor, probeRadius, direction, hits, distance, blockingLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider other = hits[i].collider;
            if (IsIgnored(other)) continue;
            // A hit at distance 0 means the probe already started inside something; keep the camera at the anchor.
            allowed = Mathf.Min(allowed, hits[i].distance);
        }

        float wanted = distance - allowed;
        // Pull in at once so the wall never shows through; let go slowly so the view does not pop.
        pulledBack = wanted > pulledBack ? wanted : Mathf.MoveTowards(pulledBack, wanted, releaseSpeed * Time.deltaTime);
        if (pulledBack > 1e-4f) cameraHolder.position = eye - direction * pulledBack;
    }

    /// <summary>The player itself, creatures, pickable things and loose moving bodies never push the camera.</summary>
    private bool IsIgnored(Collider other)
    {
        if (other.transform.IsChildOf(transform.parent != null ? transform.parent : transform)) return true;
        if (other.GetComponentInParent<Creature>() != null) return true;
        Rigidbody attached = other.attachedRigidbody;
        return attached != null && !attached.isKinematic;
    }

    private void OnValidate()
    {
        if (viewCamera != null) viewCamera.nearClipPlane = nearClipPlane;
    }
}
