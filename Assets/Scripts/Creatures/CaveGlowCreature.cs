using System.Collections.Generic;
using UnityEngine;

/// <summary>The four notes the conductor baton plays (E minor: E G B D). Each glow creature answers to one.</summary>
public enum CaveNote { E, G, B, D }

/// <summary>
/// Shared base of the glowing tunnel creatures (Glim Lantern, Lure Maw, Bell Snail). They are the only lights in
/// the dark tunnels, built in code, and they all "sing" on their species' music layer (see AudioEventBank).
///
/// Voice: every one of them can hear the others. A creature's voice carries `voiceReach` metres, 2.5 times as far
/// while the conductor baton plays it alone (solo), and not at all while muted. Species react to the loudest voice
/// they hear, so playing a prey alone can lure its predator.
/// Each one has a note (CaveNote). The conductor baton plays a note: creatures of that note move to the crosshair
/// point, each species its own way; the others are muted while it sounds.
/// Movement is slow lerped following, checked against the terrain so they never pass through rock; when a step is
/// blocked they hold still.
/// </summary>
public abstract class CaveGlowCreature : Creature
{
    [Header("Note")]
    [Tooltip("The baton note this creature answers to (it moves on its note; other notes mute it while they sound).")]
    [SerializeField] protected CaveNote note;
    public CaveNote Note { get => note; set => note = value; }

    private static readonly Color[] NoteColors =
    {
        new Color(0.62f, 0.32f, 1f),   // E violet
        new Color(1f, 0.78f, 0.12f),   // G gold
        new Color(0.15f, 0.95f, 0.8f), // B teal
        new Color(1f, 0.3f, 0.55f),    // D rose
    };
    public static Color NoteColor(CaveNote n) => NoteColors[(int)n];

    public static readonly List<CaveGlowCreature> All = new List<CaveGlowCreature>();

    [Header("Glow")]
    [SerializeField] protected Color glowColor = new Color(0.2f, 1f, 1f);
    [Tooltip("Metres its voice carries at normal volume.")]
    [Min(1f)] [SerializeField] protected float voiceReach = 12f;

    /// <summary>The music layer this species plays on (bank creatureChannels map the type to the same layer).</summary>
    public abstract string MusicLayer { get; }
    public Color GlowColor => glowColor;
    public bool Muted { get; private set; }
    public bool Soloed { get; private set; }
    /// <summary>Set by the baton each frame it points at this creature.</summary>
    public bool Aimed { get; set; }
    public Vector3? Guide { get; private set; }
    /// <summary>Hidden (eaten, waiting to come back): not heard, not seen.</summary>
    public bool Hidden { get; protected set; }
    public float Voice => Muted || Hidden || IsDead ? 0f : Soloed ? 2.5f : 1f;
    public float VoiceReach => voiceReach * Voice * Loudness;
    /// <summary>Extra loudness of the moment (a Tone Crate's thump), 1 = normal.</summary>
    protected virtual float Loudness => 1f;
    public virtual Vector3 AimPoint => transform.position;

    protected readonly List<Material> glowMaterials = new List<Material>();
    protected readonly List<Material> haloMaterials = new List<Material>();
    protected readonly List<float> haloSizes = new List<float>();
    // Halos live outside the creature, so carry size and hold position measure only the body.
    private readonly List<Transform> halos = new List<Transform>();
    private readonly List<Transform> haloAnchors = new List<Transform>();
    protected Light lamp;
    private float lampIntensity;
    protected float shownGlow = 1f;

    private static Shader glowShader, haloShader, inkShader;
    private static Mesh quad;

