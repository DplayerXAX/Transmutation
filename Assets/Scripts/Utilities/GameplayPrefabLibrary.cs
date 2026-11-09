using UnityEngine;

/// <summary>Runtime references to authored assets kept in the classified Assets/Prefabs folders.</summary>
[CreateAssetMenu(menuName = "Transmutation/Gameplay Prefab Library")]
public sealed class GameplayPrefabLibrary : ScriptableObject
{
    public GlowHoney glowHoney;
    public SkateShoesPickup skateShoes;
    public SkateShoesVisuals skateShoesVisual;
    public static GameplayPrefabLibrary Load() => Resources.Load<GameplayPrefabLibrary>("GameplayPrefabLibrary");
}
