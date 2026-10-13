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

    [Header("Runtime (Read Only)")]
    [SerializeField] private ShroomState currentState;
    [SerializeField, Range(0f, 1f)] private float growth;
    [SerializeField] private float remainingExpandedTime;

    private BouncySurface surface;
    private Vector3 baseLocalPosition;

    public ShroomState CurrentState => currentState;
    public float Growth => growth;
    public bool IsExpanded => currentState == ShroomState.Expanded;
    public override bool CanBeCarried => currentState == ShroomState.Compact;
    public override string InteractionPrompt =>
        currentState == ShroomState.Compact || currentState == ShroomState.Shrinking ? "Expand" : null;
    public override string CarriedPrompt => "[LMB] Expand and place";

    protected override void InitializeCreature()
    {
        surface = GetComponent<BouncySurface>();
        if (platform == null) platform = transform.Find("Platform");
        if (platform == null)
        {
            Debug.LogError("BounceShroom needs a Platform child.", this);
            SetSimulationEnabled(false);
            surface.SetBouncy(false);
            return;
        }
        baseLocalPosition = platform.localPosition - Vector3.up * platform.localScale.y * 0.5f;
        SetMovementPhysics(false, true);
        currentState = ShroomState.Compact;
        growth = 0f;
        surface.SetBouncy(false);
        ApplySize();
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || platform == null ||
            (currentState != ShroomState.Compact && currentState != ShroomState.Shrinking)) return;
        // Release while still compact, before pickup eligibility changes.
        if (IsCarried) Carrier.Release();
        SetMovementPhysics(true, false);
        currentState = ShroomState.Expanding;
        surface.SetBouncy(false);
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
                }
                break;
        }
    }

    protected override void TickCreature(float deltaTime) { }

    private void ApplySize()
    {
        Vector3 size = Vector3.Lerp(compactSize, expandedSize, Mathf.SmoothStep(0f, 1f, growth));
        platform.localScale = size;
        platform.localPosition = baseLocalPosition + Vector3.up * size.y * 0.5f;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (surface != null) surface.SetBouncy(false);
    }

    private void OnEnable()
    {
        if (surface != null) surface.SetBouncy(IsExpanded);
    }

    private void OnValidate()
    {
        compactSize = Vector3.Max(compactSize, Vector3.one * 0.01f);
        expandedSize = Vector3.Max(expandedSize, Vector3.one * 0.01f);
    }
}
