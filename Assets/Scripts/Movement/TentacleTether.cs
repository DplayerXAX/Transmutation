using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// With a tentacle creature on the back, the player can shoot its own arms out and stick them to things:
///   - Hold F to aim, release to shoot one of the creature's arms at what the crosshair is on; it stays stretched from the creature to
///     that point. F again shoots a second arm to a second point (a third shot lets the oldest go).
///   - Setting the creature down (right click) with two arms stuck: it hangs between the two points, held by
///     its own two stretched arms. That line is a bridge (walk on it, grip it while climbing) and a slingshot:
///     hold the left mouse button on the creature and walk back to draw the cord, let go and it shoots you
///     (and anything loose at the pocket) out over the line. It never pulls anyone in by itself.
///   - A stuck arm on the carried creature is a spring: walk further than it was shot and it pulls you back.
///   - Picking the creature up again ends the cord. R pulls every shot arm back in.
/// Added to the player by itself; it does nothing unless the carrier holds a tentacle creature.
/// The earlier carried behaviours (arms round the wrists, sticky climbing) are unchanged.
/// </summary>
[DisallowMultipleComponent]
public sealed class TentacleTether : MonoBehaviour
{
    [Tooltip("Also aims (besides holding the left mouse button): hold to aim, release to shoot.")]
    [SerializeField] private Key fireKey = Key.F;
    [Tooltip("Pull every shot arm back in.")]
    [SerializeField] private Key reelKey = Key.R;
    [Tooltip("How hard a stuck arm pulls the player back once they walk further than it was shot (acceleration per metre).")]
    [Min(0f)] [SerializeField] private float armPull = 5f;
    [Min(0f)] [SerializeField] private float maxArmPull = 40f;
    [Min(2f)] [SerializeField] private float range = 28f;
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

    public int CordCount => cords.Count;

