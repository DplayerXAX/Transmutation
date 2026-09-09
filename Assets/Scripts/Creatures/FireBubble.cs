using UnityEngine;

/// <summary>
/// A Bubble produced by FireFlower.
/// It floats for a fixed lifetime, then becomes inanimate and can be eaten by a Fire Eater.
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
    [SerializeField] private float swayDistance = 1.25f;

    [Tooltip("Number of complete side-to-side sway cycles per second.")]
    [Min(0f)]
    [SerializeField] private float swayFrequency = 0.22f;

    [SerializeField] private float rotationSpeed = 20f;

    [Header("Wind Drift")]
    [Tooltip("Minimum initial horizontal wind speed.")]
    [Min(0f)]
    [SerializeField] private float minimumInitialDriftSpeed = 0.8f;

    [Tooltip("Maximum initial horizontal wind speed.")]
    [Min(0f)]
    [SerializeField] private float maximumInitialDriftSpeed = 1.8f;

    [Tooltip("How quickly the initial wind loses strength. Lower values carry the Bubble farther.")]
    [Min(0f)]
    [SerializeField] private float driftDamping = 0.45f;

    [Header("Lifetime")]
    [Tooltip("Seconds of floating before this Bubble becomes inanimate. Independent of Heat.")]
    [Min(0f)]
    [SerializeField] private float lifetimeSeconds = 10f;

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
    private bool descending;
    private Vector3 driftVelocity;
    private float visualFlowTime;
    private MaterialPropertyBlock visualProperties;
    private static readonly int HeatShaderId = Shader.PropertyToID("_Heat");
    private static readonly int StillHeatShaderId = Shader.PropertyToID("_StillHeat");
    private static readonly int MaximumHeatShaderId = Shader.PropertyToID("_MaximumHeat");
    private static readonly int FlowTimeShaderId = Shader.PropertyToID("_FlowTime");

    /// <summary>Whether this Bubble's floating lifetime has ended and it can be eaten later.</summary>
    public bool IsInanimate => currentState == BubbleState.Inanimate;

    /// <summary>Sets up random motion and keeps the floating Bubble outside gravity simulation.</summary>
    protected override void InitializeCreature()
    {
        FindPhysicsReferences();
        FindVisualReferences();

        bubbleCollider.isTrigger = false;
        bubbleBody.isKinematic = true;
        bubbleBody.useGravity = false;

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

        float minimumSpeed = Mathf.Min(minimumInitialDriftSpeed, maximumInitialDriftSpeed);
        float maximumSpeed = Mathf.Max(minimumInitialDriftSpeed, maximumInitialDriftSpeed);
        driftVelocity = windDirection * Random.Range(minimumSpeed, maximumSpeed);

        UpdateHeatVisual(0f);
    }

    /// <summary>Updates movement and lifetime only while the Bubble remains floating.</summary>
    protected override void TickCreature(float deltaTime)
    {
        if (currentState != BubbleState.Floating)
        {
            return;
        }

        elapsedTime += deltaTime;

        FloatUpward(deltaTime);
        if (!descending)
        {
            ApplyInitialDrift(deltaTime);
            ApplySway();
        }

        RotateSlowly(deltaTime);

        if (elapsedTime >= lifetimeSeconds)
        {
            BecomeInanimate();
            return;
        }

        UpdateHeatVisual(deltaTime);
    }

    /// <summary>Stops Bubble behavior and hands movement to gravity for the Fire Eater path.</summary>
    private void BecomeInanimate()
    {
        ChangeState(BubbleState.Inanimate);
        StartFalling();
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
        SetMovementPhysics(false, true);
        if (!IsCarried && bubbleBody != null)
        {
            bubbleBody.linearVelocity = driftVelocity;
        }
    }

    private void FloatUpward(float deltaTime)
    {
        if (descending)
        {
            return;
        }

        float currentY = transform.position.y;
        if (currentY >= fallStartWorldY)
        {
            StartFalling();
            return;
        }

        float riseThisFrame = Mathf.Min(upwardSpeed * deltaTime, fallStartWorldY - currentY);
        if (riseThisFrame <= 0f)
        {
            StartFalling();
            return;
        }

        MoveCreature(Vector3.up * riseThisFrame);

        if (transform.position.y >= fallStartWorldY)
        {
            // Snap to the ceiling so we do not overshoot when frame spikes are large.
            Vector3 position = transform.position;
            position.y = fallStartWorldY;
            if (!IsCarried)
            {
                transform.position = position;
            }

            StartFalling();
        }
    }

    private void ApplyInitialDrift(float deltaTime)
    {
        MoveCreature(driftVelocity * deltaTime);
        driftVelocity *= Mathf.Exp(-driftDamping * deltaTime);
    }

    private void ApplySway()
    {
        float currentSwayOffset = CalculateSwayOffset(elapsedTime);
        float swayDelta = currentSwayOffset - previousSwayOffset;

        MoveCreature(swayDirection * swayDelta);
        previousSwayOffset = currentSwayOffset;
    }

    private float CalculateSwayOffset(float time)
    {
        float radians = time * swayFrequency * Mathf.PI * 2f + swayPhase;
        return Mathf.Sin(radians) * swayDistance;
    }

    private void RotateSlowly(float deltaTime)
    {
        FaceCreature(Quaternion.AngleAxis(rotationSpeed * deltaTime, Vector3.up) * transform.rotation);
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
        upwardSpeed = Mathf.Max(0f, upwardSpeed);
        stillVisualHeat = Mathf.Max(0f, stillVisualHeat);
        maximumVisualHeat = Mathf.Max(stillVisualHeat + 0.001f, maximumVisualHeat);
        maximumVisualFlowSpeed = Mathf.Max(0f, maximumVisualFlowSpeed);

        FindPhysicsReferences();
        FindVisualReferences();
    }
}
