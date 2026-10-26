using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A food object is either available, reserved, carried, or stored in a nest.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SquirrelFood : Creature
{
    public Squirrel Owner { get; private set; }
    public PlayerCreatureCarrier LastPlayer { get; private set; }
    private float lastHeldAt = float.NegativeInfinity;
    public bool WasRecentlyReleasedByPlayer => !IsCarried && Time.time - lastHeldAt <= 5f;
    public SquirrelNest StorageNest { get; private set; }
    public bool IsStored => StorageNest != null;
    public bool IsAvailable => !IsStored && Owner == null && !IsCarried && !IsDead && !IsConsumptionPending && isActiveAndEnabled;
    public override bool CanBeCarried => !IsStored && !IsDead;
    public override bool CanUseWhileCarried => !IsDead;
    public override string CarriedPrompt => "[LMB at nest] Store food | [RMB] Drop";

    protected override void InitializeCreature()
    {
        SetCreatureName("Squirrel Food");
        SetResolvedPolarity(CreaturePolarity.None);
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        StopFoodPhysics();
    }
    protected override void TickCreature(float deltaTime) { if (IsCarried) lastHeldAt = Time.time; }
    protected override void OnCarryStarted()
    {
        LastPlayer = Carrier;
        lastHeldAt = Time.time;
        var squirrel = Owner;
        Owner = null;
        squirrel?.ForgetFood(this);
        // Release into ordinary gravity when the player lets go.
        SetMovementPhysics(false, true);
    }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || Carrier == null || IsDead) return;
        Camera camera = Carrier.CarryCamera;
        if (camera == null) return;
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Creature found = CreatureAim.Find(gameObject.scene.GetPhysicsScene(), ray, 5f, ~0, Carrier, out _);
        if (found is SquirrelNest nest) nest.TryPlayerDeposit(Carrier, this);
    }

    // Only new world food is initialized here; stored inventory must never be recycled.
    internal void Respawn(Transform parent)
    {
        StopFoodPhysics();
        Owner = null;
        StorageNest = null;
        transform.SetParent(parent, true);
        gameObject.SetActive(true);
    }

    internal void LeaveStorage(Vector3 position)
    {
        StorageNest = null;
        Owner = null;
        transform.SetParent(null, true);
        transform.position = position;
        gameObject.SetActive(true);
        var body = GetComponent<Rigidbody>();
        body.position = position;
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
    }

    public bool Reserve(Squirrel squirrel)
    {
        if (!IsAvailable || squirrel == null) return false;
        Owner = squirrel;
        return true;
    }

    public void Release(Squirrel squirrel)
    {
        if (Owner != squirrel || IsStored) return;
        Owner = null;
        if (!gameObject.activeSelf)
        {
            transform.SetParent(null, true);
            gameObject.SetActive(true);
        }
    }

    public void HideInside(Squirrel squirrel)
    {
        if (Owner != squirrel || IsStored) return;
        StopFoodPhysics();
        transform.SetParent(squirrel.transform, true);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
    }

    /// <summary>Returns the same collected item to the world, rather than creating extra food.</summary>
    public void SpitOut(Squirrel squirrel, Vector3 position, Vector3 velocity)
    {
        if (Owner != squirrel || IsStored) return;
        Release(squirrel);
        transform.position = position;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.mass = 0.1f;
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.position = position;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
    }

    private void StopFoodPhysics()
    {
        if (!TryGetComponent<Rigidbody>(out var body)) return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.useGravity = false;
    }

    public bool Store(Squirrel squirrel, SquirrelNest nest)
    {
        if (Owner != squirrel || nest == null || nest.Owner != squirrel) return false;
        return EnterStorage(nest);
    }

    internal bool EnterStorage(SquirrelNest nest)
    {
        if (IsStored || IsCarried || IsDead || nest == null) return false;
        StopFoodPhysics();
        LastPlayer = null;
        lastHeldAt = float.NegativeInfinity;
        StorageNest = nest;
        Owner = null;
        transform.SetParent(nest.transform, false);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
        return true;
    }
}
