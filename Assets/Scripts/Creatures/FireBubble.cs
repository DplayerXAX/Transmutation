using UnityEngine;

/// <summary>
/// A Bubble produced by FireFlower.
/// Flies along one horizontal heading with gentle sway, then falls and becomes
/// a Fire Flower after remaining still for the configured delay.
/// </summary>
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public sealed class FireBubble : Creature
{
    private enum BubbleState
    {
        Floating,
        Inanimate
    }

    [Header("Floating")]
    [Tooltip("World-space upward movement in metres per second.")]
    [Min(0f)]
    [SerializeField] private float upwardSpeed = 0.5f;

    [Tooltip("World-space Y height at which this Bubble stops rising and begins falling under gravity.")]
    [SerializeField] private float fallStartWorldY = 10f;

    [Tooltip("Maximum horizontal distance from the Bubble's central upward path.")]
    [Min(0f)]
    [SerializeField] private float swayDistance = 0.35f;

    [Tooltip("Number of complete side-to-side sway cycles per second.")]
    [Min(0f)]
    [SerializeField] private float swayFrequency = 0.15f;

    [SerializeField] private float rotationSpeed = 20f;

    [Header("Horizontal Flight")]
    [Tooltip("Constant horizontal speed. Each Bubble chooses one heading at spawn and keeps it throughout flight.")]
    [Min(0f)]
    [SerializeField] private float horizontalSpeed = 1f;

    [Tooltip("Maximum flight acceleration. Limits how hard the Bubble pushes against obstacles.")]
    [Min(0.01f)]
    [SerializeField] private float flightAcceleration = 4f;

    [Header("Lifetime")]
    [Tooltip("Maximum flight time before falling. Reaching Fall Start World Y can end flight sooner. Independent of Heat.")]
    [Min(0f)]
    [SerializeField] private float lifetimeSeconds = 20f;

    [Header("Flower Transform")]
    [Tooltip("Fire Flower spawned after the fallen Bubble remains still. Independent of Heat.")]
    [SerializeField] private FireFlower fireFlowerPrefab;

    [Tooltip("Consecutive seconds of stillness after falling before becoming a flower. Moving or carrying resets the timer.")]
    [Min(0f)]
    [SerializeField] private float inanimateToFlowerDelay = 10f;

    [Tooltip("Linear speed below which physics jitter counts as still, in metres per second.")]
    [Min(0f)]
    [SerializeField] private float stillSpeedThreshold = 0.05f;

    [Tooltip("Maximum movement from the resting position before the stillness timer resets, in metres.")]
    [Min(0f)]
    [SerializeField] private float stillPositionTolerance = 0.05f;

    [Header("Visual Heat")]
    [Tooltip("Renderer using the Fire Bubble Shader. The first child Renderer is used when left empty.")]
    [SerializeField] private Renderer bubbleRenderer;

    [Tooltip("Shader still-heat floor. Visual only; does not control when the Bubble becomes inanimate.")]
    [SerializeField] private float stillVisualHeat = 13f;

    [Tooltip("Heat treated as maximum intensity by the Shader.")]
    [Min(0.001f)]
    [SerializeField] private float maximumVisualHeat = 25f;

    [Tooltip("Maximum procedural flow speed at Maximum Visual Heat.")]
    [Min(0f)]
    [SerializeField] private float maximumVisualFlowSpeed = 2f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private BubbleState currentState = BubbleState.Floating;

    [SerializeField] private Rigidbody bubbleBody;
    [SerializeField] private SphereCollider bubbleCollider;

    private Vector3 swayDirection;
    private float swayPhase;
    private float previousSwayOffset;
    private float elapsedTime;
    private float inanimateTimer;
    private Vector3 restingPosition;
    private bool transformationStarted;
    private bool descending;
    private Vector3 driftVelocity;
    private float visualFlowTime;
    private MaterialPropertyBlock visualProperties;
    private static readonly int HeatShaderId = Shader.PropertyToID("_Heat");
    private static readonly int StillHeatShaderId = Shader.PropertyToID("_StillHeat");
    private static readonly int MaximumHeatShaderId = Shader.PropertyToID("_MaximumHeat");
    private static readonly int FlowTimeShaderId = Shader.PropertyToID("_FlowTime");

    /// <summary>Whether this Bubble has started falling and can be eaten.</summary>
    public bool IsInanimate => currentState == BubbleState.Inanimate;

    /// <summary>Chooses a fixed heading and keeps the floating Bubble outside gravity simulation.</summary>
    protected override void InitializeCreature()
    {
        FindPhysicsReferences();
        FindVisualReferences();

        bubbleCollider.isTrigger = false;
        bubbleCollider.enabled = true;
        bubbleBody.isKinematic = false;
        bubbleBody.useGravity = false;
        bubbleBody.detectCollisions = true;
        bubbleBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        bubbleBody.interpolation = RigidbodyInterpolation.Interpolate;

        Vector2 randomDirection = Random.insideUnitCircle.normalized;

        if (randomDirection.sqrMagnitude < 0.001f)
        {
            randomDirection = Vector2.right;
        }

        Vector3 windDirection = new Vector3(randomDirection.x, 0f, randomDirection.y);
        // Sway crosses the main wind direction instead of retracing the same line.
        swayDirection = new Vector3(-windDirection.z, 0f, windDirection.x);
        swayPhase = Random.Range(0f, Mathf.PI * 2f);
        previousSwayOffset = CalculateSwayOffset(0f);
        descending = false;

        driftVelocity = windDirection * horizontalSpeed;
        restingPosition = bubbleBody.position;

        UpdateHeatVisual(0f);
    }

    /// <summary>Updates visuals. Movement and resting use the physics timestep.</summary>
    protected override void TickCreature(float deltaTime)
    {
        if (currentState == BubbleState.Inanimate)
        {
            return;
        }

        UpdateHeatVisual(deltaTime);
    }

    private void FixedUpdate()
    {
        if (bubbleBody == null || transformationStarted)
            return;

        if (SimulationEnabled && !descending)
        {
            elapsedTime += Time.fixedDeltaTime;
            if (elapsedTime >= lifetimeSeconds || bubbleBody.position.y >= fallStartWorldY || upwardSpeed <= 0f)
                StartFalling();
            else
                ApplyFlight(Time.fixedDeltaTime);
        }

        bool moved = bubbleBody.linearVelocity.sqrMagnitude > stillSpeedThreshold * stillSpeedThreshold ||
                     bubbleBody.angularVelocity.sqrMagnitude > 0.01f ||
                     (bubbleBody.position - restingPosition).sqrMagnitude > stillPositionTolerance * stillPositionTolerance;
        if (!SimulationEnabled || !descending || IsCarried || bubbleBody.isKinematic || moved)
        {
            inanimateTimer = 0f;
            restingPosition = bubbleBody.position;
            return;
        }

        inanimateTimer += Time.fixedDeltaTime;
        if (inanimateTimer < inanimateToFlowerDelay)
        {
            return;
        }

        TransformIntoFlower();
    }

    private void TransformIntoFlower()
    {
        transformationStarted = true;
        if (fireFlowerPrefab == null)
        {
            Debug.LogWarning($"{name} cannot become a flower because no Fire Flower prefab is assigned.", this);
            return;
        }

        Instantiate(fireFlowerPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    /// <summary>Changes the Bubble's current behavior state.</summary>
    private void ChangeState(BubbleState nextState)
    {
        if (currentState == nextState)
        {
            return;
        }

        currentState = nextState;
    }

    /// <summary>Stops scripted float motion and enables gravity so the Bubble descends.</summary>
    private void StartFalling()
    {
        if (descending)
        {
            return;
        }

        descending = true;
        ChangeState(BubbleState.Inanimate);
        inanimateTimer = 0f;
        restingPosition = transform.position;
        SetMovementPhysics(false, true);
    }

    private void ApplyFlight(float deltaTime)
    {
        float currentSwayOffset = CalculateSwayOffset(elapsedTime);
        float swaySpeed = (currentSwayOffset - previousSwayOffset) / deltaTime;
        previousSwayOffset = currentSwayOffset;

        // The carrier owns forces while held; keep sway time current for release.
        if (IsCarried)
            return;

        float riseSpeed = Mathf.Min(upwardSpeed, Mathf.Max(0f, fallStartWorldY - bubbleBody.position.y) / deltaTime);
        Vector3 desiredVelocity = driftVelocity + swayDirection * swaySpeed + Vector3.up * riseSpeed;
        Vector3 acceleration = (desiredVelocity - bubbleBody.linearVelocity) / deltaTime;
        bubbleBody.AddForce(Vector3.ClampMagnitude(acceleration, flightAcceleration), ForceMode.Acceleration);

        Vector3 desiredSpin = Vector3.up * (rotationSpeed * Mathf.Deg2Rad);
        bubbleBody.AddTorque((desiredSpin - bubbleBody.angularVelocity) * 4f, ForceMode.Acceleration);
    }

    private float CalculateSwayOffset(float time)
    {
        float radians = time * swayFrequency * Mathf.PI * 2f + swayPhase;
        return Mathf.Sin(radians) * swayDistance;
    }

    /// <summary>
    /// Advances the Shader's procedural flow according to current Heat.
    /// At Still Visual Heat, flow speed becomes zero.
    /// </summary>
    private void UpdateHeatVisual(float deltaTime)
    {
        if (bubbleRenderer == null)
        {
            return;
        }

        float heatMotion = Mathf.InverseLerp(stillVisualHeat, maximumVisualHeat, Heat);
        visualFlowTime += heatMotion * maximumVisualFlowSpeed * deltaTime;

        visualProperties ??= new MaterialPropertyBlock();
        bubbleRenderer.GetPropertyBlock(visualProperties);
        visualProperties.SetFloat(HeatShaderId, Heat);
        visualProperties.SetFloat(StillHeatShaderId, stillVisualHeat);
        visualProperties.SetFloat(MaximumHeatShaderId, maximumVisualHeat);
        visualProperties.SetFloat(FlowTimeShaderId, visualFlowTime);
        bubbleRenderer.SetPropertyBlock(visualProperties);
    }

    /// <summary>Refreshes Shader values immediately whenever Heat changes externally.</summary>
    protected override void OnHeatChanged()
    {
        UpdateHeatVisual(0f);
    }

    /// <summary>Finds the Rigidbody and solid Sphere Collider required by this Bubble.</summary>
    private void FindPhysicsReferences()
    {
        if (bubbleBody == null)
        {
            bubbleBody = GetComponent<Rigidbody>();
        }

        if (bubbleCollider == null)
        {
            bubbleCollider = GetComponent<SphereCollider>();
        }
    }

    /// <summary>Finds the first Renderer on this Bubble or one of its children.</summary>
    private void FindVisualReferences()
    {
        if (bubbleRenderer == null)
        {
            bubbleRenderer = GetComponentInChildren<Renderer>();
        }
    }

    /// <summary>Shows the fall-start height while selected.</summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        center.y = fallStartWorldY;
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireCube(center, new Vector3(2f, 0.02f, 2f));
        Gizmos.DrawLine(transform.position, center);
    }

    private void OnValidate()
    {
        lifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
        inanimateToFlowerDelay = Mathf.Max(0f, inanimateToFlowerDelay);
        upwardSpeed = Mathf.Max(0f, upwardSpeed);
        horizontalSpeed = Mathf.Max(0f, horizontalSpeed);
        flightAcceleration = Mathf.Max(0.01f, flightAcceleration);
        swayDistance = Mathf.Max(0f, swayDistance);
        swayFrequency = Mathf.Max(0f, swayFrequency);
        stillSpeedThreshold = Mathf.Max(0f, stillSpeedThreshold);
        stillPositionTolerance = Mathf.Max(0f, stillPositionTolerance);
        stillVisualHeat = Mathf.Max(0f, stillVisualHeat);
        maximumVisualHeat = Mathf.Max(stillVisualHeat + 0.001f, maximumVisualHeat);
        maximumVisualFlowSpeed = Mathf.Max(0f, maximumVisualFlowSpeed);

        FindPhysicsReferences();
        FindVisualReferences();
    }
}
