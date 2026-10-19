using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns free squirrel locomotion. Player carrying and capture retain movement ownership.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SquirrelMotor : MonoBehaviour
{
    [SerializeField, Min(1f)] private float acceleration = 18f;
    [SerializeField, Min(1f)] private float turnSpeed = 360f;
    [SerializeField] private LayerMask groundLayers = 1 << 7;
    private Rigidbody body;
    private Collider bodyCollider;
    private PhysicsMaterial originalMaterial;
    private PhysicsMaterial locomotionMaterial;
    private Squirrel squirrel;
    private Vector3 desiredVelocity;
    private Collider destinationCollider;
    private Vector3 bypassPoint;
    private bool hasBypassPoint;
    private float hopRequested;
    private float nextHop;
    public float BodyRadius => GetComponent<Collider>() is Collider collider
        ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z) : 0.5f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        if (bodyCollider == null) bodyCollider = gameObject.AddComponent<BoxCollider>();
        // The motor controls horizontal acceleration and braking. Default contact friction
        // can cancel that acceleration entirely under this project's strong gravity (-30).
        originalMaterial = bodyCollider.sharedMaterial;
        locomotionMaterial = originalMaterial != null ? Instantiate(originalMaterial) : new PhysicsMaterial();
        locomotionMaterial.name = "Squirrel Locomotion";
        locomotionMaterial.staticFriction = 0f;
        locomotionMaterial.dynamicFriction = 0f;
        locomotionMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
        bodyCollider.sharedMaterial = locomotionMaterial;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void SetIntent(Vector3 velocity, float hopSpeed = 0f, Collider destination = null)
    {
        desiredVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (destinationCollider != destination) hasBypassPoint = false;
        destinationCollider = destination;
        hopRequested = Mathf.Max(hopRequested, hopSpeed);
    }

    public void Stop() { desiredVelocity = Vector3.zero; hopRequested = 0f; }

    public void ResetRoute()
    {
        Stop();
        hasBypassPoint = false;
        destinationCollider = null;
    }

    private void FixedUpdate()
    {
        if (squirrel == null) squirrel = GetComponent<Squirrel>();
        if (squirrel == null || !squirrel.CanMoveAutonomously || body.isKinematic)
        {
            Stop();
            return;
        }
        Vector3 velocity = desiredVelocity;
        if (hasBypassPoint && velocity.sqrMagnitude > 0.01f)
        {
            Vector3 offset = Vector3.ProjectOnPlane(bypassPoint - body.position, Vector3.up);
            if (offset.sqrMagnitude < 0.09f) hasBypassPoint = false;
            else velocity = offset.normalized * velocity.magnitude;
        }
        if (!squirrel.AllowsMovementAt(body.position))
            velocity = Vector3.ProjectOnPlane(squirrel.TerritoryCenter - body.position, Vector3.up).normalized *
                Mathf.Max(2f, desiredVelocity.magnitude);
        else if (!squirrel.AllowsMovementAt(body.position + velocity * Time.fixedDeltaTime))
            velocity = Vector3.zero;

        Collider shape = GetComponent<Collider>();
        float footY = shape.bounds.min.y;
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();
        bool grounded = physics.Raycast(new Vector3(body.position.x, footY + 0.15f, body.position.z),
            Vector3.down, 0.3f, groundLayers, QueryTriggerInteraction.Ignore);
        // Avoid walking off the test floor while still allowing actual falls after external pushes.
        if (grounded && velocity.sqrMagnitude > 0.01f)
        {
            Vector3 ahead = body.position + velocity.normalized * (BodyRadius + 0.35f);
            ahead.y = footY + 0.5f;
            if (!physics.Raycast(ahead, Vector3.down, 1.2f, groundLayers, QueryTriggerInteraction.Ignore))
                velocity = Vector3.zero;
            else if (body.SweepTest(velocity.normalized, out var hit, BodyRadius + 0.4f,
                QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.5f &&
                hit.collider != destinationCollider && hit.collider.GetComponentInParent<Squirrel>() == null)
            {
                // Keep a concrete waypoint around the obstacle instead of picking a
                // tangent anew each step (which oscillates at corners and tree trunks).
                if (!hasBypassPoint && destinationCollider != null)
                    ChooseBypass(hit.collider, destinationCollider.bounds.center);
                Vector3 tangent = hasBypassPoint
                    ? Vector3.ProjectOnPlane(bypassPoint - body.position, Vector3.up).normalized
                    : Vector3.Cross(Vector3.up, hit.normal).normalized;
                if (!hasBypassPoint && Vector3.Dot(tangent, velocity) < -0.01f) tangent = -tangent;
                velocity = tangent * velocity.magnitude;
                if (!squirrel.AllowsMovementAt(body.position + velocity * Time.fixedDeltaTime)) velocity = Vector3.zero;
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

    private void ChooseBypass(Collider obstacle, Vector3 goal)
    {
        Bounds bounds = obstacle.bounds;
        bounds.Expand(new Vector3((BodyRadius + 0.3f) * 2f, 0f, (BodyRadius + 0.3f) * 2f));
        float best = float.PositiveInfinity;
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 point = new Vector3(x == 0 ? bounds.min.x : bounds.max.x, body.position.y,
                z == 0 ? bounds.min.z : bounds.max.z);
            if (!squirrel.AllowsMovementAt(point)) continue;
            Vector3 offset = point - body.position;
            float distance = offset.magnitude;
            if (distance < 0.3f) continue;
            bool blocked = false;
            foreach (RaycastHit hit in body.SweepTestAll(offset / distance, Mathf.Max(0f, distance - 0.1f),
                         QueryTriggerInteraction.Ignore))
                if (Mathf.Abs(hit.normal.y) < 0.5f && hit.collider != destinationCollider &&
                    hit.collider.GetComponentInParent<Squirrel>() == null)
                { blocked = true; break; }
            if (blocked) continue;
            float score = distance + Vector3.ProjectOnPlane(goal - point, Vector3.up).magnitude;
            if (score >= best) continue;
            best = score;
            bypassPoint = point;
            hasBypassPoint = true;
        }
    }

    private void OnDisable() { Stop(); hasBypassPoint = false; }

    private void OnDestroy()
    {
        if (bodyCollider != null) bodyCollider.sharedMaterial = originalMaterial;
        if (locomotionMaterial != null) Destroy(locomotionMaterial);
    }
}
