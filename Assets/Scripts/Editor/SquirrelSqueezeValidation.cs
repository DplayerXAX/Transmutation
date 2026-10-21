using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Runs the authored Ariia player and held-use flow, including food ownership and reuse.</summary>
public static class SquirrelSqueezeValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator routine;
    private static bool batch;
    private static double deadline;
    private const string BatchKey = "SquirrelSqueezeValidation.Batch";

    [InitializeOnLoadMethod]
    private static void Install()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(BatchKey, false)) return;
        SessionState.SetBool(BatchKey, false);
        batch = true;
        deadline = EditorApplication.timeSinceStartup + 90;
        routine = Run();
        EditorApplication.update += Step;
    }

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Ariia.unity");
        Install();
        SessionState.SetBool(BatchKey, true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Creatures/Validate Squirrel Squeeze (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Open Scene_Ariia and enter Play Mode first.");
        if (routine != null) return;
        batch = false;
        deadline = EditorApplication.timeSinceStartup + 90;
        routine = Run();
        EditorApplication.update += Step;
    }

    private static void Step()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Squirrel squeeze validation timed out.");
            if (routine.MoveNext()) return;
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Step;
        (routine as IDisposable)?.Dispose();
        routine = null;
        if (batch) EditorApplication.Exit(code);
    }

    private static IEnumerator Run()
    {
        while (!Application.isPlaying || Object.FindFirstObjectByType<CreatureThrower>() == null) yield return null;
        // Let the scene's managers initialize and the player settle on the actual floor.
        float until = Time.time + 0.5f;
        while (Time.time < until) yield return null;
        var controller = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        var interactor = Object.FindFirstObjectByType<PlayerCreatureInteractor>();
        Check(controller != null && carrier != null && interactor != null, "Ariia contains the shared player and interaction components");
        Check(carrier.InputMode == PlayerCreatureCarrier.CarryInput.ToggleRightMouse, "Daniel's right-click toggle control is retained");
        Check(Camera.main == controller.PlayerCamera, "the player camera replaces the overview camera");
        Check(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 1, "only one camera is active");
        Check(controller.Grounded, "player spawn stands on Ariia ground");
        var squirrel = Object.FindFirstObjectByType<Squirrel>();
        Check(squirrel != null && squirrel.Nest != null, "existing squirrel ecology initializes");
        var inventory = (List<SquirrelFood>)typeof(Squirrel).GetField("carried", Private).GetValue(squirrel);
        foreach (var food in inventory) if (food != null) food.Release(squirrel);
        inventory.Clear();
        var foods = new List<SquirrelFood>();
        Vector3 scale = squirrel.transform.localScale;
        int stored = squirrel.Nest.StoredFoodCount;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                item.name = "Squeeze validation food";
                item.transform.localScale = Vector3.one * 0.2f;
                var food = item.AddComponent<SquirrelFood>();
                foods.Add(food);
                Check(food.Reserve(squirrel), "food can be reserved");
                food.HideInside(squirrel);
                inventory.Add(food);
            }
            // A world click must not empty an unheld squirrel.
            squirrel.ReceiveInteraction(new CreatureInteraction(controller.gameObject, squirrel.transform.position, Vector3.forward));
            Check(squirrel.CarriedCount == 3, "unheld squirrels cannot be squeezed");
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { squirrel });
            Check(carrier.CarriedCreature == squirrel && squirrel.IsCarried, "the existing carry mechanism picks up a squirrel");
            Check(!CreatureThrower.Instance.HoldingThrowable, "left click uses the squirrel instead of throwing it");
            for (int remaining = 2; remaining >= 0; remaining--)
            {
                interactor.TryInteract();
                interactor.TryInteract(); // Repeated input during one squeeze cannot release a second item.
                until = Time.time + 0.4f;
                bool compressed = false;
                while (Time.time < until)
                {
                    compressed |= squirrel.transform.localScale.y < scale.y * 0.95f;
                    yield return null;
                }
                Check(compressed, "squeezing visibly compresses the squirrel");
                Check(squirrel.CarriedCount == remaining, "each completed press releases exactly one food");
                Check(Vector3.Distance(squirrel.transform.localScale, scale) < 0.001f, "squeeze restores the original randomized size");
                Check(carrier.CarriedCreature == squirrel, "squirrel remains held after use");
            }
            Check(squirrel.Nest.StoredFoodCount == stored, "spitting does not withdraw stored nest food");
            foreach (var food in foods)
            {
                Check(food.IsAvailable && food.Owner == null && food.transform.parent == null, "spat food returns to the world without an owner");
                Check(food.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic && body.useGravity, "spat food falls with physics");
                Check(food.Reserve(squirrel), "spat food can be collected again");
                food.HideInside(squirrel);
                inventory.Add(food);
                Check(body.isKinematic && !body.useGravity, "recollection clears temporary food physics");
            }
            interactor.TryInteract();
            carrier.Release();
            squirrel.SetSimulationEnabled(false);
            until = Time.time + 0.1f;
            while (Time.time < until) yield return null;
            Check(Vector3.Distance(squirrel.transform.localScale, scale) < 0.001f, "dropping during a squeeze restores size");
            Check(squirrel.CarriedCount == 3, "dropping before compression does not lose food");
            Debug.Log("Squirrel squeeze validation PASSED: Ariia player, camera, ground, carry, held input, one-at-a-time food, compression, ownership, recollection and interrupted use.");
        }
        finally
        {
            carrier.Release();
            squirrel.SetSimulationEnabled(true);
            inventory.RemoveAll(food => food == null || foods.Contains(food));
            foreach (var food in foods) if (food != null) Object.Destroy(food.gameObject);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Squirrel squeeze: " + description);
    }
}
