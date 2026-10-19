using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Run in batch mode with -executeMethod BoneRideValidation.Run -quit.</summary>
public static class BoneRideValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run BoneRideValidation in batch mode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bone ride validation: " + message);
            checks++;
        }
        void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        try
        {
            var camera = new GameObject("Ride validation camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            var player = new GameObject("Ride validation player");
            player.AddComponent<SmoothFirstPersonController>();
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            Call(carrier, "Awake");
            var tentacle = new GameObject("Attached tentacle").AddComponent<TentacleCreature>();
            Call(carrier, "BeginCarry", tentacle);
            Check(carrier.CarriesTentacle && tentacle.IsCarried, "tentacle attaches before mounting");

            var mount = new GameObject("Mount").AddComponent<BoneChild>();
            BoneRide.GetOn(mount, mount.transform, mount.transform, Array.Empty<Transform>());
            Check(mount.Ridden && carrier.TentacleLockedForRide, "mount locks the attached tentacle");
            carrier.Drop();
            carrier.Release();
            Call(carrier, "OnApplicationFocus", false);
            Check(carrier.CarriesTentacle && tentacle.IsCarried, "drop, release and focus loss preserve attachment");
            InputState.Change(mouse, new MouseState());
            Call(carrier, "UpdateHoldRightMouse");
            Check(carrier.CarriesTentacle, "releasing the right mouse button cannot drop the tentacle");

            BoneRide.ScriptedTurn = 0f;
            Vector3 forward = BoneRide.Steer(0f);
            camera.transform.rotation = Quaternion.Euler(70f, 140f, 0f);
            Check(Vector3.Angle(forward, BoneRide.Steer(1f)) < 0.01f, "looking around does not steer");
            BoneRide.ScriptedTurn = -1f;
            Vector3 left = BoneRide.Steer(1f);
            Check(Vector3.SignedAngle(forward, left, Vector3.up) < -44f, "left rein turns left");
            BoneRide.ScriptedTurn = 1f;
            Check(Vector3.Angle(forward, BoneRide.Steer(1f)) < 0.01f, "right rein turns right");
            BoneRide.ScriptedTurn = 0f;
            BoneRide.ScriptedRise = 1f;
            Check(BoneRide.Steer(1f).y > 0.5f, "up input climbs");
            BoneRide.ScriptedRise = -1f;
            Check(BoneRide.Steer(1f).y < -0.5f, "down input descends");
            BoneRide.ScriptedRise = 0f;
            Check(Mathf.Abs(BoneRide.Steer(1f).y) < 0.001f, "neutral input maintains a level direction");

            var tether = player.AddComponent<TentacleTether>();
            Call(tether, "Awake");
            typeof(TentacleTether).GetField("aiming", Private).SetValue(tether, true);
            typeof(TentacleTether).GetField("aimValid", Private).SetValue(tether, true);
            Call(tether, "Update");
            Check(!tether.Aiming && carrier.CarriesTentacle, "riding cancels shot aiming without dropping the creature");
            Check(tentacle.CarriedPrompt.Contains("[W/S]") && tentacle.CarriedPrompt.Contains("[LMB]"), "rider sees the new controls");

            BoneRide.GetOff();
            Check(!mount.Ridden && !carrier.TentacleLockedForRide && !carrier.PlayerBody.isKinematic,
                "dismount unlocks dropping and restores the player body");
            Check(BoneRide.ScriptedTurn == null && BoneRide.ScriptedRise == 0f, "dismount clears scripted controls");
            carrier.Drop();
            Check(!carrier.CarriesTentacle && !tentacle.IsCarried, "dropping works again after dismount");

            Call(carrier, "BeginCarry", tentacle);
            BoneRide.GetOn(mount, mount.transform, mount.transform, Array.Empty<Transform>());
            Call(carrier, "OnDisable");
            Check(carrier.CarriedCreature == null && !tentacle.IsCarried, "component shutdown still cleans up carry ownership");
            Debug.Log($"Bone ride validation passed: {checks} assertions.");
        }
        finally
        {
            BoneRide.GetOff();
            InputSystem.RemoveDevice(mouse);
        }
    }
}
