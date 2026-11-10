using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds one flat theater and its live exhibits in the current reflection scene.</summary>
[DisallowMultipleComponent]
public sealed class ReflectionSpace : MonoBehaviour
{
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material stoneMaterial;
    [SerializeField] private Shader spotlightShader;
    private readonly List<Object> resources = new();
    private InkSurface slab;
    private Transform root;
    public Transform Root => root;
    public float FloorRadius { get; private set; }

    public void BuildVisit(ReflectionCatalog catalog, ReflectionProgress progress)
    {
        root = new GameObject("Reflection Amphitheater").transform;
        var occupied = new List<(Vector3 position, float radius)>();
        var slots = new List<(ReflectionCatalog.Definition definition, Vector3 position)>();
        float angle = 0.63f;
        FloorRadius = 24f;
        foreach (string id in progress.Unlocked)
        {
            var definition = catalog.Find(id);
            if (definition == null) { Debug.LogWarning("Unknown reflection exhibit: " + id); continue; }
            Vector3 position;
            while (true)
            {
                float heading = angle % (2f * Mathf.PI);
                float distance = 6f + 1.65f * angle;
                position = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * distance;
                angle += 0.075f;
                float clearance = definition.radius + 1.5f;
                if (distance < clearance + 3f || (position.z > -clearance && Mathf.Abs(position.x) < 7f + clearance) ||
                    occupied.Exists(slot => (position - slot.position).sqrMagnitude <
                        Mathf.Pow(slot.radius + clearance, 2f))) continue;
                occupied.Add((position, definition.radius));
                break;
            }
            slots.Add((definition, position));
            FloorRadius = Mathf.Max(FloorRadius, position.magnitude + definition.radius + 7f);
        }

        Material floor = floorMaterial != null ? floorMaterial : MakeInk(new Color(0.77f, 0.75f, 0.69f));
        Material stone = stoneMaterial != null ? stoneMaterial : MakeInk(new Color(0.08f, 0.08f, 0.09f));
        Material pale = Own(RestSpotSystem.MakeRestMaterial(new Color(0.88f, 0.87f, 0.84f)));
        Material dark = Own(RestSpotSystem.MakeRestMaterial(new Color(0.06f, 0.06f, 0.07f)));
        Material switchMaterial = Own(RestSpotSystem.MakeRestMaterial(new Color(0.95f, 0.75f, 0.40f)));
        Material lightMaterial = Own(new Material(spotlightShader != null ? spotlightShader : Shader.Find("Capstone/Meditation/Spotlight")));
        var ground = Part("Amphitheater floor", Own(MeditationShapes.Disc(FloorRadius)), floor, Vector3.zero);
        ground.layer = 7;
        ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
        var baseCollider = ground.AddComponent<BoxCollider>();
        baseCollider.center = Vector3.down * 0.35f;
        baseCollider.size = new Vector3(FloorRadius * 2f, 0.6f, FloorRadius * 2f);
        var sun = new GameObject("Reflection fill").AddComponent<Light>();
        sun.transform.SetParent(root, false);
        sun.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        BuildBoundary();

        var centre = new GameObject("Central Resting Creature");
        centre.transform.SetParent(root, false);
        var seat = centre.AddComponent<RestSpot>();
        seat.Build("Seat", pale, dark, switchMaterial);
        seat.MakeFixed();
        seat.SetNew(false);
        BuildSlab(stone, pale);

        ErosiveFluidSystem fluid = null;
        ErosiveFluidSource source = null;
        if (slots.Exists(slot => slot.definition.recipe == ReflectionCatalog.Recipe.Water ||
            slot.definition.recipe == ReflectionCatalog.Recipe.Snail))
        {
            var water = new GameObject("Reflection water");
            water.transform.SetParent(root, false);
            source = water.AddComponent<ErosiveFluidSource>();
            source.SetEmitting(false);
            fluid = water.AddComponent<ErosiveFluidSystem>();
            fluid.InitializeReflection(FloorRadius, Own(RestSpotSystem.MakeRestMaterial(new Color(0.17f, 0.68f, 0.83f))));
        }
        ReflectionPhysics.SyncTransforms();

        foreach (var slot in slots)
        {
            var fixture = new GameObject("Exhibit " + slot.definition.id);
            fixture.transform.SetParent(root, false);
            fixture.transform.position = slot.position;
            fixture.transform.rotation = Quaternion.LookRotation(new Vector3(slot.position.x, 0f, slot.position.z));
            var beam = fixture.AddComponent<ReflectionSpotlight>();
            beam.Configure(slot.definition.radius, slot.definition.height, lightMaterial, switchMaterial);
            Spawn(slot.definition, slot.position, beam, fluid, source, dark);
        }
        ReflectionPhysics.SyncTransforms();
    }

