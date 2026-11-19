using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class AsuraMotor : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float acceleration = 12f;
    [Min(0f)] [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [Range(0,70)] [SerializeField] private float maximumWalkableSlope=45;
    [Min(0.02f)] [SerializeField] private float groundProbeDistance=0.25f;
    [Range(0f, 1f)] [SerializeField] private float knockbackMultiplier = 1f;
    public float KnockbackMultiplier => knockbackMultiplier;
    private Rigidbody body;
    private Collider shape;
    private Creature owner;
    private Vector3 intent, facing, bypass;
    private bool steering;
    private float pushedUntil;
    private float progressClock, blockedSeconds, sidestepUntil, steeringUntil;
    private Vector3 progressPosition, sidestep;
    private int sidestepSign=1;
    private RaycastHit[] hits = new RaycastHit[16];
    public bool Grounded { get; private set; }
    public Vector3 GroundNormal {get;private set;}=Vector3.up;
    public bool OwnsMovement => owner != null && !owner.IsDead && owner.SimulationEnabled && !owner.IsCarried &&
        !owner.IsBubbleCaptured && !owner.IsConsumptionPending && body != null && !body.isKinematic;

    private void Awake()
    {
        owner = GetComponent<Creature>(); body = GetComponent<Rigidbody>(); shape = GetComponent<Collider>();
        sidestepSign=(Mathf.Abs(GetInstanceID())%2)==0?1:-1;
        owner.Died += OnDied;
    }
    private void OnDied(Creature dead) { body.linearDamping = 1.2f; Stop(); }
    private void OnDestroy() { if (owner != null) owner.Died -= OnDied; }
    public void SetIntent(Vector3 velocity, Vector3 look)
    {
        intent = Vector3.ProjectOnPlane(velocity, Vector3.up);
        facing = Vector3.ProjectOnPlane(look, Vector3.up);
    }
    public void Stop() { intent = Vector3.zero; steering = false; }
    public void AcceptPush(float seconds = 0.5f) => pushedUntil = Mathf.Max(pushedUntil, Time.time + seconds);

    private void ProbeGround(PhysicsScene physics)
    {
        Grounded=false;GroundNormal=Vector3.up;
        Bounds bounds=shape.bounds;
        float radius=Mathf.Max(0.05f,Mathf.Min(bounds.extents.x,bounds.extents.z)*0.9f);
        float distance=Mathf.Max(0,bounds.extents.y-radius)+groundProbeDistance;
        int count;
        if(shape is BoxCollider box)
        {
            // An upright box rests on its uphill edge. A center ray or inscribed sphere
            // misses that contact, particularly for the general's wider body.
            Vector3 half=Vector3.Scale(box.size*0.475f,box.transform.lossyScale);
            while((count=physics.BoxCast(box.transform.TransformPoint(box.center),half,Vector3.down,hits,
                box.transform.rotation,groundProbeDistance,groundLayers,QueryTriggerInteraction.Ignore))==hits.Length)
                System.Array.Resize(ref hits,hits.Length*2);
        }
        else
        {
            while((count=physics.SphereCast(bounds.center,radius,Vector3.down,hits,distance,groundLayers,QueryTriggerInteraction.Ignore))==hits.Length)
                System.Array.Resize(ref hits,hits.Length*2);
        }
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
        {
            // Bodies are obstacles, not terrain to stand on; never ground on our own collider.
            if(hits[i].collider.GetComponentInParent<Creature>()!=null || hits[i].distance>=nearest)continue;
            nearest=hits[i].distance;GroundNormal=hits[i].normal;
        }
        Grounded=!float.IsPositiveInfinity(nearest) && GroundNormal.y>=Mathf.Cos(maximumWalkableSlope*Mathf.Deg2Rad) &&
            Vector3.Dot(body.linearVelocity,GroundNormal)<1;
    }

    private void FixedUpdate()
    {
        if (!OwnsMovement) { Grounded=false;Stop(); return; }
        if (shape == null) return;
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();
        float foot = shape.bounds.min.y;
        ProbeGround(physics);
        if (Time.time < pushedUntil) return;
        Vector3 desired = intent;
        // Reuse movement progress rather than running neighbour searches on every fighter.
        progressClock+=Time.fixedDeltaTime;
        if(progressClock>=0.5f)
        {
            blockedSeconds=Grounded && desired.sqrMagnitude>0.1f && Vector3.Distance(body.position,progressPosition)<0.15f?blockedSeconds+progressClock:0;
            progressPosition=body.position;progressClock=0;
            if(blockedSeconds>=2.5f && Time.time>=sidestepUntil && TryGetComponent<AsuraTerritoryMember>(out var member) && member.Territory!=null)
            {
                Vector3 side=Vector3.Cross(Vector3.up,desired.normalized)*sidestepSign;
                var probe=body.position+side*1.2f;probe.y=foot+0.6f;
                if(physics.Raycast(probe,Vector3.down,1.5f,member.Territory.groundLayers,QueryTriggerInteraction.Ignore))
                {sidestep=side;sidestepUntil=Time.time+1.5f;}
                else sidestepSign=-sidestepSign;
                steering=false;blockedSeconds=0;
            }
        }
        if(steering && Time.time>steeringUntil)steering=false;
        bool recovering=Time.time<sidestepUntil && desired.sqrMagnitude>0.01f;
        if(recovering)desired=(sidestep+desired.normalized*0.5f).normalized*desired.magnitude;
        if (desired.sqrMagnitude > 0.01f)
        {
            float radius = Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.z);
            if (steering && Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).sqrMagnitude < 0.16f) steering = false;
            Vector3 direction = steering ? Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).normalized : desired.normalized;
            Vector3 origin = body.worldCenterOfMass;
            if (!recovering && CreatureSpatialQuery.FirstSolid(gameObject.scene, origin, origin + direction * (radius + 0.9f),
                owner, null, ref hits, out var obstacle) && Mathf.Abs(obstacle.normal.y) < 0.5f &&
                (CombatTarget.Owner(obstacle.collider) == null ||
                 (CombatTarget.Owner(obstacle.collider) is Creature ally && ally!=owner && ally.Polarity==owner.Polarity &&
                  owner.TryGetComponent<AsuraTerritoryMember>(out _))))
            {
                if (!steering)
                {
                    Bounds bounds = obstacle.collider.bounds;
                    bounds.Expand(new Vector3((radius + 0.5f) * 2f, 0f, (radius + 0.5f) * 2f));
                    Vector3 tangent = Vector3.Cross(Vector3.up, direction)*sidestepSign;
                    float clearance=Mathf.Min(4,bounds.extents.magnitude+0.75f);
                    bypass = body.position + tangent * clearance + direction * 2.5f;
                    steering = true;
                    steeringUntil=Time.time+3;
                }
                direction = Vector3.ProjectOnPlane(bypass - body.position, Vector3.up).normalized;
            }
            desired = direction * desired.magnitude;
        }
        // Walking is controlled in the support plane, including its vertical component.
        // Cancel tangential gravity only while alive and supported, not by changing friction.
        if (Grounded)
        {
            Vector3 tangent=Vector3.ProjectOnPlane(desired,GroundNormal);
            if(tangent.sqrMagnitude>0.0001f)tangent=tangent.normalized*desired.magnitude;
            Vector3 current=Vector3.ProjectOnPlane(body.linearVelocity,GroundNormal);
            Vector3 walking=Vector3.MoveTowards(current,tangent,acceleration*Time.fixedDeltaTime);
            body.linearVelocity=walking+GroundNormal*Mathf.Min(0,Vector3.Dot(body.linearVelocity,GroundNormal));
            if(body.useGravity)body.AddForce(-Vector3.ProjectOnPlane(Physics.gravity,GroundNormal),ForceMode.Acceleration);
        }
        Vector3 look = desired.sqrMagnitude > 0.01f ? desired : facing;
        if (look.sqrMagnitude > 0.001f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(look), turnSpeed * Time.fixedDeltaTime));
    }
}
