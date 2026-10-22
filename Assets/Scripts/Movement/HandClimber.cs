using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hand-over-hand climbing. Each hand grabs a real point on the surface and the body is pulled up to hang
/// below the hands: one hand reaches while the other holds, the body follows each grab and the feet step
/// up the wall behind it. When the way is blocked by a bump or overhang, the hands reach around it.
/// At the top both hands plant on the edge, pull the chest up to it, push the body over and stand up.
/// <see cref="FirstPersonBody"/> reads the hands, feet and body frame to pose the limbs, and the camera
/// gets a little weight from each pull (see LateUpdate).
///
/// Controls while climbing: W/S up and down, A/D sideways, Shift faster, Space to push off the wall.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1050)]
public sealed class HandClimber : MonoBehaviour
{
    [Header("References (found automatically when empty)")]
    [SerializeField] private SmoothFirstPersonController controller;
    [SerializeField] private Camera viewCamera;

    [Header("Detection")]
    [Tooltip("Layers that can be climbed. Generated terrain lives on Ground.")]
    [SerializeField] private LayerMask climbLayers = (1 << 7) | (1 << 8);
    [Tooltip("Surfaces flatter than this (degrees from horizontal) cannot be climbed.")]
    [Range(0f, 90f)] [SerializeField] private float minWallSteepness = 55f;
    [Tooltip("Steepest ground (degrees) a climb may end on. Keep it below the controller's Max Slope Angle, " +
             "or the player slides back off the edge. Hands also keep gripping anything steeper than this.")]
    [Range(10f, 60f)] [SerializeField] private float maxStandSlope = 36f;
    [Min(0.1f)] [SerializeField] private float detectDistance = 0.9f;
    [Tooltip("Largest angle between where the player faces and the wall.")]
    [Range(0f, 90f)] [SerializeField] private float maxFacingAngle = 55f;

    [Header("Body")]
    [Tooltip("Distance from the wall to the player's centre while hanging.")]
    [Min(0.1f)] [SerializeField] private float wallOffset = 0.5f;
    [Tooltip("How far above the player's centre the hands grip.")]
    [SerializeField] private float handHeight = 0.6f;
    [Min(0f)] [SerializeField] private float handSpread = 0.3f;
    [Tooltip("Where the gripping hand ends up after each pull, above the body centre. Low = the body is pushed up " +
             "past the hand (the arm presses down at the chest) before the other hand lets go and reaches.")]
    [SerializeField] private float pressHeight = 0.2f;
    [Tooltip("How fast the arms pull the body towards its hanging spot.")]
    [Min(0.1f)] [SerializeField] private float pullSpeed = 2.4f;
    [Tooltip("How softly the body follows the hands (seconds). Higher = more sway and lag.")]
    [Range(0.02f, 0.5f)] [SerializeField] private float bodySmoothTime = 0.16f;

    [Header("Reaching")]
    [Tooltip("Short reaches used when a full one finds no grip (and when stepping around obstacles).")]
    [Min(0.1f)] [SerializeField] private float reachStep = 0.5f;
    [Tooltip("How far a free hand reaches past the holding one: grab high, pull it down to the chest, then the other hand goes.")]
    [Min(0.2f)] [SerializeField] private float reachLength = 1.04f;
    [Tooltip("How far above the gripping hand the free hand reaches when climbing up: from the chest to well above the head.")]
    [Min(0.2f)] [SerializeField] private float reachAbove = 1.365f;
    [Tooltip("Seconds for a short reach; long reaches take proportionally longer.")]
    [Min(0.05f)] [SerializeField] private float reachTime = 0.22f;
    [Tooltip("Hands let go if the body stays this far from both of them for half a second.")]
    [Min(0.5f)] [SerializeField] private float maxHandDistance = 1.4f;
    [Tooltip("Speed of reaching and pulling, relative to the values above (lower = slower, steadier climb).")]
    [Range(0.2f, 2f)] [SerializeField] private float climbSpeed = 1.2f;
    [Tooltip("Speed multiplier while holding the fast climb key.")]
    [Range(1f, 3f)] [SerializeField] private float fastClimbBoost = 1.4f;
    [Tooltip("Seconds of being blocked before the hands try to reach around the obstacle.")]
    [Min(0.05f)] [SerializeField] private float detourDelay = 0.2f;
    [Tooltip("When getting onto a wall, how much later the second hand takes hold (fraction of a reach).")]
    [Range(0f, 1f)] [SerializeField] private float enterStagger = 0.35f;
    [Tooltip("Radius of the probe that looks for holds; bigger ignores small bumps and cracks.")]
    [Range(0.01f, 0.3f)] [SerializeField] private float probeRadius = 0.08f;

    [Header("Smooth Surfaces")]
    [Tooltip("Bare hands on a SlipperySurface start sliding at this speed (m/s), speeding up to the max.")]
    [Min(0f)] [SerializeField] private float slideStartSpeed = 1.5f;
    [Min(0f)] [SerializeField] private float slideMaxSpeed = 5f;
    [Min(0f)] [SerializeField] private float slideAcceleration = 6f;

    [Header("Lunge")]
    [Tooltip("Jump key while climbing up: throw yourself up the wall and grab again.")]
    [Min(0f)] [SerializeField] private float lungeSpeed = 6.5f;
    [Tooltip("Seconds after a lunge in which the hands grab any wall in front by themselves.")]
    [Min(0f)] [SerializeField] private float lungeGrabWindow = 0.7f;

    [Header("Over The Top")]
    [Tooltip("Seconds to plant both hands on the edge, pull up, push over and stand.")]
    [Min(0.2f)] [SerializeField] private float pullUpTime = 1.2f;

    [Header("Exits")]
    [SerializeField] private Vector2 jumpOffVelocity = new Vector2(5f, 5f);
    [Min(0f)] [SerializeField] private float regrabDelay = 0.35f;

    [Header("Feel")]
    [Tooltip("How much the view trails the body when it is pulled (seconds of motion); adds weight.")]
    [Range(0f, 0.2f)] [SerializeField] private float cameraLag = 0.06f;
    [Tooltip("Head tilt towards the holding hand while the other one reaches, degrees.")]
    [Range(0f, 8f)] [SerializeField] private float reachTilt = 2.5f;
    [Tooltip("How much the view tilts up or down towards the hands while climbing (0 = never, 1 = keeps them centred).")]
    [Range(0f, 1f)] [SerializeField] private float lookAtHands = 0.4f;
    [Tooltip("The same while pulling up over an edge, when the hands matter most.")]
    [Range(0f, 1f)] [SerializeField] private float lookAtHandsPullUp = 0.65f;
    [Min(0.05f)] [SerializeField] private float footStepTime = 0.22f;

    [Header("Keys")]
    [SerializeField] private Key upKey = Key.W;
    [SerializeField] private Key downKey = Key.S;
    [SerializeField] private Key leftKey = Key.A;
    [SerializeField] private Key rightKey = Key.D;
    [SerializeField] private Key jumpOffKey = Key.Space;
    [SerializeField] private Key fastClimbKey = Key.LeftShift;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool climbing;
    [SerializeField] private bool mantling;
    [SerializeField] private bool canStartClimb;
    [Tooltip("What last ended or changed the climb, for tuning.")]
    [SerializeField] private string lastEvent;
    [Tooltip("Draw the search for a place to stand (Scene view, or Game view with Gizmos on).")]
    [SerializeField] private bool drawDebug;

