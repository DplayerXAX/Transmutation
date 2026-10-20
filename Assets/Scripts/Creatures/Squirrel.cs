using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creature behavior for territorial foraging; SquirrelMotor owns physics movement.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SquirrelMotor))]
public sealed class Squirrel : Creature
{
    public enum Activity { Searching, Collecting, Returning, Fighting, Retreating, PackingNest, Migrating }
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;
    [SerializeField, Min(0.01f)] private float reachDistance = 0.65f;
    [SerializeField, Min(0.1f)] private float searchRadius = 100f;
    [SerializeField, Min(0.1f)] private float searchInterval = 0.5f;
    [SerializeField, Min(1)] private int capacity = 3;
    [SerializeField] private Activity activity;
    [Header("Territory")]
    [SerializeField] private Vector3 territoryCenter;
    [SerializeField, Min(1f)] private float territoryRadius = 12f;
    [Header("Migration after food shortage")]
    [SerializeField, Min(1f)] private float foodShortageDelay = 30f;
    [SerializeField, Min(1f)] private float migrationSearchRadius = 45f;
    [SerializeField, Range(0.1f, 1f)] private float migrationSpeedScale = 0.7f;
    [SerializeField, Min(5f)] private float migrationTimeout = 90f;
    private float foodShortageTime;
    private float migrationTime;
    private Vector3 migrationGround;
    private Vector3 nestHalfExtents = Vector3.one * 0.5f;
    private SquirrelFood migrationFood;
    private readonly Collider[] nestPlacementHits = new Collider[32];
    private readonly List<SquirrelFood> carried = new List<SquirrelFood>();
    private FoodManager foodManager;
    private SquirrelNest nest;
    private SquirrelFood target;
    private float nextSearch;
    private float fightCooldownUntil;
    private float retreatUntil;
    private Vector3 retreatDirection;
    private Vector3 fightOrigin;
    private SquirrelMotor motor;
    public bool CanMoveAutonomously => isActiveAndEnabled && SimulationEnabled && !IsCarried &&
        !IsBubbleCaptured && !(TryGetComponent<WebCapture>(out var web) && web.IsCaptured);
    public int Capacity => capacity;
    public int CarriedCount => carried.Count;
    public Activity CurrentActivity => activity;
    public SquirrelNest Nest => nest;
    public Vector3 TerritoryCenter => territoryCenter;
    public float TerritoryRadius => territoryRadius;
    public bool IsMigrating => activity == Activity.Migrating;
    public bool AllowsMovementAt(Vector3 point) => IsMigrating || ContainsInTerritory(point);

    public bool ContainsInTerritory(Vector3 point)
    {
        Vector3 delta = point - territoryCenter;
        delta.y = 0f;
        return delta.sqrMagnitude <= territoryRadius * territoryRadius;
    }

    private Vector3 ClampToTerritory(Vector3 point)
    {
        Vector3 delta = point - territoryCenter;
        delta.y = 0f;
        Vector3 result = territoryCenter + Vector3.ClampMagnitude(delta, territoryRadius);
        result.y = point.y;
        return result;
    }
    public bool CanFight => CanMoveAutonomously && foodManager != null && nest != null &&
        Time.time >= fightCooldownUntil && activity != Activity.Fighting && activity != Activity.Retreating &&
        activity != Activity.PackingNest && !IsMigrating;

    public bool WantsToChallenge(Squirrel other) => other != null && other.CanBeAITargeted && ContainsInTerritory(other.transform.position) && carried.Count < capacity &&
        ((other.carried.Count > 0) || (target != null && (target == other.target || target.Owner == other)));

    public void BeginFight()
    {
        if (target != null) target.Release(this);
        target = null;
        fightOrigin = transform.position;
        motor.Stop();
        activity = Activity.Fighting;
    }

    public void PoseFight(Vector3 opponentOrigin, float elapsed)
    {
        if (!CanMoveAutonomously) return;
        Vector3 toward = opponentOrigin - fightOrigin;
        toward.y = 0f;
        if (toward.sqrMagnitude < 0.001f) toward = Vector3.right;
        toward.Normalize();
        float bump = Mathf.Sin(elapsed * 16f);
        motor.SetIntent(toward * (bump * 1.8f), bump > 0.6f ? 2.4f : 0f);
    }

