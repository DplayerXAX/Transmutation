using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// With a tentacle creature on the back, the player can shoot its own arms out and stick them to things:
///   - Hold left mouse to aim, release to shoot one of the creature's arms at what the crosshair is on; it stays stretched from the creature to
///     that point. Aim again to shoot a second arm to a second point (a third shot lets the oldest go).
///   - Setting the creature down (right click) with two arms stuck: it hangs between the two points, held by
///     its own two stretched arms. That line is a bridge (walk on it, grip it while climbing) and a slingshot:
///     hold the left mouse button on the creature and walk back to draw the cord, let go and it shoots you
///     (and anything loose at the pocket) out over the line. It never pulls anyone in by itself.
///   - A stuck arm on the carried creature is a spring: walk further than it was shot and it pulls you back.
///   - Picking the creature up again ends the cord. R pulls every shot arm back in.
/// Stuck arms only let go when the player says so (tap R, picking the creature up, a third shot replacing the
/// oldest) or when what they hold is gone. Putting the creature down never lets them go: with two stuck it
/// hangs between them where it can (otherwise it waits where it was put, arms still stuck); with one it waits
/// there, held by that arm. A shot that cannot stick says why next to the crosshair.
/// Added to the player by itself; it does nothing unless the carrier holds a tentacle creature.
/// The earlier carried behaviours (arms round the wrists, sticky climbing) are unchanged.
/// </summary>
[DisallowMultipleComponent]
public sealed class TentacleTether : MonoBehaviour
{
    [Tooltip("Mouse only: hold the right button to reel in on the newest stuck arm (it hauls the player to where it stuck; " +
             "hold W on arrival to grab the wall, and the arm lets go once the hands hold); press both buttons together " +
             "to let every arm go; a quick right click puts the creature down. This key still works too: tap = let go, hold = reel.")]
    [SerializeField] private Key reelKey = Key.R;
    [Min(1f)] [SerializeField] private float reelSpeed = 9f;
    [Min(1f)] [SerializeField] private float reelAcceleration = 30f;
    [Tooltip("How hard a stuck arm pulls the player back once they walk further than it was shot (acceleration per metre).")]
    [Min(0f)] [SerializeField] private float armPull = 5f;
    [Min(0f)] [SerializeField] private float armDamping = 4f;
    [Min(0f)] [SerializeField] private float maxArmPull = 40f;
    [Min(2f)] [SerializeField] private float range = 36f;
    [Range(1, 6)] [SerializeField] private int maxCords = 3;
    [Tooltip("Fastest a cord can fling something (m/s).")]
    [Min(1f)] [SerializeField] private float maxFling = 32f;
    [Tooltip("Furthest the middle of a hung cord can be pulled from its line (metres).")]
    [Min(0.5f)] [SerializeField] private float maxPull = 5f;
    [Tooltip("Launch speed per metre the cord was stretched (m/s).")]
    [Min(1f)] [SerializeField] private float flingPerMetre = 12f;
    [SerializeField] private Color cordColor = new Color(0.45f, 0.2f, 0.8f, 1f);
    [SerializeField] private Color cordGlow = new Color(0.35f, 0.95f, 0.85f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AddToPlayer()
    {
        PlayerCreatureCarrier carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null && carrier.GetComponent<TentacleTether>() == null) carrier.gameObject.AddComponent<TentacleTether>();
    }

    private sealed class Cord
    {
        public Vector3 a, b;
        public Vector3 rest;
        public int armA = -1, armB = -1;
        public GameObject root;
        public BoxCollider solid;      // pin a to the creature's body
        public BoxCollider solidB;     // the creature's body to pin b
        public BoxCollider zone;
        public Mesh mesh;
        public Rigidbody pocketBody;   // what is pushing into the cord right now
        public Vector3 pocket;         // where the cord is bent to (on the line when nothing pushes)
        public float wobble;
        public float age;
        public MeshRenderer skin;
        public Transform[] joints;
        public Transform[] tips;
        public TentacleCreature creature;   // hung between the two points by its own stretched arms
        public bool held;
        public bool teaching;
        public float passUntil;
        public readonly HashSet<Rigidbody> passing = new HashSet<Rigidbody>();
        public readonly Dictionary<Rigidbody, Load> loads = new Dictionary<Rigidbody, Load>();
    }

    private sealed class Load
    {
        public Vector3 entry;
        public float maxStretch;
        public float coolUntil;
    }

    /// <summary>How many times a cord has fired something (for tests).</summary>
    public int Flung { get; private set; }

    private PlayerCreatureCarrier carrier;
    private Camera view;
    private Rigidbody playerBody;
    private readonly List<Cord> cords = new List<Cord>();
    private float shotTimer;
    private Material material;
    private readonly Collider[] hits = new Collider[16];
    private RaycastHit[] aimHits = new RaycastHit[32];
    private readonly Dictionary<int, LeashBubble> balloons = new Dictionary<int, LeashBubble>();
    public int BalloonCount => balloons.Count;
    public bool NewestIsBalloon => pinnedArms.Count > 0 && balloons.ContainsKey(pinnedArms[pinnedArms.Count - 1]);
    public bool AimingAtBalloon => aiming && aimValid && aimBubble != null;
    public bool AimingAtBone => aiming && aimValid && BoneRide.TryGrappleTarget(aimAnchor, aimPoint, out _, out _);
    public PlayerCreatureCarrier BalloonCarrier => carrier;
    public GameObject BalloonPlayer => carrier != null && carrier.PlayerController != null ? carrier.PlayerController.gameObject
        : carrier != null ? carrier.gameObject : null;
    public Vector3 BalloonOrigin => carrier != null && carrier.HandAnchor != null ? carrier.HandAnchor.position
        : playerBody != null ? playerBody.position + Vector3.up * 0.8f : transform.position;
    public Vector3 BalloonVelocity => playerBody != null && !playerBody.isKinematic ? playerBody.linearVelocity : Vector3.zero;