    private struct Hand
    {
        public Vector3 hold, normal;
        public bool moving;
        public Vector3 from, to, toNormal;
        public float t;
        public TentacleGripSurface surface;
        public float surfaceParameter;
    }

    private struct Foot
    {
        public Vector3 hold, from, to;
        public bool moving;
        public float t;
    }

    private readonly Hand[] hands = new Hand[2];
    private readonly Foot[] footholds = new Foot[2];
    private readonly Vector3[] feet = new Vector3[2];
    private Rigidbody body;
    private Transform orientation;
    private Vector3 wallNormal = Vector3.back;
    private Vector3 bodyTarget;
    private Vector3 bodyVelocity;
    private float regrabTimer;
    private float stuckTimer;
    private float farTimer;
    private float slideSpeed;
    private float lungeTimer;
    // The hand that grabbed last; the body hangs mostly from it.
    private int leadHand;
    // Time since the hands last got higher while "up" is held, and the best height so far.
    private float noProgressTimer;
    private float bestAnchorHeight;
    private float halfHeight = 1f;
    private PlayerCreatureCarrier carrier;
    // 0 = normal climb, 1 = fast climb; eased so holding Shift speeds up smoothly.
    private float fastBlend;

    // Pull-up over an edge: hands plant at the lip, then the body goes from -> pull -> push -> stand.
    private Vector3 mantleFrom, mantlePull, mantlePush, mantleTo, mantleVelocity;
    private float mantleT;
    private float mantleDuration = 1f;
    private Vector3 standNormal = Vector3.up;

    // Camera weight.
    private Vector3 cameraBasePosition;
    private Quaternion cameraBaseRotation = Quaternion.identity;
    private Vector3 lagOffset, lagVelocity;
    private float feel, roll, look;

    public bool IsClimbing => climbing;
    public bool Controls(SmoothFirstPersonController player) => controller == player;
    public void StopForBubbleCapture() { if (climbing) Exit(Vector3.zero); }
    /// <summary>A new mount-to-wall transfer can catch immediately without an old regrab cooldown.</summary>
    public void PrepareForTraversalTransfer() { regrabTimer = 0f; lungeTimer = Mathf.Max(lungeTimer, 0.35f); }
    public bool IsMantling => mantling;
    // A pull-up over a lip within arm's reach, allowed without goo (only full climbing needs goo).
    private bool lipOnly;
    [Tooltip("Without goo the hands can still pull the body over a lip this far above the body's centre (m).")]
    [Min(0f)] [SerializeField] private float lipReach = 1.5f;
    /// <summary>0..1, how hard the arms are pulling the body right now (for the hands to tense up).</summary>
    public float PullStrain => climbing ? Mathf.Clamp01((mantling ? mantleVelocity : bodyVelocity).magnitude / 1.2f) : 0f;
    public bool CanStartClimb => GooAllowsClimbing && canStartClimb;
    private PlayerGooCoating gooCoating;
    private bool GooAllowsClimbing
    {
        get
        {
            // Also supports a supply component added by an explicit runtime test fixture.
            if (gooCoating == null && controller != null)
            {
                gooCoating = controller.GetComponent<PlayerGooCoating>();
                if (gooCoating != null) gooCoating.RegisterClimber(this);
            }
            return gooCoating != null && gooCoating.CanClimb;
        }
    }

    public void StopForGooDepletion()
    {
        canStartClimb = false;
        lungeTimer = 0f;
        if (!climbing) return;
        lastEvent = "Let go: goo depleted";
        Exit(Vector3.zero);
        if (body != null) body.useGravity = true;
    }
    public string LastEvent => lastEvent;

    /// <summary>For automated tests: when set, used instead of the keyboard (x = right, y = up).</summary>
    public Vector2? ScriptedInput { get; set; }
    /// <summary>For automated tests: fast climbing while ScriptedInput is set.</summary>
    public bool ScriptedFast { get; set; }

    /// <summary>World grip point for a hand (0 = left, 1 = right). False when the hand is free.</summary>
    public bool GetHand(int index, out Vector3 hold, out Vector3 normal, out bool gripping)
    {
        RefreshGrip(index);
        hold = default;
        normal = Vector3.up;
        gripping = false;
        // Near the end of a pull-up the hands let go of the edge and come back to the body.
        if (!climbing || (mantling && mantleT > 0.86f)) return false;
        Hand hand = hands[index];
        if (hand.moving)
        {
            hold = HandPosition(index);
            normal = Vector3.Slerp(hand.normal, hand.toNormal, Mathf.SmoothStep(0f, 1f, hand.t));
            return true;
        }
        hold = hand.hold;
        normal = hand.normal;
        gripping = true;
        return true;
    }

    public bool GetFoot(int index, out Vector3 foothold)
    {
        foothold = feet[index];
        return climbing;
    }

    /// <summary>
    /// Eye position and body axes while climbing, so the arms hang from the body (facing the wall)
    /// instead of from the camera, and turning the head does not swing them around.
    /// </summary>
    public bool GetClimbFrame(out Vector3 eye, out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        forward = FlatInward();
        up = Vector3.up;
        right = Vector3.Cross(Vector3.up, forward);
        Transform holder = viewCamera != null ? viewCamera.transform.parent : null;
        eye = holder != null ? holder.position : body != null ? body.position + Vector3.up * 0.6f : transform.position;
        return climbing;
    }

