using System.Collections.Generic;
using UnityEngine;

/// <summary>Exclusive, reversible recycling claim shared by a rooted colony and a portable flower.</summary>
[DisallowMultipleComponent]
public sealed class CompostAbsorption : MonoBehaviour
{
    struct BodyState
    {
        internal Rigidbody body;
        internal bool kinematic, gravity;
        internal RigidbodyConstraints constraints;
        internal RigidbodyInterpolation interpolation;
        internal CollisionDetectionMode collisionMode;
        internal Vector3 velocity, angularVelocity;
    }
    readonly List<BodyState> bodies = new List<BodyState>();
    Creature corpse;
    Vector3 originalPosition, originalScale, pivot;
    Quaternion originalRotation;
    public Creature Consumer { get; private set; }
    public CompostCreature Owner => Consumer as CompostCreature;
    public bool HasClaim => Consumer != null;
    public float Progress { get; internal set; }
    public long ChargeValue { get; private set; }
    public bool IsValid => Consumer != null && Consumer.isActiveAndEnabled && !Consumer.IsDead && corpse != null &&
        corpse.isActiveAndEnabled && corpse.IsDead && !corpse.IsConsumptionPending && !corpse.IsCarried && !corpse.IsBubbleCaptured;

    internal bool TryBegin(CompostCreature owner, Creature target, Bounds bounds, long value) =>
        TryBegin(owner, target, new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), value);

    internal bool TryBegin(Creature consumer, Creature target, Vector3 contactPoint, long value)
    {
        if (HasClaim || consumer == null || !Eligible(target)) return false;
        corpse = target; Consumer = consumer; ChargeValue = value; Progress = 0f;
        originalPosition = transform.position; originalRotation = transform.rotation; originalScale = transform.localScale;
        pivot = contactPoint;
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>())
        {
            if (!CreatureBounds.BelongsTo(body.transform, transform)) continue;
            bodies.Add(new BodyState { body = body, kinematic = body.isKinematic, gravity = body.useGravity,
                constraints = body.constraints, interpolation = body.interpolation, collisionMode = body.collisionDetectionMode,
                velocity = body.isKinematic ? Vector3.zero : body.linearVelocity,
                angularVelocity = body.isKinematic ? Vector3.zero : body.angularVelocity });
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true; body.useGravity = false; body.constraints = RigidbodyConstraints.None;
        }
        return true;
    }

    public static bool Eligible(Creature target) => target != null && target.isActiveAndEnabled && target.IsDead &&
        target.CanBeConsumed && !target.IsConsumptionPending && !target.IsCarried && !target.IsBubbleCaptured &&
        !(target.TryGetComponent<WebCapture>(out var web) && web.IsCaptured) &&
        !(target is RecycleFlower flower && flower.SeedReserved) &&
        !(target.TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim);

    internal void FollowBack()
    {
        if (!IsValid) return;
        float scale = Mathf.Max(0.03f, 1f - Progress);
        transform.localScale = originalScale * scale;
        float jolt = Mathf.Sin(Mathf.Clamp01(Progress / 0.06f) * Mathf.PI) * 0.09f;
        transform.SetPositionAndRotation(pivot + (originalPosition - pivot) * scale + Vector3.up * jolt, originalRotation);
        if (TryGetComponent<Rigidbody>(out var root)) root.position = transform.position;
    }
    public void CancelForPickup() => Release(true);
    internal bool Complete()
    {
        if (!IsValid) { Release(true); return false; }
        Creature removed = corpse;
        Consumer = null; corpse = null; bodies.Clear();
        CreatureRegistry.Unregister(removed);
        removed.gameObject.SetActive(false);
        Destroy(removed.gameObject);
        return true;
    }
    internal void Release(bool restorePose)
    {
        if (!HasClaim) return;
        if (corpse != null)
        {
            transform.localScale = originalScale;
            transform.SetPositionAndRotation(originalPosition, originalRotation);
        }
        foreach (BodyState state in bodies)
        {
            if (state.body == null) continue;
            state.body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            state.body.isKinematic = state.kinematic; state.body.useGravity = state.gravity;
            state.body.constraints = state.constraints; state.body.interpolation = state.interpolation;
            state.body.collisionDetectionMode = state.collisionMode;
            if (!state.kinematic)
            { state.body.linearVelocity = state.velocity; state.body.angularVelocity = state.angularVelocity; }
        }
        bodies.Clear(); Consumer = null; corpse = null;
    }
    void OnDisable() => Release(true);
    void OnDestroy() => Release(true);
}

/// <summary>No coroutine per corpse: a consumer advances its small set of exclusive claims.</summary>
internal sealed class RecycleDigestion
{
    readonly List<CompostAbsorption> claims = new List<CompostAbsorption>();
    internal int Count => claims.Count;
    internal bool Begin(Creature consumer, Creature target, Vector3 point)
    {
        if (!CompostAbsorption.Eligible(target)) return false;
        if (!target.TryGetComponent<CompostAbsorption>(out var claim)) claim = target.gameObject.AddComponent<CompostAbsorption>();
        if (!claim.TryBegin(consumer, target, point, CompostCreature.ChargesForNutrition(target.Nutrition))) return false;
        claims.Add(claim); return true;
    }
    internal long Advance(Creature owner, float dt, float seconds, long stored, long threshold = 50)
    {
        long added = 0;
        bool full = stored >= threshold;
        for (int i = 0; i < claims.Count;)
        {
            CompostAbsorption claim = claims[i];
            if (claim == null || claim.Consumer != owner || !claim.IsValid)
            { if (claim != null && claim.Consumer == owner) claim.Release(true); claims.RemoveAt(i); continue; }
            float limit = full ? Mathf.Max(claim.Progress, 0.8f) : 1f;
            claim.Progress = Mathf.MoveTowards(claim.Progress, limit, dt / Mathf.Max(0.01f, seconds));
            claim.FollowBack();
            if (claim.Progress >= 1f && !full)
            {
                long value = claim.ChargeValue;
                if (claim.Complete()) added = SaturatingAdd(added, value);
                claims.RemoveAt(i);
                if (SaturatingAdd(stored, added) >= threshold) full = true;
            }
            else i++;
        }
        return added;
    }
    internal static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
    internal void Release()
    { foreach (var claim in claims) if (claim != null) claim.Release(true); claims.Clear(); }
}
