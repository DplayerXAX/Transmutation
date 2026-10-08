using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Procedural first-person arms, hands and legs built from primitives and posed with two-bone IK.
/// Arms hang from the camera so they stay in view; legs hang from the body so they show when looking down.
/// Hands follow whatever the player is doing: idle sway while walking, the right hand holding a carried
/// creature, a quick reach on interact, and real grips on the wall while <see cref="HandClimber"/> climbs.
/// </summary>
[DefaultExecutionOrder(1200)]
public sealed class FirstPersonBody : MonoBehaviour
{
    [Header("References (found automatically when empty)")]
    [SerializeField] private SmoothFirstPersonController controller;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private PlayerCreatureCarrier carrier;
    [SerializeField] private HandClimber climber;

    [Header("Look")]
    [SerializeField] private Material limbMaterial;
    [SerializeField] private Material jointMaterial;
    [Min(0.01f)] [SerializeField] private float armThickness = 0.075f;
    [Min(0.01f)] [SerializeField] private float legThickness = 0.11f;
    [SerializeField] private bool showLegs = true;

    [Header("Arms (camera space)")]
    [SerializeField] private Vector3 shoulderOffset = new Vector3(0.24f, -0.3f, -0.05f);
    [Tooltip("Extra shoulder width when the arms hang from the body (walking, running, climbing), so they read as coming from real shoulders.")]
    [Min(0f)] [SerializeField] private float shoulderWiden = 0.08f;
    [Tooltip("How far out from the body the hanging hands rest, metres.")]
    [Min(0f)] [SerializeField] private float armHangOut = 0.14f;
    [SerializeField] private Vector3 idleHandOffset = new Vector3(0.24f, -0.34f, 0.5f);
    [Min(0.05f)] [SerializeField] private float upperArmLength = 0.36f;
    [Min(0.05f)] [SerializeField] private float forearmLength = 0.34f;
    [Tooltip("How quickly hands chase their targets.")]
    [Min(0.1f)] [SerializeField] private float handFollow = 18f;

    [Header("Legs (body space)")]
    [SerializeField] private float hipHeight = -0.05f;
    [SerializeField] private float hipWidth = 0.14f;
    [Min(0.05f)] [SerializeField] private float thighLength = 0.5f;
    [Min(0.05f)] [SerializeField] private float shinLength = 0.48f;
    [Min(0.05f)] [SerializeField] private float strideLength = 0.9f;
    [Tooltip("Steps per second when walking and at full sprint. Faster movement takes longer strides instead of " +
             "ever quicker steps. 2 per second lands on the beat of the 120 BPM music (footsteps follow these steps).")]
    [SerializeField] private Vector2 stepRate = new Vector2(2f, 3f);
    [Tooltip("Speeds (m/s) that count as walking and as full sprint, for the step rate above.")]
    [SerializeField] private Vector2 gaitSpeeds = new Vector2(5f, 7.5f);
    // How long each stride is right now (metres), which sets how far arms and legs swing.
    private float currentStride = 0.9f;

    private sealed class Limb
    {
        public Transform upper, lower, elbow, end;
        public Transform[] fingers;     // Two segments per finger, null for legs.
        public Vector3 current;         // Smoothed target, relative to the camera (arms) or the body (legs).
        public bool started;
        public float grip;              // 0 open, 1 closed.
        public float bodyFrame;         // Arms: 0 = hang from the camera, 1 = from the body.
        // Fingers (index to little): how closed each one should be and is (0 open .. 1 fist), and how splayed.
        public readonly float[] fingerGoal = new float[4];
        public readonly float[] fingerCurl = new float[4];
        public float spreadGoal, spread;
    }

    // Index fingers move first and fastest, little fingers last, so grabs ripple across the hand.
    private static readonly float[] FingerSpeed = { 9f, 7.5f, 6f, 5f };

