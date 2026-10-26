using UnityEngine;

public enum CreaturePolarity { None, Light, Dark, Random }
public enum CreatureDeathCause { None, Direct, Damage, Age, Consumed }

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

    [Header("Health and Polarity")]
    [Min(0.01f)] [SerializeField] private float maxHealth = 100f;
    [SerializeField] private CreaturePolarity initialPolarity = CreaturePolarity.Random;
    [SerializeField, HideInInspector] private float health;
    [SerializeField, HideInInspector] private CreaturePolarity polarity;
    [SerializeField, HideInInspector] private int identityOwner;
    [SerializeField, HideInInspector] private CreatureDeathCause deathCause;
    [Tooltip("Optional aim point; otherwise combat uses the physical body center.")]
    [SerializeField] private Transform aimPoint;
    public float MaxHealth => maxHealth;
    public float Health => health;
    public CreaturePolarity Polarity => polarity;
    public bool CanBeAITargeted => Polarity != CreaturePolarity.None;
    /// <summary>Structural or unique creatures ignore incidental harm: projectile, melee and blast damage,
    /// and water-contact ageing. Direct lifecycle calls (Die, SetRemainingAge, consumption) are unaffected.</summary>
    public virtual bool Invulnerable => false;
    public CreatureDeathCause DeathCause => deathCause;
    public Vector3 AimPosition => aimPoint != null ? aimPoint.position :
        (TryGetComponent<Rigidbody>(out var body) ? body.worldCenterOfMass : transform.position);
    public event System.Action<Creature, float, Creature> Damaged;

    /// <summary>Runtime template copies are new individuals, not copies of depleted runtime state.</summary>
    internal void InitializeNewIndividual()
    {
        identityOwner = GetInstanceID();
        isDead = false;
        consumptionPending = false;
        simulationEnabled = true;
        deathCause = CreatureDeathCause.None;
        health = Mathf.Max(0.01f, maxHealth);
        polarity = initialPolarity == CreaturePolarity.Random ?
            (Random.value < 0.5f ? CreaturePolarity.Light : CreaturePolarity.Dark) : initialPolarity;
    }

    internal void SetResolvedPolarity(CreaturePolarity value)
    {
        if (value == CreaturePolarity.Random) throw new System.ArgumentException("Resolved polarity cannot be Random.");
        polarity = value;
    }

    public bool ApplyDamage(float amount, Creature source = null)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || isDead || consumptionPending || Invulnerable) return false;
        float applied = Mathf.Min(health, amount);
        health = Mathf.Max(0f, health - amount);
        if (Damaged != null)
            foreach (System.Action<Creature, float, Creature> listener in Damaged.GetInvocationList())
                try { listener(this, applied, source); }
                catch (System.Exception exception) { Debug.LogException(exception, this); }
        if (health <= 0f && !isDead && !consumptionPending) DieWithCause(CreatureDeathCause.Damage);
        return true;
    }

    private void DieWithCause(CreatureDeathCause cause)
    {
        if (isDead) return;
        deathCause = cause;
        Die(); // Keep virtual dispatch, including legacy squirrel cleanup.
    }

    protected virtual void OnCarryStarted() { }
    protected virtual void OnCaptureStarted() { }
    internal void NotifyCaptureStarted() => OnCaptureStarted();
    protected virtual void OnSimulationChanged() { }

    [Header("Food and Diet")]
    [Tooltip("What this creature counts as when another creature checks its diet. Multiple tags can be selected.")]
    [SerializeField] private FoodTag foodTags = FoodTag.None;

    [Tooltip("Food tags this creature accepts. Everything includes untagged creatures, but still respects self and same-type exclusions.")]
    [SerializeField] private FoodTag dietTags = FoodTag.None;

    [Tooltip("Allows eating another creature with the same concrete script class, provided its food tags also match the diet.")]
    [SerializeField] private bool canEatSameType = false;

    [Tooltip("Total nutrition available from one meal. Shared by nearby eligible birds; does not change dietary compatibility.")]
    [Min(0f)] [SerializeField] private float nutrition = 120f;

    public float Nutrition => Mathf.Max(0f, nutrition);

    [Header("Runtime")]
    [Tooltip("When disabled, this creature remains in the scene but does not run its creature logic.")]
    [SerializeField] private bool simulationEnabled = true;

    [Header("Lifespan")]
    [Tooltip("Remaining lifetime. Counts down only while creature simulation is enabled.")]
    [Min(0f)] [SerializeField] private float age = 40f;
    [Tooltip("Lifetime spent per second. Zero means this creature does not age automatically.")]
    [Min(0f)] [SerializeField] private float ageRate = 0f;

    [SerializeField, HideInInspector] private bool isDead;
    private bool consumptionPending;

    public float Age => age;
    public float AgeRate => ageRate;
    public bool IsDead => isDead;
    public bool IsConsumptionPending => consumptionPending;

    /// <summary>
    /// Raised once after death and ability cleanup, while the original body still exists.
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

        return AcceptsFood(target.foodTags, target.GetType());
    }

    /// <summary>Checks cached food metadata after a feeding callback has removed the original.</summary>
    protected bool AcceptsFood(FoodTag tags, System.Type foodType)
    {
        if (foodType == null || (!canEatSameType && foodType == GetType()))
        {
            return false;
        }

        if (dietTags == FoodTag.Everything)
        {
            return true;
        }

        return (dietTags & tags) != FoodTag.None;
    }

    /// <summary>For things spawned at runtime, which have no inspector to set the name.</summary>
    protected void SetCreatureName(string value) => creatureName = value;

    /// <summary>For procedural creature placeholders; authored prefabs use Inspector food fields.</summary>
    protected void SetFoodAndDiet(FoodTag food, FoodTag diet)
    {
        foodTags = food;
        dietTags = diet;
    }

    /// <summary>Records death once, releases active ownership, and retains the original body.</summary>
    public virtual void Die()
    {
        if (isDead) return;
        if (deathCause == CreatureDeathCause.None) deathCause = CreatureDeathCause.Direct;
        InitializeDeadRemains();
        CreatureThrower.ReleaseFlight(this);
        EndPet();
        if (IsCarried) Carrier.Release();
        if (TryGetComponent<WebCapture>(out var webCapture)) webCapture.Release();
        if (TryGetComponent<BubbleCapture>(out var bubbleCapture) && bubbleCapture.IsCaptured) bubbleCapture.Release();
        ApplyCorpsePhysics();
        OnDeath();
        if (Died == null) return;
        foreach (System.Action<Creature> listener in Died.GetInvocationList())
        {
            try { listener(this); }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
        }
    }

    /// <summary>Initializes existing remains without another death event or death effects.</summary>
    protected void InitializeDeadRemains()
    {
        isDead = true;
        health = 0f;
        age = 0f;
        simulationEnabled = false;
    }

    /// <summary>Structural creatures retain their stationary body after death.</summary>
    protected virtual bool KeepCorpseStationary => false;

    private void ApplyCorpsePhysics()
    {
        if (KeepCorpseStationary) return;
        SetMovementPhysics(false, true);
        if (movementBody == null) return;
        movementBody.constraints &= ~RigidbodyConstraints.FreezePosition;
        movementBody.WakeUp();
    }

    /// <summary>Stop abilities and release owned occupants here; never remove the body.</summary>
    protected virtual void OnDeath() { }

    /// <summary>
    /// Runs once when the shared lifespan expires, after Die(), ability cleanup, and Died listeners.
    /// Override for age-specific remains. Direct death and consumption never invoke this hook.
    /// </summary>
    protected virtual void OnAgeDeath() { }

    /// <summary>Nutrition or special effects only. The feeding caller owns death and removal.</summary>
    public virtual void Consume(Creature consumer) { }

    /// <summary>Ownership states may protect a creature from being removed as food.</summary>
    public virtual bool CanBeConsumed => true;

    /// <summary>Completes an eating event once, even when destruction is deferred by Unity.</summary>
    public virtual bool TryConsumeFood(Creature food)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !SimulationEnabled || IsDead || consumptionPending ||
            food == null || !food.isActiveAndEnabled || !food.CanBeConsumed || food.consumptionPending || !CanEat(food)) return false;
        return food.CompleteConsumption(this);
    }

    /// <summary>Completes a species-approved physical meal without imposing ordinary diet rules.</summary>
    internal bool CompleteConsumption(Creature consumer)
    {
        if (consumptionPending || !CanBeConsumed) return false;
        consumptionPending = true;
        CreatureRegistry.Unregister(this);
        try { Consume(consumer); }
        finally
        {
            if (this != null && !isDead) DieWithCause(CreatureDeathCause.Consumed);
            if (this != null) Destroy(gameObject);
        }
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

    /// <summary>Held like an item: fixed in view at the right hand, kinematic, without pushing other bodies.</summary>
    public virtual bool HeldInView => false;

    /// <summary>Left-click uses this creature before the cursor target while held.</summary>
    public virtual bool CanUseWhileCarried => false;
    /// <summary>Return true to own this frame's carried RMB input instead of shared putdown/throw.</summary>
    public virtual bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released) => false;
    /// <summary>Release species-specific attachments before throw flight takes movement ownership.</summary>
    public virtual void PrepareForThrow(PlayerCreatureCarrier carrier) { }
    /// <summary>Alternate pickup action, such as popping instead of carrying.</summary>
    public virtual string PickupActionPrompt => null;
    public virtual void ReceivePickupAttempt(PlayerCreatureCarrier carrier) { }
    public bool IsBubbleCaptured => TryGetComponent<BubbleCapture>(out var capture) && capture.IsCaptured;

    private Rigidbody movementBody;
    private bool releaseKinematic;
    private bool releaseGravity;
    private RigidbodyConstraints releaseConstraints;
    private CollisionDetectionMode releaseCollisionMode;

    /// <summary>Claims movement without pausing the creature's other behavior.</summary>
    public bool TryBeginCarry(PlayerCreatureCarrier carrier, Transform target)
    {
        if (carrier == null || target == null || IsCarried || IsBubbleCaptured || !isActiveAndEnabled || !CanBeCarried) return false;
        CreatureThrower.ReleaseFlight(this);
        // Silk must release its movement claim before carry physics takes ownership.
        if (TryGetComponent<WebCapture>(out WebCapture webCapture)) webCapture.Release();
        if (this is LeashBubble balloon) balloon.ReleaseLeash();
        if (this is TentacleCreature tentacle)
        {
            // Release the suspended rig before carry records the creature's normal physics.
            if (TentacleTether.Instance != null) TentacleTether.Instance.DetachForPickup(tentacle);
            else { if (tentacle.Suspended) tentacle.Suspend(false); tentacle.UnpinTerrainArms(); }
            tentacle.ClearGroundHolds();
        }
        movementBody = GetComponent<Rigidbody>();
        if (movementBody == null)
        {
            // Previously stationary creatures (such as flowers) need a body for carry ownership.
            movementBody = gameObject.AddComponent<Rigidbody>();
            movementBody.isKinematic = true;
            movementBody.useGravity = false;
        }
        releaseKinematic = movementBody.isKinematic;
        releaseGravity = movementBody.useGravity;
        releaseConstraints = movementBody.constraints;
        releaseCollisionMode = movementBody.collisionDetectionMode;
        Carrier = carrier;
        movementBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        movementBody.isKinematic = true;
        movementBody.useGravity = false;
        movementBody.constraints &= ~RigidbodyConstraints.FreezePosition;
        OnCarryStarted();
        return true;
    }

    public void EndCarry(PlayerCreatureCarrier carrier)
    {
        if (Carrier != carrier) return;
        Carrier = null;
        if (this is TentacleCreature tentacle && carrier.TryGetComponent<TentacleTether>(out var tether)) tether.ParkBalloons(tentacle);
        if (movementBody == null) return;
        if (this is TentacleCreature && !movementBody.isKinematic)
        {
            movementBody.linearVelocity = Vector3.zero;
            movementBody.angularVelocity = Vector3.zero;
        }
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
        // The owner id survives script reloads, while instantiated copies have a new id.
        if (identityOwner != GetInstanceID())
        {
            identityOwner = GetInstanceID();
            health = isDead ? 0f : Mathf.Max(0.01f, maxHealth);
            polarity = initialPolarity == CreaturePolarity.Random ?
                (Random.value < 0.5f ? CreaturePolarity.Light : CreaturePolarity.Dark) : initialPolarity;
        }
        InitializeCreature();
        // Serialized death survives a script reload; initialization must not restore living physics.
        if (isDead)
        {
            InitializeDeadRemains();
            ApplyCorpsePhysics();
        }
    }

    protected virtual void OnEnable()
    {
        if (Application.isPlaying) CreatureRegistry.Register(this);
        Activated?.Invoke(this);
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
        if (!SimulationEnabled) return;
        TickCreature(Time.deltaTime);

        // Affection fades slowly once the petting stops.
        if (!IsPetted && Affection > 0f)
            Affection = Mathf.Max(0f, Affection - Time.deltaTime * affectionFade);
    }

    /// <summary>Shared lifespan advancement, separate from subclass movement/timers.</summary>
    protected void AdvanceAge(float deltaTime)
    {
        if (!simulationEnabled || isDead || consumptionPending || deltaTime <= 0f) return;
        SetRemainingAge(age - Mathf.Max(0f, ageRate) * deltaTime);
    }

    /// <summary>
    /// Controlled lifetime mutation, including while capture pauses simulation. This has no feeding
    /// or transfer rules; callers own those. Zero follows the same death path as natural ageing.
    /// </summary>
    internal void SetRemainingAge(float value)
    {
        if (isDead || consumptionPending) return;
        age = Mathf.Max(0f, value);
        if (age > 0f) return;
        DieWithCause(CreatureDeathCause.Age);
        // A death listener may consume or remove the original. Eating owns its removal in that case.
        if (this != null && isDead && !consumptionPending) OnAgeDeath();
    }

    /// <summary>
    /// Enables or disables this creature's logic without disabling its GameObject.
    /// Useful later for pausing, freezing, or temporarily stabilizing a creature.
    /// </summary>
    public void SetSimulationEnabled(bool value)
    {
        simulationEnabled = value && !isDead;
        OnSimulationChanged();
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
        if (IsDead) return;
        IsPetted = true;
        PetPoint = point;
        PetNormal = normal;
        OnPetStart();
    }

    /// <summary>Called every frame while the hand keeps stroking.</summary>
    public void ContinuePet(Vector3 point, Vector3 normal, float deltaTime)
    {
        if (!IsPetted || IsDead) return;
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
