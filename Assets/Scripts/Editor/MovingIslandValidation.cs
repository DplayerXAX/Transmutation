using System;
using UnityEngine;
using UnityEditor;

/// <summary>Small deterministic checks, invoked explicitly and never added to gameplay.</summary>
public static class MovingIslandValidation
{
    public static void StartProbe(float idleSeconds = 30)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in Scene-Fishing first.");
        var probe = new GameObject("Moving Island Representative Probe").AddComponent<MovingIslandPlayModeProbe>();
        probe.idleSeconds = idleSeconds;
        probe.squirrelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Squirrels/Squirrel.prefab");
        probe.birdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Birds/BirdCreature.prefab");
        probe.rootedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Fire/FireSource.prefab");
        probe.eggPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/CeilingBatBugEgg.prefab");
    }
    public static string Run()
    {
        var root = new GameObject("Route validation");
        GameObject fishObject = null;
        try
        {
            var route = root.AddComponent<IslandFlightPath>();
            route.waypoints = new Transform[4];
            Vector3[] points = { Vector3.zero, new(40,10,0), new(40,0,40), new(0,-10,40) };
            for (int i=0;i<4;i++)
            { var child = new GameObject("Point"); child.transform.SetParent(root.transform); child.transform.position = points[i]; route.waypoints[i] = child.transform; }
            route.Rebuild();
            Check(route.Length > 160, "spline length");
            Check(Vector3.Distance(route.Position(0), route.Position(route.Length)) < .001f, "closed route seam");
            Check(Quaternion.Angle(route.Heading(0), route.Heading(route.Length)) < .01f, "closed heading seam");
            float min = float.MaxValue, max = 0;
            for (int i=0;i<100;i++)
            { float step = Vector3.Distance(route.Position(i), route.Position(i+1)); min = Mathf.Min(min,step); max = Mathf.Max(max,step); }
            Check(min > .97f && max < 1.03f, "arc-length speed consistency");
            fishObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FishingSceneSetup.FishPath));
            var fish = fishObject.GetComponent<SeaAngelCreature>();
            CreatureSimulation.DistanceDormant(fish, root, true);
            Check(!fish.SimulationEnabled && fish.SimulationPermitted, "optimization separate from semantic pause");
            fish.SetSimulationEnabled(false);
            CreatureSimulation.DistanceDormant(fish, root, false);
            Check(!fish.SimulationEnabled, "wake cannot undo semantic pause");
            CreatureSimulation.Remove(fish);
            return "PASS: periodic position/heading, arc-length speed, independent distance gate, semantic pause preserved";
        }
        finally
        {
            if (fishObject) UnityEngine.Object.DestroyImmediate(fishObject);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
    static void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Moving island: " + label); }
}