    private Limb leftArm, rightArm, leftLeg, rightLeg;
    private Transform root;
    private Rigidbody body;
    private float walkPhase;
    /// <summary>Leg cycle in radians. A foot plants each time it passes PI/2 + n*PI (even n = right).</summary>
    public float WalkPhase => walkPhase;
    private float interactTimer;
    // Seconds off the ground, so short hops over bumps do not throw the arms up.
    private float airTime;
    private const float GrabTime = 0.45f;
    private PlayerCreaturePetter petter;
    // Vertical speed while airborne, and the landing that follows a real fall or jump.
    private float airSpeed;
    private float airPhase;
    private float landTimer;
    private float landStrength;
    // 0..1 values shared by arms and legs this frame.
    private float rising, falling, airborne, landing;
    private const float LandTime = 0.4f;
    private MaterialPropertyBlock block;
    private Renderer[] renderers;

    private void Awake()
    {
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (climber == null) climber = FindFirstObjectByType<HandClimber>();
        body = controller != null ? controller.Body : null;
        if (body == null && controller != null) body = controller.GetComponent<Rigidbody>();

        root = new GameObject("First Person Body").transform;
        leftArm = BuildLimb("Left Arm", armThickness, hand: true, side: -1f);
        rightArm = BuildLimb("Right Arm", armThickness, hand: true, side: 1f);
        if (showLegs)
        {
            leftLeg = BuildLimb("Left Leg", legThickness, hand: false, side: -1f);
            rightLeg = BuildLimb("Right Leg", legThickness, hand: false, side: 1f);
        }
        renderers = root.GetComponentsInChildren<Renderer>();
        // Tentacle creatures ride on the right hand.
        if (carrier != null)
        {
            carrier.HandAnchor = rightArm.end;
            carrier.LeftHandAnchor = leftArm.end;
        }
        // The left hand pets creatures.
        petter = FindFirstObjectByType<PlayerCreaturePetter>();
        if (petter == null) petter = gameObject.AddComponent<PlayerCreaturePetter>();
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private void LateUpdate()
    {
        if (controller == null || viewCamera == null) return;
        float deltaTime = Time.deltaTime;
        Transform view = viewCamera.transform;

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        bool grounded = controller.Grounded;
        bool climbing = climber != null && climber.IsClimbing;
        if (grounded && !climbing)
        {
            // Below walking speed steps slow down with the speed; above it the stride grows instead.
            float rate = speed < gaitSpeeds.x
                ? stepRate.x * speed / Mathf.Max(0.01f, gaitSpeeds.x)
                : Mathf.Lerp(stepRate.x, stepRate.y, Mathf.InverseLerp(gaitSpeeds.x, gaitSpeeds.y, speed));
            walkPhase += rate * Mathf.PI * deltaTime;
            float stride = rate > 0.05f ? speed / rate : strideLength;
            currentStride = Mathf.Lerp(currentStride, stride, 1f - Mathf.Exp(-4f * deltaTime));
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) interactTimer = 0.3f;
        interactTimer = Mathf.Max(0f, interactTimer - deltaTime);
        bool inAir = !grounded && !climbing;
        // Landing after a real jump or fall: knees bend, arms dip, harder the faster the fall.
        if (!inAir && airTime > 0.35f)
        {
            landTimer = LandTime;
            landStrength = climbing ? 0f : Mathf.Clamp01(0.3f - airSpeed / 10f);
        }
        if (inAir) airSpeed = velocity.y;
        airTime = inAir ? airTime + deltaTime : 0f;
        landTimer = Mathf.Max(0f, landTimer - deltaTime);
        // Flailing gets faster with the speed of the fall.
        airPhase += deltaTime * (6f + Mathf.Clamp(-velocity.y, 0f, 15f) * 0.6f);
        airborne = Mathf.Clamp01((airTime - 0.15f) / 0.25f);
        rising = airborne * Mathf.Clamp01(airSpeed / 4f);
        falling = airborne * Mathf.Clamp01(-airSpeed / 8f);
        landing = landTimer > 0f ? Mathf.Sin(landTimer / LandTime * Mathf.PI) * landStrength : 0f;

        UpdateArm(leftArm, -1f, view, speed, grounded, climbing, deltaTime);
        UpdateArm(rightArm, 1f, view, speed, grounded, climbing, deltaTime);
        if (showLegs)
        {
            UpdateLeg(leftLeg, -1f, speed, grounded, climbing, deltaTime);
            UpdateLeg(rightLeg, 1f, speed, grounded, climbing, deltaTime);
        }

        block ??= new MaterialPropertyBlock();
        foreach (Renderer part in renderers)
        {
            part.GetPropertyBlock(block);
            block.SetVector("_Center", view.position);
            block.SetFloat("_Activity", climbing ? 1f : Mathf.Clamp01(speed / 8f));
            part.SetPropertyBlock(block);
        }
        LimbsPosed?.Invoke();
    }

    /// <summary>Raised after the arms and legs are posed each frame, for things that must stick to them exactly.</summary>
    public static event System.Action LimbsPosed;

    // ---------------- Arms ----------------

    private void UpdateArm(Limb arm, float side, Transform view, float speed, bool grounded, bool climbing, float deltaTime)
    {
        // A carried tentacle creature sits on the back now, so the right hand is free.
        bool tentacleOnHand = false;
        bool carrying = side > 0f && carrier != null && carrier.CarriedCreature != null && !carrier.CarriesTentacle;
        bool petting = side < 0f && petter != null && petter.IsPetting && !climbing;
        bool poking = side < 0f && interactTimer > 0f && !petting;
        // Right hand reaching out for a creature when the pickup button is pressed.
        float grabAge = side > 0f && carrier != null ? Time.time - carrier.LastGrabTime : 10f;
        // Ends as soon as the creature is in hand; from then on the holding pose takes over.
        bool grabbing = grabAge < GrabTime && !climbing && carrier.CarriedCreature == null;
        carrying |= grabbing;
        // Arms hang from the body (so looking around does not swing them), except while holding or poking something in view.
        arm.bodyFrame = Mathf.MoveTowards(arm.bodyFrame, carrying || poking || petting || (tentacleOnHand && !climbing) ? 0f : 1f, deltaTime * 5f);

        Vector3 eye = view.position;
        Transform orientation = controller.Orientation != null ? controller.Orientation : controller.transform;
        Vector3 bodyRight = orientation.right, bodyUp = Vector3.up, bodyForward = orientation.forward;
        if (climbing && climber != null) climber.GetClimbFrame(out eye, out bodyRight, out bodyUp, out bodyForward);
        Vector3 bodyShoulder = eye + bodyRight * ((shoulderOffset.x + shoulderWiden) * side) + bodyUp * shoulderOffset.y + bodyForward * shoulderOffset.z;
        Vector3 viewShoulder = view.TransformPoint(Vector3.Scale(shoulderOffset, new Vector3(side, 1f, 1f)));
        Vector3 shoulder = Vector3.Lerp(viewShoulder, bodyShoulder, arm.bodyFrame);
        // Elbows point down and out.
        Vector3 pole = Vector3.Lerp(-view.up + view.right * side * 0.6f - view.forward * 0.2f,
                                    -bodyUp + bodyRight * side * 0.6f - bodyForward * 0.2f, arm.bodyFrame);
        Vector3 target;
        Vector3 palmNormal = -view.forward;
        float grip = 0.15f;
        float spread = 0f;
        int pointing = -1;   // A finger that stays straight (index finger when poking).
        float flutter = 0f;  // Small independent finger movement.

        if (climbing && climber.GetHand(side > 0f ? 1 : 0, out Vector3 hold, out Vector3 wallNormal, out bool gripping))
        {
            target = hold + wallNormal * 0.04f;
            palmNormal = wallNormal;
            if (gripping)
            {
                // Holding on: a full grip that tightens and trembles a little while the arms pull.
                float strain = climber.PullStrain;
                grip = 0.92f + strain * 0.08f;
                flutter = strain * 0.04f;
            }
            else
            {
                // Reaching: fingers open wide, ready to catch the hold.
                grip = 0.05f;
                spread = 1f;
            }
        }
        else if (petting)
        {
            // Stroking: the palm rests on the creature and slides back and forth along its surface.
            Vector3 normal = petter.TouchNormal;
            Vector3 along = Vector3.ProjectOnPlane(view.up, normal);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(view.forward, normal);
            along.Normalize();
            Vector3 across = Vector3.Cross(normal, along);
            float stroke = Mathf.Sin(Time.time * 4.2f);
            target = petter.TouchPoint + normal * 0.05f + along * (stroke * 0.08f) + across * (Mathf.Sin(Time.time * 2.1f) * 0.03f);
            palmNormal = normal;
            grip = 0.25f + 0.1f * Mathf.Max(0f, stroke);
            flutter = 0.08f;
        }
        else if (grabbing)
        {
            // Reach out towards what was clicked, open, then close the hand and bring it back.
            float reachOut = Mathf.Sin(Mathf.Clamp01(grabAge / GrabTime) * Mathf.PI);
            Vector3 rest = view.TransformPoint(Vector3.Scale(idleHandOffset, new Vector3(side, 1f, 1f)));
            Vector3 toGoal = carrier.LastGrabPoint - shoulder;
            Vector3 goal = shoulder + Vector3.ClampMagnitude(toGoal, (upperArmLength + forearmLength) * 0.97f);
            target = Vector3.Lerp(rest, goal, reachOut);
            palmNormal = -toGoal.normalized;
            grip = grabAge < GrabTime * 0.45f ? 0.05f : 0.85f;
            spread = grabAge < GrabTime * 0.45f ? 1f : 0f;
        }
        else if (tentacleOnHand && !climbing)
        {
            // Holding the tentacle creature up a little so it can be seen coiled round the hand.
            target = view.TransformPoint(new Vector3(0.28f, -0.42f, 0.46f) + new Vector3(0f, Mathf.Sin(Time.time * 1.3f) * 0.01f, 0f));
            palmNormal = -view.up;
            grip = 0.55f;
            flutter = 0.06f;
        }
        else if (carrying)
        {
            // Right hand closes around the held creature, pulled back towards the shoulder if it drifts away.
            Vector3 held = carrier.CarriedCreature.transform.position;
            Vector3 toHeld = held - shoulder;
            float reach = (upperArmLength + forearmLength) * 0.95f;
            target = shoulder + Vector3.ClampMagnitude(toHeld, reach) - toHeld.normalized * 0.12f;
            palmNormal = -toHeld.normalized;
            grip = 0.85f;
            flutter = 0.02f;
        }
        else if (poking)
        {
            // Left hand pokes forward on interact.
            float poke = Mathf.Sin(interactTimer / 0.3f * Mathf.PI);
            Vector3 idle = Vector3.Scale(idleHandOffset, new Vector3(side, 1f, 1f)) + new Vector3(0.12f, 0.1f, 0.25f) * poke;
            target = view.TransformPoint(idle);
            grip = 0.8f;
            pointing = 0;
        }
        else
        {
            // Hands hang loosely at the sides. Walking and running swing them forward and back in turn,
            // each arm against the leg on its own side; at the front of a swing the hand comes up into view.
            float run = grounded ? Mathf.Clamp01(speed / 5f) : 0f;
            float swing = Mathf.Sin(walkPhase + (side > 0f ? Mathf.PI : 0f));
            float forward = Mathf.Max(0f, swing);
            // 0 at a walking stride, 1 at a long running stride: bigger swings.
            float reach = Mathf.InverseLerp(strideLength, strideLength * 2.2f, currentStride);
            // Arms hang a little out from the body, from shoulders wider than the hips.
            Vector3 hang = bodyShoulder - bodyUp * (upperArmLength + forearmLength) * 0.9f + bodyRight * (armHangOut * side) + bodyForward * 0.06f;
            // Running: elbows bent and fists pumping in front of the chest, swinging in towards the middle,
            // so at the top of each swing the hand comes up into the bottom of the view, first-person style.
            Vector3 pump = bodyShoulder
                + bodyForward * (0.24f + (0.26f + 0.12f * reach) * swing)
                + bodyUp * (-0.4f + (0.24f + 0.1f * reach) * forward - 0.1f * Mathf.Max(0f, -swing))
                - bodyRight * (side * (0.03f + 0.12f * forward));
            target = Vector3.Lerp(hang, pump, run);
            target += bodyUp * (Mathf.Sin(Time.time * 1.7f + side) * 0.006f);
            // In the air (only a real jump or fall, not a short drop over a bump): arms out for balance,
            // swung forward and up on the way up, thrown up and flailing on the way down.
            float calm = airborne * (1f - Mathf.Max(rising, falling));
            target += (bodyUp * 0.2f + bodyRight * (0.15f * side) + bodyForward * 0.08f) * calm;
            target += (bodyUp * 0.3f + bodyForward * 0.25f + bodyRight * (0.06f * side)) * rising;
            target += (bodyUp * 0.5f + bodyRight * (0.28f * side)) * falling;
            target += (bodyUp * Mathf.Sin(airPhase + side * 1.7f) * 0.09f + bodyForward * Mathf.Cos(airPhase * 1.3f + side) * 0.1f) * falling;
            // Landing: hands drop and come forward to take the jolt.
            target += (bodyForward * 0.18f - bodyUp * 0.12f + bodyRight * (0.05f * side)) * landing;
            // palmNormal is the way the back of the hand faces: outward, so the palms face the legs.
            palmNormal = Vector3.Slerp(bodyRight * side, -bodyUp, falling);
            // Loose and relaxed at rest, curled a bit more when running, splayed and fluttering when falling.
            grip = Mathf.Lerp(0.3f, 0.8f, run); // loose at rest, loose fists when running
            grip = Mathf.Lerp(grip, 0.05f, falling);
            grip = Mathf.Lerp(grip, 0.65f, landing);
            spread = falling;
            flutter = 0.05f + falling * 0.12f;
        }

        // Smooth in camera space, so the hands never trail behind when the view moves fast.
        Vector3 localTarget = view.InverseTransformPoint(target);
        arm.current = arm.started ? Vector3.Lerp(arm.current, localTarget, 1f - Mathf.Exp(-handFollow * deltaTime)) : localTarget;
        arm.started = true;
        arm.grip = Mathf.MoveTowards(arm.grip, grip, deltaTime * 6f);
        for (int f = 0; f < 4; f++)
        {
            float wobble = Mathf.Sin(Time.time * (2.1f + f * 0.7f) + f * 1.3f + side) * flutter;
            arm.fingerGoal[f] = f == pointing ? 0.05f : Mathf.Clamp01(grip + wobble + (f - 1.5f) * 0.03f);
        }
        arm.spreadGoal = spread;

        SolveTwoBone(arm, shoulder, view.TransformPoint(arm.current), pole, upperArmLength, forearmLength, out Vector3 wrist);
        PoseHand(arm, wrist, palmNormal, view, side, deltaTime);
    }

    private void PoseHand(Limb arm, Vector3 wrist, Vector3 palmNormal, Transform view, float side, float deltaTime)
    {
        Vector3 forward = (wrist - arm.elbow.position).normalized;
        Vector3 up = Vector3.ProjectOnPlane(palmNormal, forward);
        if (up.sqrMagnitude < 1e-4f) up = view.up;
        Quaternion handRotation = Quaternion.LookRotation(forward, up.normalized);
        arm.end.SetPositionAndRotation(wrist, handRotation);

        // Fingers: two segments each. Each finger closes at its own speed, so a grab ripples from
        // the index finger to the little finger; open hands splay the fingers apart.
        arm.spread = Mathf.MoveTowards(arm.spread, arm.spreadGoal, deltaTime * 5f);
        for (int f = 0; f < 4; f++)
        {
            arm.fingerCurl[f] = Mathf.MoveTowards(arm.fingerCurl[f], arm.fingerGoal[f], deltaTime * FingerSpeed[f]);
            float curl = Mathf.Lerp(8f, 90f, arm.fingerCurl[f]);
            Transform first = arm.fingers[f * 2];
            Transform second = arm.fingers[f * 2 + 1];
            first.localRotation = Quaternion.Euler(curl + f * 4f, (f - 1.5f) * (6f + 9f * arm.spread) * side, 0f);
            second.localRotation = Quaternion.Euler(curl * 0.9f, 0f, 0f);
        }
    }

    // ---------------- Legs ----------------

    private void UpdateLeg(Limb leg, float side, float speed, bool grounded, bool climbing, float deltaTime)
    {
        Transform orientation = controller.Orientation != null ? controller.Orientation : controller.transform;
        Vector3 centre = controller.transform.position;
        Vector3 hip = centre + Vector3.up * hipHeight + orientation.right * (hipWidth * side);
        float legLength = thighLength + shinLength;
        Vector3 target;

        if (climbing && climber.GetFoot(side > 0f ? 1 : 0, out Vector3 foothold))
        {
            target = foothold;
        }
        else if (grounded)
        {
            float phase = walkPhase + (side > 0f ? 0f : Mathf.PI);
            float amount = Mathf.Clamp01(speed / 6f);
            // Longer strides swing the legs further and lift the knees higher.
            Vector3 swing = orientation.forward * (Mathf.Sin(phase) * Mathf.Min(currentStride * 0.4f, 0.55f) * amount);
            float lift = Mathf.Max(0f, Mathf.Cos(phase)) * Mathf.Lerp(0.18f, 0.32f, Mathf.InverseLerp(strideLength, strideLength * 2.2f, currentStride)) * amount;
            target = hip + Vector3.down * (legLength * 0.97f - lift) + swing + orientation.right * (0.03f * side);
            // Landing: knees bend to take the jolt.
            target += Vector3.up * (0.28f * landing) + orientation.forward * (0.08f * landing);
        }
        else
        {
            // Airborne: knees up and feet tucked on the way up; on the way down the legs pedal
            // and reach for the ground, wilder the faster the fall.
            float sidePhase = side > 0f ? 0f : Mathf.PI;
            target = hip + Vector3.down * (legLength * Mathf.Lerp(0.82f, 0.7f, rising)) + orientation.forward * (0.08f + 0.1f * rising);
            target += orientation.forward * (Mathf.Sin(airPhase + sidePhase) * 0.2f * falling);
            target += Vector3.up * (Mathf.Max(0f, Mathf.Cos(airPhase + sidePhase)) * 0.15f * falling);
            target += orientation.right * (0.06f * side * falling);
        }

        // Smooth relative to the hip, so the feet never trail behind when the body moves fast.
        leg.current = leg.started ? Vector3.Lerp(leg.current, target - hip, 1f - Mathf.Exp(-14f * deltaTime)) : target - hip;
        leg.started = true;
        Vector3 pole = orientation.forward + orientation.right * (0.15f * side);
        SolveTwoBone(leg, hip, hip + leg.current, pole, thighLength, shinLength, out Vector3 ankle);
        leg.end.SetPositionAndRotation(ankle + orientation.forward * 0.06f, Quaternion.LookRotation(orientation.forward, Vector3.up));
    }

    // ---------------- IK ----------------

    /// <summary>Analytic two-bone IK; places both segments and the middle joint.</summary>
    private static void SolveTwoBone(Limb limb, Vector3 start, Vector3 target, Vector3 pole, float a, float b, out Vector3 end)
    {
        Vector3 toTarget = target - start;
        float distance = Mathf.Clamp(toTarget.magnitude, 0.01f, a + b - 0.001f);
        Vector3 direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;
        float along = (a * a + distance * distance - b * b) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
        bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.Cross(direction, Vector3.right).normalized;

        Vector3 middle = start + direction * along + bend * height;
        end = start + direction * distance;

        PlaceSegment(limb.upper, start, middle);
        PlaceSegment(limb.lower, middle, end);
        limb.elbow.position = middle;
    }

    private static void PlaceSegment(Transform segment, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 1e-4f) return;
        // Capsule primitives are 2 units tall along Y.
        segment.SetPositionAndRotation(from + delta * 0.5f, Quaternion.FromToRotation(Vector3.up, delta / length));
        Vector3 scale = segment.localScale;
        segment.localScale = new Vector3(scale.x, length * 0.5f, scale.z);
    }

