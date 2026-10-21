using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Tracks food and assigns exclusive reservations to foraging squirrels.</summary>
[DisallowMultipleComponent]
public sealed class FoodManager : MonoBehaviour
{
    [SerializeField] private GameObject foodPrefab;
    [SerializeField] private List<SquirrelFood> foods = new List<SquirrelFood>();
    [Tooltip("Initial food population spawned from foodPrefab.")]
    [SerializeField, Min(0)] private int initialFoodCount = 15;
    [SerializeField, Min(0.1f)] private float spawnRadius = 6f;
    [SerializeField, Min(0.1f)] private float minimumTreeDistance = 1.5f;
    [Header("Periodic food growth")]
    [SerializeField] private bool replenishFood = true;
    [SerializeField, Min(0.1f)] private float respawnInterval = 15f;
    [SerializeField, Min(1)] private int foodPerCycle = 3;
    [SerializeField, Min(1)] private int maxUnstoredFood = 24;
    private readonly List<SquirrelTree> spawnTrees = new List<SquirrelTree>();
    private readonly Collider[] spawnHits = new Collider[32];
    private float respawnTimer;
    private int nextTree;
    private int spawnSerial;
    public IReadOnlyList<SquirrelFood> Foods => foods;
    public IReadOnlyList<SquirrelTree> Trees => spawnTrees;
    public int UnstoredFoodCount
    {
        get
        {
            int count = 0;
            foreach (var food in foods) if (food != null && !food.IsStored) count++;
            return count;
        }
    }

    private void Update() => TickFoodGrowth(Time.deltaTime);

    // Scaled time: pausing does not accumulate a burst of deferred spawns.
    internal void TickFoodGrowth(float deltaTime)
    {
        if (!replenishFood || deltaTime <= 0f || spawnTrees.Count == 0) return;
        respawnTimer += deltaTime;
        if (respawnTimer < respawnInterval) return;
        respawnTimer = 0f;
        int count = Mathf.Min(foodPerCycle, Mathf.Max(0, maxUnstoredFood - UnstoredFoodCount));
        for (int i = 0; i < count; i++) if (!SpawnFood()) break;
    }

    public void GenerateAroundTrees(IReadOnlyList<SquirrelTree> trees)
    {
        foods.RemoveAll(food => food == null);
        spawnTrees.Clear();
        if (trees != null)
            foreach (var tree in trees) if (tree != null) spawnTrees.Add(tree);
        respawnTimer = 0f;
        int count = Mathf.Max(0, Mathf.Min(initialFoodCount, maxUnstoredFood) - UnstoredFoodCount);
        for (int i = 0; i < count; i++) if (!SpawnFood()) break;
    }

    private bool SpawnFood()
    {
        if (foodPrefab == null || spawnTrees.Count == 0) return false;
        // Rotate through trees so a single clearing cannot monopolize every refresh.
        for (int attempt = 0; attempt < spawnTrees.Count * 3; attempt++)
        {
            var tree = spawnTrees[nextTree++ % spawnTrees.Count];
            if (tree == null || !tree.TrySampleGround(minimumTreeDistance,
                    Mathf.Max(minimumTreeDistance, spawnRadius), out Vector3 point)) continue;
            int hits = gameObject.scene.GetPhysicsScene().OverlapSphere(point + Vector3.up * 0.75f,
                0.6f, spawnHits, ~0, QueryTriggerInteraction.Ignore);
            if (hits > 0) continue;
            // Stored food remains owned by the nest. Refresh only creates new world food.
            SquirrelFood food;
            {
                var instance = Instantiate(foodPrefab, transform);
                food = instance.GetComponent<SquirrelFood>() ?? instance.AddComponent<SquirrelFood>();
                Register(food);
            }
            food.Respawn(transform);
            food.transform.localScale = foodPrefab.transform.localScale;
            food.transform.localRotation = foodPrefab.transform.localRotation;
            food.name = foodPrefab.name + " " + (++spawnSerial);
            SquirrelTree.PlaceOnGround(food.transform, point);
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    public SquirrelFood ReserveBuildingFood(Squirrel squirrel, float radius)
    {
        SquirrelFood best = null;
        float distance = radius * radius;
        foreach (var food in foods)
        {
            if (food == null || !food.IsAvailable || food.gameObject.scene != squirrel.gameObject.scene) continue;
            float d = (food.transform.position - squirrel.transform.position).sqrMagnitude;
            if (d >= distance) continue;
            distance = d;
            best = food;
        }
        return best != null && best.Reserve(squirrel) ? best : null;
    }

    public SquirrelFood FindMigrationFood(Squirrel squirrel, float radius)
    {
        SquirrelFood nearest = null;
        float best = radius * radius;
        foreach (var food in foods)
        {
            if (food == null || !food.IsAvailable || food.gameObject.scene != squirrel.gameObject.scene ||
                squirrel.ContainsInTerritory(food.transform.position)) continue;
            float distance = Vector3.ProjectOnPlane(food.transform.position - squirrel.TerritoryCenter, Vector3.up).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            nearest = food;
        }
        return nearest;
    }

    public void Register(SquirrelFood food)
    {
        if (food != null && !foods.Contains(food)) foods.Add(food);
    }

    public SquirrelFood ReserveNearest(Squirrel squirrel, float radius)
    {
        SquirrelFood nearest = null;
        float best = radius * radius;
        for (int i = foods.Count - 1; i >= 0; i--)
        {
            var food = foods[i];
            if (food == null) { foods.RemoveAt(i); continue; }
            // Reserved ground food remains a valid goal: rivals can contest it on arrival.
            if (food.IsStored || !food.isActiveAndEnabled || !squirrel.ContainsInTerritory(food.transform.position)) continue;
            float distance = (food.transform.position - squirrel.transform.position).sqrMagnitude;
            if (distance > best) continue;
            best = distance;
            nearest = food;
        }
        if (nearest != null && nearest.IsAvailable) nearest.Reserve(squirrel);
        return nearest;
    }
}