    private void BuildBoundary()
    {
        const int sides = 48;
        float length = 2f * FloorRadius * Mathf.Tan(Mathf.PI / sides);
        for (int i = 0; i < sides; i++)
        {
            var boundary = new GameObject("Theater boundary");
            boundary.transform.SetParent(root, false);
            boundary.transform.localRotation = Quaternion.Euler(0f, i * 360f / sides, 0f);
            boundary.transform.localPosition = boundary.transform.forward * (FloorRadius - 1f) + Vector3.up * 16f;
            boundary.AddComponent<BoxCollider>().size = new Vector3(length + 0.5f, 34f, 0.75f);
            boundary.layer = 7;
        }
    }

    private void BuildSlab(Material stone, Material glyphMaterial)
    {
        var shape = new SlabShape { width = 14f, height = 6f, depth = 1f, taper = 0.95f, roughness = 0.06f, seed = 7 };
        var board = Part("Giant Slab", Own(MeditationShapes.Slab(shape, 32)), stone, Vector3.forward * 17f);
        board.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        board.AddComponent<MeshCollider>().sharedMesh = board.GetComponent<MeshFilter>().sharedMesh;
        slab = board.AddComponent<InkSurface>();
        slab.Setup("amphitheater-slab-v1", new Vector2(shape.width, shape.height));
        var glyphs = LandmarkProgress.Glyphs;
        for (int i = 0; i < glyphs.Count; i++)
        {
            float spacing = Mathf.Min(0.7f, 12f / Mathf.Max(1, glyphs.Count));
            var glyph = Part("Inscription " + glyphs[i],
                Own(LandmarkShapes.Glyph(LandmarkGlyphs.Get(glyphs[i]), spacing * 0.72f, 0.035f)), glyphMaterial,
                new Vector3((i - (glyphs.Count - 1) * 0.5f) * spacing, 5.4f, 16.38f));
            glyph.transform.rotation = Quaternion.identity;
        }
    }

