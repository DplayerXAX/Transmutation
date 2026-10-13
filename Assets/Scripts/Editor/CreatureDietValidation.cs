using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FoodTag = Creature.FoodTag;

/// <summary>Checks the shared diet rule in an isolated preview scene without changing authored assets.</summary>
public static class CreatureDietValidation
{
    [MenuItem("Tools/Creatures/Validate Creature Diet")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;

        void Check(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Creature diet validation failed: " + description);
            }

            checks++;
        }

        try
        {
            CaveHarvestItem eater = Create<CaveHarvestItem>(scene);
            CaveHarvestItem sameType = Create<CaveHarvestItem>(scene);
            FireSource otherType = Create<FireSource>(scene);

            Check(eater.FoodTags == FoodTag.None && eater.DietTags == FoodTag.None,
                "new creatures default to empty tags");
            Check(!eater.CanEatSameType, "same-type eating defaults to off");
            Check(Attribute.IsDefined(typeof(FoodTag), typeof(FlagsAttribute)),
                "food tags support multiple Inspector selections");

            Configure(otherType, FoodTag.Meat, FoodTag.None);
            Check(!eater.CanEat(otherType), "an empty diet rejects tagged food");
            Configure(eater, FoodTag.Meat, FoodTag.Meat);
            Check(eater.CanEat(otherType), "different script classes may share a matching food tag");
            Check(!eater.CanEat(null), "null targets are rejected");
            Check(!eater.CanEat(eater), "the eater itself is rejected");

            Configure(otherType, FoodTag.Plant, FoodTag.None);
            Check(!eater.CanEat(otherType), "nonmatching food is rejected");
            Configure(otherType, FoodTag.Meat | FoodTag.Plant, FoodTag.None);
            Check(eater.CanEat(otherType), "one matching tag is sufficient on mixed food");
            Configure(eater, FoodTag.Matter, FoodTag.Plant | FoodTag.Matter);
            Check(eater.CanEat(otherType), "a diet may accept several categories");
            Configure(otherType, FoodTag.Meat, FoodTag.None);
            Check(!eater.CanEat(otherType), "multiple diet tags still reject unrelated food");
            Configure(otherType, FoodTag.Matter, FoodTag.None);
            Check(eater.CanEat(otherType), "the Matter category is accepted when selected");

            Configure(sameType, FoodTag.Plant, FoodTag.None);
            Check(!eater.CanEat(sameType), "matching food of the same concrete class is blocked by default");
            Configure(eater, FoodTag.Matter, FoodTag.Plant, true);
            Check(eater.CanEat(sameType), "the same-type option permits matching food");
            Configure(sameType, FoodTag.Meat, FoodTag.None);
            Check(!eater.CanEat(sameType), "the same-type option does not bypass the diet");
            Check(!eater.CanEat(eater), "same-type permission never allows self eating");

            Configure(eater, FoodTag.None, FoodTag.Everything);
            Configure(otherType, FoodTag.None, FoodTag.None);
            Check(eater.CanEat(otherType), "Everything includes untagged creatures");
            Check(!eater.CanEat(sameType), "Everything respects same-type exclusion");
            Configure(eater, FoodTag.None, FoodTag.Everything, true);
            Check(eater.CanEat(sameType), "Everything accepts an allowed same-type target");
            Check(!eater.CanEat(eater) && !eater.CanEat(null), "Everything still rejects self and null");
            Configure(otherType, (FoodTag)8, FoodTag.None);
            Check(eater.CanEat(otherType), "Everything includes a future category");
            Configure(eater, FoodTag.None, FoodTag.Meat | FoodTag.Plant | FoodTag.Matter);
            Check(!eater.CanEat(otherType), "selecting current categories does not imply Everything");
            Configure(otherType, FoodTag.Everything, FoodTag.None);
            Configure(eater, FoodTag.None, FoodTag.None);
            Check(!eater.CanEat(otherType), "Everything on food cannot bypass an empty diet");

            Configure(eater, FoodTag.Meat | FoodTag.Matter, FoodTag.Plant, true);
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(eater), sameType);
            Check(sameType.FoodTags == (FoodTag.Meat | FoodTag.Matter)
                && sameType.DietTags == FoodTag.Plant && sameType.CanEatSameType,
                "inherited fields retain their values through Unity serialization");

            UnityEngine.Object.DestroyImmediate(otherType.gameObject);
            Check(!eater.CanEat(otherType), "destroyed Unity targets are treated as null");
            Debug.Log("Creature diet validation passed: " + checks + " assertions.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static T Create<T>(Scene scene) where T : Creature
    {
        GameObject root = new GameObject("Diet validation " + typeof(T).Name);
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root.AddComponent<T>();
    }

    private static void Configure(Creature creature, FoodTag food, FoodTag diet, bool sameType = false)
    {
        using (var serialized = new SerializedObject(creature))
        {
            serialized.FindProperty("foodTags").intValue = (int)food;
            serialized.FindProperty("dietTags").intValue = (int)diet;
            serialized.FindProperty("canEatSameType").boolValue = sameType;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
