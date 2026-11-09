/// <summary>Classified prefab paths used by editor tools that select creatures by name.</summary>
public static class CreaturePrefabPaths
{
    public static string Resolve(string name)
    {
        if (!name.EndsWith(".prefab", System.StringComparison.Ordinal)) name += ".prefab";
        switch (name)
        {
            case "BirdCreature.prefab": return "Assets/Prefabs/Creatures/Birds/BirdCreature.prefab";
            case "BirdBones.prefab": return "Assets/Prefabs/Creatures/Birds/BirdBones.prefab";
            case "Bubble.prefab": return "Assets/Prefabs/Creatures/Bubbles/Bubble.prefab";
            case "Bubble Negative.prefab": return "Assets/Prefabs/Creatures/Bubbles/Bubble Negative.prefab";
            case "BubbleFlower.prefab": return "Assets/Prefabs/Creatures/Bubbles/BubbleFlower.prefab";
            case "TrappingBubble.prefab": return "Assets/Prefabs/Creatures/Bubbles/TrappingBubble.prefab";
            case "Fire Flower.prefab": return "Assets/Prefabs/Creatures/Fire/Fire Flower.prefab";
            case "FireSource.prefab": return "Assets/Prefabs/Creatures/Fire/FireSource.prefab";
            case "Squirrel.prefab": return "Assets/Prefabs/Creatures/Squirrels/Squirrel.prefab";
            case "Nest.prefab": return "Assets/Prefabs/Creatures/Squirrels/Nest.prefab";
            case "Food.prefab": return "Assets/Prefabs/Creatures/Squirrels/Food.prefab";
            case "BounceShroom.prefab": return "Assets/Prefabs/Creatures/Traversal/BounceShroom.prefab";
            case "GooCreature.prefab": return "Assets/Prefabs/Creatures/Traversal/GooCreature.prefab";
            case "CompostCreature.prefab": return "Assets/Prefabs/Creatures/Compost/CompostCreature.prefab";
            case "CorruptSeed.prefab": return "Assets/Prefabs/Creatures/Seeds/CorruptSeed.prefab";
            case "SkySpider.prefab": return "Assets/Prefabs/Creatures/Spiders/SkySpider.prefab";
            case "SkyWeb.prefab": return "Assets/Prefabs/Creatures/Spiders/SkyWeb.prefab";
            case "ProceduralCreature.prefab": return "Assets/Prefabs/Creatures/Prototypes/ProceduralCreature.prefab";
            case "SignedFieldSun.prefab": return "Assets/Prefabs/Creatures/Prototypes/SignedFieldSun.prefab";
            case "GeodeLarva.prefab": return "Assets/Prefabs/Creatures/OreColossus/GeodeLarva.prefab";
            case "FlashBloom.prefab": return "Assets/Prefabs/Creatures/LightEcology/FlashBloom.prefab";
            case "LightMoth.prefab": return "Assets/Prefabs/Creatures/LightEcology/LightMoth.prefab";
            case "AfterglowPod.prefab": return "Assets/Prefabs/Creatures/LightEcology/AfterglowPod.prefab";
            case "GlowHoney.prefab": return "Assets/Prefabs/Creatures/LightEcology/GlowHoney.prefab";
            default: return "Assets/Prefabs/Creatures/" + name;
        }
    }
}
