using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Creates resting creatures at authored and procedural world locations and manages seating.</summary>
[DefaultExecutionOrder(-50)] // before the controller and the climber read the keys
public sealed class RestSpotSystem : MonoBehaviour
{
    public static readonly string[] Kinds = { "Seat", "Ledge", "Lookout", "Hollow", "Landing", "Crown", "Hearth" };

    [Serializable]
    public sealed class SpotData
    {
        public string kind = "Seat";
        public Vector3 position;
        public float yaw;
    }

    [Serializable]
    public sealed class ViewData
    {
        public Vector3 position;
        public Quaternion rotation;
        // The resting place this view belongs to (its ground point).
        public Vector3 spot;
    }

    [Serializable]
    private sealed class FileData
    {
        public List<SpotData> spots = new List<SpotData>();
        public List<ViewData> views = new List<ViewData>();
    }

    private const string ResourceName = "RestSpots";
    private static string EditorFile => Path.Combine(Application.dataPath, "Resources", ResourceName + ".json");

    [Tooltip("F7 gives the view to the nearest resting place within this distance (metres).")]
    [SerializeField] private float viewRange = 60f;

    private readonly List<RestSpot> spots = new List<RestSpot>();
    private FileData recorded = new FileData();
    private int recordKind;
    private int consumedFrame = -1;
    private RestSpot worldSeat;
    private RestSpot sittingAt;
    private bool landmarkSpotsAdded, spawnSeatAdded;
    private string toast;
    private float toastUntil;
    private SmoothFirstPersonController controller;
    private Rigidbody body;
    private Camera view;
    private Transform cameraHolder;
    private MeditationController meditation;
    private bool turning;
    private ViewData scenic;
    private bool wasKinematic, seatWasKinematic;
    private Material stone, ink, glow;

