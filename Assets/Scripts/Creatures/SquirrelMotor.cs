using UnityEngine;

/// <summary>Owns free squirrel locomotion. Player carrying and capture retain movement ownership.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SquirrelMotor : MonoBehaviour
{
    [SerializeField, Min(1f)] private float acceleration = 18f;
    [SerializeField, Min(1f)] private float turnSpeed = 360f;
    [SerializeField] private LayerMask groundLayers = 1 << 7;
    private Rigidbody body;
    private Squirrel squirrel;
    private Vector3 desiredVelocity;
    private float hopRequested;
    private float nextHop;
    public float BodyRadius => GetComponent<Collider>() is Collider collider
        ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z) : 0.5f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void SetIntent(Vector3 velocity, float hopSpeed = 0f)
    {
        desiredVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        hopRequested = Mathf.Max(hopRequested, hopSpeed);
    }

    public void Stop() { desiredVelocity = Vector3.zero; hopRequested = 0f; }

    private void FixedUpdate()
    {
        if (squirrel == null) squirrel = GetComponent<Squirrel>();
        if (squirrel == null || !squirrel.CanMoveAutonomously || body.isKinematic)
        {
            Stop();
            return;
        }
        Vector3 velocity = desiredVelocity;
        if (!squirrel.ContainsInTerritory(body.position))
            velocity = Vector3.ProjectOnPlane(squirrel.TerritoryCenter - body.position, Vector3.up).normalized *
                Mathf.Max(2f, desiredVelocity.magnitude);
        else if (!squirrel.ContainsInTerritory(body.position + velocity * Time.fixedDeltaTime))
            velocity = Vector3.zero;

        Collider shape = GetComponent<Collider>();
        float footY = shape.bounds.min.y;
        bool grounded = Physics.Raycast(new Vector3(body.position.x, footY + 0.15f, body.position.z),
            Vector3.down, 0.3f, groundLayers, QueryTriggerInteraction.Ignore);
        // Avoid walking off the test floor while still allowing actual falls after external pushes.
        if (grounded && velocity.sqrMagnitude > 0.01f)
        {
            Vector3 ahead = body.position + velocity.normalized * (BodyRadius + 0.35f);
            ahead.y = footY + 0.5f;
            if (!Physics.Raycast(ahead, Vector3.down, 1.2f, groundLayers, QueryTriggerInteraction.Ignore))
                velocity = Vector3.zero;
            else if (body.SweepTest(velocity.normalized, out var hit, BodyRadius + 0.4f,
                QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.5f &&
                hit.collider.GetComponentInParent<Squirrel>() == null)
            {
                // Slide around solid trunks and rocks instead of passing through them.
                Vector3 tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
                if (Vector3.Dot(tangent, velocity) < -0.01f) tangent = -tangent;
                velocity = tangent * velocity.magnitude;
                if (!squirrel.ContainsInTerritory(body.position + velocity * Time.fixedDeltaTime)) velocity = Vector3.zero;
            }
        }
        Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        horizontal = Vector3.MoveTowards(horizontal, velocity, acceleration * Time.fixedDeltaTime);
        float vertical = body.linearVelocity.y;
        if (hopRequested > 0f && grounded && Time.time >= nextHop)
        {
            vertical = hopRequested;
            nextHop = Time.time + 0.35f;
        }
        hopRequested = 0f;
        body.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
        if (velocity.sqrMagnitude > 0.01f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(velocity),
                turnSpeed * Time.fixedDeltaTime));
    }

    private void OnDisable() => Stop();
}
