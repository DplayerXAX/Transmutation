using System.Collections.Generic;
using UnityEngine;

/// <summary>A carryable creature that temporarily anchors and expands into a spring platform.</summary>
[RequireComponent(typeof(Rigidbody), typeof(BouncySurface))]
public sealed class BounceShroom : Creature
{
    public enum ShroomState { Compact, Expanding, Expanded, Shrinking }

    [Header("Platform")]
    [Tooltip("Block containing the visible mesh and solid BoxCollider.")]
    [SerializeField] private Transform platform;
    [SerializeField] private Vector3 compactSize = new Vector3(1f, 0.5f, 1f);
    [SerializeField] private Vector3 expandedSize = new Vector3(4f, 1f, 4f);
    [Min(0.01f)] [SerializeField] private float expansionDuration = 0.6f;
    [Min(0.01f)] [SerializeField] private float shrinkDuration = 0.6f;
    [Min(0f)] [SerializeField] private float expandedDuration = 10f;

    [Header("Expansion Push")]
    [Tooltip("Small, mass-independent explosion impulse when expansion begins. Set to zero to disable.")]
    [Min(0f)] [SerializeField] private float expansionPushForce = 8f;
    [Min(0.01f)] [SerializeField] private float expansionPushRadius = 3.5f;
    [Tooltip("Adds a little upward lift so nearby bodies can clear the growing block.")]
    [Min(0f)] [SerializeField] private float expansionUpwardModifier = 0.5f;

    [Header("Friction")]
    [Tooltip("Collider material while compact, so a resting shroom does not slide down slopes. Expanding puts back " +
             "the collider's own (frictionless) material, so the platform still feels the same.")]
    [SerializeField] private PhysicsMaterial compactMaterial;

    [Header("Hopping")]
    [Tooltip("While compact and free, it hops about like a slime: a crouch, a short hop, a squash on landing.")]
    [SerializeField] private bool hops = true;
    [Tooltip("Seconds resting between hops, picked at random in this range.")]
    [SerializeField] private Vector2 hopPause = new Vector2(1.2f, 3.5f);
    [Min(0f)] [SerializeField] private float hopUpSpeed = 7f;
    [Min(0f)] [SerializeField] private float hopForwardSpeed = 2.5f;
    [Tooltip("Once this far from where it was set down, hops lean back toward that spot.")]
    [Min(0.5f)] [SerializeField] private float wanderRadius = 5f;
    [Tooltip("Never hops toward a landing whose ground is further down than this, so it stays on ledges and caps.")]
    [Min(0.1f)] [SerializeField] private float maxHopDrop = 1.5f;
    [Min(0.01f)] [SerializeField] private float crouchTime = 0.18f;

    [Header("Life Cycle")]
    [Tooltip("Dying of old age, it sprouts this young Mushroom Tree where it lay, unless another tree is too close.")]
    [SerializeField] private MushroomTree growsInto;
    [Tooltip("No sprout this close to another Mushroom Tree's trunk: the old shroom just withers away.")]
    [Min(0f)] [SerializeField] private float crowdingRadius = 6f;
    [Tooltip("Each shroom's lifespan varies by up to this share, so a group does not age out all at once.")]
    [Range(0f, 0.9f)] [SerializeField] private float lifespanJitter = 0.25f;

    [Header("Runtime (Read Only)")]
    [SerializeField] private ShroomState currentState;
    [SerializeField, Range(0f, 1f)] private float growth;
    [SerializeField] private float remainingExpandedTime;

    private enum HopPhase { Resting, Crouching, Airborne }
    private BouncySurface surface;
    private Rigidbody body;
    private Collider platformCollider;
    private PhysicsMaterial platformMaterial;
    private Vector3 baseLocalPosition;
    private HopPhase hopPhase;
    private float hopTimer;
    private bool hasHome;
    private Vector3 home, hopDirection;
    private Vector3 squash = Vector3.one;
    private static readonly Vector3 Crouched = new Vector3(1.15f, 0.7f, 1.15f), Stretched = new Vector3(0.9f, 1.15f, 0.9f),
        Landed = new Vector3(1.2f, 0.75f, 1.2f);

    public ShroomState CurrentState => currentState;
    public float Growth => growth;
    public bool IsExpanded => currentState == ShroomState.Expanded;
    public override bool CanBeCarried => currentState == ShroomState.Compact;
    public override string InteractionPrompt =>
        !IsDead && (currentState == ShroomState.Compact || currentState == ShroomState.Shrinking) ? "Expand" : null;
    public override string CarriedPrompt => IsDead ? null : "[LMB] Expand and place";
    public override bool CanUseWhileCarried => !IsDead;

