using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One scene-wide flight coordinator. Owns shared queries, never creature identity or carrying.</summary>
[DisallowMultipleComponent]
public sealed class BirdFlockManager : MonoBehaviour
{
    [Header("World-space Sky Volume")]
    [SerializeField] private Bounds skyBounds = new Bounds(new Vector3(0f, 20f, -15f), new Vector3(100f, 24f, 100f));
    [Min(0.1f)] [SerializeField] private float boundaryMargin = 7f;
    [Min(0f)] [SerializeField] private float boundaryWeight = 3f;
    [SerializeField] private bool showOctree;

    private static readonly Dictionary<int, BirdFlockManager> managers = new Dictionary<int, BirdFlockManager>();
    private static readonly ProfilerMarker stepMarker = new ProfilerMarker("BirdFlock.Simulation");
    private static readonly ProfilerMarker queryMarker = new ProfilerMarker("BirdFlock.NeighborQueries");
    private readonly List<BirdCreature> birds = new List<BirdCreature>(256);
    private readonly List<int> neighbors = new List<int>(256);
    private readonly BirdOctree octree = new BirdOctree();
    private BirdCreature[] activeBirds = new BirdCreature[256];
    private Vector3[] positionsWS = new Vector3[256];
    private Vector3[] velocitiesWS = new Vector3[256];
    private Vector3[] nextVelocitiesWS = new Vector3[256];
    private int registeredScene;

    public Bounds SkyBounds { get => skyBounds; set => skyBounds = value; }
    public int RegisteredBirdCount => birds.Count;
    public int FlyingBirdCount { get; private set; }
    public int CandidateChecks { get; private set; }
    public int OctreeNodeCount => octree.NodeCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => managers.Clear();

    public static BirdFlockManager GetOrCreate(Scene scene, Vector3 firstBirdPositionWS)
    {
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("A bird needs a loaded scene.");
        if (managers.TryGetValue(scene.handle, out BirdFlockManager existing) && existing != null) return existing;
        // Discovery runs only once per scene, not once per bird or simulation step.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            existing = root.GetComponentInChildren<BirdFlockManager>(true);
            if (existing == null) continue;
            managers[scene.handle] = existing;
            return existing;
        }
        GameObject managerObject = new GameObject("Bird Flock Manager (Automatic)");
        SceneManager.MoveGameObjectToScene(managerObject, scene);
        existing = managerObject.AddComponent<BirdFlockManager>();
        existing.skyBounds.center = firstBirdPositionWS;
        return existing;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        registeredScene = gameObject.scene.handle;
        if (managers.TryGetValue(registeredScene, out BirdFlockManager existing) && existing != null && existing != this)
        {
            Debug.LogWarning("Only one BirdFlockManager is needed per scene. Using " + existing.name, this);
            enabled = false;
            return;
        }
        managers[registeredScene] = this;
        // Handles Play Mode with domain/scene reload disabled as well as ordinary scene loading.
        birds.Clear();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (BirdCreature bird in root.GetComponentsInChildren<BirdCreature>())
                if (bird.isActiveAndEnabled) Register(bird);
    }

    private void OnDestroy()
    {
        if (managers.TryGetValue(registeredScene, out BirdFlockManager current) && current == this)
            managers.Remove(registeredScene);
        birds.Clear();
    }

    public void Register(BirdCreature bird)
    {
        if (bird == null || bird.gameObject.scene != gameObject.scene || birds.Contains(bird)) return;
        birds.Add(bird);
        if (birds.Count <= activeBirds.Length) return;
        int capacity = Mathf.NextPowerOfTwo(birds.Count);
        Array.Resize(ref activeBirds, capacity);
        Array.Resize(ref positionsWS, capacity);
        Array.Resize(ref velocitiesWS, capacity);
        Array.Resize(ref nextVelocitiesWS, capacity);
        neighbors.Capacity = capacity;
    }

    public void Unregister(BirdCreature bird) => birds.Remove(bird);

    private void FixedUpdate() => SimulateStep(Time.fixedDeltaTime, Time.fixedTime);

    /// <summary>Snapshots first, computes second, applies last. Also used by explicit editor validation.</summary>
    public void SimulateStep(float deltaTime, float simulationTime)
    {
        if (deltaTime <= 0f) return;
        using (stepMarker.Auto())
        {
            int previousCount = FlyingBirdCount;
            FlyingBirdCount = 0;
            for (int i = birds.Count - 1; i >= 0; i--)
            {
                BirdCreature bird = birds[i];
                if (bird == null || !bird.isActiveAndEnabled || bird.gameObject.scene != gameObject.scene)
                {
                    birds.RemoveAt(i);
                    continue;
                }
                if (!bird.CaptureFlightState(out Vector3 position, out Vector3 velocity)) continue;
                int index = FlyingBirdCount++;
                activeBirds[index] = bird;
                positionsWS[index] = position;
                velocitiesWS[index] = velocity;
            }
            for (int i = FlyingBirdCount; i < previousCount; i++) activeBirds[i] = null;
            octree.Rebuild(positionsWS, FlyingBirdCount);
            CandidateChecks = 0;
            using (queryMarker.Auto())
            {
                for (int i = 0; i < FlyingBirdCount; i++)
                {
                    BirdCreature bird = activeBirds[i];
                    octree.Query(positionsWS[i], bird.SearchRadius, neighbors);
                    CandidateChecks += octree.LastCandidateChecks;
                    nextVelocitiesWS[i] = bird.CalculateFlightVelocity(i, positionsWS, velocitiesWS, neighbors,
                        BoundaryDirection(positionsWS[i]), boundaryWeight, simulationTime, deltaTime);
                }
            }
            for (int i = 0; i < FlyingBirdCount; i++) activeBirds[i].ApplyFlightVelocity(nextVelocitiesWS[i], deltaTime);
        }
    }

    private Vector3 BoundaryDirection(Vector3 positionWS)
    {
        Vector3 margin = Vector3.Min(Vector3.one * boundaryMargin, skyBounds.extents * 0.9f);
        Vector3 minimum = skyBounds.min + margin, maximum = skyBounds.max - margin;
        Vector3 inside = new Vector3(Mathf.Clamp(positionWS.x, minimum.x, maximum.x),
            Mathf.Clamp(positionWS.y, minimum.y, maximum.y), Mathf.Clamp(positionWS.z, minimum.z, maximum.z));
        return inside - positionWS;
    }

    private void OnValidate()
    {
        Vector3 size = skyBounds.size;
        skyBounds.size = new Vector3(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y), Mathf.Max(1f, size.z));
        boundaryMargin = Mathf.Max(0.1f, boundaryMargin);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.7f);
        Gizmos.DrawWireCube(skyBounds.center, skyBounds.size);
        if (!showOctree) return;
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.35f);
        octree.DrawGizmos();
    }
}
