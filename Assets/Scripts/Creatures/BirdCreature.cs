using System.Collections.Generic;
using UnityEngine;

/// <summary>An ordinary carryable Creature; the scene manager only coordinates shared flight snapshots.</summary>
[RequireComponent(typeof(Rigidbody), typeof(WebCapture))]
public sealed class BirdCreature : Creature
{
    [Header("Flight")]
    [Tooltip("Minimum and maximum flight speed in metres per second. Spawn areas can override this on their spawned instances.")]
    [SerializeField] private Vector2 speedRange = new Vector2(3f, 6f);
    [Min(0.1f)] [SerializeField] private float steeringStrength = 5f;
    [Min(0f)] [SerializeField] private float turnSpeed = 160f;

    [Header("Flocking")]
    [Min(0.1f)] [SerializeField] private float neighborRadius = 9f;
    [Min(0.01f)] [SerializeField] private float separationDistance = 1.8f;
    [Min(0f)] [SerializeField] private float separationWeight = 1.8f;
    [Min(0f)] [SerializeField] private float alignmentWeight = 1f;
    [Min(0f)] [SerializeField] private float cohesionWeight = 0.65f;
    [Min(0f)] [SerializeField] private float wanderWeight = 0.3f;

    [Header("Food Attraction")]
    [Tooltip("Maximum world-space distance to compatible food, including food outside the sky volume.")]
    [Min(0f)] [SerializeField] private float huntRadius = 25f;
    [Tooltip("Maximum food steering weight, scaled by Hunger. Zero disables seeking and eating.")]
    [Min(0f)] [SerializeField] private float huntDesire = 2f;

    [Header("Hunger")]
    [Tooltip("0 is full; 100 is very hungry. Hunger does not cause starvation death.")]
    [Range(0f, 100f)] [SerializeField] private float hunger = 50f;
    [Tooltip("Hunger gained per second while creature simulation runs, including carrying.")]
    [Min(0f)] [SerializeField] private float hungerRate = 0.25f;
    [Tooltip("Minimum Hunger required to seek or physically eat food. Shared meals can feed less hungry birds.")]
    [Range(0f, 100f)] [SerializeField] private float appetiteThreshold = 20f;
    [Tooltip("Other compatible, hungry flying birds within this distance of the food share its fixed nutrition budget.")]
    [Min(0f)] [SerializeField] private float nutritionSharingRadius = 10f;

    [Header("Ageing Remains")]
    [Tooltip("Bones left only when Age expires. Direct death and consumption follow the shared creature lifecycle. An empty reference uses automatic placeholder bones.")]
    [SerializeField] private BirdBones bonesPrefab;

    private Rigidbody body;
    private WebCapture webCapture;
    private BirdFlockManager manager;
    private Vector3 lastHeadingWS;
    private float wanderPhase;

    public Rigidbody FlightBody => body;
    public Vector2 FlightSpeedRange => speedRange;
    public bool IsWebCaptured => webCapture != null && webCapture.IsCaptured;
    public BirdFlockManager FlockManager => manager;
    public float SearchRadius => Mathf.Max(neighborRadius, separationDistance);
    public float HuntRadius => huntRadius;
    public float HuntDesire => huntDesire;
    public float Hunger => Mathf.Clamp(hunger, 0f, 100f);
    public float EffectiveHuntDesire => Hunger > 0f && Hunger >= appetiteThreshold
        ? Mathf.Max(0f, huntDesire) * Hunger / 100f : 0f;
    public Creature FoodTarget { get; private set; }
    public BirdBones BonesPrefab => bonesPrefab;
    public bool IsFlying => isActiveAndEnabled && SimulationEnabled && !IsDead && !IsConsumptionPending && !IsCarried && !IsBubbleCaptured && !IsWebCaptured && body != null && !body.isKinematic;

    protected override void InitializeCreature()
    {
        body = GetComponent<Rigidbody>();
        webCapture = GetComponent<WebCapture>();
        lastHeadingWS = transform.forward;
        SetCreatureName("Bird");
        if (!Application.isPlaying || IsDead) return;
        SetMovementPhysics(false, false);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        SetFlightSeed(GetInstanceID());
        SetCreatureVelocity(lastHeadingWS * CruiseSpeed);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (Application.isPlaying && !IsDead) EnsureRegistered();
    }

