using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Development placement tool only. Spawned birds have no permanent connection to this area.</summary>
[DisallowMultipleComponent]
public sealed class BirdSpawnArea : MonoBehaviour
{
    [SerializeField] private BirdCreature birdPrefab;
    [Min(0)] [SerializeField] private int birdCount = 24;
    [Min(0f)] [SerializeField] private float spawnRadius = 5f;
    [SerializeField] private int seed = 1234;
    [Header("Flight Speed")]
    [Tooltip("Apply this area's speed range to spawned birds. Otherwise inherit the bird prefab's flight speed.")]
    [SerializeField] private bool overrideFlightSpeed;
    [Tooltip("Minimum and maximum flight speed in metres per second; used when Override Flight Speed is enabled.")]
    [SerializeField] private Vector2 flightSpeedRange = new Vector2(3f, 6f);
    private bool spawned;

    public int BirdCount => birdCount;
    public bool HasSpawned => spawned;

    public void SetFlightSpeedOverride(bool useOverride, Vector2 range)
    {
        overrideFlightSpeed = useOverride;
        flightSpeedRange.x = Mathf.Max(0.1f, range.x);
        flightSpeedRange.y = Mathf.Max(flightSpeedRange.x, range.y);
    }

    private void Start() => SpawnOnce();

    public void Configure(BirdCreature prefab, int count, float radius, int randomSeed)
    {
        birdPrefab = prefab;
        birdCount = Mathf.Max(0, count);
        spawnRadius = Mathf.Max(0f, radius);
        seed = randomSeed;
    }

    public void SpawnOnce()
    {
        if (!Application.isPlaying || spawned) return;
        if (birdPrefab == null)
        {
            Debug.LogError("BirdSpawnArea needs a BirdCreature prefab.", this);
            return;
        }
        spawned = true;
        var random = new System.Random(seed);
        // Instantiate into this scene, even if another additive scene is active.
        GameObject staging = new GameObject("Bird Spawn Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
        try
        {
            for (int i = 0; i < birdCount; i++)
            {
                Vector3 direction = SampleDirection(random);
                float radius = spawnRadius * Mathf.Pow((float)random.NextDouble(), 1f / 3f);
                Vector3 position = transform.position + direction * radius;
                Vector3 heading = Quaternion.AngleAxis((float)random.NextDouble() * 45f - 22.5f, Vector3.up) * transform.forward;
                BirdCreature bird = Instantiate(birdPrefab, position, Quaternion.LookRotation(heading), staging.transform);
                bird.name = "Bird " + (i + 1);
                // Configure before Awake starts flight, so the very first velocity uses the area's limits.
                if (overrideFlightSpeed) bird.SetFlightSpeedRange(flightSpeedRange);
                // Detaching activates Awake/OnEnable, and birds remain independent scene roots.
                bird.transform.SetParent(null, true);
                bird.SetFlightSeed(random.Next());
            }
        }
        finally { Destroy(staging); }
    }

    private static Vector3 SampleDirection(System.Random random)
    {
        float y = (float)random.NextDouble() * 2f - 1f;
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.65f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        Gizmos.DrawRay(transform.position, transform.forward * Mathf.Max(2f, spawnRadius));
    }

    private void OnValidate() => SetFlightSpeedOverride(overrideFlightSpeed, flightSpeedRange);
}
