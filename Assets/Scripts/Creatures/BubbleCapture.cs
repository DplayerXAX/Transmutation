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
    private Rigidbody bubbleBody;
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
        if ((creature == null && player == null) || (creature != null && (!creature.CanBeCaptured || creature.IsCarried ||
            (creature.TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim)))) return false;
        if (creature != null) CreatureThrower.ReleaseFlight(creature);

        // Release silk before recording simulation state, so release resumes the original behavior.
        foreach (WebCapture web in GetComponentsInChildren<WebCapture>())
            if (CreatureBounds.BelongsTo(web.transform, transform) && web.IsCaptured) web.Release();
        if (player != null) player.BeginBubbleMovementLock();
        OwningBubble = bubble;
        bubbleBody = bubble.GetComponent<Rigidbody>();
        // From the simulated pose (the bubble was just placed on it), not the interpolated drawn one.
        pivotOffset = (TryGetComponent<Rigidbody>(out var ownBody) ? ownBody.position : transform.position) - bubble.transform.position;
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

    // Follow places the occupant from the bubble's simulated position each physics step, but the bubble's shell is
    // drawn interpolated (up to one step behind). Draw the occupant on that same clock, or it slides inside the
    // shell by the bubble's speed x the physics step and a trapped player's camera shakes with it. The pose comes
    // from the bubble's simulated position and velocity rather than its interpolated transform, which swings
    // between the old and new spot for a frame after the capture teleports the shell onto the occupant.
    // Runs in Update at order -200, before the player controller moves its camera to the body; the next step's
    // Follow writes the simulated position again before physics runs.
    private void Update()
    {
        if (IsCaptured)
        {
            Vector3 centre = OwningBubble.transform.position;
            if (bubbleBody != null && bubbleBody.interpolation != RigidbodyInterpolation.None)
            {
                float behind = 1f - Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                centre = bubbleBody.position - bubbleBody.linearVelocity * (behind * Time.fixedDeltaTime);
            }
            transform.position = centre + pivotOffset;
        }
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
                // Out and towards where the player looks, so the next bubble can be aimed for.
                Camera view = player.PlayerCamera != null ? player.PlayerCamera : Camera.main;
                Vector3 ahead = view != null ? Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : Vector3.zero;
                body.linearVelocity = Vector3.up * velocity + ahead * bubble.EscapeForward;
            }
        }
        bodies.Clear(); colliders.Clear();
        bubble.ForgetCapture(this);
    }

    private void OnDisable() { if (!ReflectionPhysics.SwitchingWorldRoots) Release(); }
    private void OnDestroy() => Release();
}
