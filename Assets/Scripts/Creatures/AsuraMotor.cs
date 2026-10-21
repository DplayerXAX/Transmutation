using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class AsuraMotor : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float acceleration = 12f;
    [Min(0f)] [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private LayerMask groundLayers = ~0;
    private Rigidbody body;
    private Collider shape;
    private Creature owner;
    private Vector3 intent, facing, bypass;
    private bool steering;
    private float pushedUntil;
    private RaycastHit[] hits = new RaycastHit[16];
    public bool Grounded { get; private set; }
    public bool OwnsMovement => owner != null && owner.SimulationEnabled && !owner.IsCarried &&
        !owner.IsBubbleCaptured && !owner.IsConsumptionPending && body != null && !body.isKinematic;

    private void Awake()
    {
        owner = GetComponent<Creature>(); body = GetComponent<Rigidbody>(); shape = GetComponent<Collider>();
    }
    public void SetIntent(Vector3 velocity, Vector3 look)
    {
        intent = Vector3.ProjectOnPlane(velocity, Vector3.up);
        facing = Vector3.ProjectOnPlane(look, Vector3.up);
    }
    public void Stop() { intent = Vector3.zero; steering = false; }
    public void AcceptPush(float seconds = 0.5f) => pushedUntil = Mathf.Max(pushedUntil, Time.time + seconds);

    private void FixedUpdate()
    {
        if (!OwnsMovement) { Stop(); return; }
        if (shape == null || Time.time < pushedUntil) return;
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();
        float foot = shape.bounds.min.y;
        Grounded = physics.Raycast(new Vector3(body.position.x, foot + 0.08f, body.position.z),
            Vector3.down, 0.2f, groundLayers, QueryTriggerInteraction.Ignore);
        Vector3 desired = intent;
        if (desired.sqrMagnitude > 0.01f)
        {
            float radius = Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.z);
            if (steering && Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).sqrMagnitude < 0.16f) steering = false;
            Vector3 direction = steering ? Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).normalized : desired.normalized;
            Vector3 origin = body.worldCenterOfMass;
            if (CreatureSpatialQuery.FirstSolid(gameObject.scene, origin, origin + direction * (radius + 0.9f),
                owner, null, ref hits, out var obstacle) && Mathf.Abs(obstacle.normal.y) < 0.5f &&
                CreatureSpatialQuery.Owner(obstacle.collider) == null)
            {
                if (!steering)
                {
                    Bounds bounds = obstacle.collider.bounds;
                    bounds.Expand(new Vector3((radius + 0.5f) * 2f, 0f, (radius + 0.5f) * 2f));
                    Vector3 tangent = Vector3.Cross(Vector3.up, obstacle.normal);
                    if (Vector3.Dot(tangent, transform.right) < 0f) tangent = -tangent;
                    bypass = body.position + tangent * (bounds.extents.magnitude + 0.75f) + direction * 0.5f;
                    steering = true;
                }
                direction = Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).normalized;
            }
            desired = direction * desired.magnitude;
        }
        // Bounded force preserves vertical velocity and lets external impulses decay naturally.
        if (Grounded)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 force = Vector3.ClampMagnitude((desired - horizontal) / Time.fixedDeltaTime, acceleration);
            body.AddForce(force, ForceMode.Acceleration);
        }
        Vector3 look = desired.sqrMagnitude > 0.01f ? desired : facing;
        if (look.sqrMagnitude > 0.001f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(look), turnSpeed * Time.fixedDeltaTime));
    }
}
