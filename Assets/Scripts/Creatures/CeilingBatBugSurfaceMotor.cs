using UnityEngine;

/// <summary>Local surface following: parallel-transport the heading across concave and convex edges.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class CeilingBatBugSurfaceMotor : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 1.1f;
    [SerializeField, Min(0.05f)] private float bodyOffset = 0.21f;
    [SerializeField, Min(0.2f)] private float roamRadius = 12f;
    [SerializeField] private LayerMask surfaces = (1 << 7) | (1 << 8);
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private CeilingBatBug bug;
    private Rigidbody body;
    private float nextTurn;
    private Vector3 home;
    private bool homeConfigured;
    private CeilingBatBugHabitat habitat;
    public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
    public bool Attached { get; private set; }

    private void Awake()
    {
        bug = GetComponent<CeilingBatBug>();
        body = GetComponent<Rigidbody>();
        SurfaceNormal = transform.up;
        if (!homeConfigured) home = body.position;
        nextTurn = Time.time + 4f + (GetInstanceID() & 7) * 0.45f;
    }
    public void ConfigureHome(Vector3 position, float radius, CeilingBatBugHabitat bounds)
    { home = position; roamRadius = radius; habitat = bounds; homeConfigured = true; }
    internal void StartOnPerch(RaycastHit hit)
    {
        Attached = true;
        SurfaceNormal = hit.normal;
        body.position = hit.point + hit.normal * bodyOffset;
        transform.SetPositionAndRotation(body.position, Quaternion.LookRotation(Vector3.forward, hit.normal));
        body.useGravity = false;
    }

    public void Detach()
    {
        bool wasAttached = Attached; Attached = false;
        if (body != null && !body.isKinematic) body.useGravity = true;
        if (wasAttached && bug != null) bug.InvalidateSupport();
    }
    public bool TrySurface(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit) => Probe(origin, direction, distance, out hit);

    private void FixedUpdate()
    {
        if (bug == null || bug.IsDead || !bug.SimulationEnabled || bug.IsCarried || bug.IsBubbleCaptured ||
            (TryGetComponent<WebCapture>(out var capture) && capture.IsCaptured) || CreatureThrower.IsFlying(bug)) return;
        if (bug.HabitatWorld != null && !bug.HabitatWorld.IsRegionReady(body.position, 2f)) return;
        if (body.isKinematic) return;
        Vector3 normal = SurfaceNormal;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.Cross(normal, transform.right).normalized;
        if (Time.time >= nextTurn)
        {
            forward = Quaternion.AngleAxis(Random.Range(-40f, 40f), normal) * forward;
            nextTurn = Time.time + Random.Range(4f, 8f);
        }
        if (Vector3.Distance(body.position, home) > roamRadius * 0.88f ||
            (habitat != null && !habitat.Contains(body.position + forward * 0.8f)))
        {
            Vector3 homeward = Vector3.ProjectOnPlane(home - body.position, normal);
            if (homeward.sqrMagnitude > 0.01f) forward = Vector3.RotateTowards(forward, homeward.normalized, 5f * Time.fixedDeltaTime, 0f).normalized;
        }

        RaycastHit support;
        float step = speed * Time.fixedDeltaTime;
        Vector3 ahead = body.position + forward * (step + 0.18f);
        bool found = Probe(body.position, forward, step + bodyOffset + 0.2f, out support);
        if (!found) found = Probe(ahead + normal * 0.15f, -normal, 0.85f, out support);
        if (!found) found = Probe(ahead - normal * 0.48f, -forward, 0.75f, out support);
        if (!found) found = Probe(body.position + normal * 0.1f, -normal, 0.9f, out support);
        if (!found && !Attached)
        {
            // Reacquire after a fall or carry release without teleporting to distant surfaces.
            foreach (Vector3 direction in AcquireDirections)
                if (Probe(body.position, direction, 0.6f, out support)) { found = true; break; }
        }
        if (!found)
        {
            Detach();
            body.useGravity = true;
            return;
        }

        Attached = true;
        if (habitat != null && !habitat.Contains(support.point + support.normal * bodyOffset))
        { body.linearVelocity = Vector3.zero; body.MoveRotation(Quaternion.LookRotation(-forward, normal)); nextTurn = Time.time + 3f; return; }
        body.useGravity = false;
        Vector3 transported = Quaternion.FromToRotation(normal, support.normal) * forward;
        transported = Vector3.ProjectOnPlane(transported, support.normal).normalized;
        if (transported.sqrMagnitude < 0.1f) transported = Vector3.Cross(support.normal, transform.right).normalized;
        SurfaceNormal = support.normal;
        Vector3 desired = body.position + transported * step;
        Vector3 target = desired + support.normal * (bodyOffset - Vector3.Dot(desired - support.point, support.normal));
        body.linearVelocity = Vector3.ClampMagnitude((target - body.position) / Time.fixedDeltaTime, speed * 2f + 1f);
        body.angularVelocity = Vector3.zero;
        body.MoveRotation(Quaternion.LookRotation(transported, support.normal));
    }

    private static readonly Vector3[] AcquireDirections =
        { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

    private bool Probe(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = ReflectionPhysics.RaycastNonAlloc(origin, direction, hits, distance, surfaces, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.distance >= best || hit.collider.attachedRigidbody == body ||
                hit.collider.GetComponentInParent<Creature>() != null) continue;
            nearest = hit;
            best = hit.distance;
        }
        return best < float.PositiveInfinity;
    }
}
