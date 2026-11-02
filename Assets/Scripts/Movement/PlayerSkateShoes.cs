using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Optional momentum-based locomotion. The normal controller retains input and traversal ownership.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SmoothFirstPersonController))]
[DefaultExecutionOrder(1300)]
public sealed class PlayerSkateShoes : MonoBehaviour
{
    [Header("Gliding")]
    [SerializeField, Min(1f)] private float speedMultiplier = 1.7f;
    [SerializeField, Min(1f)] private float maximumSpeed = 32f;
    [SerializeField, Min(0f)] private float pushAcceleration = 13f;
    [SerializeField, Min(0f)] private float rollingResistance = 0.55f;
    [SerializeField, Min(0f)] private float brakeAcceleration = 19f;
    [SerializeField, Range(5f, 180f)] private float steeringDegreesPerSecond = 65f;
    [SerializeField, Min(0f)] private float airSteering = 2.5f;
    [Header("Slope takeoff")]
    [SerializeField, Min(0f)] private float minimumTakeoffSpeed = 9f;
    [SerializeField, Min(0f)] private float minimumUpwardSpeed = 1.4f;
    [SerializeField, Min(0f)] private float crestSeparationSpeed = 1.2f;
    [SerializeField, Min(0.01f)] private float takeoffGroundGrace = 0.16f;
    [Header("Equipment")]
    [SerializeField] private Key toggleKey = Key.K;
    [SerializeField] private bool equipped;
    private SmoothFirstPersonController controller;
    private Rigidbody body;
    private FirstPersonBody firstPersonBody;
    private SkateShoesVisuals visuals;
    private PhysicsMaterial skateMaterial;
    private readonly Dictionary<Collider, PhysicsMaterial> originalMaterials = new Dictionary<Collider, PhysicsMaterial>();
    private bool supportedLastStep;
    private Vector3 previousGroundVelocity;
    private Vector3 previousNormal = Vector3.up;
    private float ignoreGroundUntil;
    private GUIStyle statusStyle;
    public bool Equipped => equipped && isActiveAndEnabled;
    public bool SuppressGrounding => Equipped && Time.time < ignoreGroundUntil;
    public bool CanDrive => Equipped && controller != null && controller.enabled && body != null &&
        !body.isKinematic && !controller.Restricted && !controller.Climbing && !controller.WallRunning &&
        !controller.Flung && !controller.BubbleMovementBlocked && !BoneAwakeningCutscene.Playing &&
        controller.State != SmoothFirstPersonController.MovementState.Freeze &&
        controller.State != SmoothFirstPersonController.MovementState.Unlimited &&
        controller.State != SmoothFirstPersonController.MovementState.Sliding &&
        controller.State != SmoothFirstPersonController.MovementState.Crouching;
    public int RampTakeoffs { get; private set; }