    public void ForgetBalloon(LeashBubble bubble, int arm, bool keepPinned = false)
    {
        if (!balloons.TryGetValue(arm, out LeashBubble attached) || attached != bubble) return;
        balloons.Remove(arm); pinnedArms.Remove(arm); restLengths.Remove(arm);
        if (!keepPinned && lastCarried != null) lastCarried.UnpinArm(arm);
    }

    /// <summary>Release the player's control, while each balloon stays attached to the dropped octopus.</summary>
    public void ParkBalloons(TentacleCreature creature)
    {
        if (creature == null) return;
        foreach (var leash in creature.BalloonLeashes()) leash.bubble.UnbindHandler();
    }

    public void AdoptBalloons(TentacleCreature creature)
    {
        if (creature == null || creature.Carrier != carrier) return;
        if (lastCarried != creature)
        {
            ParkBalloons(lastCarried);
            ResetPull(); lastCarried = creature;
        }
        foreach (var leash in creature.BalloonLeashes())
        {
            if (balloons.ContainsKey(leash.arm)) continue;
            if (!leash.bubble.BindHandler(this)) continue;
            balloons[leash.arm] = leash.bubble;
            pinnedArms.Add(leash.arm); restLengths[leash.arm] = leash.bubble.LeashLength;
        }
    }

    private void ReleaseBalloons()
    {
        if (balloons.Count == 0) return;
        foreach (var entry in new List<KeyValuePair<int, LeashBubble>>(balloons))
        {
            if (entry.Value != null) entry.Value.ReleaseLeash();
            else { if (lastCarried != null) lastCarried.UnpinArm(entry.Key); pinnedArms.Remove(entry.Key); restLengths.Remove(entry.Key); }
        }
        balloons.Clear();
    }

    private void ReleaseArm(TentacleCreature creature, int arm)
    {
        if (balloons.TryGetValue(arm, out LeashBubble bubble) && bubble != null) bubble.ReleaseLeash();
        balloons.Remove(arm); pinnedArms.Remove(arm); restLengths.Remove(arm);
        creature.UnpinArm(arm);
    }

    public int CordCount => cords.Count;

    // Why the last shot could not stick (shown next to the crosshair for a moment).
    private string problem;
    private float problemUntil;
    /// <summary>Why the last shot could not stick, while it is still shown (for tests and the HUD).</summary>
    public string Problem => Time.time < problemUntil ? problem : null;

    /// <summary>Shows a short reason next to the crosshair (for example a boarding pull that was blocked).</summary>
    public void Notice(string message) => Say(message);

    private void Say(string message)
    {
        problem = message;
        problemUntil = Time.time + 1.8f;
    }