    public static RestSpotSystem Instance { get; private set; }
    public IReadOnlyList<RestSpot> Spots => spots;
    public bool Sitting => sittingAt != null;
    internal RestSpot SeatedAt => sittingAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneSetup()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != null) return;
        var world = FindFirstObjectByType<ProceduralWorld>();
        if (world != null && world.gameObject.scene == scene) Create(scene);
    }

    internal static void EnsureFor(MeditationController controller)
    {
        if (Instance == null) Create(controller.gameObject.scene);
        Instance.meditation = controller;
    }

    private static void Create(Scene scene)
    {
        var system = new GameObject("Rest Spots");
        SceneManager.MoveGameObjectToScene(system, scene);
        system.AddComponent<RestSpotSystem>();
    }

    private void Awake()
    {
        Instance = this;
        Load();
        stone = MakeRestMaterial(new Color(0.88f, 0.87f, 0.84f));
        ink = MakeRestMaterial(new Color(0.06f, 0.06f, 0.07f));
        glow = MakeRestMaterial(new Color(0.55f, 0.95f, 1f));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        foreach (SpotData s in recorded.spots) AddSpot(s.kind, s.position, s.yaw, snap: false);
    }

    internal static bool BlocksInteraction => Instance != null &&
        (Instance.Sitting || Instance.consumedFrame == Time.frameCount);

    internal void TrySit(RestSpot spot)
    {
        if (!FindPlayer() || Sitting || consumedFrame == Time.frameCount || !controller.Grounded ||
            BoneRide.Busy || !spot.CanSit || spot.gameObject.scene != controller.gameObject.scene) return;
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null && (carrier.CarriedCreature != null || carrier.PendingCreature != null)) return;
        consumedFrame = Time.frameCount;
        Sit(spot);
    }

    internal void BeginVisit()
    {
        worldSeat = sittingAt;
        Stand();
    }

    internal void StandForReturn() => Stand();

    internal void EndVisit()
    {
        if (worldSeat == null) return;
        if (sittingAt != null) Stand();
        Sit(worldSeat);
        worldSeat = null;
    }

    private float nextDefaults, nextMeditationLookup;

    private void Update()
    {
        if (!FindPlayer()) return;
        if (meditation != null && meditation.Transitioning) return;
        if (meditation == null || !meditation.Inside)
        {
            if (Time.time >= nextDefaults && (!spawnSeatAdded || !landmarkSpotsAdded))
            {
                nextDefaults = Time.time + 0.5f;
                AddDefaultSpots();
            }
            if (Keyboard.current != null) Record(Keyboard.current);
        }
        if (sittingAt == null) return;
        controller.SuppressJumpForCurrentFrame();
        if (Mouse.current?.rightButton.wasPressedThisFrame == true ||
            Keyboard.current?.spaceKey.wasPressedThisFrame == true)
        {
            consumedFrame = Time.frameCount;
            Stand();
        }
        else if (consumedFrame != Time.frameCount && Mouse.current?.leftButton.wasPressedThisFrame == true)
        {
            consumedFrame = Time.frameCount;
            if (meditation != null)
            {
                if (meditation.Inside) meditation.RequestReturn();
                else meditation.RequestEntry(sittingAt);
            }
        }
    }

    private void LateUpdate()
    {
        if (!turning || sittingAt == null || scenic == null || controller == null) return;
        bool meditating = meditation != null && meditation.Transitioning;
        Mouse mouse = Mouse.current;
        // Looking round by hand ends the turn.
        if (meditating || (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 4f)) { turning = false; return; }
        Vector3 euler = scenic.rotation.eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, euler.x);
        float k = 1f - Mathf.Exp(-1.2f * Time.deltaTime);
        float yaw = controller.LookYaw + Mathf.DeltaAngle(controller.LookYaw, euler.y) * k;
        controller.SetLook(yaw, Mathf.Lerp(controller.LookPitch, pitch, k));
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, euler.y)) < 0.2f && Mathf.Abs(controller.LookPitch - pitch) < 0.2f) turning = false;
    }

    // ---------------- Sitting ----------------

    private void Sit(RestSpot spot)
    {
        sittingAt = spot;
        var seatBody = spot.GetComponent<Rigidbody>();
        seatWasKinematic = seatBody.isKinematic;
        seatBody.isKinematic = true;
        wasKinematic = body.isKinematic;
        controller.SetRestricted(true);
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        // Lower the body so the eyes are at sitting height.
        body.position = spot.Seat + Vector3.up * 0.35f;
        scenic = spot.Fixed ? null : ViewFor(spot);
        turning = true;
        spot.SetNew(false); // keep the glyph out of the view while seated

    }

    private void Stand()
    {
        if (sittingAt == null) return;
        body.position = sittingAt.Seat + Vector3.up * 1.05f;
        body.isKinematic = wasKinematic;
        controller.SetRestricted(false);
        sittingAt.SetNew(false);
        sittingAt.GetComponent<Rigidbody>().isKinematic = seatWasKinematic;
        sittingAt = null;
        scenic = null;
        turning = false;
    }

    /// <summary>The scenic view of a resting place: the one recorded for it, or else one composed for it.</summary>
    public ViewData ViewFor(RestSpot spot)
    {
        ViewData own = RecordedView(spot);
        if (own != null) return own;
        if (spot.autoView == null) spot.autoView = ComposeView(spot);
        return spot.autoView;
    }

    private ViewData RecordedView(RestSpot spot)
    {
        foreach (ViewData v in recorded.views)
            if (v.spot != Vector3.zero && Vector3.Distance(v.spot, spot.transform.position) < 2f) return v;
        return null;
    }

    /// <summary>
    /// A view composed for a resting place: from behind and above the seat, looking out the way that is most
    /// open, preferring a landmark in sight (the spire's crown, the kiln) and the way the seat faces.
    /// </summary>
    private ViewData ComposeView(RestSpot spot)
    {
        Vector3 eye = spot.Seat + Vector3.up * 1.0f; // seated eye height
        var focus = new List<Vector3>();
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (spire != null && spire.CrownSeat(out Vector3 crown, out _)) focus.Add(crown);
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        if (kiln != null && kiln.Root != null) focus.Add(kiln.HeartPoint);

        float bestScore = float.MinValue;
        Vector3 bestDirection = spot.transform.forward;
        Vector3 bestTarget = Vector3.zero;
        float pitch = PitchDown(eye);
        for (int i = 0; i < 16; i++)
        {
            float turn = i * 22.5f;
            Vector3 direction = Quaternion.Euler(0f, turn, 0f) * spot.transform.forward;
            // How open the whole frame is from the seat: the nearest thing matters most.
            float sum = 0f, nearest = float.MaxValue;
            for (int r = -2; r <= 2; r++)
            {
                Vector3 ray = Quaternion.Euler(0f, r * 13f, 0f) * direction + Vector3.down * pitch;
                float d = OpenDistance(eye, ray, 400f);
                sum += d;
                nearest = Mathf.Min(nearest, d);
            }
            float score = sum / 5f + Mathf.Min(nearest, 60f) * 3f + Mathf.Cos(turn * Mathf.Deg2Rad) * 40f;
            Vector3 target = Vector3.zero;
            foreach (Vector3 f in focus)
            {
                Vector3 to = f - eye;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.magnitude < 25f) continue; // sitting on it
                if (Vector3.Angle(flat, direction) < 25f && !Physics.Linecast(eye, f - to.normalized * 12f, 1 << 7, QueryTriggerInteraction.Ignore))
                {
                    score += 250f;
                    target = f;
                }
            }
            if (score > bestScore) { bestScore = score; bestDirection = direction; bestTarget = target; }
        }

        // A little below the horizon; lifted towards a landmark above.
        Vector3 look = bestDirection + Vector3.down * pitch;
        if (bestTarget != Vector3.zero)
        {
            Vector3 to = bestTarget - eye;
            look = bestDirection + Vector3.up * Mathf.Clamp(to.y / new Vector2(to.x, to.z).magnitude * 0.6f, -0.2f, 0.4f);
        }
        return new ViewData { position = eye, rotation = Quaternion.LookRotation(look.normalized, Vector3.up), spot = spot.transform.position };
    }

    /// <summary>How far the view tips down (as a slope): a little, and high up (the crown, a cliff top)
    /// enough to look down onto the land rather than at the sky.</summary>
    private static float PitchDown(Vector3 eye)
    {
        float aboveGround = Physics.Raycast(eye + Vector3.down * 0.6f, Vector3.down, out RaycastHit ground, 300f, 1 << 7, QueryTriggerInteraction.Ignore) ? ground.distance : 0f;
        return 0.06f + Mathf.Min(aboveGround / 150f, 0.45f);
    }

    private static float OpenDistance(Vector3 from, Vector3 direction, float range)
    {
        return Physics.SphereCast(from, 0.4f, direction.normalized, out RaycastHit hit, range, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.distance : range;
    }

    // ---------------- Spots ----------------

    /// <summary>Adds a resting place (also used by the landmarks' defaults).</summary>
    public RestSpot AddSpot(string kind, Vector3 position, float yaw, bool snap)
    {
        if (snap && Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, 1 << 7, QueryTriggerInteraction.Ignore))
            position = hit.point;
        var go = new GameObject($"Rest Spot ({kind})");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        RestSpot spot = go.AddComponent<RestSpot>();
        spot.Build(kind, stone, ink, glow);
        spot.SetNew(false);
        spots.Add(spot);
        return spot;
    }

    /// <summary>Resting places that come with the landmarks and the spawn, added once they exist.</summary>
    private void AddDefaultSpots()
    {
        if (!spawnSeatAdded)
        {
            ProceduralWorld world = FindFirstObjectByType<ProceduralWorld>();
            HangingSpire spire = FindFirstObjectByType<HangingSpire>();
            if ((world == null || world.LayoutReady) && Physics.Raycast(body.position + Vector3.up, Vector3.down, 6f, 1 << 7))
            {
                spawnSeatAdded = true;
                // A seat a few steps from where the player starts, facing the spire.
                Vector3 toSpire = spire != null ? spire.transform.position - body.position : body.transform.forward;
                toSpire.y = 0f;
                if (toSpire.sqrMagnitude < 0.01f) toSpire = Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, toSpire.normalized);
                AddSpot("Seat", body.position + side * 4f + toSpire.normalized * 2f, Quaternion.LookRotation(toSpire).eulerAngles.y, snap: true);
            }
        }
        if (landmarkSpotsAdded) return;
        HangingSpire tower = FindFirstObjectByType<HangingSpire>();
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        bool towerReady = tower == null || tower.Built;
        bool kilnReady = kiln == null || kiln.Root != null || !kiln.isActiveAndEnabled;
        if (!towerReady || !kilnReady) return;
        landmarkSpotsAdded = true;
        if (tower != null)
        {
            foreach (var landing in tower.Landings)
            {
                Vector3 outward = -landing.inward;
                AddSpot("Landing", landing.top + outward * 0.6f, Quaternion.LookRotation(outward).eulerAngles.y, snap: true);
            }
            if (tower.CrownSeat(out Vector3 crown, out Vector3 crownFacing))
                AddSpot("Crown", crown, Quaternion.LookRotation(crownFacing).eulerAngles.y, snap: true);
        }
        if (kiln != null && kiln.Root != null)
        {
            Vector3 heart = kiln.HeartPoint;
            Vector3 away = heart - kiln.Root.position; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            AddSpot("Hearth", heart + away.normalized * 3.2f, Quaternion.LookRotation(-away).eulerAngles.y, snap: true);
        }
    }

    // ---------------- Recording (play mode) ----------------

    private void Record(Keyboard keyboard)
    {
        if (keyboard.f8Key.wasPressedThisFrame)
        {
            recordKind = (recordKind + 1) % Kinds.Length;
            Say("Recording kind: " + Kinds[recordKind]);
        }
        if (keyboard.f6Key.wasPressedThisFrame)
        {
            Vector3 feet = body.position + Vector3.down * 0.95f;
            float yaw = view.transform.eulerAngles.y;
            var data = new SpotData { kind = Kinds[recordKind], position = feet, yaw = yaw };
            recorded.spots.Add(data);
            AddSpot(data.kind, data.position, data.yaw, snap: true);
            Save();
            Say($"Rest spot recorded: {data.kind}  ({recorded.spots.Count} saved)");
        }
        if (keyboard.f7Key.wasPressedThisFrame)
        {
            // The view belongs to the nearest resting place (replacing the one it had).
            RestSpot owner = null;
            float best = viewRange;
            foreach (RestSpot spot in spots)
            {
                if (spot == null) continue;
                float d = Vector3.Distance(spot.Seat, view.transform.position);
                if (d < best) { best = d; owner = spot; }
            }
            if (sittingAt != null) owner = sittingAt;
            if (owner == null) Say($"No rest spot within {viewRange:0} m: record one with F6 first");
            else
            {
                recorded.views.RemoveAll(v => v.spot == Vector3.zero || Vector3.Distance(v.spot, owner.transform.position) < 2f);
                recorded.views.Add(new ViewData { position = view.transform.position, rotation = view.transform.rotation, spot = owner.transform.position });
                Save();
                Say($"Scenic view set for the {owner.kind}: it will look this way");
            }
        }
    }

    private void Load()
    {
        string json = null;
#if UNITY_EDITOR
        if (File.Exists(EditorFile)) json = File.ReadAllText(EditorFile);
#endif
        if (json == null)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset != null) json = asset.text;
        }
        if (!string.IsNullOrEmpty(json))
        {
            try { recorded = JsonUtility.FromJson<FileData>(json) ?? new FileData(); }
            catch (Exception e) { Debug.LogWarning("Could not read rest spots: " + e.Message); }
        }
    }

    private void Save()
    {
#if UNITY_EDITOR
        Directory.CreateDirectory(Path.GetDirectoryName(EditorFile));
        File.WriteAllText(EditorFile, JsonUtility.ToJson(recorded, true));
        UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/" + ResourceName + ".json");
#else
        Debug.Log("Rest spots can only be recorded in the editor.");
#endif
    }

    /// <summary>True when right click would sit down here or stand up (the mouse icon shows it).</summary>
    public bool RightClickSits => false;

    private PlayerCreatureCarrier carrier;

    // ---------------- Prompt ----------------

    private void Say(string message)
    {
        toast = message;
        toastUntil = Time.time + 2.5f;
    }

    private void OnGUI()
    {
        bool meditating = meditation != null && meditation.Transitioning;
        if (meditating) return;
        string line = null;
        if (Time.time < toastUntil) line = toast;
        else if (sittingAt != null && meditation != null) line = meditation.Inside ? "[LMB] Return     [RMB / Space] Stand" : "[LMB] Reflect     [RMB / Space] Stand";
        if (line == null) return;
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        style.normal.textColor = new Color(0.95f, 0.95f, 0.92f, 0.95f);
        var rect = new Rect(0f, Screen.height * 0.72f, Screen.width, 30f);
        var shadow = new GUIStyle(style);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), line, shadow);
        GUI.Label(rect, line, style);
    }

    // ---------------- Helpers ----------------

    private bool FindPlayer()
    {
        if (controller == null)
        {
            controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller == null) return false;
            body = controller.GetComponent<Rigidbody>();
        }
        if (view == null)
        {
            view = Camera.main;
            if (view != null) cameraHolder = view.transform.parent != null ? view.transform.parent : view.transform;
        }
        if (meditation == null && Time.time >= nextMeditationLookup) { nextMeditationLookup = Time.time + 1f; meditation = FindFirstObjectByType<MeditationController>(); }
        return body != null && view != null;
    }

    internal static Material MakeRestMaterial(Color colour)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", colour);
        return m;
    }
}

