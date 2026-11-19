using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps far, purely visual parts of Scene_World asleep until the player comes near. The listed groups are switched
/// off before the scene wakes (Editor/WorldStreamingScenePrep), so their renderers and colliders cost nothing at load.
/// Here each placed object stays off until the player is within the group's radius, then objects are switched on
/// nearest first inside a small time budget per frame. Objects stay on afterwards.
/// </summary>
public sealed class WorldStreaming : MonoBehaviour
{
    public const string RootName = "World — combined map";
    public const string Marker = " [streamed]";

    public readonly struct Group
    {
        public readonly string path;
        public readonly float radius;
        public Group(string path, float radius) { this.path = path; this.radius = radius; }
    }

    // Decoration only: nothing here is looked up by gameplay code. Paths start below the world root.
    public static readonly Group[] Groups =
    {
        new Group("09 Region decor — WorldDecor prefabs", 150f),
        new Group("02 Placeholder forest", 220f),
        new Group("04b Placeholder ruins — broken columns and walls", 220f),
        new Group("08 Crystal sky island — Scene_CisternBasin (marcus)/Cistern Basin Decor", 200f),
    };

    private const float ScanInterval = 0.25f, LoadingBudgetMs = 10f, PlayBudgetMs = 2.5f;

    private sealed class Unit
    {
        public GameObject target;
        public Vector3 centre;
        public float reach;
        public int group;
        public float distance;
    }

    private static WorldStreaming current;
    private readonly List<Unit> asleep = new List<Unit>();
    private readonly List<Unit> ready = new List<Unit>();
    private readonly Stopwatch timer = new Stopwatch();
    private double[] groupMs;
    private int[] groupCount;
    private Transform player;
    private float nextScan;
    private int firstWave = -1, frames;
    private bool firstWaveDone;

    /// <summary>True while the objects around the starting point are still being switched on.</summary>
    public static bool LoadingNearPlayer => current != null && !current.firstWaveDone;

    /// <summary>0..1 share of the first wave (the objects around the start) that is on.</summary>
    public static float NearProgress
    {
        get
        {
            if (current == null || current.firstWaveDone) return 1f;
            if (current.firstWave <= 0) return 0f;
            return 1f - Mathf.Clamp01((float)current.ready.Count / current.firstWave);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        Prepare(SceneManager.GetActiveScene());
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode) => Prepare(scene);

    /// <summary>The world root of a scene, or null when the scene has none.</summary>
    public static Transform FindRoot(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == RootName) return root.transform;
        return null;
    }

    /// <summary>Follows a group path; a part may carry the marker added by the scene preparation.</summary>
    public static Transform FindGroup(Transform root, string path)
    {
        Transform at = root;
        foreach (string part in path.Split('/'))
        {
            Transform next = null;
            foreach (Transform child in at)
                if (child.name == part || child.name == part + Marker) { next = child; break; }
            if (next == null) return null;
            at = next;
        }
        return at;
    }

    private static void Prepare(Scene scene)
    {
        Transform root = FindRoot(scene);
        if (root == null) return;
        WorldStreaming streaming = null;
        for (int i = 0; i < Groups.Length; i++)
        {
            Transform group = FindGroup(root, Groups[i].path);
            if (group == null || !group.name.EndsWith(Marker)) continue;
            if (streaming == null)
            {
                var host = new GameObject("World streaming");
                SceneManager.MoveGameObjectToScene(host, scene);
                streaming = host.AddComponent<WorldStreaming>();
                streaming.groupMs = new double[Groups.Length];
                streaming.groupCount = new int[Groups.Length];
                current = streaming;
            }
            streaming.Collect(group, i);
            group.name = group.name.Substring(0, group.name.Length - Marker.Length);
            // The objects are off now, so waking the group itself is free.
            group.gameObject.SetActive(true);
        }
    }

    // A plain folder (Transform only) is opened; anything else is one object to wake. Objects already off stay off.
    private void Collect(Transform parent, int group)
    {
        foreach (Transform child in parent)
        {
            if (!child.gameObject.activeSelf) continue;
            if (child.childCount > 0 && child.GetComponents<Component>().Length == 1) { Collect(child, group); continue; }
            var unit = new Unit { target = child.gameObject, centre = child.position, group = group };
            var shape = child.GetComponentInChildren<Renderer>(true);
            if (shape != null && shape.bounds.extents.sqrMagnitude > 0f) { unit.centre = shape.bounds.center; unit.reach = shape.bounds.extents.magnitude; }
            child.gameObject.SetActive(false);
            asleep.Add(unit);
            groupCount[group]++;
        }
    }

    private void Update()
    {
        frames++;
        if (player == null)
        {
            var controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller != null) player = controller.transform;
            else if (Camera.main != null) player = Camera.main.transform;
        }
        // The player is placed during the first frames, so the first look around waits for that.
        if (player == null || frames < 3) return;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            Scan();
            if (firstWave < 0) firstWave = ready.Count;
        }
        Wake(firstWaveDone ? PlayBudgetMs : LoadingBudgetMs);
        if (!firstWaveDone && ready.Count == 0)
        {
            firstWaveDone = true;
            Report();
        }
        if (firstWaveDone && asleep.Count == 0 && ready.Count == 0) enabled = false;
    }

    private void Scan()
    {
        Vector3 at = player.position;
        for (int i = asleep.Count - 1; i >= 0; i--)
        {
            Unit unit = asleep[i];
            if (Vector3.Distance(at, unit.centre) - unit.reach > Groups[unit.group].radius) continue;
            ready.Add(unit);
            asleep[i] = asleep[asleep.Count - 1];
            asleep.RemoveAt(asleep.Count - 1);
        }
        if (ready.Count == 0) return;
        foreach (Unit unit in ready) unit.distance = Vector3.Distance(at, unit.centre) - unit.reach;
        // Farthest first in the list, so the nearest is taken from the end.
        ready.Sort((a, b) => b.distance.CompareTo(a.distance));
    }

    private void Wake(float budgetMs)
    {
        timer.Restart();
        while (ready.Count > 0 && timer.Elapsed.TotalMilliseconds < budgetMs)
        {
            Unit unit = ready[ready.Count - 1];
            ready.RemoveAt(ready.Count - 1);
            if (unit.target == null) continue;
            double before = timer.Elapsed.TotalMilliseconds;
            unit.target.SetActive(true);
            groupMs[unit.group] += timer.Elapsed.TotalMilliseconds - before;
        }
    }

    private void Report()
    {
#if UNITY_EDITOR
        var text = new System.Text.StringBuilder($"World streaming: {firstWave} objects woken at the start, {asleep.Count} still asleep.");
        for (int i = 0; i < Groups.Length; i++)
            text.Append($" [{Groups[i].path}: {groupCount[i]} objects, {groupMs[i]:0} ms so far]");
        UnityEngine.Debug.Log(text.ToString());
#endif
    }

    private void OnDestroy() { if (current == this) current = null; }
}
