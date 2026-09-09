using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Seeks inanimate Fire Bubbles, flees the player only when aware and not busy eating, then creates a Fire Flower.</summary>
public sealed class FireEater : Creature
{
    private enum FireEaterState { Wandering, Chasing, Fleeing, Digesting }

    [Header("Movement")]
    [Min(0f)] [SerializeField] private float moveSpeed = 2f;
    [Min(0f)] [SerializeField] private float wanderRadius = 8f;

    [Tooltip("How quickly rotation lerps toward the desired facing. Higher is snappier.")]
    [Min(0.01f)]
    [SerializeField] private float rotationLerpSpeed = 8f;

    [Tooltip("If the eater moves less than this distance for Stuck Timeout, it picks a new path.")]
    [Min(0f)]
    [SerializeField] private float stuckDistanceThreshold = 0.35f;

    [Tooltip("Seconds of little/no progress before unstick logic runs.")]
    [Min(0.1f)]
    [SerializeField] private float stuckTimeout = 1.25f;

    [Tooltip("Forward probe used to slide along walls instead of pushing forever into them.")]
    [Min(0.05f)]
    [SerializeField] private float obstacleCheckDistance = 0.75f;

    [Header("Ground Alignment")]
    [Tooltip("How far downward the ground ray travels when aligning local Y to the surface normal.")]
    [Min(0.01f)]
    [SerializeField] private float groundRayDistance = 2f;

    [Tooltip("Ray origin offset along world up from this transform.")]
    [SerializeField] private float groundRayOriginHeight = 0.5f;

    [Tooltip("Surfaces steeper than this (degrees from world up) are ignored as ground.")]
    [Range(0f, 89f)]
    [SerializeField] private float maxGroundAngle = 55f;

    [Tooltip("How quickly the sampled ground normal is smoothed. Reduces sudden tilt pops.")]
    [Min(0.01f)]
    [SerializeField] private float groundNormalLerpSpeed = 10f;

    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Player Awareness")]
    [Tooltip("Vigilance radius. The eater can only sense Player-tagged colliders inside this range.")]
    [Min(0f)]
    [FormerlySerializedAs("fleeDetectionRadius")]
    [SerializeField] private float playerAwarenessRadius = 8f;

    [Tooltip("Move speed while fleeing the player. Fleeing is lower priority than chasing food.")]
    [Min(0f)]
    [SerializeField] private float fleeSpeed = 3.5f;

    [Header("Eating")]
    [Min(0f)] [SerializeField] private float detectionRadius = 6f;
    [Min(0f)] [SerializeField] private float digestionDuration = 5f;
    [SerializeField] private FireFlower fireFlowerPrefab;
    [Min(0f)] [SerializeField] private float flowerSpawnDistance = 1f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private FireEaterState currentState;
    [SerializeField] private FireBubble targetBubble;
    [SerializeField] private Transform fleeTarget;

    private Rigidbody eaterBody;
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private float digestionTimer;
    private float searchTimer;
    private Vector3 groundNormal = Vector3.up;
    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private readonly Collider[] overlapHits = new Collider[24];

