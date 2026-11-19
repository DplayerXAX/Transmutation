using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes heat visible as a 3D heat wave: shells of evenly spaced dots swell outward from the warm thing and fade,
/// one after another. Each shell's surface folds with slow waves; crests carry bigger, brighter dots, and a few
/// bands glitch to cyan / magenta now and then (shader: Capstone/Effects/HeatHaze).
/// Reads the owner's existing heat state; it never changes it. More heat = wider, deeper-folded shells.
/// Added automatically at runtime to fire sources, fire bubbles, fire flowers, fuzz, bone children and the
/// bone mother; it can also be added by hand to any Creature.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeatHaze : MonoBehaviour
{
    [Header("Heat")]
    [Tooltip("Creature Heat treated as full intensity. Bones use their 0..1 Warmth instead.")]
    [Min(0.1f)] public float fullHeat = 20f;
    [Tooltip("Intensity below which the haze is hidden.")]
    [Range(0f, 1f)] public float threshold = 0.04f;
    [Tooltip("Lowest intensity while alive, for things that are always hot (fire bubbles).")]
    [Range(0f, 1f)] public float baseline = 0f;

    [Header("Shells")]
    [Tooltip("Radius where shells leave the body (metres).")]
    [Min(0f)] public float inner = 0.4f;
    [Tooltip("Radius where shells fade out at full intensity (metres). Fire sources use their radiation radius, up to this.")]
    [Min(0.2f)] public float radius = 2f;
    [Tooltip("Shells alive at once.")]
    [Range(1, 8)] public int shells = 3;
    [Tooltip("Dots on each shell.")]
    [Range(50, 6000)] public int dotsPerShell = 500;
    [Tooltip("Seconds for one shell to travel from the body to the end radius.")]
    [Min(0.2f)] public float cycle = 3f;
    [Tooltip("Depth of the folds on a shell at full intensity, as a fraction of the radius.")]
    [Min(0f)] public float fold = 0.08f;
    [Tooltip("Dot size relative to the gap between dots.")]
    [Range(0.05f, 1f)] public float dotScale = 0.25f;

    [Header("Look")]
    public Color heatColor = new Color(1f, 0.32f, 0.08f, 1f);
    public Color troughColor = new Color(0.55f, 0.02f, 0.05f, 1f);
    public Color crestColor = new Color(1f, 0.85f, 0.45f, 1f);
    public Color cyanColor = new Color(0f, 0.95f, 1f, 1f);
    public Color magentaColor = new Color(1f, 0.1f, 0.75f, 1f);
    [Range(0f, 1f)] public float opacity = 0.45f;
    [Range(0f, 1f)] public float glitch = 0.5f;

    private Creature creature;
    private BoneChild boneChild;
    private BoneKiln boneKiln;
    private float shown, seed;
    private Quaternion spin;

    private static Material material;
    private static MaterialPropertyBlock block;
    private static readonly Dictionary<Vector2Int, Mesh> shellMeshes = new Dictionary<Vector2Int, Mesh>();
    private static readonly int HeatColorId = Shader.PropertyToID("_HeatColor"), DeepColorId = Shader.PropertyToID("_DeepColor"),
        CrestColorId = Shader.PropertyToID("_CrestColor"), CyanId = Shader.PropertyToID("_CyanColor"),
        MagentaId = Shader.PropertyToID("_MagentaColor"), InnerId = Shader.PropertyToID("_Inner"),
        RadiusId = Shader.PropertyToID("_Radius"), IntensityId = Shader.PropertyToID("_Intensity"),
        FoldId = Shader.PropertyToID("_Fold"), CycleId = Shader.PropertyToID("_Cycle"),
        DotSizeId = Shader.PropertyToID("_DotSize"), OpacityId = Shader.PropertyToID("_Opacity"),
        GlitchId = Shader.PropertyToID("_Glitch"), SeedId = Shader.PropertyToID("_Seed");

    /// <summary>0..1 heat intensity right now (eased, so the haze swells and settles).</summary>
    public float Intensity => shown;

    // ---------------- Auto attach ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        Creature.Activated -= OnCreatureActivated;
        Creature.Activated += OnCreatureActivated;
    }

    private static void OnCreatureActivated(Creature target)
    {
        if (!Application.isPlaying || target == null) return;
        if (target is FireSource || target is FireBubble || target is FireFlower || target is FuzzCreature) Attach(target);
    }

    /// <summary>Adds a HeatHaze tuned for this owner, unless it already has one.</summary>
    public static HeatHaze Attach(Component owner)
    {
        if (owner == null) return null;
        if (owner.TryGetComponent(out HeatHaze existing)) return existing;
        HeatHaze haze = owner.gameObject.AddComponent<HeatHaze>();
        switch (owner)
        {
            case FireSource _: haze.fullHeat = 6000f; haze.inner = 0.6f; haze.radius = 5f; haze.dotsPerShell = 1100; haze.cycle = 3.5f; break;
            case FireBubble _: haze.fullHeat = 40f; haze.baseline = 0.12f; haze.inner = 0.35f; haze.radius = 1.6f; haze.dotsPerShell = 350; haze.cycle = 2.4f; break;
            case FireFlower _: haze.fullHeat = 70f; haze.inner = 0.6f; haze.radius = 2.2f; break;
            case FuzzCreature _: haze.fullHeat = 25f; haze.inner = 0.5f; haze.radius = 1.8f; haze.heatColor = new Color(1f, 0.1f, 0.12f); break;
            case BoneChild _: haze.inner = 1f; haze.radius = 4f; haze.dotsPerShell = 900; haze.cycle = 3.5f; haze.heatColor = new Color(1f, 0.5f, 0.08f); break;
            case BoneKiln _:
                haze.inner = 3f; haze.radius = 14f; haze.shells = 4; haze.dotsPerShell = 2800; haze.cycle = 6f; haze.fold = 0.05f;
                haze.heatColor = new Color(1f, 0.45f, 0.06f);
                break;
        }
        return haze;
    }

    // ---------------- Update ----------------

    private void Awake()
    {
        creature = GetComponent<Creature>();
        boneChild = GetComponent<BoneChild>();
        boneKiln = GetComponent<BoneKiln>();
        seed = Random.value * 50f;
        spin = Random.rotationUniform;
    }

    private void Update()
    {
        float target = ReadIntensity();
        shown = Mathf.MoveTowards(shown, target, Time.deltaTime * (target > shown ? 0.8f : 0.4f));
        if (shown > threshold) Draw();
    }

    private float ReadIntensity()
    {
        if (boneKiln != null) return boneKiln.Warmth;
        if (boneChild != null) return boneChild.Warmth;
        if (creature == null || creature.IsDead) return 0f;
        return Mathf.Max(baseline, Mathf.Clamp01(creature.Heat / fullHeat));
    }

    /// <summary>The mother's heat sits in the kiln heart (her skull once awake); a child's in its chest.</summary>
    private Vector3 Centre()
    {
        if (boneKiln != null) return boneKiln.Awake ? boneKiln.Head : boneKiln.HeartPoint;
        if (boneChild != null) return boneChild.Chest;
        return transform.position;
    }

    private float EndRadius() =>
        creature is FireSource source ? Mathf.Clamp(source.RadiationRadius, inner + 0.5f, radius) : radius;

    // ---------------- Drawing ----------------

    private void Draw()
    {
        if (!EnsureMaterial()) return;
        float end = EndRadius();
        Mesh mesh = Shells(shells, dotsPerShell);
        Vector3 centre = Centre();
        // Gap between neighbouring dots halfway out, so dot size follows the shell's density.
        float midRadius = Mathf.Lerp(inner, end, 0.5f);
        float gap = Mathf.Sqrt(4f * Mathf.PI * midRadius * midRadius / dotsPerShell);

        block.SetColor(HeatColorId, heatColor);
        block.SetColor(DeepColorId, troughColor);
        block.SetColor(CrestColorId, crestColor);
        block.SetColor(CyanId, cyanColor);
        block.SetColor(MagentaId, magentaColor);
        block.SetFloat(InnerId, inner);
        block.SetFloat(RadiusId, end);
        block.SetFloat(IntensityId, shown);
        block.SetFloat(FoldId, fold * end);
        block.SetFloat(CycleId, cycle);
        block.SetFloat(DotSizeId, gap * dotScale);
        block.SetFloat(OpacityId, opacity);
        block.SetFloat(GlitchId, glitch);
        block.SetFloat(SeedId, seed);
        RenderParams parameters = new RenderParams(material)
        {
            matProps = block,
            layer = gameObject.layer,
            worldBounds = new Bounds(centre, Vector3.one * (end * 2.4f)),
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };
        // A random fixed turn per source, so neighbouring sources do not show the same dot pattern.
        Graphics.RenderMesh(parameters, mesh, 0, Matrix4x4.TRS(centre, spin, Vector3.one));
    }

    /// <summary>
    /// Evenly dotted unit spheres (Fibonacci spiral), one quad per dot, each shell tagged with its phase.
    /// The shader sets the radius, so one mesh serves every source with the same counts.
    /// </summary>
    private static Mesh Shells(int shellCount, int dots)
    {
        Vector2Int key = new Vector2Int(shellCount, dots);
        if (shellMeshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;
        var vertices = new List<Vector3>();
        var corners = new List<Vector2>();
        var phases = new List<Vector2>();
        var triangles = new List<int>();
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int s = 0; s < shellCount; s++)
        {
            float phase = (float)s / shellCount;
            // Turn each shell a little so dots of successive shells do not line up into rays.
            Quaternion turn = Quaternion.Euler(s * 37f, s * 71f, 0f);
            for (int i = 0; i < dots; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / dots;
                float ring = Mathf.Sqrt(1f - y * y);
                float angle = golden * i;
                Vector3 dir = turn * new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                int first = vertices.Count;
                vertices.Add(dir); vertices.Add(dir); vertices.Add(dir); vertices.Add(dir);
                corners.Add(new Vector2(-1, -1)); corners.Add(new Vector2(1, -1)); corners.Add(new Vector2(1, 1)); corners.Add(new Vector2(-1, 1));
                for (int k = 0; k < 4; k++) phases.Add(new Vector2(phase, 0f));
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
            }
        }
        Mesh mesh = new Mesh { name = "Heat Haze Shells", hideFlags = HideFlags.DontSave };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, corners);
        mesh.SetUVs(1, phases);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        shellMeshes[key] = mesh;
        return mesh;
    }

    private static bool EnsureMaterial()
    {
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>("HeatHaze");
            if (shader == null) return false;
            material = new Material(shader) { name = "Heat Haze (runtime)", hideFlags = HideFlags.DontSave };
        }
        block ??= new MaterialPropertyBlock();
        return true;
    }
}
