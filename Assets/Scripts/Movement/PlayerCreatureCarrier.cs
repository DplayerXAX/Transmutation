using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player. Holds any Creature at an anchor without disabling its
/// simulation, colliders, animation, or heat reactions. Hold right click to carry,
/// or click right (or press a key) once to pick up and again to drop.
/// A tentacle creature is not held out in front: it shrinks and sits on the right hand.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerCreatureCarrier : MonoBehaviour
{
    public enum CarryInput
    {
        HoldRightMouse,
        ToggleKey,
        ToggleRightMouse
    }

    [SerializeField] private Camera carryCamera;
    [Tooltip("Empty transform in front of the camera. Created automatically if unassigned.")]
    [SerializeField] private Transform carryPoint;
    [Min(0f)] [SerializeField] private float pickupRange = 5f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    [Header("Input")]
    [SerializeField] private CarryInput carryInput = CarryInput.HoldRightMouse;
    [Tooltip("Pick up / drop key used by the Toggle Key mode.")]
    [SerializeField] private Key toggleKey = Key.E;
    [Tooltip("Forward speed given to a physics creature when it is dropped in Toggle Key mode.")]
    [Min(0f)] [SerializeField] private float dropForwardSpeed = 1.5f;

    [Header("Grip")]
    [Tooltip("Lock the held creature to the hand every frame instead of pulling it there with a spring. " +
             "Feels solid and has no lag, but the held creature can pass through walls.")]
    [SerializeField] private bool firmGrip;

    [Header("Big Creatures")]
    [Tooltip("Creatures larger than this (metres, largest side) cannot be picked up.")]
    [Min(0.1f)] [SerializeField] private float maxCarrySize = 2.2f;
    [Tooltip("Degrees below the crosshair the top of a held creature is kept.")]
    [Range(0f, 30f)] [SerializeField] private float clearBelowCrosshair = 8f;
    [Tooltip("Degrees right of the crosshair the left side of a held creature is kept.")]
    [Range(0f, 30f)] [SerializeField] private float clearRightOfCrosshair = 6f;

    public Creature CarriedCreature { get; private set; }

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
    [Min(0.1f)] [SerializeField] private float bringInSpeed = 4f;
    private Creature pendingCreature;
    private float pendingAt;
    private Vector3 carryTargetLocal;
    private bool bringingIn;

    private Vector3 carryPointHome;

    private Collider[] playerColliders;
    private Collider[] carriedColliders;
    private Rigidbody heldBody;
    private RigidbodyInterpolation heldInterpolation;
    private Quaternion heldYawOffset;
    public CarryInput InputMode => carryInput;
    public Key ToggleKeyBinding => toggleKey;

    /// <summary>The player's Rigidbody, so carried creatures can help it move.</summary>
    public Rigidbody PlayerBody { get; private set; }

    /// <summary>The player's movement controller, or null.</summary>
    public SmoothFirstPersonController PlayerController { get; private set; }

    private void Awake()
    {
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
        if (!Application.isFocused && !Application.isBatchMode)
        {
            Release();
            return;
        }

        if (carryInput == CarryInput.HoldRightMouse)
            UpdateHoldRightMouse();
        else
            UpdateToggle(carryInput == CarryInput.ToggleRightMouse);
        UpdatePendingGrab();

        if (CarriedCreature != null && !CarriedCreature.isActiveAndEnabled)
            Release();
    }

    private void UpdateToggle(bool rightMouse)
    {
        bool pressed;
        if (rightMouse) pressed = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        else pressed = Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame;
        if (!pressed) return;

        if (CarriedCreature != null)
        {
            Drop();
            return;
        }
        if (pendingCreature != null) return;

        Creature creature = FindCreature();
        // The right hand reaches out on every press, towards the creature if there is one.
        LastGrabTime = Time.time;
        LastGrabPoint = creature != null ? creature.transform.position
            : carryCamera != null ? carryCamera.transform.position + carryCamera.transform.forward * 1.2f : transform.position;
        // The creature is taken only when the hand gets there, not at the moment of the click.
        pendingCreature = creature;
        pendingAt = Time.time + grabReachTime;
    }

    private void UpdatePendingGrab()
    {
        if (pendingCreature == null) return;
        if (!pendingCreature.isActiveAndEnabled || pendingCreature.IsCarried)
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

    private void UpdateHoldRightMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            Release();
            return;
        }

        // Keep trying while held so a moving Bubble can enter the pickup ray
        // after the initial click.
        if (mouse.rightButton.isPressed && CarriedCreature == null)
        {
            Creature creature = FindCreature();
            if (creature != null) BeginCarry(creature);
        }

        if (!mouse.rightButton.isPressed) Release();
    }

    /// <summary>The Creature the pickup input would grab now, or null.</summary>
    public Creature FindCreature()
    {
        if (carryCamera == null) return null;
        Ray ray = carryCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // Ignore heat-volume triggers: pick the visible creature's solid body.
if (!Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayers, QueryTriggerInteraction.Ignore))
    return null;