    private Vector3 stuckSamplePosition;
    private float stuckTimer;
    private Vector3 preferredMoveDirection;
    private float fleeSideSign = 1f;

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
        // Orientation is driven by ground-normal alignment each tick.
        eaterBody.constraints |= RigidbodyConstraints.FreezeRotation;
        spawnPosition = transform.position;
        stuckSamplePosition = transform.position;
        ChooseWanderTarget();
        currentState = FireEaterState.Wandering;
    }

    protected override void TickCreature(float deltaTime)
    {
        searchTimer -= deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = 0.25f;
            FindNearestBubble();
            FindNearestPlayer();
        }

        UpdateStuckTracking(deltaTime);

        if (digestionTimer > 0f)
        {
            currentState = FireEaterState.Digesting;
            stuckTimer = 0f;
            StopMoving(deltaTime);
            digestionTimer -= deltaTime;

            if (digestionTimer <= 0f)
            {
                digestionTimer = 0f;
                CreateFlower();
                ChooseWanderTarget();
                currentState = FireEaterState.Wandering;
            }

            return;
        }

        // Food outranks fleeing: keep chasing even if the player is inside awareness range.
        if (targetBubble != null && targetBubble.IsInanimate)
        {
            currentState = FireEaterState.Chasing;
            MoveTowards(targetBubble.transform.position, moveSpeed, deltaTime);
            return;
        }

        targetBubble = null;

        if (fleeTarget != null)
        {
            currentState = FireEaterState.Fleeing;
            MoveAwayFrom(fleeTarget.position, fleeSpeed, deltaTime);
            return;
        }

        currentState = FireEaterState.Wandering;

        if (FlatDistance(transform.position, wanderTarget) < 0.5f)
        {
            ChooseWanderTarget();
        }

        MoveTowards(wanderTarget, moveSpeed, deltaTime);
    }

    private void UpdateStuckTracking(float deltaTime)
    {
        if (currentState == FireEaterState.Digesting)
        {
            stuckSamplePosition = transform.position;
            stuckTimer = 0f;
            return;
        }

        float moved = FlatDistance(transform.position, stuckSamplePosition);
        if (moved >= stuckDistanceThreshold)
        {
            stuckSamplePosition = transform.position;
            stuckTimer = 0f;
            return;
        }

        stuckTimer += deltaTime;
        if (stuckTimer < stuckTimeout)
        {
            return;
        }

        stuckTimer = 0f;
        stuckSamplePosition = transform.position;
        HandleStuck();
    }

    private void HandleStuck()
    {
        fleeSideSign = -fleeSideSign;

        switch (currentState)
        {
            case FireEaterState.Fleeing:
                // Nudge the flee heading with a side step next frame via preferredMoveDirection bias.
                preferredMoveDirection = Vector3.ProjectOnPlane(
                    preferredMoveDirection + transform.right * fleeSideSign,
                    groundNormal);
                if (preferredMoveDirection.sqrMagnitude > 0.0001f)
                {
                    preferredMoveDirection.Normalize();
                }

                break;

            case FireEaterState.Chasing:
                targetBubble = null;
                ChooseWanderTargetNearSelf();
                currentState = FireEaterState.Wandering;
                break;

            default:
                ChooseWanderTargetNearSelf();
                break;
        }
    }

    private void FindNearestPlayer()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            playerAwarenessRadius,
            overlapHits,
            ~0,
            QueryTriggerInteraction.Collide);

        Transform nearestPlayer = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider nearbyObject = overlapHits[i];
            if (nearbyObject == null || !nearbyObject.CompareTag("Player"))
            {
                continue;
            }

            float distance = (nearbyObject.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlayer = nearbyObject.transform;
            }
        }

        fleeTarget = nearestPlayer;
    }

    private void FindNearestBubble()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRadius,
            overlapHits,
            ~0,
            QueryTriggerInteraction.Collide);

        FireBubble nearestBubble = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider nearbyObject = overlapHits[i];
            if (nearbyObject == null)
            {
                continue;
            }

            FireBubble bubble = nearbyObject.GetComponentInParent<FireBubble>();
            if (bubble == null || !bubble.IsInanimate)
            {
                continue;
            }

            float distance = (bubble.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestBubble = bubble;
            }
        }

        targetBubble = nearestBubble;
    }

    private void MoveAwayFrom(Vector3 threatPosition, float speed, float deltaTime)
    {
        UpdateGroundNormal(deltaTime);

        Vector3 fleeDirection = Vector3.ProjectOnPlane(transform.position - threatPosition, groundNormal);
        if (fleeDirection.sqrMagnitude < 0.001f)
        {
            fleeDirection = Vector3.ProjectOnPlane(transform.right * fleeSideSign, groundNormal);
            if (fleeDirection.sqrMagnitude < 0.001f)
            {
                fleeDirection = Vector3.ProjectOnPlane(Vector3.right * fleeSideSign, groundNormal);
            }
        }

        if (fleeDirection.sqrMagnitude < 0.001f)
        {
            StopMoving(deltaTime);
            return;
        }

        fleeDirection.Normalize();
        // Blend in a side bias after becoming stuck so we do not keep ramming the same wall.
        if (preferredMoveDirection.sqrMagnitude > 0.001f)
        {
            fleeDirection = (fleeDirection + preferredMoveDirection * 0.85f).normalized;
            fleeDirection = Vector3.ProjectOnPlane(fleeDirection, groundNormal).normalized;
        }

        ApplyMovement(fleeDirection, speed, deltaTime);
    }

    private void MoveTowards(Vector3 targetPosition, float speed, float deltaTime)
    {
        UpdateGroundNormal(deltaTime);

        Vector3 moveDirection = Vector3.ProjectOnPlane(targetPosition - transform.position, groundNormal);
        if (moveDirection.sqrMagnitude < 0.001f)
        {
            if (currentState == FireEaterState.Chasing)
            {
                targetBubble = null;
            }

            ChooseWanderTargetNearSelf();
            StopMoving(deltaTime);
            return;
        }

        moveDirection.Normalize();
        ApplyMovement(moveDirection, speed, deltaTime);
    }

    private void ApplyMovement(Vector3 desiredDirection, float speed, float deltaTime)
    {
        Vector3 moveDirection = SteerAroundObstacles(desiredDirection);
        preferredMoveDirection = moveDirection;
        AlignLocalAxes(moveDirection, deltaTime);
        SetPlanarVelocity(moveDirection, speed);
    }

    /// <summary>If something solid is ahead, slide along it instead of pushing into the wall forever.</summary>
    private Vector3 SteerAroundObstacles(Vector3 desiredDirection)
    {
        Vector3 origin = transform.position + groundNormal * groundRayOriginHeight;
        if (!Physics.SphereCast(
                origin,
                0.25f,
                desiredDirection,
                out RaycastHit hit,
                obstacleCheckDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            return desiredDirection;
        }

        if (!IsValidObstacleHit(hit))
        {
            return desiredDirection;
        }

        Vector3 slide = Vector3.ProjectOnPlane(desiredDirection, hit.normal);
        slide = Vector3.ProjectOnPlane(slide, groundNormal);
        if (slide.sqrMagnitude < 0.001f)
        {
            slide = Vector3.ProjectOnPlane(Vector3.Cross(hit.normal, groundNormal) * fleeSideSign, groundNormal);
        }

        return slide.sqrMagnitude > 0.001f ? slide.normalized : desiredDirection;
    }

    private bool IsValidObstacleHit(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        Transform hitRoot = hit.collider.transform;
        if (hitRoot == transform || hitRoot.IsChildOf(transform) || transform.IsChildOf(hitRoot))
        {
            return false;
        }

        if (hit.collider.GetComponentInParent<FireBubble>() != null)
        {
            return false;
        }

        if (hit.collider.CompareTag("Player"))
        {
            return false;
        }

        return true;
    }

    private void StopMoving(float deltaTime)
    {
        UpdateGroundNormal(deltaTime);

        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, groundNormal);
        if (projectedForward.sqrMagnitude > 0.001f)
        {
            AlignLocalAxes(projectedForward.normalized, deltaTime);
        }
        else
        {
            AlignLocalAxes(Vector3.ProjectOnPlane(Vector3.forward, groundNormal).normalized, deltaTime);
        }

        SetPlanarVelocity(Vector3.zero, 0f);
    }

    private void UpdateGroundNormal(float deltaTime)
    {
        Vector3 origin = transform.position + Vector3.up * groundRayOriginHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHits,
            groundRayDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        Vector3 sampledNormal = Vector3.up;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            if (!IsValidGroundHit(hit))
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            sampledNormal = hit.normal.normalized;
        }

        float t = 1f - Mathf.Exp(-groundNormalLerpSpeed * deltaTime);
        groundNormal = Vector3.Slerp(groundNormal, sampledNormal, t).normalized;
        if (groundNormal.sqrMagnitude < 0.0001f)
        {
            groundNormal = Vector3.up;
        }
    }

    private bool IsValidGroundHit(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        Transform hitRoot = hit.collider.transform;
        if (hitRoot == transform || hitRoot.IsChildOf(transform) || transform.IsChildOf(hitRoot))
        {
            return false;
        }

        if (hit.collider.GetComponentInParent<FireBubble>() != null)
        {
            return false;
        }

        if (hit.normal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle;
    }

    private void AlignLocalAxes(Vector3 localForwardOnSurface, float deltaTime)
    {
        if (localForwardOnSurface.sqrMagnitude < 0.0001f)
        {
            localForwardOnSurface = Vector3.ProjectOnPlane(transform.forward, groundNormal);
            if (localForwardOnSurface.sqrMagnitude < 0.0001f)
            {
                localForwardOnSurface = Vector3.ProjectOnPlane(Vector3.forward, groundNormal);
            }
        }

        if (localForwardOnSurface.sqrMagnitude < 0.0001f || IsCarried)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(localForwardOnSurface.normalized, groundNormal);
        float t = 1f - Mathf.Exp(-rotationLerpSpeed * deltaTime);
        Quaternion currentRotation = eaterBody != null ? eaterBody.rotation : transform.rotation;
        Quaternion lerpedRotation = Quaternion.Slerp(currentRotation, targetRotation, t);

        if (eaterBody != null)
        {
            eaterBody.MoveRotation(lerpedRotation);
        }
        else
        {
            FaceCreature(lerpedRotation);
        }
    }

    /// <summary>Moves along the intended planar direction, independent of mid-lerp facing.</summary>
    private void SetPlanarVelocity(Vector3 planarDirection, float speed)
    {
        if (IsCarried || eaterBody == null || eaterBody.isKinematic)
        {
            return;
        }

        Vector3 planarVelocity = planarDirection.sqrMagnitude > 0.0001f && speed > 0f
            ? planarDirection.normalized * speed
            : Vector3.zero;

        float normalSpeed = Vector3.Dot(eaterBody.linearVelocity, groundNormal);
        if (normalSpeed > 0f)
        {
            normalSpeed = 0f;
        }

        eaterBody.linearVelocity = planarVelocity + groundNormal * normalSpeed;
    }

    private void OnCollisionEnter(Collision collision) => TryEat(collision.collider);
    private void OnCollisionStay(Collision collision) => TryEat(collision.collider);

    private void TryEat(Collider touchedObject)
    {
        if (digestionTimer > 0f)
        {
            return;
        }

        FireBubble bubble = touchedObject.GetComponentInParent<FireBubble>();
        if (bubble == null || !bubble.IsInanimate)
        {
            return;
        }

        Destroy(bubble.gameObject);
        targetBubble = null;
        digestionTimer = digestionDuration;
        currentState = FireEaterState.Digesting;
        StopMoving(Time.deltaTime);
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
        // Keep wander point near the current surface height so ProjectOnPlane does not collapse.
        wanderTarget.y = transform.position.y;
    }

    private void ChooseWanderTargetNearSelf()
    {
        Vector2 offset = Random.insideUnitCircle * Mathf.Max(2f, wanderRadius * 0.5f);
        wanderTarget = transform.position + new Vector3(offset.x, 0f, offset.y);
        preferredMoveDirection = Vector3.zero;
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

        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, playerAwarenessRadius);

        Vector3 origin = transform.position + Vector3.up * groundRayOriginHeight;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + Vector3.down * groundRayDistance);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(wanderTarget, 0.25f);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        wanderRadius = Mathf.Max(0f, wanderRadius);
        rotationLerpSpeed = Mathf.Max(0.01f, rotationLerpSpeed);
        stuckDistanceThreshold = Mathf.Max(0f, stuckDistanceThreshold);
        stuckTimeout = Mathf.Max(0.1f, stuckTimeout);
        obstacleCheckDistance = Mathf.Max(0.05f, obstacleCheckDistance);
        groundRayDistance = Mathf.Max(0.01f, groundRayDistance);
        maxGroundAngle = Mathf.Clamp(maxGroundAngle, 0f, 89f);
        groundNormalLerpSpeed = Mathf.Max(0.01f, groundNormalLerpSpeed);
        playerAwarenessRadius = Mathf.Max(0f, playerAwarenessRadius);
        fleeSpeed = Mathf.Max(0f, fleeSpeed);
        detectionRadius = Mathf.Max(0f, detectionRadius);
        digestionDuration = Mathf.Max(0f, digestionDuration);
        flowerSpawnDistance = Mathf.Max(0f, flowerSpawnDistance);
    }
}
