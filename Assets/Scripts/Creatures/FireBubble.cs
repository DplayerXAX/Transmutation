using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Bubble produced by FireFlower.
/// It floats and cools until it is transformed, bursts, or becomes inanimate.
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

    [Tooltip("Minimum height this Bubble rises before it stops moving upward.")]
    [Min(0f)]
    [SerializeField] private float minimumRiseDistance = 0.75f;

    [Tooltip("Maximum height this Bubble rises before it stops moving upward.")]
    [Min(0f)]
    [SerializeField] private float maximumRiseDistance = 1.75f;

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

    [Header("Cooling and Interaction")]
    [Tooltip("Heat lost every second while this Bubble is floating.")]
    [Min(0f)]
    [SerializeField] private float coolingPerSecond = 1f;

    [Tooltip("Lowest Heat at which interaction transforms this Bubble into a Fire Source.")]
    [SerializeField] private float interactionMinimumHeat = 13f;

    [Tooltip("Highest Heat at which interaction transforms this Bubble into a Fire Source. Above this, it bursts.")]
    [SerializeField] private float interactionMaximumHeat = 17f;

    [Tooltip("Fire Source created by a successful interaction.")]
    [SerializeField] private FireSource fireSourcePrefab;

    [Header("Hot Interaction Burst")]
    [Tooltip("Heat lost per metre when a too-hot Bubble bursts.")]
    [Min(0.001f)]
    [SerializeField] private float burstHeatDropPerMetre = 3f;

    [Tooltip("Physics layers that can contain creatures receiving burst Heat.")]
    [SerializeField] private LayerMask burstReceiverLayers = ~0;

    [Header("Visual Heat")]
    [Tooltip("Renderer using the Fire Bubble Shader. The first child Renderer is used when left empty.")]
    [SerializeField] private Renderer bubbleRenderer;

    [Tooltip("Heat treated as maximum intensity by the Shader.")]
    [Min(13.001f)]
    [SerializeField] private float maximumVisualHeat = 25f;

    [Tooltip("Maximum procedural flow speed at Maximum Visual Heat.")]
    [Min(0f)]
    [SerializeField] private float maximumVisualFlowSpeed = 2f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private BubbleState currentState = BubbleState.Floating;

    [SerializeField] private Rigidbody bubbleBody;
    [SerializeField] private SphereCollider bubbleCollider;

    [Header("Debug")]
    [Tooltip("Print one message only when the player interacts with this Bubble.")]
    [SerializeField] private bool logPlayerInteraction = true;

    private readonly HashSet<Creature> uniqueBurstReceivers = new HashSet<Creature>();
    private Vector3 swayDirection;
    private float swayPhase;
    private float previousSwayOffset;
    private float elapsedTime;
    private float targetRiseDistance;
    private float risenDistance;
    private Vector3 driftVelocity;
    private float visualFlowTime;
    private MaterialPropertyBlock visualProperties;
    private static readonly int HeatShaderId = Shader.PropertyToID("_Heat");
    private static readonly int StillHeatShaderId = Shader.PropertyToID("_StillHeat");
    private static readonly int MaximumHeatShaderId = Shader.PropertyToID("_MaximumHeat");
    private static readonly int FlowTimeShaderId = Shader.PropertyToID("_FlowTime");

    /// <summary>Whether this Bubble has reached its lower Heat bound and can be eaten later.</summary>
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

        float minimumHeight = Mathf.Min(minimumRiseDistance, maximumRiseDistance);
        float maximumHeight = Mathf.Max(minimumRiseDistance, maximumRiseDistance);
        targetRiseDistance = Random.Range(minimumHeight, maximumHeight);

        float minimumSpeed = Mathf.Min(minimumInitialDriftSpeed, maximumInitialDriftSpeed);
        float maximumSpeed = Mathf.Max(minimumInitialDriftSpeed, maximumInitialDriftSpeed);
        driftVelocity = windDirection * Random.Range(minimumSpeed, maximumSpeed);

        UpdateHeatVisual(0f);
    }

    /// <summary>Updates movement and cooling only while the Bubble remains alive and floating.</summary>
    protected override void TickCreature(float deltaTime)
    {
        if (currentState != BubbleState.Floating)
        {
            return;
        }

        elapsedTime += deltaTime;

        FloatUpward(deltaTime);
        ApplyInitialDrift(deltaTime);
        ApplySway();
        RotateSlowly(deltaTime);
        CoolDown(deltaTime);
        UpdateHeatVisual(deltaTime);
    }

    /// <summary>Responds to the player's centre-screen interaction ray.</summary>
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (currentState != BubbleState.Floating)
        {
            LogPlayerInteraction($"Inanimate -> no effect (Heat {Heat:0.00}).");
            return;
        }

        if (Heat > interactionMaximumHeat)
        {
            LogPlayerInteraction($"Too hot -> burst (Heat {Heat:0.00}).");
            BurstAndReleaseHeat();
        }
        else if (Heat >= interactionMinimumHeat)
        {
            LogPlayerInteraction($"Correct timing -> Fire Source (Heat {Heat:0.00}).");
            TransformIntoFireSource();
        }
        else
        {
            LogPlayerInteraction($"Too cold -> no effect (Heat {Heat:0.00}).");
        }
    }

    /// <summary>
    /// Removes Heat while floating. At the lower interaction bound, cooling stops and
    /// the Bubble becomes inanimate while preserving that remaining Heat.
    /// </summary>
    private void CoolDown(float deltaTime)
    {
        float heatAboveLowerBound = Heat - interactionMinimumHeat;

        if (heatAboveLowerBound <= 0f)
        {
            BecomeInanimate();
            return;
        }

        float coolingThisFrame = Mathf.Min(coolingPerSecond * deltaTime, heatAboveLowerBound);
        SpendHeat(coolingThisFrame);

        if (Heat <= interactionMinimumHeat)
        {
            // Remove any tiny floating-point remainder without cooling below the bound.
            SetHeat(interactionMinimumHeat);
            BecomeInanimate();
        }
    }

    /// <summary>Replaces this Bubble with a falling Fire Source and transfers all remaining Heat.</summary>
    private void TransformIntoFireSource()
    {
        if (fireSourcePrefab == null)
        {
            Debug.LogWarning($"{name} cannot transform because no Fire Source prefab is assigned.", this);
            return;
        }

        float transferredHeat = SpendHeat(Heat);
        FireSource fireSource = Instantiate(fireSourcePrefab, transform.position, transform.rotation);

        fireSource.SetHeat(transferredHeat);
        fireSource.BeginFalling(driftVelocity);

        Destroy(gameObject);
    }

    /// <summary>
    /// Distributes all remaining Heat once using linear distance falloff, then destroys the Bubble.
    /// Every creature independently receives Heat; receivers do not divide it.
    /// </summary>
    private void BurstAndReleaseHeat()
    {
        float burstHeat = SpendHeat(Heat);
        float burstRadius = burstHeat / Mathf.Max(0.001f, burstHeatDropPerMetre);
        Collider[] overlaps = Physics.OverlapSphere(
            transform.position,
            burstRadius,
            burstReceiverLayers,
            QueryTriggerInteraction.Collide
        );

        uniqueBurstReceivers.Clear();

        foreach (Collider overlap in overlaps)
        {
            Creature creature = overlap.GetComponentInParent<Creature>();

            if (creature == null || creature == this || !uniqueBurstReceivers.Add(creature))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, creature.transform.position);
            float receivedHeat = Mathf.Max(0f, burstHeat - distance * burstHeatDropPerMetre);

            creature.AddHeat(receivedHeat);
        }

        Destroy(gameObject);
    }

    /// <summary>Stops Bubble behavior and hands movement to gravity for the future Fire Eater path.</summary>
    private void BecomeInanimate()
    {
        ChangeState(BubbleState.Inanimate);

        bubbleBody.isKinematic = false;
        bubbleBody.useGravity = true;
        bubbleBody.linearVelocity = driftVelocity;
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

    private void FloatUpward(float deltaTime)
    {
        float remainingRise = targetRiseDistance - risenDistance;

        if (remainingRise <= 0f)
        {
            return;
        }

        float riseThisFrame = Mathf.Min(upwardSpeed * deltaTime, remainingRise);
        transform.position += Vector3.up * riseThisFrame;
        risenDistance += riseThisFrame;
    }

    private void ApplyInitialDrift(float deltaTime)
    {
        transform.position += driftVelocity * deltaTime;
        driftVelocity *= Mathf.Exp(-driftDamping * deltaTime);
    }

    private void ApplySway()
    {
        float currentSwayOffset = CalculateSwayOffset(elapsedTime);
        float swayDelta = currentSwayOffset - previousSwayOffset;

        transform.position += swayDirection * swayDelta;
        previousSwayOffset = currentSwayOffset;
    }

    private float CalculateSwayOffset(float time)
    {
        float radians = time * swayFrequency * Mathf.PI * 2f + swayPhase;
        return Mathf.Sin(radians) * swayDistance;
    }

    private void RotateSlowly(float deltaTime)
    {
        transform.Rotate(Vector3.up, rotationSpeed * deltaTime, Space.World);
    }

    /// <summary>
    /// Advances the Shader's procedural flow according to current Heat.
    /// At the lower interaction threshold (13 by default), flow speed becomes zero.
    /// </summary>
    private void UpdateHeatVisual(float deltaTime)
    {
        if (bubbleRenderer == null)
        {
            return;
        }

        float heatMotion = Mathf.InverseLerp(interactionMinimumHeat, maximumVisualHeat, Heat);
        visualFlowTime += heatMotion * maximumVisualFlowSpeed * deltaTime;

        visualProperties ??= new MaterialPropertyBlock();
        bubbleRenderer.GetPropertyBlock(visualProperties);
        visualProperties.SetFloat(HeatShaderId, Heat);
        visualProperties.SetFloat(StillHeatShaderId, interactionMinimumHeat);
        visualProperties.SetFloat(MaximumHeatShaderId, maximumVisualHeat);
        visualProperties.SetFloat(FlowTimeShaderId, visualFlowTime);
        bubbleRenderer.SetPropertyBlock(visualProperties);
    }

    /// <summary>Refreshes Shader values immediately whenever Heat changes externally.</summary>
    protected override void OnHeatChanged()
    {
        UpdateHeatVisual(0f);
    }

    /// <summary>Prints only the result of a deliberate player interaction.</summary>
    private void LogPlayerInteraction(string result)
    {
        if (logPlayerInteraction)
        {
            Debug.Log($"[FireBubble] Player interaction: {result}", this);
        }
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

    /// <summary>Shows the current too-hot burst radius while the Bubble is selected.</summary>
    private void OnDrawGizmosSelected()
    {
        float burstRadius = Heat / Mathf.Max(0.001f, burstHeatDropPerMetre);

        Gizmos.color = new Color(1f, 0.15f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, burstRadius);
    }

    private void OnValidate()
    {
        coolingPerSecond = Mathf.Max(0f, coolingPerSecond);
        minimumRiseDistance = Mathf.Max(0f, minimumRiseDistance);
        maximumRiseDistance = Mathf.Max(0f, maximumRiseDistance);
        interactionMinimumHeat = Mathf.Max(0f, interactionMinimumHeat);
        interactionMaximumHeat = Mathf.Max(interactionMinimumHeat, interactionMaximumHeat);
        burstHeatDropPerMetre = Mathf.Max(0.001f, burstHeatDropPerMetre);
        maximumVisualHeat = Mathf.Max(interactionMinimumHeat + 0.001f, maximumVisualHeat);
        maximumVisualFlowSpeed = Mathf.Max(0f, maximumVisualFlowSpeed);

        FindPhysicsReferences();
        FindVisualReferences();
    }
}
