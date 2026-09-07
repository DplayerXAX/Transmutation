using UnityEngine;

/// <summary>Wanders, eats inanimate Fire Bubbles, then creates a Fire Flower.</summary>
public sealed class FireEater : Creature
{
    private enum FireEaterState { Wandering, Chasing, Digesting }

    [Header("Movement")]
    [Min(0f)] [SerializeField] private float moveSpeed = 2f;
    [Min(0f)] [SerializeField] private float wanderRadius = 8f;

    [Header("Eating")]
    [Min(0f)] [SerializeField] private float detectionRadius = 6f;
    [Min(0f)] [SerializeField] private float digestionDuration = 5f;
    [SerializeField] private FireFlower fireFlowerPrefab;
    [Min(0f)] [SerializeField] private float flowerSpawnDistance = 1f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private FireEaterState currentState;
    [SerializeField] private FireBubble targetBubble;

    private Rigidbody eaterBody;
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private float digestionTimer;
    private float searchTimer;

    protected override void InitializeCreature()
    {
        eaterBody = GetComponent<Rigidbody>();

        if (eaterBody == null)
        {
            Debug.LogError($"{name} needs a Rigidbody for FireEater movement.", this);
            SetSimulationEnabled(false);
            return;
        }

        if (GetComponent<Collider>() == null)
        {
            Debug.LogWarning($"{name} needs a Collider to touch and eat Fire Bubbles.", this);
        }

        eaterBody.useGravity = true;
        eaterBody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        spawnPosition = transform.position;
        ChooseWanderTarget();
        currentState = FireEaterState.Wandering;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (currentState == FireEaterState.Digesting)
        {
            StopMoving();
            digestionTimer -= deltaTime;

            if (digestionTimer <= 0f)
            {
                CreateFlower();
                ChooseWanderTarget();
                currentState = FireEaterState.Wandering;
            }

            return;
        }

        searchTimer -= deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = 0.25f;
            FindNearestBubble();
        }

        if (targetBubble != null && targetBubble.IsInanimate)
        {
            currentState = FireEaterState.Chasing;
            MoveTowards(targetBubble.transform.position);
            return;
        }

        targetBubble = null;
        currentState = FireEaterState.Wandering;

        if (FlatDistance(transform.position, wanderTarget) < 0.5f)
        {
            ChooseWanderTarget();
        }

        MoveTowards(wanderTarget);
    }

    private void FindNearestBubble()
    {
        Collider[] nearbyObjects = Physics.OverlapSphere(transform.position, detectionRadius);
        FireBubble nearestBubble = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (Collider nearbyObject in nearbyObjects)
        {
            FireBubble bubble = nearbyObject.GetComponentInParent<FireBubble>();
            if (bubble == null || !bubble.IsInanimate) continue;

            float distance = (bubble.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestBubble = bubble;
            }
        }

        targetBubble = nearestBubble;
    }

    private void MoveTowards(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            StopMoving();
            return;
        }

        direction.Normalize();
        SetCreatureHorizontalVelocity(direction * moveSpeed);
        FaceCreature(Quaternion.LookRotation(direction, Vector3.up));
    }

    private void StopMoving()
    {
        SetCreatureHorizontalVelocity(Vector3.zero);
    }

    private void OnCollisionEnter(Collision collision) => TryEat(collision.collider);
    private void OnCollisionStay(Collision collision) => TryEat(collision.collider);

    private void TryEat(Collider touchedObject)
    {
        if (currentState == FireEaterState.Digesting) return;

        FireBubble bubble = touchedObject.GetComponentInParent<FireBubble>();
        if (bubble == null || !bubble.IsInanimate) return;

        Destroy(bubble.gameObject);
        targetBubble = null;
        digestionTimer = digestionDuration;
        currentState = FireEaterState.Digesting;
        StopMoving();
    }

    private void CreateFlower()
    {
        if (fireFlowerPrefab == null)
        {
            Debug.LogWarning($"{name} needs a Fire Flower prefab assigned.", this);
            return;
        }

        Vector3 flowerPosition = transform.position - transform.forward * flowerSpawnDistance;
        Instantiate(fireFlowerPrefab, flowerPosition, Quaternion.identity);
    }

    private void ChooseWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = spawnPosition + new Vector3(offset.x, 0f, offset.y);
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        wanderRadius = Mathf.Max(0f, wanderRadius);
        detectionRadius = Mathf.Max(0f, detectionRadius);
        digestionDuration = Mathf.Max(0f, digestionDuration);
        flowerSpawnDistance = Mathf.Max(0f, flowerSpawnDistance);
    }
}
