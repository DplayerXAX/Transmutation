using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Pure district rules plus the real worker placement pipeline.</summary>
public static class RealmLayoutValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Realm layout: " + message);
            checks++;
        }
        var layout = new RealmDecorLayout.Snapshot { active = true, courtyard = 18f,
            tower = Vector3.zero, spawn = Vector3.back * 70f, kelp = Vector3.left * 60f,
            crystal = Vector3.right * 60f, bone = Vector3.forward * 80f };
        Check(layout.Weight(DecorShapes.Shape.Pebbles, false, Vector3.right * 10f) == 0f, "main tower courtyard stays clear");
        Check(layout.OnApproach(Vector3.back * 40f) && layout.OnApproach(Vector3.left * 30f)
            && layout.OnApproach(Vector3.right * 30f) && layout.OnApproach(Vector3.forward * 40f), "all four landmark approaches have clear corridors");
        Vector3 wet = layout.kelp + Vector3.forward * 20f;
        Vector3 crystal = layout.crystal + Vector3.forward * 20f;
        Vector3 bone = layout.bone + Vector3.right * 20f;
        Check(layout.District(wet) == 0 && layout.Weight(DecorShapes.Shape.Fronds, false, wet) > 0.9f,
            "wet district supports vegetation");
        Check(layout.Weight(DecorShapes.Shape.Shards, false, wet) == 0f, "wet district does not mix in shard fields");
        Check(layout.District(crystal) == 1 && layout.Weight(DecorShapes.Shape.Shards, false, crystal) > 0.9f,
            "geode district supports stone and crystals");
        Check(layout.Weight(DecorShapes.Shape.Bladders, false, crystal) == 0f, "geode district does not mix in sacs");
        Check(layout.District(bone) == 2 && layout.Weight(DecorShapes.Shape.Pebbles, false, bone) > 0.9f,
            "bone district keeps sparse supporting stones");
        Check(layout.Weight(DecorShapes.Shape.Fronds, false, bone) == 0f, "bone district leaves its skeleton as the main silhouette");
        Check(layout.Weight(DecorShapes.Shape.Fronds, false, Vector3.back * 40f) == 0f,
            "equal-distance theme boundary has a quiet transition");
        Check(layout.Weight(DecorShapes.Shape.Shards, true, wet) == 0f
            && layout.Weight(DecorShapes.Shape.ShellTentacle, true, wet) > 0.9f, "underground wet district has its own matching family");
        Check(layout.Weight(DecorShapes.Shape.Shards, true, crystal) > 0.9f
            && layout.Weight(DecorShapes.Shape.ShellTentacle, true, crystal) == 0f, "underground crystals stay in the mineral district");
        Check(layout.Weight(DecorShapes.Shape.Fronds, false, wet + Vector3.up * 100f) == layout.Weight(DecorShapes.Shape.Fronds, false, wet),
            "districts are stable across terrain elevations");
        var go = new GameObject("Decoration pipeline"); go.SetActive(false);
        var decorator = go.AddComponent<WorldDecorator>();
        try
        {
            Type jobType = typeof(WorldDecorator).GetNestedType("Job", BindingFlags.NonPublic);
            var variants = typeof(WorldDecorator).GetMethod("GetVariants", Private);
            variants.Invoke(decorator, new object[] { DecorShapes.Shape.Fronds });
            variants.Invoke(decorator, new object[] { DecorShapes.Shape.Shards });
            var kinds = new[] {
                new WorldDecorator.Kind { shape = DecorShapes.Shape.Fronds, group = WorldDecorator.Group.Organic,
                    clustersPer1000m2 = 12f, membersPerCluster = new Vector2Int(2, 4), patchCoverage = 1f, normalYRange = new Vector2(0.5f, 1f) },
                new WorldDecorator.Kind { shape = DecorShapes.Shape.Shards, group = WorldDecorator.Group.Stone,
                    clustersPer1000m2 = 12f, membersPerCluster = new Vector2Int(2, 4), patchCoverage = 1f, normalYRange = new Vector2(0.5f, 1f) }
            };
            object Compute(bool themed)
            {
                object job = Activator.CreateInstance(jobType, true);
                void Set(string field, object value) => jobType.GetField(field).SetValue(job, value);
                Set("vertices", new[] { new Vector3(-85f, 0f, -30f), new Vector3(-35f, 0f, -30f),
                    new Vector3(-85f, 0f, 30f), new Vector3(-35f, 0f, 30f) });
                Set("normals", new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
                Set("triangles", new[] { new[] { 0, 2, 1, 1, 2, 3 } });
                Set("localToWorld", Matrix4x4.identity); Set("worldToLocal", Matrix4x4.identity);
                Set("randomSeed", 41); Set("spawn", Vector3.forward * 500f); Set("kinds", kinds);
                Set("layout", themed ? layout : default(RealmDecorLayout.Snapshot));
                typeof(WorldDecorator).GetMethod("Compute", Private).Invoke(decorator, new[] { job });
                return job;
            }
            object themed = Compute(true);
            var buffers = (IDictionary)jobType.GetField("result").GetValue(themed);
            int vertices = 0, wrongFamily = 0;
            foreach (DictionaryEntry entry in buffers)
            {
                var list = (List<Vector3>)entry.Value.GetType().GetField("vertices").GetValue(entry.Value);
                if ((WorldDecorator.Group)entry.Key == WorldDecorator.Group.Organic) vertices += list.Count;
                else wrongFamily += list.Count;
            }
            Check(vertices > 0 && wrongFamily == 0, "real worker emits vegetation and rejects stone meshes in the wet district");
            object mixed = Compute(false);
            Check((int)jobType.GetField("pieces").GetValue(themed) < (int)jobType.GetField("pieces").GetValue(mixed),
                "theme filtering actually reduces overlapping decoration families");
            Debug.Log($"Realm layout validation passed: {checks} assertions.");
        }
        finally { Object.DestroyImmediate(go); }
    }
}