    public void EndFight(Squirrel winner, Squirrel loser, float cooldown, bool resolved)
    {
        motor.Stop();
        fightCooldownUntil = Time.time + cooldown;
        if (resolved && this == loser)
        {
            if (carried.Count > 0)
            {
                var food = carried[carried.Count - 1];
                carried.RemoveAt(carried.Count - 1);
                if (food != null)
                {
                    food.Release(this);
                    if (winner != null && winner.carried.Count < winner.capacity && food.Reserve(winner))
                    {
                        food.HideInside(winner);
                        winner.carried.Add(food);
                    }
                }
            }
            retreatDirection = winner != null ? transform.position - winner.transform.position : -transform.forward;
            retreatDirection.y = 0f;
            if (retreatDirection.sqrMagnitude < 0.001f) retreatDirection = -transform.forward;
            retreatDirection.Normalize();
            retreatUntil = Time.time + 1.2f;
            activity = Activity.Retreating;
        }
        else activity = carried.Count >= capacity ? Activity.Returning : Activity.Searching;
        nextSearch = 0f;
    }

    public void Initialize(FoodManager foods, SquirrelNest home, float size, int baseCapacity, float homeRadius = 12f)
    {
        CancelMigration();
        ReleaseFood();
        foodManager = foods;
        nest = home;
        territoryCenter = home.transform.position;
        territoryRadius = Mathf.Max(1f, homeRadius);
        transform.localScale *= size;
        capacity = Mathf.Max(1, Mathf.RoundToInt(baseCapacity * size * size * size));
        activity = Activity.Searching;
        nextSearch = 0f;
        foodShortageTime = 0f;
        GetComponent<Rigidbody>().mass = Mathf.Max(0.1f, size * size * size);
    }

    protected override void InitializeCreature()
    {
        motor = GetComponent<SquirrelMotor>();
        SetCreatureName("Squirrel");
        SetFoodAndDiet(FoodTag.Meat, FoodTag.Plant);
        SetMovementPhysics(false, true);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (!CanMoveAutonomously) { motor.Stop(); return; }
        if (activity != Activity.Fighting) motor.Stop();
        if (foodManager == null || nest == null || !nest.isActiveAndEnabled) return;
        carried.RemoveAll(food => food == null);
        if (activity == Activity.Fighting) return;
        if (activity == Activity.PackingNest || IsMigrating)
        {
            TickMigration(deltaTime);
            return;
        }
        if (activity == Activity.Retreating)
        {
            if (Time.time < retreatUntil)
            {
                motor.SetIntent(retreatDirection * (moveSpeed * 1.6f));
                return;
            }
            activity = carried.Count > 0 ? Activity.Returning : Activity.Searching;
        }
        if (activity == Activity.Returning)
        {
            if (!MoveTo(nest.transform.position, nest.GetComponent<Collider>())) return;
            foreach (var food in carried) nest.Deposit(this, food);
            carried.Clear();
            activity = Activity.Searching;
            return;
        }
        if (target != null && (!target.isActiveAndEnabled || target.IsStored || !ContainsInTerritory(target.transform.position)))
        {
            target.Release(this);
            target = null;
        }
        if (target == null)
        {
            if (carried.Count == 0) foodShortageTime += deltaTime;
            if (Time.time < nextSearch) return;
            nextSearch = Time.time + searchInterval;
            target = foodManager.ReserveNearest(this, searchRadius);
            activity = target != null ? Activity.Collecting :
                carried.Count > 0 ? Activity.Returning : Activity.Searching;
            if (target == null)
            {
                if (carried.Count == 0 && foodShortageTime >= foodShortageDelay) TryPlanMigration();
                return;
            }
            foodShortageTime = 0f;
        }
        if (!MoveTo(target.transform.position, target.GetComponent<Collider>())) return;
        if (target.Owner != this && !target.Reserve(this)) return;
        target.HideInside(this);
        carried.Add(target);
        target = null;
        activity = carried.Count >= capacity ? Activity.Returning : Activity.Searching;
        nextSearch = 0f;
    }

    private bool MoveTo(Vector3 destination, Collider destinationCollider, bool migrating = false)
    {
        // AI supplies horizontal intent; the motor preserves gravity and collision response.
        if (!migrating) destination = ClampToTerritory(destination);
        destination.y = transform.position.y;
        Vector3 offset = destination - transform.position;
        // Interact with the object's surface, so a large food/nest collider cannot
        // physically block us outside a centre-based pickup radius.
        Vector3 interactionOffset = !migrating && destinationCollider != null
            ? destinationCollider.ClosestPoint(transform.position) - transform.position : offset;
        interactionOffset.y = 0f;
        float reach = reachDistance * Mathf.Max(0.1f, transform.lossyScale.x) + motor.BodyRadius;
        if (interactionOffset.sqrMagnitude <= reach * reach) return true;
        // The destination remains solid, but steering must not circle around the
        // very food or nest we are trying to approach.
        motor.SetIntent(offset.normalized * moveSpeed * (migrating ? migrationSpeedScale : 1f),
            destination: destinationCollider);
        return false;
    }

