using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A stationary bubble emitter, or a manually aimed emitter while carried.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class BubbleFlower : Creature
{
    [SerializeField] private TrappingBubble bubblePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Vector2 groundFiringInterval = new Vector2(3f, 6f);
    [Min(0f)] [SerializeField] private float manualCooldown = 0.5f;
    [Min(0f)] [SerializeField] private float launchSpeed = 8f;
    [Tooltip("Shots fired while carried: launch speed and how long they keep it, so they fly straight out ahead " +
             "(about speed x time metres) before slowing to a float.")]
    [Min(0f)] [SerializeField] private float carriedLaunchSpeed = 13f;
    [Min(0.05f)] [SerializeField] private float carriedLaunchHold = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float verticalLaunchMultiplier = 0.5f;
    [Min(0.1f)] [SerializeField] private float aimDistance = 30f;
    private float groundTimer, nextManualShot;
    private bool wasCarried;
    private PlayerCreatureCarrier queuedCarrier;
    private Vector3 queuedDirection;
    private float queuedUntil;
    private RaycastHit[] shotHits = new RaycastHit[32];
    private Collider[] shotOverlaps = new Collider[32];
    private const float InputBufferSeconds = 0.18f;

    public TrappingBubble LastShot { get; private set; }
    public override bool CanUseWhileCarried => !IsDead;
    public override string CarriedPrompt => IsDead ? null : "[LMB] Shoot bubble";
    public override string InteractionPrompt => !IsDead && IsCarried ? "Shoot bubble" : null;

    protected override void InitializeCreature()
    {
        SetCreatureName("Bubble Flower");
        if (muzzle == null) muzzle = transform.Find("Muzzle");
        SetMovementPhysics(false, true); // Settle on the ground again after being dropped.
        ResetGroundTimer();
    }

    private void ResetGroundTimer() => groundTimer = Random.Range(groundFiringInterval.x, groundFiringInterval.y);

    protected override void TickCreature(float deltaTime)
    {
        if (IsCarried)
        {
            wasCarried = true;
            if (queuedCarrier != null && (queuedCarrier != Carrier || Time.time > queuedUntil || BoneAwakeningCutscene.Playing)) queuedCarrier = null;
            if (queuedCarrier != null && Time.time >= nextManualShot) TryManualShot(queuedDirection);
            return;
        }
        queuedCarrier = null;
        if (wasCarried) { ResetGroundTimer(); wasCarried = false; }
        groundTimer -= deltaTime;
        if (groundTimer > 0f) return;
        Fire(Vector3.up, null, 0f);
        ResetGroundTimer();
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || !IsCarried || BoneAwakeningCutscene.Playing) return;
        // A press just before cooldown ends survives until the shot is ready; one press queues at most one shot.
        queuedCarrier = Carrier;
        queuedDirection = interaction.Direction;
        queuedUntil = Time.time + InputBufferSeconds;
        if (Time.time >= nextManualShot) TryManualShot(interaction.Direction);
    }

    private void TryManualShot(Vector3 fallbackDirection)
    {
        Vector3 origin = muzzle != null ? muzzle.position : transform.position + transform.up;
        Camera camera = Carrier.GetComponentInChildren<Camera>();
        if (camera == null && Carrier.PlayerController != null) camera = Carrier.PlayerController.PlayerCamera;
        if (camera == null) camera = Camera.main;
        Ray ray = camera != null ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : new Ray(origin, fallbackDirection.sqrMagnitude > 0.001f ? fallbackDirection.normalized : transform.forward);
        Vector3 aimPoint = ray.GetPoint(aimDistance);
        int count;
        var physics = gameObject.scene.GetPhysicsScene();
        while ((count = physics.Raycast(ray.origin, ray.direction, shotHits, aimDistance, ~0, QueryTriggerInteraction.Ignore)) == shotHits.Length)
            shotHits = new RaycastHit[shotHits.Length * 2];
        float closest = aimDistance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = shotHits[i];
            if (IgnoreLaunchCollider(hit.collider) || hit.distance >= closest) continue;
            closest = hit.distance;
            aimPoint = hit.point;
        }
        // Sweep the full bubble from the eyes, rather than spawning a fixed distance inside/beyond a nearby wall.
        if (!TryLaunchOrigin(ray, bubblePrefab != null ? bubblePrefab.InitialRadius : 0.4f, out origin)) return;
        Vector3 direction = (aimPoint - origin).normalized;
        // Something right in front of the eyes (nearer than where the bubble starts): just shoot straight ahead.
        if (direction.sqrMagnitude < 0.001f || Vector3.Dot(direction, ray.direction) < 0.5f) direction = ray.direction;
        if (Fire(direction, Carrier.PlayerController != null ? Carrier.PlayerController.gameObject : Carrier.gameObject, carriedLaunchSpeed, origin, carriedLaunchHold))
        {
            nextManualShot = Time.time + manualCooldown;
            queuedCarrier = null;
        }
    }

    private bool Fire(Vector3 direction, GameObject shooter, float speed) =>
        Fire(direction, shooter, speed, (muzzle != null ? muzzle.position : transform.position + Vector3.up)
            + direction * ((bubblePrefab != null ? bubblePrefab.InitialRadius : 0.4f) + 0.1f), -1f);

    private bool Fire(Vector3 direction, GameObject shooter, float speed, Vector3 origin, float hold)
    {
        if (bubblePrefab == null) return false;
        LastShot = Instantiate(bubblePrefab, origin, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(LastShot.gameObject, gameObject.scene);
        Vector3 velocity = direction * speed;
        velocity.y *= verticalLaunchMultiplier;
        LastShot.Launch(this, shooter, velocity, hold);
        return true;
    }

    private bool IgnoreLaunchCollider(Collider collider) => collider.GetComponentInParent<Creature>() == this ||
        (Carrier != null && (collider.transform.IsChildOf(Carrier.transform) ||
            (Carrier.PlayerBody != null && collider.attachedRigidbody == Carrier.PlayerBody) ||
            (Carrier.PlayerController != null && (collider.GetComponentInParent<SmoothFirstPersonController>() == Carrier.PlayerController ||
                Carrier.PlayerController.IsContainingBubble(collider)))));

    private bool TryLaunchOrigin(Ray ray, float radius, out Vector3 origin)
    {
        var physics = gameObject.scene.GetPhysicsScene();
        float distance = 0.9f + radius + 0.1f;
        int count;
        while ((count = physics.SphereCast(ray.origin, radius + 0.025f, ray.direction, shotHits, distance, ~0,
            QueryTriggerInteraction.Ignore)) == shotHits.Length) shotHits = new RaycastHit[shotHits.Length * 2];
        for (int i = 0; i < count; i++)
            if (!IgnoreLaunchCollider(shotHits[i].collider)) distance = Mathf.Min(distance, Mathf.Max(0f, shotHits[i].distance - 0.04f));
        // Also catch an initial overlap, which a sphere sweep cannot report reliably.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            origin = ray.GetPoint(distance);
            while ((count = physics.OverlapSphere(origin, radius + 0.015f, shotOverlaps, ~0,
                QueryTriggerInteraction.Ignore)) == shotOverlaps.Length) shotOverlaps = new Collider[shotOverlaps.Length * 2];
            bool blocked = false;
            for (int i = 0; i < count; i++) blocked |= !IgnoreLaunchCollider(shotOverlaps[i]);
            if (!blocked) return true;
            if (distance <= 0f) break;
            distance = Mathf.Max(0f, distance - 0.2f);
        }
        origin = ray.origin;
        return false;
    }

    private void OnValidate()
    {
        groundFiringInterval.x = Mathf.Max(0.1f, groundFiringInterval.x);
        groundFiringInterval.y = Mathf.Max(groundFiringInterval.x, groundFiringInterval.y);
    }
}
