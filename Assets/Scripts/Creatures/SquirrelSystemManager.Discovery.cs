using System;
using UnityEngine;

public sealed partial class SquirrelSystemManager
{
    private void DiscoverScene()
    {
        var scene = gameObject.scene;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node.GetComponent<SquirrelSystemManager>() != null)
                    continue;
                var tree = node.GetComponent<SquirrelTree>();
                if (tree == null && (node.name.StartsWith("Environment Trunk ", StringComparison.OrdinalIgnoreCase) ||
                    Matches(node.name, "Tree")))
                    tree = node.gameObject.AddComponent<SquirrelTree>();
                if (tree != null) Register(tree);
                var squirrel = node.GetComponent<Squirrel>();
                if (squirrel == null && Matches(node.name, "Squirrel") &&
                    node.GetComponentInParent<Squirrel>() == null)
                    squirrel = node.gameObject.AddComponent<Squirrel>();
                if (squirrel != null) Register(squirrel);
                var nest = node.GetComponent<SquirrelNest>();
                if (nest == null && Matches(node.name, "Nest") &&
                    node.GetComponentInParent<SquirrelNest>() == null)
                    nest = node.gameObject.AddComponent<SquirrelNest>();
                if (nest != null) Register(nest);
                var food = node.GetComponent<SquirrelFood>();
                if (food == null && Matches(node.name, "Food") &&
                    node.GetComponentInParent<SquirrelFood>() == null)
                    food = node.gameObject.AddComponent<SquirrelFood>();
                if (food != null) Register(food);
            }
    }

    private static bool Matches(string value, string name) =>
        value.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase);

}