    public void ReleaseCreature(TentacleCreature creature)
    {
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == creature) RemoveCord(i);
        if (lastCarried == creature)
        {
            lastCarried = null;
            pinnedArms.Clear();
            restLengths.Clear();
            aiming = aimValid = false;
        }
    }

    /// <summary>The player's tether (for the arm pose while aiming).</summary>
    public static TentacleTether Instance { get; private set; }
    public bool Aiming => aiming;
    public Vector3 AimPoint => aimPoint;

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
        if (carrier == null) return;
        // A hung creature picked up again: the cord is gone and its arms come back.
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == null || cords[i].creature.IsDead || cords[i].creature.IsCarried) RemoveCord(i);

        TentacleCreature carried = carrier.CarriedCreature as TentacleCreature;
        if (carried != null && carried.IsDead) carried = null;
        if (carried == null)
        {
            // Just set down: with two arms stuck it hangs between them as a cord; otherwise its arms come back.
            if (lastCarried != null && !lastCarried.IsDead)
            {
                List<Vector3> pins = lastCarried.PinnedPoints();
                if (pins.Count >= 2) MakeCord(pins[pins.Count - 2], pins[pins.Count - 1], lastCarried);
                else lastCarried.UnpinAll();
            }
            lastCarried = null;
            pinnedArms.Clear();
            return;
        }
        lastCarried = carried;
        if (view == null) view = Camera.main;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Hold F: aim. Release: the arm shoots where the marker was.
        // Hold the left mouse button (or F): aim. Release: the arm shoots where the marker was.
        Mouse mouse = Mouse.current;
        bool pressed = (mouse != null && mouse.leftButton.isPressed && Application.isFocused) || keyboard[fireKey].isPressed;
        bool aimNow = pressed && Aim(out aimPoint, out aimValid);
        ShowAim(aimNow);
        if (!pressed && aiming && aimValid) FireAt(aimPoint);
        aiming = pressed;
        if (keyboard[reelKey].wasPressedThisFrame) ReelAll();

        shotTimer += Time.deltaTime;
    }

    /// <summary>
    /// Fire at a point as if the crosshair were on it (also used by tests). Each shot sends out another of the
    /// carried creature's own arms to stick there; with more than two out, the oldest lets go.
    /// </summary>
    public void FireAt(Vector3 point)
    {
        TentacleCreature creature = carrier != null ? carrier.CarriedCreature as TentacleCreature : null;
        if (creature == null || !creature.SimulationEnabled) return;
        if (pinnedArms.Count >= 2)
        {
            creature.UnpinArm(pinnedArms[0]);
            pinnedArms.RemoveAt(0);
        }
        int arm = creature.PinArm(point);
        if (arm >= 0)
        {
            pinnedArms.Add(arm);
            // It is shot at its full length: walking further than this stretches it.
            restLengths[arm] = playerBody != null ? Vector3.Distance(playerBody.position, point) : 0f;
        }
        shotTimer = 0f;
    }

    private readonly List<int> pinnedArms = new List<int>();
    private readonly Dictionary<int, float> restLengths = new Dictionary<int, float>();

    private bool aiming, aimValid;
    private Vector3 aimPoint;
    private LineRenderer aimLine;
    private Transform aimMarker;

    /// <summary>Where the crosshair is: a point in range on something solid (not the player).</summary>
    private bool Aim(out Vector3 point, out bool valid)
    {
        point = default;
        valid = false;
        if (view == null) return false;
        Ray ray = new Ray(view.transform.position, view.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range, ~(1 << 2), QueryTriggerInteraction.Ignore)
            && (playerBody == null || hit.rigidbody != playerBody))
        {
            point = hit.point + hit.normal * 0.05f;
            valid = true;
        }
        else point = ray.origin + ray.direction * range;
        return true;
    }

    /// <summary>While aiming: a thin line from the hand and a ring on the target (pale when it can stick, dim red when not).</summary>
    private void ShowAim(bool on)
    {
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
        Color colour = aimValid ? new Color(0.85f, 1f, 0.95f) : new Color(0.6f, 0.15f, 0.2f);
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

    /// <summary>Hold F: the carried creature pulls every arm back in.</summary>
    private void ReelAll()
    {
        pinnedArms.Clear();
        restLengths.Clear();
        if (carrier != null && carrier.CarriedCreature is TentacleCreature creature) creature.UnpinAll();
    }

    private void RemoveCord(int index)
    {
        Cord cord = cords[index];
        if (cord.creature != null)
        {
            SetCreatureSolid(cord, true);
            cord.creature.Suspend(false);
            cord.creature.UnpinAll();
        }
        if (cord.root != null) Destroy(cord.root);
        cords.RemoveAt(index);
    }

    // ---------------- Cords ----------------

    private void MakeCord(Vector3 a, Vector3 b, TentacleCreature creature)
    {
        pinnedArms.Clear();
        if ((b - a).magnitude < 0.8f) { creature.UnpinAll(); return; }
        if (cords.Count >= maxCords) RemoveCord(0);
        var cord = new Cord { a = a, b = b, pocket = (a + b) * 0.5f + Vector3.down * 0.4f, creature = creature };
        creature.Suspend(true);
        cord.root = new GameObject("Tentacle Cord") { layer = 7 };
        cord.root.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation((b - a).normalized, Vector3.up));
        // Solid: two flat strips along the two stretched arms, from each pin to the creature's body, so what
        // is walked on is where the arms are seen (a bridge with a dip in the middle), and grippable when climbing.
        cord.solid = Strip(cord.root.transform, "Arm A");
        cord.solidB = Strip(cord.root.transform, "Arm B");
        FitStrips(cord);
        // Nothing else to draw: the creature hangs in the middle and its own two stretched arms are the cord.
        SetCreatureSolid(cord, false);
        cords.Add(cord);
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

    private void FixedUpdate()
    {
        for (int i = cords.Count - 1; i >= 0; i--)
            if (cords[i].creature == null || cords[i].creature.IsDead) RemoveCord(i);
        foreach (Cord cord in cords)
            if (cord.creature.SimulationEnabled) Spring(cord, Time.fixedDeltaTime);
        PullBack();
    }

    /// <summary>
    /// Stuck arms on a carried creature are springs: walk further from where one stuck than it was shot and it
    /// pulls back, harder the further you go, until walking away is a struggle.
    /// </summary>
    private void PullBack()
    {
        if (playerBody == null || playerBody.isKinematic || carrier == null) return;
        if (!(carrier.CarriedCreature is TentacleCreature creature)) return;
        if (!creature.SimulationEnabled) return;
        List<Vector3> pins = creature.PinnedPoints();
        if (pins.Count == 0) return;
        Vector3 pull = Vector3.zero;
        int k = 0;
        foreach (int arm in pinnedArms)
        {
            if (k >= pins.Count) break;
            Vector3 pin = pins[k++];
            float rest = restLengths.TryGetValue(arm, out float r) ? r : 0f;
            Vector3 toPin = pin - playerBody.position;
            float stretch = toPin.magnitude - rest;
            if (stretch <= 0f) continue;
            pull += toPin.normalized * Mathf.Min(stretch * armPull, maxArmPull);
        }
        playerBody.AddForce(pull, ForceMode.Acceleration);
    }

    /// <summary>
    /// The cord only moves when the player pulls it: hold the left mouse button on the hung creature and walk
    /// back, and the middle of the cord comes with the hand. Let go and it snaps back through the line, flinging
    /// the player (and anything loose near the pocket) out the other side. Nothing is pulled in by itself.
    /// </summary>
    private void Spring(Cord cord, float deltaTime)
    {
        Vector3 mid = (cord.a + cord.b) * 0.5f;
        Vector3 rest = mid + Vector3.down * 0.4f;
        bool held = cord.creature != null && cord.creature.IsPetted && cord.creature.Dragged;
        if (held)
        {
            rest = mid + Vector3.ClampMagnitude(cord.creature.PetPoint - mid, maxPull);
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
            Vector3 drawn = cord.pocket - mid;
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
        cord.pocket = Vector3.Lerp(cord.pocket, rest, 1f - Mathf.Exp(-(held ? 10f : 22f) * deltaTime));
        if (cord.passing.Count > 0 && Time.time > cord.passUntil)
            foreach (Rigidbody body in new List<Rigidbody>(cord.passing)) Pass(cord, body, false);
    }

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
            body.position = Vector3.Lerp(body.position, at, 1f - Mathf.Exp(-12f * Time.deltaTime));
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
}