    private void Awake()
    {
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller == null) return;
        gooCoating = controller.GetComponent<PlayerGooCoating>();
        if (gooCoating != null) gooCoating.RegisterClimber(this);
        body = controller.GetComponent<Rigidbody>();
        orientation = controller.Orientation != null ? controller.Orientation : controller.transform;
        var capsule = controller.GetComponent<CapsuleCollider>();
        if (capsule != null) halfHeight = capsule.height * 0.5f * controller.transform.lossyScale.y;
        if (viewCamera == null) viewCamera = Camera.main;
        carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (viewCamera != null)
        {
            cameraBasePosition = viewCamera.transform.localPosition;
            cameraBaseRotation = viewCamera.transform.localRotation;
        }
    }

    private void OnEnable() => ProceduralWorld.AnyTerrainChanged += TerrainChanged;
    private void TerrainChanged(ProceduralWorld world, Bounds bounds)
    {
        if (!climbing || world.gameObject.scene != gameObject.scene) return;
        for (int i = 0; i < hands.Length; i++)
        {
            Vector3 target = HandTarget(i);
            if (!bounds.Contains(target)) continue;
            Vector3 normal = hands[i].moving ? hands[i].toNormal : hands[i].normal;
            if (Physics.Raycast(target + normal * 0.12f, -normal, out RaycastHit hit, 0.24f, climbLayers, QueryTriggerInteraction.Ignore))
            {
                if (hands[i].moving) { hands[i].to = hit.point; hands[i].toNormal = hit.normal; }
                else { hands[i].hold = hit.point; hands[i].normal = hit.normal; }
                continue;
            }
            if (!mantling && ProbeWall(target, out hit) && (hit.point - target).sqrMagnitude <= 0.25f)
            { hands[i].moving = false; hands[i].hold = hit.point; hands[i].normal = hit.normal; continue; }
            lastEvent = "Let go: terrain beneath a grip was eroded";
            Exit(Vector3.zero); return;
        }
    }

    private void Update()
    {
        if (!GooAllowsClimbing && !lipOnly)
        {
            StopForGooDepletion();
            TryLipPull();
            return;
        }
        if (BoneRide.Mount != null) { canStartClimb = false; return; }
        if (controller == null || body == null || controller.BubbleMovementBlocked) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null && ScriptedInput == null) return;
        bool Held(Key key) => keyboard != null && keyboard[key].isPressed;
        float vertical = ScriptedInput?.y ?? (Held(upKey) ? 1f : 0f) - (Held(downKey) ? 1f : 0f);
        float horizontal = ScriptedInput?.x ?? (Held(rightKey) ? 1f : 0f) - (Held(leftKey) ? 1f : 0f);
        bool fast = ScriptedInput != null ? ScriptedFast : Held(fastClimbKey);
        bool jumpOff = ScriptedInput == null && keyboard != null && keyboard[jumpOffKey].wasPressedThisFrame;
        float deltaTime = Time.deltaTime;
        regrabTimer = Mathf.Max(0f, regrabTimer - deltaTime);
        lungeTimer = Mathf.Max(0f, lungeTimer - deltaTime);
        fastBlend = Mathf.MoveTowards(fastBlend, fast ? 1f : 0f, deltaTime * 3f);

        // Like PEAK: holding up climbs towards where you look, so the view steers round glaze and bulges.
        if (climbing && ScriptedInput == null && vertical > 0f && viewCamera != null)
        {
            float lookSide = Vector3.Dot(viewCamera.transform.forward, WallRight());
            horizontal = Mathf.Clamp(horizontal + Mathf.Clamp(lookSide * 2f, -1f, 1f), -1f, 1f);
        }

        if (mantling)
        {
            UpdateMantle(deltaTime);
            return;
        }

        if (!climbing)
        {
            RaycastHit wall = default;
            canStartClimb = regrabTimer <= 0f && FindWall(out wall);
            // Hands catch the wall by themselves at the end of a lunge.
            if (canStartClimb && lungeTimer > 0f && !controller.Grounded)
                TryStartClimb();
            else if (canStartClimb && vertical > 0f && !controller.Grounded)
                TryStartClimb();
            else if (canStartClimb && vertical > 0f && controller.Grounded && WallIsTall(wall))
                TryStartClimb();
            return;
        }

        canStartClimb = false;
        controller.SetClimbing(true);
        if (jumpOff && vertical > 0f)
        {
            Lunge(horizontal);
            return;
        }
        if (jumpOff)
        {
            lastEvent = "Jumped off (jump key)";
            Exit(FlatOutward() * jumpOffVelocity.x + Vector3.up * jumpOffVelocity.y);
            return;
        }

        // Climbing down onto the floor ends the climb.
        if (vertical < 0f && Physics.Raycast(body.position, Vector3.down, halfHeight + 0.15f, climbLayers, QueryTriggerInteraction.Ignore))
        {
            lastEvent = "Climbed down onto the floor";
            Exit(Vector3.zero);
            return;
        }

        RefreshGrip(0);
        RefreshGrip(1);
        AdvanceHands(deltaTime);
        SlideOnSlippery(deltaTime);
        if (vertical > 0f)
        {
            float height = (HandTarget(0).y + HandTarget(1).y) * 0.5f;
            if (height > bestAnchorHeight + 0.25f)
            {
                bestAnchorHeight = height;
                noProgressTimer = 0f;
            }
            else noProgressTimer += deltaTime;
        }
        else noProgressTimer = 0f;
        if (vertical != 0f || horizontal != 0f) TryReach(vertical, horizontal);
        else stuckTimer = 0f;
        if (mantling) return;
        UpdateBodyTarget(deltaTime);
        UpdateFeet(deltaTime);

        // Let go only when the body cannot stay near the hands for a while (a bad grab), never on one long reach.
        float limit = maxHandDistance + 0.3f;
        bool far = (hands[0].hold - body.position).magnitude > limit && (hands[1].hold - body.position).magnitude > limit;
        farTimer = far ? farTimer + deltaTime : 0f;
        if (farTimer > 1f && !StickyHands)
        {
            lastEvent = $"Let go: hands {(hands[0].hold - body.position).magnitude:0.0} and {(hands[1].hold - body.position).magnitude:0.0} m away, " +
                        $"body {(body.position - bodyTarget).magnitude:0.0} m off its spot";
            Exit(Vector3.zero);
        }
    }

    private void FixedUpdate()
    {

        if (!GooAllowsClimbing && !lipOnly) { StopForGooDepletion(); return; }
        if (BoneRide.Mount != null || !climbing || mantling || body == null) return;
        // Eased pull: the body speeds up and settles instead of sliding at a constant speed.
        float scale = SpeedScale();
        Vector3 next = Vector3.SmoothDamp(body.position, bodyTarget, ref bodyVelocity,
            bodySmoothTime / scale, pullSpeed * scale * 1.6f, Time.fixedDeltaTime);
        body.MovePosition(OutOfWalls(body.position, next));
    }

    /// <summary>
    /// The climbing body is moved by hand (kinematic), so nothing stops it entering the wall. Each move is swept
    /// with a slim capsule round the chest and head first: it stops at anything solid in the way (a ledge or drum
    /// step overhead, a rim) and slides along it instead. The legs are left out, so a flared foot of a wall does
    /// not hold the climber off.
    /// </summary>
    /// <summary>For tests: off moves the body exactly as before (no sweep).</summary>
    public static bool SweepBody = true;

    private Vector3 OutOfWalls(Vector3 from, Vector3 to)
    {
        if (!SweepBody) return to;
        const float radius = 0.3f, skin = 0.03f;
        Vector3 low = from + Vector3.up * 0.05f, high = from + Vector3.up * 0.65f;
        Vector3 delta = to - from;
        Vector3 moved = Vector3.zero;
        for (int pass = 0; pass < 2; pass++)
        {
            float length = delta.magnitude;
            if (length < 1e-4f) break;
            Vector3 dir = delta / length;
            if (!Physics.CapsuleCast(low + moved, high + moved, radius, dir, out RaycastHit hit, length + skin, climbLayers, QueryTriggerInteraction.Ignore)
                || hit.collider.attachedRigidbody == body)
            {
                moved += delta;
                break;
            }
            // Up to the wall, then what is left of the move slides along it.
            float allowed = Mathf.Max(0f, hit.distance - skin);
            moved += dir * allowed;
            delta = Vector3.ProjectOnPlane(dir * (length - allowed), hit.normal);
        }
        return from + moved;
    }

    // ---------------- Starting ----------------

    private bool FindWall(out RaycastHit hit, bool withoutGoo = false)
    {
        hit = default;
        if (!GooAllowsClimbing && !withoutGoo) return false;
        Vector3 origin = controller.transform.position + Vector3.up * 0.3f;
        if (!Physics.SphereCast(origin, 0.25f, orientation.forward, out hit, detectDistance, climbLayers, QueryTriggerInteraction.Ignore))
            return false;
        // With a tentacle on the hand even gentle slopes and overhangs can be grabbed.
        float steepness = StickyHands ? 45f : minWallSteepness;
        if (Vector3.Angle(hit.normal, Vector3.up) < steepness || hit.normal.y < (StickyHands ? -1.01f : -0.85f)) return false;
        Vector3 flatNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        return flatNormal.sqrMagnitude > 1e-4f && Vector3.Angle(orientation.forward, -flatNormal) <= maxFacingAngle;
    }

    /// <summary>From the ground, only grab walls that go higher than a step.</summary>
    private bool WallIsTall(RaycastHit wall)
    {
        // Look straight at the wall a little above the hands; walls that lean back are further away up there.
        Vector3 inward = -Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
        Vector3 origin = controller.transform.position + Vector3.up * (handHeight + 0.4f);
        return Physics.Raycast(origin, inward, out RaycastHit high, detectDistance + 1.2f, climbLayers, QueryTriggerInteraction.Ignore)
            && Vector3.Angle(high.normal, Vector3.up) >= MinGripAngle;
    }

    private void TryStartClimb()
    {
        if (!GooAllowsClimbing) return;
        if (!FindWall(out RaycastHit wall)) return;
        wallNormal = wall.normal;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 guess = controller.transform.position + WallUp() * handHeight + WallRight() * (handSpread * side);
            // On bumpy terrain one hand can miss; let it grab where the wall was found instead.
            Vector3 point = wall.point + WallRight() * (handSpread * side * 0.5f), normal = wall.normal;
            Collider surface = wall.collider;
            if (ProbeWall(guess, out RaycastHit grip))
            {
                point = grip.point;
                normal = grip.normal;
                surface = grip.collider;
            }
            // Getting onto the wall: both hands come up from the chest and take hold one after the other,
            // while the body lines up with the wall.
            Vector3 chest = controller.transform.position + WallUp() * 0.1f + WallRight() * (0.25f * side) + wall.normal * 0.15f;
            hands[i] = new Hand
            {
                hold = point, normal = normal, moving = true, from = chest, to = point, toNormal = normal,
                t = i == 0 ? 0f : -enterStagger,
            };
            AttachGrip(i, surface, point);
        }
        leadHand = 1;

        climbing = true;
        stuckTimer = 0f;
        farTimer = 0f;
        noProgressTimer = 0f;
        bestAnchorHeight = float.MinValue;
        bodyVelocity = Vector3.zero;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        controller.SetRestricted(true);
        controller.SetClimbing(true);
        for (int i = 0; i < 2; i++)
        {
            Vector3 foot = body.position + Vector3.down * halfHeight * 0.85f;
            footholds[i] = new Foot { hold = foot };
            feet[i] = foot;
        }
        UpdateBodyTarget(1f);
        lastEvent = StickyHands ? "Grabbed the wall (tentacle helping)" : "Grabbed the wall";
    }

    // ---------------- Reaching ----------------

    private void AttachGrip(int index, Collider collider, Vector3 point)
    {
        hands[index].surface = collider != null ? collider.GetComponent<TentacleGripSurface>() : null;
        if (hands[index].surface != null) hands[index].surfaceParameter = hands[index].surface.Capture(point);
    }

    private void RefreshGrip(int index)
    {
        Hand hand = hands[index];
        if (hand.surface == null) return;
        Vector3 normal = hand.moving ? hand.toNormal : hand.normal;
        if (!hand.surface.Resolve(hand.surfaceParameter, normal, out Vector3 point))
        { hands[index].surface = null; return; }
        if (hand.moving) hands[index].to = point;
        else hands[index].hold = point;
    }

    private void AdvanceHands(float deltaTime)
    {
        for (int i = 0; i < 2; i++)
        {
            if (!hands[i].moving) continue;
            float travel = Mathf.Max(reachStep, (hands[i].to - hands[i].from).magnitude);
            hands[i].t += deltaTime * SpeedScale() / (reachTime * travel / reachStep);
            if (hands[i].t < 1f) continue;
            hands[i].moving = false;
            hands[i].hold = hands[i].to;
            hands[i].normal = hands[i].toNormal;
            leadHand = i;
            // A small jolt as the hand takes the weight.
            lagVelocity -= WallUp() * 0.35f;
        }
    }

    /// <summary>Where a hand is now, following an arc off the wall while it reaches.</summary>
    private Vector3 HandPosition(int index)
    {
        Hand hand = hands[index];
        if (!hand.moving) return hand.hold;
        float t = Mathf.Clamp01(hand.t);
        // Quick start, careful placement, lifted off the wall and brought down onto the hold.
        float along = 1f - (1f - t) * (1f - t) * (1f - t);
        float arc = Mathf.Sin(t * Mathf.PI);
        return Vector3.LerpUnclamped(hand.from, hand.to, along) + hand.toNormal * (arc * 0.13f) + Vector3.up * (arc * arc * 0.05f);
    }

    private void TryReach(float vertical, float horizontal)
    {
        // Let the body mostly catch up first, so the hands never run far ahead of it.
        // One hand grabs, the arms pull the body up, and only then does the other hand let go and reach.
        if ((body.position - bodyTarget).magnitude > Mathf.Lerp(0.08f, 0.15f, fastBlend)) return;
        // Like a climber: one hand holds while the body is pushed up past it; only then does the other let go.
        if (hands[0].moving || hands[1].moving) return;
        int moving = -1;

        Vector3 up = WallUp();
        Vector3 right = WallRight();
        Vector3 direction = (up * vertical + right * horizontal).normalized;
        Vector3 holdA = HandTarget(0), holdB = HandTarget(1);
        Vector3 anchor = (holdA + holdB) * 0.5f;

        // Going up for a while without getting higher, at a top too steep to stand on: scramble onto it.
        if (vertical > 0f && noProgressTimer > 2.5f && !WallAbove(anchor) && FindLedgeTop(anchor, true, out Vector3 steepTop, relaxed: true))
        {
            noProgressTimer = 0f;
            StartMantle(anchor, steepTop, "Scrambled onto a steep top");
            return;
        }

        // The wall is flattening out: step up as soon as there is ground flat enough to stand on.
        if (vertical > 0f && wallNormal.y > 0.5f && FindLedgeTop(anchor, false, out Vector3 slopeTop))
        {
            StartMantle(anchor, slopeTop, "Stepped up where the wall flattens out");
            return;
        }

        // An opening right above the hands with a floor in it (a window, a ledge, a gap): climb in
        // rather than reaching round it.
        if (vertical > 0f && !WallAbove(anchor) && FindLedgeTop(anchor, false, out Vector3 opening) && opening.y < anchor.y + 1.6f)
        {
            StartMantle(anchor, opening, "Climbed into an opening");
            return;
        }

        // Move the trailing hand: the one furthest behind in the direction of travel.
        int index = moving >= 0 ? 1 - moving
            : Vector3.Dot(holdA - anchor, direction) <= Vector3.Dot(holdB - anchor, direction) ? 0 : 1;
        // Fast climbing takes longer reaches, not just quicker ones.
        // Up the wall the free hand goes from below the gripping hand to well above the head; sideways and
        // down the shorter reach is used.
        float length = (vertical > 0f ? reachAbove : reachLength) * Mathf.Lerp(1f, 1.15f, fastBlend) * (StickyHands ? 1.2f : 1f);
        if (TryReachWith(index, anchor, direction, length, ReachScales)) return;

        // Blocked going up: over the top if there is ground up there.
        if (vertical > 0f && FindLedgeTop(anchor, false, out Vector3 top))
        {
            StartMantle(anchor, top, "Climbed over the top");
            return;
        }

        stuckTimer += Time.deltaTime;
        if (vertical > 0f && stuckTimer > 0.15f && FindLedgeTop(anchor, true, out top))
        {
            StartMantle(anchor, top, "Climbed over a thick lip (wide search)");
            return;
        }

        // Still blocked by a bump or overhang: reach around it, with either hand.
        if (stuckTimer < detourDelay) return;
        Vector3[] detours = vertical != 0f
            ? new[] { (up * vertical + right).normalized, (up * vertical - right).normalized, right, -right }
            : new[] { (direction + up).normalized, (direction - up).normalized };
        foreach (Vector3 detour in detours)
        {
            if (TryReachWith(index, anchor, detour, length, DetourScales) ||
                (moving < 0 && TryReachWith(1 - index, anchor, detour, length, DetourScales)))
            {
                lastEvent = "Reached around an obstacle";
                return;
            }
        }
    }

    /// <summary>
    /// Looks for the best hold for one hand: candidates in a fan around the wanted direction and at
    /// several reach lengths, each scored by how close it is to the ideal spot, how far it gets the
    /// climb, how good the surface is and how comfortably the arms can span it.
    /// </summary>
    private bool TryReachWith(int index, Vector3 anchor, Vector3 direction, float length, float[] scales)
    {
        float side = index == 0 ? -1f : 1f;
        Vector3 right = WallRight();
        Vector3 other = HandTarget(1 - index);
        // Reach past the holding hand, on this hand's own side.
        float ahead = Mathf.Max(0f, Vector3.Dot(other - anchor, direction));
        Vector3 ideal = anchor + right * (handSpread * side) + direction * (ahead + length);

        float bestScore = float.MaxValue;
        RaycastHit best = default;
        foreach (float scale in scales)
        foreach (float turn in FanAngles)
        {
            Vector3 heading = Quaternion.AngleAxis(turn, wallNormal) * direction;
            Vector3 guess = anchor + right * (handSpread * side) + heading * (ahead + length * scale);
            if (!ProbeHold(guess, heading, out RaycastHit grip)) continue;
            // The grip must actually be on the way (an angled probe can land below a lip),
            // and the hands stay within a body's span of each other.
            float progress = Vector3.Dot(grip.point - anchor, direction);
            if (progress < length * scale * 0.3f) continue;
            float span = (grip.point - other).magnitude;
            if (span > 1.4f) continue;

            float score = (grip.point - ideal).magnitude          // close to where the hand wants to go
                        - progress * 1.0f                           // gets the climb further
                        + (1f - Vector3.Dot(grip.normal, wallNormal)) * 0.6f  // same face, not a sudden twist
                        + Mathf.Max(0f, span - 1.0f) * 1.5f          // comfortable between the hands
                        + Mathf.Max(0f, -grip.normal.y - 0.3f) * 0.8f; // overhangs are harder
            if (score >= bestScore) continue;
            bestScore = score;
            best = grip;
        }
        if (bestScore == float.MaxValue) return false;

        hands[index].moving = true;
        hands[index].t = 0f;
        hands[index].from = hands[index].hold;
        hands[index].to = best.point;
        hands[index].toNormal = best.normal;
        AttachGrip(index, best.collider, best.point);
        stuckTimer = 0f;
        return true;
    }

    // Degrees around the wanted direction (in the wall plane) that candidate holds are tried at.
    private static readonly float[] FanAngles = { 0f, -22f, 22f, -45f, 45f };

    /// <summary>
    /// Finds a hold near a guessed point: straight into the wall, angled in over a bulge, and round
    /// corners (back across an outside corner, or ahead into an inside one).
    /// </summary>
    private bool ProbeHold(Vector3 guess, Vector3 heading, out RaycastHit hit)
    {
        if (ProbeWall(guess, out hit)) return true;
        // Corners only matter when moving sideways; going up, these probes would catch ledge undersides.
        if (Mathf.Abs(Vector3.Dot(heading, WallUp())) > 0.5f) return false;
        // Outside corner: the wall turned away; look back across the corner at the new face.
        Vector3 beyond = guess + heading * 0.3f + wallNormal * 0.25f;
        if (Cast(beyond, (-heading - wallNormal * 0.5f).normalized, 1.2f, out hit)) return true;
        // Inside corner: a new face stands across the way; look ahead along the wall.
        Vector3 along = guess - heading * 0.6f + wallNormal * 0.35f;
        return Cast(along, heading, 1.2f, out hit);
    }

    /// <summary>A sphere cast (steady on small bumps) with a thin ray as fallback, keeping only grippable surfaces.</summary>
    private bool Cast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        if (Physics.SphereCast(origin, probeRadius, direction, out RaycastHit swept, distance, climbLayers, QueryTriggerInteraction.Ignore) && swept.distance > 0f)
        {
            // A sphere touching an edge reports a blended normal; aim a thin ray at the contact for the real face.
            Vector3 toContact = swept.point - origin;
            if (Physics.Raycast(origin, toContact.normalized, out hit, toContact.magnitude + 0.2f, climbLayers, QueryTriggerInteraction.Ignore) && Grippable(hit.normal))
                return true;
        }
        return Physics.Raycast(origin, direction, out hit, distance, climbLayers, QueryTriggerInteraction.Ignore) && Grippable(hit.normal);
    }

    private static readonly float[] ReachScales = { 1f, 0.7f, 0.45f, 0.25f };
    private static readonly float[] DetourScales = { 1f, 0.6f };

    /// <summary>Hands grip anything steeper than the steepest ground the player can stand on.</summary>
    private float MinGripAngle => Mathf.Min(minWallSteepness * 0.8f, maxStandSlope);

    /// <summary>Where a hand is, or where it is going if it is mid-reach.</summary>
    private Vector3 HandTarget(int index) => hands[index].moving ? hands[index].to : hands[index].hold;

    /// <summary>Horizontal direction into the wall, even when the wall leans back or overhangs.</summary>
    private Vector3 FlatInward()
    {
        Vector3 inward = -Vector3.ProjectOnPlane(wallNormal, Vector3.up);
        if (inward.sqrMagnitude > 1e-4f) return inward.normalized;
        Vector3 forward = orientation != null ? Vector3.ProjectOnPlane(orientation.forward, Vector3.up) : Vector3.forward;
        return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
    }

    private Vector3 FlatOutward() => -FlatInward();

    /// <summary>Finds a grippable surface near a guessed grip point.</summary>
    private bool ProbeWall(Vector3 guess, out RaycastHit hit)
    {
        Vector3 origin = guess + wallNormal * 0.6f;
        // Straight at the wall first, then angled down and in, for walls that curve back over a bulge.
        if (Cast(origin, -wallNormal, 2f, out hit)) return true;
        return Cast(origin, (-wallNormal - WallUp() * 0.7f).normalized, 2f, out hit);
    }

    /// <summary>True when there is more grippable wall just above the hands (a gap or bump, not a top).</summary>
    private bool WallAbove(Vector3 anchor)
    {
        Vector3 inward = FlatInward();
        Vector3 origin = anchor + Vector3.up * 1.2f - inward * 0.6f;
        return Physics.Raycast(origin, inward, out RaycastHit hit, 1.4f, climbLayers, QueryTriggerInteraction.Ignore) && Grippable(hit.normal);
    }

    /// <summary>Too steep to stand on, and not a ceiling (overhangs are fine).</summary>
    private bool Grippable(Vector3 normal) => Vector3.Angle(normal, Vector3.up) >= MinGripAngle && normal.y > (StickyHands ? -1.01f : -0.85f);

    /// <summary>True when a hold at this point is on a smooth surface (see SlipperySurface) that bare hands slide down.</summary>
    private bool OnSlippery(Vector3 point, Vector3 normal, out RaycastHit hit)
    {
        slipSurface = null;
        // Ledge tops and lips hold: hands resting on something flat-ish do not slide.
        if (normal.y > 0.45f) { hit = default; return false; }
        if (!Physics.Raycast(point + normal * 0.3f, -normal, out hit, 0.6f, climbLayers, QueryTriggerInteraction.Ignore)) return false;
        if (hit.normal.y > 0.45f) return false;
        slipSurface = hit.collider.GetComponent<SlipperySurface>();
        return slipSurface != null;
    }

    private SlipperySurface slipSurface;

    /// <summary>
    /// Bare hands on a smooth surface slide down it, faster the longer they stay, until they reach rough
    /// stone again. A carried tentacle creature sticks them in place.
    /// </summary>
    private void SlideOnSlippery(float deltaTime)
    {
        // Any hand on (or reaching for) the smooth surface drags the whole grip down with it.
        bool sliding = false;
        SlipperySurface surface = null;
        if (!StickyHands)
            for (int i = 0; i < 2 && !sliding; i++)
            {
                sliding = hands[i].moving ? OnSlippery(hands[i].to, hands[i].toNormal, out _) : OnSlippery(hands[i].hold, hands[i].normal, out _);
                if (sliding) surface = slipSurface;
            }
        // A hand still holding a ledge top stops the slide.
        if (sliding)
            for (int i = 0; i < 2; i++)
                if (!hands[i].moving && hands[i].normal.y > 0.45f) sliding = false;
        if (sliding)
        {
            float scale = surface != null ? surface.speedScale : 1f;
            bool speedsUp = surface == null || surface.accelerate;
            Vector3 drop = Vector3.down * (Mathf.Max(speedsUp ? slideSpeed : 0f, slideStartSpeed) * scale * deltaTime);
            for (int i = 0; i < 2; i++)
            {
                hands[i].from += drop;
                hands[i].to += drop;
                Vector3 lower = hands[i].hold + drop;
                // Stay on the surface as it curves.
                if (Physics.Raycast(lower + hands[i].normal * 0.3f, -hands[i].normal, out RaycastHit hit, 0.8f, climbLayers, QueryTriggerInteraction.Ignore)
                    && Grippable(hit.normal))
                {
                    hands[i].hold = hit.point;
                    hands[i].normal = hit.normal;
                }
                else hands[i].hold = lower;
            }
            slideSpeed = Mathf.MoveTowards(Mathf.Max(slideSpeed, slideStartSpeed), slideMaxSpeed, slideAcceleration * deltaTime);
            lastEvent = "Sliding down a smooth surface";
            // Sliding gives back the height gained, so the no-progress scramble does not kick in.
            bestAnchorHeight = Mathf.Min(bestAnchorHeight, (hands[0].hold.y + hands[1].hold.y) * 0.5f);
        }
        else slideSpeed = 0f;
        IsSliding = sliding;
    }

    /// <summary>True while bare hands slide down a smooth surface.</summary>
    public bool IsSliding { get; private set; }

    /// <summary>A carried tentacle creature wraps the hand and sticks to anything: ceilings too, from further away, and it never slips.</summary>
    public bool StickyHands => carrier != null && carrier.CarriesTentacle;

    /// <summary>
    /// Looks for ground above and behind the hands that the player can stand on: flat enough
    /// (maxStandSlope) and with room for the body. Picks the closest, flattest spot.
    /// wide = a bigger search (higher, further back and to the sides) for thick or overhanging lips.
    /// </summary>
    private bool FindLedgeTop(Vector3 anchor, bool wide, out Vector3 top, bool relaxed = false)
    {
        top = default;
        // Look straight in (not along a leaning wall's normal, which points down into the slope).
        Vector3 inward = FlatInward();
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        // Relaxed: any foothold up to 60 degrees, for tops with nothing flatter.
        float minNormalY = relaxed ? 0.5f : Mathf.Cos(maxStandSlope * Mathf.Deg2Rad);
        const float radius = 0.45f;
        float best = float.MaxValue;
        float[] backs = wide ? WideBacks : LedgeBacks;
        float[] heights = wide ? WideHeights : LedgeHeights;
        float[] laterals = wide ? WideLaterals : NoLaterals;

        foreach (float lateral in laterals)
        foreach (float back in backs)
        foreach (float height in heights)
        {
            Vector3 origin = anchor + Vector3.up * height + inward * back + side * lateral;
            bool ok = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, height + 1f, climbLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y >= minNormalY;
            if (ok)
            {
                // Room to stand: a body-sized capsule above the spot touches nothing.
                Vector3 low = hit.point + Vector3.up * (radius + 0.2f);
                Vector3 high = hit.point + Vector3.up * Mathf.Max(radius + 0.3f, halfHeight * 2f - radius);
                ok = !Physics.CheckCapsule(low, high, radius, climbLayers, QueryTriggerInteraction.Ignore);
            }
            if (drawDebug)
                Debug.DrawLine(origin, ok ? hit.point : origin + Vector3.down * (height + 1f), ok ? Color.green : Color.red, 0.1f);
            // The body must be able to get there: up past the edge, then in over it, not through a ledge above.
            if (ok) ok = PathToTopClear(anchor, hit.point);
            if (!ok) continue;

            float score = (hit.point - anchor).magnitude + (1f - hit.normal.y) * 4f;
            if (score >= best) continue;
            best = score;
            standNormal = hit.normal;
            top = hit.point;
        }
        return best < float.MaxValue;
    }

    /// <summary>
    /// True when the body can rise from the hands to just above a standing spot and move in over it
    /// without passing through stone (for example a ledge overhead when climbing up under it).
    /// </summary>
    private bool PathToTopClear(Vector3 anchor, Vector3 top)
    {
        // A body-thick probe, not a thin line, so the pull-up never scrapes through a ledge edge.
        const float body = 0.32f;
        Vector3 start = anchor + wallNormal * 0.55f;
        Vector3 rise = new Vector3(start.x, Mathf.Max(start.y, top.y + 0.7f), start.z);
        Vector3 over = top + Vector3.up * 0.7f;
        return !Sweep(start, rise, body) && !Sweep(rise, over, body);
    }

    private bool Sweep(Vector3 from, Vector3 to, float radius)
    {
        Vector3 path = to - from;
        float length = path.magnitude;
        if (length < 1e-3f) return false;
        // Starting against the wall itself: judge by a thin line instead.
        if (Physics.CheckSphere(from, radius, climbLayers, QueryTriggerInteraction.Ignore)) return Physics.Linecast(from, to, climbLayers, QueryTriggerInteraction.Ignore);
        return Physics.SphereCast(from, radius, path / length, out _, length, climbLayers, QueryTriggerInteraction.Ignore);
    }

    // Spots start a little back from the edge so the player is not set down half over it.
    private static readonly float[] LedgeBacks = { 0.6f, 1.0f, 1.4f, 1.8f };
    private static readonly float[] LedgeHeights = { 0.8f, 1.4f, 2.0f };
    private static readonly float[] WideBacks = { 0.6f, 1.0f, 1.5f, 2.0f, 2.6f };
    private static readonly float[] WideHeights = { 0.6f, 1.2f, 1.8f, 2.4f, 3.0f };
    private static readonly float[] WideLaterals = { 0f, -0.6f, 0.6f };
    private static readonly float[] NoLaterals = { 0f };

    /// <summary>The edge of the platform between the hands and the standing spot, where the hands plant.</summary>
    private Vector3 FindLip(Vector3 anchor, Vector3 top, Vector3 inward)
    {
        float y = Mathf.Max(top.y, anchor.y) + 1f;
        float reach = Vector3.ProjectOnPlane(top - anchor, Vector3.up).magnitude;
        for (float d = 0.05f; d <= reach + 0.05f; d += 0.12f)
        {
            Vector3 origin = new Vector3(anchor.x, y, anchor.z) + inward * d;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, y - top.y + 1.5f, climbLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.35f && hit.point.y > anchor.y - 0.3f)
                return hit.point;
        }
        return top;
    }

    // ---------------- Body ----------------

    private void UpdateBodyTarget(float deltaTime)
    {
        Vector3 normal = (hands[0].normal + hands[1].normal).normalized;
        if (normal.sqrMagnitude < 0.5f) normal = wallNormal;
        // Ease the wall direction so one odd grip does not swing the body around.
        wallNormal = Vector3.Slerp(wallNormal, normal, 1f - Mathf.Exp(-10f * deltaTime)).normalized;
        // Hang mostly from the hand that grabbed last: it is pulled down to the chest as the body rises.
        Vector3 grips = Vector3.Lerp(hands[1 - leadHand].hold, hands[leadHand].hold, 0.9f);
        // Shift weight onto the holding hand while the other one reaches.
        for (int i = 0; i < 2; i++)
            if (hands[i].moving)
                grips += WallRight() * ((i == 0 ? 0.08f : -0.08f) * Mathf.Sin(Mathf.Clamp01(hands[i].t) * Mathf.PI));
        bodyTarget = ClearOfWall(grips - WallUp() * pressHeight + wallNormal * wallOffset);
    }

    /// <summary>
    /// Moves a body spot out from the wall until the torso has room, so the body never passes through
    /// ledges or bulges between the hands and the feet (the climbing body is moved directly, not by physics).
    /// </summary>
    private Vector3 ClearOfWall(Vector3 target)
    {
        const float core = 0.38f; // most of the body: only grazing the wall itself is fine
        float reach = Mathf.Max(0f, halfHeight - core - 0.15f);
        Vector3 outward = wallNormal.sqrMagnitude > 0.5f ? wallNormal : FlatOutward();
        for (float d = 0f; d <= 2.8f; d += 0.2f)
        {
            Vector3 p = target + outward * d;
            if (!Physics.CheckCapsule(p + Vector3.up * reach, p - Vector3.up * reach, core, climbLayers, QueryTriggerInteraction.Ignore))
                return p;
        }
        return target;
    }

    /// <summary>Feet stand on real footholds and step up after the body, one at a time.</summary>
    private void UpdateFeet(float deltaTime)
    {
        Vector3 up = WallUp(), right = WallRight();
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 ideal = body.position - up * (halfHeight * 0.85f) + right * (0.17f * side) - wallNormal * (wallOffset - 0.12f);
            ref Foot foot = ref footholds[i];
            if (foot.moving)
            {
                foot.t += deltaTime * SpeedScale() / footStepTime;
                if (foot.t >= 1f)
                {
                    foot.moving = false;
                    foot.hold = foot.to;
                }
            }
            else if ((foot.hold - ideal).sqrMagnitude > 0.35f * 0.35f && !footholds[1 - i].moving)
            {
                Vector3 to = ideal;
                if (Physics.Raycast(ideal + wallNormal * 0.5f, -wallNormal, out RaycastHit hit, 1f, climbLayers, QueryTriggerInteraction.Ignore))
                    to = hit.point + hit.normal * 0.03f;
                foot = new Foot { hold = foot.hold, from = foot.hold, to = to, moving = true, t = 0f };
            }

            feet[i] = foot.moving
                ? Vector3.Lerp(foot.from, foot.to, Mathf.SmoothStep(0f, 1f, foot.t)) + wallNormal * (Mathf.Sin(foot.t * Mathf.PI) * 0.12f)
                : foot.hold;
        }
    }

    /// <summary>Climb speed now, easing up to the fast speed while the fast climb key is held.</summary>
    private float SpeedScale() => climbSpeed * Mathf.Lerp(1f, fastClimbBoost, fastBlend);

    // ---------------- Lunge ----------------

    /// <summary>Throws the body up the wall (towards where the player looks) and lets the hands grab again on the way.</summary>
    private void Lunge(float horizontal)
    {
        Vector3 direction = (WallUp() + WallRight() * (horizontal * 0.5f)).normalized;
        lastEvent = "Lunged up the wall";
        Exit(direction * lungeSpeed - wallNormal * 0.8f);
        regrabTimer = 0.18f;
        lungeTimer = lungeGrabWindow;
    }

    private Vector3 WallUp()
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, wallNormal);
        return up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.up;
    }

    private Vector3 WallRight() => Vector3.Cross(wallNormal, WallUp()).normalized;

    // ---------------- Over the top ----------------

    /// <summary>
    /// No goo: walking (or jumping) forward into an edge whose top is within arm's reach plants both hands on it
    /// and pulls the body over, like the end of a climb. Nothing higher can be climbed.
    /// </summary>
    private void TryLipPull()
    {
        if (climbing || mantling || controller == null || body == null || controller.BubbleMovementBlocked || BoneRide.Mount != null) return;
        Keyboard keyboard = Keyboard.current;
        float vertical = ScriptedInput?.y ?? (keyboard != null && keyboard[upKey].isPressed ? 1f : 0f);
        regrabTimer = Mathf.Max(0f, regrabTimer - Time.deltaTime);
        if (vertical <= 0f || regrabTimer > 0f || !FindWall(out RaycastHit wall, withoutGoo: true)) return;
        wallNormal = wall.normal;
        Vector3 anchor = body.position + Vector3.up * handHeight;
        if (!FindLedgeTop(anchor, true, out Vector3 top)) return;
        float feetY = body.position.y - halfHeight;
        if (top.y > body.position.y + lipReach || top.y < feetY + 0.45f) return;

        // Same start as a climb, then straight into the pull-up.
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 chest = controller.transform.position + Vector3.up * 0.1f + WallRight() * (0.25f * side) + wall.normal * 0.15f;
            hands[i] = new Hand { hold = chest, normal = wall.normal, from = chest, to = chest, toNormal = wall.normal };
            Vector3 foot = body.position + Vector3.down * halfHeight * 0.85f;
            footholds[i] = new Foot { hold = foot };
            feet[i] = foot;
        }
        climbing = true;
        lipOnly = true;
        bodyVelocity = Vector3.zero;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        controller.SetRestricted(true);
        controller.SetClimbing(true);
        StartMantle(anchor, top, "Pulled up over a lip (no goo)");
    }

    private void StartMantle(Vector3 anchor, Vector3 top, string note)
    {
        Vector3 inward = FlatInward();
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 lip = FindLip(anchor, top, inward);

        mantling = true;
        mantleT = 0f;
        mantleFrom = body.position;
        // Pull: chest up to the edge, body close to the wall.
        mantlePull = new Vector3(lip.x, lip.y - halfHeight * 0.2f, lip.z) - inward * 0.6f;
        mantlePull.y = Mathf.Max(mantlePull.y, mantleFrom.y);
        mantleTo = top + Vector3.up * (halfHeight + 0.05f);
        // Push: arms straighten, hips come up over the edge.
        mantlePush = new Vector3(lip.x, lip.y + halfHeight * 0.45f, lip.z) + inward * 0.15f;
        mantlePush.y = Mathf.Max(mantlePush.y, mantleTo.y - halfHeight * 0.4f);
        float length = Vector3.Distance(mantleFrom, mantlePull) + Vector3.Distance(mantlePull, mantlePush) + Vector3.Distance(mantlePush, mantleTo);
        mantleDuration = pullUpTime * Mathf.Clamp(length / 2.4f, 0.8f, 1.8f);
        lastEvent = note + $" (ground {Vector3.Angle(standNormal, Vector3.up):0} deg)";

        // Both hands go to the edge, shoulder width apart.
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? -1f : 1f;
            Vector3 plant = lip + side * (0.24f * s) + inward * 0.1f;
            if (Physics.Raycast(plant + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 1.2f, climbLayers, QueryTriggerInteraction.Ignore))
                plant = hit.point;
            hands[i] = new Hand { moving = true, from = HandPosition(i), to = plant, normal = hands[i].normal, toNormal = Vector3.up, t = 0f };
        }
    }

    private void UpdateMantle(float deltaTime)
    {
        Vector3 before = MantlePoint(Mathf.Clamp01(mantleT));
        mantleT += deltaTime * Mathf.Sqrt(SpeedScale()) / mantleDuration;
        float t = Mathf.Clamp01(mantleT);

        // Hands plant on the edge during the first fifth, the right one a moment after the left.
        for (int i = 0; i < 2; i++)
        {
            if (!hands[i].moving) continue;
            hands[i].t = Mathf.Clamp01((t - (i == 0 ? 0f : 0.05f)) / 0.15f);
            if (hands[i].t < 1f) continue;
            hands[i].moving = false;
            hands[i].hold = hands[i].to;
            hands[i].normal = Vector3.up;
            lagVelocity += Vector3.down * 0.3f;
        }

        Vector3 position = MantlePoint(t);
        mantleVelocity = deltaTime > 0f ? (position - before) / deltaTime : Vector3.zero;
        body.MovePosition(OutOfWalls(body.position, position));
        UpdateFeet(deltaTime);

        if (mantleT >= 1f)
        {
            mantling = false;
            Exit(Vector3.zero);
        }
    }

    /// <summary>Body centre during the pull-up: settle into the arms, pull, push over, stand.</summary>
    private Vector3 MantlePoint(float t)
    {
        const float plantEnd = 0.2f, pullEnd = 0.56f, pushEnd = 0.84f;
        if (t < plantEnd)
            return mantleFrom + Vector3.down * (0.05f * Mathf.Sin(t / plantEnd * Mathf.PI));
        if (t < pullEnd)
            return Vector3.Lerp(mantleFrom, mantlePull, Ease((t - plantEnd) / (pullEnd - plantEnd)));
        if (t < pushEnd)
        {
            // Up first, then rolling forward over the edge.
            float u = Ease((t - pullEnd) / (pushEnd - pullEnd));
            Vector3 corner = new Vector3(mantlePull.x, mantlePush.y, mantlePull.z);
            return Vector3.Lerp(Vector3.Lerp(mantlePull, corner, u), Vector3.Lerp(corner, mantlePush, u), u);
        }
        return Vector3.Lerp(mantlePush, mantleTo, Ease((t - pushEnd) / (1f - pushEnd)));
    }

    /// <summary>Smooth, but keeps a little speed at the joins so the move flows.</summary>
    private static float Ease(float u)
    {
        u = Mathf.Clamp01(u);
        return Mathf.Lerp(u, u * u * (3f - 2f * u), 0.75f);
    }

    // ---------------- Camera weight ----------------

    private void LateUpdate()
    {
        if (viewCamera == null || body == null) return;
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;
        feel = Mathf.MoveTowards(feel, climbing ? 1f : 0f, deltaTime * 4f);
        if (feel <= 0f && lagOffset == Vector3.zero) return;

        // The view trails the body a little when it is pulled, then catches up with a soft overshoot.
        Vector3 velocity = mantling ? mantleVelocity : climbing ? bodyVelocity : Vector3.zero;
        Vector3 lagTarget = Vector3.ClampMagnitude(-velocity * cameraLag, 0.12f) * feel;
        // SmoothDamp stays stable through long frames; grip jolts are added to lagVelocity.
        lagOffset = Vector3.SmoothDamp(lagOffset, lagTarget, ref lagVelocity, 0.09f, Mathf.Infinity, deltaTime);
        lagOffset = Vector3.ClampMagnitude(lagOffset, 0.2f);
        if (float.IsNaN(lagOffset.x + lagOffset.y + lagOffset.z)) lagOffset = lagVelocity = Vector3.zero;
        if (feel <= 0f && lagOffset.sqrMagnitude < 1e-6f && lagVelocity.sqrMagnitude < 1e-4f)
        {
            lagOffset = lagVelocity = Vector3.zero;
            roll = look = 0f;
            viewCamera.transform.localPosition = cameraBasePosition;
            viewCamera.transform.localRotation = cameraBaseRotation;
            return;
        }

        // Tilt the head towards the holding hand while the other one reaches.
        float rollTarget = 0f;
        if (climbing && !mantling)
            for (int i = 0; i < 2; i++)
                if (hands[i].moving)
                    rollTarget += (i == 0 ? -1f : 1f) * reachTilt * Mathf.Sin(Mathf.Clamp01(hands[i].t) * Mathf.PI);
        roll = Mathf.Lerp(roll, rollTarget, 1f - Mathf.Exp(-10f * deltaTime));

        // The head follows the hands up and down, so reaches and the pull-up stay in view:
        // up at the edge while pulling, down at it while pushing over.
        Transform holder = viewCamera.transform.parent;
        float lookTarget = 0f;
        if (climbing && holder != null && !(mantling && mantleT > 0.86f))
        {
            Vector3 local = holder.InverseTransformPoint((HandPosition(0) + HandPosition(1)) * 0.5f);
            if (local.z > 0.05f)
            {
                // Aim a little above the hands so they sit low in the view.
                float toHands = -Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg - 6f;
                float holderPitch = Mathf.DeltaAngle(0f, holder.eulerAngles.x);
                lookTarget = toHands * (mantling ? lookAtHandsPullUp : lookAtHands);
                lookTarget = Mathf.Clamp(lookTarget, Mathf.Max(-25f, -85f - holderPitch), Mathf.Min(25f, 85f - holderPitch));
            }
        }
        look = Mathf.Lerp(look, lookTarget, 1f - Mathf.Exp(-6f * deltaTime));

        Vector3 localLag = holder != null ? holder.InverseTransformVector(lagOffset) : lagOffset;
        viewCamera.transform.localPosition = cameraBasePosition + localLag;
        viewCamera.transform.localRotation = cameraBaseRotation * Quaternion.Euler(look * feel, 0f, roll * feel);
    }

    // ---------------- Exits ----------------

    private void Exit(Vector3 velocity)
    {
        climbing = false;
        mantling = false;
        lipOnly = false;
        regrabTimer = regrabDelay;
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = velocity;
        }
        if (controller != null)
        {
            controller.SetRestricted(false);
            controller.SetClimbing(false);
        }
    }

    private void OnDisable()
    {
        ProceduralWorld.AnyTerrainChanged -= TerrainChanged;
        if (climbing) Exit(Vector3.zero);
    }
}
