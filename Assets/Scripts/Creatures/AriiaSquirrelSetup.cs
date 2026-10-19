using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects authored Squirrel/Food/Nest roots in Ariia without changing the scene asset.</summary>
public static class AriiaSquirrelSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.name.Equals("Scene_Ariia", StringComparison.OrdinalIgnoreCase) &&
            !scene.name.Equals("Scene Ariia", StringComparison.OrdinalIgnoreCase)) return;
        FoodManager foods = null;
        SquirrelManager squirrels = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (foods == null) foods = root.GetComponentInChildren<FoodManager>(true);
            if (squirrels == null) squirrels = root.GetComponentInChildren<SquirrelManager>(true);
        }
        if (foods == null) foods = CreateManager<FoodManager>(scene, "Food Manager");
        if (squirrels == null) squirrels = CreateManager<SquirrelManager>(scene, "Squirrel Manager");
        squirrels.SetFoodManager(foods);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node.GetComponent<SquirrelManager>() != null || node.GetComponent<FoodManager>() != null)
                    continue;
                var squirrel = node.GetComponent<Squirrel>();
                if (squirrel == null && Matches(node.name, "Squirrel") &&
                    node.GetComponentInParent<Squirrel>() == null)
                    squirrel = node.gameObject.AddComponent<Squirrel>();
                if (squirrel != null) squirrels.Register(squirrel);
                var nest = node.GetComponent<SquirrelNest>();
                if (nest == null && Matches(node.name, "Nest") &&
                    node.GetComponentInParent<SquirrelNest>() == null)
                    nest = node.gameObject.AddComponent<SquirrelNest>();
                if (nest != null) squirrels.Register(nest);
                var food = node.GetComponent<SquirrelFood>();
                if (food == null && Matches(node.name, "Food") &&
                    node.GetComponentInParent<SquirrelFood>() == null)
                    food = node.gameObject.AddComponent<SquirrelFood>();
                if (food != null) foods.Register(food);
            }
    }

    private static bool Matches(string value, string name) =>
        value.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase);

    private static T CreateManager<T>(Scene scene, string name) where T : Component
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root.AddComponent<T>();
    }
}