    protected override void InitializeCreature()
    {
        surface = GetComponent<BouncySurface>();
        body = GetComponent<Rigidbody>();
        if (platform == null) platform = transform.Find("Platform");
        if (platform == null)
        {
            Debug.LogError("BounceShroom needs a Platform child.", this);
            SetSimulationEnabled(false);
            surface.SetBouncy(false);
            return;
        }
        baseLocalPosition = platform.localPosition - Vector3.up * platform.localScale.y * 0.5f;
        platformCollider = platform.GetComponent<Collider>();
        if (platformCollider != null) platformMaterial = platformCollider.sharedMaterial;
        SetMovementPhysics(false, true);
        currentState = ShroomState.Compact;
        growth = 0f;
        SetGrip(true);
        surface.SetBouncy(false);
        ApplySize();
        if (Application.isPlaying && AgeRate > 0f && lifespanJitter > 0f && !IsDead)
            SetRemainingAge(Age * Random.Range(1f - lifespanJitter, 1f + lifespanJitter));
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || platform == null ||
            (currentState != ShroomState.Compact && currentState != ShroomState.Shrinking)) return;
        // Release while still compact, before pickup eligibility changes.
        if (IsCarried) Carrier.Release();
        StopHopping();
        SetMovementPhysics(true, false);
        currentState = ShroomState.Expanding;
        surface.SetBouncy(false);
        SetGrip(false);
        PushNearbyBodies();
    }

    private void PushNearbyBodies()
    {
        if (expansionPushForce <= 0f) return;
        Vector3 centre = platform.position;
        float radius = Mathf.Max(0.01f, expansionPushRadius);
        Collider[] nearby = Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Ignore);
        Rigidbody ownBody = GetComponent<Rigidbody>();
        var pushedBodies = new HashSet<Rigidbody>();
        foreach (Collider nearbyCollider in nearby)
        {
            Rigidbody body = nearbyCollider.attachedRigidbody;
            // A compound body receives one impulse, regardless of its collider count.
            if (body == null || body == ownBody || body.isKinematic || !pushedBodies.Add(body)) continue;
            body.WakeUp();
            body.AddExplosionForce(expansionPushForce, centre, radius,
                expansionUpwardModifier, ForceMode.VelocityChange);
        }
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled || platform == null) return;
        switch (currentState)
        {
            case ShroomState.Compact:
                Hop(Time.fixedDeltaTime);
                break;
            case ShroomState.Expanding:
                growth = Mathf.MoveTowards(growth, 1f, Time.fixedDeltaTime / Mathf.Max(0.01f, expansionDuration));
                ApplySize();
                if (growth >= 1f)
                {
                    currentState = ShroomState.Expanded;
                    remainingExpandedTime = expandedDuration;
                    surface.SetBouncy(true);
                }
                break;
            case ShroomState.Expanded:
                remainingExpandedTime = Mathf.Max(0f, remainingExpandedTime - Time.fixedDeltaTime);
                if (remainingExpandedTime <= 0f)
                {
                    currentState = ShroomState.Shrinking;
                    surface.SetBouncy(false);
                }
                break;
            case ShroomState.Shrinking:
                growth = Mathf.MoveTowards(growth, 0f, Time.fixedDeltaTime / Mathf.Max(0.01f, shrinkDuration));
                ApplySize();
                if (growth <= 0f)
                {
                    currentState = ShroomState.Compact;
                    SetMovementPhysics(false, true);
                    SetGrip(true);
                }
                break;
        }
    }

    protected override void TickCreature(float deltaTime) { }

    /// <summary>Rest, crouch, hop, squash on landing, rest again. Only while compact, free and dynamic.</summary>
    private void Hop(float deltaTime)
    {
        if (!hops || IsCarried || IsBubbleCaptured || body == null || body.isKinematic) { StopHopping(); return; }
        bool grounded = Grounded();
        switch (hopPhase)
        {
            case HopPhase.Resting:
                SetSquash(Vector3.MoveTowards(squash, Vector3.one, deltaTime * 3f));
                // Knocked about or falling: wait until it has settled again.
                if (!grounded || body.linearVelocity.sqrMagnitude > 0.25f) { hopTimer = Mathf.Max(hopTimer, 0.4f); return; }
                if (!hasHome)
                {
                    home = transform.position;
                    hasHome = true;
                    hopTimer = Random.Range(hopPause.x, hopPause.y);
                }
                hopTimer -= deltaTime;
                if (hopTimer > 0f) return;
                if (!TryChooseHop(out hopDirection)) { hopTimer = Random.Range(hopPause.x, hopPause.y); return; }
                hopPhase = HopPhase.Crouching;
                hopTimer = crouchTime;
                break;
            case HopPhase.Crouching:
                SetSquash(Vector3.MoveTowards(squash, Crouched, deltaTime / crouchTime));
                hopTimer -= deltaTime;
                if (hopTimer > 0f) return;
                body.rotation = Quaternion.LookRotation(hopDirection, Vector3.up);
                body.linearVelocity = hopDirection * hopForwardSpeed + Vector3.up * hopUpSpeed;
                hopPhase = HopPhase.Airborne;
                hopTimer = 0.15f; // clear of the ground before landing counts
                break;
            case HopPhase.Airborne:
                SetSquash(Vector3.MoveTowards(squash, Stretched, deltaTime * 4f));
                hopTimer -= deltaTime;
                if (hopTimer > 0f || !grounded || body.linearVelocity.y > 0.1f) return;
                hopPhase = HopPhase.Resting;
                SetSquash(Landed);
                hopTimer = Random.Range(hopPause.x, hopPause.y);
                break;
        }
    }

    /// <summary>A random direction, leaning home once it has wandered, with ground under the landing and no wall in the way.</summary>
    private bool TryChooseHop(out Vector3 direction)
    {
        Vector3 from = platform.position, toHome = home - transform.position;
        toHome.y = 0f;
        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        float reach = hopForwardSpeed * 2f * hopUpSpeed / gravity + compactSize.x * 0.5f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (toHome.magnitude > wanderRadius) direction = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * toHome.normalized;
            else
            {
                Vector2 circle = Random.insideUnitCircle.normalized;
                direction = new Vector3(circle.x, 0f, circle.y);
            }
            if (direction.sqrMagnitude < 0.01f) continue;
            // Checked a little above the body, so a gentle uphill slope does not count as a wall.
            if (Physics.Raycast(from + Vector3.up * 0.5f, direction, reach, ~0, QueryTriggerInteraction.Ignore)) continue;
            Vector3 landing = from + direction * reach + Vector3.up * 0.5f;
            if (Physics.Raycast(landing, Vector3.down, 0.5f + compactSize.y * 0.5f + maxHopDrop, ~0, QueryTriggerInteraction.Ignore))
                return true;
        }
        direction = Vector3.zero;
        return false;
    }

    private bool Grounded() =>
        Physics.Raycast(platform.position, Vector3.down, platform.localScale.y * 0.5f + 0.12f, ~0, QueryTriggerInteraction.Ignore);

    private void SetSquash(Vector3 value)
    {
        if (squash == value) return;
        squash = value;
        ApplySize();
    }

    private void StopHopping()
    {
        hopPhase = HopPhase.Resting;
        hopTimer = 0f;
        SetSquash(Vector3.one);
    }

    protected override void OnCarryStarted()
    {
        // Wherever it is set down next becomes its new home.
        hasHome = false;
        StopHopping();
    }

    protected override void OnDeath()
    {
        if (surface != null) surface.SetBouncy(false);
    }

    /// <summary>Old age closes the cycle: a young Mushroom Tree sprouts where the shroom lay. Other deaths leave remains.</summary>
    protected override void OnAgeDeath()
    {
        Vector3 at = transform.position;
        if (growsInto != null && !Crowded(at))
        {
            MushroomTree sprout = Instantiate(growsInto, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            sprout.name = "Mushroom Tree (sprouted)";
        }
        Destroy(gameObject);
    }

    private bool Crowded(Vector3 at)
    {
        foreach (MushroomTree tree in FindObjectsByType<MushroomTree>(FindObjectsSortMode.None))
        {
            Vector3 offset = tree.transform.position - at;
            if (new Vector2(offset.x, offset.z).magnitude < crowdingRadius) return true;
        }
        return false;
    }

    private void SetGrip(bool compact)
    {
        if (platformCollider == null || compactMaterial == null) return;
        platformCollider.sharedMaterial = compact ? compactMaterial : platformMaterial;
    }

    private void ApplySize()
    {
        // The hop squash only ever applies while compact; it is reset before expanding.
        Vector3 size = Vector3.Scale(Vector3.Lerp(compactSize, expandedSize, Mathf.SmoothStep(0f, 1f, growth)), squash);
        platform.localScale = size;
        platform.localPosition = baseLocalPosition + Vector3.up * size.y * 0.5f;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (surface != null) surface.SetBouncy(false);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (surface != null) surface.SetBouncy(IsExpanded);
    }

    private void OnValidate()
    {
        compactSize = Vector3.Max(compactSize, Vector3.one * 0.01f);
        expandedSize = Vector3.Max(expandedSize, Vector3.one * 0.01f);
    }
}
