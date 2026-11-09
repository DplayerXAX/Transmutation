using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Play Mode checks for the Cistern Snail: identity, ageing from stored water, the Geode Larva's stillness,
/// and (when the open scene has erosive water) spraying and drinking real droplets.
/// </summary>
public static class CisternSnailValidation
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;

    private sealed class Failure : Exception { public Failure(string message) : base(message) { } }

    private static void Check(bool condition, string description)
    { if (!condition) throw new Failure("Cistern Snail: " + description); checks++; }

    private static void Set(object target, string name, object value)
    {
        Type type = target.GetType(); FieldInfo field = null;
        while (type != null && field == null) { field = type.GetField(name, Any); type = type.BaseType; }
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static object Call(object target, string name, params object[] args)
    {
        Type type = target.GetType();
        while (type != null)
        {
            foreach (MethodInfo method in type.GetMethods(Any | BindingFlags.DeclaredOnly))
                if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(target, args);
            type = type.BaseType;
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }

    private static CisternSnail Snail(Vector3 at)
    {
        var go = new GameObject("Validation Cistern Snail");
        go.SetActive(false);
        go.transform.position = at;
        go.AddComponent<Rigidbody>().isKinematic = true;
        var snail = go.AddComponent<CisternSnail>();
        go.SetActive(true);
        return snail;
    }

    [MenuItem("Tools/Creatures/Cistern Snail/Validate (Play Mode)")]
    public static void Validate() => Debug.Log(Run());

    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        ErosiveFluidSystem fluid = Object.FindFirstObjectByType<ErosiveFluidSystem>();
        bool water = fluid != null && fluid.isActiveAndEnabled && fluid.World != null && fluid.World.LayoutReady;
        Vector3 origin = water ? fluid.World.transform.position + Vector3.up * 60f : new Vector3(0f, -900f, 0f);
        CisternSnail snail = null;
        GeodeLarva larva = null;
        try
        {
            // ---- Identity ----
            snail = Snail(origin);
            Check(snail.CanBeCarried && snail.CanUseWhileCarried && snail.Polarity == CreaturePolarity.None, "carryable, usable while carried, never an AI target");
            Check(snail.transform.Find("Visual/Shell") != null, "builds its shell");
            Check(snail.Stored == 0 && snail.CarriedPrompt.StartsWith("Empty") && snail.InteractionPrompt == null, "starts empty with nothing to spray");

            // ---- Stored water ages it ----
            Set(snail, "ageRate", 0f);
            Call(snail, "SetRemainingAge", 100f);
            Call(snail, "TickCreature", 1f);
            Check(Mathf.Abs(snail.Age - 100f) < 1e-3f, "an empty shell does not age it");
            snail.SetStored(snail.Capacity);
            Call(snail, "TickCreature", 1f);
            Check(Mathf.Abs(snail.Age - 98f) < 1e-3f, $"a full shell ages it two seconds a second (age {snail.Age:0.000})");
            snail.SetStored(snail.Capacity / 2);
            Call(snail, "TickCreature", 1f);
            Check(Mathf.Abs(snail.Age - 97f) < 1e-3f, "half full, half as fast");

            // ---- A Geode Larva's aura keeps it from ageing ----
            var larvaObject = new GameObject("Validation Larva");
            larvaObject.SetActive(false);
            larvaObject.transform.position = origin + Vector3.right * 2f;
            larvaObject.AddComponent<Rigidbody>().isKinematic = true;
            larva = larvaObject.AddComponent<GeodeLarva>();
            larvaObject.SetActive(true);
            float before = snail.Age;
            Call(snail, "TickCreature", 1f);
            Check(larva.StillnessActive && snail.Age == before, "beside a free larva the stored water does not age it");
            Object.DestroyImmediate(larvaObject); larva = null;

            if (!water) return Done("no erosive water in the open scene, so spraying and drinking were skipped");

            // ---- Spraying and drinking real water ----
            snail.SetStored(10);
            int active = fluid.ActiveCount;
            int sent = snail.Release(Vector3.up, 5, 4f, 0.2f);
            Check(sent == 5 && snail.Stored == 5 && fluid.ActiveCount == active + 5, "releasing turns stored water into droplets");
            Check(snail.Drink() == 0, "it does not swallow its own spray straight back");
            Set(snail, "drinkAfter", 0f);
            int drunk = snail.Drink();
            Check(drunk > 0 && snail.Stored == 5 + drunk, $"free droplets by the shell are drunk ({drunk})");
            Check(snail.Drink() == 0, "a drunk droplet cannot be drunk twice");
            snail.SetStored(snail.Capacity);
            Check(snail.Drink() == 0, "a full shell drinks nothing");
            return Done("spraying and drinking checked with the scene's water");
        }
        finally
        {
            if (larva != null) Object.DestroyImmediate(larva.gameObject);
            if (snail != null) Object.DestroyImmediate(snail.gameObject);
        }
    }

    private static string Done(string note)
    {
        string result = $"PASS: {checks} Cistern Snail checks ({note}).";
        Debug.Log(result);
        return result;
    }
}
