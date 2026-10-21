using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class Squirrel
{
    [Header("Nest construction")]
    [SerializeField, Min(1)] private int nestBuildFoodCost = 3;
    [SerializeField, Min(0.1f)] private float nestBuildSeconds = 2f;
    [SerializeField, Min(4f)] private float additionalNestMaxDistance = 10f;
    [SerializeField, Min(1f)] private float otherOwnerNestClearance = 6f;
    [SerializeField, Min(1f)] private float nestDiscoveryRadius = 6f;
    private sealed class NestMemory
    {
        public SquirrelNest Nest;
        public Vector3 Position;
        public float Radius;
        public bool Missing;
    }
    private readonly List<NestMemory> homes = new List<NestMemory>();
    private GameObject nestPrefab;
    private SquirrelSystemManager nestManager;
    private bool rebuildingMissingNest;
    private Vector3 buildAnchor, buildGround;
    private float buildProgress, discoveryTimer;
    public bool IsNestWork => activity == Activity.GatheringNestMaterials || activity == Activity.BuildingNest;
    public int KnownNestCount => homes.Count;
    public bool ActiveNestKnownMissing => MemoryFor(nest)?.Missing ?? false;

    public void ConfigureNestBuilding(GameObject prefab, SquirrelSystemManager manager = null)
    { nestPrefab = prefab; nestManager = manager; }

    private NestMemory MemoryFor(SquirrelNest home) => homes.Find(memory => memory.Nest == home);
    private void RememberNest(SquirrelNest home)
    {
        if (home == null) return;
        var memory = MemoryFor(home);
        if (memory == null) { memory = new NestMemory { Nest = home }; homes.Add(memory); }
        memory.Position = home.transform.position;
        var shape = home.GetComponent<Collider>();
        memory.Radius = shape != null ? Mathf.Max(shape.bounds.extents.x, shape.bounds.extents.z) : 0.5f;
        memory.Missing = false;
    }
    public void ForgetFood(SquirrelFood food)
    {
        carried.Remove(food);
        if (target == food) target = null;
    }

    // Movement follows remembered coordinates. Live nest position is checked only on arrival.
    private bool ApproachRememberedNest()
    {
        var memory = MemoryFor(nest);
        if (memory == null) return true;
        Vector3 offset = Vector3.ProjectOnPlane(memory.Position - transform.position, Vector3.up);
        float reach = reachDistance * Mathf.Max(0.1f, transform.lossyScale.x) + motor.BodyRadius + memory.Radius;
        if (offset.sqrMagnitude <= reach * reach) return true;
        motor.SetIntent(offset.normalized * moveSpeed, destination: nest != null ? nest.GetComponent<Collider>() : null,
            goal: memory.Position);
        return false;
    }
    private bool NestStillAtRememberedPlace()
    {
        var memory = MemoryFor(nest);
        return memory != null && nest != null && nest.isActiveAndEnabled && !nest.IsDead && !nest.IsCarried &&
            Vector3.ProjectOnPlane(nest.transform.position - memory.Position, Vector3.up).sqrMagnitude <= 4f;
    }
    private void ReturnToRememberedNest()
    {
        if (!ApproachRememberedNest()) return;
        if (!NestStillAtRememberedPlace()) { DiscoverMissingNest(); return; }
        for (int i = carried.Count - 1; i >= 0; i--)
            if (nest.Deposit(this, carried[i])) carried.RemoveAt(i);
        activity = Activity.Searching;
        if (nest.IsFull) SelectStorageOrBuild();
    }
    private void DiscoverMissingNest()
    {
        var memory = MemoryFor(nest);
        if (memory != null) memory.Missing = true;
        BeginNestConstruction(true);
    }
    private void BeginNestConstruction(bool missing)
    {
        if (target != null) target.Release(this);
        target = null;
        buildAnchor = MemoryFor(nest)?.Position ?? territoryCenter;
        rebuildingMissingNest = missing;
        buildProgress = 0f;
        activity = Activity.GatheringNestMaterials;
        nextSearch = 0f;
        motor.ResetRoute();
    }
    private void SelectStorageOrBuild()
    {
        foreach (var memory in homes)
        {
            if (memory.Missing || memory.Nest == null || memory.Nest == nest || memory.Nest.IsFull) continue;
            nest = memory.Nest;
            territoryCenter = memory.Position;
            activity = carried.Count > 0 ? Activity.Returning : Activity.Searching;
            motor.ResetRoute();
            return;
        }
        BeginNestConstruction(false);
    }
    private bool TickNestTasks(float deltaTime)
    {
        discoveryTimer -= deltaTime;
        if (discoveryTimer <= 0f)
        {
            discoveryTimer = 0.5f;
            DiscoverOldNests();
        }
        if (!IsNestWork)
        {
            if (nest == null) { BeginNestConstruction(true); return true; }
            if (activity == Activity.Searching && nest.IsFull) { SelectStorageOrBuild(); return true; }
            return false;
        }
        if (activity == Activity.BuildingNest)
        {
            if (!MoveTo(buildGround, null, true)) return true;
            if (!SuitableBuildingSite(buildGround, out Vector3 ground))
            { activity = Activity.GatheringNestMaterials; buildProgress = 0f; return true; }
            buildProgress += deltaTime;
            if (buildProgress < nestBuildSeconds || carried.Count < nestBuildFoodCost || nestPrefab == null) return true;
            var instance = Instantiate(nestPrefab, transform.position, Quaternion.identity, nestManager != null ? nestManager.transform : null);
            if (instance.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
            var home = instance.GetComponent<SquirrelNest>() ?? instance.AddComponent<SquirrelNest>();
            home.AssignOwner(this);
            SquirrelTree.PlaceOnGround(home.transform, ground);
            // Spend exactly the collected instances only after a valid nest exists.
            for (int i = 0; i < nestBuildFoodCost; i++)
            {
                var material = carried[carried.Count - 1];
                carried.RemoveAt(carried.Count - 1);
                material.Release(this);
                material.Die();
                // Spent materials leave physics immediately; Destroy is deferred until frame end.
                material.gameObject.SetActive(false);
                Destroy(material.gameObject);
            }
            nest = home;
            nestManager?.Register(home);
            territoryCenter = home.transform.position;
            RememberNest(home);
            foodShortageTime = 0f;
            activity = carried.Count > 0 ? Activity.Returning : Activity.Searching;
            motor.ResetRoute();
            Physics.SyncTransforms();
            return true;
        }
        if (carried.Count >= nestBuildFoodCost)
        {
            if (TryChooseBuildingSite()) { activity = Activity.BuildingNest; buildProgress = 0f; }
            return true;
        }
        if (target != null && (!target.isActiveAndEnabled || target.IsStored || target.Owner != this || target.IsCarried))
        { target.Release(this); target = null; }
        if (target == null)
        {
            if (Time.time < nextSearch) return true;
            nextSearch = Time.time + searchInterval;
            target = foodManager.ReserveBuildingFood(this, migrationSearchRadius);
            if (target == null) return true;
        }
        if (!MoveTo(target.transform.position, target.GetComponent<Collider>(), true)) return true;
        target.HideInside(this);
        carried.Add(target);
        target = null;
        nextSearch = 0f;
        return true;
    }

    private bool SuitableBuildingSite(Vector3 point, out Vector3 ground)
    {
        ground = point;
        bool nearTree = false;
        foreach (var tree in foodManager.Trees)
            if (tree != null && Vector3.ProjectOnPlane(tree.GroundPosition - point, Vector3.up).sqrMagnitude <= 36f)
            { nearTree = true; break; }
        if (!nearTree) return false;
        foreach (var home in SquirrelNest.All)
        {
            if (home == null || home.gameObject.scene != gameObject.scene || home.IsDead) continue;
            float separation = home.Owner == this ? 3.5f : otherOwnerNestClearance;
            if (Vector3.ProjectOnPlane(home.transform.position - point, Vector3.up).sqrMagnitude < separation * separation)
                return false;
        }
        if (!rebuildingMissingNest && Vector3.ProjectOnPlane(point - buildAnchor, Vector3.up).sqrMagnitude >
            additionalNestMaxDistance * additionalNestMaxDistance) return false;
        return TryNestGround(point, out ground);
    }
    private bool TryChooseBuildingSite()
    {
        if (nestPrefab == null) return false;
        float best = float.PositiveInfinity;
        bool found = false;
        float range = rebuildingMissingNest ? migrationSearchRadius : additionalNestMaxDistance + 6f;
        foreach (var tree in foodManager.Trees)
        {
            if (tree == null || Vector3.ProjectOnPlane(tree.GroundPosition - buildAnchor, Vector3.up).sqrMagnitude > range * range) continue;
            for (int i = 0; i < 12; i++)
            {
                Vector3 point = tree.GroundPosition + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * 4f;
                if (!SuitableBuildingSite(point, out var ground)) continue;
                float score = (ground - transform.position).sqrMagnitude;
                if (score >= best) continue;
                best = score; buildGround = ground; found = true;
            }
        }
        return found;
    }
    private void DiscoverOldNests()
    {
        // Discovery is local and occluded; moving an unseen nest never updates memory.
        foreach (var memory in homes)
        {
            var home = memory.Nest;
            if (!memory.Missing || home == null || home.IsCarried || home.IsMoving || home.IsDead ||
                home.gameObject.scene != gameObject.scene) continue;
            Vector3 offset = home.transform.position - transform.position;
            if (offset.sqrMagnitude > nestDiscoveryRadius * nestDiscoveryRadius) continue;
            var physics = gameObject.scene.GetPhysicsScene();
            if (physics.Raycast(AimPosition, offset.normalized, out var hit, offset.magnitude + 1f, ~0,
                    QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(home.transform)) continue;
            memory.Position = home.transform.position;
            memory.Missing = false;
            var current = MemoryFor(nest);
            if (!IsNestWork && current != null && !current.Missing && nest != home && home.StoredFoodCount > 0)
            {
                // A recovered stocked nest is worth carrying back beside the present home.
                buildAnchor = current.Position;
                rebuildingMissingNest = false;
                if (TryChooseBuildingSite())
                {
                    nest = home;
                    territoryCenter = memory.Position;
                    nestHalfExtents = home.GetComponent<Collider>()?.bounds.extents ?? Vector3.one * 0.5f;
                    migrationGround = buildGround;
                    migrationFood = null;
                    migrationTime = 0f;
                    if (target != null) target.Release(this);
                    target = null;
                    activity = Activity.PackingNest;
                    motor.ResetRoute();
                    return;
                }
            }
            // Recover a usable old home when homeless; otherwise retain it as known spare storage.
            if (IsNestWork && !home.IsFull)
            {
                nest = home; territoryCenter = memory.Position;
                if (target != null) target.Release(this);
                target = null;
                activity = carried.Count > 0 ? Activity.Returning : Activity.Searching;
                motor.ResetRoute();
            }
            return;
        }
    }
}
