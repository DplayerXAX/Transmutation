using UnityEngine;

/// <summary>A solid contact, shared by scripted throws and Rigidbody collisions.</summary>
public readonly struct CreatureImpact
{
    public Collider OtherCollider { get; }
    public Creature OtherCreature { get; }
    public Vector3 Point { get; }
    /// <summary>Points from the other surface toward the receiving creature.</summary>
    public Vector3 Normal { get; }
    /// <summary>Receiver velocity minus the other body's velocity, before the response.</summary>
    public Vector3 RelativeVelocity { get; }
    public Vector3 ReceiverVelocity { get; }
    public float NormalSpeed => Mathf.Max(0f, -Vector3.Dot(RelativeVelocity, Normal));

    // Cache identity before either reaction can remove the other body.
    internal int OtherBodyId { get; }
    internal bool IsTrigger { get; }

    public CreatureImpact(Collider otherCollider, Vector3 point, Vector3 normal,
        Vector3 relativeVelocity, Vector3 receiverVelocity)
    {
        OtherCollider = otherCollider;
        OtherCreature = CreatureSpatialQuery.Owner(otherCollider);
        Point = point;
        Normal = normal.normalized;
        RelativeVelocity = relativeVelocity;
        ReceiverVelocity = receiverVelocity;
        OtherBodyId = otherCollider == null ? 0 : otherCollider.attachedRigidbody != null ?
            otherCollider.attachedRigidbody.GetInstanceID() : otherCollider.GetInstanceID();
        IsTrigger = otherCollider != null && otherCollider.isTrigger;
    }
}