    /// <summary>Terrain layers the creatures walk on and are blocked by.</summary>
    protected static int TerrainMask => DarkTunnels.TerrainMask;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!All.Contains(this)) All.Add(this);
    }

    protected override void OnDisable()
    {
        All.Remove(this);
        AudioManager.SetCreatureMuted(this, false);
        base.OnDisable();
    }

    public void SetMuted(bool value)
    {
        if (Muted == value) return;
        Muted = value;
        AudioManager.SetCreatureMuted(this, value);
        if (value) SetSolo(false);
    }

    public void SetSolo(bool value)
    {
        Soloed = value && !Muted;
        if (!Soloed) Guide = null;
    }

    public void SetGuide(Vector3? point) => Guide = Soloed ? point : null;

    /// <summary>The loudest creature of type T this one hears, and how loud (0..1, 0 = none).</summary>
    protected T Hear<T>(out float loudness, float earBoost = 1f) where T : CaveGlowCreature
    {
        loudness = 0f;
        T best = null;
        Vector3 p = transform.position;
        foreach (CaveGlowCreature c in All)
        {
            if (c == this || !(c is T t)) continue;
            float reach = c.VoiceReach * earBoost;
            if (reach <= 0f) continue;
            float d = Vector3.Distance(p, c.transform.position);
            if (d >= reach) continue;
            float l = 1f - d / reach;
            if (l > loudness) { loudness = l; best = t; }
        }
        return best;
    }

    // ---------------- Building ----------------

    protected Material GlowMaterial(Color color, float core = 0.6f, float rim = 1.6f)
    {
        if (glowShader == null) glowShader = Shader.Find("Capstone/Cave/Glow");
        var m = new Material(glowShader != null ? glowShader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Cave glow" };
        m.SetColor("_Color", color);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Core", core);
        m.SetFloat("_Rim", rim);
        glowMaterials.Add(m);
        return m;
    }

    /// <summary>Glowing line material (vertex colour, additive), for threads drawn with LineRenderers.</summary>
    protected Material LineGlowMaterial()
    {
        var m = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Cave glow line" };
        glowMaterials.Add(m);
        return m;
    }

    /// <summary>Dark body material: lit, so the creature's own glow and its neighbours' lamps show it.</summary>
    protected static Material InkMaterial(Color color)
    {
        if (inkShader == null) inkShader = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(inkShader) { name = "Cave ink" };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.35f);
        return m;
    }

    /// <summary>How the light round a glow creature shows.
    /// Reveal (default): it uncovers the terrain round it from the black fog, with a slight shift to its colour
    /// (World/CaveReveal + Shaders/World/CaveReveal.hlsl), so the lit rock can be seen from far away.
    /// Volume: a banded light volume plus a lit mask (Capstone/Cave/LightVolume). LegacyHalo: the old soft billboard.</summary>
    public enum LightLook { Reveal, Volume, LegacyHalo }
    public static LightLook LightStyle = LightLook.Reveal;
    /// <summary>Reveal style only: also draw faint shafts of light in the air round each creature.</summary>
    public static bool RevealShafts = false;
    private readonly List<CaveReveal.Light> reveals = new List<CaveReveal.Light>();
    private static Shader volumeShader;
    private static Mesh ball;

    /// <summary>The light round the creature (see LightStyle). Size = the old halo's width; the volume reaches twice that.</summary>
    protected Renderer AddHalo(Transform parent, Vector3 local, float size, float strength = 1f)
    {
        if (LightStyle == LightLook.Reveal)
        {
            var revealAt = new GameObject("Reveal light").transform;
            revealAt.SetParent(parent, false);
            revealAt.localPosition = local;
            reveals.Add(CaveReveal.Add(revealAt, size * 3f, glowColor, () => Hidden ? 0f : Mathf.Clamp01(shownGlow)));
            // Optional faint shafts of light in the air round it (off: the reveal alone, like a stencil).
            if (!RevealShafts) return null;
            if (volumeShader == null) volumeShader = Shader.Find("Capstone/Cave/LightVolume");
            if (volumeShader == null) return null;
            Renderer shafts = AddVolume(parent, local, size * 2.2f, strength);
            Material shaft = shafts.sharedMaterial;
            shaft.SetFloat("_Rays", 1f);
            shaft.SetFloat("_Mask", 0f);
            shaft.SetFloat("_Rim", 0f);
            shaft.SetFloat("_Density", 0.12f);
            shaft.SetFloat("_NearFade", 3f);
            return shafts;
        }
        if (LightStyle == LightLook.Volume)
        {
            if (volumeShader == null) volumeShader = Shader.Find("Capstone/Cave/LightVolume");
            if (volumeShader != null) return AddVolume(parent, local, size * 2f, strength);
        }
        if (haloShader == null) haloShader = Shader.Find("Capstone/Cave/GlowHalo");
        if (haloShader == null) return null;
        if (quad == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        var anchor = new GameObject("Halo anchor").transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = local;
        var go = new GameObject("Halo (" + name + ")");
        go.transform.position = anchor.position;
        halos.Add(go.transform);
        haloAnchors.Add(anchor);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var m = new Material(haloShader) { name = "Cave halo" };
        m.SetColor("_Color", glowColor * strength);
        m.SetFloat("_Size", size);
        r.sharedMaterial = m;
        // The quad is resized in the shader; keep it from being culled at the screen edge.
        r.localBounds = new Bounds(Vector3.zero, Vector3.one * size * 2f);
        haloMaterials.Add(m);
        haloSizes.Add(size);
        return r;
    }

    private Renderer AddVolume(Transform parent, Vector3 local, float radius, float strength)
    {
        if (ball == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        var anchor = new GameObject("Light anchor").transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = local;
        // Outside the creature (like the halo), so carry size and hold position measure only the body.
        var go = new GameObject("Light volume (" + name + ")");
        go.transform.position = anchor.position;
        go.transform.localScale = Vector3.one * radius * 2f;
        halos.Add(go.transform);
        haloAnchors.Add(anchor);
        go.AddComponent<MeshFilter>().sharedMesh = ball;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var m = new Material(volumeShader) { name = "Cave light volume" };
        m.SetColor("_Color", glowColor * strength);
        r.sharedMaterial = m;
        haloMaterials.Add(m);
        haloSizes.Add(radius);
        return r;
    }

    protected virtual void LateUpdate()
    {
        for (int i = 0; i < halos.Count; i++)
            if (halos[i] != null && haloAnchors[i] != null)
            {
                halos[i].position = haloAnchors[i].position;
                halos[i].gameObject.SetActive(haloAnchors[i].gameObject.activeInHierarchy && shownGlow > 0.01f);
            }
    }

    protected virtual void OnDestroy()
    {
        foreach (Transform h in halos) if (h != null) Destroy(h.gameObject);
        foreach (CaveReveal.Light r in reveals) CaveReveal.Remove(r);
    }

    protected Light AddLamp(Transform parent, float range, float intensity)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        lamp = go.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = glowColor;
        lamp.range = range;
        lamp.intensity = intensity;
        lamp.shadows = LightShadows.None;
        lamp.renderMode = LightRenderMode.ForcePixel;
        lampIntensity = intensity;
        return lamp;
    }

    protected static GameObject Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Material material)
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

    protected static LineRenderer Line(Transform parent, string name, Material material, float width, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var l = go.AddComponent<LineRenderer>();
        l.sharedMaterial = material;
        l.widthMultiplier = width;
        l.positionCount = points;
        l.useWorldSpace = true;
        l.numCapVertices = 2;
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.receiveShadows = false;
        return l;
    }

    /// <summary>Eases the glow towards a brightness (1 = normal); aimed creatures shine a little brighter.</summary>
    protected void UpdateGlow(float brightness, float deltaTime)
    {
        float target = Hidden ? 0f : brightness * (Aimed ? 1.35f : 1f) * (Soloed ? 1.25f : 1f);
        shownGlow = Mathf.MoveTowards(shownGlow, target, deltaTime * 1.5f);
        foreach (Material m in glowMaterials) m.SetFloat("_Glow", shownGlow);
        for (int i = 0; i < haloMaterials.Count; i++)
        {
            haloMaterials[i].SetFloat("_Glow", shownGlow);
            haloMaterials[i].SetFloat("_Size", haloSizes[i] * (Aimed ? 1.25f : 1f));
        }
        if (lamp != null)
        {
            lamp.intensity = lampIntensity * shownGlow;
            lamp.enabled = shownGlow > 0.02f;
        }
    }

    // ---------------- Movement ----------------

    /// <summary>Moves a flying body by a step, sliding along rock; never into it. Returns the distance moved.</summary>
    protected float Fly(ref Vector3 position, Vector3 step, float radius)
    {
        float length = step.magnitude;
        if (length < 1e-5f) return 0f;
        Vector3 dir = step / length;
        if (Physics.SphereCast(position, radius, dir, out RaycastHit hit, length + 0.05f, TerrainMask, QueryTriggerInteraction.Ignore))
        {
            // Slide along the wall with what is left of the step.
            float free = Mathf.Max(0f, hit.distance - 0.05f);
            position += dir * free;
            Vector3 slide = Vector3.ProjectOnPlane(step - dir * free, hit.normal);
            if (slide.sqrMagnitude > 1e-6f &&
                !Physics.SphereCast(position, radius, slide.normalized, out _, slide.magnitude + 0.05f, TerrainMask, QueryTriggerInteraction.Ignore))
                position += slide;
            return free;
        }
        position += step;
        return length;
    }

    /// <summary>
    /// Creeps along a surface: moves along the surface plane, then sticks down to the rock again (wrapping over
    /// edges and up into corners). When no surface is found the step is not taken.
    /// </summary>
    protected bool Crawl(ref Vector3 position, ref Vector3 normal, Vector3 direction, float distance, float minUp = -2f)
    {
        Vector3 along = Vector3.ProjectOnPlane(direction, normal);
        if (along.sqrMagnitude < 1e-6f || distance <= 0f) return false;
        along.Normalize();
        // A wall ahead: climb onto it.
        if (Physics.Raycast(position + normal * 0.15f, along, out RaycastHit ahead, distance + 0.25f, TerrainMask, QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Dot(ahead.normal, Vector3.up) < minUp) return false;
            position = ahead.point;
            normal = ahead.normal;
            return true;
        }
        Vector3 next = position + along * distance;
        if (Physics.Raycast(next + normal * 0.5f, -normal, out RaycastHit down, 1.2f, TerrainMask, QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Dot(down.normal, Vector3.up) < minUp) return false;
            position = down.point;
            normal = Vector3.Slerp(normal, down.normal, 0.5f).normalized;
            return true;
        }
        // Over an edge: wrap round onto the face below it.
        if (Physics.Raycast(next - normal * 0.35f, -along, out RaycastHit wrap, distance + 0.4f, TerrainMask, QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Dot(wrap.normal, Vector3.up) < minUp) return false;
            position = wrap.point;
            normal = wrap.normal;
            return true;
        }
        return false;
    }

    /// <summary>Damped follow used for every body part: slow, phase-delayed, never overshooting.</summary>
    protected static Vector3 Follow(Vector3 current, Vector3 target, float rate, float deltaTime) =>
        Vector3.Lerp(current, target, 1f - Mathf.Exp(-rate * deltaTime));

    protected override void InitializeCreature()
    {
        base.InitializeCreature();
        SetLuminosity(1f);
    }
}
