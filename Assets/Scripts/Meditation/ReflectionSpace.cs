using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The blank place the player goes to while meditating: a pale floor in a paper void,
/// a ring of steles and smaller standing tablets to draw on, and casts of met creatures.
/// Built once at runtime, then moved to wherever the player meditates.
/// </summary>
[DisallowMultipleComponent]
public sealed class ReflectionSpace : MonoBehaviour
{
    [Header("Materials (created from the shaders when empty)")]
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material stoneMaterial;
    [SerializeField] private Material faceMaterial;
    [SerializeField] private Material castMaterial;

    [Header("Layout")]
    [Tooltip("Changing the seed changes every stele shape, so saved drawings no longer match.")]
    [SerializeField] private int seed = 7;
    [Min(4f)] [SerializeField] private float floorRadius = 14f;
    [Min(2f)] [SerializeField] private float walkRadius = 11f;
    [Range(1, 9)] [SerializeField] private int steleCount = 5;
    [Range(0, 9)] [SerializeField] private int tabletCount = 4;
    [SerializeField] private Vector2 steleDistance = new Vector2(6.5f, 8.5f);
    [SerializeField] private Vector2 tabletDistance = new Vector2(4.2f, 5.2f);

    private struct Obstacle
    {
        public Vector3 local;
        public float radius;
    }

    private Transform root;
    private readonly List<InkSurface> surfaces = new List<InkSurface>();
    private readonly List<Obstacle> obstacles = new List<Obstacle>();
    private readonly Dictionary<string, ReflectionCast> casts = new Dictionary<string, ReflectionCast>();

    public Transform Root => root;
    public float WalkRadius => walkRadius;

    /// <summary>Moves the space to the anchor (floor centre) and shows it.</summary>
    public void Place(Vector3 anchor, float yaw)
    {
        if (root == null) Build();
        root.SetPositionAndRotation(anchor, Quaternion.Euler(0f, yaw, 0f));
        root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        SaveDrawings();
        if (root != null) root.gameObject.SetActive(false);
    }

    public void SaveDrawings()
    {
        foreach (InkSurface surface in surfaces) surface.Save();
    }

