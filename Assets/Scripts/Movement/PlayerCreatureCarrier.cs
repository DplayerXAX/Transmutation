using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to the player. Holds any Creature at an anchor without disabling its
/// simulation, colliders, animation, or heat reactions. Click right to pick up;
/// tap right to put down, or hold and release right to throw.
/// A tentacle creature shrinks onto the player's back while carried.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerCreatureCarrier : MonoBehaviour
{
    [SerializeField] private Camera carryCamera;
    [Tooltip("Empty transform in front of the camera. Created automatically if unassigned.")]
    [SerializeField] private Transform carryPoint;
    [Min(0f)] [SerializeField] private float pickupRange = 5f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    [Header("Input")]
    [Tooltip("On: hold right click to charge a throw, release to throw (a tap puts down). A tentacle creature is never " +
             "thrown: right click only puts it down, so its stuck arms are not thrown about by a long press. Off: right click only puts down.")]
    [SerializeField] private bool chargedThrow = true;
    [Tooltip("Hold this many seconds before releasing RMB becomes a throw instead of a putdown.")]
    [Min(0.01f)] [SerializeField] private float throwHoldThreshold = 0.2f;
    [Tooltip("Total RMB hold time needed to reach maximum throw speed.")]
    [Min(0.02f)] [SerializeField] private float fullThrowHoldTime = 1.2f;
    [Tooltip("Forward speed given to a free physics creature when it is put down.")]
    [Min(0f)] [SerializeField] private float dropForwardSpeed = 1.5f;

    [Header("Big Creatures")]
    [Tooltip("Creatures larger than this (metres, largest side) cannot be picked up.")]
    [Min(0.1f)] [SerializeField] private float maxCarrySize = 2.2f;
    [Tooltip("Degrees below the crosshair the top of a held creature is kept.")]
    [Range(0f, 30f)] [SerializeField] private float clearBelowCrosshair = 8f;
    [Tooltip("Degrees right of the crosshair the left side of a held creature is kept.")]
    [Range(0f, 30f)] [SerializeField] private float clearRightOfCrosshair = 6f;
    [Tooltip("A carried flower is held close, low in the bottom right of the view, like a tool in hand. Off = held out in front like other creatures.")]
    [SerializeField] private bool flowerInCorner = true;
    [Tooltip("Where a carried flower is held, from the camera (right, up, forward). Up is for the top of the flower.")]
    [SerializeField] private Vector3 flowerCorner = new Vector3(0.62f, -0.24f, 0.95f);
    [Header("Adaptive Hold")]
    [Tooltip("Any creature (not tentacles) is held by its visual middle, in front of the right hand, further out the bigger it is, and pulled back towards the camera when it would go into a wall or the ground. Off = the older fixed hold points below.")]
    [SerializeField] private bool adaptiveHold = true;
    [Tooltip("Where the right hand rests, from the camera (right, up, forward). The held creature's near side sits just beyond it.")]
    [SerializeField] private Vector3 handRest = new Vector3(0.28f, -0.3f, 0.45f);
    [Tooltip("Direction a bigger creature is moved out along (from the hand, camera space): forward, a little right and down.")]
    [SerializeField] private Vector3 holdOutward = new Vector3(0.35f, -0.25f, 1f);
    private Vector3 heldCentreLocal;
    private float heldRadius;

    [Tooltip("Where a carried Bellows is held (its middle, from the camera): out to the right, only a little low, so it is not dropped into the ground.")]
    [SerializeField] private Vector3 bellowsHold = new Vector3(0.9f, -0.45f, 2.1f);

    public Creature CarriedCreature { get; private set; }
    public Camera CarryCamera => carryCamera;
    public bool TryCarryFromInteraction(Creature creature)
    {
        if (CarriedCreature != null || pendingCreature != null || creature == null ||
            creature.gameObject.scene != gameObject.scene) return false;
        BeginCarry(creature);
        return CarriedCreature == creature;
    }
    public Creature PendingCreature => pendingCreature;

    /// <summary>The player's right hand (set by FirstPersonBody); tentacle creatures ride on it.</summary>
    public Transform HandAnchor { get; set; }
    /// <summary>The player's left hand (set by FirstPersonBody).</summary>
    public Transform LeftHandAnchor { get; set; }
    /// <summary>When the pickup input was last pressed and what it reached for, so the right hand can reach out.</summary>
    public float LastGrabTime { get; private set; } = -10f;
    public Vector3 LastGrabPoint { get; private set; }

    [Tooltip("Seconds from the click until the reaching hand closes on the creature and it is picked up.")]
    [Min(0f)] [SerializeField] private float grabReachTime = 0.22f;
    [Tooltip("How fast a picked-up creature is brought from where it was taken to where it is held (m/s).")]
    [Min(0.1f)] [SerializeField] private float bringInSpeed = 12f;
    private Creature pendingCreature;
    private float pendingAt;
    private Vector3 carryTargetLocal;
    private bool bringingIn;
    private Creature rightMouseCreature;
    private float rightMouseHeldTime;
    private CreatureThrower thrower;

    /// <summary>True when a held right click can throw (see chargedThrow).</summary>
    public bool ThrowEnabled => chargedThrow && !CarriesTentacle;
    public bool IsChargingThrow => ThrowEnabled && rightMouseCreature != null && rightMouseCreature == CarriedCreature && rightMouseHeldTime >= throwHoldThreshold;
    public float ThrowCharge01 => IsChargingThrow ? Mathf.InverseLerp(throwHoldThreshold, fullThrowHoldTime, rightMouseHeldTime) : 0f;

    private Vector3 carryPointHome;

    private Collider[] playerColliders;
    private Collider[] carriedColliders;
    private Rigidbody heldBody;
    private RigidbodyInterpolation heldInterpolation;
    private Quaternion heldYawOffset;
    private struct DroppedCollision { public Collider creature, player; public float restoreAt; }
    private readonly List<DroppedCollision> droppedCollisions = new List<DroppedCollision>();

    /// <summary>The player's Rigidbody, so carried creatures can help it move.</summary>
    public Rigidbody PlayerBody { get; private set; }

    /// <summary>The player's movement controller, or null.</summary>
    public SmoothFirstPersonController PlayerController { get; private set; }

    private void Awake()
    {
        thrower = GetComponent<CreatureThrower>();
        if (thrower == null) thrower = gameObject.AddComponent<CreatureThrower>();
        PlayerBody = GetComponentInParent<Rigidbody>();
        PlayerController = GetComponentInParent<SmoothFirstPersonController>();
        playerColliders = transform.root.GetComponentsInChildren<Collider>(true);
        if (carryCamera == null) carryCamera = Camera.main;
        if (carryPoint == null && carryCamera != null)
        {
            carryPoint = new GameObject("CreatureCarryPoint").transform;
            carryPoint.SetParent(carryCamera.transform, false);
            carryPoint.localPosition = new Vector3(0f, -0.35f, 2.5f);
        }
        if (carryPoint != null) carryPointHome = carryPoint.localPosition;
    }

    private void Update()
    {
        if (BoneAwakeningCutscene.Playing || RestSpotSystem.BlocksInteraction || MeditationController.BlocksPlayerInput)
        { CancelRightMouse(); return; }
        if (!Application.isFocused && !Application.isBatchMode)
        {
            CancelRightMouse();
            // A tentacle creature stays on the back (dropping it would end its stuck arms' rig by surprise).
            if (!CarriesTentacle) Release();
            return;
        }

        if (BoneRide.HandleInput(this)) { rideInputGuard = true; CancelRightMouse(); return; }
        // Releasing the mounting click must not become a putdown or throw.
        if (rideInputGuard)
        {
            if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;
            rideInputGuard = false;
        }

        if (CarriedCreature != null && !CarriedCreature.isActiveAndEnabled)
            ReleaseCarriedCreature();
        Mouse mouse = Mouse.current;
        if (mouse == null) { CancelRightMouse(); return; }
        UpdateRightMouse(mouse.rightButton.wasPressedThisFrame, mouse.rightButton.isPressed,
            mouse.rightButton.wasReleasedThisFrame, Time.deltaTime);
        UpdatePendingGrab();
    }

    private void OnValidate()
    {
        throwHoldThreshold = Mathf.Max(0.01f, throwHoldThreshold);
        fullThrowHoldTime = Mathf.Max(throwHoldThreshold + 0.01f, fullThrowHoldTime);
    }

    // One owner resolves every RMB gesture. A pickup press never arms a release gesture.
    private void UpdateRightMouse(bool pressed, bool held, bool released, float deltaTime)
    {
        if (rightMouseCreature != CarriedCreature) CancelRightMouse();
        if (CarriedCreature != null && CarriedCreature.TryHandleCarriedRightMouse(this, pressed, held, released))
        {
            CancelRightMouse();
            return;
        }
        if (Time.timeScale <= 0f)
        {
            // Releases during a pause are consumed, never deferred until resuming.
            if (released || !held) CancelRightMouse();
            return;
        }
        if (CarriedCreature != null)
        {
            if (pressed)
            {
                rightMouseCreature = CarriedCreature;
                rightMouseHeldTime = 0f;
            }
            else if (rightMouseCreature != null && (held || released))
                rightMouseHeldTime += Mathf.Max(0f, deltaTime);

            if (released && rightMouseCreature != null)
            {
                bool shouldThrow = IsChargingThrow;
                float charge = ThrowCharge01;
                CancelRightMouse();
                if (shouldThrow) { thrower.Throw(charge); AudioManager.PostEvent("Play_Drop", gameObject); }
                else Drop();
            }
            else if (!held) CancelRightMouse();
            return;
        }
        if (!pressed || pendingCreature != null) return;

        Creature creature = FindCreature();
        if (creature != null && !string.IsNullOrEmpty(creature.PickupActionPrompt))
        {
            BeginCarry(creature);
            return;
        }
        // The right hand reaches out on every press, towards the creature if there is one.
        LastGrabTime = Time.time;
        LastGrabPoint = creature != null ? creature.transform.position
            : carryCamera != null ? carryCamera.transform.position + carryCamera.transform.forward * 1.2f : transform.position;
        // The creature is taken only when the hand gets there, not at the moment of the click.
        pendingCreature = creature;
        pendingAt = Time.time + grabReachTime;
    }

    private void CancelRightMouse()
    {
        rightMouseCreature = null;
        rightMouseHeldTime = 0f;
    }

    private void UpdatePendingGrab()
    {
        if (pendingCreature == null) return;
        if (!pendingCreature.isActiveAndEnabled || pendingCreature.IsCarried || pendingCreature.IsBubbleCaptured)
        {
            pendingCreature = null;
            return;
        }
        LastGrabPoint = pendingCreature.transform.position; // The hand follows a creature that moves.
        if (Time.time < pendingAt) return;
        Creature creature = pendingCreature;
        pendingCreature = null;
        float reach = pickupRange + 1f;
        if (carryCamera != null && (creature.transform.position - carryCamera.transform.position).sqrMagnitude > reach * reach) return;
        BeginCarry(creature);
    }

    /// <summary>Brings a just-taken creature in from where it was grabbed instead of snapping it there.</summary>
    private void BringIn(float deltaTime)
    {
        if (!bringingIn || carryPoint == null) return;
        carryPoint.localPosition = Vector3.MoveTowards(carryPoint.localPosition, carryTargetLocal, bringInSpeed * deltaTime);
        if ((carryPoint.localPosition - carryTargetLocal).sqrMagnitude < 1e-4f) bringingIn = false;
    }

    /// <summary>The Creature the pickup input would grab now, or null.</summary>
    public Creature FindCreature()
    {
        if (carryCamera == null) return null;
        Ray ray = carryCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // The same forgiving aim as left click (CreatureAim): skips the player's own body and what is held, ignores
        // heat-volume triggers, and when the thin centre ray just misses a small creature, a slightly wider cast finds it
        // (never round a wall). A plain single ray used to miss small or thin colliders, so the prompt flickered.
        Creature creature = CreatureAim.Find(gameObject.scene.GetPhysicsScene(), ray, pickupRange, pickupLayers, this, out _);
        if (creature == null) return null;

if (creature == null || creature.IsBubbleCaptured || !creature.isActiveAndEnabled)
    return null;

if (!string.IsNullOrEmpty(creature.PickupActionPrompt)) return creature;
if (!creature.CanBeCarried || ReflectionSpotlight.IsFrozen(creature)) return null;

// Too big to hold in one hand: no prompt, no pickup. Tentacle creatures shrink onto the hand, so any size works.
// A Bellows can always be picked up, however full it is: it is a bag of air, and carrying it is how its stream is aimed.
if (!(creature is TentacleCreature) && !(creature is BellowsCreature) && MeasureCreature(creature, out _, out _) > maxCarrySize)
    return null;

return creature;
}

/// <summary>Largest side of the creature's visible shape. Also how far it rises above its pivot and spreads sideways.</summary>
private static float MeasureCreature(Creature creature, out float above, out float sideways)
{
    above = sideways = 0f;
    if (!CreatureBounds.TryMeasure(creature.transform, out Bounds bounds)) return 0f;

    Vector3 pivot = creature.transform.position;
    above = Mathf.Max(0f, bounds.max.y - pivot.y);
    sideways = Mathf.Max(
        Mathf.Max(bounds.max.x - pivot.x, pivot.x - bounds.min.x),
        Mathf.Max(bounds.max.z - pivot.z, pivot.z - bounds.min.z)
    );

    return Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
}

// Bigger creatures are held lower, further right and further out, so they never cover the crosshair.
private void FitCarryPoint(Creature creature)
{
    if (carryPoint == null) return;

    MeasureCreature(creature, out float above, out float sideways);
    heldCentreLocal = Vector3.zero;
    heldRadius = 0f;
    if (adaptiveHold && !(creature is TentacleCreature) && CreatureBounds.TryMeasure(creature.transform, out Bounds seen))
    {
        // Held by its visual middle (many pivots sit at the feet or off to one side), in front of the right hand.
        heldCentreLocal = Quaternion.Inverse(creature.transform.rotation) * (seen.center - creature.transform.position);
        heldRadius = Mathf.Max(seen.extents.x, Mathf.Max(seen.extents.y, seen.extents.z));
        carryPoint.localPosition = handRest + holdOutward.normalized * (heldRadius * 0.9f + 0.05f);
        return;
    }
    if (creature is BellowsCreature bellows)
    {
        // A swollen one is held further out and to the side, so it does not fill the view.
        float extra = Mathf.Max(0f, bellows.Radius - 0.6f);
        carryPoint.localPosition = bellowsHold + new Vector3(extra * 0.5f, -extra * 0.2f, extra * 1.2f);
        return;
    }
    if (flowerInCorner && creature is BubbleFlower)
    {
        // The flower's top sits at the corner point, so the whole flower stays in the lower right.
        carryPoint.localPosition = new Vector3(flowerCorner.x, flowerCorner.y - above, flowerCorner.z);
        return;
    }

    Vector3 home = carryPointHome;
    float z = Mathf.Max(home.z, home.z + sideways * 0.8f);
    float y = Mathf.Min(
        home.y,
        -(above + Mathf.Tan(clearBelowCrosshair * Mathf.Deg2Rad) * z)
    );
    float x = Mathf.Max(
        home.x,
        sideways + Mathf.Tan(clearRightOfCrosshair * Mathf.Deg2Rad) * z
    );

    carryPoint.localPosition = new Vector3(x, y, z);

}

    private void BeginCarry(Creature creature)
    {
        if (creature == null || creature.IsBubbleCaptured || !creature.isActiveAndEnabled) return;
        if (!string.IsNullOrEmpty(creature.PickupActionPrompt))
        {
            creature.ReceivePickupAttempt(this);
            return;
        }
        if (!creature.CanBeCarried || ReflectionSpotlight.IsFrozen(creature)) return;
        if (carryPoint == null) return;
        if (creature.TryGetComponent<CompostAbsorption>(out var absorption)) absorption.CancelForPickup();
        FitCarryPoint(creature);
        if (!creature.TryBeginCarry(this, carryPoint))
        {
            carryPoint.localPosition = carryPointHome;
            return;
        }
        CarriedCreature = creature;
        AudioManager.PostEvent("Play_PickUp", gameObject);
        CancelRightMouse();
        if (creature is TentacleCreature tentacle && TryGetComponent<TentacleTether>(out var tether)) tether.AdoptBalloons(tentacle);
        // Start where the hand took it and bring it in from there.
        carryTargetLocal = carryPoint.localPosition;
        carryPoint.position = creature.transform.position + creature.transform.rotation * heldCentreLocal;
        heldPull = 0f;
        bringingIn = true;
        // A held creature must never push the player: looking down onto it would lift the player into the air.
        carriedColliders = creature.GetComponentsInChildren<Collider>();
        SetPlayerCollision(carriedColliders, ignore: true);

        heldBody = creature.GetComponent<Rigidbody>();
        if (heldBody != null)
        {
            heldInterpolation = heldBody.interpolation;
            heldBody.interpolation = RigidbodyInterpolation.None;
            heldBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            heldBody.isKinematic = true; // EndCarry restores the creature's own setting.
        }
        heldYawOffset = Quaternion.Inverse(CarryYaw()) * creature.transform.rotation;
        SnapHeld();
    }

    private void FixedUpdate()
    {
        BoneRide.StepApproach(Time.fixedDeltaTime);
        RestoreDroppedCollisions();
    }

    // After the camera has moved this frame, so the held creature never trails behind the view.
    private void LateUpdate()
    {
        if (CarriedCreature == null) return;
        if (!CarriesTentacle) BringIn(Time.deltaTime);
        FollowHand(Time.deltaTime);
        SnapHeld();
    }

    /// <summary>A tentacle creature clings to the player's back (between the shoulder blades), out of view.</summary>
    private void FollowHand(float deltaTime)
    {
        if (carryPoint == null || !CarriesTentacle) return;
        Transform player = PlayerBody != null ? PlayerBody.transform : transform;
        Vector3 forward = carryCamera != null ? Vector3.ProjectOnPlane(carryCamera.transform.forward, Vector3.up) : player.forward;
        if (forward.sqrMagnitude < 1e-4f) forward = player.forward;
        forward.Normalize();
        // Low on the back (below the head), so its body never comes near the camera; only the arms reach forward.
        Vector3 back = player.position + Vector3.up * 0.02f - forward * 0.5f;
        // Right after the grab it climbs from the hand round to the back instead of jumping there.
        if (bringingIn)
        {
            carryPoint.position = Vector3.MoveTowards(carryPoint.position, back, bringInSpeed * deltaTime);
            if ((carryPoint.position - back).sqrMagnitude < 1e-3f) bringingIn = false;
        }
        else carryPoint.position = back;
    }

    public bool CarriesTentacle => CarriedCreature is TentacleCreature && !CarriedCreature.IsDead;

    /// <summary>The attached creature stays on the rider while the mouse buttons pull the reins.</summary>
    private bool rideInputGuard;
    public bool TentacleLockedForRide => CarriesTentacle && BoneRide.Busy;

    private void SnapHeld()
    {
        if (carryPoint == null) return;
        Quaternion rotation = CarryYaw() * heldYawOffset;
        Vector3 centre = carryPoint.position;
        if (adaptiveHold && heldRadius > 0f && !bringingIn) centre = KeepOutOfWalls(centre);
        // The carry point is where the creature's middle goes, not its pivot.
        Vector3 position = centre - rotation * heldCentreLocal;
        CarriedCreature.transform.SetPositionAndRotation(position, rotation);
        if (heldBody != null) heldBody.position = position;
    }

    private readonly RaycastHit[] holdHits = new RaycastHit[16];

    /// <summary>
    /// If the held creature would sink into a wall, the ground or anything solid, it comes back along the line from
    /// the camera until it is clear (never closer than just past the hand), and goes out again when there is room.
    /// </summary>
    private Vector3 KeepOutOfWalls(Vector3 wanted)
    {
        if (carryCamera == null) return wanted;
        Vector3 eye = carryCamera.transform.position;
        Vector3 toward = wanted - eye;
        float distance = toward.magnitude;
        if (distance < 0.01f) return wanted;
        Vector3 dir = toward / distance;
        float radius = Mathf.Max(0.05f, heldRadius * 0.75f);
        int count = ReflectionPhysics.SphereCastNonAlloc(eye, radius, dir, holdHits, distance, ~((1 << 2) | (1 << 6)), QueryTriggerInteraction.Ignore);
        float clear = distance;
        for (int i = 0; i < count; i++)
        {
            Collider c = holdHits[i].collider;
            if (c == null || holdHits[i].distance <= 0f) continue;
            if (CarriedCreature != null && c.transform.IsChildOf(CarriedCreature.transform)) continue;
            if (c.attachedRigidbody != null && (c.attachedRigidbody == PlayerBody || !c.attachedRigidbody.isKinematic)) continue;
            if (System.Array.IndexOf(playerColliders, c) >= 0) continue;
            clear = Mathf.Min(clear, holdHits[i].distance);
        }
        float minimum = Mathf.Min(distance, 0.35f);
        float target = Mathf.Max(minimum, clear);
        // Ease the change, so it slides back and out instead of jumping.
        heldPull = Mathf.MoveTowards(heldPull, distance - target, Time.deltaTime * (distance - target > heldPull ? 12f : 3f));
        return eye + dir * Mathf.Max(minimum, distance - heldPull);
    }

    private float heldPull;

    private Quaternion CarryYaw()
    {
        Vector3 forward = carryCamera != null ? carryCamera.transform.forward : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }

    /// <summary>Releases the creature and, if it is a free physics body, sends it slightly forward.</summary>
    public void Drop()
    {
        if (TentacleLockedForRide) return;
        Creature dropped = CarriedCreature;
        Release();
        if (dropped != null) AudioManager.PostEvent("Play_Drop", gameObject);
        if (dropped == null || carryCamera == null) return;

        Rigidbody body = dropped.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
            body.linearVelocity += carryCamera.transform.forward * dropForwardSpeed;
    }

    public void Release()
    {
        CancelRightMouse();
        pendingCreature = null;
        if (TentacleLockedForRide) return;
        ReleaseCarriedCreature();
    }

    private void ReleaseCarriedCreature()
    {
        CancelRightMouse();
        pendingCreature = null;
        Creature dropped = CarriedCreature;
        if (CarriedCreature != null) CarriedCreature.EndCarry(this);
        CarriedCreature = null;
        bringingIn = false;
        if (heldBody != null) heldBody.interpolation = heldInterpolation;
        heldBody = null;
        if (dropped is TentacleCreature && carriedColliders != null && playerColliders != null)
        {
            // A back-carried octopus grows to full size after release. Do not enable overlapping collision pairs.
            QueueDroppedCollisions(carriedColliders);
        }
        else SetPlayerCollision(carriedColliders, ignore: false);
        carriedColliders = null;
        if (carryPoint != null) carryPoint.localPosition = carryPointHome;
        if (dropped is TentacleCreature tentacle)
        {
            tentacle.PrepareForDrop();
            if (TryGetComponent<TentacleTether>(out var tether)) tether.PlaceDroppedCreature(tentacle);
        }
    }

    private void SetPlayerCollision(Collider[] colliders, bool ignore)
    {
        if (colliders == null || playerColliders == null) return;
        foreach (Collider carried in colliders)
        {
            if (carried == null) continue;
            foreach (Collider player in playerColliders)
                if (player != null) Physics.IgnoreCollision(carried, player, ignore);
        }
    }

    private void RestoreDroppedCollisions()
    {
        for (int i = droppedCollisions.Count - 1; i >= 0; i--)
        {
            DroppedCollision pair = droppedCollisions[i];
            if (pair.creature == null || pair.player == null) { droppedCollisions.RemoveAt(i); continue; }
            TentacleCreature creature = pair.creature.GetComponentInParent<TentacleCreature>();
            // Carry and the hanging rig now own these pairs; their release paths will restore them later.
            if (creature != null && (creature.IsCarried || creature.Suspended)) { droppedCollisions.RemoveAt(i); continue; }
            if (Time.time < pair.restoreAt) continue;
            if (CollidersPenetrate(pair.creature, pair.player)) continue;
            Physics.IgnoreCollision(pair.creature, pair.player, false);
            droppedCollisions.RemoveAt(i);
        }
    }

    public void DeferTentacleCollisionRestore(TentacleCreature creature)
    {
        Collider[] colliders = creature.GetComponentsInChildren<Collider>();
        SetPlayerCollision(colliders, true);
        QueueDroppedCollisions(colliders);
    }

    private void QueueDroppedCollisions(Collider[] colliders)
    {
        if (playerColliders == null) return;
        foreach (Collider creatureCollider in colliders)
            foreach (Collider playerCollider in playerColliders)
                if (creatureCollider != null && playerCollider != null)
                    droppedCollisions.Add(new DroppedCollision { creature = creatureCollider, player = playerCollider, restoreAt = Time.time + 0.5f });
    }

    public static bool CollidersPenetrate(Collider a, Collider b)
    {
        CollisionPose(a, out Vector3 pa, out Quaternion qa);
        CollisionPose(b, out Vector3 pb, out Quaternion qb);
        return Physics.ComputePenetration(a, pa, qa, b, pb, qb, out _, out float depth) && depth > 0.005f;
    }

    private static void CollisionPose(Collider collider, out Vector3 position, out Quaternion rotation)
    {
        position = collider.transform.position; rotation = collider.transform.rotation;
        Rigidbody body = collider.attachedRigidbody;
        if (body == null) return;
        // During FixedUpdate the rendered/interpolated transform can still show the previous physics pose.
        Quaternion delta = body.rotation * Quaternion.Inverse(body.transform.rotation);
        position = body.position + delta * (position - body.transform.position);
        rotation = delta * rotation;
    }

    private void OnDisable() { BoneRide.CancelApproach(); ReleaseCarriedCreature(); }
    private void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        if (BoneRide.Approaching) { BoneRide.CancelApproach(); rideInputGuard = true; }
        else if (!CarriesTentacle) Release();
    }
}
