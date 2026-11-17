using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The conductor baton: a hand-held tool in the hotbar's extra slot (BatonItem + BodyMounts.SetTool), never dropped.
/// Every glow creature has a note (CaveNote: E, G, B, D; its colour is the note's colour). While the baton is in the hand:
///   RMB       - choose the next note; the tip takes the note's colour and the creatures of that note within reach
///               glow a little brighter.
///   hold LMB  - play the note (it sounds on every beat): every creature of that note within reach moves towards the
///               rock under the crosshair, each its own way (Glims fly, snails creep over walls, the Maw crawls, the
///               Tone Mesas slide); every other creature in reach clashes with the note and is muted while it sounds
///               (silent, dim, still, unheard by the others). Release to stop.
/// A thin vibrating thread runs from the tip to the crosshair point while playing.
/// It starts in the fist of the skeleton in the deepest chamber (BatonSkeleton), or in the player's hand
/// (DarkTunnels.startWithBaton).
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class ConductorBaton : MonoBehaviour
{
    [Tooltip("Creatures further than this do not hear the baton.")]
    [SerializeField] private float maxRange = 45f;
    [SerializeField] private float length = 0.42f;

    public static ConductorBaton Instance { get; private set; }
    /// <summary>The baton is the thing in the player's hand.</summary>
    public bool Held => item != null && carrier != null && carrier.CarriedCreature == item;
    public bool HandOpen => skeleton != null && skeleton.HandOpen;
    /// <summary>Lying free, or in a fist that is open.</summary>
    public bool CanBeTaken => !InFist || HandOpen;
    public bool InFist => skeleton != null && transform.parent == skeleton.Grip;
    public bool Playing => playing;
    /// <summary>The note the baton plays now (RMB changes it).</summary>
    public CaveNote CurrentNote => note;
    public BatonItem Item => item;
    /// <summary>Kept in the extra hotbar slot: never put down or thrown.</summary>
    public bool IsTool { get; private set; }

    private BatonSkeleton skeleton;
    private BatonItem item;
    private Rigidbody body;
    private Transform pose, tip;
    private Material tipMaterial, haloMaterial;
    private GameObject tipHalo;
    private LineRenderer thread;
    private readonly Vector3[] threadPoints = new Vector3[24];
    private CaveNote note;
    private bool playing;
    private Vector3 guidePoint;
    private readonly System.Collections.Generic.List<CaveGlowCreature> silenced = new System.Collections.Generic.List<CaveGlowCreature>();
    private float flick, takeFlash, raise;
    private PlayerCreatureCarrier carrier;
    private Camera cam;
    private Vector3? lastRest;

    /// <summary>Builds the baton. With a skeleton it is put in its fist; without, it lies where it is.</summary>
    public void Build(BatonSkeleton holder)
    {
        Instance = this;
        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 1;
        col.center = new Vector3(0f, length * 0.45f, 0f);
        col.height = length;
        col.radius = 0.045f;
        body.mass = 0.4f;
        body.linearDamping = 0.2f;
        body.angularDamping = 0.6f;
        // Held by its handle: the grip sits on the hand, the tip points ahead and a little up, towards the crosshair.
        var hold = GetComponent<HoldGrip>();
        if (hold == null) hold = gameObject.AddComponent<HoldGrip>();
        hold.gripPoint = new Vector3(0f, 0.06f, 0f);
        hold.heldEuler = new Vector3(74f, 10f, 0f);
        hold.followPitch = true;
        hold.handNudge = new Vector3(0.03f, -0.03f, 0.1f);
        item = GetComponent<BatonItem>();
        if (item == null) item = gameObject.AddComponent<BatonItem>();
        item.Baton = this;

        Shader glowShader = Shader.Find("Capstone/Cave/Glow");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var grip = new Material(lit) { name = "Baton grip" };
        grip.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.03f));
        grip.SetFloat("_Smoothness", 0.6f);
        // Pale ivory shaft with a faint light of its own, so it reads in the hand even in the dark tunnels.
        var shaft = new Material(lit) { name = "Baton shaft" };
        shaft.SetColor("_BaseColor", new Color(0.86f, 0.83f, 0.76f));
        shaft.SetFloat("_Smoothness", 0.5f);
        shaft.EnableKeyword("_EMISSION");
        shaft.SetColor("_EmissionColor", new Color(0.12f, 0.115f, 0.1f));
        tipMaterial = new Material(glowShader) { name = "Baton tip" };
        tipMaterial.SetColor("_Color", new Color(1f, 0.95f, 0.8f) * 2f);

        pose = new GameObject("Pose").transform;
        pose.SetParent(transform, false);
        Part(PrimitiveType.Cylinder, pose, new Vector3(0f, 0.06f, 0f), new Vector3(0.034f, 0.06f, 0.034f), grip);      // grip
        Part(PrimitiveType.Sphere, pose, new Vector3(0f, 0.125f, 0f), new Vector3(0.05f, 0.022f, 0.05f), grip);        // guard
        Part(PrimitiveType.Cylinder, pose, new Vector3(0f, 0.125f + length * 0.37f, 0f), new Vector3(0.008f, length * 0.37f, 0.008f), shaft);
        tip = Part(PrimitiveType.Sphere, pose, new Vector3(0f, 0.125f + length * 0.74f, 0f), Vector3.one * 0.018f, tipMaterial).transform;

        // A small crisp pool of light round the tip while it is not in a hand: the one thing to find in the dark chamber.
        Shader haloShader = Shader.Find("Capstone/Cave/LightVolume");
        if (haloShader != null)
        {
            var ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ballObject.GetComponent<Collider>());
            ballObject.name = "Tip light";
            ballObject.transform.SetParent(tip, false);
            // The tip is 2.4 cm wide; the light reaches about 0.7 m.
            ballObject.transform.localScale = Vector3.one * (1.4f / 0.018f);
            haloMaterial = new Material(haloShader) { name = "Baton light" };
            haloMaterial.SetColor("_Color", new Color(1f, 0.9f, 0.7f));
            var hr = ballObject.GetComponent<MeshRenderer>();
            hr.sharedMaterial = haloMaterial;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tipHalo = ballObject;
        }

        // The tip uncovers the rock right round it from the dark (a small reveal light), whether lying or in the hand.
        if (CaveGlowCreature.LightStyle == CaveGlowCreature.LightLook.Reveal)
        {
            if (tipHalo != null) { Destroy(tipHalo); tipHalo = null; }
            CaveReveal.Add(tip, 2.5f, new Color(1f, 0.92f, 0.75f), () => 1f);
        }

        Shader lineShader = Shader.Find("Capstone/Cave/GlowLine");
        var lineMat = new Material(lineShader != null ? lineShader : Shader.Find("Sprites/Default")) { name = "Baton thread" };
        var go = new GameObject("Sounding thread");
        go.transform.SetParent(transform, false);
        thread = go.AddComponent<LineRenderer>();
        thread.sharedMaterial = lineMat;
        thread.positionCount = threadPoints.Length;
        thread.widthMultiplier = 0.012f;
        thread.useWorldSpace = true;
        thread.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        thread.enabled = false;

        skeleton = holder;
        if (holder != null)
        {
            holder.Baton = this;
            transform.SetParent(holder.Grip, false);
            transform.localPosition = new Vector3(0f, -0.3f, 0f);
            transform.localRotation = Quaternion.identity;
        }
    }

    /// <summary>Gives the skeleton whose fist this baton belongs to, without putting it there (baton already held).</summary>
    public void SetSkeleton(BatonSkeleton holder)
    {
        skeleton = holder;
        if (holder != null) holder.Baton = this;
    }

    public float PlayerDistance(Vector3 point)
    {
        Find();
        Vector3 player = carrier != null && carrier.PlayerBody != null ? carrier.PlayerBody.position : cam != null ? cam.transform.position : point + Vector3.one * 999f;
        return Vector3.Distance(point, player);
    }

    /// <summary>
    /// Taken by the player (from the fist, or at the start): it becomes the tool of the extra hotbar slot (BodyMounts),
    /// which can never be dropped, and that slot is selected. Without BodyMounts it is simply carried.
    /// </summary>
    public bool Take(bool quiet = false)
    {
        Find();
        if (Held) return true;
        if (carrier == null || item.IsCarried) return false;
        if (InFist) transform.SetParent(null, true);
        // The tip light must not count as part of the baton when the hand measures it.
        if (tipHalo != null) tipHalo.SetActive(false);
        var slots = carrier.GetComponent<BodyMounts>();
        if (slots == null) slots = FindFirstObjectByType<BodyMounts>();
        if (slots != null)
        {
            IsTool = true;
            body.isKinematic = true;
            slots.SetTool(item, true);
            if (!Held) return false;
        }
        else if (carrier.CarriedCreature != null || !carrier.TryCarryFromInteraction(item)) return false;
        takeFlash = 1f;
        if (!quiet) AudioManager.PostEvent("Play_PickUp");
        return true;
    }

    private void Find()
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (cam == null) cam = carrier != null && carrier.CarryCamera != null ? carrier.CarryCamera : Camera.main;
    }

    private void Update()
    {
        takeFlash = Mathf.MoveTowards(takeFlash, 0f, Time.deltaTime * 0.8f);
        flick = Mathf.MoveTowards(flick, 0f, Time.deltaTime * 4f);
        Find();
        // Lying free (not in the fist, not carried or stowed): it falls and rests like any small thing.
        if (!InFist && !item.IsCarried && body.isKinematic && !IsTool)
        {
            // Continuous collision: a thin stick dropped or thrown must not slip through the terrain.
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        if (!body.isKinematic)
        {
            // Safety net: if it still falls away (a gap in the rock), it comes back to where it last lay.
            if (body.linearVelocity.sqrMagnitude < 0.04f) lastRest = body.position;
            else if (lastRest.HasValue && (body.linearVelocity.y < -30f || body.position.y < lastRest.Value.y - 25f))
            {
                body.position = lastRest.Value + Vector3.up * 0.4f;
                body.linearVelocity = Vector3.zero;
            }
        }
        if (tipHalo != null) tipHalo.SetActive(!item.IsCarried);
        foreach (CaveGlowCreature c in CaveGlowCreature.All) c.Aimed = false;
        if (!Held)
        {
            if (playing) StopPlaying();
            if (thread != null) thread.enabled = false;
            tipMaterial.SetFloat("_Glow", item.IsCarried ? 0.4f : HandOpen || !InFist ? 1f : 0.35f);
            if (haloMaterial != null) haloMaterial.SetFloat("_Glow", (HandOpen || !InFist ? 0.9f : 0.3f) * (0.8f + 0.2f * Mathf.Sin(Time.time * Mathf.PI * 2f)));
            return;
        }
        if (cam == null) return;

        bool blocked = MeditationController.BlocksPlayerInput || RestSpotSystem.BlocksInteraction || !Application.isFocused && !Application.isBatchMode;
        Mouse mouse = Mouse.current;
        bool hold = debugUntil > 0f || mouse != null && mouse.leftButton.isPressed && !blocked;

        // The creatures of the chosen note glow a little brighter, so you see which ones the note will move.
        foreach (CaveGlowCreature c in CaveGlowCreature.All)
            if (c.Note == note && InReach(c)) c.Aimed = true;

        if (hold && !playing) StartPlaying();
        else if (!hold && playing) StopPlaying();
        if (playing)
        {
            Vector3 target = debugUntil > 0f ? debugPoint : CrosshairPoint();
            guidePoint = target;
            foreach (CaveGlowCreature c in CaveGlowCreature.All)
            {
                if (c.Hidden || c.IsCarried || !InReach(c)) continue;
                if (c.Note == note)
                {
                    // The note's creatures go to the crosshair, each its own way.
                    if (!c.Soloed) c.SetSolo(true);
                    c.SetGuide(c is GlimLantern ? target + Vector3.up * 1.5f : target);
                }
                else if (!c.Muted)
                {
                    // The others clash with the note: they fall silent and still while it sounds.
                    c.SetMuted(true);
                    silenced.Add(c);
                }
            }
            // The note sounds on every beat while held.
            AudioManager.PostEventOnGrid("Play_Crate_" + note, gameObject, 1);
            if (debugUntil > 0f && Time.time > debugUntil) { debugUntil = -1f; StopPlaying(); }
        }
        DrawThread();
    }

    /// <summary>RMB while the baton is held: the next note (E, G, B, D). Never puts the baton down.</summary>
    public bool HandleRightMouse(bool pressed, bool held, bool released)
    {
        if (!pressed) return true;
        note = (CaveNote)(((int)note + 1) % 4);
        flick = 1f;
        // A soft taste of the new note, on the beat.
        AudioManager.PostEventOnGrid("Play_Crate_" + note, gameObject, 2);
        return true;
    }

    private void LateUpdate()
    {
        if (pose == null) return;
        // The hand pose itself comes from the HoldGrip; here only the wrist: raised while playing, swaying on the
        // beat (120 BPM = 2 beats a second), a flick when the note changes.
        float want = Held && playing ? 1f : 0f;
        raise = Mathf.MoveTowards(raise, want, Time.deltaTime * 3f);
        float beat = Held && playing ? Mathf.Sin(Time.time * Mathf.PI * 2f) * 6f : 0f;
        Quaternion wrist = Quaternion.Euler(-raise * 18f + beat - flick * 25f, 0f, 0f);
        wristNow = wrist;
        if (!PlaceInHand())
        {
            pose.localPosition = Vector3.zero;
            pose.localRotation = Quaternion.Slerp(pose.localRotation, wrist, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        // The tip shows the chosen note's colour.
        Color c = CaveGlowCreature.NoteColor(note) * (playing ? 2.6f : 1.6f);
        tipMaterial.SetColor("_Color", c * (1f + takeFlash * 2f + flick));
        if (Held) tipMaterial.SetFloat("_Glow", 1f);
    }

    private Quaternion wristNow = Quaternion.identity;

    private void OnEnable() => FirstPersonBody.LimbsPosed += OnLimbsPosed;
    private void OnLimbsPosed() => PlaceInHand();

    /// <summary>
    /// Draws the baton in the visible right hand, right after the body has posed its arm, so looking up or down never
    /// pulls them apart: the grip sits in the palm, the rod turns with the view (the HoldGrip's rotation) and the wrist.
    /// </summary>
    private bool PlaceInHand()
    {
        Transform hand = carrier != null ? carrier.HandAnchor : null;
        if (!Held || hand == null || cam == null || pose == null) return false;
        var grip = GetComponent<HoldGrip>();
        Quaternion inHand = cam.transform.rotation * Quaternion.Euler(grip != null ? grip.heldEuler : new Vector3(74f, 10f, 0f)) * wristNow;
        Vector3 gripLocal = grip != null ? grip.gripPoint : new Vector3(0f, 0.06f, 0f);
        pose.rotation = inHand;
        pose.position = hand.position + cam.transform.forward * 0.02f - inHand * gripLocal;
        return true;
    }

    private void StartPlaying()
    {
        playing = true;
        silenced.Clear();
    }

    private void StopPlaying()
    {
        playing = false;
        foreach (CaveGlowCreature c in CaveGlowCreature.All)
            if (c.Soloed) c.SetSolo(false);
        foreach (CaveGlowCreature c in silenced)
            if (c != null) c.SetMuted(false);
        silenced.Clear();
        debugUntil = -1f;
    }

    private void OnDisable()
    {
        FirstPersonBody.LimbsPosed -= OnLimbsPosed;
        if (playing) StopPlaying();
        foreach (CaveGlowCreature c in CaveGlowCreature.All) c.Aimed = false;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private bool InReach(CaveGlowCreature c) => cam != null && Vector3.Distance(cam.transform.position, c.AimPoint) <= maxRange;

    /// <summary>Where the crosshair meets the rock (or a point far ahead in the open).</summary>
    private Vector3 CrosshairPoint()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore)) return hit.point;
        return ray.GetPoint(20f);
    }

    private void DrawThread()
    {
        thread.enabled = playing;
        if (!playing) return;
        Vector3 a = tip.position, b = guidePoint;
        Vector3 side = Vector3.Cross((b - a).normalized, cam.transform.forward);
        if (side.sqrMagnitude < 1e-4f) side = cam.transform.right;
        side.Normalize();
        float dist = Vector3.Distance(a, b);
        for (int i = 0; i < threadPoints.Length; i++)
        {
            float t = i / (threadPoints.Length - 1f);
            // A standing wave, like a plucked string, pulsing on the beat.
            float amp = Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.35f, dist * 0.02f) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * 2f)));
            threadPoints[i] = Vector3.Lerp(a, b, t) + side * Mathf.Sin(t * Mathf.PI * 3f + Time.time * 9f) * amp;
        }
        thread.SetPositions(threadPoints);
        Color c = CaveGlowCreature.NoteColor(note);
        thread.startColor = new Color(1f, 0.95f, 0.85f);
        thread.endColor = c;
    }

    private static GameObject Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // ---------------- Test hooks (Editor/CaveBatonCheck) ----------------

    public void DebugGive() { if (!Held) Take(); }

    /// <summary>Plays a note at a point for some seconds, as a held LMB with the crosshair there would.</summary>
    public void DebugConduct(CaveNote played, Vector3 point, float seconds)
    {
        Find();
        note = played;
        debugPoint = point;
        debugUntil = Time.time + seconds;
    }

    private Vector3 debugPoint;
    private float debugUntil = -1f;
}
