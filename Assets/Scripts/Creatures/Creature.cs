using UnityEngine;

/// <summary>
/// Information passed to a creature when the player or another object interacts with it.
/// This keeps individual creatures independent from specific keyboard or mouse buttons.
/// </summary>
public readonly struct CreatureInteraction
{
    /// <summary>The object that initiated the interaction.</summary>
    public GameObject Source { get; }

    /// <summary>The world-space point where the interaction occurred.</summary>
    public Vector3 Point { get; }

    /// <summary>The world-space direction of the interaction.</summary>
    public Vector3 Direction { get; }

    /// <summary>
    /// Creates a description of one interaction.
    /// </summary>
    public CreatureInteraction(GameObject source, Vector3 point, Vector3 direction)
    {
        Source = source;
        Point = point;
        Direction = direction;
    }
}

/// <summary>
/// Minimal base type shared by every interactive creature in the project.
///
/// This class owns common lifecycle, interaction, and shared carry movement.
/// Blooming, movement decisions, feeding, and other specific rules belong in subclasses
/// or separate components rather than being added to this base class.
/// </summary>
[DisallowMultipleComponent]
public abstract class Creature : MonoBehaviour
{
    [Header("Creature Identity")]
    [Tooltip("Editor-facing name used to identify this creature during development. It does not need to be shown to the player.")]
    [SerializeField] private string creatureName = "Unnamed Creature";

    [Header("Runtime")]
    [Tooltip("When disabled, this creature remains in the scene but does not run its creature logic.")]
    [SerializeField] private bool simulationEnabled = true;

    [Header("Shared Parameters")]
    [Tooltip("Heat currently stored by this creature. Kept as a float so small changes over time are not lost.")]
    [Min(0f)]
    [SerializeField] private float heat;

    /// <summary>Editor-facing name of this creature.</summary>
    public string CreatureName => creatureName;

    /// <summary>Whether this creature currently runs its simulation logic.</summary>
    public bool SimulationEnabled => simulationEnabled;

    /// <summary>Heat currently stored by this creature.</summary>
    public float Heat => heat;

    /// <summary>The carrier currently controlling this creature's world position.</summary>
    public PlayerCreatureCarrier Carrier { get; private set; }

    public bool IsCarried => Carrier != null;

    [Header("Carry Physics")]
    [Min(0f)] [SerializeField] private float carryStrength = 45f;
    [Min(0f)] [SerializeField] private float carryDamping = 12f;
    [Min(0f)] [SerializeField] private float maximumCarryAcceleration = 60f;
    [Min(0.1f)] [SerializeField] private float maximumCarryDistance = 8f;

    private Rigidbody movementBody;
    private bool releaseKinematic;
    private bool releaseGravity;
    private RigidbodyConstraints releaseConstraints;
    private CollisionDetectionMode releaseCollisionMode;
    private Quaternion carryRotationOffset;

    /// <summary>Claims movement without pausing the creature's other behavior.</summary>
    public bool TryBeginCarry(PlayerCreatureCarrier carrier, Transform target)
    {
        if (carrier == null || target == null || IsCarried || !isActiveAndEnabled) return false;
        movementBody = GetComponent<Rigidbody>();
        if (movementBody == null)
        {
            // Previously stationary creatures (such as flowers) need a body to be pulled.
            movementBody = gameObject.AddComponent<Rigidbody>();
            movementBody.isKinematic = true;
            movementBody.useGravity = false;
        }
        releaseKinematic = movementBody.isKinematic;
        releaseGravity = movementBody.useGravity;
        releaseConstraints = movementBody.constraints;
        releaseCollisionMode = movementBody.collisionDetectionMode;
        Carrier = carrier;
        carryRotationOffset = Quaternion.Inverse(target.rotation) * movementBody.rotation;
        movementBody.isKinematic = false;
        movementBody.useGravity = false;
        movementBody.constraints &= ~RigidbodyConstraints.FreezePosition;
        movementBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        movementBody.WakeUp();
        return true;
    }

