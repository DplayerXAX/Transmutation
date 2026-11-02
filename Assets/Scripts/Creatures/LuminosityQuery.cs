using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public readonly struct LuminosityReading
{
    public ICreatureStats Source { get; }
    public float EmittedLuminosity { get; }
    public float Distance { get; }
    public float PerceivedBrightness { get; }
    public LuminosityReading(ICreatureStats source, float emitted, float distance, float perceived)
    { Source = source; EmittedLuminosity = emitted; Distance = distance; PerceivedBrightness = perceived; }
}

/// <summary>Scene-local light discovery. Observer code owns radius, threshold and reactions.</summary>
public static class LuminosityQuery
{
    public const int DefaultObstructionMask = (1 << 0) | (1 << 7) | (1 << 8);
    private sealed class Snapshot
    {
        public readonly List<ICreatureStats> Members = new List<ICreatureStats>(256);
        public readonly List<ICreatureStats> Sources = new List<ICreatureStats>(256);
        public readonly List<int> Candidates = new List<int>(64);
        public readonly BirdOctree Tree = new BirdOctree();
        public Vector3[] Positions = new Vector3[256];
        public RaycastHit[] Hits = new RaycastHit[16];
        public float NextRefresh = float.NegativeInfinity;
    }
    private static readonly Dictionary<int, Snapshot> scenes = new Dictionary<int, Snapshot>();
    public static int SnapshotCount { get; private set; }
    public static int LastCandidateChecks { get; private set; }

    private static bool Exists(ICreatureStats source) => source is Object obj && obj != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        scenes.Clear(); SnapshotCount = LastCandidateChecks = 0;
        SceneManager.sceneUnloaded -= RemoveScene;
        SceneManager.sceneUnloaded += RemoveScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreMembership()
    {
        // One startup scan also supports entering Play with domain/scene reload disabled.
        foreach (Creature c in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
            if (c.isActiveAndEnabled) Register(c);
        foreach (PlayerCreatureStats p in Object.FindObjectsByType<PlayerCreatureStats>(FindObjectsSortMode.None))
            if (p.isActiveAndEnabled) Register(p);
    }

    private static void RemoveScene(Scene scene) => scenes.Remove(scene.handle);

    internal static void Register(ICreatureStats source)
    {
        if (!Exists(source)) return;
        int key = source.StatRoot.gameObject.scene.handle;
        if (!scenes.TryGetValue(key, out Snapshot snapshot)) scenes.Add(key, snapshot = new Snapshot());
        if (!snapshot.Members.Contains(source)) snapshot.Members.Add(source);
        snapshot.NextRefresh = float.NegativeInfinity;
    }

    internal static void Unregister(ICreatureStats source)
    {
        // A body may have moved scenes since registration. Only lifecycle changes use this path.
        foreach (Snapshot snapshot in scenes.Values)
            if (snapshot.Members.Remove(source)) snapshot.NextRefresh = float.NegativeInfinity;
    }

    /// <summary>Results are unsorted. Positions have at most 0.1s snapshot latency; brightness is read live.</summary>
    public static void Nearby(Scene scene, Vector3 observationPosition, float detectionRadius,
        float minimumBrightness, Transform observerRoot, LayerMask obstructionMask, List<LuminosityReading> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear(); LastCandidateChecks = 0;
        if (float.IsNaN(detectionRadius) || float.IsInfinity(detectionRadius) || detectionRadius < 0f)
            throw new ArgumentOutOfRangeException(nameof(detectionRadius));
        if (float.IsNaN(minimumBrightness) || float.IsInfinity(minimumBrightness) || minimumBrightness < 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumBrightness));
        if (!scene.IsValid() || !scene.isLoaded || detectionRadius <= 0f || !scenes.TryGetValue(scene.handle, out Snapshot snapshot)) return;
        if (Time.time >= snapshot.NextRefresh)
        {
            snapshot.Sources.Clear();
            for (int i = snapshot.Members.Count - 1; i >= 0; i--)
            {
                ICreatureStats source = snapshot.Members[i];
                if (!Exists(source) || source.StatRoot.gameObject.scene != scene)
                { snapshot.Members.RemoveAt(i); continue; }
                if (source.IsAvailableForPerception) snapshot.Sources.Add(source);
            }
            if (snapshot.Positions.Length < snapshot.Sources.Count)
                Array.Resize(ref snapshot.Positions, Mathf.NextPowerOfTwo(snapshot.Sources.Count));
            for (int i = 0; i < snapshot.Sources.Count; i++) snapshot.Positions[i] = snapshot.Sources[i].EmissionPosition;
            snapshot.Tree.Rebuild(snapshot.Positions, snapshot.Sources.Count);
            snapshot.NextRefresh = Time.time + 0.1f;
            SnapshotCount++;
        }
        snapshot.Tree.Query(observationPosition, detectionRadius, snapshot.Candidates);
        LastCandidateChecks = snapshot.Tree.LastCandidateChecks;
        for (int i = 0; i < snapshot.Candidates.Count; i++)
        {
            ICreatureStats source = snapshot.Sources[snapshot.Candidates[i]];
            if (!Exists(source) || !source.IsAvailableForPerception || source.StatRoot == observerRoot ||
                source.StatRoot.gameObject.scene != scene) continue;
            float emitted = source.Luminosity;
            if (emitted <= 0f) continue;
            Vector3 delta = source.EmissionPosition - observationPosition;
            float squareDistance = delta.sqrMagnitude;
            if (squareDistance > detectionRadius * detectionRadius) continue;
            float perceived = emitted / (1f + squareDistance);
            if (perceived < minimumBrightness) continue;
            float distance = Mathf.Sqrt(squareDistance);
            if (distance > 0.0001f && Obstructed(scene.GetPhysicsScene(), observationPosition,
                delta / distance, distance, observerRoot, source.StatRoot, obstructionMask, snapshot)) continue;
            results.Add(new LuminosityReading(source, emitted, distance, perceived));
        }
    }

    private static bool Obstructed(PhysicsScene physics, Vector3 from, Vector3 direction, float distance,
        Transform observer, Transform source, int mask, Snapshot snapshot)
    {
        int count;
        while ((count = physics.Raycast(from, direction, snapshot.Hits, distance, mask, QueryTriggerInteraction.Ignore)) == snapshot.Hits.Length)
            Array.Resize(ref snapshot.Hits, snapshot.Hits.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Transform hit = snapshot.Hits[i].collider.transform;
            if (hit.IsChildOf(source) || (observer != null && hit.IsChildOf(observer))) continue;
            return true;
        }
        return false;
    }
}
