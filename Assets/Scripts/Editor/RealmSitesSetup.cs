using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the Human Realm exploration sites (Web Gorge, Bubble Well) and the dressing between the spire
/// and the kiln (stream, lamps, light threads, root shelters, crystals, fossils, anemones) to the open scene.
/// They build themselves at runtime next to the Hanging Spire, so only one object is added here.
/// </summary>
public static class RealmSitesSetup
{
    private static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/" + name + ".mat");

    [MenuItem("Tools/Capstone/Realm/Add Web Gorge And Bubble Well To Open Scene")]
    private static void Add()
    {
        var spider = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/SkySpider.prefab");
        var flower = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
        var fireSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/FireSource.prefab");
        var fireFlower = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Fire Flower.prefab");
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Stone.mat");
        var silk = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_Silk.mat");
        var ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_Ink.mat");
        var seed = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_Seed.mat");
        if (spider == null || flower == null || fireSource == null || fireFlower == null || stone == null || silk == null)
        {
            EditorUtility.DisplayDialog("Realm Sites", "Missing the spider / bubble flower / fire prefabs or the world materials.", "OK");
            return;
        }

        GameObject holder = GameObject.Find("Human Realm Sites");
        if (holder == null)
        {
            holder = new GameObject("Human Realm Sites");
            Undo.RegisterCreatedObjectUndo(holder, "Add Realm Sites");
        }

        WebGorge gorge = holder.GetComponent<WebGorge>();
        if (gorge == null) gorge = Undo.AddComponent<WebGorge>(holder);
        Undo.RecordObject(gorge, "Set Web Gorge");
        gorge.Setup(spider.GetComponent<SkySpider>(), flower.GetComponent<BubbleFlower>(), stone, silk, ink, seed);
        EditorUtility.SetDirty(gorge);

        BubbleWell well = holder.GetComponent<BubbleWell>();
        if (well == null) well = Undo.AddComponent<BubbleWell>(holder);
        Undo.RecordObject(well, "Set Bubble Well");
        well.Setup(flower.GetComponent<BubbleFlower>(), fireSource.GetComponent<FireSource>(), fireFlower.GetComponent<FireFlower>(), stone, ink, seed);
        EditorUtility.SetDirty(well);

        RealmDressing dressing = holder.GetComponent<RealmDressing>();
        if (dressing == null) dressing = Undo.AddComponent<RealmDressing>(holder);
        Undo.RecordObject(dressing, "Set Realm Dressing");
        dressing.Setup(Load("MAT_Realm_Lamp"), Load("MAT_Realm_Root"), Load("MAT_Realm_Anemone"), Load("MAT_Realm_Crystal"),
            Load("MAT_Realm_Film"), Load("MAT_Realm_Thread"), Load("MAT_Realm_Fossil"), Load("MAT_Realm_Stream"),
            Load("MAT_Realm_Slime"), Load("MAT_Realm_Mud"));
        EditorUtility.SetDirty(dressing);

        SpireOvergrowth overgrowth = holder.GetComponent<SpireOvergrowth>();
        if (overgrowth == null) overgrowth = Undo.AddComponent<SpireOvergrowth>(holder);
        Undo.RecordObject(overgrowth, "Set Spire Overgrowth");
        overgrowth.Setup(Load("MAT_Realm_Vein"), Load("MAT_Realm_Frost"), Load("MAT_Realm_WebStrand"), Load("MAT_Realm_WebFilm"),
            Load("MAT_Realm_Lamp"), Load("MAT_Realm_Mushroom"));
        EditorUtility.SetDirty(overgrowth);

        RealmFlora flora = holder.GetComponent<RealmFlora>();
        if (flora == null) flora = Undo.AddComponent<RealmFlora>(holder);
        Undo.RecordObject(flora, "Set Realm Flora");
        flora.Setup(Load("MAT_Realm_Fossil"), Load("MAT_Realm_Frost"), Load("MAT_Realm_Mushroom"), Load("MAT_Realm_MushroomBlue"),
            Load("MAT_Realm_Coral"), Load("MAT_Realm_Jelly"), Load("MAT_Realm_Slime"), Load("MAT_Realm_Anemone"));
        EditorUtility.SetDirty(flora);

        // A kelp forest round the first side tower and a geode field round the second.
        TowerGroves groves = holder.GetComponent<TowerGroves>();
        if (groves == null) groves = Undo.AddComponent<TowerGroves>(holder);
        Undo.RecordObject(groves, "Set Tower Groves");
        groves.Setup(Load("MAT_Realm_Kelp"), Load("MAT_Realm_Jellyfish"), Load("MAT_Realm_Geode"), Load("MAT_Realm_Amethyst"), Load("MAT_Realm_Basalt"));
        EditorUtility.SetDirty(groves);

        // The light pillar through the spire, lit once its crown seed is taken.
        SpireBeam beam = holder.GetComponent<SpireBeam>();
        if (beam == null) beam = Undo.AddComponent<SpireBeam>(holder);
        Undo.RecordObject(beam, "Set Spire Beam");
        beam.Setup(Load("MAT_Realm_Beam"), Load("MAT_Realm_Lamp"));
        EditorUtility.SetDirty(beam);

        // Stops self-spreading fire flowers from piling up over a long session (a cause of slowdown).
        if (holder.GetComponent<RealmPopulation>() == null) Undo.AddComponent<RealmPopulation>(holder);

        EditorSceneManager.MarkSceneDirty(holder.scene);
        Selection.activeGameObject = holder;
        Debug.Log("Realm sites and dressing added. They appear between the Hanging Spire and the Bone Kiln in Play Mode. Save the scene to keep them.");
    }
}
