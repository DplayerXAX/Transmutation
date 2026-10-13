using UnityEngine;

/// <summary>
/// External prefab reference: instantiating a seed must not remap its offspring template to itself.
/// This shared asset keeps the template alive after the consumed seed instance is destroyed.
/// </summary>
[CreateAssetMenu(menuName = "Creatures/Corrupt Seed Definition")]
public sealed class CorruptSeedDefinition : ScriptableObject
{
    [SerializeField] private CorruptSeed prefab;

    public CorruptSeed Prefab => prefab;
}
