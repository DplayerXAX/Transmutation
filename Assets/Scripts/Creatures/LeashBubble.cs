using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared balloon leash for ordinary and fire bubbles, owned by the octopus across pickup/drop.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public abstract class LeashBubble : Creature
{
    private SphereCollider leashCollider;
    protected SphereCollider BalloonCollider => leashCollider != null ? leashCollider : leashCollider = GetComponent<SphereCollider>();
    protected virtual bool CanLeashBubble(TentacleTether owner) => true;
    protected virtual void OnLeashAttached() { }
    protected virtual void OnLeashReleased() { }
    protected bool IgnoreLeashTarget(GameObject target) => IsLeashed &&
        (target == leashCreature.gameObject || (UsesHand && target == leashOwner.BalloonPlayer));

    public override bool CanBeConsumed => !IsLeashed && base.CanBeConsumed;
    public override void Die() { if (!IsLeashed) base.Die(); }
    protected override void Update()
    {
        if (IsLeashed) { if (SimulationEnabled) TickCreature(Time.deltaTime); }
        else base.Update();
    }
    protected override void OnDeath() { ReleaseLeash(); RestoreExpiredIgnores(true); base.OnDeath(); }
    protected override void OnDisable() { ReleaseLeash(); RestoreExpiredIgnores(true); base.OnDisable(); }

    private struct IgnoredCollision
    {
        public Collider collider;
        public bool wasIgnored;
        public float until;
        public Creature heldCreature;
        public bool leash, leashHandler;
    }
    private readonly List<IgnoredCollision> ignoredCollisions = new List<IgnoredCollision>();
    private TentacleTether leashOwner;
    private TentacleCreature leashCreature;
    private int leashArm = -1;
    private float leashLength, blockedLeashTime;
    private bool handAnchor, anchorReady;
    private Vector3 previousAnchor;
    private RaycastHit[] leashHits = new RaycastHit[16];
    private static ulong nextLeashOrder;
    private Rigidbody leashBody;
    protected Rigidbody BalloonBody => leashBody != null ? leashBody : leashBody = GetComponent<Rigidbody>();
    public abstract float Radius { get; }

    public bool IsLeashed => leashCreature != null;
    public TentacleCreature LeashCreature => leashCreature;
    public ulong LeashOrder { get; private set; }
    private bool UsesHand => leashOwner != null && leashCreature != null && leashCreature.IsCarried
        && leashCreature.Carrier == leashOwner.BalloonCarrier;
    public Vector3 LeashOrigin => UsesHand ? leashOwner.BalloonOrigin
        : leashCreature != null ? leashCreature.transform.position + Vector3.up * leashCreature.TetherBodyRadius * 0.35f : transform.position;
    public float LeashLength => leashLength;
    public Vector3 LeashPoint
    {
        get
        {
            Vector3 centre = BalloonBody.position;
            Vector3 toward = IsLeashed ? LeashOrigin - centre : Vector3.down;
            return centre + (toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.down) * Radius;
        }
    }

    public bool CanLeash(TentacleTether owner) => owner != null && owner.BalloonPlayer != null && !IsDead && isActiveAndEnabled && SimulationEnabled && !IsCarried && !IsBubbleCaptured && CanLeashBubble(owner);

    public bool AttachLeash(TentacleTether owner, TentacleCreature creature, int arm, float length)
    {
        if (!CanLeash(owner) || creature == null || !creature.IsCarried || creature.Carrier != owner.BalloonCarrier) return false;
        ReleaseLeash();
        leashOwner = owner; leashCreature = creature; leashArm = arm;
        LeashOrder = ++nextLeashOrder;
        leashLength = Mathf.Max(1.4f, length); blockedLeashTime = 0f;
        OnLeashAttached();
        anchorReady = false;
        if (owner.BalloonPlayer != null) IgnoreColliders(owner.BalloonPlayer, Time.time, null, true, true);
        IgnoreColliders(creature.gameObject, Time.time, null, true);
        return true;
    }

    public void ShortenLeash(float metres) => leashLength = Mathf.Max(1.4f, leashLength - Mathf.Max(0f, metres));

    /// <summary>Dropping leaves the sucker on the shell, with the octopus body as its new anchor.</summary>
    public void UnbindHandler()
    {
        TentacleTether owner = leashOwner;
        leashOwner = null;
        if (owner != null) owner.ForgetBalloon(this, leashArm, true);
        RetargetAnchor();
        RestoreExpiredIgnores();
    }

    public bool BindHandler(TentacleTether owner)
    {
        if (!IsLeashed || owner == null || leashCreature.Carrier != owner.BalloonCarrier || !CanLeash(owner)) return false;
        if (leashOwner == owner) return true;
        UnbindHandler();
        leashOwner = owner;
        RetargetAnchor();
        if (owner.BalloonPlayer != null) IgnoreColliders(owner.BalloonPlayer, Time.time, null, true, true);
        return true;
    }

    private void RetargetAnchor()
    {
        if (!IsLeashed) return;
        previousAnchor = LeashOrigin; handAnchor = UsesHand; anchorReady = true; blockedLeashTime = 0f;
        // Hand/body changes preserve the current slack rather than yanking or breaking the leash.
        leashLength = Mathf.Max(leashLength, Vector3.Distance(previousAnchor, BalloonBody.position) - Radius);
    }

    public void ReleaseLeash()
    {
        TentacleTether owner = leashOwner;
        TentacleCreature creature = leashCreature;
        int arm = leashArm;
        leashOwner = null; leashCreature = null; leashArm = -1; blockedLeashTime = 0f;
        if (creature != null) creature.UnpinArm(arm);
        if (owner != null) owner.ForgetBalloon(this, arm);
        RestoreExpiredIgnores();
        if (creature != null) OnLeashReleased();
    }

    protected Vector3 LeashVelocity(Vector3 velocity, float deltaTime)
    {
        if (!IsLeashed) return velocity;
        if (!leashCreature.isActiveAndEnabled || leashCreature.IsDead || !leashCreature.SimulationEnabled
            || !leashCreature.ArmPin(leashArm, out _))
        { ReleaseLeash(); return velocity; }
        if (leashOwner != null && (!UsesHand || !leashOwner.isActiveAndEnabled)) UnbindHandler();
        if (!anchorReady || handAnchor != UsesHand) RetargetAnchor();
        Vector3 from = LeashOrigin;
        Vector3 anchorVelocity = Vector3.ClampMagnitude((from - previousAnchor) / deltaTime, 14f);
        previousAnchor = from;
        Vector3 span = BalloonBody.position - from;
        float distance = span.magnitude, limit = leashLength + Radius;
        // Release instead of dragging the player or cutting through a corner.
        bool blocked = false;
        if (distance > Radius + 0.15f)
        {
            var physics = gameObject.scene.GetPhysicsScene();
            int count;
            while ((count = physics.Raycast(from, span / distance, leashHits, distance - Radius - 0.1f, ~0,
                QueryTriggerInteraction.Ignore)) == leashHits.Length) leashHits = new RaycastHit[leashHits.Length * 2];
            for (int i = 0; i < count; i++)
            {
                Collider hit = leashHits[i].collider;
                blocked |= hit.GetComponentInParent<Creature>() == null && hit.GetComponentInParent<SmoothFirstPersonController>() == null
                    && !(UsesHand && hit.transform.IsChildOf(leashOwner.BalloonPlayer.transform)) && hit.GetComponent<TentacleGripSurface>() == null;
            }
        }
        blockedLeashTime = blocked ? blockedLeashTime + deltaTime : 0f;
        if (blockedLeashTime > 0.3f || distance > limit + Mathf.Max(3f, limit * 0.75f))
        { ReleaseLeash(); return velocity; }
        if (distance < 0.001f) return velocity;
        Vector3 outward = span / distance;
        float outwardSpeed = Vector3.Dot(velocity - anchorVelocity, outward);
        float allowed = distance <= limit ? (limit - distance) / deltaTime : -Mathf.Min((distance - limit) * 6f, 7f);
        if (outwardSpeed > allowed) velocity -= outward * (outwardSpeed - allowed);
        return Vector3.ClampMagnitude(velocity, 14f);
    }

    protected void IgnoreColliders(GameObject root, float until, Creature heldCreature = null, bool forLeash = false, bool forHandler = false)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == BalloonCollider) continue;
            int existing = ignoredCollisions.FindIndex(entry => entry.collider == collider);
            if (existing >= 0)
            {
                if (forLeash)
                {
                    IgnoredCollision entry = ignoredCollisions[existing]; entry.leash = true; entry.leashHandler |= forHandler;
                    ignoredCollisions[existing] = entry;
                }
                continue;
            }
            bool wasIgnored = Physics.GetIgnoreCollision(BalloonCollider, collider);
            ignoredCollisions.Add(new IgnoredCollision { collider = collider, wasIgnored = wasIgnored,
                until = until, heldCreature = heldCreature, leash = forLeash, leashHandler = forHandler });
            Physics.IgnoreCollision(BalloonCollider, collider, true);
        }
    }

    protected void RestoreExpiredIgnores(bool all = false)
    {
        for (int i = ignoredCollisions.Count - 1; i >= 0; i--)
        {
            IgnoredCollision entry = ignoredCollisions[i];
            if (!all && entry.collider != null && ((entry.leash && IsLeashed && (!entry.leashHandler || UsesHand)) || (Time.time < entry.until &&
                (entry.heldCreature == null || entry.heldCreature.IsCarried)))) continue;
            if (entry.collider != null && BalloonCollider != null)
                Physics.IgnoreCollision(BalloonCollider, entry.collider, entry.wasIgnored);
            ignoredCollisions.RemoveAt(i);
        }
    }

}
