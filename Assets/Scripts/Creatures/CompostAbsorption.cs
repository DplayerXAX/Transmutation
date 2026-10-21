using System.Collections.Generic;
using UnityEngine;

/// <summary>A reversible, exclusive absorption claim. Does not change the creature's life state.</summary>
[DisallowMultipleComponent]
public sealed class CompostAbsorption : MonoBehaviour
{
    private struct BodyState
    {
        public Rigidbody body;
        public bool kinematic, gravity;
        public RigidbodyConstraints constraints;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionMode;
        public Vector3 velocity, angularVelocity;
    }

    private struct CollisionState
    {
        public Collider corpse, platform;
        public bool wasIgnored;
    }

    private readonly List<BodyState> bodies = new List<BodyState>();
    private readonly List<CollisionState> collisions = new List<CollisionState>();
    private Creature corpse;
    private Vector3 startLocal;
    private Quaternion rotationLocal;
    private float sinkDepth;
    private Rigidbody rootBody;
    public CompostCreature Owner { get; private set; }
    public float Progress { get; internal set; }
    public long ChargeValue { get; private set; }
    public bool IsValid => Owner != null && corpse != null && corpse.isActiveAndEnabled && corpse.IsDead &&
        !corpse.IsConsumptionPending && !corpse.IsCarried && !corpse.IsBubbleCaptured;

    internal bool TryBegin(CompostCreature owner, Creature target, Bounds bounds, long value)
    {
        if (Owner != null || owner == null || target == null || !target.IsDead) return false;
        corpse = target;
        Owner = owner;
        ChargeValue = value;
        Progress = 0f;
        startLocal = owner.transform.InverseTransformPoint(transform.position);
        rotationLocal = Quaternion.Inverse(owner.transform.rotation) * transform.rotation;
        sinkDepth = Mathf.Max(0.05f, bounds.max.y - owner.BackTopWS + 0.025f);
        rootBody = GetComponent<Rigidbody>();
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>())
        {
            if (!CreatureBounds.BelongsTo(body.transform, transform)) continue;
            bodies.Add(new BodyState
            {
                body = body, kinematic = body.isKinematic, gravity = body.useGravity, constraints = body.constraints,
                interpolation = body.interpolation, collisionMode = body.collisionDetectionMode,
                velocity = body.isKinematic ? Vector3.zero : body.linearVelocity,
                angularVelocity = body.isKinematic ? Vector3.zero : body.angularVelocity
            });
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.None;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        foreach (Collider own in GetComponentsInChildren<Collider>())
        {
            if (!own.enabled || !CreatureBounds.BelongsTo(own.transform, transform)) continue;
            foreach (Collider platform in owner.BodyColliders)
            {
                if (platform == null || !platform.enabled) continue;
                bool ignored = Physics.GetIgnoreCollision(own, platform);
                collisions.Add(new CollisionState { corpse = own, platform = platform, wasIgnored = ignored });
                Physics.IgnoreCollision(own, platform, true);
            }
        }
        return true;
    }

    internal void FollowBack()
    {
        if (!IsValid) return;
        Vector3 pointOS = startLocal - Vector3.up * (sinkDepth * Progress);
        Vector3 position = Owner.RideSurface.TransformPlannedPoint(pointOS);
        Quaternion rotation = Owner.RideSurface.PlannedRotation * rotationLocal;
        if (rootBody != null)
        {
            rootBody.MovePosition(position);
            rootBody.MoveRotation(rotation);
        }
        else transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>Called before shared carry physics takes ownership; existing pickup rules still apply.</summary>
    public void CancelForPickup() => Release(true);

    internal bool Complete()
    {
        if (!IsValid) { Release(false); return false; }
        Creature removed = corpse;
        RestorePhysics(false);
        Owner = null;
        corpse = null;
        // Disable immediately so deferred destruction cannot also award a bird meal or another compost claim.
        removed.gameObject.SetActive(false);
        Destroy(removed.gameObject);
        return true;
    }

    internal void Release(bool liftAboveBack)
    {
        if (Owner == null) return;
        RestorePhysics(liftAboveBack);
        Owner = null;
        corpse = null;
    }

    private void RestorePhysics(bool liftAboveBack)
    {
        if (liftAboveBack && Owner != null && corpse != null)
        {
            Vector3 restored = Owner.transform.TransformPoint(startLocal);
            Quaternion rotation = Owner.transform.rotation * rotationLocal;
            transform.SetPositionAndRotation(restored, rotation);
            if (rootBody != null) { rootBody.position = restored; rootBody.rotation = rotation; }
        }
        foreach (CollisionState state in collisions)
            if (state.corpse != null && state.platform != null)
                Physics.IgnoreCollision(state.corpse, state.platform, state.wasIgnored);
        collisions.Clear();
        foreach (BodyState state in bodies)
        {
            if (state.body == null) continue;
            state.body.isKinematic = state.kinematic;
            state.body.useGravity = state.gravity;
            state.body.constraints = state.constraints;
            state.body.interpolation = state.interpolation;
            state.body.collisionDetectionMode = state.collisionMode;
            if (!state.kinematic)
            {
                state.body.linearVelocity = liftAboveBack ? Vector3.zero : state.velocity;
                state.body.angularVelocity = liftAboveBack ? Vector3.zero : state.angularVelocity;
            }
        }
        bodies.Clear();
    }

    private void OnDisable() => Release(false);
    private void OnDestroy() => Release(false);
}