    // ---------------- Construction ----------------

    private Limb BuildLimb(string limbName, float thickness, bool hand, float side)
    {
        var limbRoot = new GameObject(limbName).transform;
        limbRoot.SetParent(root, false);
        var limb = new Limb
        {
            upper = Part(PrimitiveType.Capsule, "Upper", limbRoot, limbMaterial, new Vector3(thickness, 0.2f, thickness), hand),
            lower = Part(PrimitiveType.Capsule, "Lower", limbRoot, limbMaterial, new Vector3(thickness * 0.85f, 0.2f, thickness * 0.85f), hand),
            elbow = Part(PrimitiveType.Sphere, "Joint", limbRoot, jointMaterial, Vector3.one * thickness * 1.15f, hand),
        };

        if (hand)
        {
            // Unscaled hand pivot at the wrist; all sizes below are in metres.
            limb.end = new GameObject("Hand").transform;
            limb.end.SetParent(limbRoot, false);
            Transform palm = Part(PrimitiveType.Cube, "Palm", limb.end, jointMaterial, new Vector3(0.085f, 0.028f, 0.095f), true);
            palm.localPosition = new Vector3(0f, 0f, 0.0475f);

            limb.fingers = new Transform[8];
            for (int f = 0; f < 4; f++)
            {
                // Uneven finger lengths keep the hand from looking stamped out.
                float first = f == 0 ? 0.03f : f == 3 ? 0.026f : 0.034f;
                float second = first * 0.75f;
                var knuckle = new GameObject("Finger " + f).transform;
                knuckle.SetParent(limb.end, false);
                knuckle.localPosition = new Vector3((f - 1.5f) * 0.021f * side, 0f, 0.095f);
                Transform bone = Part(PrimitiveType.Cube, "Phalanx", knuckle, limbMaterial, new Vector3(0.017f, 0.017f, first), true);
                bone.localPosition = new Vector3(0f, 0f, first * 0.5f);

                var middle = new GameObject("Knuckle").transform;
                middle.SetParent(knuckle, false);
                middle.localPosition = new Vector3(0f, 0f, first);
                Transform tip = Part(PrimitiveType.Cube, "Phalanx", middle, limbMaterial, new Vector3(0.015f, 0.015f, second), true);
                tip.localPosition = new Vector3(0f, 0f, second * 0.5f);

                limb.fingers[f * 2] = knuckle;
                limb.fingers[f * 2 + 1] = middle;
            }

            var thumbBase = new GameObject("Thumb").transform;
            thumbBase.SetParent(limb.end, false);
            thumbBase.localPosition = new Vector3(-0.045f * side, 0f, 0.03f);
            thumbBase.localRotation = Quaternion.Euler(20f, -40f * side, 0f);
            Transform thumb = Part(PrimitiveType.Cube, "Phalanx", thumbBase, limbMaterial, new Vector3(0.019f, 0.019f, 0.045f), true);
            thumb.localPosition = new Vector3(0f, 0f, 0.0225f);
        }
        else
        {
            limb.end = Part(PrimitiveType.Cube, "Foot", limbRoot, jointMaterial, new Vector3(thickness * 1.1f, thickness * 0.6f, thickness * 2.2f), false);
        }
        return limb;
    }

    private static Transform Part(PrimitiveType type, string partName, Transform parent, Material material, Vector3 scale, bool viewOnly)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localScale = scale;
        var renderer = part.GetComponent<MeshRenderer>();
        if (material != null) renderer.sharedMaterial = material;
        // Arms float in front of the camera, so their shadows would look detached.
        renderer.shadowCastingMode = viewOnly ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        return part.transform;
    }
}
