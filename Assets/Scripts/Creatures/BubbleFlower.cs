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
    [Min(0.1f)] [SerializeField] private float aimDistance = 30f;
    private float groundTimer, nextManualShot;
    private bool wasCarried;

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
        if (IsCarried) { wasCarried = true; return; }
        if (wasCarried) { ResetGroundTimer(); wasCarried = false; }
        groundTimer -= deltaTime;
        if (groundTimer > 0f) return;
        Fire(Vector3.up, null, 0f);
        ResetGroundTimer();
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || !IsCarried || Time.time < nextManualShot) return;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position + transform.up;
        Camera camera = Carrier.GetComponentInChildren<Camera>();
        if (camera == null && Carrier.PlayerController != null) camera = Carrier.PlayerController.PlayerCamera;
        if (camera == null) camera = Camera.main;
        Ray ray = camera != null ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : new Ray(origin, interaction.Direction.normalized);
        Vector3 aimPoint = ray.GetPoint(aimDistance);
        RaycastHit[] hits = new RaycastHit[32];
        int count;
        var physics = gameObject.scene.GetPhysicsScene();
        while ((count = physics.Raycast(ray.origin, ray.direction, hits, aimDistance, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
            hits = new RaycastHit[hits.Length * 2];
        System.Array.Sort(hits, 0, count, System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.GetComponentInParent<Creature>() == this ||
                (Carrier.PlayerController != null && hit.collider.GetComponentInParent<SmoothFirstPersonController>() == Carrier.PlayerController) ||
                (Carrier.PlayerController != null && Carrier.PlayerController.IsContainingBubble(hit.collider))) continue;
            aimPoint = hit.point;
            break;
        }
        Vector3 direction = (aimPoint - origin).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = ray.direction;
        if (Fire(direction, Carrier.PlayerController != null ? Carrier.PlayerController.gameObject : Carrier.gameObject, launchSpeed))
            nextManualShot = Time.time + manualCooldown;
    }

    private bool Fire(Vector3 direction, GameObject shooter, float speed)
    {
        if (bubblePrefab == null) return false;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up;
        float radius = bubblePrefab.GetComponent<SphereCollider>().radius * bubblePrefab.transform.localScale.x;
        origin += direction * (radius + 0.1f);
        LastShot = Instantiate(bubblePrefab, origin, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(LastShot.gameObject, gameObject.scene);
        LastShot.Launch(this, shooter, direction * speed);
        return true;
    }

    private void OnValidate()
    {
        groundFiringInterval.x = Mathf.Max(0.1f, groundFiringInterval.x);
        groundFiringInterval.y = Mathf.Max(groundFiringInterval.x, groundFiringInterval.y);
    }
}