Creature creature = hit.collider.GetComponentInParent<Creature>();

if (creature == null || !creature.CanBeCarried)
    return null;

// Too big to hold in one hand: no prompt, no pickup. Tentacle creatures shrink onto the hand, so any size works.
if (!(creature is TentacleCreature) && MeasureCreature(creature, out _, out _) > maxCarrySize)
    return null;

return creature;
}

/// <summary>Largest side of the creature's visible shape. Also how far it rises above its pivot and spreads sideways.</summary>
private static float MeasureCreature(Creature creature, out float above, out float sideways)
{
    above = sideways = 0f;
    Bounds bounds = default;
    bool any = false;

    foreach (Renderer renderer in creature.GetComponentsInChildren<Renderer>())
    {
        if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer)
            continue;

        if (!any) bounds = renderer.bounds;
        else bounds.Encapsulate(renderer.bounds);

        any = true;
    }

    if (!any) return 0f;

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
        if (carryPoint == null) return;
        FitCarryPoint(creature);
        if (!creature.TryBeginCarry(this, carryPoint))
        {
            carryPoint.localPosition = carryPointHome;
            return;
        }
        CarriedCreature = creature;
        // Start where the hand took it and bring it in from there.
        carryTargetLocal = carryPoint.localPosition;
        carryPoint.position = creature.transform.position;
        bringingIn = true;
        // A held creature must never push the player: looking down onto it would lift the player into the air.
        carriedColliders = creature.GetComponentsInChildren<Collider>();
        SetPlayerCollision(carriedColliders, ignore: true);

        if (firmGrip)
        {
            heldBody = creature.GetComponent<Rigidbody>();
            if (heldBody != null)
            {
                heldInterpolation = heldBody.interpolation;
                heldBody.interpolation = RigidbodyInterpolation.None;
                heldBody.isKinematic = true; // EndCarry restores the creature's own setting.
            }
            heldYawOffset = Quaternion.Inverse(CarryYaw()) * creature.transform.rotation;
            SnapHeld();
        }
    }

    private void FixedUpdate()
    {
        if (CarriedCreature == null || firmGrip) return;
        if (!CarriesTentacle) BringIn(Time.fixedDeltaTime);
        FollowHand(Time.fixedDeltaTime);
        if (carryPoint == null || !CarriedCreature.PullTowards(this, carryPoint)) Release();
    }

    // After the camera has moved this frame, so the held creature never trails behind the view.
    private void LateUpdate()
    {
        if (CarriedCreature == null) return;
        if (firmGrip && !CarriesTentacle) BringIn(Time.deltaTime);
        FollowHand(Time.deltaTime);
        if (firmGrip) SnapHeld();
    }

    /// <summary>A tentacle creature clings to the player's back (between the shoulder blades), out of view.</summary>
    private void FollowHand(float deltaTime)
    {
        if (carryPoint == null || !(CarriedCreature is TentacleCreature)) return;
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

    public bool CarriesTentacle => CarriedCreature is TentacleCreature;

    private void SnapHeld()
    {
        if (carryPoint == null) return;
        CarriedCreature.transform.SetPositionAndRotation(carryPoint.position, CarryYaw() * heldYawOffset);
        if (heldBody != null) heldBody.position = carryPoint.position;
    }

    private Quaternion CarryYaw()
    {
        Vector3 forward = carryCamera != null ? carryCamera.transform.forward : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }

    /// <summary>Releases the creature and, if it is a free physics body, sends it slightly forward.</summary>
    public void Drop()
    {
        Creature dropped = CarriedCreature;
        Release();
        if (dropped == null || carryCamera == null) return;

        Rigidbody body = dropped.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
            body.linearVelocity += carryCamera.transform.forward * dropForwardSpeed;
    }

    public void Release()
    {
        if (CarriedCreature != null) CarriedCreature.EndCarry(this);
        CarriedCreature = null;
        bringingIn = false;
        if (heldBody != null) heldBody.interpolation = heldInterpolation;
        heldBody = null;
        SetPlayerCollision(carriedColliders, ignore: false);
        carriedColliders = null;
        if (carryPoint != null) carryPoint.localPosition = carryPointHome;
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

    private void OnDisable() => Release();
    private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
}
