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
/// This class owns common lifecycle, interaction, shared carry movement, diet, and feeding handoff.
/// Blooming, movement decisions, nutrition effects, and other specific rules belong in subclasses
/// or separate components rather than being added to this base class.
/// </summary>
[DisallowMultipleComponent]
public abstract class Creature : MonoBehaviour
{
/// <summary>Lets collision consumers discover newly spawned or reactivated creatures immediately.</summary>
public static event System.Action<Creature> Activated;
protected virtual void OnEnable() => Activated?.Invoke(this);

/// <summary>Shared food categories. Combine tags; Everything also covers future categories.</summary>
[System.Flags]
public enum FoodTag
{
    None = 0,
    Meat = 1,
    Plant = 2,
    Matter = 4,
    Everything = -1
}

    [Header("Creature Identity")]
    [Tooltip("Editor-facing name used to identify this creature during development. It does not need to be shown to the player.")]
    [SerializeField] private string creatureName = "Unnamed Creature";

    [Header("Food and Diet")]
    [Tooltip("What this creature counts as when another creature checks its diet. Multiple tags can be selected.")]
    [SerializeField] private FoodTag foodTags = FoodTag.None;

    [Tooltip("Food tags this creature accepts. Everything includes untagged creatures, but still respects self and same-type exclusions.")]
    [SerializeField] private FoodTag dietTags = FoodTag.None;

    [Tooltip("Allows eating another creature with the same concrete script class, provided its food tags also match the diet.")]
    [SerializeField] private bool canEatSameType = false;

    [Header("Runtime")]
    [Tooltip("When disabled, this creature remains in the scene but does not run its creature logic.")]
    [SerializeField] private bool simulationEnabled = true;

    [Header("Lifespan")]
    [Tooltip("Remaining lifetime. Counts down only while creature simulation is enabled.")]
    [Min(0f)] [SerializeField] private float age = 40f;
    [Tooltip("Lifetime spent per second. Zero means this creature does not age automatically.")]
    [Min(0f)] [SerializeField] private float ageRate = 0f;

    private bool isDead;
    private bool consumptionPending;

    public float Age => age;
    public float AgeRate => ageRate;
    public bool IsDead => isDead;
    public bool IsConsumptionPending => consumptionPending;

    /// <summary>
    /// Raised once after death is recorded, while the host still exists and before subclass remains.
    /// Listeners react independently; a failing listener cannot interrupt other death behavior.
    /// </summary>
    public event System.Action<Creature> Died;

    [Header("Shared Parameters")]
    [Tooltip("Heat currently stored by this creature. Kept as a float so small changes over time are not lost.")]
    [Min(0f)]
    [SerializeField] private float heat;

    /// <summary>Editor-facing name of this creature.</summary>
    public string CreatureName => creatureName;

    /// <summary>Food categories this creature provides.</summary>
    public FoodTag FoodTags => foodTags;

    /// <summary>Food categories this creature accepts.</summary>
    public FoodTag DietTags => dietTags;

    /// <summary>Whether the diet may include other instances of this concrete creature class.</summary>
    public bool CanEatSameType => canEatSameType;

    /// <summary>
    /// Checks shared dietary compatibility only. Individual eating behaviors must also check
    /// their own conditions, such as readiness, distance, cooldowns, or the target's state.
    /// </summary>
    public bool CanEat(Creature target)
    {
        if (target == null || target == this)
        {
            return false;
        }

        if (!canEatSameType && target.GetType() == GetType())
        {
            return false;
        }

        if (dietTags == FoodTag.Everything)
        {
            return true;
        }

        return (dietTags & target.foodTags) != FoodTag.None;
    }

    /// <summary>For things spawned at runtime, which have no inspector to set the name.</summary>
    protected void SetCreatureName(string value) => creatureName = value;

    /// <summary>For procedural creature placeholders; authored prefabs use Inspector food fields.</summary>
    protected void SetFoodAndDiet(FoodTag food, FoodTag diet)
    {
        foodTags = food;
        dietTags = diet;
    }

    /// <summary>Stops creature logic. Overrides should call base and guard repeated death.</summary>
    public virtual void Die()
    {
        if (isDead) return;
        isDead = true;
        age = 0f;
        SetSimulationEnabled(false);
        if (Died == null) return;
        foreach (System.Action<Creature> listener in Died.GetInvocationList())
        {
            try { listener(this); }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
        }
    }

    /// <summary>Nutrition or special effects only. The feeding caller owns death and removal.</summary>
    public virtual void Consume(Creature consumer) { }

