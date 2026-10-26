using System.Collections.Generic;
using UnityEngine;

/// <summary>Stationary hive: bounded nectar storage, timed fermentation, and physical honey harvest.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class GlowBeeNest : Creature
{
    [Min(0.1f)] [SerializeField] private float fermentationSeconds = 14f;
    [Min(1)] [SerializeField] private int nectarPerHoney = 3;
    [Min(1)] [SerializeField] private int honeyCapacity = 4;
    private readonly List<GlowHoney> products = new List<GlowHoney>();
    private GlowBeeColony colony;
    private float storedNectar, fermentation;
    public Vector3 Entrance => transform.position + transform.forward * 0.65f + Vector3.up * 0.15f;
    public float StoredNectar => storedNectar;
    public float FermentationProgress => fermentation / fermentationSeconds;
    public int HoneyCount { get { RemoveHarvested(); return products.Count; } }
    public override bool CanBeCarried => false;
    protected override bool KeepCorpseStationary => true;
    public override string InteractionPrompt => !IsDead && HoneyCount > 0 ? "Harvest glowing honey" : null;
    internal void Configure(GlowBeeColony owner) => colony = owner;
    protected override void InitializeCreature()
    {
        SetCreatureName("Glow Bee Nest");
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        GetComponent<SphereCollider>().radius = 0.48f;
    }
    public bool TryStoreNectar(float amount)
    {
        if (!SimulationEnabled || IsConsumptionPending || amount <= 0f ||
            float.IsNaN(amount) || float.IsInfinity(amount) || storedNectar + amount > nectarPerHoney * honeyCapacity) return false;
        storedNectar += amount;
        return true;
    }
    protected override void TickCreature(float deltaTime)
    {
        RemoveHarvested();
        if (colony == null || storedNectar < nectarPerHoney || products.Count >= honeyCapacity) return;
        fermentation += deltaTime;
        if (fermentation < fermentationSeconds) return;
        GlowHoney honey = colony.CreateHoney(this, products.Count);
        if (honey == null) return;
        products.Add(honey);
        storedNectar -= nectarPerHoney;
        fermentation = 0f;
    }
    private void RemoveHarvested()
    {
        products.RemoveAll(h => h == null || h.IsCarried || h.IsDead || h.IsConsumptionPending ||
            h.transform.parent != transform || Vector3.Distance(h.transform.position, transform.position) > 1.5f);
    }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead || !SimulationEnabled || interaction.Source == null) return;
        var carrier = interaction.Source.GetComponentInParent<PlayerCreatureCarrier>();
        if (carrier == null) carrier = interaction.Source.transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
        if (carrier == null || carrier.gameObject.scene != gameObject.scene ||
            Vector3.Distance(carrier.CarryCamera != null ? carrier.CarryCamera.transform.position : carrier.transform.position,
                transform.position) > 5f) return;
        RemoveHarvested();
        if (products.Count > 0 && carrier.TryCarryFromInteraction(products[0])) RemoveHarvested();
    }
    protected override void OnDeath()
    {
        foreach (GlowHoney honey in products)
        {
            if (honey == null) continue;
            honey.transform.SetParent(null, true);
            var body = honey.GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
        }
        products.Clear();
    }
}
