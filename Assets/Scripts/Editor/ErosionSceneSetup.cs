using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ErosionSceneSetup
{
    private const string Folder = "Assets/Materials/Fluid";
    public static string IntegrationScenePath
    {
        get
        {
            // Keep the identity of the created copy when it is renamed in the editor.
            string existing = AssetDatabase.GUIDToAssetPath("d1db09bf3895ffd44b5f2a5d5c4c1600");
            return string.IsNullOrEmpty(existing) ? "Assets/Scenes/Scene_Daniel_Erosion.unity" : existing;
        }
    }
    [MenuItem("Tools/Capstone/Erosion/Create Independent Test Scene")]
    public static void CreatePrototype()
    {
        Materials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("Erosion Test Terrain").AddComponent<ProceduralWorld>();
        Set(world, "worldSize", new Vector2(32f, 32f)); Set(world, "cellSize", 0.5f); Set(world, "chunkCells", 16);
        Set(world, "erosionTestCourse", true); Set(world, "blockingRadius", 40f); Set(world, "sinkholeCount", 0); Set(world, "firstSinkholeDistance", 0f);
        Set(world, "surfaceMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat"));
        Set(world, "innerMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat"));
        ErosiveFluidSystem fluid = AddFluid(world, new Vector3(0.5f, 7f, -10f), true);
        AddSource(fluid, "Branch Water (enable to divert flow)", new Vector3(2f, 5.4f, -5f), false);
        AddSource(fluid, "Cave Wall Water", new Vector3(6.2f, -1.5f, 9f), false);
        var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset);
        player.transform.position = new Vector3(-4f, 8f, -8f);
        var controller = player.GetComponentInChildren<SmoothFirstPersonController>();
        Set(controller, "whatIsGround", (1 << 7) | (1 << 8));
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
        light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_QuietLines.mat");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.55f);
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Pit Physics Witness";
        cube.transform.position = new Vector3(-12f, 5f, -8f); cube.AddComponent<Rigidbody>();
        cube.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Stone.mat");
        GameObject fuzz = new GameObject("Erosion Fuzz Witness", typeof(SphereCollider), typeof(Rigidbody), typeof(FuzzCreature));
        fuzz.transform.position = new Vector3(-10f, 5f, -8f); fuzz.GetComponent<SphereCollider>().radius = 0.4f;
        Set(fuzz.GetComponent<FuzzCreature>(), "furMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/MAT_Fuzz_Fur.mat"));
        Set(fuzz.GetComponent<FuzzCreature>(), "gooMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/MAT_Fuzz_Goo.mat"));
        GameObject tentacle = new GameObject("Erosion Tentacle Witness", typeof(SphereCollider), typeof(Rigidbody), typeof(TentacleCreature));
        tentacle.transform.position = new Vector3(-7f, 5f, -7f); tentacle.GetComponent<SphereCollider>().radius = 0.4f;
        string[] fields = { "bodyMaterial", "jointMaterial", "tipMaterial", "membraneMaterial" };
        string[] names = { "Body", "Joint", "Tip", "Membrane" };
        for (int i = 0; i < fields.Length; i++) Set(tentacle.GetComponent<TentacleCreature>(), fields[i], AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/MAT_Octopus_" + names[i] + ".mat"));
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/ErosionFluidTest.unity");
        AssetDatabase.SaveAssets(); Debug.Log("Erosion prototype scene created.");
    }
    [MenuItem("Tools/Capstone/Erosion/Create Daniel Erosion Copy")]
    public static void CreateIntegration()
    {
        Materials();
        string output = IntegrationScenePath;
        if (!File.Exists(output)) AssetDatabase.CopyAsset("Assets/Scenes/Scene_Daniel.unity", output);
        var scene = EditorSceneManager.OpenScene(output);
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (Object.FindFirstObjectByType<ErosiveFluidSystem>() == null)
        {
            var fluid = AddFluid(world, world.transform.position + new Vector3(32f, 50f, 40f), false);
            var placement = fluid.gameObject.AddComponent<ErosionDemoPlacement>(); Set(placement, "fluid", fluid);
            var controls = fluid.GetComponent<ErosionDemoControls>(); Set(controls, "fixedBrushPosition", new Vector3(0f, 0f, 5f));
        }
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Debug.Log("Created Scene_Daniel_Erosion; original scene and build list preserved.");
    }
    private static ErosiveFluidSystem AddFluid(ProceduralWorld world, Vector3 point, bool emitting)
    {
        var fluid = new GameObject("Erosive Fluid").AddComponent<ErosiveFluidSystem>(); Set(fluid, "world", world);
        Set(fluid, "dropletMaterial", AssetDatabase.LoadAssetAtPath<Material>(Folder + "/MAT_ErosiveWater.mat"));
        Set(fluid, "debrisMaterial", AssetDatabase.LoadAssetAtPath<Material>(Folder + "/MAT_ErosionDebris.mat"));
        Set(fluid, "rippleMaterial", AssetDatabase.LoadAssetAtPath<Material>(Folder + "/MAT_ErosionRipple.mat"));
        AddSource(fluid, "Slope Water Source", point, emitting);
        var controls = fluid.gameObject.AddComponent<ErosionDemoControls>(); Set(controls, "fluid", fluid); return fluid;
    }
    private static void AddSource(ErosiveFluidSystem fluid, string name, Vector3 point, bool emitting)
    {
        var source = new GameObject(name).AddComponent<ErosiveFluidSource>(); source.transform.SetParent(fluid.transform); source.transform.position = point;
        Set(source, "fluid", fluid); Set(source, "emitting", emitting);
    }
    private static void Materials()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials", "Fluid");
        MaterialAsset("MAT_ErosiveWater", "Capstone/World/ErosiveWater");
        MaterialAsset("MAT_ErosionRipple", "Capstone/World/ErosionRipple");
        MaterialAsset("MAT_ErosionDebris", "Universal Render Pipeline/Particles/Unlit");
    }
    private static void MaterialAsset(string name, string shader)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat") != null) return;
        var material = new Material(Shader.Find(shader)) { enableInstancing = true }; AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
    }
    internal static void Set(Object target, string name, object value)
    {
        var serialized = new SerializedObject(target); SerializedProperty property = serialized.FindProperty(name);
        if (value is bool b) property.boolValue = b;
        else if (value is int i) property.intValue = i;
        else if (value is float f) property.floatValue = f;
        else if (value is Vector2 v2) property.vector2Value = v2;
        else if (value is Vector3 v3) property.vector3Value = v3;
        else property.objectReferenceValue = value as Object;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