    private void Spawn(ReflectionCatalog.Definition definition, Vector3 position, ReflectionSpotlight beam,
        ErosiveFluidSystem fluid, ErosiveFluidSource source, Material supportMaterial)
    {
        if (definition.recipe == ReflectionCatalog.Recipe.Bees)
        {
            var colony = new GameObject("Reflection Bee Colony").AddComponent<GlowBeeColony>();
            colony.transform.SetParent(root, false);
            colony.InitializeReflection(position, beam);
            return;
        }
        if (definition.recipe == ReflectionCatalog.Recipe.Water)
        {
            for (int i = 0; i < definition.count; i++)
            {
                Vector3 offset = new Vector3((i % 6 - 2.5f) * 0.55f, 0.35f + i / 12 * 0.52f, (i / 6 - 2f) * 0.55f);
                if (fluid.Spawn(source, position + offset, Vector3.zero))
                    beam.Register(fluid.Droplets[fluid.Droplets.Count - 1]);
            }
            return;
        }
        CeilingBatBugHabitat habitat = null;
        if (definition.recipe == ReflectionCatalog.Recipe.BatBugs)
        {
            var perch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            perch.name = "Bat bug perch";
            perch.layer = 7;
            perch.transform.SetParent(root, false);
            perch.transform.position = position + Vector3.up * 3.8f;
            perch.transform.localScale = new Vector3(7f, 0.35f, 1.5f);
            perch.GetComponent<Renderer>().sharedMaterial = supportMaterial;
            var habitatRoot = new GameObject("Bat bug habitat");
            habitatRoot.transform.SetParent(root, false);
            habitatRoot.transform.position = position;
            habitat = habitatRoot.AddComponent<CeilingBatBugHabitat>();
            habitat.bugPrefab = definition.prefab.GetComponent<CeilingBatBug>();
            habitat.eggPrefab = definition.support.GetComponent<CeilingBatBugEgg>();
            habitat.roamRadius = definition.radius;
            habitat.localBounds = new Bounds(Vector3.up * 3f, new Vector3(12f, 9f, 12f));
            ReflectionPhysics.SyncTransforms();
        }

        for (int i = 0; i < definition.count; i++)
        {
            Vector3 at = position + Vector3.up * 0.55f;
            if (definition.recipe == ReflectionCatalog.Recipe.BatBugs)
                at = position + new Vector3((i - (definition.count - 1) * 0.5f) * 1.25f, 3.35f, 0f);
            else if (definition.recipe == ReflectionCatalog.Recipe.Trees)
                at = position + Vector3.right * (i == 0 ? -4.5f : 1.5f);
            else if (definition.recipe == ReflectionCatalog.Recipe.Colossus)
                at = position;
            else if (definition.count > 1)
                at += Quaternion.Euler(0f, i * 360f / definition.count, 0f) * Vector3.forward * 2f + Vector3.up * 1.5f;

            Creature creature = definition.prefab != null ? SpawnPrefab(definition, at, i) : SpawnProcedural(definition, at);
            if (creature == null) { Debug.LogError("Could not create reflection exhibit " + definition.id, this); continue; }
            if (creature is CeilingBatBug bug)
            {
                var life = bug.GetComponent<CeilingBatBugLife>() ?? bug.gameObject.AddComponent<CeilingBatBugLife>();
                life.InitializeAdult(habitat, at, Mathf.Lerp(55f, 75f, i / (float)definition.count), i + 814);
                if (ReflectionPhysics.Raycast(at, Vector3.up, out var hit, 1f, 1 << 7))
                    bug.GetComponent<CeilingBatBugSurfaceMotor>().StartOnPerch(hit);
            }
            if (creature is CompostCreature compost) compost.PrepareReflection();
            if (creature is SkySpider spider) beam.Register(spider.CurrentWeb);
            if (creature is Squirrel squirrel) BuildSquirrelSupport(squirrel, definition, position, beam, supportMaterial);
            if (creature is TentacleCreature tentacle) tentacle.PrepareReflectionPose();
            beam.Register(creature);
        }
    }

    private Creature SpawnPrefab(ReflectionCatalog.Definition definition, Vector3 at, int index)
    {
        GameObject instance = Instantiate(definition.prefab, at, Quaternion.identity, root);
        if (instance.TryGetComponent(out MushroomTree tree))
            tree.Configure(12f, 4f, true, index == 0 ? 0f : 1f);
        return instance.GetComponent<Creature>();
    }

    private Creature SpawnProcedural(ReflectionCatalog.Definition definition, Vector3 position)
    {
        var specimen = new GameObject(definition.id);
        specimen.transform.SetParent(root, false);
        specimen.transform.position = position;
        switch (definition.recipe)
        {
            case ReflectionCatalog.Recipe.Bellows: return specimen.AddComponent<BellowsCreature>();
            case ReflectionCatalog.Recipe.Snail: return specimen.AddComponent<CisternSnail>();
            case ReflectionCatalog.Recipe.Colossus:
                var colossus = specimen.AddComponent<OreColossus>();
                colossus.PrepareReflection(definition.support.GetComponent<GeodeLarva>());
                return colossus;
            case ReflectionCatalog.Recipe.FireEater:
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.SetParent(specimen.transform, false);
                visual.transform.localScale = new Vector3(1f, 0.7f, 1.25f);
                visual.GetComponent<Collider>().enabled = false;
                visual.GetComponent<Renderer>().sharedMaterial = Own(RestSpotSystem.MakeRestMaterial(new Color(0.42f, 0.13f, 0.07f)));
                specimen.AddComponent<Rigidbody>();
                specimen.AddComponent<SphereCollider>().radius = 0.5f;
                var eater = specimen.AddComponent<FireEater>();
                eater.SetFlowerPrefab(definition.support.GetComponent<FireFlower>());
                return eater;
            default:
                Destroy(specimen);
                return null;
        }
    }

