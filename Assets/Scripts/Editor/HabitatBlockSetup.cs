using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Habitat Block: builds Scene_HabitatBlock, a small floating diorama for the art bible. A black-and-white chunk
/// of ground split into four regions, each bursting with its own colour of life, made from the game's own
/// materials and prefabs:
///   front left  - the bone mother, time and distortion (cold blue bone, cyan time ribbons, glitch),
///   front right - heat (fire flowers, fire bubbles, a heat crater),
///   back right  - light (crystals, lamps, beams, iridescent bubbles),
///   back left   - green growth (a spring, kelp, moss, mushrooms, cool creatures).
/// A cyan stream runs from the spring through the bones and falls past a cut in the front, which shows the inner
/// world cave. Lit by Scene_Daniel's sun under the liquid ink sky. Scene_Daniel and Scene_Lam are not touched.
/// </summary>
[InitializeOnLoad]
public static class HabitatBlockSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_ArtReference.unity";
    private const string MaterialFolder = "Assets/Materials/HabitatBlock";
    private const string CaptureFolder = "ArtCaptures/HabitatBlock";
    private const string Request = "Temp/HabitatBlock.request";
    private const string Status = "Temp/HabitatBlock.status";
    private const int Layer = 28;
    // The ground shaders draw rings round a centre; the block sits away from the origin so its lines run calm.
    private static readonly Vector3 Origin = new Vector3(140f, 0f, 90f);

    // Block footprint: x -HalfX..HalfX, z -HalfZ..HalfZ. Front (the cut face) is at z = -HalfZ.
    private const float HalfX = 8f, HalfZ = 6f;
    private const float SideBottom = -5f;
    // Cave behind the front cut: an ellipse round (0, CaveY) that closes up towards the back.
    private const float CaveY = -3.1f, CaveA = 5.2f, CaveB = 1.5f, CaveFloor = -4f, CaveDepth = 7.5f;

    // Region centres (x, z).
    private static readonly Vector2 BoneZone = new Vector2(-4.3f, -3f);
    private static readonly Vector2 HeatZone = new Vector2(4.4f, -2.8f);
    private static readonly Vector2 LightZone = new Vector2(4.6f, 3.3f);
    private static readonly Vector2 GrowthZone = new Vector2(-4.4f, 3.2f);
    private static readonly Vector2 Spring = new Vector2(-4.6f, 3.0f);
    private const float SpringRadius = 1.6f, SpringLevel = -0.25f;
    // The stream: from the spring, through the bones, to the front edge.
    private static readonly Vector2[] StreamPath =
    {
        new Vector2(-4.0f, 2.0f), new Vector2(-3.1f, 0.6f), new Vector2(-2.3f, -0.9f), new Vector2(-2.4f, -2.6f),
        new Vector2(-2.9f, -4.2f), new Vector2(-2.6f, -6.0f),
    };

    private static Transform root;
    private static double nextPoll;

    static HabitatBlockSetup() => EditorApplication.update += Poll;

    // Lets an outside tool ask for a rebuild or capture while the editor is open (write the command to Request).
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            // "refresh" imports changed scripts first; the build then runs on the next request.
            if (command.Contains("refresh")) { File.WriteAllText(Status, "OK: refresh"); AssetDatabase.Refresh(); return; }
            if (command.Contains("dump")) { Dump(); File.WriteAllText(Status, "OK: dump"); return; }
            if (command.Contains("optics")) RefineOpticsInScene();
            if (command.Contains("build")) BuildAndSave(false);
            if (command.Contains("capture")) Capture();
            File.WriteAllText(Status, "OK: " + command);
        }
        catch (Exception error)
        {
            File.WriteAllText(Status, error.ToString());
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Capstone/Art Direction/Create Habitat Block Scene")]
    private static void CreateScene() => BuildAndSave(true);

    [MenuItem("Tools/Capstone/Art Direction/Habitat Block: Refine Optics In Place")]
    private static void RefineOpticsMenu() => RefineOpticsInScene();

    [MenuItem("Tools/Capstone/Art Direction/Capture Habitat Block Views")]
    private static void CaptureMenu() => Capture();

    private static void BuildAndSave(bool interactive)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene;
        bool keepOpen = interactive;
        if (interactive)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Habitat Block", "Scene_HabitatBlock already exists. Rebuild it?", "Rebuild", "Cancel"))
                return;
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        else
        {
            // Outside requests never touch other open scenes: rebuild the habitat scene in place when it is open,
            // otherwise build it next to them and close it again.
            scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject old in scene.GetRootGameObjects()) Object.DestroyImmediate(old);
                keepOpen = true;
            }
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        }

        SceneManager.SetActiveScene(scene);
        Build();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        if (!interactive && previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
        if (!keepOpen) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("Scene_HabitatBlock saved. Views are under Habitat Block / Views; Capture Habitat Block Views renders them.");
    }

    // Writes the habitat scene's hierarchy (three levels, with materials) to Temp/HabitatBlock.hierarchy.txt.
    private static void Dump()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
        var lines = new List<string> { "open in editor: " + !opened + ", dirty: " + scene.isDirty };
        void Walk(Transform t, int depth)
        {
            var r = t.GetComponent<Renderer>();
            lines.Add(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [off]") + (r != null && r.sharedMaterial != null ? "  <" + r.sharedMaterial.name + ">" : "") + (t.childCount > 0 ? "  (" + t.childCount + ")" : ""));
            if (depth < 2) foreach (Transform c in t) Walk(c, depth + 1);
        }
        foreach (GameObject g in scene.GetRootGameObjects()) Walk(g.transform, 0);
        File.WriteAllLines("Temp/HabitatBlock.hierarchy.txt", lines);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    // ---------------- Build ----------------

    private static void Build()
    {
        root = new GameObject("Habitat Block").transform;
        LampTops.Clear();
        Lighting();
        Ground();
        Cave();
        Water();
        BoneMother();
        Heat();
        LightRegion();
        Growth();
        PosterDressing();
        Effects();
        Weave();
        Optics();
        RefineOptics();
        Views();
        Callouts();
        root.position = Origin;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        Selection.activeGameObject = root.gameObject;
    }

    // Scene_Daniel's sun and ambient, the liquid ink sky, no fog.
    private static void Lighting()
    {
        var sun = new GameObject("Directional Light");
        sun.transform.SetParent(root, false);
        sun.transform.rotation = new Quaternion(0.40821788f, -0.23456968f, 0.10938163f, 0.8754261f);
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95686275f, 0.8392157f);
        light.intensity = 1.6f;
        light.shadows = LightShadows.Soft;
        RenderSettings.sun = light;

        Material sky = Mat("MAT_Sky_LiquidInk");
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.212f, 0.227f, 0.259f);
        RenderSettings.ambientEquatorColor = new Color(0.114f, 0.125f, 0.133f);
        RenderSettings.ambientGroundColor = new Color(0.047f, 0.043f, 0.035f);
        RenderSettings.fog = false;
    }

    // ---------------- Ground ----------------

    private static float Noise(float x, float z, float scale, float offset) => Mathf.PerlinNoise(x * scale + offset, z * scale + offset * 0.7f);

    private static float Bump(float x, float z, Vector2 c, float radius) =>
        Mathf.Exp(-((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) / (radius * radius));

    // Top surface: gentle ground, a ridge at the back right (light), a heat crater, the spring and the stream bed.
    private static float Height(float x, float z)
    {
        float h = (Noise(x, z, 0.3f, 11f) - 0.5f) * 0.45f + (Noise(x, z, 1.1f, 3f) - 0.5f) * 0.1f;
        h += 1.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 6f, z)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, 4f, x));
        h += 0.9f * Bump(x, z, LightZone + new Vector2(0.6f, 1f), 2.4f);
        // Heat crater: a raised rim round a sunken middle.
        float r = new Vector2(x - HeatZone.x, z - HeatZone.y).magnitude;
        h += 0.55f * Mathf.Exp(-(r - 1.5f) * (r - 1.5f) / 0.5f) - 0.45f * Mathf.Exp(-r * r / 0.9f);
        h -= 0.85f * Bump(x, z, Spring, SpringRadius);
        float d = StreamDistance(x, z);
        h -= 0.35f * Mathf.Exp(-d * d / 0.25f) * Mathf.InverseLerp(StreamPath[0].y + 0.6f, StreamPath[0].y - 0.4f, z);
        return h;
    }

    private static float StreamDistance(float x, float z)
    {
        var p = new Vector2(x, z);
        float best = 99f;
        for (int i = 0; i + 1 < StreamPath.Length; i++)
        {
            Vector2 a = StreamPath[i], b = StreamPath[i + 1];
            float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
            best = Mathf.Min(best, (p - (a + (b - a) * t)).magnitude);
        }
        return best;
    }

    // A smooth point along the stream path (t 0..1).
    private static Vector2 StreamAt(float t)
    {
        float f = t * (StreamPath.Length - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, StreamPath.Length - 2);
        float u = f - i;
        Vector2 p0 = StreamPath[Mathf.Max(0, i - 1)], p1 = StreamPath[i], p2 = StreamPath[i + 1], p3 = StreamPath[Mathf.Min(StreamPath.Length - 1, i + 2)];
        return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
    }

    // Rough rock push on the side faces.
    private static float Rough(float a, float b) => (Noise(a, b, 0.9f, 5f) - 0.5f) * 0.35f + (Noise(a, b, 3.1f, 8f) - 0.5f) * 0.08f;

    private static bool InCaveMouth(float x, float y) =>
        y > CaveFloor && (x / CaveA) * (x / CaveA) + ((y - CaveY) / CaveB) * ((y - CaveY) / CaveB) < 1f;

    // Calmer copies of the world line materials: no hatching, fewer and wider-spaced lines.
    private static void Calm(Material m)
    {
        m.SetFloat("_HatchStrength", 0.08f);
        m.SetFloat("_FloorFreq", 4.5f);
        m.SetFloat("_RingFreq", 14f);
        m.SetFloat("_RingDistort", 1.5f);
        m.SetFloat("_ShadeLevels", 2f);
        m.SetVector("_Center", new Vector4(Origin.x - 30f, 0f, Origin.z + 60f, 0f));
    }

    private static void Ground()
    {
        Material surface = Variant("MAT_World_Surface", "HB_Ground", m =>
        {
            Calm(m);
            m.SetColor("_LineColor", new Color(0.82f, 0.82f, 0.84f));
            m.SetColor("_LineColor2", new Color(0.82f, 0.82f, 0.84f));
        });
        var group = Group("Ground (black with white lines)");

        Part("Top", group, Surface(96, 72, (u, v) =>
        {
            float x = Mathf.Lerp(-HalfX, HalfX, u), z = Mathf.Lerp(-HalfZ, HalfZ, v);
            return new Vector3(x, Height(x, z), z);
        }, Vector3.up), surface);

        // Four side faces from the top edge down to SideBottom, the front with the cave mouth cut out.
        Part("Front Face", group, Surface(160, 60, (u, v) =>
        {
            float x = Mathf.Lerp(-HalfX, HalfX, u), top = Height(x, -HalfZ), y = Mathf.Lerp(SideBottom, top, v);
            return new Vector3(x, y, -HalfZ - Rough(x, y) * Edge(u) * Edge(v));
        }, Vector3.back, (p) => !InCaveMouth(p.x, p.y)), surface);
        Part("Back Face", group, Surface(96, 40, (u, v) =>
        {
            float x = Mathf.Lerp(HalfX, -HalfX, u), top = Height(x, HalfZ), y = Mathf.Lerp(SideBottom, top, v);
            return new Vector3(x, y, HalfZ + Rough(x + 40f, y) * Edge(u) * Edge(v));
        }, Vector3.forward), surface);
        Part("Left Face", group, Surface(72, 40, (u, v) =>
        {
            float z = Mathf.Lerp(HalfZ, -HalfZ, u), top = Height(-HalfX, z), y = Mathf.Lerp(SideBottom, top, v);
            return new Vector3(-HalfX - Rough(z + 80f, y) * Edge(u) * Edge(v), y, z);
        }, Vector3.left), surface);
        Part("Right Face", group, Surface(72, 40, (u, v) =>
        {
            float z = Mathf.Lerp(-HalfZ, HalfZ, u), top = Height(HalfX, z), y = Mathf.Lerp(SideBottom, top, v);
            return new Vector3(HalfX + Rough(z + 120f, y) * Edge(u) * Edge(v), y, z);
        }, Vector3.right), surface);

        // Underside: a rough root hanging below, like a chunk torn out of the ground.
        Part("Underside", group, Surface(64, 48, (u, v) =>
        {
            float x = Mathf.Lerp(-HalfX, HalfX, u), z = Mathf.Lerp(-HalfZ, HalfZ, v);
            float edge = Mathf.Max(Mathf.Abs(x) / HalfX, Mathf.Abs(z) / HalfZ);
            float drop = (1f - edge * edge) * (3.5f + 2.5f * Noise(x, z, 0.4f, 61f));
            return new Vector3(x, SideBottom - drop, z);
        }, Vector3.down), surface);
    }

    // 0 at the ends of a face, 1 inside, so faces meet cleanly at the corners.
    private static float Edge(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 12f);

    // ---------------- Inner world ----------------

    private static void Cave()
    {
        Material inner = Variant("MAT_World_Inner", "HB_Inner", Calm);
        var group = Group("Inner World (cave behind the front cut)");

        // A tube from the mouth, narrowing to a closed end at the back, with a flat floor. Faces inward.
        Part("Cave", group, Surface(72, 40, (u, v) =>
        {
            float angle = u * Mathf.PI * 2f, z = -HalfZ + v * CaveDepth;
            float s = Mathf.Sqrt(Mathf.Max(0f, 1f - v * v)) + 0.0001f;
            float wobble = 1f + (Noise(angle * 2f, z, 0.8f, 33f) - 0.5f) * 0.3f * Mathf.Min(1f, v * 6f);
            float x = Mathf.Cos(angle) * CaveA * s * wobble, y = CaveY + Mathf.Sin(angle) * CaveB * s * wobble;
            y = Mathf.Max(y, CaveFloor + (Noise(x, z, 1.2f, 4f) - 0.5f) * 0.15f * Mathf.Min(1f, v * 6f));
            return new Vector3(x, y, z);
        }, null, null, new Vector3(0f, CaveY, -2f)), inner);

        float ceiling = CaveY + CaveB;
        Prefab("Decor_Crystals_0", group, new Vector3(-2.6f, CaveFloor, -3.0f), 0f, 2.5f, false, true);
        Prefab("Decor_Crystals_3", group, new Vector3(3.6f, CaveFloor, -3.6f), 80f, 2f, false, true);
        Prefab("Decor_GlowSacs_0", group, new Vector3(1.8f, CaveFloor, -2.2f), 50f, 2f, false, true);
        Prefab("Decor_GlowSacs_2", group, new Vector3(-4.0f, CaveFloor, -4.4f), 160f, 1.4f, false, true);
        Prefab("Decor_CavePebbles_0", group, new Vector3(0.3f, CaveFloor, -4.8f), 20f, 2.2f, false, true);
        Prefab("Decor_ShellCarpets_0", group, new Vector3(-0.9f, CaveFloor, -1.6f), 70f, 2.9f, false, true);
        Prefab("Decor_NeedleBeards_0", group, new Vector3(2.8f, CaveFloor, -4.9f), 130f, 1.6f, false, true);
        Prefab("Decor_ShellTentacles_0", group, new Vector3(-2.0f, CaveFloor, -5.0f), 200f, 1.8f, false, true);
        Prefab("Decor_HangingVeils_0", group, new Vector3(-1.2f, ceiling - 0.05f, -3.6f), 0f, 1.8f, true, true);
        Prefab("Decor_HangingVeils_1", group, new Vector3(1.4f, ceiling - 0.1f, -2.6f), 120f, 1.6f, true, true);

        PointLight("Inner Cyan", group, new Vector3(-1.2f, -2.5f, -3.2f), new Color(0.3f, 0.9f, 1f), 7f, 8f);
        PointLight("Inner Violet", group, new Vector3(2.4f, -2.8f, -2.2f), new Color(0.6f, 0.3f, 1f), 6f, 7f);
    }

    // ---------------- Water: spring, stream, fall ----------------

    private static void Water()
    {
        Material water = Mat("MAT_ErosiveWater") ?? Mat("MAT_Realm_Stream");
        var group = Group("Water (cool)");

        // Spring: a disc at the water level; the ground round it hides the edge.
        var spring = new RealmMeshes();
        var rows = new List<Vector3[]>();
        for (int i = 0; i <= 10; i++)
        {
            var row = new Vector3[33];
            for (int k = 0; k <= 32; k++)
            {
                float a = k / 32f * Mathf.PI * 2f, r = SpringRadius * 1.2f * i / 10f;
                row[k] = new Vector3(Spring.x + Mathf.Cos(a) * r, SpringLevel, Spring.y + Mathf.Sin(a) * r);
            }
            rows.Add(row);
        }
        spring.Grid(rows, 0.3f);
        Part("Spring", group, spring.Build("Spring"), water);

        // Stream down the bed, then a fall past the cave mouth.
        var path = new List<Vector3>();
        var up = new List<Vector3>();
        for (int i = 0; i <= 60; i++)
        {
            Vector2 p = StreamAt(i / 60f);
            path.Add(new Vector3(p.x, Mathf.Min(SpringLevel, Height(p.x, p.y) + 0.05f), p.y));
            up.Add(Vector3.up);
        }
        Vector3 lip = path[path.Count - 1];
        for (int i = 1; i <= 24; i++)
        {
            float t = i / 24f;
            path.Add(new Vector3(lip.x + t * 0.2f, Mathf.Lerp(lip.y, CaveFloor - 0.6f, t), -HalfZ - 0.15f - Mathf.Sin(t * Mathf.PI * 0.5f) * 0.6f));
            up.Add(Vector3.back);
        }
        var stream = new RealmMeshes();
        stream.Ribbon(path, up, 0.3f, 4, 0.6f);
        Part("Stream And Fall", group, stream.Build("Stream"), water);

        // Glowing cyan veins along and out of the stream, like lightning in the ground.
        var veins = new RealmMeshes();
        var random = new Random(77);
        for (int v = 0; v < 9; v++)
        {
            Vector2 start = StreamAt(Lerp(random, 0.1f, 0.85f));
            float angle = Lerp(random, 0f, Mathf.PI * 2f), length = Lerp(random, 1f, 2.6f);
            var vein = new List<Vector3>();
            Vector2 p = start;
            for (int k = 0; k <= 10; k++)
            {
                vein.Add(new Vector3(p.x, Height(p.x, p.y) + 0.02f, p.y));
                angle += Lerp(random, -0.7f, 0.7f);
                p += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length / 10f;
            }
            veins.Tube(vein, u => Mathf.Lerp(0.045f, 0.012f, u), 5, 0f, 1f, (float)random.NextDouble(), 0.02f);
        }
        Part("Cyan Veins", group, veins.Build("Veins"), Mat("MAT_Realm_Vein"));
    }

    // ---------------- Front left: bone mother, time, distortion ----------------

    // The bone mother in the Bone Kiln's own language, at diorama scale: a chain of twisted marble vertebrae
    // slanting out of the ground (skull half buried at the front), glassy comb-jelly rib bands hooking down
    // to the ground, the kiln's designed flora and wire spirals on the bare vertebrae, two curled children.
    private static readonly Vector3[] MotherKeys =
    {
        new Vector3(-6.7f, -0.45f, -4.9f), new Vector3(-5.9f, 0.9f, -3.9f), new Vector3(-4.7f, 2.4f, -2.7f),
        new Vector3(-3.4f, 2.7f, -1.6f), new Vector3(-2.3f, 1.8f, -0.6f), new Vector3(-1.6f, 0.5f, 0.4f), new Vector3(-1.3f, -0.6f, 1.1f),
    };

    // A point on her spine (t 0 = skull .. 1 = tail), y measured from the ground under it.
    private static Vector3 MotherSpine(float t)
    {
        float f = Mathf.Clamp01(t) * (MotherKeys.Length - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, MotherKeys.Length - 2);
        float u = f - i;
        Vector3 p0 = MotherKeys[Mathf.Max(0, i - 1)], p1 = MotherKeys[i], p2 = MotherKeys[i + 1], p3 = MotherKeys[Mathf.Min(MotherKeys.Length - 1, i + 2)];
        Vector3 p = 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        p.y += Height(p.x, p.z);
        return p;
    }

    private static void BoneMother()
    {
        var group = Group("Bone Mother, Time, Distortion (front left)");
        Material marble = Mat("MAT_Landmark_BoneMarble") ?? Mat("MAT_Landmark_Bone");
        Material ink = Mat("MAT_Landmark_Ink");
        Material comb = ShaderAsset("Capstone/Landmarks/BoneComb", "HB_BoneComb", null);
        Material flora = ShaderAsset("Capstone/Landmarks/Flora", "HB_Flora", null);
        var random = new Random(19);

        var vertebrae = new BoneShapes.Builder();
        var ribs = new List<Mesh>();
        var growth = new BoneFlora(23);
        const int stations = 22;
        for (int i = 0; i < stations; i++)
        {
            float t = i / (stations - 1f);
            Vector3 p = MotherSpine(t);
            Vector3 forward = (MotherSpine(t + 0.02f) - MotherSpine(t - 0.02f)).normalized;
            // The helix: each vertebra wrung a little further round the spine.
            Vector3 up = Quaternion.AngleAxis(Mathf.Sin(t * 7f) * 28f, forward) * Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
            Vector3 side = Vector3.Cross(up, forward).normalized;
            float radius = Mathf.Lerp(0.16f, 0.3f, Mathf.Sin(Mathf.Clamp01(t * 1.2f) * Mathf.PI)) * (t > 0.85f ? Mathf.Lerp(1f, 0.5f, (t - 0.85f) / 0.15f) : 1f);
            BoneShapes.Vertebra(vertebrae, p, forward, up, radius, t);
            float above = p.y - Height(p.x, p.z);
            bool ribbed = i >= 4 && i <= 16 && above > 0.8f;
            if (ribbed)
            {
                // A pair of hooked bands, hung in the vertebra's frame, curling down to touch the ground.
                float length = above * 0.95f + 0.5f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 out1 = side * s;
                    var points = new List<Vector3>
                    {
                        p + out1 * radius,
                        p + out1 * (length * 0.55f) + up * (length * 0.18f),
                        p + out1 * (length * 0.85f) - up * (length * 0.3f),
                        p + out1 * (length * 0.7f) - up * (length * 0.85f) - forward * 0.2f,
                    };
                    ribs.Add(BoneShapes.Ribbon(CatmullRom(points, 6), 0.3f, 0.08f, 0.05f, forward, (float)random.NextDouble()));
                }
            }
            else if (i > 0 && i < stations - 1 && random.NextDouble() < 0.7)
            {
                // Bare vertebrae carry the kiln's flora: a wire spiral to the next one, or a small arrangement on top.
                if (random.NextDouble() < 0.5) growth.Vine(p, MotherSpine(t + 1f / (stations - 1f)), radius * 1.5f, 2.5f);
                else growth.Cluster(p + up * radius, up, 0.35f);
            }
        }
        Part("Mother Vertebrae", group, vertebrae.ToMesh("Mother Vertebrae"), marble);
        var ribParent = Group("Mother Ribs", group);
        for (int r = 0; r < ribs.Count; r++) Part("Rib " + r, ribParent, ribs[r], comb);
        GameObject floraObject = growth.Attach(group, flora, "Mother Flora");
        if (floraObject != null) floraObject.GetComponent<MeshFilter>().sharedMesh.hideFlags = HideFlags.None;

        // The skull, half buried at the front, with ink eye sockets.
        Vector3 head = MotherSpine(0f), headDir = (MotherSpine(0f) - MotherSpine(0.06f)).normalized;
        GameObject skull = Part("Mother Skull", group, BoneShapes.Skull(new Vector3(0.6f, 0.48f, 1.05f), 0.3f), marble);
        skull.transform.SetPositionAndRotation(head + headDir * 0.5f + Vector3.down * 0.25f, Quaternion.LookRotation(headDir + Vector3.down * 0.35f));
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject eye = Part("Eye Socket", skull.transform, BuiltinSphere(), ink);
            eye.transform.localPosition = new Vector3(s * 0.36f, 0.22f, 0.62f);
            eye.transform.localScale = new Vector3(0.26f, 0.2f, 0.14f);
        }

        // Her children, curled up near her.
        BoneChildPose(group, new Vector2(-7.0f, -1.6f), 40f, 1f, marble, comb, 61);
        BoneChildPose(group, new Vector2(-4.6f, -5.0f), 200f, 0.85f, marble, comb, 62);

        // Time: two translucent cyan ribbons flowing through her ribs, with bright threads along them.
        var ribbons = new RealmMeshes();
        var threads = new RealmMeshes();
        for (int j = 0; j < 2; j++)
        {
            var path = new List<Vector3>();
            var up = new List<Vector3>();
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                Vector3 s = MotherSpine(Mathf.Lerp(-0.1f, 1.1f, t));
                float lift = Mathf.Lerp(0.6f, 1.2f, j) + Mathf.Sin(t * 6f + j * 2f) * 0.35f;
                path.Add(new Vector3(s.x + Mathf.Sin(t * 5f + j) * 0.8f, Height(s.x, s.z) + lift, s.z + Mathf.Cos(t * 4f + j) * 0.5f));
                up.Add(Quaternion.AngleAxis(t * 200f + j * 70f, (MotherKeys[MotherKeys.Length - 1] - MotherKeys[0]).normalized) * Vector3.up);
            }
            ribbons.Ribbon(path, up, 0.28f - j * 0.08f, 6, j / 2f);
            threads.Tube(path, u => 0.014f, 4, 0f, 1f, (float)random.NextDouble(), 0f);
        }
        Part("Time Ribbons", group, ribbons.Build("Time Ribbons"), Variant("MAT_Realm_Jelly", "HB_TimeRibbon", m =>
        {
            m.SetColor("_Tint", new Color(0.25f, 0.85f, 1f));
            m.SetColor("_FilmA", new Color(0.2f, 1f, 1f));
            m.SetColor("_FilmB", new Color(0.55f, 0.6f, 1f));
            m.SetColor("_FilmC", new Color(0.8f, 1f, 1f));
            m.SetFloat("_Opacity", 0.28f);
            m.SetFloat("_EdgeOpacity", 0.9f);
            m.SetFloat("_Glow", 1f);
        }));
        Part("Time Threads", group, threads.Build("Time Threads"), Mat("MAT_Realm_Thread"));

        // Distortion: a glitched puddle in her shadow.
        Material puddle = Mat("GlitchedPool");
        if (puddle != null) Part("Glitched Puddle", group, Disc(new Vector2(-5.4f, -2.0f), 0.9f, 0.7f, 0.04f), puddle);
        PointLight("Comb Blue", group, MotherSpine(0.45f) + Vector3.down * 0.6f, new Color(0.3f, 0.65f, 1f), 6f, 7f);
    }

    // A curled child of the mother, built like BoneChild: marble vertebrae and skull in glassy skin beads,
    // three pairs of ribs folded down with skin wings, lying along an arc on the ground.
    private static void BoneChildPose(Transform parent, Vector2 at, float yaw, float size, Material bone, Material skin, int seed)
    {
        var random = new Random(seed);
        var child = Group("Bone Child", parent);
        const int count = 7;
        const float spacing = 0.55f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (count - 1f);
            float radius = Mathf.Lerp(0.2f, 0.09f, t) * (1f + 0.4f * Mathf.Sin(t * Mathf.PI));
            // Curled: the body follows a tightening arc.
            float a = (yaw + t * 230f) * Mathf.Deg2Rad, ring = 1.15f * size * (1f - t * 0.35f);
            Vector2 flat = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
            Vector2 next = at + new Vector2(Mathf.Cos(a + 0.2f), Mathf.Sin(a + 0.2f)) * ring;
            Vector3 p = new Vector3(flat.x, Height(flat.x, flat.y) + radius * 1.4f * size, flat.y);
            Vector3 forward = new Vector3(flat.x - next.x, 0f, flat.y - next.y).normalized;
            var bead = new GameObject(i == 0 ? "Head" : "Vertebra " + i).transform;
            bead.SetParent(child, false);
            bead.SetPositionAndRotation(p, Quaternion.LookRotation(forward, Vector3.up));
            bead.localScale = Vector3.one * size;
            var b = new BoneShapes.Builder();
            BoneShapes.Vertebra(b, Vector3.zero, Vector3.forward, Vector3.up, radius, (float)random.NextDouble(), spacing * 0.42f);
            Part("Bone", bead, b.ToMesh("Child Vertebra"), bone);
            if (i == 0) Part("Skull", bead, BoneShapes.Skull(new Vector3(0.32f, 0.26f, 0.5f), (float)random.NextDouble()), bone).transform.localPosition = new Vector3(0f, 0.05f, 0.35f);
            GameObject skinBead = Part("Skin", bead, BuiltinSphere(), skin);
            skinBead.transform.localPosition = i == 0 ? new Vector3(0f, 0.05f, 0.25f) : Vector3.zero;
            skinBead.transform.localScale = new Vector3(radius * 3.2f, radius * 2.8f, spacing * 1.35f) * (i == 0 ? 1.5f : 1f);
            if (i < 2 || i > 4) continue;
            for (int s = -1; s <= 1; s += 2)
            {
                float length = 1.3f - (i - 2) * 0.2f;
                var points = new List<Vector3>();
                for (int k = 0; k <= 8; k++)
                {
                    float u = k / 8f;
                    points.Add(new Vector3(s * Mathf.Sin(u * 1.9f) * length, Mathf.Sin(u * 2.6f) * 0.22f - u * u * 0.15f, -u * 0.35f));
                }
                var hinge = new GameObject(s < 0 ? "Rib L" : "Rib R").transform;
                hinge.SetParent(bead, false);
                hinge.localPosition = new Vector3(s * radius * 0.8f, 0.05f, 0f);
                hinge.localRotation = Quaternion.Euler(0f, 0f, -55f * s);
                Part("Rib", hinge, BoneShapes.Ribbon(points, 0.11f, 0.05f, 0.05f, Vector3.forward, (float)random.NextDouble()), bone);
                Part("Wing", hinge, BoneShapes.Ribbon(points, 0.42f, 0.16f, 0.025f, Vector3.forward, 0.5f), skin).transform.localPosition = new Vector3(0f, 0f, -0.08f);
            }
        }
    }

    private static List<Vector3> CatmullRom(List<Vector3> keys, int steps)
    {
        var result = new List<Vector3>();
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            Vector3 p0 = keys[Mathf.Max(0, i - 1)], p1 = keys[i], p2 = keys[i + 1], p3 = keys[Mathf.Min(keys.Count - 1, i + 2)];
            for (int k = 0; k < steps; k++)
            {
                float u = k / (float)steps;
                result.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u));
            }
        }
        result.Add(keys[keys.Count - 1]);
        return result;
    }

    private static Mesh builtinSphere;
    private static Mesh BuiltinSphere()
    {
        if (builtinSphere == null) builtinSphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        return builtinSphere;
    }

    // ---------------- Front right: heat ----------------

    private static void Heat()
    {
        var group = Group("Heat (front right)");
        Vector3 c = Top(HeatZone.x, HeatZone.y);

        // Molten cracks running out of the crater, the warm twin of the cyan veins.
        var random = new Random(5);
        var cracks = new RealmMeshes();
        for (int v = 0; v < 14; v++)
        {
            float angle = v / 14f * Mathf.PI * 2f + Lerp(random, -0.2f, 0.2f), length = Lerp(random, 1.2f, 3.2f);
            Vector2 p = HeatZone + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.3f;
            var crack = new List<Vector3>();
            for (int k = 0; k <= 30; k++)
            {
                crack.Add(new Vector3(p.x, Height(p.x, p.y) - 0.01f, p.y));
                angle += Lerp(random, -0.25f, 0.25f);
                p += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length / 30f;
            }
            cracks.Tube(crack, u => Mathf.Lerp(0.05f, 0.012f, u), 5, 0f, 1f, (float)random.NextDouble(), 0.02f);
        }
        Part("Molten Cracks", group, cracks.Build("Molten Cracks"), Variant("MAT_Realm_Vein", "HB_MoltenVein", m =>
        {
            m.SetColor("_Body", new Color(0.12f, 0.02f, 0.01f));
            m.SetColor("_BodyLit", new Color(0.35f, 0.06f, 0.02f));
            m.SetColor("_Glow", new Color(1f, 0.16f, 0.05f));
            m.SetColor("_Sheen", new Color(1f, 0.8f, 0.4f));
        }));

        Creature("Fire Flower", group, Top(HeatZone.x - 0.2f, HeatZone.y + 0.1f), 0f, 3.4f, 0f, true);
        Creature("Fire Flower", group, Top(HeatZone.x + 1.6f, HeatZone.y + 1.0f), 40f, 2.2f, 0f, true);
        Creature("Fire Flower", group, Top(HeatZone.x - 1.7f, HeatZone.y + 0.8f), 200f, 2f, 0f, true);
        Creature("Fire Flower", group, Top(HeatZone.x + 1.1f, HeatZone.y - 1.6f), 120f, 1.7f, 0f, true);
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = c + new Vector3(Lerp(random, -1.2f, 1.2f), 1.6f + i * 0.55f, Lerp(random, -0.9f, 0.9f));
            Creature("Bubble", group, p, 0f, Lerp(random, 0.25f, 0.6f));
        }
        Prefab("Decor_Shards_1", group, Top(HeatZone.x + 2.6f, HeatZone.y - 0.2f), 30f, 1.4f);
        Prefab("Decor_Cairns_2", group, Top(HeatZone.x - 2.4f, HeatZone.y - 1.6f), 150f, 1.2f);
        Haze(group, "Crater Haze", c + Vector3.up * 0.6f, 0.8f, 3.6f, 1600, 3f);
        Haze(group, "Flower Haze", Top(HeatZone.x + 1.6f, HeatZone.y + 1.0f) + Vector3.up * 0.9f, 0.5f, 2f, 700, 7f);
        PointLight("Heat Orange", group, c + Vector3.up * 1.2f, new Color(1f, 0.22f, 0.08f), 7f, 7f);
        PointLight("Heat Red", group, c + new Vector3(1.4f, 0.6f, -1.4f), new Color(1f, 0.06f, 0.04f), 5f, 5f);
    }

    // Heat haze as the game draws it (HeatHaze): dotted shells swelling out of a hot spot. HeatHaze only draws
    // while playing and reads a creature's heat, so here the same mesh and shader sit in the scene at full heat.
    private static void Haze(Transform parent, string name, Vector3 centre, float inner, float radius, int dots, float seed)
    {
        Shader shader = Resources.Load<Shader>("HeatHaze");
        if (shader == null) { Debug.LogWarning("Habitat Block: HeatHaze shader not found."); return; }
        Material material = ShaderAsset("", "HB_HeatHaze_" + name.Replace(" ", ""), m =>
        {
            float mid = Mathf.Lerp(inner, radius, 0.5f);
            m.SetFloat("_Inner", inner);
            m.SetFloat("_Radius", radius);
            m.SetFloat("_Intensity", 1f);
            m.SetFloat("_Fold", 0.08f * radius);
            m.SetFloat("_Cycle", 3.5f);
            m.SetFloat("_DotSize", Mathf.Sqrt(4f * Mathf.PI * mid * mid / dots) * 0.18f);
            m.SetFloat("_Opacity", 0.55f);
            m.SetFloat("_Glitch", 0.5f);
            m.SetFloat("_Seed", seed);
            m.SetColor("_HeatColor", new Color(1f, 0.12f, 0.05f));
            m.SetColor("_DeepColor", new Color(0.55f, 0.01f, 0.03f));
            m.SetColor("_CrestColor", new Color(1f, 0.5f, 0.25f));
        }, shader);

        // Evenly dotted unit spheres (Fibonacci spiral), one quad per dot, three shells, each tagged with its phase.
        const int shells = 3;
        var vertices = new List<Vector3>();
        var corners = new List<Vector2>();
        var phases = new List<Vector2>();
        var triangles = new List<int>();
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int s = 0; s < shells; s++)
        {
            Quaternion turn = Quaternion.Euler(s * 37f, s * 71f, 0f);
            for (int i = 0; i < dots; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / dots, ring = Mathf.Sqrt(1f - y * y), angle = golden * i;
                Vector3 dir = turn * new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                int first = vertices.Count;
                for (int k = 0; k < 4; k++) { vertices.Add(dir); phases.Add(new Vector2((float)s / shells, 0f)); }
                corners.Add(new Vector2(-1, -1)); corners.Add(new Vector2(1, -1)); corners.Add(new Vector2(1, 1)); corners.Add(new Vector2(-1, 1));
                triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }
        }
        var mesh = new Mesh { name = "Heat Haze Shells", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, corners);
        mesh.SetUVs(1, phases);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.4f);
        GameObject go = Part(name, parent, mesh, material);
        go.transform.SetPositionAndRotation(centre, Quaternion.Euler(seed * 13f, seed * 29f, 0f));
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------- Back right: light ----------------

    private static void LightRegion()
    {
        var group = Group("Light (back right)");
        var random = new Random(31);

        var crystal = new RealmMeshes();
        var frost = new RealmMeshes();
        var film = new RealmMeshes();
        CrystalCluster(crystal, Top(LightZone.x + 1.2f, LightZone.y + 1.2f), 11, 2.2f, random);
        CrystalCluster(crystal, Top(LightZone.x - 1.6f, LightZone.y - 0.6f), 6, 1.2f, random);
        CrystalCluster(frost, Top(LightZone.x + 2.4f, LightZone.y - 1.4f), 7, 1.3f, random);
        CrystalCluster(film, Top(LightZone.x - 0.2f, LightZone.y + 2.0f), 6, 1.5f, random);
        Part("Crystals", group, crystal.Build("Crystals"), Variant("MAT_Realm_Crystal", "HB_LightCrystal", m =>
        {
            m.SetColor("_DeepColor", new Color(0.35f, 0.5f, 0.7f));
            m.SetColor("_MidColor", new Color(0.85f, 0.93f, 1f));
            m.SetColor("_EdgeColor", Color.white);
            m.SetColor("_FringeA", new Color(0.4f, 1f, 1f));
            m.SetColor("_FringeB", new Color(1f, 0.55f, 0.9f));
        }));
        Part("Frost Crystals", group, frost.Build("Frost"), Mat("MAT_Realm_Frost"));
        Part("Film Crystals", group, film.Build("Film"), Mat("MAT_Realm_Film"));

        // Lamp stalks: twisted pale stems holding a glowing orb.
        var stalks = new RealmMeshes();
        var orbs = new RealmMeshes();
        Vector2[] lamps = { new Vector2(LightZone.x - 0.8f, LightZone.y + 0.6f), new Vector2(LightZone.x + 0.9f, LightZone.y - 0.4f), new Vector2(LightZone.x - 2.4f, LightZone.y + 1.6f) };
        for (int l = 0; l < lamps.Length; l++)
        {
            Vector3 foot = Top(lamps[l].x, lamps[l].y);
            float height = 1.6f + l * 0.5f;
            Vector3 top = foot + new Vector3(Lerp(random, -0.3f, 0.3f), height, Lerp(random, -0.3f, 0.3f));
            for (int s = 0; s < 3; s++)
            {
                var path = new List<Vector3>();
                for (int k = 0; k <= 12; k++)
                {
                    float u = k / 12f, a = s * 2.1f + u * 7f;
                    float spread = Mathf.Lerp(0.3f, 0.06f, Mathf.SmoothStep(0f, 1f, u * 3f));
                    path.Add(Vector3.Lerp(foot - Vector3.up * 0.1f, top, u) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spread);
                }
                stalks.Tube(path, u => Mathf.Lerp(0.06f, 0.035f, u), 6, 0f, 0.95f, (float)random.NextDouble(), 0.03f);
            }
            orbs.Sphere(top + Vector3.up * 0.2f, Vector3.one * (0.26f + l * 0.05f), Quaternion.identity, 10, 14, 1f, (float)random.NextDouble());
            PointLight("Lamp Glow", group, top + Vector3.up * 0.2f, new Color(0.85f, 0.92f, 1f), 3f, 4f);
            LampTops.Add(top + Vector3.up * 0.2f);
        }
        Part("Lamp Stalks", group, stalks.Build("Lamp Stalks"), Mat("MAT_Realm_Thread"));
        Part("Lamp Orbs", group, orbs.Build("Lamp Orbs"), Mat("MAT_Realm_Lamp"));

        // Iridescent bubbles rising off the ridge.
        var bubbles = new RealmMeshes();
        for (int i = 0; i < 26; i++)
        {
            float r = Mathf.Lerp(0.08f, 0.42f, Mathf.Pow((float)random.NextDouble(), 2f));
            Vector3 p = Top(LightZone.x + Lerp(random, -2f, 2.4f), LightZone.y + Lerp(random, -1.5f, 2f)) + Vector3.up * Lerp(random, 1f, 5.5f);
            bubbles.Sphere(p, Vector3.one * r, Quaternion.identity, 10, 14, 0.5f, (float)random.NextDouble());
        }
        Part("Iridescent Bubbles", group, bubbles.Build("Bubbles"), Variant("MAT_Realm_WebFilm", "HB_Bubble", m =>
        {
            m.SetColor("_Tint", new Color(0.85f, 0.9f, 1f));
            m.SetColor("_FilmA", new Color(0.3f, 1f, 0.95f));
            m.SetColor("_FilmB", new Color(1f, 0.45f, 0.85f));
            m.SetColor("_FilmC", new Color(1f, 0.9f, 0.4f));
            m.SetFloat("_Iridescence", 1f);
            m.SetFloat("_Opacity", 0.12f);
            m.SetFloat("_EdgeOpacity", 0.9f);
            m.SetFloat("_Glow", 0.8f);
        }));

        // Light falling from above: two soft beams.
        Material beam = Mat("MAT_Realm_Beam");
        if (beam != null)
        {
            var beams = new RealmMeshes();
            foreach (Vector2 b in new[] { new Vector2(LightZone.x + 0.4f, LightZone.y + 0.8f), new Vector2(LightZone.x + 2.2f, LightZone.y - 0.6f) })
            {
                Vector3 foot = Top(b.x, b.y);
                beams.Tube(new List<Vector3> { foot, foot + new Vector3(-1.2f, 9f, 0.6f) }, u => Mathf.Lerp(0.25f, 0.55f, u), 14, 0f, 1f, 0.5f);
            }
            Part("Light Beams", group, beams.Build("Beams"), beam).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        Prefab("Decor_Halos_0", group, Top(LightZone.x - 2.8f, LightZone.y - 0.2f), 20f, 2.2f);
        Prefab("Decor_Halos_2", group, Top(LightZone.x + 2.8f, LightZone.y + 1.6f), 110f, 1.6f);
        PointLight("Light White", group, Top(LightZone.x, LightZone.y) + Vector3.up * 2.5f, new Color(0.9f, 0.95f, 1f), 5f, 8f);
    }

    // ---------------- Back left: green growth ----------------

    private static void Growth()
    {
        var group = Group("Green Growth (back left)");
        var random = new Random(48);

        // Green blades round the spring.
        var kelp = new RealmMeshes();
        Vector2[] clumps = { new Vector2(-6.6f, 4.6f), new Vector2(-3.0f, 4.4f), new Vector2(-6.9f, 1.8f), new Vector2(-2.6f, 2.0f) };
        foreach (Vector2 clump in clumps)
            for (int b = random.Next(5, 9); b > 0; b--) Blade(kelp, Top(clump.x + Lerp(random, -0.6f, 0.6f), clump.y + Lerp(random, -0.6f, 0.6f)), random);
        Part("Green Blades", group, kelp.Build("Green Blades"), Variant("MAT_Realm_Kelp", "HB_GreenKelp", m =>
        {
            m.SetColor("_Root", new Color(0.04f, 0.25f, 0.16f));
            m.SetColor("_Body", new Color(0.2f, 0.9f, 0.5f));
            m.SetColor("_Tip", new Color(0.8f, 1f, 0.35f));
            m.SetColor("_Rib", new Color(0.02f, 0.08f, 0.06f));
            m.SetColor("_Bead", new Color(0.9f, 1f, 0.7f));
            m.SetFloat("_Translucency", 1.6f);
            m.SetFloat("_Cull", 0f);
        }));

        // Moss carpet round the spring: many small glowing lumps, thinning out away from the water.
        var moss = new RealmMeshes();
        for (int i = 0; i < 620; i++)
        {
            Vector2 p = GrowthZone + new Vector2(Lerp(random, -3.4f, 3.2f), Lerp(random, -2.4f, 2.6f));
            float spring = new Vector2(p.x - Spring.x, p.y - Spring.y).magnitude;
            if (spring < SpringRadius * 0.75f || StreamDistance(p.x, p.y) < 0.4f) continue;
            if (random.NextDouble() > Mathf.InverseLerp(3.8f, 1f, spring)) continue;
            moss.Sphere(Top(p.x, p.y), new Vector3(Lerp(random, 0.06f, 0.16f), Lerp(random, 0.05f, 0.12f), Lerp(random, 0.06f, 0.16f)), Quaternion.identity, 4, 6, 1f, (float)random.NextDouble());
        }
        Part("Moss", group, moss.Build("Moss"), Mat("MAT_Realm_Slime"));

        // Pale mushrooms with green-glowing gills.
        var mushrooms = new RealmMeshes();
        for (int i = 0; i < 8; i++) RealmFlora.Mushroom(mushrooms, Scatter(-6.4f, 3.3f, 0.7f, random), Vector3.up, Lerp(random, 0.9f, 1.8f), random);
        for (int i = 0; i < 6; i++) RealmFlora.Mushroom(mushrooms, Scatter(-2.2f, 3.4f, 0.6f, random), Vector3.up, Lerp(random, 0.8f, 1.5f), random);
        Part("Mushrooms", group, mushrooms.Build("Mushrooms"), Variant("MAT_Realm_MushroomBlue", "HB_GrowthMushroom", m =>
        {
            m.SetColor("_Top", new Color(0.88f, 0.95f, 0.86f));
            m.SetColor("_Shade", new Color(0.4f, 0.55f, 0.45f));
            m.SetColor("_Under", new Color(0.55f, 1f, 0.45f));
            m.SetColor("_Rim", new Color(0.85f, 1f, 0.8f));
        }));

        // The kiln's designed flora, in the same language as on the mother's bones, growing out of the moss.
        var designed = new BoneFlora(77);
        for (int i = 0; i < 9; i++)
        {
            Vector2 p = GrowthZone + new Vector2(Lerp(random, -2.8f, 2.6f), Lerp(random, -2f, 2.2f));
            if ((p - Spring).magnitude < SpringRadius || StreamDistance(p.x, p.y) < 0.5f) continue;
            designed.Cluster(Top(p.x, p.y), Vector3.up, Lerp(random, 0.35f, 0.6f));
        }
        GameObject floraObject = designed.Attach(group, ShaderAsset("Capstone/Landmarks/Flora", "HB_Flora", null), "Designed Flora");
        if (floraObject != null) floraObject.GetComponent<MeshFilter>().sharedMesh.hideFlags = HideFlags.None;

        var anemone = new RealmMeshes();
        Anemone(anemone, Top(-5.8f, 0.9f), random);
        Anemone(anemone, Top(-3.6f, 5.2f), random);
        Part("Anemones", group, anemone.Build("Anemones"), Mat("MAT_Realm_Anemone"));
        PointLight("Growth Green", group, Top(GrowthZone.x, GrowthZone.y) + Vector3.up * 2f, new Color(0.35f, 1f, 0.5f), 5f, 7f);
    }

    // One blade: a wide ribbon rising in a lazy S and twisting as it goes.
    private static void Blade(RealmMeshes mesh, Vector3 foot, Random random)
    {
        float height = Lerp(random, 1.4f, 3.4f), width = Lerp(random, 0.1f, 0.2f), lean = Lerp(random, 0f, Mathf.PI * 2f), twist = Lerp(random, 1.5f, 3.5f);
        var path = new List<Vector3>();
        var up = new List<Vector3>();
        Vector3 leanDir = new Vector3(Mathf.Cos(lean), 0f, Mathf.Sin(lean));
        for (int k = 0; k <= 12; k++)
        {
            float u = k / 12f;
            path.Add(foot - Vector3.up * 0.05f + Vector3.up * u * height + leanDir * Mathf.Sin(u * Mathf.PI * 1.2f) * 0.35f * u);
            float a = u * twist + lean;
            up.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
        }
        mesh.Ribbon(path, up, width, 2, (float)random.NextDouble());
    }

    // ---------------- Poster dressing: each region's own bold colours and patterns ----------------

    // Flat poster colours, outlined in ink, each region with its own palette and pattern family
    // (after Daniel's poster reference): bone = cream / black stripes and polka dots with salmon and teal,
    // heat = crimson / mustard waves and leopard spots, light = teal / cream / gold mosaic, halftone and stars,
    // growth = olive / forest / mustard terrazzo and stripes with coral.
    private enum Pattern { Stripes, Dots, Mosaic, Waves, Spots, Stars }

    private static Material Poster(string name, Pattern pattern, Color a, Color b, Color c, float scale, float fill, float accent, float seed)
    {
        return ShaderAsset("Capstone/ArtDirection/PosterPattern", "HB_Poster_" + name, m =>
        {
            m.SetFloat("_Pattern", (int)pattern);
            m.SetColor("_ColorA", a);
            m.SetColor("_ColorB", b);
            m.SetColor("_ColorC", c);
            m.SetColor("_InkColor", new Color(0.03f, 0.03f, 0.04f));
            m.SetFloat("_Scale", scale);
            m.SetFloat("_Fill", fill);
            m.SetFloat("_Accent", accent);
            m.SetFloat("_Seed", seed);
            m.SetFloat("_Outline", 0.025f);
        });
    }

    private static readonly Color Cream = new Color(0.95f, 0.9f, 0.8f), Ink = new Color(0.04f, 0.04f, 0.05f);
    private static readonly Color Salmon = new Color(0.95f, 0.52f, 0.47f), Teal = new Color(0.16f, 0.58f, 0.62f);
    private static readonly Color Crimson = new Color(0.78f, 0.1f, 0.12f), Mustard = new Color(0.94f, 0.68f, 0.12f), Orange = new Color(0.98f, 0.42f, 0.1f);
    private static readonly Color SkyTeal = new Color(0.3f, 0.62f, 0.72f), Gold = new Color(0.96f, 0.76f, 0.26f);
    private static readonly Color Olive = new Color(0.48f, 0.55f, 0.2f), Forest = new Color(0.1f, 0.36f, 0.22f), Coral = new Color(0.95f, 0.43f, 0.33f);

    private static void PosterDressing()
    {
        // Bone mother: a striped arch behind her skull, polka-dot stones, a teal mosaic step.
        var bone = Group("Poster: Bone Mother", root);
        Material boneStripes = Poster("BoneStripes", Pattern.Stripes, Cream, Ink, Salmon, 2.2f, 0.45f, 0.18f, 1f);
        Material boneDots = Poster("BoneDots", Pattern.Dots, Cream, Ink, Salmon, 3f, 0.5f, 0.25f, 2f);
        Material boneMosaic = Poster("BoneMosaic", Pattern.Mosaic, Teal, Cream, Salmon, 2.5f, 0.5f, 0f, 3f);
        Arch(bone, "Striped Arch", Top(-7.3f, -3.1f), 70f, 2.1f, 0.2f, boneStripes);
        Stone(bone, "Dot Stone", new Vector2(-7.2f, -5.2f), 0.7f, 11, boneDots);
        Stone(bone, "Dot Stone", new Vector2(-5.3f, -0.4f), 0.5f, 12, boneDots);
        Stone(bone, "Dot Stone", new Vector2(-3.7f, -3.9f), 0.35f, 13, boneDots);
        Step(bone, "Mosaic Step", new Vector2(-1.3f, -4.7f), new Vector3(1.4f, 0.5f, 1.1f), 15f, boneMosaic);
        Step(bone, "Mosaic Step", new Vector2(-1.0f, -4.4f), new Vector3(0.8f, 0.9f, 0.7f), 15f, boneMosaic);

        // Heat: wave-banded domes, leopard-spot pillars, a striped ring hanging over the crater.
        var heat = Group("Poster: Heat", root);
        Material heatWaves = Poster("HeatWaves", Pattern.Waves, Crimson, Ink, Orange, 2.4f, 0.5f, 0f, 4f);
        Material heatSpots = Poster("HeatSpots", Pattern.Spots, Crimson, Ink, Orange, 3.2f, 0.7f, 0.5f, 5f);
        Material heatRing = Poster("HeatRing", Pattern.Stripes, Crimson, Ink, Mustard, 3f, 0.5f, 0.2f, 6f);
        Stone(heat, "Wave Dome", new Vector2(6.9f, -4.9f), 0.7f, 21, heatWaves);
        Stone(heat, "Wave Dome", new Vector2(2.3f, -4.9f), 0.6f, 22, heatWaves);
        Stone(heat, "Wave Dome", new Vector2(7.2f, -0.6f), 0.5f, 23, heatWaves);
        Pillar(heat, "Spot Pillar", new Vector2(6.9f, -2.3f), 0.24f, 1.6f, heatSpots);
        Pillar(heat, "Spot Pillar", new Vector2(2.0f, -1.6f), 0.18f, 1.0f, heatSpots);
        Pillar(heat, "Spot Pillar", new Vector2(5.2f, -5.2f), 0.2f, 0.9f, heatSpots);
        GameObject ring = Part("Striped Ring", heat, Torus(1.7f, 0.14f, 0f, 360f), heatRing);
        ring.transform.SetPositionAndRotation(Top(HeatZone.x, HeatZone.y) + Vector3.up * 3.6f, Quaternion.Euler(70f, 20f, 0f));

        // Light: a starfield monolith, mosaic steps up the ridge, halftone discs standing like suns.
        var light = Group("Poster: Light", root);
        Material lightStars = Poster("LightStars", Pattern.Stars, new Color(0.03f, 0.04f, 0.08f), Color.white, Gold, 2.5f, 0.5f, 0f, 7f);
        Material lightMosaic = Poster("LightMosaic", Pattern.Mosaic, SkyTeal, Cream, Gold, 2.8f, 0.5f, 0f, 8f);
        Material lightDots = Poster("LightDots", Pattern.Dots, Cream, SkyTeal, Gold, 4f, 0.55f, 0.2f, 9f);
        Step(light, "Star Monolith", new Vector2(7.4f, 5.3f), new Vector3(0.35f, 2.4f, 1.1f), -30f, lightStars);
        Step(light, "Mosaic Step", new Vector2(2.2f, 5.1f), new Vector3(1.5f, 0.45f, 1.1f), 10f, lightMosaic);
        Step(light, "Mosaic Step", new Vector2(2.5f, 5.2f), new Vector3(1f, 0.9f, 0.8f), 10f, lightMosaic);
        Step(light, "Mosaic Step", new Vector2(2.8f, 5.3f), new Vector3(0.55f, 1.35f, 0.5f), 10f, lightMosaic);
        Disc(light, "Halftone Sun", Top(3.2f, 2.6f) + Vector3.up * 0.85f, 0.7f, Quaternion.Euler(0f, -40f, 0f), lightDots);
        Disc(light, "Halftone Sun", Top(6.6f, 1.6f) + Vector3.up * 0.55f, 0.42f, Quaternion.Euler(0f, -10f, 0f), lightDots);

        // Growth: terrazzo boulders, striped pods, a coral and green mosaic step by the spring.
        var growth = Group("Poster: Growth", root);
        Material growthSpots = Poster("GrowthTerrazzo", Pattern.Spots, Mustard, Forest, Coral, 3.4f, 0.65f, 0.45f, 10f);
        Material growthStripes = Poster("GrowthStripes", Pattern.Stripes, Olive, Forest, Mustard, 3f, 0.5f, 0.2f, 11f);
        Material growthMosaic = Poster("GrowthMosaic", Pattern.Mosaic, Olive, Forest, Coral, 3f, 0.5f, 0f, 12f);
        Stone(growth, "Terrazzo Boulder", new Vector2(-7.3f, 5.3f), 0.75f, 31, growthSpots);
        Stone(growth, "Terrazzo Boulder", new Vector2(-1.5f, 5.3f), 0.55f, 32, growthSpots);
        Stone(growth, "Terrazzo Boulder", new Vector2(-0.8f, 2.7f), 0.4f, 33, growthSpots);
        Pod(growth, "Striped Pod", new Vector2(-7.4f, 3.3f), 0.45f, 1.6f, 34, growthStripes);
        Pod(growth, "Striped Pod", new Vector2(-4.1f, 1.1f), 0.35f, 1.2f, 35, growthStripes);
        Pod(growth, "Striped Pod", new Vector2(-2.0f, 0.4f), 0.25f, 0.8f, 36, growthStripes);
        Step(growth, "Mosaic Step", new Vector2(-3.0f, 2.2f), new Vector3(1.2f, 0.4f, 0.9f), -25f, growthMosaic);
    }

    private static void Stone(Transform parent, string name, Vector2 at, float radius, int seed, Material material)
    {
        GameObject go = Part(name, parent, BoneShapes.Lump(radius, seed), material);
        go.transform.SetPositionAndRotation(Top(at.x, at.y) + Vector3.up * radius * 0.25f, Quaternion.Euler(0f, seed * 37f, 0f));
        go.transform.localScale = new Vector3(1f, 0.8f, 1f);
    }

    private static void Pod(Transform parent, string name, Vector2 at, float radius, float height, int seed, Material material)
    {
        GameObject go = Part(name, parent, BoneShapes.Lump(radius, seed), material);
        go.transform.SetPositionAndRotation(Top(at.x, at.y) + Vector3.up * height * 0.45f, Quaternion.Euler(seed * 3f % 12f, seed * 37f, 0f));
        go.transform.localScale = new Vector3(1f, height / (radius * 2f), 1f);
    }

    private static void Pillar(Transform parent, string name, Vector2 at, float radius, float height, Material material)
    {
        GameObject go = Part(name, parent, Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), material);
        go.transform.SetPositionAndRotation(Top(at.x, at.y) + Vector3.up * (height * 0.5f - 0.1f), Quaternion.identity);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
    }

    private static void Step(Transform parent, string name, Vector2 at, Vector3 size, float yaw, Material material)
    {
        GameObject go = Part(name, parent, Resources.GetBuiltinResource<Mesh>("Cube.fbx"), material);
        go.transform.SetPositionAndRotation(Top(at.x, at.y) + Vector3.up * (size.y * 0.5f - 0.08f), Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = size;
    }

    private static void Disc(Transform parent, string name, Vector3 centre, float radius, Quaternion facing, Material material)
    {
        GameObject go = Part(name, parent, Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), material);
        go.transform.SetPositionAndRotation(centre, facing * Quaternion.Euler(90f, 0f, 0f));
        go.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
    }

    private static void Arch(Transform parent, string name, Vector3 foot, float yaw, float radius, float thickness, Material material)
    {
        GameObject go = Part(name, parent, Torus(radius, thickness, 0f, 180f), material);
        go.transform.SetPositionAndRotation(foot - Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));
    }

    // A torus (or a part of one, from a0 to a1 degrees) in the XY plane, round the origin.
    private static Mesh Torus(float radius, float thickness, float a0, float a1)
    {
        const int along = 48, round = 12;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= along; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)along) * Mathf.Deg2Rad;
            Vector3 centre = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            Vector3 outward = centre.normalized;
            for (int k = 0; k <= round; k++)
            {
                float b = k / (float)round * Mathf.PI * 2f;
                Vector3 n = outward * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                vertices.Add(centre + n * thickness);
                normals.Add(n);
            }
        }
        for (int i = 0; i < along; i++)
            for (int k = 0; k < round; k++)
            {
                int a = i * (round + 1) + k, b = a + round + 1;
                triangles.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
            }
        var mesh = new Mesh { name = "Torus" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------- Effects: heat, light, time (HabitatFX shader, animated) ----------------

    private enum Fx { HeatColumn, LightShaft, TimeRings, Sparks, Ripples, QuantumFoam, Tide, Diffraction, Veil, Starburst, Caustics, Halo, Leaf }

    private static Material FxMaterial(string name, Fx mode, Color a, Color b, float intensity, float speed, float count, float fill, float size, float rise, float seed)
    {
        return ShaderAsset("Capstone/ArtDirection/HabitatFX", "HB_FX_" + name, m =>
        {
            m.SetFloat("_Mode", (int)mode);
            m.SetColor("_Color", a);
            m.SetColor("_Color2", b);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Speed", speed);
            m.SetFloat("_Count", count);
            m.SetFloat("_Fill", fill);
            m.SetFloat("_Size", size);
            m.SetFloat("_Rise", rise);
            m.SetFloat("_Seed", seed);
        });
    }

    private static void Effects()
    {
        var group = Group("Effects (heat, light, time)");

        // Heat: hot air climbing out of the crater in two nested columns, embers floating up.
        Vector3 crater = Top(HeatZone.x, HeatZone.y);
        Part("Embers", group, Sparks(crater, 1.6f, 0.2f, 140, 51), FxMaterial("Embers", Fx.Sparks, new Color(1f, 0.1f, 0.04f) * 2.2f, new Color(1f, 0.55f, 0.25f) * 2f, 1.2f, 0.6f, 0f, 0f, 0.07f, 5f, 2f))
            .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Light: soft shafts falling onto the ridge, and starry flares twinkling on the crystals.
        Material shaft = FxMaterial("LightShaft", Fx.LightShaft, new Color(1f, 0.97f, 0.85f), new Color(0.6f, 0.85f, 1f), 0.8f, 1f, 0f, 0f, 0f, 0f, 3f);
        Vector3 sunward = new Vector3(-0.35f, 1f, 0.25f).normalized;
        foreach (Vector2 at in new[] { new Vector2(LightZone.x + 0.6f, LightZone.y + 0.9f), new Vector2(LightZone.x - 1.6f, LightZone.y - 0.4f), new Vector2(LightZone.x + 2.3f, LightZone.y - 1.2f) })
        {
            Vector3 foot = Top(at.x, at.y);
            Part("Light Shaft", group, Cone(foot + sunward * 9f, foot, 1.5f, 0.45f), shaft).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        Part("Crystal Flares", group, Sparks(Top(LightZone.x + 0.6f, LightZone.y + 0.6f) + Vector3.up * 1.2f, 2.8f, 1.6f, 70, 52),
            FxMaterial("Flares", Fx.Sparks, new Color(0.7f, 0.9f, 1f) * 2f, Color.white * 3f, 1f, 1f, 0f, 0f, 0.16f, 0f, 4f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        Vector3 heart = MotherSpine(0.45f);

        // Ground pools under each region, overlapping so neighbours bleed together (the main effects are in Optics):
        // heat = jittering foam whose cell walls split into colour fringes, light = faint diffraction rings,
        // growth = soft green ripples.
        Ripples(group, "Heat Foam", HeatZone, 5f, FxMaterial("HeatFoam", Fx.QuantumFoam,
            new Color(1f, 0.06f, 0.03f), new Color(1f, 0.6f, 0.3f), 0.5f, 1f, 11f, 1.6f, 0f, 0f, 6f));
        Ripples(group, "Light Diffraction", LightZone, 5.5f, FxMaterial("LightDiffraction", Fx.Diffraction,
            new Color(1f, 0.9f, 0.7f), new Color(0.9f, 0.95f, 1f), 0.2f, 1f, 9f, 0f, 16f, 0f, 7f));
        Ripples(group, "Growth Pool", GrowthZone, 5.5f, FxMaterial("GrowthPool", Fx.Ripples,
            new Color(0.2f, 0.9f, 0.3f) * 0.8f, new Color(0.8f, 1f, 0.3f), 0.5f, 0.3f, 5f, 0.45f, 3f, 0f, 8f));
        Part("Time Motes", group, Sparks(heart, 3.2f, 1.8f, 60, 53), FxMaterial("TimeMotes", Fx.Sparks, new Color(0.2f, 0.9f, 1f) * 2f, new Color(0.85f, 1f, 1f) * 2f, 0.8f, 0.25f, 0f, 0f, 0.08f, 1.2f, 6f))
            .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // A disc lying on the ground round centre (radius in metres), cut off at the block's edges.
    private static void Ripples(Transform parent, string name, Vector2 centre, float radius, Material material)
    {
        const int rings = 40, round = 96;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var inside = new List<bool>();
        for (int j = 0; j <= rings; j++)
            for (int i = 0; i <= round; i++)
            {
                float r = j / (float)rings, a = i / (float)round * Mathf.PI * 2f;
                // Clamped to the block, so the pool ends cleanly at its edges.
                float x = Mathf.Clamp(centre.x + Mathf.Cos(a) * r * radius, -HalfX, HalfX), z = Mathf.Clamp(centre.y + Mathf.Sin(a) * r * radius, -HalfZ, HalfZ);
                vertices.Add(new Vector3(x, Height(x, z) + 0.05f, z));
                uvs.Add(new Vector2(i / (float)round, r));
                inside.Add(true);
            }
        var triangles = new List<int>();
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < round; i++)
            {
                int a = j * (round + 1) + i, b = a + round + 1;
                if (!inside[a] || !inside[a + 1] || !inside[b] || !inside[b + 1]) continue;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Part(name, parent, mesh, material).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // A sheet hovering lift metres over the ground across area (x, z, width, depth); uv.y runs from the back
    // edge (0) to the front (1), the way the tide flows.
    private static void Tide(Transform parent, string name, Rect area, float lift, Material material)
    {
        const int nx = 70, nz = 70;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = i / (float)nx, v = j / (float)nz;
                float x = Mathf.Lerp(area.xMin, area.xMax, u), z = Mathf.Lerp(area.yMax, area.yMin, v);
                vertices.Add(new Vector3(x, Height(x, z) + lift, z));
                uvs.Add(new Vector2(u, v));
            }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + nx + 1;
                triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(new Vector3(area.center.x, lift + 1f, area.center.y), new Vector3(area.width, 6f, area.height));
        Part(name, parent, mesh, material).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // An open tube from base up height, narrowing from r0 to r1 and leaning a little (uv.y 0 bottom .. 1 top).
    private static Mesh Tube(Vector3 basePoint, float r0, float r1, float height, float lean)
    {
        const int round = 32, along = 16;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j <= along; j++)
        {
            float v = j / (float)along, r = Mathf.Lerp(r0, r1, v);
            for (int i = 0; i <= round; i++)
            {
                float a = i / (float)round * Mathf.PI * 2f;
                Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(basePoint + n * r + Vector3.up * v * height + Vector3.right * lean * v * v);
                normals.Add(n);
                uvs.Add(new Vector2(i / (float)round, v));
            }
        }
        for (int j = 0; j < along; j++)
            for (int i = 0; i < round; i++)
            {
                int a = j * (round + 1) + i, b = a + round + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = "Tube" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A cone from top (radius r0) to foot (radius r1), uv.y 0 at the top .. 1 at the foot.
    private static Mesh Cone(Vector3 top, Vector3 foot, float r0, float r1)
    {
        const int round = 32, along = 10;
        Vector3 axis = (foot - top).normalized;
        Vector3 u = Vector3.Cross(axis, Vector3.forward).normalized, w = Vector3.Cross(axis, u);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j <= along; j++)
        {
            float v = j / (float)along, r = Mathf.Lerp(r0, r1, v);
            Vector3 c = Vector3.Lerp(top, foot, v);
            for (int i = 0; i <= round; i++)
            {
                float a = i / (float)round * Mathf.PI * 2f;
                Vector3 n = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                vertices.Add(c + n * r);
                normals.Add(n);
                uvs.Add(new Vector2(i / (float)round, v));
            }
        }
        for (int j = 0; j < along; j++)
            for (int i = 0; i < round; i++)
            {
                int a = j * (round + 1) + i, b = a + round + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = "Cone" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A flat ring in the XZ plane (uv.x round, uv.y across, uv1.x = turning speed).
    private static Mesh Annulus(float radius, float width, float speed)
    {
        const int round = 128;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var speeds = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i <= round; i++)
        {
            float a = i / (float)round * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int k = 0; k < 2; k++)
            {
                vertices.Add(d * (radius + (k == 0 ? -width : width)));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(i / (float)round, k));
                speeds.Add(new Vector2(speed, 0f));
            }
        }
        for (int i = 0; i < round; i++)
        {
            int a = i * 2;
            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        var mesh = new Mesh { name = "Ring" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, speeds);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Spark quads scattered in an ellipsoid round centre; all four corners of a quad sit on its centre and the
    // shader spreads them to face the camera (uv = corner, uv1 = random size, life offset).
    private static Mesh Sparks(Vector3 centre, float radius, float height, int count, int seed)
    {
        var random = new Random(seed);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var randoms = new List<Vector2>();
        var triangles = new List<int>();
        Vector2[] corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        for (int i = 0; i < count; i++)
        {
            Vector3 p = centre + Flat(random, radius) + Vector3.up * Lerp(random, 0.1f, height);
            Vector2 r = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            int first = vertices.Count;
            for (int k = 0; k < 4; k++) { vertices.Add(p); uvs.Add(corners[k]); randoms.Add(r); }
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
        var mesh = new Mesh { name = "Sparks" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, randoms);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // Room for the rise and the camera-facing spread.
        mesh.bounds = new Bounds(centre + Vector3.up * 3f, new Vector3(radius * 2f + 2f, height + 8f, radius * 2f + 2f));
        return mesh;
    }

    // ---------------- Optics: heat refracts, time is thin film, light diffracts ----------------

    // One optical language for the three elements, so they sit together: heat is a lens of molten, unstable
    // matter that really refracts what is behind it with dispersion; time is thin translucent veils whose
    // folds glow (light path through a film grows as 1 / |N.V|), flowing out of the mother through the other
    // regions; light is diffraction starbursts on the lamps and crystals, every colour spread by its wavelength.
    private static readonly List<Vector3> LampTops = new List<Vector3>();

    private static void Optics()
    {
        var group = Group("Optics (heat lens, time veils, light bursts)");

        // Heat: a molten dome over the crater and a plume of blobs rising out of it, all refracting.
        Material lens = ShaderAsset("Capstone/ArtDirection/HeatLens", "HB_HeatLens", m =>
        {
            m.SetFloat("_Strength", 0.07f);
            m.SetFloat("_Dispersion", 0.5f);
            m.SetFloat("_Shimmer", 0.35f);
            m.SetFloat("_Melt", 0.12f);
            m.SetColor("_Glow", new Color(1f, 0.12f, 0.04f) * 1.4f);
            m.SetColor("_Highlight", new Color(1f, 0.82f, 0.82f) * 1.2f);
            m.SetColor("_Tint", new Color(1f, 0.78f, 0.72f));
        });
        Vector3 crater = Top(HeatZone.x, HeatZone.y);
        (Vector3 offset, Vector3 size)[] blobs =
        {
            (new Vector3(0f, 0.15f, 0f), new Vector3(2.6f, 1.0f, 2.3f)),
            (new Vector3(0.2f, 1.5f, 0.1f), new Vector3(1.3f, 1.7f, 1.2f)),
            (new Vector3(0.55f, 3.0f, 0.3f), new Vector3(0.9f, 1.3f, 0.85f)),
            (new Vector3(1.0f, 4.2f, 0.5f), new Vector3(0.55f, 0.8f, 0.5f)),
            (new Vector3(-1.6f, 1.1f, 0.8f), new Vector3(0.45f, 0.5f, 0.45f)),
        };
        foreach (var blob in blobs)
        {
            GameObject go = Part("Heat Lens", group, BuiltinSphere(), lens);
            go.transform.SetPositionAndRotation(crater + blob.offset, Quaternion.Euler(0f, blob.offset.y * 40f, 8f));
            go.transform.localScale = blob.size;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Time: veils flowing out of the mother, over the stream and on into the other regions.
        Vector3 Ground(float x, float z, float lift) => new Vector3(x, Height(x, z) + lift, z);
        (Vector3[] path, float width, Color a, Color b)[] veils =
        {
            (new[] { Ground(-7f, -4.5f, 1.2f), Ground(-4.5f, -2.5f, 2.6f), Ground(-1.5f, -1.8f, 2.0f), Ground(1.8f, -2.6f, 1.6f), Ground(4.5f, -2.0f, 3.0f) },
                1.3f, new Color(0.05f, 0.35f, 1f), new Color(0.3f, 0.9f, 1f) * 1.4f),
            (new[] { Ground(-6.5f, -1.5f, 0.6f), Ground(-4f, -0.5f, 1.4f), Ground(-1.8f, 1.2f, 1.8f), Ground(-3.5f, 3.6f, 1.6f), Ground(-6.5f, 4.5f, 2.2f) },
                1.0f, new Color(0.05f, 0.5f, 0.6f), new Color(0.6f, 1f, 0.35f) * 1.3f),
            (new[] { Ground(-5.5f, -5.2f, 0.4f), Ground(-3f, -3.5f, 1.0f), Ground(0.2f, -0.4f, 2.4f), Ground(2.6f, 2.2f, 2.8f), Ground(5.2f, 3.6f, 3.6f) },
                1.5f, new Color(0.1f, 0.3f, 0.9f), new Color(1f, 0.8f, 0.25f) * 1.4f),
            (new[] { Ground(-7.5f, -2.8f, 2.4f), Ground(-5f, -4f, 3.4f), Ground(-2.4f, -4.8f, 2.6f), Ground(0.4f, -5.2f, 1.2f) },
                0.8f, new Color(0.1f, 0.6f, 1f), new Color(0.75f, 1f, 1f) * 1.4f),
        };
        for (int v = 0; v < veils.Length; v++)
        {
            Material veil = FxMaterial("Veil" + v, Fx.Veil, veils[v].a, veils[v].b, 1f, 0.7f + v * 0.15f, 0f, 0f, 0f, 0.35f, 10f + v);
            Part("Time Veil", group, Veil(CatmullRom(veils[v].path.ToList(), 16), veils[v].width, v * 0.37f), veil).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Light: diffraction bursts on the lamp orbs and the tallest crystals, and a big one where the shafts land.
        var bursts = new List<(Vector3 at, float size)>();
        foreach (Vector3 lamp in LampTops) bursts.Add((lamp, 1.6f));
        bursts.Add((Top(LightZone.x + 1.2f, LightZone.y + 1.2f) + Vector3.up * 2.2f, 2.4f));
        bursts.Add((Top(LightZone.x + 2.4f, LightZone.y - 1.4f) + Vector3.up * 1.2f, 1.1f));
        bursts.Add((Top(LightZone.x + 0.6f, LightZone.y + 0.9f) + Vector3.up * 0.4f, 3.2f));
        Part("Light Bursts", group, BurstQuads(bursts), FxMaterial("Starburst", Fx.Starburst, new Color(1f, 0.35f, 0.25f), new Color(0.6f, 0.8f, 1f) * 1.2f, 1f, 1f, 0f, 0f, 1f, 0f, 12f))
            .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // A veil: a wide strip along path, standing mostly upright and twisting slowly, uv.x across, uv.y metres along.
    private static Mesh Veil(List<Vector3> path, float width, float twist)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var extra = new List<Vector2>();
        var triangles = new List<int>();
        float length = 0f;
        var along = new List<float>();
        for (int i = 0; i < path.Count; i++) { if (i > 0) length += Vector3.Distance(path[i - 1], path[i]); along.Add(length); }
        const int across = 8;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 forward = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Quaternion.AngleAxis(Mathf.Sin(along[i] * 0.4f + twist * 10f) * 50f, forward) * Vector3.up;
            for (int k = 0; k <= across; k++)
            {
                float u = k / (float)across;
                // The sheet curls a little across its width, so its folds catch the light.
                vertices.Add(path[i] + up * (u - 0.5f) * width + side * Mathf.Sin(u * Mathf.PI) * width * 0.25f);
                uvs.Add(new Vector2(u, along[i]));
                extra.Add(new Vector2(twist, length));
            }
        }
        for (int i = 0; i + 1 < path.Count; i++)
            for (int k = 0; k < across; k++)
            {
                int a = i * (across + 1) + k, b = a + across + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = "Veil" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, extra);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Bounds b2 = mesh.bounds;
        b2.Expand(2f);
        mesh.bounds = b2;
        return mesh;
    }

    // One camera-facing quad per burst (the shader spreads the corners); uv1 = random, phase.
    private static Mesh BurstQuads(List<(Vector3 at, float size)> bursts)
    {
        var random = new Random(71);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var extra = new List<Vector2>();
        var triangles = new List<int>();
        Vector2[] corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        var bounds = new Bounds();
        bool first = true;
        foreach (var burst in bursts)
        {
            // The material's size is 1 m; each burst's own size goes into the quad by repeating it in uv1.x.
            Vector2 r = new Vector2(Mathf.Clamp01((burst.size - 0.7f) / 0.6f * 0.5f), (float)random.NextDouble());
            int start = vertices.Count;
            for (int k = 0; k < 4; k++) { vertices.Add(burst.at); uvs.Add(corners[k]); extra.Add(new Vector2(burst.size, r.y)); }
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            if (first) { bounds = new Bounds(burst.at, Vector3.one * burst.size * 2f); first = false; }
            else bounds.Encapsulate(new Bounds(burst.at, Vector3.one * burst.size * 2f));
        }
        var mesh = new Mesh { name = "Bursts" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, extra);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        bounds.Expand(1f);
        mesh.bounds = bounds;
        return mesh;
    }

    // ---------------- Crystal optics: specular gems and fluorescent minerals ----------------

    // Applies the crystal pass to the saved (or open) habitat scene without rebuilding it, so hand edits stay.
    private static void RefineOpticsInScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
        Transform block = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Habitat Block");
        if (block == null) throw new InvalidOperationException("Habitat Block root not found in Scene_HabitatBlock.");
        Scene active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        Vector3 at = block.position;
        block.position = Vector3.zero;
        root = block;
        RefineOptics();
        block.position = at;
        foreach (Transform t in block.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (active.IsValid() && active != scene) SceneManager.SetActiveScene(active);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    private static Material CrystalMaterial(string name, bool fluorescent, Color tint, Color glow, float glowAmount, float f0, float roughness, float refraction, float dispersion, float absorb)
    {
        return ShaderAsset("Capstone/ArtDirection/Crystal", "HB_Crystal_" + name, m =>
        {
            m.SetFloat("_Kind", fluorescent ? 1f : 0f);
            m.SetColor("_Tint", tint);
            m.SetColor("_Glow", glow);
            m.SetFloat("_GlowAmount", glowAmount);
            m.SetFloat("_F0", f0);
            m.SetFloat("_Roughness", roughness);
            m.SetFloat("_Refraction", refraction);
            m.SetFloat("_Dispersion", dispersion);
            m.SetFloat("_Absorb", absorb);
            m.SetFloat("_Facets", 1f);
        });
    }

    // No sparkles: light comes from crystals instead. Clear gems refract and disperse what is behind them and
    // carry sharp sun highlights; fluorescent minerals glow in their own colour from the light they take in.
    // The light region gets the clear gems, and fluorescent clusters sit on the borders so each neighbour's
    // colour is carried by a mineral: uranium green towards growth, fluorite violet towards the bone mother,
    // apatite cyan by the stream; the cave crystals turn fluorescent too.
    private static void RefineOptics()
    {
        foreach (string name in new[] { "Crystal Flares", "Light Bursts", "Time Motes", "Embers", "Crystal Optics" })
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToList())
                if (t != null) Object.DestroyImmediate(t.gameObject);

        Material shaft = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/HB_FX_LightShaft.mat");
        if (shaft != null) { shaft.SetFloat("_Dust", 0f); EditorUtility.SetDirty(shaft); }

        Material clear = CrystalMaterial("Clear", false, new Color(0.86f, 0.93f, 1f), Color.black, 0f, 0.17f, 0.04f, 0.09f, 0.6f, 1.2f);
        Material smoke = CrystalMaterial("Smoky", false, new Color(0.7f, 0.66f, 0.78f), Color.black, 0f, 0.12f, 0.08f, 0.07f, 0.45f, 2f);
        Material uranium = CrystalMaterial("Uranium", true, new Color(0.75f, 0.95f, 0.6f), new Color(0.35f, 1f, 0.25f), 3.2f, 0.06f, 0.08f, 0.04f, 0.2f, 1f);
        Material fluorite = CrystalMaterial("Fluorite", true, new Color(0.75f, 0.65f, 1f), new Color(0.55f, 0.25f, 1f), 3.4f, 0.06f, 0.08f, 0.04f, 0.2f, 1f);
        Material apatite = CrystalMaterial("Apatite", true, new Color(0.65f, 0.9f, 1f), new Color(0.15f, 0.75f, 1f), 3f, 0.06f, 0.1f, 0.04f, 0.2f, 1f);

        void Swap(string name, Material material)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true).Where(r => r.name == name || (r.transform.parent != null && r.transform.parent.name == name)))
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
        }
        Swap("Crystals", clear);
        Swap("Frost Crystals", smoke);
        Swap("Film Crystals", fluorite);
        Swap("Lamp Orbs", apatite);
        foreach (string cave in new[] { "Decor_Crystals_0", "Decor_Crystals_3" }) Swap(cave, fluorite);
        foreach (string cave in new[] { "Decor_GlowSacs_0", "Decor_GlowSacs_2" }) Swap(cave, apatite);

        var group = Group("Crystal Optics");
        var random = new Random(404);
        void Cluster(string name, Vector2 at, int count, float size, Material material, Color light)
        {
            var mesh = new RealmMeshes();
            CrystalCluster(mesh, Top(at.x, at.y), count, size, random);
            Mesh built = mesh.Build(name);
            Part(name, group, built, material).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (light.maxColorComponent > 0f)
            {
                // Fluorescent minerals bleed light into the air round them and light the ground in their colour.
                Part(name + " Halo", group, built, FxMaterial("Halo" + name.Replace(" ", ""), Fx.Halo, light * 0.4f, light * 1.2f, 0.5f, 1f, 0f, 0f, size * 0.05f, 0f, 0f))
                    .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                Ripples(group, name + " Glow", at, size * 2.6f, FxMaterial("Glow" + name.Replace(" ", ""), Fx.Ripples, light * 0.5f, light, 0.7f, 0.2f, 1.5f, 1.4f, 8f, 0f, 3f));
                PointLight(name + " Light", group, Top(at.x, at.y) + Vector3.up * size * 0.6f, light, 3.5f, size * 4f);
            }
            // Every crystal throws refracted light on the ground on its far side from the sun: caustics.
            Vector3 sunward = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(-0.4f, 0.8f, 0.4f);
            Vector2 away = new Vector2(-sunward.x, -sunward.z).normalized * size * 1.1f;
            Color tint = light.maxColorComponent > 0f ? light : new Color(1f, 0.97f, 0.9f);
            Ripples(group, name + " Caustics", at + away, size * 1.8f, FxMaterial("Caustics" + name.Replace(" ", ""), Fx.Caustics, tint * 1.4f, Color.white * 1.6f, 1.3f, 0.6f, 4f, 0.25f, 26f, 0f, at.x));
        }
        Cluster("Clear Gem", new Vector2(LightZone.x + 1.8f, LightZone.y + 0.2f), 9, 2.0f, clear, Color.clear);
        Cluster("Clear Gem", new Vector2(LightZone.x - 1.0f, LightZone.y + 1.6f), 5, 1.0f, clear, Color.clear);
        Cluster("Smoky Gem", new Vector2(LightZone.x + 3.0f, LightZone.y + 1.4f), 4, 0.9f, smoke, Color.clear);
        Cluster("Uranium Glass", new Vector2(-1.4f, 3.4f), 9, 1.9f, uranium, new Color(0.35f, 1f, 0.3f));
        Cluster("Uranium Glass", new Vector2(-2.2f, 5.0f), 4, 0.7f, uranium, new Color(0.35f, 1f, 0.3f));
        Cluster("Fluorite", new Vector2(-0.6f, -1.0f), 8, 1.6f, fluorite, new Color(0.55f, 0.3f, 1f));
        Cluster("Fluorite", new Vector2(-3.4f, -5.2f), 4, 0.7f, fluorite, new Color(0.55f, 0.3f, 1f));
        Cluster("Apatite", new Vector2(2.4f, 0.8f), 6, 1.3f, apatite, new Color(0.2f, 0.7f, 1f));
        LeafGrowth();
    }

    // Growth as backlit chlorophyll: a lime light table on the ground and large curled leaf membranes arching
    // over the spring, clear lime face-on and dark green edge-on (after Daniel's green reference).
    private static void LeafGrowth()
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Leaf Growth").ToList())
            if (t != null) Object.DestroyImmediate(t.gameObject);
        var group = Group("Leaf Growth");

        Material pool = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/HB_FX_GrowthPool.mat");
        if (pool != null)
        {
            pool.SetColor("_Color", new Color(0.45f, 1f, 0.05f));
            pool.SetColor("_Color2", new Color(0.85f, 1f, 0.2f));
            pool.SetFloat("_Intensity", 0.9f);
            pool.SetFloat("_Fill", 1.5f);
            pool.SetFloat("_Count", 2f);
            pool.SetFloat("_Size", 6f);
            EditorUtility.SetDirty(pool);
        }

        Material leaf = FxMaterial("Leaf", Fx.Leaf, new Color(0.01f, 0.12f, 0.02f), new Color(0.55f, 1f, 0.08f), 1.3f, 0.3f, 0f, 0.5f, 0f, 0f, 0f);
        leaf.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        leaf.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        leaf.renderQueue = 3010;

        var random = new Random(88);
        for (int k = 0; k < 7; k++)
        {
            float angle = k / 7f * Mathf.PI * 2f + Lerp(random, -0.3f, 0.3f);
            Vector2 foot = Spring + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Lerp(random, 1.4f, 2.6f);
            Vector3 basePoint = Top(foot.x, foot.y);
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float height = Lerp(random, 1.6f, 3.2f), width = Lerp(random, 0.9f, 1.6f), curl = Lerp(random, 0.6f, 1.3f);
            Part("Leaf Membrane", group, LeafMesh(basePoint, outward, height, width, curl), leaf).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // A leaf: rises from base, leans out and curls back over, widest past the middle, cupped across.
    private static Mesh LeafMesh(Vector3 basePoint, Vector3 outward, float height, float width, float curl)
    {
        const int along = 28, across = 12;
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j <= along; j++)
        {
            float v = j / (float)along, a = v * curl * Mathf.PI;
            Vector3 spine = basePoint + Vector3.up * Mathf.Sin(a) * height / Mathf.Max(0.5f, Mathf.Sin(Mathf.Min(a, Mathf.PI * 0.5f)) + 0.5f) * 0.9f
                            + outward * (1f - Mathf.Cos(a)) * height * 0.45f;
            float w = width * Mathf.Sin(Mathf.Clamp01(v * 1.15f) * Mathf.PI) * (0.4f + 0.6f * v);
            Vector3 normal = (Vector3.up * Mathf.Cos(a) - outward * Mathf.Sin(a)).normalized;
            for (int i = 0; i <= across; i++)
            {
                float u = i / (float)across, s = u * 2f - 1f;
                vertices.Add(spine + side * s * w + Vector3.Cross(side, normal).normalized * 0f + normal * (s * s) * w * 0.35f);
                uvs.Add(new Vector2(u, v * height));
            }
        }
        for (int j = 0; j < along; j++)
            for (int i = 0; i < across; i++)
            {
                int a0 = j * (across + 1) + i, b0 = a0 + across + 1;
                triangles.AddRange(new[] { a0, b0, a0 + 1, a0 + 1, b0, b0 + 1 });
            }
        var mesh = new Mesh { name = "Leaf" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }


    // ---------------- Weaving the regions together ----------------

    // Ties the pieces to each other and to the regions round them, so nothing stands on its own: the kiln's
    // flora grows on every poster stone, moss creeps out of the growth region over the mother's tail and the
    // stones near it, and molten cracks run from the crater to the stream and under the light ridge.
    private static void Weave()
    {
        var group = Group("Weave (links between regions)");
        var random = new Random(91);
        var flora = new BoneFlora(97);
        var moss = new RealmMeshes();

        foreach (Transform poster in root)
        {
            if (!poster.name.StartsWith("Poster:")) continue;
            foreach (Renderer piece in poster.GetComponentsInChildren<Renderer>())
            {
                Bounds b = piece.bounds;
                Vector2 at = new Vector2(b.center.x - Origin.x, b.center.z - Origin.z);
                Vector3 top = new Vector3(at.x, b.max.y - Origin.y - 0.05f, at.y);
                if (random.NextDouble() < 0.7) flora.Cluster(top, Vector3.up, Lerp(random, 0.25f, 0.45f));
                // Moss gathers round the feet of stones on the growth side of the block.
                float green = Mathf.InverseLerp(9f, 2f, (at - GrowthZone).magnitude);
                int lumps = Mathf.RoundToInt(green * 26f);
                for (int k = 0; k < lumps; k++)
                {
                    float angle = Lerp(random, 0f, Mathf.PI * 2f), reach = Mathf.Max(b.extents.x, b.extents.z) * Lerp(random, 0.9f, 1.6f);
                    Vector2 p = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                    moss.Sphere(Top(p.x, p.y), new Vector3(Lerp(random, 0.05f, 0.13f), Lerp(random, 0.04f, 0.1f), Lerp(random, 0.05f, 0.13f)), Quaternion.identity, 4, 6, 1f, (float)random.NextDouble());
                }
            }
        }

        // Moss creeping along the mother's tail and up her last vertebrae.
        for (int i = 0; i < 160; i++)
        {
            float t = Lerp(random, 0.55f, 1f);
            Vector3 s = MotherSpine(t);
            Vector2 p = new Vector2(s.x, s.z) + new Vector2(Lerp(random, -0.9f, 0.9f), Lerp(random, -0.9f, 0.9f)) * (1.4f - t);
            float y = random.NextDouble() < 0.3 ? s.y + Lerp(random, -0.15f, 0.2f) : Height(p.x, p.y);
            if (random.NextDouble() > Mathf.InverseLerp(0.5f, 1f, t)) continue;
            moss.Sphere(new Vector3(p.x, y, p.y), Vector3.one * Lerp(random, 0.05f, 0.12f), Quaternion.identity, 4, 6, 1f, (float)random.NextDouble());
        }
        Part("Creeping Moss", group, moss.Build("Creeping Moss"), Mat("MAT_Realm_Slime"));
        GameObject grown = flora.Attach(group, ShaderAsset("Capstone/Landmarks/Flora", "HB_Flora", null), "Flora On Stones");
        if (grown != null) grown.GetComponent<MeshFilter>().sharedMesh.hideFlags = HideFlags.None;

        // Molten cracks reaching out of the heat region: to the stream (where they meet the cyan veins) and
        // under the light ridge.
        var cracks = new RealmMeshes();
        Vector2[] targets = { new Vector2(-2.3f, -1.2f), new Vector2(-2.6f, -3.4f), new Vector2(3.6f, 1.6f), new Vector2(6.4f, 1.2f) };
        foreach (Vector2 target in targets)
        {
            Vector2 p = HeatZone;
            var crack = new List<Vector3>();
            for (int k = 0; k <= 40; k++)
            {
                float u = k / 40f;
                Vector2 q = Vector2.Lerp(HeatZone, target, u) + new Vector2(Mathf.Sin(u * 13f + target.x), Mathf.Cos(u * 11f + target.y)) * 0.25f * Mathf.Sin(u * Mathf.PI);
                crack.Add(new Vector3(q.x, Height(q.x, q.y) - 0.01f, q.y));
            }
            cracks.Tube(crack, u => Mathf.Lerp(0.05f, 0.01f, u), 5, 0f, 1f, (float)random.NextDouble(), 0.02f);
        }
        Material molten = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/HB_MoltenVein.mat");
        if (molten != null) Part("Molten Reach", group, cracks.Build("Molten Reach"), molten);
    }

    // ---------------- Inked objects (the grey majority) ----------------

    private static void InkedDecor()
    {
        var group = Group("Inked Objects (light grey with ink lines)");
        // A few tall marks where the regions meet, so the eye reads four regions round a centre.
        Prefab("Decor_Obelisks_0", group, Top(0.2f, 4.6f), 15f, 3.6f);
        Prefab("Decor_Arches_0", group, Top(0.6f, 0.4f), 80f, 3f);
        Prefab("Decor_Totems_0", group, Top(7.0f, 0.2f), 20f, 2.6f);
        Prefab("Decor_Totems_2", group, Top(-7.2f, -0.6f), 200f, 2.2f);
        Prefab("Decor_Cairns_0", group, Top(0.4f, -2.2f), 40f, 1.4f);

        // Smaller grey pieces, kept along the borders between regions.
        var random = new Random(1029);
        string[] families = { "Pebbles", "Cairns", "Shards", "Needles", "Cages", "Totems" };
        int placed = 0;
        for (int tries = 0; tries < 600 && placed < 22; tries++)
        {
            float x = Lerp(random, -HalfX + 0.5f, HalfX - 0.5f), z = Lerp(random, -HalfZ + 0.5f, HalfZ - 0.5f);
            bool border = Mathf.Abs(x) < 1.6f || Mathf.Abs(z) < 1.2f || Mathf.Abs(x) > HalfX - 1.2f || Mathf.Abs(z) > HalfZ - 1f;
            if (!border || Clear(x, z) < 0.6f) continue;
            Prefab("Decor_" + families[random.Next(families.Length)] + "_" + random.Next(6), group, Top(x, z), Lerp(random, 0f, 360f), Lerp(random, 0.4f, 1f));
            placed++;
        }
    }

    // Distance to the nearest spot kept open (spring, stream, region centres).
    private static float Clear(float x, float z)
    {
        var p = new Vector2(x, z);
        float spring = (p - Spring).magnitude - SpringRadius;
        float stream = StreamDistance(x, z) - 0.5f;
        float zones = Mathf.Min(Mathf.Min((p - BoneZone).magnitude, (p - HeatZone).magnitude), Mathf.Min((p - LightZone).magnitude, (p - GrowthZone).magnitude)) - 1.8f;
        return Mathf.Min(spring, Mathf.Min(stream, zones));
    }

    // ---------------- Shapes ----------------

    // A flat disc on the ground (or sunk into a crater when 'follow' is off), for puddles and glowing floors.
    private static Mesh Disc(Vector2 c, float radius, float squash, float lift, bool follow = false)
    {
        var mesh = new RealmMeshes();
        var rows = new List<Vector3[]>();
        float flat = Height(c.x, c.y) + lift;
        for (int i = 0; i <= 8; i++)
        {
            var row = new Vector3[33];
            for (int k = 0; k <= 32; k++)
            {
                float a = k / 32f * Mathf.PI * 2f, r = radius * i / 8f * (1f + 0.15f * Mathf.Sin(a * 3f));
                float x = c.x + Mathf.Cos(a) * r, z = c.y + Mathf.Sin(a) * r * squash;
                row[k] = new Vector3(x, follow ? Height(x, z) + lift : flat, z);
            }
            rows.Add(row);
        }
        mesh.Grid(rows, 0.5f);
        return mesh.Build("Disc");
    }

    private static void CoralFoam(RealmMeshes mesh, Vector3 at, Random random, float spread)
    {
        int count = random.Next(24, 40);
        float piece = (float)random.NextDouble();
        for (int i = 0; i < count; i++)
        {
            float r = Mathf.Lerp(0.06f, 0.24f, Mathf.Pow((float)random.NextDouble(), 1.5f));
            Vector3 offset = Flat(random, spread);
            float pile = (1f - offset.magnitude / spread) * spread * 0.8f * (float)random.NextDouble();
            Vector3 p = at + offset;
            p.y = Height(p.x, p.z);
            mesh.Sphere(p + Vector3.up * (pile + r * 0.6f), Vector3.one * r, Quaternion.identity, 6, 8, Mathf.Clamp01(pile / spread + 0.3f), piece);
        }
    }

    private static void Anemone(RealmMeshes mesh, Vector3 at, Random random)
    {
        int fingers = random.Next(9, 15);
        for (int i = 0; i < fingers; i++)
        {
            Vector3 dir = Flat(random, 1f).normalized;
            float length = Lerp(random, 0.5f, 1.2f);
            Vector3 start = at + dir * (float)random.NextDouble() * 0.3f - Vector3.up * 0.1f;
            var path = new List<Vector3>();
            for (int k = 0; k <= 8; k++)
            {
                float u = k / 8f;
                path.Add(start + Vector3.up * u * length + dir * u * u * length * 0.5f);
            }
            mesh.Tube(path, u => Mathf.Lerp(0.07f, 0.03f, u), 6, 0f, 1f, (float)random.NextDouble(), 0.04f);
        }
    }

    private static void CrystalCluster(RealmMeshes mesh, Vector3 at, int count, float size, Random random)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = (Vector3.up + Flat(random, 0.7f)).normalized;
            float length = size * Lerp(random, 0.4f, 1f);
            Vector3 foot = at + Flat(random, size * 0.35f);
            foot.y = Height(foot.x, foot.z) - 0.1f;
            mesh.Crystal(foot + dir * length * 0.5f, Quaternion.FromToRotation(Vector3.up, dir), length, length * 0.18f, 6, (float)random.NextDouble(), random);
        }
    }

    // ---------------- Views and callouts ----------------

    private static readonly (string name, Vector3 position, Vector3 target)[] ViewList =
    {
        ("Hero", new Vector3(-13f, 9f, -17f), new Vector3(0.3f, -0.6f, 0f)),
        ("Front Cut", new Vector3(0.5f, -1.0f, -21f), new Vector3(0f, -1.4f, 0f)),
        ("Back High", new Vector3(14f, 13f, 15f), new Vector3(0f, -0.5f, 0f)),
        ("Bone Mother", new Vector3(-0.5f, 2.6f, -9.5f), new Vector3(-4.6f, 0.6f, -2.8f)),
        ("Heat", new Vector3(8.5f, 2.6f, -8.5f), new Vector3(4.2f, 0.8f, -2.6f)),
        ("Light", new Vector3(10.5f, 5f, 0.5f), new Vector3(4.4f, 1.8f, 3.6f)),
        ("Growth", new Vector3(-10f, 3.5f, 8.5f), new Vector3(-4.2f, 0.4f, 3f)),
        ("Inner World", new Vector3(0.3f, -2.9f, -10.5f), new Vector3(0f, -3.2f, -2f)),
        ("Top", new Vector3(0f, 26f, -0.6f), new Vector3(0f, 0f, 0f)),
    };

    private static void Views()
    {
        var views = Group("Views");
        foreach (var view in ViewList)
        {
            var t = new GameObject("View " + view.name).transform;
            t.SetParent(views, false);
            t.SetPositionAndRotation(view.position, Quaternion.LookRotation(view.target - view.position));
        }

        var cameraObject = new GameObject("Habitat Camera");
        cameraObject.transform.SetParent(root, false);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        // The heat lens refracts the opaque texture.
        cameraObject.AddComponent<UniversalAdditionalCameraData>().requiresColorOption = CameraOverrideOption.On;
        Transform hero = views.GetChild(0);
        cameraObject.transform.SetPositionAndRotation(hero.position, hero.rotation);
    }

    // Labels for the art bible, off by default (turn on "Callouts" to show them).
    private static void Callouts()
    {
        var group = Group("Callouts");
        Vector3 eye = ViewList[0].position;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        (string text, Vector3 at, Color color)[] labels =
        {
            ("BONE MOTHER / TIME\nmarble bone, comb-glass ribs, cyan time", Top(BoneZone.x, BoneZone.y) + Vector3.up * 4.4f, new Color(0.45f, 0.9f, 1f)),
            ("HEAT\nfire flowers, molten cracks, heat haze", Top(HeatZone.x, HeatZone.y) + Vector3.up * 5.6f, new Color(1f, 0.55f, 0.2f)),
            ("LIGHT\ncrystal, lamp, film", Top(LightZone.x, LightZone.y) + Vector3.up * 6.4f, new Color(0.95f, 0.95f, 1f)),
            ("GROWTH\ngreen, organic", Top(GrowthZone.x, GrowthZone.y) + Vector3.up * 4.2f, new Color(0.5f, 1f, 0.55f)),
            ("INNER WORLD\nviolet black, cyan lines", new Vector3(0f, -5.6f, -6.5f), new Color(0.6f, 0.5f, 1f)),
        };
        foreach (var label in labels)
        {
            var go = new GameObject(label.text.Split('\n')[0]);
            go.transform.SetParent(group, false);
            go.transform.position = label.at;
            go.transform.rotation = Quaternion.LookRotation(label.at - eye);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = label.text;
            mesh.font = font;
            mesh.fontSize = 100;
            mesh.characterSize = 0.022f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = label.color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        group.gameObject.SetActive(false);
    }

    // ---------------- Placement helpers ----------------

    private static Vector3 Top(float x, float z) => new Vector3(x, Height(x, z), z);

    private static Vector3 Scatter(float x, float z, float radius, Random random)
    {
        Vector3 p = new Vector3(x, 0f, z) + Flat(random, radius);
        p.y = Height(p.x, p.z);
        return p;
    }

    private static Vector3 Flat(Random random, float radius)
    {
        float a = (float)random.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)random.NextDouble()) * radius;
        return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
    }

    private static float Lerp(Random random, float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    // A linked decor prefab instance, sunk a little into the ground; scaled down to maxSize (or to it, when exact).
    private static void Prefab(string name, Transform parent, Vector3 at, float yaw, float maxSize, bool hanging = false, bool exact = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WorldDecor/" + name + ".prefab");
        if (prefab == null) { Debug.LogWarning("Habitat Block: missing decor prefab " + name); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(at - Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
        Fit(go.transform, maxSize, exact);
        if (hanging) go.transform.position = at;
    }

    // Creatures are shown as static mesh copies (no scripts), so the vignette holds still in Edit and Play mode.
    // showHidden also copies renderers the creature only turns on at runtime (a fire flower's bloom).
    private static void Creature(string name, Transform parent, Vector3 at, float yaw, float maxSize, float roll = 0f, bool showHidden = false)
    {
        string path = AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Prefabs/Creatures" })
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
        var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        if (prefab == null) { Debug.LogWarning("Habitat Block: missing creature prefab " + name); return; }

        GameObject instance = Object.Instantiate(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var copy = new GameObject(name).transform;
        copy.SetParent(parent, false);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(showHidden))
        {
            if (!renderer.enabled && !showHidden) continue;
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh { name = skinned.sharedMesh.name + " Pose" };
                skinned.BakeMesh(mesh, true);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
            }
            if (mesh == null) continue;
            var part = new GameObject(renderer.name);
            part.transform.SetParent(copy, false);
            part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            part.transform.localScale = renderer is SkinnedMeshRenderer ? Vector3.one : renderer.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
        }
        Object.DestroyImmediate(instance);
        if (copy.childCount == 0)
        {
            Debug.LogWarning("Habitat Block: " + name + " builds its mesh at runtime, skipped.");
            Object.DestroyImmediate(copy.gameObject);
            return;
        }
        // Stand the copy on its lowest point.
        Bounds bounds = WorldBounds(copy);
        copy.position = at - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) - Vector3.up * 0.03f;
        var holder = new GameObject(name + " Pose").transform;
        holder.SetParent(parent, false);
        holder.position = at;
        copy.SetParent(holder, true);
        holder.rotation = Quaternion.Euler(0f, yaw, roll);
        Fit(holder, maxSize);
    }

    // Scales down to maxSize, or to exactly maxSize when 'exact' (scales small pieces up too).
    private static void Fit(Transform t, float maxSize, bool exact = false)
    {
        Bounds bounds = WorldBounds(t);
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if ((size > maxSize || exact) && size > 0.001f) t.localScale *= maxSize / size;
    }

    private static Bounds WorldBounds(Transform t)
    {
        bool any = false;
        var bounds = new Bounds(t.position, Vector3.zero);
        foreach (MeshFilter filter in t.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            Vector3 c = filter.sharedMesh.bounds.center, e = filter.sharedMesh.bounds.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = m.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    private static void PointLight(string name, Transform parent, Vector3 at, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
    }

    private static Transform Group(string name, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : root, false);
        return t;
    }

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (material == null) { Debug.LogWarning("Habitat Block: missing material for " + name); return go; }
        // Some shape builders mark their meshes DontSave; clear it so the scene keeps them.
        if ((mesh.hideFlags & HideFlags.DontSave) != 0) mesh.hideFlags &= ~HideFlags.DontSave;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    private static Material Mat(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Material", new[] { "Assets/Materials" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        Debug.LogWarning("Habitat Block: missing material " + name);
        return null;
    }

    // A tuned copy of a game material, saved in Assets/Materials/HabitatBlock (rewritten on every build).
    private static Material Variant(string baseName, string name, Action<Material> tune)
    {
        Material source = Mat(baseName);
        if (source == null) return null;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "HabitatBlock");
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
        }
        tune(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    // A material asset on a game shader that has no material of its own (the kiln's rib and flora shaders are
    // made at runtime), saved in Assets/Materials/HabitatBlock and rewritten on every build.
    private static Material ShaderAsset(string shaderName, string name, Action<Material> tune, Shader shader = null)
    {
        shader = shader != null ? shader : Shader.Find(shaderName);
        if (shader == null) { Debug.LogWarning("Habitat Block: shader " + shaderName + " not found."); return null; }
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "HabitatBlock");
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        tune?.Invoke(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    // A grid surface from a (u, v) function. Faces are turned towards 'outward' (or away from 'inside'),
    // and cells whose centre fails 'keep' are left out.
    private static Mesh Surface(int nu, int nv, Func<float, float, Vector3> point, Vector3? outward,
        Func<Vector3, bool> keep = null, Vector3? inside = null)
    {
        var vertices = new List<Vector3>();
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
                vertices.Add(point(i / (float)nu, j / (float)nv));
        var triangles = new List<int>();
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                Vector3 centre = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) * 0.25f;
                if (keep != null && !keep(centre)) continue;
                Vector3 normal = Vector3.Cross(vertices[c] - vertices[a], vertices[b] - vertices[a]);
                Vector3 want = outward ?? (inside.HasValue ? inside.Value - centre : Vector3.up);
                if (Vector3.Dot(normal, want) >= 0f) triangles.AddRange(new[] { a, c, b, b, c, d });
                else triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------- Capture ----------------

    // Renders every view (and the hero view with callouts) to ArtCaptures/HabitatBlock, 1920 x 1080.
    private static void Capture()
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Scene_HabitatBlock does not exist yet. Create it first.");
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            opened = true;
        }
        SceneManager.SetActiveScene(scene);
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            Transform block = scene.GetRootGameObjects().Select(g => g.transform).First(t => t.name == "Habitat Block");
            Camera camera = block.GetComponentInChildren<Camera>(true);
            Transform views = block.Find("Views");
            Transform callouts = block.Find("Callouts");
            int mask = camera.cullingMask;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            camera.cullingMask = 1 << Layer;
            camera.scene = scene;
            Directory.CreateDirectory(CaptureFolder);
            foreach (string old in Directory.GetFiles(CaptureFolder, "*.png")) File.Delete(old);
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i <= views.childCount; i++)
                {
                    bool labelled = i == views.childCount;
                    Transform view = views.GetChild(labelled ? 0 : i);
                    if (callouts != null) callouts.gameObject.SetActive(labelled);
                    camera.transform.SetPositionAndRotation(view.position, view.rotation);
                    camera.Render();
                    camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    texture.Apply();
                    string file = (i + 1).ToString("00") + "_" + view.name.Substring(5).Replace(" ", "") + (labelled ? "_Callouts" : "") + ".png";
                    File.WriteAllBytes(Path.Combine(CaptureFolder, file), texture.EncodeToPNG());
                }
            }
            finally
            {
                if (callouts != null) callouts.gameObject.SetActive(false);
                camera.targetTexture = null;
                camera.scene = default;
                RenderTexture.active = active;
                camera.cullingMask = mask;
                camera.transform.SetPositionAndRotation(position, rotation);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
            Debug.Log("Habitat Block views saved to " + CaptureFolder);
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = async;
            if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