    private void Awake()
    {
        controller = GetComponent<SmoothFirstPersonController>();
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable() { if (equipped) ApplyEquipment(); }

    public void Equip()
    {
        equipped = true;
        enabled = true;
        ApplyEquipment();
        LandmarkToast.Show("Skate shoes on - glide downhill, steer with WASD, S to brake, K to switch shoes");
    }

    public void SetEquipped(bool value)
    {
        if (value) Equip();
        else
        {
            equipped = false;
            RestoreEquipment();
            LandmarkToast.Show("Skate shoes off - K to wear them again");
        }
    }

    private void ApplyEquipment()
    {
        if (skateMaterial == null)
            skateMaterial = new PhysicsMaterial("Skate shoe friction")
            {
                dynamicFriction = 0.015f, staticFriction = 0.015f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger || collider.attachedRigidbody != body) continue;
            if (!originalMaterials.ContainsKey(collider)) originalMaterials.Add(collider, collider.sharedMaterial);
            collider.sharedMaterial = skateMaterial;
        }
        if (visuals == null)
        {
            var library = GameplayPrefabLibrary.Load();
            if (library != null && library.skateShoesVisual != null)
                visuals = Instantiate(library.skateShoesVisual, transform);
            else
            {
                var root = new GameObject("Worn skate shoes");
                root.transform.SetParent(transform, false);
                visuals = root.AddComponent<SkateShoesVisuals>();
            }
            visuals.name = "Worn skate shoes";
        }
        visuals.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (controller != null && controller.enabled && Application.isFocused && Time.timeScale > 0f &&
            !controller.BubbleMovementBlocked && !BoneAwakeningCutscene.Playing &&
            Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetEquipped(!equipped);
        if (!CanDrive) supportedLastStep = false;
    }

    /// <summary>Called only by the movement controller's fixed step; never runs a second movement loop.</summary>
    public bool Move(Vector3 input, float walkingSpeed, bool grounded, RaycastHit ground,
        Vector3 supportVelocity, float maximumSlopeAngle, float dt, bool allowRampTakeoff = true)
    {
        if (!CanDrive) { supportedLastStep = false; return false; }
        bool supported = grounded && !SuppressGrounding && ground.collider != null &&
            Vector3.Angle(ground.normal, Vector3.up) <= maximumSlopeAngle;
        Vector3 velocity = body.linearVelocity - supportVelocity;

        // A convex crest separates the old uphill tangent from the new surface. Keep that tangent;
        // a concave downhill-to-uphill transition stays supported and converts speed into height.
        if (allowRampTakeoff && supportedLastStep && previousGroundVelocity.magnitude >= minimumTakeoffSpeed &&
            previousGroundVelocity.y >= minimumUpwardSpeed &&
            (!supported || (Vector3.Angle(previousNormal, ground.normal) > 5f &&
                Vector3.Dot(previousGroundVelocity, ground.normal) > crestSeparationSpeed)))
        {
            velocity = previousGroundVelocity;
            body.linearVelocity = velocity + supportVelocity;
            ignoreGroundUntil = Time.time + takeoffGroundGrace;
            controller.BeginSkateLaunch();
            supported = false;
            RampTakeoffs++;
        }

        if (supported)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, ground.normal);
            // Transport momentum around modest surface bends, avoiding loss at tessellated ramp seams.
            if (supportedLastStep && Vector3.Angle(previousNormal, ground.normal) < 55f && tangent.sqrMagnitude > 0.01f)
                tangent = tangent.normalized * Mathf.Max(tangent.magnitude, previousGroundVelocity.magnitude);
            tangent += Vector3.ProjectOnPlane(Physics.gravity, ground.normal) * dt;
            tangent = Vector3.MoveTowards(tangent, Vector3.zero, rollingResistance * dt);
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 wish = Vector3.ProjectOnPlane(input, ground.normal).normalized;
                float speed = tangent.magnitude;
                float alignment = speed > 0.1f ? Vector3.Dot(tangent / speed, wish) : 1f;
                if (alignment < -0.25f)
                    tangent = Vector3.MoveTowards(tangent, Vector3.zero, brakeAcceleration * dt);
                else
                {
                    if (speed > 0.1f)
                        tangent = Vector3.RotateTowards(tangent, wish * speed,
                            steeringDegreesPerSecond * Mathf.Deg2Rad * dt, 0f);
                    float push = Mathf.Clamp(walkingSpeed * speedMultiplier - tangent.magnitude, 0f, pushAcceleration * dt);
                    tangent += wish * push;
                }
            }
            velocity = Vector3.ClampMagnitude(tangent, maximumSpeed);
            previousGroundVelocity = velocity;
            previousNormal = ground.normal;
            body.useGravity = false; // Gravity along the surface was integrated above.
        }
        else
        {
            body.useGravity = true;
            // In the air, steering bends horizontal momentum gently without braking a launch.
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (input.sqrMagnitude > 0.01f && flat.sqrMagnitude > 0.01f)
                flat = Vector3.RotateTowards(flat, input.normalized * flat.magnitude,
                    airSteering * dt / Mathf.Max(1f, flat.magnitude), 0f);
            velocity = new Vector3(flat.x, velocity.y, flat.z);
        }
        body.linearDamping = 0f;
        body.linearVelocity = velocity + supportVelocity;
        supportedLastStep = supported;
        return true;
    }

    private void LateUpdate()
    {
        if (!Equipped || visuals == null) return;
        if (firstPersonBody == null) firstPersonBody = transform.root.GetComponentInChildren<FirstPersonBody>();
        Transform facing = controller.Orientation != null ? controller.Orientation : transform;
        for (int i = 0; i < 2; i++)
        {
            Vector3 position;
            Quaternion rotation;
            if (firstPersonBody == null || !firstPersonBody.TryGetFootPose(i == 1, out position, out rotation))
            {
                position = transform.position + Vector3.down * 0.9f + facing.right * (i == 0 ? -0.18f : 0.18f);
                rotation = Quaternion.LookRotation(facing.forward, Vector3.up);
            }
            visuals.SetFootPose(i, position + Vector3.up * 0.07f, rotation);
        }
    }

    private void RestoreEquipment()
    {
        foreach (var entry in originalMaterials)
            if (entry.Key != null && entry.Key.sharedMaterial == skateMaterial) entry.Key.sharedMaterial = entry.Value;
        originalMaterials.Clear();
        supportedLastStep = false;
        ignoreGroundUntil = 0f;
        if (visuals != null) visuals.gameObject.SetActive(false);
        if (body != null && !body.isKinematic && controller != null && !controller.Climbing && !controller.WallRunning)
            body.useGravity = true;
    }

    private void OnGUI()
    {
        if (controller == null || !controller.enabled || BoneAwakeningCutscene.Playing) return;
        if (statusStyle == null) statusStyle = new GUIStyle(GUI.skin.label)
        { fontSize = 16, normal = { textColor = new Color(0.6f, 1f, 0.9f) } };
        string status = Equipped ? $"Skate shoes  {body.linearVelocity.magnitude:0.0} m/s    [S] Brake    [K] Take off"
            : "Skate shoes stored    [K] Put on";
        GUI.Label(new Rect(20f, Screen.height - 145f, 560f, 28f), status, statusStyle);
    }

    private void OnDisable() => RestoreEquipment();
    private void OnDestroy()
    {
        RestoreEquipment();
        if (visuals != null) Destroy(visuals.gameObject);
        if (skateMaterial != null) Destroy(skateMaterial);
    }
}
