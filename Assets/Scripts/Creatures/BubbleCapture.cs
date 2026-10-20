using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Owns one occupant's movement and restores only the state it changed.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class BubbleCapture : MonoBehaviour
{
    private struct BodyState
    {
        public Rigidbody body;
        public bool kinematic, gravity, collisions;
        public RigidbodyConstraints constraints;
        public CollisionDetectionMode collisionMode;
        public Vector3 offset;
    }
    private struct ColliderState { public Collider collider; public bool enabled; }
    private readonly List<BodyState> bodies = new List<BodyState>();
    private readonly List<ColliderState> colliders = new List<ColliderState>();
    private Creature creature;
    private SmoothFirstPersonController player;
    private Vector3 pivotOffset;
    private bool previousSimulation;
    private float eligibleAfter;
    private int escapeFrame = -1;

    public TrappingBubble OwningBubble { get; private set; }
    public bool IsCaptured => OwningBubble != null;
    public bool CanCapture => isActiveAndEnabled && !IsCaptured && Time.time >= eligibleAfter;
    public bool BlocksPlayerMovement => IsCaptured || escapeFrame == Time.frameCount;

    public bool Attach(TrappingBubble bubble)
    {
        if (!CanCapture || bubble == null) return false;
        creature = GetComponent<Creature>();
        player = GetComponent<SmoothFirstPersonController>();
        if ((creature == null && player == null) || (creature != null && creature.IsCarried)) return false;
        if (creature != null) CreatureThrower.ReleaseFlight(creature);

        // Release silk before recording simulation state, so release resumes the original behavior.
        foreach (WebCapture web in GetComponentsInChildren<WebCapture>())
            if (CreatureBounds.BelongsTo(web.transform, transform) && web.IsCaptured) web.Release();
        if (player != null) player.BeginBubbleMovementLock();
        OwningBubble = bubble;
        pivotOffset = transform.position - bubble.transform.position;
        if (creature != null)
        {
            previousSimulation = creature.SimulationEnabled;
            creature.SetSimulationEnabled(false);
        }
        bodies.Clear(); colliders.Clear();
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            if (!CreatureBounds.BelongsTo(body.transform, transform)) continue;
            bodies.Add(new BodyState { body = body, kinematic = body.isKinematic, gravity = body.useGravity,
                collisions = body.detectCollisions, constraints = body.constraints,
                collisionMode = body.collisionDetectionMode, offset = body.position - bubble.transform.position });
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
        }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (!CreatureBounds.BelongsTo(collider.transform, transform)) continue;
            colliders.Add(new ColliderState { collider = collider, enabled = collider.enabled });
            collider.enabled = false;
        }
        if (creature != null) creature.NotifyCaptureStarted();
        return true;
    }

    public void Follow(Vector3 bubbleCenter)
    {
        if (!IsCaptured) return;
        transform.position = bubbleCenter + pivotOffset;
        foreach (BodyState state in bodies)
            if (state.body != null) state.body.position = bubbleCenter + state.offset;
    }

    /// <summary>Like carry physics, defer a creature's new free-body state until ownership ends.</summary>
    public void SetReleasePhysics(Rigidbody body, bool kinematic, bool gravity)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            BodyState state = bodies[i];
            if (state.body != body) continue;
            state.kinematic = kinematic; state.gravity = gravity;
            bodies[i] = state;
            return;
        }
    }

    private void Update()
    {
        if (player != null && IsCaptured && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            Escape();
    }

    public void Escape()
    {
        if (player == null || !IsCaptured) return;
        escapeFrame = Time.frameCount;
        OwningBubble.Pop(true);
    }

    public void Release(bool launchPlayer = false)
    {
        TrappingBubble bubble = OwningBubble;
        if (bubble == null) return;
        OwningBubble = null;
        eligibleAfter = Time.time + bubble.ReleaseImmunity;
        foreach (ColliderState state in colliders)
            if (state.collider != null) state.collider.enabled = state.enabled;
        foreach (BodyState state in bodies)
        {
            Rigidbody body = state.body;
            if (body == null) continue;
            body.isKinematic = state.kinematic;
            body.useGravity = state.gravity;
            body.constraints = state.constraints;
            body.detectCollisions = state.collisions;
            body.collisionDetectionMode = state.collisionMode;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.WakeUp(); }
        }
        if (creature != null) creature.SetSimulationEnabled(previousSimulation);
        if (player != null)
        {
            player.EndBubbleMovementLock();
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (launchPlayer && body != null && !body.isKinematic)
            {
                player.PrepareBubbleEscape();
                float velocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * bubble.EscapeHeight);
                body.linearVelocity = Vector3.up * velocity;
            }
        }
        bodies.Clear(); colliders.Clear();
        bubble.ForgetCapture(this);
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