    private void BuildSquirrelSupport(Squirrel squirrel, ReflectionCatalog.Definition definition,
        Vector3 position, ReflectionSpotlight beam, Material supportMaterial)
    {
        var manager = root.GetComponentInChildren<SquirrelSystemManager>();
        if (manager == null)
        {
            manager = new GameObject("Reflection Squirrel System").AddComponent<SquirrelSystemManager>();
            manager.transform.SetParent(root, false);
            manager.ConfigureReflection(definition.support, definition.product);
        }
        for (int i = 0; i < 2; i++)
        {
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Squirrel tree anchor";
            trunk.transform.SetParent(root, false);
            trunk.transform.position = position + new Vector3(i == 0 ? -3.5f : 3.5f, 2f, 1.5f);
            trunk.transform.localScale = new Vector3(0.65f, 2f, 0.65f);
            trunk.GetComponent<Renderer>().sharedMaterial = supportMaterial;
            manager.Register(trunk.AddComponent<SquirrelTree>());
        }
        ReflectionPhysics.SyncTransforms();
        manager.Register(squirrel);
        var nest = Instantiate(definition.support, position + new Vector3(2f, 0.4f, 0f), Quaternion.identity, root).GetComponent<SquirrelNest>();
        manager.Register(nest);
        manager.InitializeSystem();
        PlaceOnFloor(squirrel, position + Vector3.left * 1.5f);
        PlaceOnFloor(nest, position + Vector3.right * 1.5f);
        beam.Register(nest);
        for (int i = 0; i < 3; i++)
        {
            var food = Instantiate(definition.product, position + new Vector3(-1.5f, 0.25f, 1f + i * 0.7f), Quaternion.identity, root);
            var foodCreature = food.GetComponent<SquirrelFood>();
            manager.Register(foodCreature);
            beam.Register(foodCreature);
        }
    }

    private static void PlaceOnFloor(Creature creature, Vector3 point)
    {
        SquirrelTree.PlaceOnGround(creature.transform, point);
        Rigidbody body = creature.GetComponent<Rigidbody>();
        if (body == null) return;
        Vector3 position = creature.transform.position;
        position.y = Mathf.Max(point.y + 0.25f, position.y);
        creature.transform.position = body.position = position;
    }

    public void SaveDrawings() => slab?.Save();
    public void Hide()
    {
        SaveDrawings();
        slab = null;
        root = null;
        foreach (Object resource in resources) if (resource != null) Destroy(resource);
        resources.Clear();
    }

    private GameObject Part(string name, Mesh mesh, Material material, Vector3 localPosition)
    {
        var part = new GameObject(name);
        part.transform.SetParent(root, false);
        part.transform.localPosition = localPosition;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }

    private T Own<T>(T resource) where T : Object { resources.Add(resource); return resource; }

    private Material MakeInk(Color color)
    {
        var material = Own(new Material(Shader.Find("Capstone/Meditation/Ink")));
        material.SetColor("_PaperColor", color);
        material.SetColor("_ShadeColor", color * 0.65f);
        material.SetColor("_InkColor", Color.white);
        material.SetFloat("_EdgeRound", 0f);
        return material;
    }

    public static void MakeBlackStone(Material material)
    {
        material.SetColor("_PaperColor", new Color(0.07f, 0.07f, 0.075f, 1f));
        material.SetColor("_ShadeColor", new Color(0.02f, 0.02f, 0.025f, 1f));
        material.SetColor("_InkColor", new Color(0.92f, 0.91f, 0.88f, 1f));
        material.SetFloat("_HatchStrength", 0.35f);
        material.SetFloat("_RimInk", 0.6f);
    }

    public void SetMaterials(Material floor, Material stone, Shader spotlight)
    {
        floorMaterial = floor;
        stoneMaterial = stone;
        spotlightShader = spotlight;
    }
}
