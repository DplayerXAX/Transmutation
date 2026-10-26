using System.Collections.Generic;
using UnityEngine;

/// <summary>Independent of species collision callbacks; only reports contact entry, never resting contact.</summary>
[DisallowMultipleComponent]
public sealed class CreatureImpactRelay : MonoBehaviour
{
    private Creature owner;
    private Rigidbody body;
    private Vector3 velocityBeforeStep;
    private float reportedStep = float.NegativeInfinity;
    private readonly HashSet<int> reportedBodies = new HashSet<int>();

    internal void Initialize(Creature creature)
    {
        owner = creature;
        body = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        velocityBeforeStep = body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (owner == null || CreatureThrower.IsFlying(owner) ||
            CreatureThrower.IsFlying(CreatureSpatialQuery.Owner(collision.collider))) return;
        // Unity reports other-minus-receiver. Match the API's receiver-minus-other convention.
        CreatureImpact strongest = default;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Vector3 relative = -collision.relativeVelocity;
            var impact = new CreatureImpact(collision.collider, contact.point, contact.normal, relative, velocityBeforeStep);
            if (impact.NormalSpeed > strongest.NormalSpeed) strongest = impact;
        }
        Dispatch(owner, strongest);
    }

    /// <summary>Shared filtering and per-physics-step body-pair deduplication for both producers.</summary>
    internal static void Dispatch(Creature receiver, CreatureImpact impact)
    {
        if (receiver == null || !receiver.CanReceiveImpact(impact)) return;
        var relay = receiver.GetComponent<CreatureImpactRelay>();
        if (relay == null) return;
        if (relay.reportedStep != Time.fixedTime)
        {
            relay.reportedStep = Time.fixedTime;
            relay.reportedBodies.Clear();
        }
        if (!relay.reportedBodies.Add(impact.OtherBodyId)) return;
        try { receiver.ReceiveImpact(impact); }
        catch (System.Exception exception) { Debug.LogException(exception, receiver); }
    }
}
