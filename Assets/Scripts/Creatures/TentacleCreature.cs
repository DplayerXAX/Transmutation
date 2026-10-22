using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract octopus: a skeleton of primitive spheres and boxes wrapped in an irregular,
/// translucent membrane that is rebuilt around the bones every frame.
/// Each tentacle is a chain of joints that reaches for nearby ground or walls and sticks there,
/// stepping to a new hold when it is stretched too far. The body wanders and floats on its tentacles.
/// While carried, gripping tentacles let the player climb up or down along nearby surfaces.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(1100)]
public sealed class TentacleCreature : Creature
{
    [Header("Body")]
    [Min(0.05f)] [SerializeField] private float bodyRadius = 0.35f;
    [Tooltip("Height of the body centre above the ground while wandering.")]
    [Min(0f)] [SerializeField] private float hoverHeight = 0.9f;
    [SerializeField] private float bobAmplitude = 0.08f;
    [SerializeField] private float bobSpeed = 1.6f;
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Material jointMaterial;
    [SerializeField] private Material tipMaterial;
    [Tooltip("Changes the random shape: limb lengths, lumps, deformed bones.")]
    [SerializeField] private int seed = 13;
    [Tooltip("0 = tidy and symmetric, 1 = very uneven.")]
    [Range(0f, 1f)] [SerializeField] private float asymmetry = 0.85f;

    [Header("Membrane")]
    [Tooltip("Skin wrapped around the skeleton. Leave empty to show only the bones.")]
    [SerializeField] private Material membraneMaterial;
    [Tooltip("Tentacle skin radius as a multiple of the joint radius.")]
    [Min(1f)] [SerializeField] private float membraneThickness = 2.6f;
    [Tooltip("Body skin radius as a multiple of the body radius.")]
    [Min(1f)] [SerializeField] private float bodySkinScale = 1.45f;
    [Tooltip("How lumpy and uneven the skin is.")]
    [Range(0f, 0.6f)] [SerializeField] private float irregularity = 0.24f;
    [Range(6, 40)] [SerializeField] private int membraneRings = 22;
    [Range(5, 16)] [SerializeField] private int membraneSides = 10;
    [Tooltip("Chance per tentacle of a deformed bone pushing out through the skin.")]
    [Range(0f, 1f)] [SerializeField] private float exposedBoneChance = 0.6f;

    [Header("Tentacles")]
    [Range(3, 12)] [SerializeField] private int tentacleCount = 7;
    [Range(3, 16)] [SerializeField] private int jointsPerTentacle = 7;
    [Tooltip("Furthest a tentacle tip can be from the body centre.")]
    [Min(0.5f)] [SerializeField] private float reach = 2.4f;
    [Tooltip("A planted tip steps when its ideal hold drifts this far away.")]
    [Min(0.1f)] [SerializeField] private float stepDistance = 1.1f;
    [Min(0.02f)] [SerializeField] private float stepDuration = 0.22f;
    [Min(0f)] [SerializeField] private float stepHeight = 0.45f;
    [Min(0.005f)] [SerializeField] private float jointRadius = 0.0765f;
    [Min(0.005f)] [SerializeField] private float tipRadius = 0.03825f;
    [Tooltip("Surfaces the tentacles can stick to.")]
    [SerializeField] private LayerMask surfaceLayers = (1 << 0) | (1 << 7) | (1 << 8);