/// <summary>
/// One resting place: a low stone seat in an etched ring, shaped a little by its kind (a lookout has an empty
/// frame standing before it, a hollow a low hood of stone). A small glyph hovers over it while its kind is new.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class RestSpot : Creature
{
    public string kind;
    [SerializeField, Min(0f)] private float discoveryRadius = 10f;
    [SerializeField] private bool showDiscoveryRange;
    public float DiscoveryRadius => discoveryRadius;
    public bool Fixed { get; private set; }
    public bool CanSit => !IsCarried && !IsDead && (Fixed ||
        (GetComponent<Rigidbody>().linearVelocity.sqrMagnitude < 0.25f &&
        ReflectionPhysics.Raycast(Seat, Vector3.down, 0.8f, 1 << 7, QueryTriggerInteraction.Ignore)));
    public override bool CanBeCarried => !Fixed && !(RestSpotSystem.Instance != null && RestSpotSystem.Instance.Sitting);
    public override bool CanBeConsumed => false;
    public override bool Invulnerable => true;
    public override bool CanUseWhileCarried => true;
    public override string InteractionPrompt => IsCarried ? null : "Sit";
    public override void Die() { }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried) RestSpotSystem.Instance?.TrySit(this);
    }

    public void MakeFixed()
    {
        Fixed = true;
        GetComponent<Rigidbody>().isKinematic = true;
    }

    protected override void InitializeCreature()
    {
        SetCreatureName("Resting Creature");
        Rigidbody body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.mass = 8f;
    }

    private void OnDrawGizmos()
    {
        if (!showDiscoveryRange || Fixed) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, discoveryRadius);
    }

    // The view composed for this place when none was recorded (RestSpotSystem.ViewFor).
    [System.NonSerialized] public RestSpotSystem.ViewData autoView;
    private Transform marker;

    /// <summary>Where the player sits.</summary>
    public Vector3 Seat => transform.position + Vector3.up * 0.45f;

    public void Build(string kindName, Material stone, Material ink, Material glow)
    {
        kind = kindName;
        // The seat: a flat slab on a short block, slightly askew.
        Block(new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.4f, 0.7f), stone, solid: true, tilt: 3f);
        Block(new Vector3(0f, 0.43f, 0f), new Vector3(1.2f, 0.08f, 0.9f), stone, solid: true, tilt: 2f);
        // An etched ring round it on the ground.
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            Block(new Vector3(Mathf.Cos(a), 0.02f, Mathf.Sin(a)) * 1.8f, new Vector3(0.45f, 0.03f, 0.05f), ink, solid: false, yaw: -a * Mathf.Rad2Deg + 90f);
        }
        if (kind == "Lookout")
        {
            // An empty picture frame on two posts, framing the view ahead.
            Vector3 f = new Vector3(0f, 0f, 2.6f);
            Block(f + new Vector3(-0.9f, 1.1f, 0f), new Vector3(0.06f, 2.2f, 0.06f), ink, false);
            Block(f + new Vector3(0.9f, 1.1f, 0f), new Vector3(0.06f, 2.2f, 0.06f), ink, false);
            Block(f + new Vector3(0f, 2.2f, 0f), new Vector3(1.86f, 0.06f, 0.06f), ink, false);
            Block(f + new Vector3(0f, 1.1f, 0f), new Vector3(1.86f, 0.06f, 0.06f), ink, false);
        }
        else if (kind == "Hollow")
        {
            // A low hood of stone over the seat.
            Block(new Vector3(0f, 1.6f, -0.3f), new Vector3(1.8f, 0.15f, 1.2f), stone, true, tilt: -12f);
            Block(new Vector3(-0.85f, 0.8f, -0.4f), new Vector3(0.15f, 1.6f, 0.8f), stone, true);
            Block(new Vector3(0.85f, 0.8f, -0.4f), new Vector3(0.15f, 1.6f, 0.8f), stone, true);
        }
        // The new-kind glyph: a small slowly turning diamond.
        GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(m.GetComponent<Collider>());
        m.name = "New Rest Marker";
        m.transform.SetParent(transform, false);
        m.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        m.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        m.transform.localScale = Vector3.one * 0.18f;
        var r = m.GetComponent<Renderer>();
        r.sharedMaterial = glow;
        r.shadowCastingMode = ShadowCastingMode.Off;
        marker = m.transform;
    }

    public void SetNew(bool isNew)
    {
        if (marker != null) marker.gameObject.SetActive(isNew);
    }

    protected override void TickCreature(float dt)
    {
        if (marker != null && marker.gameObject.activeSelf)
        {
            marker.localPosition = new Vector3(0f, 1.6f + Mathf.Sin(Time.time * 1.5f) * 0.08f, 0f);
            marker.Rotate(Vector3.up, 40f * Time.deltaTime, Space.World);
        }
    }

    private void Block(Vector3 local, Vector3 size, Material material, bool solid, float tilt = 0f, float yaw = 0f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Rest Stone";
        if (!solid) Destroy(go.GetComponent<Collider>());
        else go.layer = 8;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = solid ? ShadowCastingMode.On : ShadowCastingMode.Off;
    }
}
