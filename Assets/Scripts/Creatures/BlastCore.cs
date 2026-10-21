using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(ProjectileAttack))]
public sealed class BlastCore : Creature
{
    [Min(0.01f)] [SerializeField] private float firingInterval = 1f;
    [SerializeField] private Transform muzzle;
    private ProjectileAttack attack;
    private float nextShot;
    public int ShotsFired { get; private set; }
    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsDead || !IsCarried ? null : "Fire blast seed";
    public override string CarriedPrompt => IsDead ? null : "Left click: fire blast seed";
    protected override void InitializeCreature()
    {
        SetCreatureName("Blast Core"); SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        attack = GetComponent<ProjectileAttack>();
        if (muzzle == null) muzzle = transform.Find("Muzzle");
    }
    protected override void TickCreature(float deltaTime) { }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || !SimulationEnabled || IsConsumptionPending || Time.time < nextShot || attack == null) return;
        Vector3 direction = interaction.Direction.normalized;
        if (direction.sqrMagnitude < 0.01f) return;
        Vector3 origin = muzzle != null ? muzzle.position : AimPosition + direction * 0.4f;
        GameObject shooter = Carrier.PlayerBody != null ? Carrier.PlayerBody.gameObject : interaction.Source;
        if (attack.Fire(this, origin, direction, shooter) == null) return;
        nextShot = Time.time + firingInterval; ShotsFired++;
    }
}