    private void TryPlanMigration()
    {
        migrationFood = foodManager.FindMigrationFood(this, migrationSearchRadius);
        if (migrationFood == null) return;
        Collider shape = nest.GetComponent<Collider>();
        nestHalfExtents = shape != null ? shape.bounds.extents : Vector3.one * 0.5f;
        nestHalfExtents = Vector3.Max(nestHalfExtents, Vector3.one * 0.25f);
        Vector3 foodPosition = migrationFood.transform.position;
        Vector3 inward = Vector3.ProjectOnPlane(territoryCenter - foodPosition, Vector3.up).normalized;
        for (int i = 0; i < 8; i++)
        {
            Vector3 candidate = foodPosition + Quaternion.Euler(0f, i * 45f, 0f) * inward *
                (Mathf.Max(nestHalfExtents.x, nestHalfExtents.z) + 2f);
            if (!TryNestGround(candidate, out migrationGround)) continue;
            migrationTime = 0f;
            activity = Activity.PackingNest;
            return;
        }
        migrationFood = null;
    }

    private bool TryNestGround(Vector3 point, out Vector3 ground)
    {
        ground = point;
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();
        if (!physics.Raycast(point + Vector3.up * 100f, Vector3.down, out RaycastHit hit,
                200f, 1 << 7, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) return false;
        ground = hit.point;
        Vector3 half = nestHalfExtents + new Vector3(0.15f, 0f, 0.15f);
        Vector3 center = ground + Vector3.up * (half.y + 0.08f);
        int count = physics.OverlapBox(center, half, nestPlacementHits, Quaternion.identity, ~0,
            QueryTriggerInteraction.Ignore);
        if (count == nestPlacementHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider other = nestPlacementHits[i];
            if (other == hit.collider || other.transform.IsChildOf(nest.transform) ||
                other.GetComponentInParent<Squirrel>() == this) continue;
            return false;
        }
        return true;
    }

    private void TickMigration(float deltaTime)
    {
        migrationTime += deltaTime;
        if (migrationTime >= migrationTimeout)
        {
            CancelMigration();
            return;
        }
        if (activity == Activity.PackingNest)
        {
            if (!MoveTo(nest.transform.position, nest.GetComponent<Collider>())) return;
            // Recheck before lifting: another squirrel may have occupied the destination.
            if (!TryNestGround(migrationGround, out migrationGround) || !nest.BeginMove(this))
            {
                CancelMigration();
                return;
            }
            activity = Activity.Migrating;
            motor.ResetRoute();
        }
        if (!MoveTo(migrationGround, migrationFood != null ? migrationFood.GetComponent<Collider>() : null, true)) return;
        if (!TryNestGround(migrationGround, out Vector3 ground))
        {
            CancelMigration();
            return;
        }
        nest.EndMove(ground);
        territoryCenter = nest.transform.position;
        FinishMigration();
    }

    private void CancelMigration()
    {
        if (nest != null && nest.IsMoving)
        {
            // Death, disable and timeouts detach the same nest, preserving owner and stores.
            Vector3 point = TryNestGround(transform.position, out var ground) ? ground : territoryCenter;
            if (gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * 100f, Vector3.down,
                    out var hit, 200f, 1 << 7, QueryTriggerInteraction.Ignore)) point = hit.point;
            nest.EndMove(point);
            territoryCenter = nest.transform.position;
        }
        if (activity == Activity.PackingNest || IsMigrating) FinishMigration();
    }

    private void FinishMigration()
    {
        activity = Activity.Searching;
        foodShortageTime = 0f;
        migrationFood = null;
        nextSearch = 0f;
        motor?.ResetRoute();
    }

    private void ReleaseFood()
    {
        if (target != null) target.Release(this);
        target = null;
        foreach (var food in carried) if (food != null) food.Release(this);
        carried.Clear();
    }

    public override void Die()
    {
        if (IsDead) return;
        CancelMigration();
        ReleaseFood();
        motor.Stop();
        base.Die();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        CancelMigration();
        ReleaseFood();
        if (motor != null) motor.Stop();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.8f);
        Vector3 center = Application.isPlaying ? territoryCenter : transform.position;
        Vector3 previous = center + Vector3.right * territoryRadius;
        for (int i = 1; i <= 64; i++)
        {
            float angle = i * Mathf.PI * 2f / 64;
            Vector3 next = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * territoryRadius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