    /// <summary>Adds a cast for every journal entry that does not have one yet.</summary>
    public void SyncCasts(CreatureJournal journal)
    {
        if (root == null) Build();
        IReadOnlyList<JournalEntry> entries = journal.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            JournalEntry entry = entries[i];
            if (casts.ContainsKey(entry.key)) continue;
            casts[entry.key] = SpawnCast(entry, journal.GetCast(entry), i);
        }
    }

    /// <summary>Remembers where the player put a cast (saved with the journal).</summary>
    public void StoreCast(ReflectionCast cast)
    {
        if (cast == null || cast.Entry == null) return;
        cast.Entry.placed = true;
        cast.Entry.castPosition = root.InverseTransformPoint(cast.transform.position);
        cast.Entry.castYaw = (Quaternion.Inverse(root.rotation) * cast.transform.rotation).eulerAngles.y;
    }

    /// <summary>Keeps a walking point on the floor and outside steles and tablets.</summary>
    public Vector3 Constrain(Vector3 worldPosition, float bodyRadius)
    {
        Vector3 local = root.InverseTransformPoint(worldPosition);
        float height = local.y;
        local.y = 0f;
        if (local.magnitude > walkRadius) local = local.normalized * walkRadius;
        foreach (Obstacle obstacle in obstacles)
        {
            Vector3 away = local - obstacle.local;
            float minimum = obstacle.radius + bodyRadius;
            if (away.sqrMagnitude < minimum * minimum)
                local = obstacle.local + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward) * minimum;
        }
        local.y = height;
        return root.TransformPoint(local);
    }

    private void Build()
    {
        CreateMissingMaterials();
        root = new GameObject("Reflection Space").transform;
        root.SetParent(transform, false);
        var random = new System.Random(seed);

        GameObject floor = Part("Floor", root, MeditationShapes.Disc(floorRadius), floorMaterial);
        floor.transform.localPosition = Vector3.down * 0.002f;

        float step = 360f / steleCount;
        for (int i = 0; i < steleCount; i++)
        {
            float angle = i * step + Range(random, -step * 0.2f, step * 0.2f);
            if (i == 0) angle = 0f; // One stele straight ahead on arrival.
            float distance = Range(random, steleDistance.x, steleDistance.y);
            BuildStele(random, i, angle, distance);
        }

        float tabletStep = 360f / Mathf.Max(1, tabletCount);
        for (int i = 0; i < tabletCount; i++)
        {
            float angle = (i + 0.5f) * tabletStep + Range(random, -10f, 10f);
            float distance = Range(random, tabletDistance.x, tabletDistance.y);
            BuildTablet(random, i, angle, distance);
        }
        root.gameObject.SetActive(false);
    }

    private void BuildStele(System.Random random, int index, float angle, float distance)
    {
        SlabShape shape = SlabShape.Random(random, 1.5f, 2.7f, 0.42f);
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
        var stele = new GameObject("Stele " + index).transform;
        stele.SetParent(root, false);
        stele.localPosition = position + Vector3.down * 0.05f;
        stele.localRotation = Quaternion.LookRotation(-position.normalized) * Quaternion.Euler(0f, Range(random, -8f, 8f), 0f);

        // The whole stone is the drawing surface; ink fades out around the front edges.
        MakeDrawable(Part("Stone", stele, MeditationShapes.Slab(shape, 20), stoneMaterial), "stele_" + index, shape);
        obstacles.Add(new Obstacle { local = position, radius = shape.width * 0.55f });
    }

    private void BuildTablet(System.Random random, int index, float angle, float distance)
    {
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
        var tablet = new GameObject("Tablet " + index).transform;
        tablet.SetParent(root, false);
        tablet.localPosition = position;
        tablet.localRotation = Quaternion.LookRotation(-position.normalized) * Quaternion.Euler(0f, Range(random, -25f, 25f), 0f);

        float postHeight = Range(random, 0.9f, 1.25f);
        Part("Post", tablet, MeditationShapes.Post(random.Next(1, 9999), postHeight + 0.2f, 0.05f), stoneMaterial);

        SlabShape shape = SlabShape.Random(random, 0.85f, 0.7f, 0.08f);
        shape.lean = 0f;
        shape.roughness *= 0.5f;
        var board = new GameObject("Board").transform;
        board.SetParent(tablet, false);
        board.localPosition = new Vector3(0f, postHeight - shape.height * 0.35f, 0.03f);
        board.localRotation = Quaternion.Euler(-Range(random, 8f, 20f), 0f, Range(random, -6f, 6f));
        MakeDrawable(Part("Stone", board, MeditationShapes.Slab(shape, 12), stoneMaterial), "tablet_" + index, shape);
        obstacles.Add(new Obstacle { local = position, radius = 0.35f });
    }

    private void MakeDrawable(GameObject stone, string id, SlabShape shape)
    {
        var collider = stone.AddComponent<MeshCollider>();
        collider.sharedMesh = stone.GetComponent<MeshFilter>().sharedMesh;
        var surface = stone.AddComponent<InkSurface>();
        surface.Setup(id, new Vector2(shape.width, shape.height));
        surfaces.Add(surface);
    }

    private ReflectionCast SpawnCast(JournalEntry entry, CreatureCast creatureCast, int index)
    {
        var go = new GameObject("Cast " + entry.displayName);
        go.transform.SetParent(root, false);
        // One child per original renderer, with the creature's own materials.
        foreach (CreatureCast.Part part in creatureCast.parts)
        {
            var materials = new Material[part.mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = part.materials != null && i < part.materials.Length ? part.materials[i] : null;
                materials[i] = original != null ? original : castMaterial;
            }
            GameObject child = Part(part.mesh.name, go.transform, part.mesh, materials[0]);
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            if (part.block != null) renderer.SetPropertyBlock(part.block);
        }

        // Unplaced casts gather loosely around the centre, spaced by their size.
        float spacing = Mathf.Max(1.6f, creatureCast.bounds.size.magnitude * 0.6f);
        if (entry.placed)
        {
            go.transform.localPosition = entry.castPosition;
            go.transform.localRotation = Quaternion.Euler(0f, entry.castYaw, 0f);
        }
        else
        {
            float angle = 140f + index * 47f;
            float distance = Mathf.Min(walkRadius - 1f, 2.2f + spacing * 0.5f + (index % 3) * spacing * 0.5f);
            go.transform.localPosition = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            go.transform.localRotation = Quaternion.Euler(0f, angle + 180f, 0f);
        }
        var box = go.AddComponent<BoxCollider>();
        box.center = creatureCast.bounds.center;
        box.size = Vector3.Max(creatureCast.bounds.size, Vector3.one * 0.25f);
        var cast = go.AddComponent<ReflectionCast>();
        cast.Entry = entry;
        return cast;
    }

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private void CreateMissingMaterials()
    {
        Shader ink = Shader.Find("Capstone/Meditation/Ink");
        if (ink == null) return;
        if (floorMaterial == null)
        {
            floorMaterial = new Material(ink) { name = "Reflection Floor (runtime)" };
            floorMaterial.SetFloat("_RingCount", 22f);
            floorMaterial.SetFloat("_RingKeep", 0.55f);
            floorMaterial.SetFloat("_EdgeRagged", 0.18f);
            floorMaterial.SetFloat("_EdgeRound", 1f);
            floorMaterial.SetFloat("_RimInk", 0f);
        }
        if (stoneMaterial == null)
        {
            stoneMaterial = new Material(ink) { name = "Reflection Stone (runtime)" };
            MakeBlackStone(stoneMaterial);
        }
        if (faceMaterial == null)
        {
            faceMaterial = new Material(ink) { name = "Reflection Face (runtime)" };
            faceMaterial.SetFloat("_EdgeRagged", 0.12f);
            faceMaterial.SetFloat("_HatchStrength", 0f);
            faceMaterial.SetFloat("_RimInk", 0f);
        }
        if (castMaterial == null)
        {
            castMaterial = new Material(ink) { name = "Reflection Cast (runtime)" };
            castMaterial.SetFloat("_Breath", 0.006f);
        }
    }

    /// <summary>Black stone with pale ink: drawings show as light lines.</summary>
    public static void MakeBlackStone(Material material)
    {
        material.SetColor("_PaperColor", new Color(0.07f, 0.07f, 0.075f, 1f));
        material.SetColor("_ShadeColor", new Color(0.02f, 0.02f, 0.025f, 1f));
        material.SetColor("_InkColor", new Color(0.92f, 0.91f, 0.88f, 1f));
        material.SetFloat("_HatchStrength", 0.35f);
        material.SetFloat("_RimInk", 0.6f);
    }

    public void SetMaterials(Material floor, Material stone, Material face, Material cast)
    {
        floorMaterial = floor;
        stoneMaterial = stone;
        faceMaterial = face;
        castMaterial = cast;
    }

    private static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}
