using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Compatibility samples are captured before a generator change and compared afterward.</summary>
public static class TerrainRecipeValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Baseline = "Temp/MountainLegacyBaseline.json";
    [Serializable] private sealed class Record { public string path; public float[] values; public Vector3 grid; public Bounds bounds; }
    [Serializable] private sealed class Snapshot { public List<Record> records = new List<Record>(); }

    public static object CaptureLegacy()
    {
        var snapshot = SampleLegacy();
        File.WriteAllText(Baseline, JsonUtility.ToJson(snapshot));
        return new { layouts = snapshot.records.Count, samples = snapshot.records.Sum(x => x.values.Length) };
    }

    public static object VerifyLegacy()
    {
        var before = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Baseline));
        var after = SampleLegacy();
        Require(before.records.Count == after.records.Count, "Legacy layout count changed");
        int samples = 0;
        foreach (var a in before.records)
        {
            var b = after.records.Single(x => x.path == a.path);
            Require(a.grid == b.grid && a.bounds == b.bounds, "Legacy grid or bounds moved: " + a.path);
            for (int i = 0; i < a.values.Length; i++)
            { Require(a.values[i] == b.values[i], "Legacy shape changed: " + a.path + " sample " + i); samples++; }
        }
        return new { layouts = before.records.Count, identicalSamples = samples, identicalBounds = true };
    }

    private static Snapshot SampleLegacy()
    {
        var snapshot = new Snapshot();
        var paths = AssetDatabase.FindAssets("t:TerrainSculptData").Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToList();
        paths.Insert(0, "default");
        Type type = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
        foreach (string path in paths)
        {
            var go = new GameObject("Recipe compatibility probe") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var world = go.AddComponent<ProceduralWorld>();
                object field;
                float headroom = 0f;
                if (path == "default") field = JsonUtility.FromJson(world.CaptureLayout(out _), type);
                else
                {
                    var data = AssetDatabase.LoadAssetAtPath<TerrainSculptData>(path);
                    if (!data.HasLayout) continue;
                    field = JsonUtility.FromJson(data.Layout, type); headroom = data.Headroom;
                }
                if ((bool?)type.GetProperty("IsVolumeRecipe")?.GetValue(field) == true) continue;
                typeof(ProceduralWorld).GetField("field", Private).SetValue(world, field);
                typeof(ProceduralWorld).GetMethod("PrepareGrid", Private).Invoke(world, new object[] { headroom });
                var height = (Func<float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float>),field,"SurfaceHeight");
                var ceiling = (Func<float,float>)Delegate.CreateDelegate(typeof(Func<float,float>),field,"CeilingHeight");
                var floor = (Func<float,float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float,float>),field,"InnerGroundHeight");
                var density = (Func<Vector3,float,float,float,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float,float,float,float>),field,"Density");
                Vector2 size = world.WorldSize; var values = new List<float>(); var random = new System.Random(48731);
                for (int i=0; i<256; i++)
                {
                    float x=((float)random.NextDouble()-0.5f)*size.x, z=((float)random.NextDouble()-0.5f)*size.y;
                    float h=height(x,z), c=ceiling(h), g=floor(x,z,h);
                    float y=Mathf.Lerp(g-20f,h+20f,(float)random.NextDouble());
                    values.Add(h); values.Add(c); values.Add(g); values.Add(density(new Vector3(x,y,z),h,c,g));
                }
                snapshot.records.Add(new Record { path=path, values=values.ToArray(), grid=world.GridMinimum, bounds=world.VolumeBounds });
            }
            finally { Object.DestroyImmediate(go); }
        }
        return snapshot;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    [Serializable] private sealed class RecipeLayout { public int seed; public Vector2 worldSize; public TerrainGenerationRecipe recipe; }
    private static ProceduralWorld erosionWorld;
    private static Vector3 erosionPoint;
    private static float densityBefore, budgetBefore;
    private static int changedEvents;
    private static Action<Bounds> erosionListener;

    public static object BeginMountainErosion()
    {
        Require(Application.isPlaying,"Erosion check requires Play mode");
        erosionWorld=Object.FindFirstObjectByType<ProceduralWorld>();
        Require(erosionWorld!=null && erosionWorld.GenerationComplete && erosionWorld.IsVolumeTerrain,"Wait for Mountain to finish loading");
        var layout=JsonUtility.FromJson<RecipeLayout>(erosionWorld.SculptData.Layout);
        var shape=new MountainDensity(layout.seed,layout.worldSize,layout.recipe);
        var island=shape.Islands.OrderByDescending(x=>x.centre.y).First();
        erosionPoint=island.centre+Vector3.up*25f;
        Require(erosionWorld.TryFindSurfaceBelow(erosionPoint,out float top),"Runtime skyland must have a surface");
        erosionPoint.y=top-3f;densityBefore=erosionWorld.SampleDensity(erosionPoint);
        Require(densityBefore>0f,"Erosion probe must begin inside rock");
        budgetBefore=erosionWorld.DensityBudgetPerSecond;
        var settings=new SerializedObject(erosionWorld);settings.FindProperty("densityBudgetPerSecond").floatValue=4000f;settings.ApplyModifiedPropertiesWithoutUndo();
        changedEvents=0;erosionListener=_=>changedEvents++;erosionWorld.TerrainChanged+=erosionListener;
        erosionWorld.Erode(erosionPoint,9f,100f);
        return new { densityBefore, point=erosionPoint, top };
    }

    public static object FinishMountainErosion()
    {
        Require(erosionWorld!=null,"Begin the erosion check first");
        if(erosionWorld.UpdatesPending)return new { done=false, pending=erosionWorld.PendingSampleCount };
        try
        {
            float after=erosionWorld.SampleDensity(erosionPoint);
            Require(after<densityBefore-0.1f,"Erosion must remove generated skyland density");
            Require(changedEvents>0 && erosionWorld.ModifiedSampleCount>0,"Shared terrain events and edit storage must update");
            Vector3 above=new Vector3(erosionPoint.x,erosionPoint.y+30f,erosionPoint.z);
            Require(erosionWorld.TryFindSurfaceBelow(above,out float sampled),"An eroded surface must remain queryable");
            var hits=Physics.RaycastAll(above,Vector3.down,800f,1<<erosionWorld.TerrainLayer).Where(h=>erosionWorld.OwnsCollider(h.collider)).OrderBy(h=>h.distance).ToArray();
            Require(hits.Length>0 && Mathf.Abs(hits[0].point.y-sampled)<0.3f,"Eroded collider and density must match");
            Require(SurfaceGeometry.IsTerrain(hits[0].collider),"Existing surface tools must recognize Mountain chunks");
            erosionWorld.Generate(true);
            Require(Mathf.Abs(erosionWorld.SampleDensity(erosionPoint)-after)<0.0001f,"Identical recipe regeneration must retain runtime erosion");
            return new { done=true, densityBefore, densityAfter=after, changedEvents, modifiedSamples=erosionWorld.ModifiedSampleCount, colliderError=Mathf.Abs(hits[0].point.y-sampled), erosionSurvivesRegeneration=true };
        }
        finally
        {
            erosionWorld.TerrainChanged-=erosionListener;
            var settings=new SerializedObject(erosionWorld);settings.FindProperty("densityBudgetPerSecond").floatValue=budgetBefore;settings.ApplyModifiedPropertiesWithoutUndo();
            erosionWorld=null;
        }
    }

    public static object RunMountainTools()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run the sculpt checks in Edit mode.");
        Scene original = SceneManager.GetActiveScene();
        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        TerrainSculptData data=null, copy=null;
        int checks=0;
        void Check(bool ok,string message) { Require(ok,message); checks++; }
        try
        {
            var go=new GameObject("Mountain tools validation"); go.layer=7; go.tag="Ground";
            SceneManager.MoveGameObjectToScene(go,test);
            var world=go.AddComponent<ProceduralWorld>(); var settings=new SerializedObject(world);
            settings.FindProperty("worldSize").vector2Value=new Vector2(288f,288f);
            settings.FindProperty("cellSize").floatValue=3f;
            settings.FindProperty("chunkCells").intValue=16;
            settings.FindProperty("seed").intValue=1707;
            var recipe=settings.FindProperty("generationRecipe");
            recipe.FindPropertyRelative("kind").enumValueIndex=(int)TerrainRecipeKind.AmplifiedMountains;
            recipe.FindPropertyRelative("safeSpawn").vector3Value=new Vector3(-110f,148f,-110f);
            recipe.FindPropertyRelative("islandCount").intValue=1;
            recipe.FindPropertyRelative("islandElevation").vector2Value=new Vector2(420f,440f);
            recipe.FindPropertyRelative("islandRadius").vector2Value=new Vector2(60f,65f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            data=ScriptableObject.CreateInstance<TerrainSculptData>();
            data.Freeze(world.CaptureLayout(out float[] expected),expected,"isolated recipe tool checks");
            settings.Update();settings.FindProperty("sculptData").objectReferenceValue=data;settings.ApplyModifiedPropertiesWithoutUndo();
            world.Generate(true);
            Check(world.CanSculpt && world.GenerationComplete,"Mountain recipe must use the existing sculpt tools");
            var savedLayout=JsonUtility.FromJson<RecipeLayout>(data.Layout);
            var islandShape=new MountainDensity(savedLayout.seed,savedLayout.worldSize,savedLayout.recipe);
            Vector3 centre=islandShape.Islands[0].centre;
            Vector3 point=new Vector3(centre.x,530f,centre.z);
            Check(world.TryFindSurfaceBelow(point,out float islandTop) && islandTop>390f,"Skyland surface is found above the mountains");
            float belowIsland=islandTop-12f;
            while(belowIsland>220f && world.SampleDensity(new Vector3(centre.x,belowIsland,centre.z))>0f)belowIsland-=3f;
            Check(world.TryFindSurfaceAbove(new Vector3(centre.x,belowIsland,centre.z),out float underside) && underside<islandTop-20f,"Skyland underside is found separately");
            Check(world.TryFindSurfaceBelow(new Vector3(centre.x,underside-6f,centre.z),out float ground) && ground<underside-15f,"A surface query below the skyland finds the lower world");
            bool ColliderAt(Vector3 p,out float y)
            {
                var hits=Physics.RaycastAll(p,Vector3.down,800f,1<<world.TerrainLayer).Where(h=>world.OwnsCollider(h.collider)).OrderBy(h=>h.distance).ToArray();
                y=hits.Length>0?hits[0].point.y:0f;return hits.Length>0;
            }
            Check(ColliderAt(point,out float colliderTop) && Mathf.Abs(colliderTop-islandTop)<0.25f,"Skyland density and collider agree");
            var normals=new Dictionary<Vector3Int,Vector3>();int shared=0;
            foreach(var filter in world.GetComponentsInChildren<MeshFilter>())
            {
                var mesh=filter.sharedMesh;var vertices=mesh.vertices;var ns=mesh.normals;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=filter.transform.TransformPoint(vertices[i]);
                    if(Mathf.Abs(p.x)>0.0001f)continue;
                    var key=Vector3Int.RoundToInt(p*10000f);
                    if(normals.TryGetValue(key,out var previous)) { Check(Vector3.Dot(previous,ns[i])>0.9999f,"Adjacent chunks must have matching normals");shared++; }
                    else normals.Add(key,ns[i]);
                }
            }
            Check(shared>10,"The seam test must include actual shared vertices");
            point=new Vector3(0f,530f,centre.z);
            Check(world.TryFindSurfaceBelow(point,out float seamTop) && seamTop>390f,"A skyland crosses the chunk boundary");
            Vector3 carve=new Vector3(0f,seamTop-3f,centre.z);float baseline=world.SampleDensity(carve);
            world.BeginSculptStroke();world.Sculpt(TerrainSculptTool.Carve,carve,9f,30f,0f);world.EndSculptStroke();
            Check(world.SampleDensity(carve)<0f && world.SampleDensity(carve)<baseline,"Existing Carve removes skyland rock across a chunk seam");
            Check(data.Edits.Count>0,"The existing sculpt asset stores recipe edits");
            float carved=world.SampleDensity(carve);
            copy=ScriptableObject.CreateInstance<TerrainSculptData>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(data),copy);
            Check(copy.Edits.Count==data.Edits.Count && copy.Layout==data.Layout,"Recipe and sculpt edits survive serialization");
            settings.Update();settings.FindProperty("sculptData").objectReferenceValue=copy;settings.ApplyModifiedPropertiesWithoutUndo();
            world.Generate(true);
            Check(Mathf.Abs(world.SampleDensity(carve)-carved)<0.0001f,"Regeneration retains the saved skyland carving");
            Check(ColliderAt(point,out float carvedTop) && world.TryFindSurfaceBelow(point,out float sampledTop) && Mathf.Abs(carvedTop-sampledTop)<0.25f,"Carved collider and surface queries remain aligned");
            copy.ClearEdits();world.SyncSculptFromData();
            Check(Mathf.Abs(world.SampleDensity(carve)-baseline)<0.0001f,"Clearing sculpt edits restores the original recipe");
            return new { checks, sharedSeamVertices=shared, islandTop, underside, ground, savedSculptSamples=data.Edits.Count };
        }
        finally
        {
            EditorSceneManager.CloseScene(test,true);SceneManager.SetActiveScene(original);
            if(data!=null)Object.DestroyImmediate(data);if(copy!=null)Object.DestroyImmediate(copy);
        }
    }
}
