using UnityEngine;

/// <summary>Passive Plant food. Its only consumption effect is attaching or extending an infection.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class CorruptSeed : Creature
{
    [Tooltip("Shared definition referencing the offspring prefab, independent of this seed instance.")]
    [SerializeField] private CorruptSeedDefinition offspringDefinition;

    public CorruptSeed OffspringPrefab => offspringDefinition != null ? offspringDefinition.Prefab : null;

    protected override void InitializeCreature()
    {
        SetCreatureName("Corrupt Seed");
        SetFoodAndDiet(FoodTags | FoodTag.Plant, FoodTag.None);
    }

    protected override void TickCreature(float deltaTime) { }

    public override void Consume(Creature consumer)
    {
        if (!Application.isPlaying || consumer == null || consumer.IsDead) return;
        if (!consumer.TryGetComponent<CorruptSeedInfection>(out var infection))
            infection = consumer.gameObject.AddComponent<CorruptSeedInfection>();
        infection.Infect(FoodTags, OffspringPrefab);
    }

    /// <summary>Use an inactive staging parent so tags and scene are correct before Awake/OnEnable.</summary>
    internal static CorruptSeed Spawn(CorruptSeed prefab, Transform staging, Vector3 position, FoodTag tags)
    {
        CorruptSeed seed = Instantiate(prefab, position, Quaternion.identity, staging);
        seed.SetFoodAndDiet(tags | FoodTag.Plant, FoodTag.None);
        seed.gameObject.SetActive(true);
        seed.transform.SetParent(null, true);
        return seed;
    }
}
