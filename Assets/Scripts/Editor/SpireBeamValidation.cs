using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Run with -executeMethod SpireBeamValidation.Run in an isolated batch project.</summary>
public static class SpireBeamValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this validation in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var staging = new GameObject("Beam validation");
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Spire beam: " + description);
            checks++;
        }
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);

        try
        {
            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(staging.transform);
            var controller = playerObject.AddComponent<SmoothFirstPersonController>();
            Rigidbody player = playerObject.GetComponent<Rigidbody>();
            var beamObject = new GameObject("Light");
            beamObject.transform.SetParent(staging.transform);
            var beam = beamObject.AddComponent<SpireBeam>();
            Set(beam, "player", player);
            Set(beam, "controller", controller);
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            Set(beam, "axis", origin);
            var stops = (List<Vector3>)Get(beam, "stops");
            stops.Add(origin);
            stops.Add(origin + new Vector3(4f, 30f, 0f));
            stops.Add(origin + new Vector3(8f, 200f, 0f));
            MethodInfo update = typeof(SpireBeam).GetMethod("UpdateActivation", Private);
            int Tick(float seconds) => (int)update.Invoke(beam, new object[] { seconds });
            void At(int stop) => player.position = stops[stop] + Vector3.up * 1.1f;
            void Leave()
            {
                player.position = origin + Vector3.right * 100f;
                Tick(0f);
            }

            At(0);
            Check(Tick(10f) < 0, "locked beam never activates");
            Set(beam, "unlocked", true);
            Check(Tick(1f) < 0, "one second is not enough");
            Check(Tick(1f) == 2, "two uninterrupted seconds sends the inner world to the summit without keys");
            At(2);
            Check(Tick(20f) < 0, "arrival cannot immediately send the player back");
            Leave();
            At(2);
            Check(Tick(2f) == 0, "leaving and reentering the summit enables travel to the inner world");

            Leave();
            At(0);
            Check(Tick(1f) < 0, "charge begins on entry");
            Leave();
            At(0);
            Check(Tick(1f) < 0, "leaving clears partial charge");
            Check(Tick(1f) == 2, "a fresh full stay is required after reentry");

            Leave();
            At(0);
            Tick(1f);
            At(1);
            Check(Tick(1f) < 0, "moving to a different stop resets the countdown");
            Check(Tick(1f) == 2, "ground entrance goes directly to the summit");

            Leave();
            At(0);
            Tick(1f);
            controller.SetRestricted(true);
            Check(Tick(10f) < 0, "restricted movement cannot start travel");
            controller.SetRestricted(false);
            Check(Tick(1f) < 0, "movement restrictions clear partial charge");
            Check(Tick(1f) == 2, "travel works after the restriction ends");

            Leave();
            At(0);
            Set(beam, "riding", true);
            Check(Tick(100f) < 0, "a ride cannot start another ride");
            Set(beam, "riding", false);
            Check(Tick(1f) < 0, "ride time did not accumulate a charge");
            Check(Tick(0f) < 0, "paused game time does not advance activation");
            Check(Tick(1f) == 2, "normal time resumes the wait");

            Leave();
            player.position = origin + Vector3.up * 201.1f;
            Check(Tick(2f) == 0, "the actual light pillar works even when the summit ring is off-axis");
            Leave();
            player.position = origin + Vector3.up * 100f;
            Check(Tick(10f) < 0, "passing through the pillar between stops does not teleport");

            Set(beam, "rideTime", 0f);
            MethodInfo ride = typeof(SpireBeam).GetMethod("Ride", Private);
            IEnumerator completed = (IEnumerator)ride.Invoke(beam, new object[] { 2 });
            Check(!completed.MoveNext(), "a completed ride finishes its coroutine");
            Check(Vector3.Distance(player.position, stops[2] + Vector3.up * 1.1f) < 0.01f, "arrival places the player on the destination ring");
            Check(!player.isKinematic && !(bool)Get(controller, "bubbleMovementLocked"), "arrival restores physics and movement");

            Set(beam, "rideTime", 1000f);
            IEnumerator interrupted = (IEnumerator)ride.Invoke(beam, new object[] { 0 });
            Check(interrupted.MoveNext() && player.isKinematic && (bool)Get(controller, "bubbleMovementLocked"),
                "the player is held safely during transit");
            ((IDisposable)interrupted).Dispose();
            Check(!player.isKinematic && !(bool)Get(controller, "bubbleMovementLocked") && !(bool)Get(beam, "riding"),
                "interrupting a ride restores physics and movement");
            Debug.Log($"Spire beam validation passed: {checks} assertions.");
        }
        finally { Object.DestroyImmediate(staging); }
    }
}