    [Header("Wandering")]
    [Min(0f)] [SerializeField] private float wanderRadius = 6f;
    [Min(0f)] [SerializeField] private float moveSpeed = 1.2f;
    [Min(0f)] [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private Vector2 pauseTimeRange = new Vector2(0.5f, 2.5f);
    [Tooltip("Fall speed when no ground is under the body.")]
    [Min(0f)] [SerializeField] private float fallSpeed = 4f;

    [Header("Climb Assist (while carried)")]
    [Tooltip("Planted tentacles needed before the creature can pull the player.")]
    [Min(1)] [SerializeField] private int minGripsToClimb = 2;
    [Min(0f)] [SerializeField] private float climbUpSpeed = 4f;
    [Min(0f)] [SerializeField] private float climbDownSpeed = 3f;
    [Min(0f)] [SerializeField] private float climbAcceleration = 25f;
    [SerializeField] private Key climbUpKey = Key.Space;
    [SerializeField] private Key climbDownKey = Key.LeftCtrl;
    [Tooltip("Climbing up needs a hold at least this far above the player's centre.")]
    [Min(0f)] [SerializeField] private float climbClearance = 0.3f;
    [SerializeField] private string climbUpPrompt = "[Space]  Climb up";
    [SerializeField] private string climbDownPrompt = "[Ctrl]  Climb down";

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int plantedTentacles;
    [SerializeField] private bool gripping;
    [SerializeField] private bool canClimbUp;
    [SerializeField] private bool canHang;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!IsDead) FirstPersonBody.LimbsPosed += StickToArms;
    }
    private void OnDisableHook() => FirstPersonBody.LimbsPosed -= StickToArms;

    /// <summary>
    /// After the player's arms are posed this frame, carried arms are moved onto them again so they
    /// stay wrapped tight while the arms swing (the creature's own update runs before the arms move).
    /// </summary>
    private void StickToArms()
    {
        if (!SimulationEnabled || !IsCarried || Carrier == null || tentacles == null) return;
        foreach (Tentacle tentacle in tentacles)
        {
            if (tentacle.touchingHand || tentacle.planted || tentacle.stepping || tentacle.pinned) continue;
            tentacle.foot = CoilPoint(tentacle);
            LayoutTentacle(tentacle);
        }
        UpdateMembrane();
    }

    protected override void OnPetStart()
    {
        if (Dragged)
        {
            // Dragging grips the central body, not an arm reaching out to pet the player's hand.
            if (tentacles != null) foreach (Tentacle arm in tentacles) arm.touchingHand = false;
            return;
        }
        // The two arms nearest the hand let go of the ground and reach up to it.
        if (tentacles == null) return;
        int first = -1, second = -1;
        float firstDistance = float.MaxValue, secondDistance = float.MaxValue;
        for (int i = 0; i < tentacles.Length; i++)
        {
            float distance = (tentacles[i].foot - PetPoint).sqrMagnitude;
            if (distance < firstDistance)
            {
                second = first; secondDistance = firstDistance;
                first = i; firstDistance = distance;
            }
            else if (distance < secondDistance)
            {
                second = i; secondDistance = distance;
            }
        }
        foreach (int i in new[] { first, second })
        {
            if (i < 0) continue;
            tentacles[i].touchingHand = true;
            tentacles[i].planted = tentacles[i].stepping = false;
        }
    }

    protected override void OnPetEnd()
    {
        if (tentacles == null) return;
        foreach (Tentacle tentacle in tentacles) tentacle.touchingHand = false;
    }

    /// <summary>The player carrying this creature is climbing (the climber drives the body kinematically).</summary>
    private bool PlayerClimbing => IsCarried && BoneRide.Mount == null && Carrier.PlayerBody != null && Carrier.PlayerBody.isKinematic;

    /// <summary>Enough tentacles are holding a surface to support the player.</summary>
    public bool IsGripping => gripping;

    /// <summary>
    /// Another script moves the body (for example crawling up a tower wall); the creature skips its
    /// own wandering and its free tentacles reach towards that surface instead of the ground below.
    /// </summary>
    public bool ExternalMovement { get; set; }
    /// <summary>Outward normal of the surface the body is crawling on while ExternalMovement is set.</summary>
    public Vector3 CrawlNormal { get; set; } = Vector3.up;

    [Header("Carried On The Back")]
    [Tooltip("Size of the creature while it clings to the player's back.")]
    [Range(0.05f, 1f)] [SerializeField] private float backScale = 0.32f;
    private Vector3 restScale = Vector3.one;
    private bool bodyInitialized;
    /// <summary>Authored free-body size, even while this creature is shrunk on the player's back.</summary>
    public Vector3 UncarriedScale => bodyInitialized ? restScale : transform.localScale;
    // Skin and bones are laid out in world space, so the carried size is applied to them by hand (1 = normal).
    private float limbSize = 1f;
    private bool throwPoseAttached;

    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released)
        => carrier.TentacleLockedForRide;

    public override void PrepareForThrow(PlayerCreatureCarrier carrier)
    {
        if (IsDead) return;
        if (TentacleTether.Instance != null) TentacleTether.Instance.ReleaseCreature(this);
        UnpinAll();
        Suspend(false);
        EndPet();
        LetGo(false);
        Dragged = gripping = canClimbUp = canHang = false;
        plantedTentacles = 0;
        transform.localScale = restScale;
        limbSize = 1f;
        LayoutThrowPose();
        // The procedural skin uses world-space vertices. Attach the finished pose during flight,
        // when simulation is paused, so it travels with the body without rebuilding it.
        if (tentacleRoot != null)
        {
            tentacleRoot.SetParent(transform, true);
            throwPoseAttached = true;
        }
    }

    private void LayoutThrowPose()
    {
        if (tentacles == null) return;
        foreach (Tentacle tentacle in tentacles)
        {
            tentacle.planted = tentacle.stepping = tentacle.touchingHand = false;
            tentacle.foot = DriftPoint(tentacle, Time.time * 2.2f + tentacle.phase, 100f);
            LayoutTentacle(tentacle);
        }
        UpdateMembrane();
    }

    protected override void OnSimulationChanged()
    {
        if (!throwPoseAttached || (!SimulationEnabled && !IsDead)) return;
        throwPoseAttached = false;
        if (tentacleRoot == null) return;
        // Resume normal world-space authoring after landing, recapture, or death.
        tentacleRoot.SetParent(null, false);
        tentacleRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        tentacleRoot.localScale = Vector3.one;
        LayoutThrowPose();
    }

    public override string CarriedPrompt
    {
        get
        {
            if (Carrier != null && Carrier.TentacleLockedForRide)
                return "[LMB] Turn left    [RMB] Turn right    [W/S] Up/Down    [C] Dismount";
            TentacleTether control = TentacleTether.Instance;
            string tether = control != null && control.NewestIsBalloon
                ? "[Hold LMB] Aim / release to shoot    [Hold R] Pull balloon closer    [Tap R] Let go"
                : "[Hold LMB] Aim / release to shoot    [R] Release / hold to reel";
            if (control != null && control.AimingAtBalloon) tether = "[Release LMB] Leash bubble\n" + tether;
            if (!gripping) return tether;
            bool grounded = Carrier != null && Carrier.PlayerController != null && Carrier.PlayerController.Grounded;
            string up = canClimbUp ? climbUpPrompt : null;
            string down = grounded ? null : climbDownPrompt;
            if (up != null && down != null) return tether + "\n" + up + "    " + down;
            return tether + (up != null || down != null ? "\n" + (up ?? down) : "");
        }
    }

    private sealed class Tentacle
    {
        public Vector3 restDirection;    // Horizontal direction in body space.
        public float elevation;          // Probe tilt used while carried, in degrees.
        public float phase;
        public bool planted;
        public bool touchingHand;        // Reaching up to the player's petting hand.
        public bool wrapping;            // Wrapped round caught prey.
        public bool pinned;              // Shot out and stuck to a point (TentacleTether).
        public Vector3 pinPoint;
        // Stuck to something that moves (a bone child, the colossus): the point rides along with it.
        public Transform pinAnchor;
        public Vector3 pinLocal;
        public LeashBubble pinBubble;
        public Vector3 Pin => pinBubble != null && pinBubble.IsLeashed ? pinBubble.LeashPoint
            : pinAnchor != null ? pinAnchor.TransformPoint(pinLocal) : pinPoint;
        public float pinTime;
        public Vector3 hold;
        public Vector3 holdNormal = Vector3.up;
        public Collider holdCollider;
        public Collider stepCollider;
        public Vector3 foot;
        public bool stepping;
        public float stepTime;
        public Vector3 stepFrom;
        public Vector3 stepTo;
        public Vector3 stepNormal;
        public Transform[] joints;
        public Transform[] links;

        // Curve shared by the bones and the membrane, refreshed every frame.
        public Vector3 root, c1, c2, tip, side, outward;
        // A shot arm on the player: runs from the body to the player's hand (via), then straight out to the tip.
        public bool hasVia;
        public Vector3 via;
        public float viaShare;
        public float rippleTime;

        // Per-limb randomness so no two arms match.
        public float reach;
        public float thickness = 1f;
        public float rootHeight;
        public float arch = 0.3f;
        public float rippleAmplitude = 0.06f;
        public float rippleSpeed = 3f;
        public float tumorT;
        public float tumorSize;
        public int deformedJoint = -1;
        public float deformScale = 1f;
        public float deformAngle;
    }

    private struct BodyBump
    {
        public Vector3 direction;
        public float size;
        public float sharpness;
    }

    private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int ActivityId = Shader.PropertyToID("_Activity");

    private readonly List<Renderer> partRenderers = new List<Renderer>();
    private MaterialPropertyBlock propertyBlock;
    private float activity;

    private Tentacle[] tentacles;
    private Transform tentacleRoot;
    private Transform bodyVisual;
    private Transform crown;
    private Vector3[] curve;
    private BodyBump[] bodyBumps = new BodyBump[0];
    private Vector3 bodyStretch = Vector3.one;
    private Vector3 home;
    private Vector3 wanderTarget;
    private float pauseTimer;
    private bool wasCarried;

    protected override void InitializeCreature()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        home = transform.position;
        wanderTarget = home;
        restScale = transform.localScale;
        bodyInitialized = true;
        curve = new Vector3[jointsPerTentacle];
        BuildVisuals();
    }

    // ---------------- Shot-out arms (TentacleTether) ----------------

    /// <summary>Shoots a free arm out to stick at a point. Returns its index, or -1 if every arm is busy.</summary>
    public int PinArm(Vector3 point) => PinArm(point, null);

    /// <summary>Like PinArm, but stuck to anchor: the arm's end moves with it.</summary>
    public int PinArm(Vector3 point, Transform anchor)
    {
        if (!SimulationEnabled || tentacles == null) return -1;
        // The free arm whose tip is nearest the target goes.
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle t = tentacles[i];
            if (t.pinned || t.wrapping || t.touchingHand) continue;
            float d = (t.foot - point).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        if (best < 0) return -1;
        Tentacle arm = tentacles[best];
        arm.pinned = true;
        arm.planted = arm.stepping = false;
        arm.pinPoint = point;
        arm.pinAnchor = anchor;
        arm.pinBubble = anchor != null ? anchor.GetComponentInParent<LeashBubble>() : null;
        arm.pinLocal = anchor != null ? anchor.InverseTransformPoint(point) : Vector3.zero;
        arm.pinTime = Time.time;
        return best;
    }

    /// <summary>Where a shot arm is stuck (false if that arm is not stuck).</summary>
    public bool ArmPin(int index, out Vector3 point)
    {
        point = default;
        if (tentacles == null || index < 0 || index >= tentacles.Length || !tentacles[index].pinned) return false;
        point = tentacles[index].Pin;
        return true;
    }

    public void UnpinArm(int index)
    {
        if (tentacles == null || index < 0 || index >= tentacles.Length) return;
        tentacles[index].pinned = false;
        LeashBubble bubble = tentacles[index].pinBubble;
        if (bubble != null && bubble.LeashCreature == this) bubble.ReleaseLeash();
    }

    public List<(int arm, LeashBubble bubble)> BalloonLeashes()
    {
        var result = new List<(int arm, LeashBubble bubble)>();
        if (tentacles != null)
            for (int i = 0; i < tentacles.Length; i++)
            {
                Tentacle arm = tentacles[i];
                if (arm.pinned && arm.pinBubble != null && arm.pinBubble.LeashCreature == this) result.Add((i, arm.pinBubble));
            }
        result.Sort((a, b) => a.bubble.LeashOrder.CompareTo(b.bubble.LeashOrder));
        return result;
    }

    public void UnpinTerrainArms()
    {
        if (tentacles == null) return;
        for (int i = 0; i < tentacles.Length; i++)
            if (tentacles[i].pinBubble == null || tentacles[i].pinBubble.LeashCreature != this) UnpinArm(i);
    }

    /// <summary>How many arms are stuck into something under root (reins in a bone child or the colossus).</summary>
    public int PinnedInto(Transform root)
    {
        int n = 0;
        if (tentacles != null && root != null)
            foreach (Tentacle t in tentacles)
                if (t.pinned && t.pinAnchor != null && t.pinAnchor.IsChildOf(root)) n++;
        return n;
    }

    /// <summary>How many arms are shot out and stuck right now.</summary>
    public int PinnedCount
    {
        get { int n = 0; if (tentacles != null) foreach (Tentacle t in tentacles) if (t.pinned) n++; return n; }
    }

    /// <summary>Where the pinned arms are stuck.</summary>
    public List<Vector3> PinnedPoints()
    {
        var points = new List<Vector3>();
        if (tentacles != null) foreach (Tentacle t in tentacles) if (t.pinned) points.Add(t.Pin);
        return points;
    }

    /// <summary>
    /// Hung between its stuck arms (set down after shooting two): it stops walking and falling, and something
    /// else (TentacleTether) moves its body; its pinned arms stay stretched to where they stuck.
    /// </summary>
    public bool Suspended { get; private set; }
    public float TetherBodyRadius => Mathf.Max(0.45f, bodyRadius * bodySkinScale * 1.25f * Mathf.Abs(UncarriedScale.x));

    /// <summary>Set while the player's left hand holds and drags it (PlayerCreaturePetter).</summary>
    public bool Dragged { get; set; }

    public Vector3 BodyGrip(Vector3 viewer)
    {
        Vector3 normal = viewer - transform.position;
        if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
        return transform.position + normal.normalized * (bodyRadius * bodySkinScale * limbSize);
    }

    public void Suspend(bool on)
    {
        if (IsDead) on = false;
        Suspended = on;
        ExternalMovement = on;
        // Both wandering and hanging use swept, script-driven movement, as in InitializeCreature.
        SetMovementPhysics(true, false);
    }

    public void ClearGroundHolds()
    {
        gripping = canClimbUp = canHang = false;
        plantedTentacles = 0;
        if (tentacles == null) return;
        foreach (Tentacle arm in tentacles)
        {
            arm.planted = arm.stepping = arm.touchingHand = false;
            arm.holdCollider = arm.stepCollider = null;
        }
    }

    public void PrepareForDrop()
    {
        ClearGroundHolds();
        Dragged = false; EndPet();
        home = wanderTarget = transform.position;
        wasCarried = false;
        pauseTimer = Mathf.Max(pauseTimer, 0.8f);
    }

    public void UnpinAll()
    {
        if (tentacles == null) return;
        for (int i = 0; i < tentacles.Length; i++) UnpinArm(i);
    }

    /// <summary>The creature's own looks, for things made of its arms (the tether cords).</summary>
    public Material MembraneMaterial => membraneMaterial;
    public Material JointMaterial => jointMaterial;
    public Material LinkMaterial => bodyMaterial;
    public Material TipMaterial => tipMaterial;

    // ---------------- Hunting ----------------

    [Header("Hunting")]
    [Tooltip("Fuzzy creatures nearer than this are stalked and grabbed.")]
    [Min(0f)] [SerializeField] private float huntRadius = 9f;
    [Tooltip("Seconds it holds its catch before letting it squirm free (random in this range).")]
    [SerializeField] private Vector2 holdTime = new Vector2(6f, 11f);
    [Tooltip("Seconds before it hunts again after letting go.")]
    [Min(0f)] [SerializeField] private float huntCooldown = 15f;

    private FuzzCreature prey;
    private bool holding;
    private float holdTimer, huntCooldownTimer, nextPreyScan;

    /// <summary>True while it has a fuzzy creature wrapped in its arms.</summary>
    public bool Holding => holding;

    private void UpdateHunt(float deltaTime)
    {
        huntCooldownTimer = Mathf.Max(0f, huntCooldownTimer - deltaTime);
        bool able = !IsCarried && !ExternalMovement && !IsPetted && huntCooldownTimer <= 0f && huntRadius > 0f;
        if (!able)
        {
            if (prey != null) LetGo(cooldown: IsPetted);
            return;
        }
        if (holding)
        {
            holdTimer -= deltaTime;
            // The prey slipped away (the player freed it), or it has held long enough.
            if (prey == null || !prey.Caught || holdTimer <= 0f) LetGo(cooldown: true);
            return;
        }
        if (prey == null && Time.time >= nextPreyScan)
        {
            nextPreyScan = Time.time + 0.5f;
            float best = huntRadius;
            foreach (FuzzCreature fuzz in FindObjectsByType<FuzzCreature>(FindObjectsSortMode.None))
            {
                if (!fuzz.CanBeAITargeted || fuzz.IsDead || fuzz.Caught || fuzz.IsCarried) continue;
                float d = Vector3.Distance(fuzz.transform.position, transform.position);
                if (d < best) { best = d; prey = fuzz; }
            }
        }
        if (prey == null) return;
        if (!prey.CanBeAITargeted || prey.IsDead || prey.Caught || prey.IsCarried || Vector3.Distance(prey.transform.position, transform.position) > huntRadius * 1.5f) { prey = null; return; }
        // Close enough: the arms nearest it let go of the ground and wrap round it.
        if (Vector3.Distance(prey.transform.position, transform.position) < reach * 1.1f)
        {
            holding = true;
            holdTimer = Random.Range(holdTime.x, holdTime.y);
            prey.Catch(transform);
            var order = new List<int>();
            for (int i = 0; i < tentacles.Length; i++) order.Add(i);
            order.Sort((a, b) => (tentacles[a].foot - prey.transform.position).sqrMagnitude.CompareTo((tentacles[b].foot - prey.transform.position).sqrMagnitude));
            for (int k = 0; k < Mathf.Min(4, order.Count); k++)
            {
                tentacles[order[k]].wrapping = true;
                tentacles[order[k]].planted = tentacles[order[k]].stepping = false;
            }
        }
    }

    private void LetGo(bool cooldown)
    {
        if (prey != null && prey.Caught) prey.Free();
        prey = null;
        holding = false;
        if (tentacles != null) foreach (Tentacle tentacle in tentacles) tentacle.wrapping = false;
        if (cooldown) huntCooldownTimer = huntCooldown;
    }

    protected override void OnDeath()
    {
        LetGo(false);
        UnpinAll();
        Suspended = ExternalMovement = Dragged = false;
        gripping = canClimbUp = canHang = false;
        plantedTentacles = 0;
        OnDisableHook();
        // These meshes were authored in world space. Preserve their pose, then move with the corpse.
        if (tentacleRoot != null) tentacleRoot.SetParent(transform, true);
        if (TentacleTether.Instance != null) TentacleTether.Instance.ReleaseCreature(this);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (wasCarried && !IsCarried) home = transform.position; // Wander around where it was dropped.
        wasCarried = IsCarried;
        UpdateHunt(deltaTime);

        if (!IsCarried && !ExternalMovement) Wander(deltaTime);
        // Shrinks to sit on the hand while carried, grows back when let go.
        Vector3 scale = IsCarried ? restScale * backScale : restScale;
        transform.localScale = Vector3.MoveTowards(transform.localScale, scale, deltaTime * 2f);
        limbSize = Mathf.MoveTowards(limbSize, IsCarried ? backScale : 1f, deltaTime * 2f);
        UpdateTentacles(deltaTime);
        UpdateBodyVisual(deltaTime);
        // The skin is the costly part: rebuilt every frame up close, every few frames far off, not at all
        // while it is out of view (it catches up the moment it is seen again).
        if (SkinWorthUpdating()) UpdateMembrane();
        UpdateShaderState(deltaTime);
    }

    private static readonly Plane[] ViewPlanes = new Plane[6];
    private static int viewPlanesFrame = -1;
    private static Camera viewCamera;

    private bool SkinWorthUpdating()
    {
        if (IsCarried || IsPetted || Suspended || PinnedCount > 0) return true;
        if (viewPlanesFrame != Time.frameCount)
        {
            viewPlanesFrame = Time.frameCount;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera != null) GeometryUtility.CalculateFrustumPlanes(viewCamera, ViewPlanes);
        }
        if (viewCamera == null) return true;
        Vector3 centre = transform.position;
        float extent = (reach + bodyRadius) * 1.5f * Mathf.Max(0.1f, transform.lossyScale.x);
        if (!GeometryUtility.TestPlanesAABB(ViewPlanes, new Bounds(centre, Vector3.one * (extent * 2f)))) return false;
        float distance = Vector3.Distance(viewCamera.transform.position, centre);
        int every = distance < 30f ? 1 : distance < 60f ? 2 : 4;
        return (Time.frameCount + (seed & 3)) % every == 0;
    }

    /// <summary>Feeds the line shader its ring centre and how busy the creature is.</summary>
    private void UpdateShaderState(float deltaTime)
    {
        int steppingCount = 0;
        foreach (Tentacle tentacle in tentacles)
            if (tentacle.stepping) steppingCount++;
        float target = IsCarried ? (gripping ? 1f : 0.6f) : steppingCount / (float)tentacles.Length;
        // Petted: the rings run fast and bright, like purring.
        if (IsPetted) target = 1f;
        activity = Mathf.MoveTowards(activity, target, deltaTime * 2f);

        propertyBlock ??= new MaterialPropertyBlock();
        foreach (Renderer part in partRenderers)
        {
            part.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(CenterId, transform.position);
            propertyBlock.SetFloat(ActivityId, activity);
            part.SetPropertyBlock(propertyBlock);
        }
    }

    // ---------------- Body movement ----------------

    private void Wander(float deltaTime)
    {
        if (IsPetted && Dragged)
        {
            // Held by the hand: pulled along after it, body a little below and behind the grip.
            Vector3 target = PetPoint;
            Vector3 move = target - transform.position;
            MoveOnSurface(move * Mathf.Min(1f, 7f * deltaTime));
            Vector3 flat = Vector3.ProjectOnPlane(move, Vector3.up);
            if (flat.sqrMagnitude > 0.01f)
                FaceCreature(Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat.normalized, Vector3.up), turnSpeed * deltaTime));
            return;
        }
        if (IsPetted)
        {
            // Stays put and turns towards the hand that strokes it.
            Vector3 toHand = Vector3.ProjectOnPlane(PetPoint - transform.position, Vector3.up);
            if (toHand.sqrMagnitude > 1e-4f)
                FaceCreature(Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toHand.normalized, Vector3.up), turnSpeed * 0.5f * deltaTime));
            return;
        }
        Vector3 position = transform.position;
        if (prey != null)
        {
            // Stalking: creep straight at it, a little faster than wandering; holding: stay over it.
            wanderTarget = prey.transform.position;
            if (holding) wanderTarget = position;
        }
        Vector3 toTarget = Vector3.ProjectOnPlane(wanderTarget - position, Vector3.up);

        if (toTarget.magnitude < 0.3f && prey == null)
        {
            pauseTimer -= deltaTime;
            if (pauseTimer <= 0f)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                wanderTarget = home + new Vector3(offset.x, 0f, offset.y);
                pauseTimer = Random.Range(pauseTimeRange.x, pauseTimeRange.y);
            }
        }
        else if (toTarget.sqrMagnitude > 1e-4f)
        {
            Quaternion facing = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            FaceCreature(Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * deltaTime));
            // Only crawl roughly the way the body faces, so turns look deliberate.
            float alignment = Mathf.Clamp01(Vector3.Dot(transform.forward, toTarget.normalized));
            MoveOnSurface(transform.forward * (moveSpeed * (prey != null ? 1.5f : 1f) * alignment * deltaTime));
        }

        // Float on the tentacles: settle towards hover height above whatever is below.
        float probeHeight = reach * 2f;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        if (CastSurface(transform.position + Vector3.up * 0.5f, Vector3.down, probeHeight * 2f, out RaycastHit ground))
        {
            float targetY = ground.point.y + hoverHeight + bob;
            float newY = Mathf.Lerp(transform.position.y, targetY, 1f - Mathf.Exp(-6f * deltaTime));
            MoveOnSurface(Vector3.up * (newY - transform.position.y));
        }
        else
        {
            MoveOnSurface(Vector3.down * (fallSpeed * deltaTime));
        }
    }

    // Kinematic wandering/dragging must sweep before changing the transform: colliders alone do not stop it.
    private void MoveOnSurface(Vector3 move)
    {
        float radius = bodyRadius * bodySkinScale * 1.25f * Mathf.Max(0.1f, transform.lossyScale.x);
        Vector3 at = transform.position;
        // A newly loaded floor can overlap the larger membrane. Recover before sweeping, otherwise every cast starts inside it.
        if (CastSurface(at + Vector3.up * 0.5f, Vector3.down, reach * 4f, out RaycastHit initialFloor) && initialFloor.normal.y > 0.45f)
            at.y = Mathf.Max(at.y, initialFloor.point.y + radius + 0.025f);
        for (int pass = 0; pass < 3 && move.sqrMagnitude > 0.000001f; pass++)
        {
            float distance = move.magnitude;
            RaycastHit nearest = default;
            float limit = distance + 0.025f;
            foreach (RaycastHit hit in Physics.SphereCastAll(at, radius, move / distance, limit, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Creature>() != null
                    || hit.collider.GetComponentInParent<SmoothFirstPersonController>() != null || hit.distance >= limit) continue;
                nearest = hit;
                limit = hit.distance;
            }
            if (nearest.collider == null) { at += move; break; }
            Vector3 step = move.normalized * Mathf.Clamp(limit - 0.025f, 0f, distance);
            at += step;
            move = Vector3.ProjectOnPlane(move - step, nearest.normal);
        }
        // Recover when a terrain chunk appears underneath an already moving creature.
        if (CastSurface(at + Vector3.up * 0.5f, Vector3.down, reach * 4f, out RaycastHit floor) && floor.normal.y > 0.45f)
            at.y = Mathf.Max(at.y, floor.point.y + radius + 0.025f);
        MoveCreature(at - transform.position);
    }

    /// <summary>Resolve a climb grip onto the rendered pinned arm, rather than its wider walking collider.</summary>
    public bool SamplePinnedGrip(Vector3 pin, float t, out Vector3 point, out float radius)
    {
        point = default;
        radius = 0f;
        if (tentacles == null) return false;
        foreach (Tentacle arm in tentacles)
        {
            if (!arm.pinned || (arm.foot - pin).sqrMagnitude > 0.1f) continue;
            point = EvaluateTentacle(arm, Mathf.Clamp01(t));
            radius = SkinRadius(arm, Mathf.Clamp01(t));
            return true;
        }
        return false;
    }

    // ---------------- Climb assist ----------------

    private void FixedUpdate()
    {
        plantedTentacles = 0;
        gripping = canClimbUp = canHang = false;
        if (!SimulationEnabled) return;
        Rigidbody player = IsCarried ? Carrier.PlayerBody : null;
        if (player == null || tentacles == null) return;
        if (Carrier.PlayerController != null && Carrier.PlayerController.BubbleMovementBlocked) return;

        // Only holds above the player can pull it up; holds on the floor below only slow a fall.
        int upperHolds = 0;
        float highestHold = float.MinValue;
        foreach (Tentacle tentacle in tentacles)
        {
            if (!tentacle.planted || tentacle.stepping) continue;
            plantedTentacles++;
            highestHold = Mathf.Max(highestHold, tentacle.hold.y);
            if (tentacle.hold.y > player.position.y) upperHolds++;
        }

        gripping = plantedTentacles >= minGripsToClimb;
        canHang = gripping && upperHolds > 0;
        canClimbUp = canHang && highestHold > player.position.y + climbClearance;
        if (!gripping) return;

        Keyboard keyboard = Keyboard.current;
        if (player.isKinematic || keyboard == null) return;

        bool upPressed = keyboard[climbUpKey].isPressed;
        bool downPressed = keyboard[climbDownKey].isPressed;
        bool grounded = Carrier.PlayerController != null && Carrier.PlayerController.Grounded;

        float targetSpeed;
        if (upPressed && !downPressed && canClimbUp) targetSpeed = climbUpSpeed;
        else if (downPressed && !upPressed && !grounded) targetSpeed = -climbDownSpeed;
        else if (!upPressed && !downPressed && !grounded && canHang) targetSpeed = 0f; // Hang on.
        else return; // Normal movement: walking, jumping and falling are left alone.

        // A controlled descent never speeds up a fall that is already slower.
        if (targetSpeed < 0f && player.linearVelocity.y > targetSpeed && !canHang) return;
        Vector3 velocity = player.linearVelocity;
        velocity.y = Mathf.MoveTowards(velocity.y, targetSpeed, climbAcceleration * Time.fixedDeltaTime);
        player.linearVelocity = velocity;
        if (player.useGravity) player.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }

    // ---------------- Tentacles ----------------

    private void UpdateTentacles(float deltaTime)
    {
        int stepping = 0;
        foreach (Tentacle tentacle in tentacles)
            if (tentacle.stepping) stepping++;
        int maxStepping = Mathf.Max(1, tentacles.Length / 2);

        Vector3 centre = transform.position;
        for (int i = 0; i < tentacles.Length; i++)
        {
            Tentacle tentacle = tentacles[i];
            if (tentacle.pinned)
            {
                // Shot out: the arm stretches from the body to where it stuck, whipping out over a moment.
                float shoot = Mathf.Clamp01((Time.time - tentacle.pinTime) / 0.18f);
                tentacle.foot = Vector3.Lerp(tentacle.foot, tentacle.Pin, shoot < 1f ? 1f - Mathf.Exp(-25f * deltaTime) : 1f);
                LayoutTentacle(tentacle);
                continue;
            }
            if ((tentacle.touchingHand && IsPetted) || (tentacle.wrapping && holding && prey != null))
            {
                // Held up to the petting hand (or wrapped round prey); no stepping until it lets go.
                tentacle.foot = FootPosition(tentacle, deltaTime);
                LayoutTentacle(tentacle);
                continue;
            }

            if (IsCarried && !PlayerClimbing && (tentacle.planted || tentacle.stepping))
            {
                // Back on the hand after a climb: let go of the wall and coil up again.
                if (tentacle.stepping) stepping--;
                tentacle.planted = tentacle.stepping = false;
            }

            bool hasIdeal = FindIdealHold(
                tentacle,
                out Vector3 idealPoint,
                out Vector3 idealNormal,
                out Collider idealCollider
            );

            // Generated colliders retain identity when their mesh changes;
            // a cached world point may no longer exist.
            if (tentacle.planted &&
                (tentacle.holdCollider == null ||
                (tentacle.holdCollider.GetComponent<ProceduralWorldChunk>() != null &&
                !tentacle.holdCollider.Raycast(
                    new Ray(tentacle.hold + tentacle.holdNormal * 0.12f, -tentacle.holdNormal),
                    out _,
                    0.24f))))
            {
                tentacle.planted = false;
            }

            if (tentacle.stepping)
            {
                tentacle.stepTime += deltaTime / stepDuration;
                if (tentacle.stepTime >= 1f)
                {
                    tentacle.stepping = false;
                    tentacle.planted = true;
                    tentacle.hold = tentacle.stepTo;
                    tentacle.holdNormal = tentacle.stepNormal;
                    tentacle.holdCollider = tentacle.stepCollider;
                    stepping--;
                }
            }
            else
            {
                bool outOfReach = tentacle.planted && (tentacle.hold - centre).sqrMagnitude > tentacle.reach * tentacle.reach;
                bool drifted = tentacle.planted && hasIdeal && (tentacle.hold - idealPoint).sqrMagnitude > stepDistance * stepDistance;
                bool wantsHold = !tentacle.planted && hasIdeal;
                bool neighbourStepping = tentacles[(i + 1) % tentacles.Length].stepping ||
                                         tentacles[(i + tentacles.Length - 1) % tentacles.Length].stepping;

                if ((outOfReach || drifted || wantsHold) && hasIdeal && stepping < maxStepping &&
                    (!neighbourStepping || outOfReach))
                {
                    tentacle.stepping = true;
                    tentacle.stepTime = 0f;
                    tentacle.stepFrom = tentacle.foot;
                    tentacle.stepTo = idealPoint;
                    tentacle.stepNormal = idealNormal;
                    tentacle.stepCollider = idealCollider;
                    stepping++;
                }
                else if (outOfReach)
                {
                    tentacle.planted = false; // Let go and curl until a new hold is in reach.
                }
            }

            tentacle.foot = FootPosition(tentacle, deltaTime);
            LayoutTentacle(tentacle);
        }
    }

    /// <summary>Where this tentacle would like to hold right now.</summary>
    private bool FindIdealHold(Tentacle tentacle, out Vector3 point, out Vector3 normal, out Collider collider)
    {
        Vector3 centre = transform.position;
        float reach = tentacle.reach;
        Vector3 outward = Flatten(transform.rotation * tentacle.restDirection);
        point = default;
        normal = Vector3.up;
        collider = null;

        RaycastHit hit;
        if (ExternalMovement && !IsCarried)
        {
            // Crawling on a wall: spread over it, each arm reaching out sideways and into the surface.
            Vector3 wallUp = CrawlNormal.sqrMagnitude > 0.5f ? CrawlNormal.normalized : Vector3.up;
            Vector3 along = Vector3.ProjectOnPlane(transform.rotation * tentacle.restDirection, wallUp);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Vector3.up, wallUp);
            Vector3 direction = (along.normalized * 0.7f - wallUp * 0.7f).normalized;
            if (!CastSurface(centre, direction, reach, out hit) && !CastSurface(centre, -wallUp, reach, out hit))
                return false;
        }
        else if (IsCarried)
        {
            // On the hand it only reaches for surfaces while the player climbs; otherwise it stays coiled
            // so no arms hang across the view.
            // On the player's back its arms stay wrapped round the player's arms, even while climbing.
            if (!PlayerClimbing) return false;
            reach *= 0.7f;
            // Reach out in a fan around the body so some arms find walls above and some below.
            Vector3 axis = Vector3.Cross(Vector3.up, outward);
            Vector3 direction = Quaternion.AngleAxis(-tentacle.elevation, axis) * outward;
            if (!CastSurface(centre, direction, reach, out hit) &&
                !CastSurface(centre, Vector3.Slerp(direction, Vector3.down, 0.6f), reach, out hit))
                return false;
        }
        else
        {
            // Plant ahead of the body in its direction of travel, then look straight down.
            Vector3 origin = centre + outward * (reach * 0.55f) + transform.forward * 0.3f + Vector3.up * reach * 0.5f;
            if (!CastSurface(origin, Vector3.down, reach * 1.6f, out hit) &&
                !CastSurface(centre, outward, reach, out hit))
                return false;
        }

        if ((hit.point - centre).sqrMagnitude > reach * reach) return false;
        point = hit.point;
        normal = hit.normal;
        collider = hit.collider;
        return true;
    }

    private Vector3 FootPosition(Tentacle tentacle, float deltaTime)
    {
        if (tentacle.wrapping && holding && prey != null)
        {
            // Coiled round the catch, each arm circling it at its own height, squeezing in and out.
            float wave = Time.time * 3.1f + tentacle.phase * 2f;
            Vector3 centrePrey = prey.transform.position + Vector3.up * 0.15f;
            float squeeze = 0.32f + 0.08f * Mathf.Sin(Time.time * 5f + tentacle.phase);
            Vector3 coil = centrePrey + new Vector3(Mathf.Cos(wave), Mathf.Sin(wave * 0.6f) * 0.5f, Mathf.Sin(wave)) * squeeze;
            return Vector3.Lerp(tentacle.foot, coil, 1f - Mathf.Exp(-8f * deltaTime));
        }
        if (tentacle.touchingHand && IsPetted)
        {
            // Wrap gently round the petting hand, each arm at its own spot, slowly feeling about.
            float wave = Time.time * 2.4f + tentacle.phase;
            Vector3 sideways = Vector3.Cross(PetNormal, Vector3.up);
            if (sideways.sqrMagnitude < 1e-4f) sideways = Vector3.right;
            sideways.Normalize();
            Vector3 up = Vector3.Cross(sideways, PetNormal);
            Vector3 touch = PetPoint + PetNormal * 0.08f + (sideways * Mathf.Cos(wave) + up * Mathf.Sin(wave * 0.7f)) * 0.09f;
            return Vector3.Lerp(tentacle.foot, touch, 1f - Mathf.Exp(-6f * deltaTime));
        }

        if (tentacle.stepping)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tentacle.stepTime));
            Vector3 lift = (tentacle.stepNormal + Vector3.up).normalized * (Mathf.Sin(t * Mathf.PI) * stepHeight);
            return Vector3.Lerp(tentacle.stepFrom, tentacle.stepTo, t) + lift;
        }

        if (tentacle.planted) return tentacle.hold;

        float time = Time.time * 2.2f + tentacle.phase;
        if (IsCarried && Carrier != null)
        {
            // Clinging to the player's back: the arms reach over the shoulders and wrap round the
            // player's forearms, half on each arm, so the hands (and the holds they grab) are its suckers.
            return CoilPoint(tentacle);
        }

        // Unattached: drift loosely below the body and sway.
        return DriftPoint(tentacle, time, deltaTime);
    }

    /// <summary>Where a carried arm wraps round the player's forearm (exactly, no lag).</summary>
    private Vector3 CoilPoint(Tentacle tentacle)
    {
        {
            int index = System.Array.IndexOf(tentacles, tentacle);
            Transform hand = index % 2 == 0 ? Carrier.HandAnchor : Carrier.LeftHandAnchor;
            if (hand == null) hand = Carrier.HandAnchor;
            Vector3 back = hand != null ? -hand.forward : Carrier.transform.position - transform.position;
            back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.down;
            Vector3 across = Vector3.Cross(back, Vector3.up);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.right;
            across.Normalize();
            Vector3 around = Vector3.Cross(across, back);
            Vector3 wrist = hand != null ? hand.position : transform.position;
            float angle = tentacle.phase * 2.4f + Time.time * 0.6f;
            // Spread from the wrist up the forearm, one wrap each.
            float distance = 0.02f + 0.18f * ((index / 2) / Mathf.Max(1f, tentacles.Length / 2f - 1f));
            return wrist + back * distance + (across * Mathf.Cos(angle) + around * Mathf.Sin(angle)) * 0.045f;
        }
    }

    private Vector3 DriftPoint(Tentacle tentacle, float time, float deltaTime)
    {
        Vector3 outward = Flatten(transform.rotation * tentacle.restDirection);
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        Vector3 idle = transform.position + outward * (tentacle.reach * 0.45f) + Vector3.down * (tentacle.reach * 0.35f) +
                       side * (Mathf.Sin(time) * 0.25f) + Vector3.up * (Mathf.Cos(time * 1.3f) * 0.15f);
        return Vector3.Lerp(tentacle.foot, idle, 1f - Mathf.Exp(-8f * deltaTime));
    }

    /// <summary>Places joints along a curve from the body surface to the tip.</summary>
    private void LayoutTentacle(Tentacle tentacle)
    {
        Vector3 outward = Flatten(transform.rotation * tentacle.restDirection);
        Vector3 root = transform.position + outward * (bodyRadius * 0.9f * limbSize) + Vector3.up * (tentacle.rootHeight * limbSize);
        // A shot arm on the player runs from the creature's body along to the player's right hand, then out of
        // the hand straight at what it stuck to, so it is one arm from the body and leaves from the hand.
        tentacle.hasVia = tentacle.pinned && IsCarried && Carrier != null && Carrier.HandAnchor != null;
        if (tentacle.hasVia)
        {
            Transform hand = Carrier.HandAnchor;
            if (BoneRide.MountRoot != null && tentacle.pinAnchor != null && tentacle.pinAnchor.IsChildOf(BoneRide.MountRoot))
            {
                // The first rein goes through the left hand, the second through the right.
                foreach (Tentacle other in tentacles)
                {
                    if (!other.pinned || other.pinAnchor == null || !other.pinAnchor.IsChildOf(BoneRide.MountRoot)) continue;
                    if (other == tentacle && Carrier.LeftHandAnchor != null) hand = Carrier.LeftHandAnchor;
                    break;
                }
            }
            tentacle.via = hand.position;
            float toHand = Vector3.Distance(root, tentacle.via), outOfHand = Vector3.Distance(tentacle.via, tentacle.foot);
            // A good share of the joints for the short stretch to the hand, so it bends smoothly there.
            tentacle.viaShare = Mathf.Clamp(toHand / Mathf.Max(0.01f, toHand + outOfHand), 0.2f, 0.45f);
        }
        Vector3 tip = tentacle.foot;
        if (tentacle.pinned && !IsCarried)
            root = transform.position + (tip - transform.position).normalized * (bodyRadius * 0.9f * limbSize);
        float length = Vector3.Distance(root, tip);
        Vector3 tipNormal = tentacle.stepping ? tentacle.stepNormal
            : tentacle.planted ? tentacle.holdNormal
            : Vector3.up;
        // On the player's back the arms run low round the body and come up to the wrists from below,
        // instead of arching up past the player's head.
        float arch = tentacle.arch;
        if (tentacle.pinned)
        {
            // Pulled taut: nearly straight, the tip pressed flat onto what it stuck to.
            tipNormal = (root - tip).normalized;
            arch *= 0.05f;
        }
        else if (IsCarried && !tentacle.planted && !tentacle.stepping)
        {
            tipNormal = Vector3.down;
            arch *= 0.1f;
        }

        // Arch up and out of the body, then come down onto the surface along its normal.
        tentacle.root = root;
        tentacle.tip = tip;
        tentacle.outward = outward;
        tentacle.c1 = root + outward * (length * 0.4f) + Vector3.up * (length * arch);
        tentacle.c2 = tip + tipNormal * (length * 0.35f);
        if (tentacle.pinned && !IsCarried)
        {
            tentacle.c1 = Vector3.Lerp(root, tip, 1f / 3f);
            tentacle.c2 = Vector3.Lerp(root, tip, 2f / 3f);
        }
        tentacle.side = Vector3.Cross(Vector3.up, outward);
        tentacle.rippleTime = Time.time * tentacle.rippleSpeed + tentacle.phase;

        int count = curve.Length;
        for (int j = 0; j < count; j++)
            curve[j] = EvaluateTentacle(tentacle, j / (float)(count - 1));

        for (int j = 0; j < count; j++)
        {
            float t = j / (float)(count - 1);
            float radius = Mathf.Lerp(jointRadius, tipRadius, t) * tentacle.thickness * limbSize;
            Transform joint = tentacle.joints[j];
            joint.position = curve[j];
            joint.localScale = Vector3.one * (radius * 2f);
            if (j == tentacle.deformedJoint)
            {
                // A swollen bone knot pushed sideways so it breaks through the skin.
                Vector3 along = (curve[Mathf.Min(j + 1, count - 1)] - curve[Mathf.Max(j - 1, 0)]).normalized;
                Vector3 across = Vector3.ProjectOnPlane(tentacle.side, along).normalized;
                Vector3 push = Quaternion.AngleAxis(tentacle.deformAngle, along) * across;
                joint.position = curve[j] + push * (SkinRadius(tentacle, t) * 0.95f);
                joint.localScale = new Vector3(1.3f, 0.8f, 1f) * (radius * 2f * tentacle.deformScale);
                joint.rotation = Quaternion.LookRotation(push, along);
            }

            if (j == count - 1) continue;
            Vector3 delta = curve[j + 1] - curve[j];
            Transform link = tentacle.links[j];
            float linkLength = delta.magnitude;
            if (linkLength < 1e-4f)
            {
                link.localScale = Vector3.zero;
                continue;
            }
            float width = radius * 0.55f; // Thin bones; the membrane gives the volume.
            link.SetPositionAndRotation(curve[j] + delta * 0.5f, Quaternion.LookRotation(delta / linkLength, Vector3.up));
            link.localScale = new Vector3(width, width, linkLength);
        }
    }

    /// <summary>Point on the tentacle centre line, t = 0 at the body and 1 at the tip.</summary>
    private static Vector3 EvaluateTentacle(Tentacle tentacle, float t)
    {
        if (tentacle.hasVia)
        {
            // Body to hand: a soft curve that comes into the hand already pointing at the target.
            Vector3 outDir = (tentacle.tip - tentacle.via).normalized;
            if (t <= tentacle.viaShare)
            {
                float s = t / tentacle.viaShare, w = 1f - s;
                float span = Vector3.Distance(tentacle.root, tentacle.via);
                Vector3 c = tentacle.via - outDir * (span * 0.6f);
                return w * w * tentacle.root + 2f * w * s * c + s * s * tentacle.via;
            }
            // Hand to the stuck tip: nearly straight, a slight sag in the middle.
            float k = (t - tentacle.viaShare) / (1f - tentacle.viaShare);
            float length = Vector3.Distance(tentacle.via, tentacle.tip);
            return Vector3.Lerp(tentacle.via, tentacle.tip, k) + Vector3.down * (Mathf.Sin(k * Mathf.PI) * length * 0.03f);
        }
        float u = 1f - t;
        Vector3 point = u * u * u * tentacle.root + 3f * u * u * t * tentacle.c1 +
                        3f * u * t * t * tentacle.c2 + t * t * t * tentacle.tip;
        // Ripple strongest mid-tentacle, zero at both ends so holds stay put.
        return tentacle.pinned ? point : point + tentacle.side * (Mathf.Sin(tentacle.rippleTime - t * 5f) * tentacle.rippleAmplitude * Mathf.Sin(t * Mathf.PI));
    }

    private void UpdateBodyVisual(float deltaTime)
    {
        if (bodyVisual == null) return;
        // A slow squash and stretch, like breathing.
        float pulse = 1f + 0.06f * Mathf.Sin(Time.time * bobSpeed * 2f);
        bodyVisual.localScale = new Vector3(pulse, 2f - pulse, pulse) * (bodyRadius * 2f);
        // Exposed spurs twitch now and then instead of turning like a machine.
        if (crown != null)
            crown.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 7f) * Mathf.Max(0f, Mathf.Sin(Time.time * 0.6f)) * 6f, 0f, 0f);
    }

    // ---------------- Helpers ----------------

    /// <summary>Raycast against surfaces, skipping creatures and the player carrying this one.</summary>
    private bool CastSurface(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, distance, surfaceLayers, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        Transform carrierRoot = IsCarried ? Carrier.transform.root : null;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = HitBuffer[i];
            Transform hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(transform)) continue;
            if (carrierRoot != null && hitTransform.IsChildOf(carrierRoot)) continue;
            if (hit.collider.GetComponentInParent<Creature>() != null) continue;
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = hit;
            }
        }
        return bestDistance < float.MaxValue;
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
    }

    // ---------------- Construction ----------------

    private void BuildVisuals()
    {
        var lightColor = new Color(0.92f, 0.92f, 0.9f);
        var darkColor = new Color(0.06f, 0.06f, 0.07f);
        var accentColor = new Color(1f, 0.78f, 0.015f);
        var random = new System.Random(seed);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        float Uneven(float neat, float min, float max) => Mathf.Lerp(neat, Range(min, max), asymmetry);

        bodyVisual = CreatePart(PrimitiveType.Sphere, "Body", transform, bodyMaterial, darkColor);
        bodyVisual.localScale = Vector3.one * (bodyRadius * 2f);

        // Body: a few lopsided swellings and an uneven stretch.
        bodyStretch = new Vector3(Uneven(1f, 0.8f, 1.3f), Uneven(1f, 0.75f, 1.2f), Uneven(1f, 0.85f, 1.35f));
        bodyBumps = new BodyBump[5];
        for (int i = 0; i < bodyBumps.Length; i++)
        {
            bodyBumps[i] = new BodyBump
            {
                direction = new Vector3(Range(-1f, 1f), Range(-0.3f, 1f), Range(-1f, 1f)).normalized,
                size = Range(0.15f, 0.55f) * asymmetry,
                sharpness = Range(3f, 9f),
            };
        }

        // Bone spurs: most stay buried in the body, a few stab out through the skin.
        crown = new GameObject("Spurs").transform;
        crown.SetParent(transform, false);
        const int spurCount = 8;
        for (int i = 0; i < spurCount; i++)
        {
            Vector3 direction = new Vector3(Range(-1f, 1f), Range(0f, 1f), Range(-1f, 1f)).normalized;
            bool exposed = random.NextDouble() < 0.35;
            Transform spur = CreatePart(PrimitiveType.Cube, "Spur", crown, jointMaterial, lightColor);
            float length = exposed ? Range(0.45f, 0.75f) : Range(0.12f, 0.2f);
            spur.localPosition = direction * (bodyRadius * (exposed ? 1.1f : 0.9f));
            spur.localRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(Range(-25f, 25f), Range(-25f, 25f), 45f);
            spur.localScale = new Vector3(0.07f, 0.07f, length) * (bodyRadius / 0.35f);
        }
        Transform knot = CreatePart(PrimitiveType.Sphere, "Knot", crown, tipMaterial, accentColor);
        knot.localPosition = new Vector3(Range(-0.4f, 0.4f), 1f, Range(-0.4f, 0.4f)).normalized * (bodyRadius * 1.25f);
        knot.localScale = Vector3.one * (bodyRadius * Range(0.35f, 0.6f));

        // Joints are laid out in world space every frame, so they live under an unscaled scene root.
        tentacleRoot = new GameObject(name + " Tentacles").transform;

        tentacles = new Tentacle[tentacleCount];
        int stuntedArm = random.Next(tentacleCount);
        for (int i = 0; i < tentacleCount; i++)
        {
            // Uneven spacing around the body instead of a perfect star.
            float angle = (i + 0.5f + Uneven(0f, -0.4f, 0.4f)) * Mathf.PI * 2f / tentacleCount;
            bool stunted = asymmetry > 0.3f && i == stuntedArm;
            var tentacle = new Tentacle
            {
                restDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                // Alternate reach angles so a held creature finds surfaces above, level and below.
                elevation = (i % 4) switch { 0 => -55f, 1 => 25f, 2 => -15f, _ => 60f } + Range(-15f, 15f),
                phase = Range(0f, 10f),
                reach = reach * (stunted ? 0.45f : Uneven(1f, 0.7f, 1.2f)),
                thickness = stunted ? 1.5f : Uneven(1f, 0.65f, 1.45f),
                rootHeight = bodyRadius * Uneven(-0.3f, -0.75f, 0.35f),
                arch = Uneven(0.3f, 0.12f, 0.55f),
                rippleAmplitude = Uneven(0.06f, 0.02f, 0.14f),
                rippleSpeed = Uneven(3f, 1.6f, 4.8f),
                tumorT = Range(0.2f, 0.75f),
                tumorSize = random.NextDouble() < 0.5 * asymmetry ? Range(0.4f, 1f) : 0f,
                deformedJoint = random.NextDouble() < exposedBoneChance ? random.Next(1, Mathf.Max(2, jointsPerTentacle - 2)) : -1,
                deformScale = Range(1.8f, 2.8f),
                deformAngle = Range(0f, 360f),
                joints = new Transform[jointsPerTentacle],
                links = new Transform[jointsPerTentacle - 1],
            };
            tentacle.foot = transform.position + transform.rotation * tentacle.restDirection * (tentacle.reach * 0.5f);

            var arm = new GameObject("Tentacle " + i).transform;
            arm.SetParent(tentacleRoot, false);
            for (int j = 0; j < jointsPerTentacle; j++)
            {
                bool isTip = j == jointsPerTentacle - 1;
                bool accent = isTip || j == tentacle.deformedJoint;
                tentacle.joints[j] = CreatePart(PrimitiveType.Sphere, "Joint", arm,
                    accent ? tipMaterial : jointMaterial, accent ? accentColor : lightColor);
                if (!isTip)
                    tentacle.links[j] = CreatePart(PrimitiveType.Cube, "Link", arm, bodyMaterial, darkColor);
            }
            tentacles[i] = tentacle;
        }

        BuildMembrane();
    }

    private Transform CreatePart(PrimitiveType type, string partName, Transform parent, Material material, Color fallbackColor)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);

        var renderer = part.GetComponent<MeshRenderer>();
        partRenderers.Add(renderer);
        if (material != null)
            renderer.sharedMaterial = material;
        else
            renderer.material.color = fallbackColor;
        return part.transform;
    }

    // ---------------- Membrane ----------------

    private Mesh membraneMesh;
    private Vector3[] membraneVertices;
    private Vector3[] bodySkinDirections;   // Unit directions in body space.
    private int bodySkinVertexCount;
    private Vector3[] ringCentres;

    private void BuildMembrane()
    {
        if (membraneMaterial == null) return;

        // Body: a sphere with single pole vertices.
        const int latitudes = 10;
        const int longitudes = 18;
        var directions = new List<Vector3> { Vector3.up };
        for (int i = 1; i < latitudes; i++)
        {
            float theta = Mathf.PI * i / latitudes;
            for (int j = 0; j < longitudes; j++)
            {
                float phi = 2f * Mathf.PI * j / longitudes;
                directions.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
            }
        }
        directions.Add(Vector3.down);
        bodySkinDirections = directions.ToArray();
        bodySkinVertexCount = bodySkinDirections.Length;

        var triangles = new List<int>();
        int bottom = bodySkinVertexCount - 1;
        for (int j = 0; j < longitudes; j++)
        {
            int next = (j + 1) % longitudes;
            AddOutwardTriangle(triangles, 0, 1 + j, 1 + next);
            for (int i = 0; i < latitudes - 2; i++)
            {
                int a = 1 + i * longitudes + j, b = 1 + i * longitudes + next;
                int c = a + longitudes, d = b + longitudes;
                AddOutwardTriangle(triangles, a, c, b);
                AddOutwardTriangle(triangles, b, c, d);
            }
            int lastRow = 1 + (latitudes - 2) * longitudes;
            AddOutwardTriangle(triangles, bottom, lastRow + next, lastRow + j);
        }

        // Tentacles: tubes of rings with a closed tip.
        int tubeVertexCount = membraneRings * membraneSides + 1;
        for (int k = 0; k < tentacles.Length; k++)
        {
            int start = bodySkinVertexCount + k * tubeVertexCount;
            for (int r = 0; r < membraneRings - 1; r++)
            for (int s = 0; s < membraneSides; s++)
            {
                int a = start + r * membraneSides + s;
                int b = start + r * membraneSides + (s + 1) % membraneSides;
                int c = a + membraneSides;
                int d = b + membraneSides;
                // Ring frames are (normal, binormal = tangent x normal), so this order faces outward.
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            int tipVertex = start + tubeVertexCount - 1;
            int lastRing = start + (membraneRings - 1) * membraneSides;
            for (int s = 0; s < membraneSides; s++)
            {
                triangles.Add(lastRing + s);
                triangles.Add(lastRing + (s + 1) % membraneSides);
                triangles.Add(tipVertex);
            }
        }

        membraneVertices = new Vector3[bodySkinVertexCount + tentacles.Length * tubeVertexCount];
        ringCentres = new Vector3[membraneRings];
        membraneMesh = new Mesh { name = "Creature Membrane", hideFlags = HideFlags.DontSave };
        membraneMesh.MarkDynamic();
        membraneMesh.indexFormat = membraneVertices.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        membraneMesh.vertices = membraneVertices;
        membraneMesh.SetTriangles(triangles, 0);

        // Surface coordinates that stick to the skin, so tears do not slide as it moves.
        // uv0 = body direction, or (length, cos, sin) around a tube; uv1.x = limb id (0 = body).
        var surface = new List<Vector3>(membraneVertices.Length);
        var limb = new List<Vector2>(membraneVertices.Length);
        foreach (Vector3 direction in bodySkinDirections)
        {
            surface.Add(direction * 1.6f);
            limb.Add(Vector2.zero);
        }
        for (int k = 0; k < tentacles.Length; k++)
        {
            for (int r = 0; r < membraneRings; r++)
            for (int s = 0; s < membraneSides; s++)
            {
                float angle = 2f * Mathf.PI * s / membraneSides;
                surface.Add(new Vector3(r / (float)(membraneRings - 1) * 5f, Mathf.Cos(angle), Mathf.Sin(angle)));
                limb.Add(new Vector2(k + 1, 0f));
            }
            surface.Add(new Vector3(5f, 0f, 0f));
            limb.Add(new Vector2(k + 1, 0f));
        }
        membraneMesh.SetUVs(0, surface);
        membraneMesh.SetUVs(1, limb);

        var skin = new GameObject("Membrane", typeof(MeshFilter), typeof(MeshRenderer));
        skin.transform.SetParent(tentacleRoot, false);
        skin.GetComponent<MeshFilter>().sharedMesh = membraneMesh;
        var skinRenderer = skin.GetComponent<MeshRenderer>();
        skinRenderer.sharedMaterial = membraneMaterial;
        partRenderers.Add(skinRenderer);
    }

    /// <summary>Adds a body triangle, flipping it if it would face into the sphere.</summary>
    private void AddOutwardTriangle(List<int> triangles, int a, int b, int c)
    {
        Vector3 pa = bodySkinDirections[a], pb = bodySkinDirections[b], pc = bodySkinDirections[c];
        Vector3 normal = Vector3.Cross(pb - pa, pc - pa);
        bool outward = Vector3.Dot(normal, pa + pb + pc) > 0f;
        triangles.Add(a);
        triangles.Add(outward ? b : c);
        triangles.Add(outward ? c : b);
    }

    private void UpdateMembrane()
    {
        if (membraneMesh == null) return;
        float time = Time.time;
        Quaternion rotation = transform.rotation;
        Vector3 centre = transform.position;

        // Body: lumpy, breathing blob that swells towards each tentacle root.
        float breath = 1f + 0.07f * Mathf.Sin(time * bobSpeed * 2f);
        float bodySkin = bodyRadius * bodySkinScale * limbSize;
        for (int i = 0; i < bodySkinVertexCount; i++)
        {
            Vector3 local = bodySkinDirections[i];
            Vector3 direction = rotation * local;
            float lopsided = 0f;
            foreach (BodyBump bump in bodyBumps)
                lopsided += bump.size * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(local, bump.direction)), bump.sharpness);
            float lump = Mathf.Sin(direction.x * 4.1f + time * 0.9f) * 0.5f +
                         Mathf.Sin(direction.y * 5.3f - time * 1.3f + direction.z * 3f) * 0.3f +
                         Mathf.Sin(direction.z * 6.7f + time * 0.7f) * 0.2f;
            float swell = 0f;
            foreach (Tentacle tentacle in tentacles)
            {
                Vector3 rootDirection = (tentacle.outward + Vector3.down * 0.4f).normalized;
                swell = Mathf.Max(swell, Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, rootDirection)), 6f));
            }
            float radius = bodySkin * (1f + irregularity * lump + 0.18f * swell + lopsided);
            Vector3 offset = rotation * Vector3.Scale(local, bodyStretch) * radius;
            offset.y *= direction.y > 0f ? 2f - breath : 0.85f; // Breathing dome, squat underside.
            offset.x *= breath;
            offset.z *= breath;
            membraneVertices[i] = centre + offset;
        }

        // Tentacles: tubes around the bone curve, thick at the body and pinched at the tip.
        int tubeVertexCount = membraneRings * membraneSides + 1;
        float tipSkinRadius = tipRadius * membraneThickness * 0.5f;
        for (int k = 0; k < tentacles.Length; k++)
        {
            Tentacle tentacle = tentacles[k];
            int start = bodySkinVertexCount + k * tubeVertexCount;
            for (int r = 0; r < membraneRings; r++)
                ringCentres[r] = EvaluateTentacle(tentacle, r / (float)(membraneRings - 1));

            // Parallel-transport frames keep the rings from twisting.
            Vector3 tangent = (ringCentres[1] - ringCentres[0]).normalized;
            Vector3 normal = Vector3.Cross(tangent, Vector3.up);
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(tangent, Vector3.right);
            normal.Normalize();

            for (int r = 0; r < membraneRings; r++)
            {
                float t = r / (float)(membraneRings - 1);
                Vector3 previous = ringCentres[Mathf.Max(r - 1, 0)];
                Vector3 next = ringCentres[Mathf.Min(r + 1, membraneRings - 1)];
                Vector3 newTangent = next - previous;
                if (newTangent.sqrMagnitude > 1e-8f) tangent = newTangent.normalized;
                Vector3 projected = Vector3.ProjectOnPlane(normal, tangent);
                if (projected.sqrMagnitude > 1e-8f) normal = projected.normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal);

                float baseRadius = SkinRadius(tentacle, t);
                // Slow swellings travel down the arm, like something moving inside.
                float pulse = 1f + 0.25f * Mathf.Max(0f, Mathf.Sin(t * 9f - time * 2.4f + tentacle.phase));
                for (int s = 0; s < membraneSides; s++)
                {
                    float angle = 2f * Mathf.PI * s / membraneSides;
                    float lump = Mathf.Sin(t * 9f + tentacle.phase + time * 1.4f) * 0.5f +
                                 Mathf.Sin(angle * 3f + t * 13f - time + tentacle.phase * 2f) * 0.3f +
                                 Mathf.Sin(angle * 2f - t * 5f + tentacle.phase * 0.7f) * 0.2f;
                    float radius = baseRadius * pulse * (1f + irregularity * lump);
                    membraneVertices[start + r * membraneSides + s] =
                        ringCentres[r] + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius;
                }
            }
            membraneVertices[start + tubeVertexCount - 1] = ringCentres[membraneRings - 1] + tangent * tipSkinRadius;
        }

        membraneMesh.vertices = membraneVertices;
        membraneMesh.RecalculateNormals();
        membraneMesh.RecalculateBounds();
    }

    /// <summary>Unlumped skin radius of a tentacle at t, including its thickness and tumour.</summary>
    private float SkinRadius(Tentacle tentacle, float t)
    {
        float rootRadius = jointRadius * membraneThickness * 1.5f * tentacle.thickness;
        float tipSkinRadius = tipRadius * membraneThickness * 0.5f;
        float radius = Mathf.Lerp(rootRadius, tipSkinRadius, Mathf.Pow(t, 0.7f)) * limbSize;
        float tumor = (t - tentacle.tumorT) / 0.09f;
        return radius * (1f + tentacle.tumorSize * Mathf.Exp(-tumor * tumor));
    }

    protected override void OnDisable()
    {
        UnpinAll();
        base.OnDisable();
        OnDisableHook();
        gripping = false;
    }

    private void OnDestroy()
    {
        if (tentacleRoot != null) Destroy(tentacleRoot.gameObject);
        if (membraneMesh != null) Destroy(membraneMesh);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.78f, 0.015f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, reach * 1.2f);
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
    }
}
