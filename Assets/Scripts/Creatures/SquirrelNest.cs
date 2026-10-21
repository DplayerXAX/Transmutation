using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SquirrelNest : Creature
{
    [SerializeField, Min(1)] private int foodCapacity = 12;
    [SerializeField] private List<SquirrelFood> inventory = new List<SquirrelFood>();
    private static readonly List<SquirrelNest> all = new List<SquirrelNest>();
    public static IReadOnlyList<SquirrelNest> All => all;
    public int FoodCapacity => foodCapacity;
    public int StoredFoodCount { get { inventory.RemoveAll(food => food == null || food.IsDead || food.StorageNest != this); return inventory.Count; } }
    public bool IsFull => StoredFoodCount >= foodCapacity;
    public override bool CanBeCarried => !IsMoving && !IsDead;
    public override bool CanBeConsumed => false;
    public override string InteractionPrompt => IsMoving || IsCarried ? null :
        $"[LMB] Take food ({StoredFoodCount}/{foodCapacity}) | [RMB] Carry nest";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetNests() => all.Clear();
    protected override void InitializeCreature()
    {
        SetCreatureName("Squirrel Nest");
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        SetResolvedPolarity(CreaturePolarity.None);
        var body = GetComponent<Rigidbody>();
        body.useGravity = true;
        body.isKinematic = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }
    protected override void TickCreature(float deltaTime) { }
    protected override void OnEnable() { base.OnEnable(); if (!all.Contains(this)) all.Add(this); }
    protected override void OnDisable() { all.Remove(this); base.OnDisable(); }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsMoving || IsCarried || IsDead || interaction.Source == null) return;
        var player = interaction.Source.GetComponentInParent<PlayerCreatureCarrier>() ??
            interaction.Source.transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
        if (player == null || player.gameObject.scene != gameObject.scene) return;
        if (player.CarriedCreature is SquirrelFood held) { TryPlayerDeposit(player, held); return; }
        if (player.CarriedCreature != null || player.PendingCreature != null) return;
        Vector3 direction = player.CarryCamera != null ? player.CarryCamera.transform.forward : player.transform.forward;
        Vector3 origin = player.CarryCamera != null ? player.CarryCamera.transform.position : player.transform.position + Vector3.up;
        if (TryWithdraw(origin + direction * 1.5f, out var food)) player.TryCarryFromInteraction(food);
    }
    public bool TryPlayerDeposit(PlayerCreatureCarrier player, SquirrelFood food)
    {
        if (player == null || player.CarriedCreature != food || food == null || IsFull || IsMoving || IsCarried || IsDead ||
            player.gameObject.scene != gameObject.scene) return false;
        player.Release();
        if (!food.EnterStorage(this)) return false;
        inventory.Add(food);
        return true;
    }
    public bool TryWithdraw(Vector3 position, out SquirrelFood food)
    {
        food = null;
        if (IsMoving || IsCarried || IsDead || StoredFoodCount == 0) return false;
        int index = inventory.Count - 1;
        food = inventory[index];
        inventory.RemoveAt(index);
        food.LeaveStorage(position);
        return true;
    }
    public Squirrel Owner { get; private set; }
    public bool IsMoving { get; private set; }
    private Transform restingParent;
    private Collider[] shapes;
    private bool[] enabledShapes;
    private Rigidbody nestBody;
    private bool wasKinematic;
    private bool usedGravity;

    public bool BeginMove(Squirrel squirrel)
    {
        if (Owner != squirrel || IsMoving || IsCarried || IsDead) return false;
        restingParent = transform.parent;
        shapes = GetComponentsInChildren<Collider>(true);
        enabledShapes = new bool[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            enabledShapes[i] = shapes[i].enabled;
            shapes[i].enabled = false;
        }
        nestBody = GetComponent<Rigidbody>();
        if (nestBody != null)
        {
            wasKinematic = nestBody.isKinematic;
            usedGravity = nestBody.useGravity;
            nestBody.isKinematic = true;
            nestBody.useGravity = false;
        }
        IsMoving = true;
        transform.SetParent(squirrel.transform, true);
        transform.localPosition = new Vector3(0f, 1.2f, -0.9f);
        return true;
    }

    public void EndMove(Vector3 ground)
    {
        if (!IsMoving) return;
        transform.SetParent(restingParent, true);
        SquirrelTree.PlaceOnGround(transform, ground);
        for (int i = 0; i < shapes.Length; i++)
            if (shapes[i] != null) shapes[i].enabled = enabledShapes[i];
        if (nestBody != null)
        {
            nestBody.isKinematic = wasKinematic;
            nestBody.useGravity = usedGravity;
        }
        IsMoving = false;
        Physics.SyncTransforms();
    }

    public bool AssignOwner(Squirrel squirrel)
    {
        if (squirrel == null || (Owner != null && Owner != squirrel)) return false;
        Owner = squirrel;
        return true;
    }

    public bool Deposit(Squirrel squirrel, SquirrelFood food)
    {
        if (squirrel != Owner || food == null || IsFull || IsCarried || IsMoving || IsDead ||
            food.gameObject.scene != gameObject.scene || !food.Store(squirrel, this)) return false;
        inventory.Add(food);
        return true;
    }
}
