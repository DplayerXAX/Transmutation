using UnityEditor;
using UnityEngine;

/// <summary>Compatibility entry points now validate the rooted Recycle rather than retired platform riding.</summary>
public static class CompostCreatureValidation
{
    public static string ValidateAssets() => RecycleValidation.ValidateAssets();
    public static string Run() => RecycleValidation.Run();
    [MenuItem("Tools/Creatures/Compost/Validate Gameplay (Play Mode)")]
    public static void Validate() => Debug.Log(Run());
}