    private void EnsureRegistered()
    {
        if (manager != null && manager.gameObject.scene == gameObject.scene) return;
        if (manager != null) manager.Unregister(this);
        manager = BirdFlockManager.GetOrCreate(gameObject.scene, transform.position);
        manager.Register(this);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || IsDead || IsConsumptionPending) return;
        if (deltaTime > 0f) hunger = Mathf.Clamp(Hunger + Mathf.Max(0f, hungerRate) * deltaTime, 0f, 100f);
        // Also repairs registration after a manager is destroyed or this bird moves between scenes.
        EnsureRegistered();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (manager != null) manager.Unregister(this);
        manager = null;
        FoodTarget = null;
    }

    protected override void OnDeath()
    {
        if (manager != null) manager.Unregister(this);
        manager = null;
        FoodTarget = null;
    }

    protected override void OnAgeDeath()
    {
        // Shared death has already released attachments, stopped flight, and notified listeners.
        BirdBones remains = BirdBones.Spawn(bonesPrefab, gameObject.scene, transform.position, transform.rotation,
            body != null ? body.linearVelocity : Vector3.zero,
            body != null ? body.angularVelocity : Vector3.zero);
        remains.SetResolvedPolarity(Polarity);
        // Retire the original immediately so its collider/food cannot compete with the remains.
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision) => EatContact(collision.collider);
    private void OnCollisionStay(Collision collision) => EatContact(collision.collider);
    private void OnTriggerEnter(Collider other) => EatContact(other);
    private void OnTriggerStay(Collider other) => EatContact(other);

    public override bool TryConsumeFood(Creature food)
    {
        if (!IsFlying || EffectiveHuntDesire <= 0f || food == null) return false;
        // Special effects and death can change tags, move objects, or remove the food immediately.
        var meal = new BirdMeal(food, nutritionSharingRadius);
        BirdFlockManager mealManager = manager;
        if (!base.TryConsumeFood(food)) return false;
        if (mealManager != null) mealManager.ShareNutrition(this, meal);
        return true;
    }

    internal bool CanReceiveNutrition(BirdMeal meal) => IsFlying && Hunger > 0f &&
        gameObject.scene == meal.Scene && AcceptsFood(meal.FoodTags, meal.FoodType);

    internal float ReceiveNutrition(float amount)
    {
        if (!IsFlying || amount <= 0f) return 0f;
        float received = Mathf.Min(Hunger, amount);
        hunger = Mathf.Max(0f, Hunger - received);
        if (EffectiveHuntDesire <= 0f) FoodTarget = null;
        return received;
    }

    private void EatContact(Collider other)
    {
        if (!IsFlying || EffectiveHuntDesire <= 0f || other == null) return;
        Creature food = other.GetComponentInParent<Creature>();
        if (food != null && food.gameObject.scene == gameObject.scene) TryConsumeFood(food);
    }

    internal Vector3 FoodDirection(Vector3 position, List<Creature> foods, List<Vector3> foodPositions)
    {
        FoodTarget = null;
        if (EffectiveHuntDesire <= 0f || huntRadius <= 0f) return Vector3.zero;
        float nearestSquared = huntRadius * huntRadius;
        Vector3 direction = Vector3.zero;
        for (int i = 0; i < foods.Count; i++)
        {
            Creature food = foods[i];
            if (food == null || !food.CanBeAITargeted || food.IsConsumptionPending || !CanEat(food)) continue;
            Vector3 offset = foodPositions[i] - position;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared > nearestSquared) continue;
            if (FoodTarget != null && distanceSquared == nearestSquared && food.GetInstanceID() > FoodTarget.GetInstanceID()) continue;
            FoodTarget = food;
            nearestSquared = distanceSquared;
            direction = offset;
        }
        return direction;
    }

    public void SetFlightSeed(int seed)
    {
        var random = new System.Random(seed);
        wanderPhase = (float)random.NextDouble() * Mathf.PI * 2f;
    }

    /// <summary>Sets this instance's limits without changing its prefab or claiming carry/web movement.</summary>
    public void SetFlightSpeedRange(Vector2 range)
    {
        speedRange.x = Mathf.Max(0.1f, range.x);
        speedRange.y = Mathf.Max(speedRange.x, range.y);
    }

    private float CruiseSpeed => (speedRange.x + speedRange.y) * 0.5f;

    internal bool CaptureFlightState(out Vector3 positionWS, out Vector3 velocityWS)
    {
        positionWS = body != null ? body.position : transform.position;
        velocityWS = Vector3.zero;
        if (!IsFlying) { FoodTarget = null; return false; }
        velocityWS = body.linearVelocity;
        // A stationary drop or a collision must not strand a bird on the ground.
        if (velocityWS.sqrMagnitude < 0.0001f) velocityWS = lastHeadingWS * speedRange.x;
        return true;
    }

    private Vector3 Steer(Vector3 directionWS, Vector3 velocityWS)
    {
        if (directionWS.sqrMagnitude < 0.0001f) return Vector3.zero;
        return Vector3.ClampMagnitude(directionWS.normalized * CruiseSpeed - velocityWS, steeringStrength);
    }

    internal Vector3 CalculateFlightVelocity(int self, Vector3[] positionsWS, Vector3[] velocitiesWS,
        List<int> neighbors, Vector3 boundaryDirectionWS, float boundaryWeight, float time, float deltaTime,
        Vector3 foodDirectionWS = default)
    {
        Vector3 position = positionsWS[self], velocity = velocitiesWS[self];
        Vector3 separation = Vector3.zero, alignment = Vector3.zero, center = Vector3.zero;
        int count = 0;
        float neighborSquared = neighborRadius * neighborRadius;
        float separationSquared = separationDistance * separationDistance;
        for (int n = 0; n < neighbors.Count; n++)
        {
            int other = neighbors[n];
            if (other == self) continue;
            Vector3 offset = position - positionsWS[other];
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared < separationSquared)
            {
                // A stable opposite pair of directions resolves exactly coincident spawns.
                if (distanceSquared < 0.000001f)
                {
                    int a = Mathf.Min(self, other), b = Mathf.Max(self, other);
                    Vector3 axis = new Vector3(Mathf.Sin(a * 1.7f + b), Mathf.Cos(a + b * 2.3f), Mathf.Sin(b * 0.9f + a));
                    separation += axis.normalized * (self < other ? 1f : -1f);
                }
                else separation += offset / Mathf.Max(distanceSquared, 0.01f);
            }
            if (distanceSquared > neighborSquared) continue;
            alignment += velocitiesWS[other];
            center += positionsWS[other];
            count++;
        }
        Vector3 acceleration = Steer(separation, velocity) * separationWeight;
        if (count > 0)
        {
            acceleration += Steer(alignment / count, velocity) * alignmentWeight;
            acceleration += Steer(center / count - position, velocity) * cohesionWeight;
        }
        float phase = time * 0.6f + wanderPhase;
        Vector3 wander = lastHeadingWS + new Vector3(Mathf.Sin(phase), Mathf.Sin(phase * 0.73f) * 0.6f,
            Mathf.Cos(phase * 0.87f)) * 0.65f;
        acceleration += Steer(wander, velocity) * wanderWeight;
        acceleration += Steer(boundaryDirectionWS, velocity) * boundaryWeight;
        acceleration += Steer(foodDirectionWS, velocity) * EffectiveHuntDesire;
        Vector3 nextVelocity = velocity + Vector3.ClampMagnitude(acceleration, steeringStrength) * deltaTime;
        float speed = nextVelocity.magnitude;
        Vector3 heading = speed > 0.0001f ? nextVelocity / speed : lastHeadingWS;
        return heading * Mathf.Clamp(speed, speedRange.x, speedRange.y);
    }

    internal void ApplyFlightVelocity(Vector3 velocityWS, float deltaTime)
    {
        if (!IsFlying) return;
        SetCreatureVelocity(velocityWS);
        if (velocityWS.sqrMagnitude < 0.0001f) return;
        lastHeadingWS = velocityWS.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(lastHeadingWS, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(lastHeadingWS, up);
        body.angularVelocity = Vector3.zero;
        body.MoveRotation(Quaternion.RotateTowards(body.rotation, rotation, turnSpeed * deltaTime));
    }

    private void OnValidate()
    {
        SetFlightSpeedRange(speedRange);
        steeringStrength = Mathf.Max(0.1f, steeringStrength);
        neighborRadius = Mathf.Max(0.1f, neighborRadius);
        separationDistance = Mathf.Max(0.01f, separationDistance);
        huntRadius = Mathf.Max(0f, huntRadius);
        huntDesire = Mathf.Max(0f, huntDesire);
        hunger = Mathf.Clamp(hunger, 0f, 100f);
        hungerRate = Mathf.Max(0f, hungerRate);
        appetiteThreshold = Mathf.Clamp(appetiteThreshold, 0f, 100f);
        nutritionSharingRadius = Mathf.Max(0f, nutritionSharingRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.15f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, huntRadius);
        if (FoodTarget != null) Gizmos.DrawLine(transform.position, FoodTarget.transform.position);
    }
}
