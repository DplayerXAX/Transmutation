using System.Collections.Generic;
using UnityEngine;

/// <summary>A single body's stable attachment to stationary silk, with reversible physics ownership.</summary>
[DisallowMultipleComponent]
public sealed class WebCapture : MonoBehaviour
{
    public Rigidbody Body { get; private set; }
    public SkyWeb OwningWeb { get; private set; }
    public Vector3 ContactPoint { get; private set; }
    public bool IsCaptured => OwningWeb != null;

    private Creature creature;
    private bool previousSimulation;
    private bool ownsBodyState;
    private bool previousKinematic, previousGravity;
    private CollisionDetectionMode previousCollisionMode;
    private float eligibleAfter;
    private Collider[] bodyColliders;
    private readonly HashSet<Collider> ignoredWebColliders = new HashSet<Collider>();
    private readonly List<(Collider body, Collider silk)> ignoredPairs = new List<(Collider, Collider)>();

    public bool CanCapture => !IsCaptured && Time.time >= eligibleAfter;

    /// <summary>Lets freshly released/transformed remains clear the original silk before recapture.</summary>
    internal void DelayCapture(float seconds) => eligibleAfter = Mathf.Max(eligibleAfter, Time.time + Mathf.Max(0f, seconds));

    public bool Attach(SkyWeb web, Rigidbody body, Vector3 contactPoint)
    {
        if (!CanCapture || web == null || body == null || body.gameObject != gameObject || body.isKinematic) return false;
        if (body.TryGetComponent<BubbleCapture>(out var bubbleCapture) && !bubbleCapture.CanCapture) return false;
        Creature target = body.GetComponentInParent<Creature>();
        if (target != null && (!target.CanBeCaptured || target.IsDead || target.IsCarried ||
            (target.TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim))) return false;
        creature = target;
        Body = body;
        ContactPoint = contactPoint;
        OwningWeb = web;
        if (creature != null)
        {
            previousSimulation = creature.SimulationEnabled;
            creature.Died += OnCreatureDied;
            creature.SetSimulationEnabled(false);
            // Stop riding/other autonomous movement before taking physics ownership.
            creature.NotifyCaptureStarted();
        }
        previousKinematic = body.isKinematic;
        previousGravity = body.useGravity;
        previousCollisionMode = body.collisionDetectionMode;
        ownsBodyState = true;
        bodyColliders = body.GetComponentsInChildren<Collider>(true);
        foreach (Collider strand in web.GetComponentsInChildren<Collider>(true)) IgnoreWebCollider(strand);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        // FixedJoint constraints against the world can fight penetration correction and
        // frozen rotation axes, projecting a large captive far from its contact point.
        // A stationary web pins the existing pose without involving that solver loop.
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.useGravity = false;
        return true;
    }

    /// <summary>Also called for strands grown after this body was caught.</summary>
    internal void IgnoreWebCollider(Collider strand)
    {
        if (!IsCaptured || strand == null || strand.isTrigger || bodyColliders == null || !ignoredWebColliders.Add(strand)) return;
        foreach (Collider own in bodyColliders)
        {
            if (own == null || !own.enabled || own.isTrigger || own.attachedRigidbody != Body || Physics.GetIgnoreCollision(own, strand)) continue;
            Physics.IgnoreCollision(own, strand, true);
            ignoredPairs.Add((own, strand));
        }
    }

    public void Release()
    {
        SkyWeb previousWeb = OwningWeb;
        OwningWeb = null;
        foreach (var pair in ignoredPairs)
            if (pair.body != null && pair.silk != null) Physics.IgnoreCollision(pair.body, pair.silk, false);
        ignoredPairs.Clear();
        ignoredWebColliders.Clear();
        bodyColliders = null;
        if (ownsBodyState)
        {
            ownsBodyState = false;
            if (Body != null)
            {
                Body.isKinematic = previousKinematic;
                Body.useGravity = previousGravity;
                Body.collisionDetectionMode = previousCollisionMode;
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                    Body.WakeUp();
                }
            }
        }
        if (creature != null)
        {
            creature.Died -= OnCreatureDied;
            creature.SetSimulationEnabled(previousSimulation);
        }
        creature = null;
        eligibleAfter = Time.time + 0.5f;
        if (previousWeb != null) previousWeb.ForgetCapture(this);
    }

    private void OnDisable() { if (!ReflectionPhysics.SwitchingWorldRoots) Release(); }
    private void OnDestroy() => Release();
    private void OnCreatureDied(Creature dead) => Release();
}
