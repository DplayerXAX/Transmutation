using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A physics bubble with one occupant. Separate from the FireFlower's FireBubble.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class TrappingBubble : LeashBubble
{
    [Header("Motion")]
    [Min(0f)] [SerializeField] private float upwardSpeed = 0.25f;
    [Min(0.01f)] [SerializeField] private float launchDecaySeconds = 0.35f;
    [SerializeField] private Vector2 riseDuration = new Vector2(12f, 20f);
    [SerializeField] private Vector2 lifetime = new Vector2(45f, 60f);
    [Min(0f)] [SerializeField] private float swayDistance = 0.08f;
    [Min(0f)] [SerializeField] private float swayFrequency = 0.15f;

    [Header("Capture")]
    [Min(0.01f)] [SerializeField] private float initialDiameter = 0.8f;
    [Tooltip("Largest measured model dimension that can be captured, in metres.")]
    [Min(0.1f)] [SerializeField] private float maxCaptureSize = 4f;
    [Min(0f)] [SerializeField] private float fittingPadding = 0.1f;
    [Min(0f)] [SerializeField] private float releaseImmunity = 0.25f;
    [Min(0f)] [SerializeField] private float shooterProtection = 0.25f;
    [Min(0f)] [SerializeField] private float escapeHeight = 4f;

    [Header("Breaking")]
    [Tooltip("For un-leashed bubbles: scenery stops bubbles and other creatures do not pop occupied bubbles. " +
             "Interaction, pickup attempts and escape still pop them; timeout requires Limit Lifetime.")]
    [SerializeField] private bool requireExplicitInteractionToPop;
    [SerializeField] private Transform shell;

    private readonly HashSet<Collider> expansionContacts = new HashSet<Collider>();
    private Rigidbody body;
    private SphereCollider solidCollider;
    private SphereCollider captureTrigger;
    private BubbleFlower flower;
    private GameObject shooter;
    private Vector3 launchVelocity;
    private float launchDecayOverride = -1f;
    private float elapsedTime, motionTime, riseSeconds, lifetimeSeconds, phase;
    private bool popped;
    public BubbleCapture Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;
    public bool IsPopped => popped;
    public float ReleaseImmunity => releaseImmunity;
    public float EscapeHeight => escapeHeight;
    public override float Radius => solidCollider != null ? solidCollider.radius * Mathf.Abs(transform.lossyScale.x) : initialDiameter * 0.5f;
    public float InitialRadius => initialDiameter * 0.5f;
    public float ElapsedTime => elapsedTime;
    public bool RequiresExplicitInteraction { get => requireExplicitInteractionToPop; set => requireExplicitInteractionToPop = value; }
    protected override bool CanLeashBubble(TentacleTether owner) => !popped && (Occupant == null || Occupant.gameObject != owner.BalloonPlayer);
    protected override void OnLeashAttached() => launchVelocity *= 0.25f;
    public override bool CanBeCarried => false;
    public override string PickupActionPrompt => popped || IsLeashed ? null : "Pop bubble";
    public override string InteractionPrompt => !popped && !IsLeashed && IsOccupied ? "Pop bubble" : null;

    protected override void InitializeCreature()
    {
        SetCreatureName("Bubble");
        body = GetComponent<Rigidbody>();
        solidCollider = GetComponent<SphereCollider>();
        body.isKinematic = false;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = 0f;
        solidCollider.isTrigger = false;
        Transform trigger = transform.Find("CaptureTrigger");
        if (trigger == null)
        {
            trigger = new GameObject("CaptureTrigger").transform;
            trigger.SetParent(transform, false);
            trigger.gameObject.AddComponent<SphereCollider>();
        }
        captureTrigger = trigger.GetComponent<SphereCollider>();
        captureTrigger.isTrigger = true;
        if (shell == null) shell = transform.Find("Shell");
        SetRadius(initialDiameter * 0.5f);
        riseSeconds = Random.Range(riseDuration.x, riseDuration.y);
        lifetimeSeconds = Random.Range(lifetime.x, lifetime.y);
        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    public void Launch(BubbleFlower source, GameObject firingPlayer, Vector3 velocity) => Launch(source, firingPlayer, velocity, -1f);

    /// <summary>Launches with its own launch slow-down time (seconds; below 0 = launchDecaySeconds).</summary>
    public void Launch(BubbleFlower source, GameObject firingPlayer, Vector3 velocity, float decaySeconds)
    {
        launchDecayOverride = decaySeconds;
        if (body == null) InitializeCreature();
        flower = source;
        shooter = firingPlayer;
        launchVelocity = velocity;
        body.linearVelocity = velocity + Vector3.up * upwardSpeed;
        if (flower != null) IgnoreColliders(flower.gameObject, float.PositiveInfinity);
        if (shooter != null) IgnoreColliders(shooter, Time.time + shooterProtection);
    }

    protected override void TickCreature(float deltaTime) { }
    private void FixedUpdate() => SimulateStep(Time.fixedDeltaTime);

    /// <summary>Physics-timestep entry point, also used by temporary-scene validation.</summary>
    public void SimulateStep(float deltaTime)
    {
        if (popped || !SimulationEnabled || deltaTime <= 0f) return;
        if (body == null) InitializeCreature();
        motionTime += deltaTime;
        if (!IsLeashed) elapsedTime += deltaTime;
        RestoreExpiredIgnores();
        if (LifetimeLimited && !IsLeashed && elapsedTime >= lifetimeSeconds) { Pop(); return; }
        if (IsOccupied) Occupant.Follow(body.position);
        Vector3 velocity = Vector3.zero;
        if (elapsedTime < riseSeconds)
        {
            float angularSpeed = swayFrequency * Mathf.PI * 2f;
            velocity = launchVelocity * Mathf.Exp(-motionTime / Mathf.Max(0.01f, launchDecayOverride > 0f ? launchDecayOverride : launchDecaySeconds)) + Vector3.up * upwardSpeed;
            velocity += new Vector3(Mathf.Cos(motionTime * angularSpeed + phase), 0f,
                Mathf.Sin(motionTime * angularSpeed + phase)) * (swayDistance * angularSpeed);
        }
        // Unity's contact solver stops this velocity against walls and ceilings.
        velocity = LeashVelocity(velocity, deltaTime);
        body.linearVelocity = velocity;
    }

    private GameObject FindTarget(Collider collider)
    {
        var player = collider.GetComponentInParent<SmoothFirstPersonController>();
        if (player != null) return player.gameObject;
        var creature = collider.GetComponentInParent<Creature>();
        return creature != null ? creature.gameObject : null;
    }

    private bool IgnoreTarget(GameObject target)
    {
        if (target == null || target == gameObject || !target.activeInHierarchy) return true;
        if (IgnoreLeashTarget(target)) return true;
        if (flower != null && target == flower.gameObject) return true;
        if (target == shooter && elapsedTime < shooterProtection) return true;
        Creature creature = target.GetComponent<Creature>();
        return creature != null && (creature is TrappingBubble || creature is SkyWeb || creature.IsCarried ||
            (creature is LeashBubble balloon && balloon.IsLeashed) ||
            creature.IsBubbleCaptured || !creature.isActiveAndEnabled);
    }

    public bool TryCapture(GameObject target)
    {
        if (popped || IsOccupied || !SimulationEnabled || IgnoreTarget(target)) return false;
        if (target.GetComponent<Creature>() == null && target.GetComponent<SmoothFirstPersonController>() == null) return false;
        BubbleCapture capture = target.GetComponent<BubbleCapture>();
        if (capture != null && !capture.CanCapture) return false;
        if (!CreatureBounds.TryMeasure(target.transform, out Bounds bounds) || CreatureBounds.LargestSide(bounds) > maxCaptureSize) return false;
        if (capture == null) capture = target.AddComponent<BubbleCapture>();
        if (body == null) InitializeCreature();
        Vector3 previousPosition = body.position;
        // Move the shell to the occupant, rather than teleporting the occupant to the approaching projectile.
        body.position = bounds.center;
        transform.position = bounds.center;
        if (!capture.Attach(this)) { body.position = previousPosition; transform.position = previousPosition; return false; }
        Occupant = capture;
        launchVelocity = Vector3.zero;
        SetRadius(Mathf.Max(initialDiameter * 0.5f, bounds.extents.magnitude + fittingPadding));
        // Newly enlarged colliders do not count as another creature arriving or a scenery impact.
        expansionContacts.Clear();
        Collider[] nearby = new Collider[32];
        var physics = gameObject.scene.GetPhysicsScene();
        int count;
        while ((count = physics.OverlapSphere(body.position, Radius + 0.025f, nearby, ~0, QueryTriggerInteraction.Ignore)) == nearby.Length)
            nearby = new Collider[nearby.Length * 2];
        for (int i = 0; i < count; i++) expansionContacts.Add(nearby[i]);
        var carrier = target.GetComponent<PlayerCreatureCarrier>();
        if (carrier != null && carrier.CarriedCreature != null)
            IgnoreColliders(carrier.CarriedCreature.gameObject, float.PositiveInfinity, carrier.CarriedCreature);
        return true;
    }

    private void SetRadius(float worldRadius)
    {
        solidCollider.radius = worldRadius / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
        captureTrigger.radius = solidCollider.radius + 0.025f;
        if (shell != null) shell.localScale = Vector3.one * solidCollider.radius * 2f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return;
        HandleContact(other, false);
    }
    private void OnCollisionEnter(Collision collision) => HandleContact(collision.collider, true);

    // A player who touched the bubble while still immune (just fired it, or just escaped another bubble)
    // gets no second Enter event; keep trying while they stay in contact.
    private void OnTriggerStay(Collider other)
    {
        if (other.isTrigger || popped || IsOccupied || !SimulationEnabled) return;
        var player = other.GetComponentInParent<SmoothFirstPersonController>();
        if (player != null && !IgnoreTarget(player.gameObject)) TryCapture(player.gameObject);
    }

    private void HandleContact(Collider other, bool solidContact)
    {
        if (popped || !SimulationEnabled || other == null || expansionContacts.Contains(other)) return;
        GameObject target = FindTarget(other);
        if (target != null)
        {
            Creature creature = target.GetComponent<Creature>();
            if (creature != null && creature.IsCarried)
                IgnoreColliders(target, float.PositiveInfinity, creature);
            if (IgnoreTarget(target)) return;
            if (!IsOccupied) TryCapture(target);
            else if (target != Occupant.gameObject && !requireExplicitInteractionToPop) Pop();
        }
        else if (solidContact && !requireExplicitInteractionToPop && !IsLeashed) Pop();
    }

    private void OnTriggerExit(Collider other) => expansionContacts.Remove(other);

    public override void ReceivePickupAttempt(PlayerCreatureCarrier carrier) => Pop();
    public override void ReceiveInteraction(CreatureInteraction interaction) { if (IsOccupied) Pop(); }
    public override void MeetCreature(Creature other)
    {
        if (popped || !IsOccupied || other == null || other.gameObject == Occupant.gameObject || IgnoreTarget(other.gameObject)) return;
        Pop(); // Deliberate creature interaction works with either toggle setting.
    }

    public void Pop(bool launchPlayer = false)
    {
        if (popped || IsDead) return;
        if (IsLeashed)
        {
            // Space still frees an occupant, but leaves the protected shell on the octopus's arm.
            if (launchPlayer && Occupant != null) Occupant.Release(true);
            return;
        }
        popped = true;
        ReleaseLeash();
        if (solidCollider != null) solidCollider.enabled = false;
        if (captureTrigger != null) captureTrigger.enabled = false;
        if (Occupant != null)
        {
            Occupant.Follow(body.position);
            Occupant.Release(launchPlayer);
        }
        RestoreExpiredIgnores(true);
        if (Application.isPlaying) Destroy(gameObject);
        else gameObject.SetActive(false);
    }

    public void ForgetCapture(BubbleCapture capture)
    {
        if (Occupant != capture) return;
        Occupant = null;
        if (!IsDead) Pop();
    }

    protected override void OnDeath()
    {
        base.OnDeath();
        if (Occupant != null) Occupant.Release();
        RestoreExpiredIgnores(true);
    }
    protected override void OnDisable() { base.OnDisable(); Pop(); }
    private void OnDestroy() => Pop();

    private void OnValidate()
    {
        riseDuration.x = Mathf.Max(0f, riseDuration.x); riseDuration.y = Mathf.Max(riseDuration.x, riseDuration.y);
        lifetime.x = Mathf.Max(0.01f, lifetime.x); lifetime.y = Mathf.Max(lifetime.x, lifetime.y);
    }
}