    /// <summary>Called by the carrier once per physics step. Never teleports the body.</summary>
    public bool PullTowards(PlayerCreatureCarrier carrier, Transform target)
    {
        if (Carrier != carrier || !isActiveAndEnabled || movementBody == null || target == null) return false;
        Vector3 error = target.position - movementBody.position;
        if (error.sqrMagnitude > maximumCarryDistance * maximumCarryDistance) return false;
        Vector3 acceleration = error * carryStrength - movementBody.linearVelocity * carryDamping;
        movementBody.AddForce(Vector3.ClampMagnitude(acceleration, maximumCarryAcceleration), ForceMode.Acceleration);

        // Gentle yaw following; preserve upright constraints and let contact resist rotation.
        Vector3 desiredForward = target.rotation * carryRotationOffset * Vector3.forward;
        Vector3 currentForward = movementBody.rotation * Vector3.forward;
        float yawError = Vector3.SignedAngle(Vector3.ProjectOnPlane(currentForward, Vector3.up),
            Vector3.ProjectOnPlane(desiredForward, Vector3.up), Vector3.up) * Mathf.Deg2Rad;
        float yawAcceleration = Mathf.Clamp(yawError * 20f - movementBody.angularVelocity.y * 8f, -30f, 30f);
        movementBody.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);
        return true;
    }

    public void EndCarry(PlayerCreatureCarrier carrier)
    {
        if (Carrier != carrier) return;
        Carrier = null;
        if (movementBody == null) return;
        // Change collision mode before returning a body to kinematic operation.
        movementBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        movementBody.isKinematic = releaseKinematic;
        movementBody.useGravity = releaseGravity;
        movementBody.constraints = releaseConstraints;
        movementBody.collisionDetectionMode = releaseCollisionMode;
        if (!movementBody.isKinematic) movementBody.WakeUp();
    }

    /// <summary>State changes update release behavior without interrupting an active hold.</summary>
    protected void SetMovementPhysics(bool isKinematic, bool useGravity)
    {
        if (movementBody == null) movementBody = GetComponent<Rigidbody>();
        if (movementBody == null) return;
        if (IsCarried)
        {
            releaseKinematic = isKinematic;
            releaseGravity = useGravity;
            return;
        }
        movementBody.isKinematic = isKinematic;
        movementBody.useGravity = useGravity;
    }

    // Existing autonomous movement stays unchanged when free. Carry ownership is checked here.
    protected void MoveCreature(Vector3 displacement)
    {
        if (!IsCarried) transform.position += displacement;
    }

    protected void SetCreatureHorizontalVelocity(Vector3 velocity)
    {
        if (IsCarried) return;
        if (movementBody == null) movementBody = GetComponent<Rigidbody>();
        if (movementBody != null && !movementBody.isKinematic)
            movementBody.linearVelocity = new Vector3(velocity.x, movementBody.linearVelocity.y, velocity.z);
    }

    protected void FaceCreature(Quaternion rotation)
    {
        if (!IsCarried) transform.rotation = rotation;
    }

    protected virtual void OnDisable()
    {
        if (IsCarried) Carrier.Release();
    }

    /// <summary>
    /// Unity initialization entry point.
    /// Calls the subclass hook once when the creature instance is created.
    /// </summary>
    protected virtual void Awake()
    {
        InitializeCreature();
    }

    /// <summary>
    /// Unity frame update entry point.
    /// Passes delta time to the subclass while simulation is enabled.
    /// </summary>
    protected virtual void Update()
    {
        if (!simulationEnabled)
        {
            return;
        }

        TickCreature(Time.deltaTime);
    }

    /// <summary>
    /// Enables or disables this creature's logic without disabling its GameObject.
    /// Useful later for pausing, freezing, or temporarily stabilizing a creature.
    /// </summary>
    public void SetSimulationEnabled(bool value)
    {
        simulationEnabled = value;
    }

    /// <summary>
    /// Gives Heat to this creature.
    /// Fire Sources and future creatures can use this common entry point.
    /// </summary>
    public virtual void AddHeat(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        heat += amount;
        OnHeatChanged();
    }

    /// <summary>
    /// Replaces this creature's stored Heat with an exact value.
    /// Primarily used when one creature transforms into another and transfers its state.
    /// </summary>
    public virtual void SetHeat(float amount)
    {
        heat = Mathf.Max(0f, amount);
        OnHeatChanged();
    }

    /// <summary>
    /// Removes up to the requested amount of Heat and returns the amount actually removed.
    /// Returning the actual amount makes Heat transfer easy to conserve.
    /// </summary>
    protected float SpendHeat(float requestedAmount)
    {
        if (requestedAmount <= 0f || heat <= 0f)
        {
            return 0f;
        }

        float spentAmount = Mathf.Min(requestedAmount, heat);
        heat -= spentAmount;
        OnHeatChanged();

        return spentAmount;
    }

    /// <summary>
    /// Receives a deliberate interaction from the player or another system.
    /// Subclasses override this only when that creature reacts to direct interaction.
    /// </summary>
    public virtual void ReceiveInteraction(CreatureInteraction interaction)
    {
    }

    /// <summary>
    /// Short verb shown in the interaction prompt (for example "Ignite").
    /// Null means left-click does nothing here, so no prompt is shown.
    /// Override together with ReceiveInteraction.
    /// </summary>
    public virtual string InteractionPrompt => null;

    /// <summary>
    /// Extra control hint shown while the player carries this creature, or null for none.
    /// </summary>
    public virtual string CarriedPrompt => null;

    /// <summary>
    /// Receives notification that this creature has met another creature.
    /// Collision or trigger components can call this method later.
    /// </summary>
    public virtual void MeetCreature(Creature other)
    {
    }

    /// <summary>
    /// One-time initialization hook for subclasses.
    /// Use this instead of adding another Awake method whenever possible.
    /// </summary>
    protected virtual void InitializeCreature()
    {
    }

    /// <summary>
    /// Optional hook called whenever this creature gains or spends Heat.
    /// Subclasses can use it to update state without exposing the Heat field.
    /// </summary>
    protected virtual void OnHeatChanged()
    {
    }

    /// <summary>
    /// Per-frame behavior hook implemented by each concrete creature.
    /// </summary>
    protected abstract void TickCreature(float deltaTime);
}