    public void ReleaseCreature(TentacleCreature creature)
    {
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == creature) RemoveCord(i);
        if (lastCarried == creature)
        {
            ReleaseBalloons();
            lastCarried = null;
            pinnedArms.Clear();
            restLengths.Clear();
            aiming = aimValid = false;
            reeling = false;
            reelHeldTime = 0f;
            ShowAim(false);
        }
    }

    /// <summary>The player's tether (for the arm pose while aiming).</summary>
    public static TentacleTether Instance { get; private set; }
    public bool Aiming => aiming;
    /// <summary>While aiming: true when the crosshair is on something an arm can stick to.</summary>
    public bool AimValid => aiming && aimValid;
    public Vector3 AimPoint => aimPoint;
    public void PauseInput()
    {
        aiming = aimValid = reeling = false;
        reelHeldTime = 0f;
        reeledArm = -1;
        ShowAim(false);
        // A camera takeover is not the player's deliberate slingshot release.
        foreach (Cord cord in cords) { cord.held = false; cord.passUntil = Time.time + 0.5f; }
    }

    private void Awake()
    {
        Instance = this;
        carrier = GetComponent<PlayerCreatureCarrier>();
        playerBody = GetComponentInParent<Rigidbody>();
        if (playerBody == null)
        {
            var controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller != null) playerBody = controller.GetComponent<Rigidbody>();
        }
    }

    private TentacleCreature lastCarried;

    private void Update()
    {
        if (BoneAwakeningCutscene.Playing) return;
        if (carrier == null) return;
        // A hung creature picked up again: the cord is gone and its arms come back.
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == null || cords[i].creature.IsDead || cords[i].creature.IsCarried) RemoveCord(i);

        TentacleCreature carried = carrier.CarriedCreature as TentacleCreature;
        if (carried != null && carried.IsDead) carried = null;
        if (carried == null)
        {
            if (lastCarried != null) PlaceDroppedCreature(lastCarried);
            else { ResetPull(); ShowAim(false); }
            return;
        }
        if (lastCarried != carried) AdoptBalloons(carried);
        // Boarding owns movement; once seated, LMB can target the next wall or creature.
        if (BoneRide.Approaching)
        {
            aiming = false;
            aimValid = false;
            reeling = false;
            reelHeldTime = 0f;
            ShowAim(false);
            return;
        }
        if (view == null) view = Camera.main;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        // A carried tentacle keeps the same aiming gesture on foot and on a mount.
        // Both buttons together: every arm lets go, and this left press does not aim or shoot.
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && rightDown) Chord();
        if (mouse == null || !mouse.leftButton.isPressed) leftSuppressed = false;
        bool pressed = Application.isFocused && mouse != null && mouse.leftButton.isPressed && !leftSuppressed;
        bool aimNow = pressed && Aim(out aimPoint, out aimValid);
        ShowAim(aimNow);
        if (!pressed && aiming && !leftSuppressed && Application.isFocused && mouse != null && mouse.leftButton.wasReleasedThisFrame)
        {
            if (aimValid) FireAt(aimPoint, aimAnchor);
            else Say(aimAnchor == null ? $"Too far: an arm reaches {range:0} m" : "The arm cannot hold on to that");
        }
        aiming = pressed;
        // Tap R: let go. Hold R: reel in on the newest stuck arm.
        bool reelHeld = ScriptedReel ?? (keyboard != null && keyboard[reelKey].isPressed);
        if (reelHeld) reelHeldTime += Time.deltaTime;
        else if (reelHeldTime > 0f)
        {
            if (reelHeldTime <= TapTime) ReelAll();
            reelHeldTime = 0f;
        }
        if (automaticReel && (Time.time > automaticReelUntil || pinnedArms.Count == 0)) automaticReel = false;
        reeling = (automaticReel || (reelHeld && reelHeldTime > TapTime) || (rightMode == RightReel && rightDown)) && pinnedArms.Count > 0;
        // Once the hands hold a wall, a reeled arm lets go (so it does not pull the climber back down).
        if (climber == null) climber = FindFirstObjectByType<HandClimber>();
        if (climber != null && climber.IsClimbing && reeledArm >= 0)
        {
            // Arrived: the hands have the wall, so the arm lets go. Climbing somewhere else entirely keeps it.
            bool arrived = carried.ArmPin(reeledArm, out Vector3 reeledPin) && playerBody != null && Vector3.Distance(playerBody.position, reeledPin) < 3f;
            if (arrived) ReleaseArm(carried, reeledArm);
            automaticReel = reeling = false;
            reeledArm = -1;
        }
        else if (!reeling && reeledArm >= 0 && carried.ArmPin(reeledArm, out Vector3 stillPin) && playerBody != null
                 && Vector3.Distance(playerBody.position, stillPin) > 3f)
            reeledArm = -1; // Stopped reeling part way: nothing to let go of later.

        shotTimer += Time.deltaTime;
    }

    /// <summary>
    /// Fire at a point as if the crosshair were on it (also used by tests). Each shot sends out another of the
    /// carried creature's own arms to stick there; with more than two out, the oldest lets go.
    /// </summary>
    public void FireAt(Vector3 point) => FireAt(point, null);

    /// <summary>Fires at a point on something that may move (anchor): the arm stays stuck to it as it moves.</summary>
    public void FireAt(Vector3 point, Transform anchor)
    {
        TentacleCreature creature = carrier != null ? carrier.CarriedCreature as TentacleCreature : null;
        if (creature == null) return;
        if (!creature.SimulationEnabled) { Say("The tentacle creature cannot move right now"); return; }
        if (lastCarried != creature) AdoptBalloons(creature);
        LeashBubble bubble = anchor != null ? anchor.GetComponentInParent<LeashBubble>() : null;
        if (bubble != null && (!bubble.CanLeash(this) || balloons.ContainsValue(bubble))) return;
        bool transferring = BoneRide.Mount != null && bubble == null
            && (anchor == null || BoneRide.MountRoot == null || !anchor.IsChildOf(BoneRide.MountRoot));
        if (transferring) BoneRide.GetOff(true);
        bool rein = BoneRide.MountRoot != null && anchor != null && anchor.IsChildOf(BoneRide.MountRoot);
        int freePins = 0, oldestFree = -1;
        foreach (int pin in pinnedArms)
            if (!creature.ArmPinnedInto(pin, BoneRide.MountRoot)) { freePins++; if (oldestFree < 0) oldestFree = pin; }
        if (!rein && freePins >= 2)
        {
            ReleaseArm(creature, oldestFree);
        }
        int arm = creature.PinArm(point, anchor);
        if (arm < 0) Say("No free arm: tap R to let the stuck ones go");
        if (arm >= 0)
        {
            pinnedArms.Add(arm);
            // It is shot at its full length: walking further than this stretches it.
            restLengths[arm] = playerBody != null ? Vector3.Distance(playerBody.position, point) : 0f;
            if (bubble != null)
            {
                float length = Mathf.Clamp(Vector3.Distance(BalloonOrigin, bubble.transform.position) - bubble.Radius, 1.4f, range);
                if (bubble.AttachLeash(this, creature, arm, length)) balloons[arm] = bubble;
                else ReleaseArm(creature, arm);
            }
            else if (BoneRide.TryGrappleTarget(anchor, point, out Component mount, out Transform seat))
            {
                if (!BoneRide.BeginApproach(mount, seat, carrier, true, transferring)) ReleaseArm(creature, arm);
                else PauseInput();
            }
            else if (transferring)
            {
                automaticReel = reeling = true;
                automaticReelUntil = Time.time + 5f;
                if (climber == null) climber = FindFirstObjectByType<HandClimber>();
                climber?.PrepareForTraversalTransfer();
            }
        }
        shotTimer = 0f;
    }

    private readonly List<int> pinnedArms = new List<int>();
    private const float TapTime = 0.2f;

    // The right mouse button while carrying the tentacle creature: a tap puts it down (the carrier does that),
    // holding it reels in, pressing it together with the left button lets every arm go.
    private const int RightNone = 0, RightReel = 1, RightChord = 2;
    private int rightMode;
    private bool rightDown, leftSuppressed;
    private float rightHeldFor;

    /// <summary>
    /// Called by the carried creature with the right button's state each frame. Returns true when the tether
    /// has used the press (reeling, or a two-button let-go), so it is not taken as putting the creature down.
    /// </summary>
    public bool HandleRightMouse(bool pressed, bool held, bool released)
    {
        if (pressed)
        {
            rightMode = RightNone;
            rightHeldFor = 0f;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) Chord();
        }
        rightDown = (pressed || held) && !released;
        if (rightDown) rightHeldFor += Time.deltaTime;
        if (rightDown && rightMode == RightNone && rightHeldFor > TapTime && pinnedArms.Count > 0) rightMode = RightReel;
        bool used = rightMode != RightNone;
        if (released || !rightDown)
        {
            rightDown = false;
            rightMode = RightNone;
        }
        return used;
    }

    /// <summary>Both mouse buttons together: every stuck arm lets go, and the aim in progress is dropped.</summary>
    private void Chord()
    {
        ReelAll();
        rightMode = RightChord;
        leftSuppressed = true;
        aiming = aimValid = false;
        ShowAim(false);
    }
    private float reelHeldTime;
    private bool reeling;
    private bool automaticReel;
    private float automaticReelUntil;
    public bool AutomaticReel => automaticReel;
    private int reeledArm = -1;
    private Vector3 reelApproach;
    private HandClimber climber;

    /// <summary>For tests: holds (true) or lets go of (false) the reel key; null = the keyboard.</summary>
    public bool? ScriptedReel { get; set; }

    /// <summary>True while the newest stuck arm is hauling the player in.</summary>
    public bool Reeling => reeling;
    /// <summary>Physics steps the reel has actually pulled (for tests).</summary>
    public int ReelSteps { get; private set; }
    private readonly Dictionary<int, float> restLengths = new Dictionary<int, float>();

    private bool aiming, aimValid;
    private Vector3 aimPoint;
    private Transform aimAnchor;
    private LeashBubble aimBubble;
    private LineRenderer aimLine;
    private Transform aimMarker;

    /// <summary>Where the crosshair is: a point in range on something solid (not the player).</summary>
    private bool Aim(out Vector3 point, out bool valid)
    {
        point = default;
        valid = false;
        aimAnchor = null; aimBubble = null;
        if (view == null) return false;
        Ray ray = new Ray(view.transform.position, view.transform.forward);
        int count;
        var physics = gameObject.scene.GetPhysicsScene();
        while ((count = physics.Raycast(ray.origin, ray.direction, aimHits, range, ~(1 << 2), QueryTriggerInteraction.Ignore)) == aimHits.Length)
            aimHits = new RaycastHit[aimHits.Length * 2];
        float closest = range + 1f;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = aimHits[i];
            if (hit.distance >= closest || (playerBody != null && hit.rigidbody == playerBody)
                || (BoneRide.MountRoot != null && hit.collider.transform.IsChildOf(BoneRide.MountRoot))
                || (carrier != null && carrier.CarriedCreature != null && hit.collider.GetComponentInParent<Creature>() == carrier.CarriedCreature)) continue;
            closest = hit.distance;
            point = hit.point + hit.normal * 0.05f;
            aimAnchor = hit.collider.transform;
            aimBubble = hit.collider.GetComponentInParent<LeashBubble>();
            valid = aimBubble == null || aimBubble.CanLeash(this);
            if (aimBubble == null && (aimAnchor.GetComponentInParent<BoneChild>() != null || aimAnchor.GetComponentInParent<BoneKiln>() != null))
                valid = BoneRide.TryGrappleTarget(aimAnchor, point, out _, out _);
        }
        if (aimAnchor == null) point = ray.origin + ray.direction * range;
        return true;
    }

    /// <summary>While aiming: a thin line from the hand and a ring on the target (pale when it can stick, dim red when not).</summary>
    private void ShowAim(bool on)
    {
        if (!on && aimLine == null) return;
        if (aimLine == null)
        {
            var go = new GameObject("Tentacle Aim");
            aimLine = go.AddComponent<LineRenderer>();
            aimLine.positionCount = 16;
            aimLine.widthMultiplier = 0.025f;
            aimLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            aimLine.shadowCastingMode = ShadowCastingMode.Off;
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Aim Marker";
            ring.transform.localScale = new Vector3(0.35f, 0.01f, 0.35f);
            ring.GetComponent<Renderer>().sharedMaterial = aimLine.material;
            aimMarker = ring.transform;
        }
        aimLine.gameObject.SetActive(on);
        aimMarker.gameObject.SetActive(on && aimValid);
        if (!on) return;
        Color colour = aimValid ? (AimingAtBone ? new Color(1f, 0.75f, 0.3f)
            : aimBubble != null ? new Color(0.45f, 0.85f, 1f) : new Color(0.85f, 1f, 0.95f)) : new Color(0.6f, 0.15f, 0.2f);
        aimLine.sharedMaterial.SetColor("_BaseColor", colour);
        Vector3 from = carrier.HandAnchor != null ? carrier.HandAnchor.position : view.transform.position + view.transform.forward * 0.5f;
        for (int i = 0; i < 16; i++)
        {
            float t = i / 15f;
            // A gentle arc, like the arm will fly.
            aimLine.SetPosition(i, Vector3.Lerp(from, aimPoint, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * Vector3.Distance(from, aimPoint) * 0.06f));
        }
        if (aimValid)
        {
            aimMarker.position = aimPoint;
            aimMarker.rotation = Quaternion.LookRotation(view.transform.forward) * Quaternion.Euler(90f, 0f, 0f);
            aimMarker.localScale = new Vector3(0.35f, 0.01f, 0.35f) * (1f + 0.15f * Mathf.Sin(Time.time * 8f));
        }
    }

    /// <summary>Tap R: the carried creature pulls every arm back in.</summary>
    /// <summary>Every shot arm lets go (getting off a bone child or the colossus drops the reins).</summary>
    public void LetGoAll() => ReelAll();

    private void ReelAll()
    {
        BoneRide.CancelApproach();
        ResetPull();
        if (carrier != null && carrier.CarriedCreature is TentacleCreature creature) creature.UnpinAll();
    }

    private void ResetPull()
    {
        if (rightMode == RightReel) rightMode = RightChord; // nothing left to reel: the press stays used up
        rightDown = false; // re-read from the button on the next frame the creature is carried
        ReleaseBalloons();
        pinnedArms.Clear();
        restLengths.Clear();
        reeling = false;
        automaticReel = false;
        reelHeldTime = 0f;
        reeledArm = -1;
        aiming = aimValid = false;
    }

    /// <summary>A thrown creature has landed with its arms still stuck: with two or more it hangs between the last two.</summary>
    public void SettleThrown(TentacleCreature creature)
    {
        if (creature == null || creature.IsDead || creature.IsCarried) return;
        List<Vector3> pins = creature.PinnedPoints();
        // Where it cannot hang (rock between it and a pin), the arms just stay stuck.
        if (pins.Count >= 2) MakeCord(pins[pins.Count - 2], pins[pins.Count - 1], creature, keepPinsIfInvalid: true);
    }

    /// <summary>Pickup cancels the old spring without triggering a release/launch.</summary>
    public void DetachForPickup(TentacleCreature creature)
    {
        creature.ForgetThrownArms();
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == creature) RemoveCord(i);
        if (creature.Suspended) creature.Suspend(false);
        ParkBalloons(creature);
        creature.UnpinTerrainArms();
        creature.Dragged = false;
        creature.EndPet();
        ParkBalloons(lastCarried);
        ResetPull();
        lastCarried = null;
    }

    private void RemoveCord(int index)
    {
        Cord cord = cords[index];
        if (cord.root != null) cord.root.SetActive(false); // Stop collision now, not at deferred Destroy after the frame.
        cord.passing.Clear();
        if (cord.creature != null)
        {
            cord.creature.Suspend(false);
            if (carrier != null) carrier.DeferTentacleCollisionRestore(cord.creature);
            else SetCreatureSolid(cord, true);
            // The arms stay stuck: picking it up (DetachForPickup) or its death lets them go, not this.
        }
        if (cord.root != null) Destroy(cord.root);
        cords.RemoveAt(index);
    }

    /// <summary>Called synchronously after carry collision is restored, before the creature's next autonomous tick.</summary>
    public void PlaceDroppedCreature(TentacleCreature creature)
    {
        if (creature == null || creature.IsCarried) return;
        // Thrown: its stuck arms stay as they are while it flies (see SettleThrown for the landing).
        if (creature.ArmsKeptFromThrow)
        {
            if (lastCarried == creature) lastCarried = null;
            ResetPull();
            ShowAim(false);
            return;
        }
        if (!creature.IsDead)
        {
            ParkBalloons(creature);
            if (creature.BalloonLeashes().Count > 0) creature.UnpinTerrainArms();
            else
            {
                // Two stuck: it hangs between the last two. One stuck: it waits where it was put, held by that arm.
                // Either way the arms stay stuck (TentacleCreature.HeldByArms keeps it from wandering off).
                List<Vector3> pins = creature.PinnedPoints();
                if (pins.Count >= 2) MakeCord(pins[pins.Count - 2], pins[pins.Count - 1], creature, keepPinsIfInvalid: true);
            }
        }
        if (lastCarried == creature) lastCarried = null;
        ResetPull();
        ShowAim(false);
    }

    // ---------------- Cords ----------------

    private void MakeCord(Vector3 a, Vector3 b, TentacleCreature creature, bool teaching = false, bool keepPinsIfInvalid = false)
    {
        if ((b - a).magnitude < 0.8f) { if (!keepPinsIfInvalid) creature.UnpinAll(); return; }
        // A player's drop is already its rest pose. Recentering long wall anchors caused a fast, unsolicited flight.
        Vector3 rest = teaching ? (a + b) * 0.5f + Vector3.down * 0.4f : creature.transform.position;
        var cord = new Cord { a = a, b = b, pocket = creature.transform.position, creature = creature, teaching = teaching };
        for (int i = 0; i < 32; i++)
            if (creature.ArmPin(i, out Vector3 pin))
            {
                if ((pin - a).sqrMagnitude < 0.01f) cord.armA = i;
                else if ((pin - b).sqrMagnitude < 0.01f) cord.armB = i;
            }
        // Where it was put down may be against rock (it rode on the player's back). Look for the nearest clear
        // place to hang, between there and the line between the two arms. With none, it is not hung: it waits
        // where it is with its arms still stuck (unless asked to let go).
        if (!PocketClear(cord, cord.pocket))
        {
            if (!FindHangPocket(cord, creature.transform.position, out Vector3 clear))
            {
                if (!keepPinsIfInvalid) creature.UnpinAll();
                return;
            }
            cord.pocket = clear;
            creature.transform.position = clear;
            if (creature.TryGetComponent(out Rigidbody hangBody)) hangBody.position = clear;
        }
        cord.rest = SafePocket(cord, cord.pocket, rest);
        if (!teaching)
        {
            int count = 0, oldest = -1;
            for (int i = 0; i < cords.Count; i++)
                if (!cords[i].teaching) { count++; if (oldest < 0) oldest = i; }
            if (count >= maxCords) RemoveCord(oldest);
        }
        creature.Suspend(true);
        cord.root = new GameObject("Tentacle Cord") { layer = 7 };
        cord.root.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation((b - a).normalized, Vector3.up));
        // Solid: two flat strips along the two stretched arms, from each pin to the creature's body, so what
        // is walked on is where the arms are seen (a bridge with a dip in the middle), and grippable when climbing.
        cord.solid = Strip(cord.root.transform, "Arm A");
        cord.solidB = Strip(cord.root.transform, "Arm B");
        cord.solid.gameObject.AddComponent<TentacleGripSurface>().Setup(creature, a);
        cord.solidB.gameObject.AddComponent<TentacleGripSurface>().Setup(creature, b);
        FitStrips(cord);
        // Nothing else to draw: the creature hangs in the middle and its own two stretched arms are the cord.
        SetCreatureSolid(cord, false);
        // Newly created bridge strips may overlap the player's capsule. Restore contact only after it is clear.
        if (playerBody != null) Pass(cord, playerBody, true);
        cord.passUntil = Time.time + 0.5f;
        cords.Add(cord);
    }

    /// <summary>
    /// The nearest place the hung body fits (clear of rock): along the way from where it was put towards the
    /// middle of the two arms, a little below that middle, and round about it.
    /// </summary>
    private static bool FindHangPocket(Cord cord, Vector3 from, out Vector3 pocket)
    {
        Vector3 mid = (cord.a + cord.b) * 0.5f;
        Vector3 low = mid + Vector3.down * 0.4f;
        Vector3 along = (cord.b - cord.a).normalized;
        Vector3 side = Vector3.Cross(along, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(along, Vector3.forward);
        side.Normalize();
        for (int i = 1; i <= 8; i++)
        {
            pocket = Vector3.Lerp(from, low, i / 8f);
            if (BodyClear(cord, pocket)) return true;
        }
        float[] rings = { 0.6f, 1.2f, 2f };
        foreach (float r in rings)
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI / 4f;
                pocket = low + (Vector3.up * Mathf.Sin(angle) + side * Mathf.Cos(angle)) * r;
                if (BodyClear(cord, pocket)) return true;
            }
        pocket = from;
        return false;
    }

    private static bool BodyClear(Cord cord, Vector3 at)
    {
        int count = Physics.OverlapSphereNonAlloc(at, PocketRadius(cord), terrainOverlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (TerrainSolid(terrainOverlaps[i])) return false;
        return true;
    }

    public bool HangCreature(TentacleCreature creature, Vector3 a, Transform anchorA, Vector3 b, Transform anchorB)
    {
        if (creature == null || creature.IsCarried || creature.Suspended) return false;
        creature.UnpinAll();
        if (creature.PinArm(a, anchorA) < 0 || creature.PinArm(b, anchorB) < 0)
        { creature.UnpinAll(); return false; }
        MakeCord(a, b, creature, true);
        return creature.Suspended;
    }

    private static BoxCollider Strip(Transform parent, string name)
    {
        var go = new GameObject(name) { layer = 7 };
        go.transform.SetParent(parent, false);
        return go.AddComponent<BoxCollider>();
    }

    /// <summary>Lays the two strips from the pins to where the creature hangs (the arms are seen there).</summary>
    private static void FitStrips(Cord cord)
    {
        Vector3 middle = cord.creature != null ? cord.creature.transform.position : cord.pocket;
        Fit(cord.solid, cord.a, middle);
        Fit(cord.solidB, middle, cord.b);
    }

    private static void Fit(BoxCollider strip, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        float length = span.magnitude;
        if (length < 0.05f) { strip.enabled = false; return; }
        strip.enabled = true;
        // Its top runs along the arm, so feet stand on the arm rather than above it; overlaps a little at the body.
        strip.transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(span / length, Vector3.up));
        strip.size = new Vector3(1.0f, 0.35f, length + 0.5f);
        strip.center = new Vector3(0f, -0.1f, 0f);
    }

    private static bool TerrainSolid(Collider collider) => collider != null && !collider.isTrigger
        && collider.GetComponentInParent<Creature>() == null
        && collider.GetComponentInParent<SmoothFirstPersonController>() == null
        && collider.GetComponent<TentacleGripSurface>() == null;

    private static float PocketRadius(Cord cord) => cord.creature != null ? cord.creature.TetherBodyRadius : 0.6f;
    private static readonly RaycastHit[] terrainCasts = new RaycastHit[64];
    private static readonly Collider[] terrainOverlaps = new Collider[64];

    private static RaycastHit[] Sweep(Vector3 from, float radius, Vector3 direction, float distance, out int count)
    {
        count = Physics.SphereCastNonAlloc(from, radius, direction, terrainCasts, distance, ~0, QueryTriggerInteraction.Ignore);
        if (count < terrainCasts.Length) return terrainCasts;
        var all = Physics.SphereCastAll(from, radius, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        count = all.Length;
        return all;
    }

    private static bool ArmClear(Vector3 middle, Vector3 pin)
    {
        Vector3 span = pin - middle;
        float distance = span.magnitude;
        // Leave only the sucker's last few centimetres touching its support.
        if (distance < 0.24f) return true;
        var results = Sweep(middle, 0.16f, span.normalized, distance - 0.24f, out int count);
        for (int i = 0; i < count; i++) if (TerrainSolid(results[i].collider)) return false;
        return true;
    }

    // The body clear of rock. The arms are checked only as a rule for moving it (SafePocket): a hang whose arm
    // already wraps round an edge (stuck round a corner) is allowed, it just may not be dragged to cut deeper.
    private static bool PocketClear(Cord cord, Vector3 at) => BodyClear(cord, at);

    private static bool ArmsClear(Cord cord, Vector3 at) => ArmClear(at, cord.a) && ArmClear(at, cord.b);

    // Sweep the body, then constrain the whole two-arm triangle. A clear destination alone is
    // insufficient: the return path or an arm can otherwise cut through a thin rock.
    private static Vector3 SafePocket(Cord cord, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return from;
        float travel = distance;
        var results = Sweep(from, PocketRadius(cord), delta / distance, distance, out int count);
        for (int i = 0; i < count; i++)
            if (TerrainSolid(results[i].collider)) travel = Mathf.Min(travel, Mathf.Max(0f, results[i].distance - 0.03f));
        to = from + delta / distance * travel;
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel / 0.35f), 1, 32);
        bool armsWereClear = ArmsClear(cord, from);
        bool Ok(Vector3 p) => PocketClear(cord, p) && (!armsWereClear || ArmsClear(cord, p));
        Vector3 safe = from;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 candidate = Vector3.Lerp(from, to, i / (float)steps);
            if (!Ok(candidate))
            {
                for (int refine = 0; refine < 5; refine++)
                {
                    Vector3 half = (safe + candidate) * 0.5f;
                    if (Ok(half)) safe = half; else candidate = half;
                }
                break;
            }
            safe = candidate;
        }
        return safe;
    }

    private void FixedUpdate()
    {
        if (BoneAwakeningCutscene.Playing) return;
        for (int i = cords.Count - 1; i >= 0; i--)
        {
            if (cords[i].creature == null || cords[i].creature.IsDead || cords[i].creature.IsCarried) { RemoveCord(i); continue; }
            // An arm whose hold is gone (what it held was destroyed): it can no longer hang between the two.
            if ((cords[i].armA >= 0 && !cords[i].creature.ArmPin(cords[i].armA, out _))
                || (cords[i].armB >= 0 && !cords[i].creature.ArmPin(cords[i].armB, out _))) { RemoveCord(i); continue; }
            if (cords[i].creature.SimulationEnabled) Spring(cords[i], Time.fixedDeltaTime);
        }
        if (BoneRide.Busy) return;
        if (automaticReel && Time.time > automaticReelUntil) automaticReel = reeling = false;
        if (reeling && pinnedArms.Count > 0) ReelIn(Time.fixedDeltaTime);
        else PullBack();
    }

    /// <summary>
    /// Holding the reel key: the newest stuck arm shortens, hauling the player along it towards where it stuck,
    /// gravity held off while it pulls. Close to the end the player hangs there until the hands take the wall.
    /// </summary>
    private void ReelIn(float deltaTime)
    {
        if (carrier == null) return;
        if (!(carrier.CarriedCreature is TentacleCreature creature) || creature != lastCarried || pinnedArms.Count == 0) return;
        int arm = pinnedArms[pinnedArms.Count - 1];
        if (balloons.TryGetValue(arm, out LeashBubble bubble))
        {
            if (bubble != null) bubble.ShortenLeash(reelSpeed * 0.45f * deltaTime);
            reeledArm = -1;
            return; // A balloon moves towards the player; it never reels the player into the air.
        }
        if (playerBody == null || playerBody.isKinematic) return;
        if (!creature.ArmPin(arm, out Vector3 pin)) return;
        if (reeledArm != arm)
        {
            // Hauled to just in front of where it stuck, the hands about level with it: close enough to grab.
            reeledArm = arm;
            Vector3 flat = Vector3.ProjectOnPlane(pin - playerBody.position, Vector3.up);
            reelApproach = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }
        ReelSteps++;
        Vector3 goal = pin - reelApproach * 0.6f + Vector3.down * 0.5f;
        Vector3 to = goal - playerBody.position;
        float distance = to.magnitude;
        Vector3 want = distance > 0.15f ? to / distance * Mathf.Min(reelSpeed, distance * 4f) : Vector3.zero;
        Vector3 velocity = Vector3.MoveTowards(playerBody.linearVelocity, want, reelAcceleration * deltaTime);
        var controller = playerBody.GetComponent<SmoothFirstPersonController>();
        // Fling lifts the walking limits and the heavier fall for a moment, so the pull is not fought.
        if (controller != null) controller.Fling(velocity, 0.15f); else playerBody.linearVelocity = velocity;
        playerBody.AddForce(-Physics.gravity, ForceMode.Acceleration);
        restLengths[arm] = Mathf.Min(restLengths.TryGetValue(arm, out float r) ? r : distance, distance);
    }

    /// <summary>
    /// Stuck arms on a carried creature are springs: walk further from where one stuck than it was shot and it
    /// pulls back, harder the further you go, until walking away is a struggle.
    /// </summary>
    private void PullBack()
    {
        if (playerBody == null || playerBody.isKinematic || carrier == null) return;
        if (!(carrier.CarriedCreature is TentacleCreature creature) || creature != lastCarried) return;
        if (!creature.SimulationEnabled) return;
        Vector3 pull = Vector3.zero;
        foreach (int arm in pinnedArms)
        {
            if (balloons.ContainsKey(arm)) continue;
            if (!creature.ArmPin(arm, out Vector3 pin) || !restLengths.TryGetValue(arm, out float rest)) continue;
            Vector3 toPin = pin - playerBody.position;
            float stretch = toPin.magnitude - rest;
            if (stretch <= 0f) continue;
            float outwardSpeed = -Vector3.Dot(playerBody.linearVelocity, toPin.normalized);
            pull += toPin.normalized * Mathf.Clamp(stretch * armPull + outwardSpeed * armDamping, 0f, maxArmPull);
        }
        playerBody.AddForce(Vector3.ClampMagnitude(pull, maxArmPull), ForceMode.Acceleration);
    }

    /// <summary>
    /// The cord only moves when the player pulls it: hold the left mouse button on the hung creature and walk
    /// back, and the middle of the cord comes with the hand. Let go and it snaps back through the line, flinging
    /// the player (and anything loose near the pocket) out the other side. Nothing is pulled in by itself.
    /// </summary>
    private void Spring(Cord cord, float deltaTime)
    {
        if (cord.creature != null)
        {
            if (cord.creature.ArmPin(cord.armA, out Vector3 a)) cord.a = a;
            if (cord.creature.ArmPin(cord.armB, out Vector3 b)) cord.b = b;
        }
        Vector3 mid = (cord.a + cord.b) * 0.5f;
        Vector3 restingPocket = cord.rest == default ? mid + Vector3.down * 0.4f : cord.rest;
        Vector3 rest = restingPocket;
        bool held = cord.creature != null && cord.creature.IsPetted && cord.creature.Dragged;
        if (held)
        {
            rest = restingPocket + Vector3.ClampMagnitude(cord.creature.PetPoint - restingPocket, maxPull);
            // The arms being drawn back pass round the player who pulls them instead of shoving them.
            if (!cord.held && playerBody != null) Pass(cord, playerBody, true);
            cord.held = true;
            cord.passUntil = float.MaxValue;
        }
        else if (cord.held)
        {
            // Let go: snap back and shoot.
            cord.held = false;
            cord.passUntil = Time.time + 0.6f;
            cord.wobble = 1f;
            Vector3 drawn = cord.pocket - restingPocket;
            Vector3 along = (cord.b - cord.a).normalized;
            Vector3 back = Vector3.ProjectOnPlane(drawn, along);
            float stretch = back.magnitude;
            if (stretch > 0.5f)
            {
                Vector3 dir = -back / stretch;
                float speed = Mathf.Clamp(stretch * flingPerMetre, 5f, maxFling);
                Vector3 shot = dir * speed + Vector3.up * (speed * 0.35f);
                int count = Physics.OverlapSphereNonAlloc(cord.pocket, Mathf.Max(2.5f, stretch * 0.6f), hits, ~0, QueryTriggerInteraction.Ignore);
                var done = new HashSet<Rigidbody>();
                for (int i = 0; i < count; i++)
                {
                    Rigidbody body = hits[i].attachedRigidbody;
                    if (body == null || body.isKinematic || !done.Add(body)) continue;
                    if (cord.creature != null && body.transform.IsChildOf(cord.creature.transform)) continue;
                    var controller = body.GetComponent<SmoothFirstPersonController>();
                    if (controller != null) controller.Fling(shot, 1.2f); else body.linearVelocity = shot;
                    Pass(cord, body, true);
                }
                // The player who pulled it is shot even if the hand was a little way off the pocket.
                if (playerBody != null && !done.Contains(playerBody) && Vector3.Distance(playerBody.position, cord.pocket) < stretch + 3f)
                {
                    var controller = playerBody.GetComponent<SmoothFirstPersonController>();
                    if (controller != null) controller.Fling(shot, 1.2f);
                    Pass(cord, playerBody, true);
                }
                // Shot things fly through the cord, then it is solid again.
                cord.passUntil = Time.time + 1f;
                Flung++;
            }
        }
        Vector3 desired = Vector3.Lerp(cord.pocket, rest, 1f - Mathf.Exp(-(held ? 10f : 22f) * deltaTime));
        cord.pocket = SafePocket(cord, cord.pocket, desired);
        if (cord.passing.Count > 0 && Time.time > cord.passUntil)
            foreach (Rigidbody body in new List<Rigidbody>(cord.passing))
                if (!InsideStrip(cord, body)) Pass(cord, body, false);
    }

    private static bool InsideStrip(Cord cord, Rigidbody body)
    {
        if (body == null) return false;
        foreach (Collider collider in body.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            if (StripPenetrates(collider, cord.solid) || StripPenetrates(collider, cord.solidB)) return true;
        }
        return false;
    }

    private static bool StripPenetrates(Collider collider, BoxCollider strip) => strip != null && strip.enabled
        && PlayerCreatureCarrier.CollidersPenetrate(collider, strip);

    private static void Pass(Cord cord, Rigidbody body, bool through)
    {
        if (body != null)
            foreach (Collider c in body.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(c, cord.solid, through);
                Physics.IgnoreCollision(c, cord.solidB, through);
            }
        if (through) cord.passing.Add(body); else cord.passing.Remove(body);
    }

    private static Vector3 ClosestOnLine(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
        return a + ab * t;
    }

    private void LateUpdate()
    {
        foreach (Cord cord in cords)
        {
            cord.age += Time.deltaTime;
            cord.wobble = Mathf.MoveTowards(cord.wobble, 0f, Time.deltaTime * 0.8f);
            if (cord.creature == null) continue;
            // The creature's body is the middle of the cord: pulled to where the cord is bent to, twanging after a shot.
            Vector3 side = Vector3.Cross((cord.b - cord.a).normalized, Vector3.up);
            Vector3 at = cord.pocket + side * (Mathf.Sin(Time.time * 18f) * 0.4f * cord.wobble);
            Transform body = cord.creature.transform;
            body.position = SafePocket(cord, body.position,
                Vector3.Lerp(body.position, at, 1f - Mathf.Exp(-12f * Time.deltaTime)));
            var rb = cord.creature.GetComponent<Rigidbody>();
            if (rb != null) rb.position = body.position;
            FitStrips(cord);
        }
    }

    /// <summary>While hung, the creature's own collider does not block things pushed into the cord.</summary>
    private void SetCreatureSolid(Cord cord, bool solid)
    {
        if (cord.creature == null || playerBody == null) return;
        foreach (Collider mine in cord.creature.GetComponentsInChildren<Collider>())
            foreach (Collider theirs in playerBody.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(mine, theirs, !solid);
    }

    private static void Tube(Mesh mesh, List<Vector3> points, float radius)
    {
        const int sides = 6;
        var vertices = new List<Vector3>(points.Count * sides);
        var uvs = new List<Vector2>(points.Count * sides);
        var triangles = new List<int>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 tangent = points[Mathf.Min(points.Count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
            tangent.Normalize();
            Vector3 u = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(tangent, u);
            float t = i / (float)Mathf.Max(1, points.Count - 1);
            // Bulging suckers along the arm.
            float r = radius * (1f + 0.35f * Mathf.Max(0f, Mathf.Sin(t * 40f)) + 0.6f * Mathf.Pow(Mathf.Abs(2f * t - 1f), 6f));
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.PI * 2f / sides;
                vertices.Add(points[i] + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * r);
                uvs.Add(new Vector2(t, k / (float)sides));
            }
        }
        for (int i = 0; i < points.Count - 1; i++)
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            int a0 = i * sides + k, a1 = i * sides + k1, b0 = (i + 1) * sides + k, b1 = (i + 1) * sides + k1;
            triangles.Add(a0); triangles.Add(b1); triangles.Add(b0);
            triangles.Add(a0); triangles.Add(a1); triangles.Add(b1);
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private MaterialPropertyBlock skinBlock;

    private TentacleCreature Creature => carrier != null ? carrier.CarriedCreature as TentacleCreature : null;

    private Material Skin() => Creature != null && Creature.MembraneMaterial != null ? Creature.MembraneMaterial : Fallback();
    private Material Joint() => Creature != null && Creature.JointMaterial != null ? Creature.JointMaterial : Fallback();
    private Material Tip() => Creature != null && Creature.TipMaterial != null ? Creature.TipMaterial : Fallback();

    private Material Fallback()
    {
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Tentacle Cord (runtime)" };
        material.SetColor("_BaseColor", cordColor);
        return material;
    }

    private static Transform Part(PrimitiveType type, Transform parent, Material material, float size)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private void OnDisable() => ReelAll();

    private GUIStyle problemStyle;

    private void OnGUI()
    {
        string message = Problem;
        if (message == null || carrier == null || !carrier.CarriesTentacle) return;
        if (problemStyle == null)
        {
            problemStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
            problemStyle.normal.textColor = new Color(1f, 0.72f, 0.7f, 0.95f);
        }
        var rect = new Rect(0f, Screen.height * 0.5f + 34f, Screen.width, 26f);
        var shadow = new GUIStyle(problemStyle);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), message, shadow);
        GUI.Label(rect, message, problemStyle);
    }
}

/// <summary>The broad bridge is easy to walk on, but climbing palms grip the visible arm itself.</summary>
public sealed class TentacleGripSurface : MonoBehaviour
{
    private TentacleCreature creature;
    private Vector3 pin;
    public void Setup(TentacleCreature owner, Vector3 tip) { creature = owner; pin = tip; }

    public float Capture(Vector3 point)
    {
        float best = float.PositiveInfinity, parameter = 0f;
        for (int i = 0; i <= 64; i++)
        {
            float t = i / 64f;
            if (creature == null || !creature.SamplePinnedGrip(pin, t, out Vector3 at, out _)) break;
            float distance = (at - point).sqrMagnitude;
            if (distance < best) { best = distance; parameter = t; }
        }
        return parameter;
    }

    public bool Resolve(float parameter, Vector3 normal, out Vector3 point)
    {
        point = default;
        if (creature == null || !creature.SamplePinnedGrip(pin, parameter, out Vector3 centre, out float radius)) return false;
        point = centre + normal.normalized * radius;
        return true;
    }
}
