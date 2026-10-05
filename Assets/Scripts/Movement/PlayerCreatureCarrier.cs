using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player. Holds any Creature at an anchor without disabling its
/// simulation, colliders, animation, or heat reactions. Either hold right click to
/// carry, or press a key once to pick up and again to drop.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerCreatureCarrier : MonoBehaviour
{
    public enum CarryInput
    {
        HoldRightMouse,
        ToggleKey
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

    public Creature CarriedCreature { get; private set; }

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
    }

    private void Update()
    {
        if (!Application.isFocused)
        {
            Release();
            return;
        }

        if (carryInput == CarryInput.ToggleKey)
            UpdateToggle();
        else
            UpdateHoldRightMouse();

        if (CarriedCreature != null && !CarriedCreature.isActiveAndEnabled)
            Release();
    }

    private void UpdateToggle()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame) return;

        if (CarriedCreature != null)
        {
            Drop();
            return;
        }

        Creature creature = FindCreature();
        if (creature != null) BeginCarry(creature);
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
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayers, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Creature>();
        return null;
    }

    private void BeginCarry(Creature creature)
    {
        if (carryPoint == null || !creature.TryBeginCarry(this, carryPoint)) return;
        CarriedCreature = creature;
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
        if (carryPoint == null || !CarriedCreature.PullTowards(this, carryPoint)) Release();
    }

    // After the camera has moved this frame, so the held creature never trails behind the view.
    private void LateUpdate()
    {
        if (firmGrip && CarriedCreature != null) SnapHeld();
    }

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
        if (heldBody != null) heldBody.interpolation = heldInterpolation;
        heldBody = null;
        SetPlayerCollision(carriedColliders, ignore: false);
        carriedColliders = null;
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