    /// <summary>Completes an eating event once, even when destruction is deferred by Unity.</summary>
    public virtual bool TryConsumeFood(Creature food)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !SimulationEnabled || IsDead || consumptionPending ||
            food == null || !food.isActiveAndEnabled || food.consumptionPending || !CanEat(food)) return false;
        food.consumptionPending = true;
        CreatureRegistry.Unregister(food);
        food.Consume(this);
        if (food != null) food.Die();
        if (food != null) Destroy(food.gameObject);
        return true;
    }

    /// <summary>Whether this creature currently runs its simulation logic.</summary>
    public bool SimulationEnabled => simulationEnabled && !isDead;

    /// <summary>Heat currently stored by this creature.</summary>
    public float Heat => heat;

    /// <summary>The carrier currently controlling this creature's world position.</summary>
    public PlayerCreatureCarrier Carrier { get; private set; }

    public bool IsCarried => Carrier != null;

    /// <summary>Whether this creature can currently be picked up.</summary>
    public virtual bool CanBeCarried => true;

    /// <summary>Left-click uses this creature before the cursor target while held.</summary>
    public virtual bool CanUseWhileCarried => false;
    /// <summary>Alternate pickup action, such as popping instead of carrying.</summary>
    public virtual string PickupActionPrompt => null;
    public virtual void ReceivePickupAttempt(PlayerCreatureCarrier carrier) { }
    public bool IsBubbleCaptured => TryGetComponent<BubbleCapture>(out var capture) && capture.IsCaptured;

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
        if (carrier == null || target == null || IsCarried || IsBubbleCaptured || !isActiveAndEnabled || !CanBeCarried) return false;
        // Silk must release its movement claim before carry physics takes ownership.
        if (TryGetComponent<WebCapture>(out WebCapture webCapture)) webCapture.Release();
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
        if (TryGetComponent<BubbleCapture>(out var capture) && capture.IsCaptured)
        {
            capture.SetReleasePhysics(movementBody, isKinematic, useGravity);
            return;
        }
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
        if (!IsCarried && !IsBubbleCaptured) transform.position += displacement;
    }

    protected void SetCreatureHorizontalVelocity(Vector3 velocity)
    {
        if (IsCarried || IsBubbleCaptured) return;
        if (movementBody == null) movementBody = GetComponent<Rigidbody>();
        if (movementBody != null && !movementBody.isKinematic)
            movementBody.linearVelocity = new Vector3(velocity.x, movementBody.linearVelocity.y, velocity.z);
    }

    /// <summary>Full world-space flight velocity. Carry movement always retains ownership.</summary>
    protected void SetCreatureVelocity(Vector3 velocity)
    {
        if (IsCarried || IsBubbleCaptured) return;
        if (movementBody == null) movementBody = GetComponent<Rigidbody>();
        if (movementBody != null && !movementBody.isKinematic)
            movementBody.linearVelocity = velocity;
    }

    protected void FaceCreature(Quaternion rotation)
    {
        if (!IsCarried && !IsBubbleCaptured) transform.rotation = rotation;
    }

    protected virtual void OnDisable()
    {
        CreatureRegistry.Unregister(this);
        if (IsCarried) Carrier.Release();
        if (TryGetComponent<BubbleCapture>(out var capture) && capture.IsCaptured) capture.Release();
    }

    /// <summary>
    /// Unity initialization entry point.
    /// Calls the subclass hook once when the creature instance is created.
    /// </summary>
    protected virtual void Awake()
    {
        InitializeCreature();
    }

    protected virtual void OnEnable()
    {
        if (Application.isPlaying) CreatureRegistry.Register(this);
    }

    /// <summary>
    /// Unity frame update entry point.
    /// Passes delta time to the subclass while simulation is enabled.
    /// </summary>
    protected virtual void Update()
    {
        if (!SimulationEnabled)
        {
            return;
        }

AdvanceAge(Time.deltaTime);
if (simulationEnabled && !isDead) TickCreature(Time.deltaTime);

// Affection fades slowly once the petting stops.
if (!IsPetted && Affection > 0f)
    Affection = Mathf.Max(0f, Affection - Time.deltaTime * affectionFade);
}

/// <summary>Shared lifespan advancement, separate from subclass movement/timers.</summary>
protected void AdvanceAge(float deltaTime)
{
    if (!simulationEnabled || isDead || deltaTime <= 0f) return;
    age = Mathf.Max(0f, age - Mathf.Max(0f, ageRate) * deltaTime);
    if (age <= 0f) Die();
    }

    /// <summary>
    /// Enables or disables this creature's logic without disabling its GameObject.
    /// Useful later for pausing, freezing, or temporarily stabilizing a creature.
    /// </summary>
    public void SetSimulationEnabled(bool value)
    {
        simulationEnabled = value && !isDead;
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

    // ---------------- Petting (the player's left hand) ----------------

    [Header("Petting")]
    [Tooltip("Affection gained per second of petting (0..1 scale).")]
    [Min(0f)] [SerializeField] private float affectionGain = 0.35f;
    [Tooltip("Affection lost per second after petting stops.")]
    [Min(0f)] [SerializeField] private float affectionFade = 0.05f;

    /// <summary>The player's hand is stroking this creature right now.</summary>
    public bool IsPetted { get; private set; }
    /// <summary>0..1: grows while petted and slowly fades. Behaviours can read it (follow, calm down, glow...).</summary>
    public float Affection { get; private set; }
    /// <summary>Where the hand touches the creature, and the surface direction there.</summary>
    public Vector3 PetPoint { get; private set; }
    public Vector3 PetNormal { get; private set; } = Vector3.up;

    /// <summary>Called by the player's petting hand when it starts stroking this creature.</summary>
    public void BeginPet(Vector3 point, Vector3 normal)
    {
        IsPetted = true;
        PetPoint = point;
        PetNormal = normal;
        OnPetStart();
    }

    /// <summary>Called every frame while the hand keeps stroking.</summary>
    public void ContinuePet(Vector3 point, Vector3 normal, float deltaTime)
    {
        if (!IsPetted) return;
        PetPoint = point;
        PetNormal = normal;
        Affection = Mathf.Min(1f, Affection + deltaTime * affectionGain);
        OnPet(deltaTime);
    }

    /// <summary>Called when the hand lets go.</summary>
    public void EndPet()
    {
        if (!IsPetted) return;
        IsPetted = false;
        OnPetEnd();
    }

    /// <summary>Hooks for each creature's own reaction to being petted (all optional).</summary>
    protected virtual void OnPetStart() { }
    protected virtual void OnPet(float deltaTime) { }
    protected virtual void OnPetEnd() { }

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
