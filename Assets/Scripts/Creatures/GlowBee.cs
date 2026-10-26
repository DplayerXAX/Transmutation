using UnityEngine;

/// <summary>A nectar forager. Dynamic flight allows existing silk and bubble capture to own its body.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider), typeof(WebCapture))]
public sealed class GlowBee : Creature
{
    public enum Activity { Seeking, Gathering, Returning, Fleeing, Resting, CirclingPlayer }
    [Min(0.1f)] [SerializeField] private float flightSpeed = 2.4f;
    [Min(0.1f)] [SerializeField] private float gatheringSeconds = 2.5f;
    [Min(0.1f)] [SerializeField] private float predatorDistance = 4f;
    private GlowBeeColony colony;
    private GlowBeeNest nest;
    private NectarFlower flower;
    private Rigidbody body;
    private float timer, nextSearch, nectar;
    private float orbitUntil, nextOrbit;
    [Min(0.5f)] [SerializeField] private float playerInterestRange = 5f;
    [Min(0.5f)] [SerializeField] private float orbitSeconds = 3f;
    private Vector3 destination;
    private Transform leftWing, rightWing;
    public Activity State { get; private set; }
    public float CarriedNectar => nectar;
    public GlowBeeNest Nest => nest;

    protected override void InitializeCreature()
    {
        SetCreatureName("Glow Bee");
        SetFoodAndDiet(FoodTag.Meat, FoodTag.None);
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = false;
        body.mass = 0.08f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        GetComponent<SphereCollider>().radius = 0.14f;
    }

    internal void Configure(GlowBeeColony owner, GlowBeeNest home, float delay)
    {
        colony = owner;
        nest = home;
        timer = delay;
        State = Activity.Resting;
        destination = body.position;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (IsCarried || IsBubbleCaptured || IsConsumptionPending || colony == null) return;
        if (leftWing == null) leftWing = transform.Find("Left wing");
        if (rightWing == null) rightWing = transform.Find("Right wing");
        float flap = Mathf.Sin(Time.time * 65f + GetInstanceID()) * 35f;
        if (leftWing != null) leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
        if (rightWing != null) rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
        if (nest == null || nest.IsDead) { StopFlight(); return; }
        if (Time.time >= nextSearch)
        {
            nextSearch = Time.time + 0.3f;
            if (colony.TryFindPredator(body.position, predatorDistance, out Vector3 threat))
            {
                Vector3 escape = body.position - threat;
                if (escape.sqrMagnitude < 0.01f) escape = Vector3.right;
                destination = body.position + escape.normalized * 3f + Vector3.up * 0.6f;
                State = Activity.Fleeing;
                timer = 1.2f;
            }
        }
        if (State == Activity.Fleeing)
        {
            timer -= deltaTime;
            if (timer <= 0f) State = nectar > 0f ? Activity.Returning : Activity.Seeking;
            return;
        }
        if (nectar > 0f)
        {
            State = Activity.Returning;
            destination = nest.Entrance;
            if (Vector3.Distance(body.position, destination) < 0.4f && nest.TryStoreNectar(nectar))
            {
                nectar = 0f;
                State = Activity.Resting;
                timer = 1.5f;
            }
            return;
        }
        var player = colony.PlayerEffects;
        bool canVisit = player != null && player.isActiveAndEnabled &&
            (player.transform.position - nest.transform.position).sqrMagnitude < 24f * 24f;
        float interestRange = player != null && player.IsScented ? 12f : playerInterestRange;
        if (canVisit && (Time.time < orbitUntil || (Time.time >= nextOrbit &&
            (player.transform.position - body.position).sqrMagnitude < interestRange * interestRange)))
        {
            if (State != Activity.CirclingPlayer)
            {
                orbitUntil = Time.time + orbitSeconds;
                timer = 0f;
            }
            State = Activity.CirclingPlayer;
            float angle = Time.time * 1.6f + Mathf.Abs(GetInstanceID() % 100) * 0.63f;
            destination = player.transform.position + new Vector3(Mathf.Cos(angle) * 1.8f,
                1.25f + Mathf.Sin(angle * 1.7f) * 0.3f, Mathf.Sin(angle) * 1.8f);
            nextOrbit = orbitUntil + 8f;
            return;
        }
        if (State == Activity.CirclingPlayer) State = Activity.Seeking;
        if (State == Activity.Resting)
        {
            destination = nest.Entrance;
            timer -= deltaTime;
            if (timer > 0f) return;
            State = Activity.Seeking;
        }
        if (flower == null || !flower.isActiveAndEnabled || !flower.HasNectar)
        {
            flower = colony.FindFlower(body.position);
            State = Activity.Seeking;
            timer = 0f;
            if (flower == null) { destination = nest.Entrance + Vector3.up * 0.5f; return; }
        }
        destination = flower.SippingPoint;
        if (Vector3.Distance(body.position, destination) > 0.35f)
        {
            State = Activity.Seeking;
            timer = 0f;
            return;
        }
        State = Activity.Gathering;
        timer += deltaTime;
        if (timer < gatheringSeconds) return;
        nectar = flower.TakeNectar(1f);
        flower = null;
        timer = 0f;
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled || IsConsumptionPending || IsCarried || IsBubbleCaptured ||
            body == null || body.isKinematic || colony == null) return;
        Vector3 offset = destination - body.position;
        Vector3 desired = Vector3.ClampMagnitude(offset * 3f, flightSpeed * (State == Activity.Fleeing ? 1.5f : 1f));
        // Steer around physical scenery. Silk remains catchable, including while fleeing.
        if (desired.sqrMagnitude > 0.1f && Physics.SphereCast(body.position, 0.16f, desired.normalized,
                out RaycastHit hit, 0.75f, colony.SceneryLayers, QueryTriggerInteraction.Ignore))
            desired = Vector3.ProjectOnPlane(desired, hit.normal) + hit.normal * 0.6f + Vector3.up * 0.8f;
        SetCreatureVelocity(Vector3.MoveTowards(body.linearVelocity, desired, 10f * Time.fixedDeltaTime));
        if (desired.sqrMagnitude > 0.05f)
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(desired), Time.fixedDeltaTime * 6f));
    }

    private void StopFlight()
    {
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
    }
    protected override void OnCaptureStarted() { timer = 0f; StopFlight(); }
    protected override void OnDeath() { StopFlight(); }
}
