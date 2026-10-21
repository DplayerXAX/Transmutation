using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repeatable carry/throw integration checks in a disposable scene, without saving authored scenes.</summary>
public static class CarryThrowValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Creatures/Validate Charged Carry and Throw (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Carry/throw: " + message);
            checks++;
        }
        void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        var originalThrower = CreatureThrower.Instance;
        var originalTether = TentacleTether.Instance;
        float originalTimeScale = Time.timeScale;
        Scene scene = SceneManager.CreateScene("Carry Throw Validation " + Guid.NewGuid());
        GameObject root = new GameObject("Carry throw witness"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = new Vector3(10000f, 10000f, 10000f);
        var playerBody = root.AddComponent<Rigidbody>(); playerBody.isKinematic = true;
        var playerCollider = root.AddComponent<CapsuleCollider>();
        var cameraObject = new GameObject("Witness camera"); cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        var anchor = new GameObject("Witness anchor").transform; anchor.SetParent(camera.transform, false);
        anchor.localPosition = new Vector3(0f, -0.35f, 1f);
        var carrier = root.AddComponent<PlayerCreatureCarrier>();
        Set(carrier, "carryCamera", camera); Set(carrier, "carryPoint", anchor);
        root.SetActive(true); carrier.enabled = false;
        var thrower = root.GetComponent<CreatureThrower>(); Set(thrower, "view", camera);
        var staging = new GameObject("Inactive fixtures"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);

        CarryThrowValidationProbe Probe(bool stationary = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(staging.transform, false);
            go.transform.position = root.transform.position + Vector3.forward * 3f;
            var body = go.AddComponent<Rigidbody>(); body.isKinematic = stationary; body.useGravity = !stationary;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (!stationary) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var creature = go.AddComponent<CarryThrowValidationProbe>();
            go.transform.SetParent(null, true);
            return creature;
        }
        void Input(bool pressed, bool held, bool released, float dt) => Call(carrier, "UpdateRightMouse", pressed, held, released, dt);
        void Carry(Creature creature) => Call(carrier, "BeginCarry", creature);
        void Retire(Creature creature)
        {
            carrier.Release();
            // Reclaiming returns flight physics and simulation ownership, including for corpses.
            if (creature != null && !creature.IsCarried) { Carry(creature); carrier.Release(); }
            if (creature != null) Object.DestroyImmediate(creature.gameObject);
        }
        float FlightSpeed()
        {
            var flights = (IList)typeof(CreatureThrower).GetField("flights", Private).GetValue(thrower);
            object flight = flights[flights.Count - 1];
            return ((Vector3)flight.GetType().GetField("velocity").GetValue(flight)).magnitude;
        }

        try
        {
            Time.timeScale = 1f;
            var pickup = Probe(); Physics.SyncTransforms();
            Check(carrier.FindCreature() == pickup, "camera ray finds pickup fixture");
            Input(true, true, false, 0f);
            Set(carrier, "pendingAt", 0f); Call(carrier, "UpdatePendingGrab");
            Input(false, true, false, 2f); Input(false, false, true, 0.02f);
            Check(carrier.CarriedCreature == pickup && !carrier.IsChargingThrow, "holding and releasing pickup click never arms a throw");
            Rigidbody body = pickup.GetComponent<Rigidbody>();
            Check(body.isKinematic && !body.useGravity && body.interpolation == RigidbodyInterpolation.None && pickup.SimulationEnabled,
                "firm grip locks physics while creature behavior continues");
            Check(Physics.GetIgnoreCollision(playerCollider, pickup.GetComponent<Collider>()), "held creature cannot push player");
            Input(true, true, false, 0f); Input(false, false, true, 0.1f);
            Check(!pickup.IsCarried && pickup.Preparations == 0 && body.linearVelocity.z >= 1.49f, "quick tap puts down with existing nudge");
            Check(!body.isKinematic && body.useGravity && body.interpolation == RigidbodyInterpolation.Interpolate &&
                body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic &&
                !Physics.GetIgnoreCollision(playerCollider, pickup.GetComponent<Collider>()), "putdown restores physics and player collisions");
            Object.DestroyImmediate(pickup.gameObject);

            foreach (float duration in new[] { 0.2f, 0.7f, 1.2f, 8f })
            {
                var probe = Probe(); probe.Usable = true; Carry(probe);
                Input(true, true, false, 0f); Input(false, true, false, duration);
                float charge = Mathf.InverseLerp(0.2f, 1.2f, duration);
                Check(carrier.IsChargingThrow && Mathf.Abs(carrier.ThrowCharge01 - charge) < 0.001f, "charge timing at " + duration + " seconds");
                Call(thrower, "LateUpdate");
                Check(typeof(CreatureThrower).GetField("arc", Private).GetValue(thrower) == null, "default debug setting creates no preview");
                Input(false, false, true, 0f);
                Check(!probe.IsCarried && !probe.SimulationEnabled && probe.Preparations == 1 && probe.GetComponent<Rigidbody>().isKinematic,
                    "usable creature hands off to throw flight");
                Check(Mathf.Abs(FlightSpeed() - Mathf.Lerp(4f, 13f, charge)) < 0.001f, "launch strength matches charge");
                Retire(probe);
            }

            foreach (float charge in new[] { -5f, 5f })
            {
                var probe = Probe(); Carry(probe); thrower.Throw(charge);
                Check(Mathf.Abs(FlightSpeed() - (charge < 0f ? 4f : 13f)) < 0.001f, "public throw clamps charge");
                Retire(probe);
            }
            var legacy = Probe(); Carry(legacy); thrower.Throw();
            Check(Mathf.Abs(FlightSpeed() - 13f) < 0.001f, "parameterless throw remains maximum strength");
            Carry(legacy); carrier.Release();
            Check(legacy.GetComponent<Rigidbody>().collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic,
                "reclaiming flight restores the original collision mode"); Retire(legacy);

            float weakDistance = 0f;
            foreach (float charge in new[] { 0f, 1f })
            {
                var probe = Probe(); Carry(probe); thrower.Throw(charge);
                Vector3 start = probe.transform.position;
                for (int step = 0; step < 10; step++) Call(thrower, "FixedUpdate");
                float distance = Vector3.ProjectOnPlane(probe.transform.position - start, Vector3.up).magnitude;
                if (charge == 0f) weakDistance = distance;
                else Check(distance > weakDistance * 3f, "maximum throw travels farther than weak throw under real flight updates");
                Check(probe.transform.position.y < start.y + 0.5f, "flight applies the existing gravity");
                Retire(probe);
            }
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.name = "Throw collision witness"; wall.transform.position = root.transform.position + Vector3.forward * 3f;
            wall.transform.localScale = new Vector3(10f, 10f, 0.2f); Physics.SyncTransforms();
            var blocked = Probe(); Carry(blocked); thrower.Throw();
            for (int step = 0; step < 30 && !blocked.SimulationEnabled; step++) Call(thrower, "FixedUpdate");
            Check(blocked.SimulationEnabled && thrower.LastHit.Contains(wall.name), "throw stops at solid obstacles and returns simulation ownership");
            Retire(blocked); Object.DestroyImmediate(wall);

            var first = Probe(); Carry(first); Input(true, true, false, 0f); Input(false, true, false, 0.7f);
            carrier.Release(); var replacement = Probe(); Carry(replacement); Input(false, false, true, 0.1f);
            Check(replacement.IsCarried && replacement.Preparations == 0, "stale release cannot throw replacement");
            Retire(first); Retire(replacement);

            var overridden = Probe(); Carry(overridden); Input(true, true, false, 0f); Input(false, true, false, 0.7f);
            overridden.ConsumeRightMouse = true; Input(false, true, false, 0.5f);
            Check(!carrier.IsChargingThrow, "custom RMB hook cancels shared charge");
            overridden.ConsumeRightMouse = false; Input(false, false, true, 0.1f);
            Check(overridden.IsCarried && overridden.Preparations == 0, "custom handler cannot leave a delayed throw"); Retire(overridden);

            var paused = Probe(); Carry(paused); Input(true, true, false, 0f); Input(false, true, false, 0.7f);
            Time.timeScale = 0f; Input(false, true, false, 10f);
            Check(Mathf.Abs(carrier.ThrowCharge01 - 0.5f) < 0.001f, "pause does not advance charge");
            Input(false, false, true, 10f); Time.timeScale = 1f; Input(false, false, false, 1f);
            Check(paused.IsCarried && !carrier.IsChargingThrow && paused.Preparations == 0, "paused release is consumed without firing later"); Retire(paused);

            foreach (string cancel in new[] { "focus", "disable", "unavailable" })
            {
                var probe = Probe(); Carry(probe); Input(true, true, false, 0f); Input(false, true, false, 1f);
                if (cancel == "focus") Call(carrier, "OnApplicationFocus", false);
                else if (cancel == "disable") Call(carrier, "OnDisable");
                else { probe.enabled = false; Call(carrier, "Update"); }
                Input(false, false, true, 0.1f);
                Check(!carrier.IsChargingThrow && probe.Preparations == 0, cancel + " cancels without throwing");
                Object.DestroyImmediate(probe.gameObject);
            }

            var stationary = Probe(true); Carry(stationary); carrier.Drop();
            Check(stationary.GetComponent<Rigidbody>().isKinematic && !stationary.GetComponent<Rigidbody>().useGravity, "stationary putdown restores stationary body");
            Object.DestroyImmediate(stationary.gameObject);
            var corpse = Probe(); corpse.Die(); Carry(corpse); thrower.Throw(); Retire(corpse);
            Check(carrier.CarriedCreature == null, "corpse can be carried, thrown, reclaimed and released");

            var preview = Probe(); Carry(preview); Input(true, true, false, 0f); Input(false, true, false, 0.7f);
            Set(thrower, "debugShowThrowTrajectory", true); Call(thrower, "LateUpdate");
            var arc = (LineRenderer)typeof(CreatureThrower).GetField("arc", Private).GetValue(thrower);
            Check(arc != null && arc.gameObject.activeSelf && arc.positionCount > 1, "debug toggle creates charged preview");
            Set(thrower, "debugShowThrowTrajectory", false); Call(thrower, "LateUpdate");
            Check(!arc.gameObject.activeSelf, "turning debug off hides existing preview");
            var hud = root.AddComponent<InteractionPromptHUD>(); Set(hud, "carrier", carrier);
            Check(((string)Call(hud, "DropHint")).Contains("50%"), "HUD displays matching charge percentage"); Retire(preview);

            var tentacleRoot = new GameObject("Throwable tentacle"); tentacleRoot.transform.SetParent(staging.transform, false);
            tentacleRoot.transform.position = root.transform.position + Vector3.forward * 3f;
            var tentacle = tentacleRoot.AddComponent<TentacleCreature>(); tentacleRoot.transform.SetParent(null, true);
            var tether = root.AddComponent<TentacleTether>(); Carry(tentacle);
            Set(tether, "lastCarried", tentacle);
            tentacle.PinArm(root.transform.position + Vector3.left * 2f);
            tentacle.PinArm(root.transform.position + Vector3.right * 2f);
            Check(tentacle.PinnedCount == 2, "tentacle has two real anchors before throwing");
            thrower.Throw(0.5f);
            var visualRoot = (Transform)typeof(TentacleCreature).GetField("tentacleRoot", Private).GetValue(tentacle);
            Check(!tentacle.IsCarried && !tentacle.SimulationEnabled && tentacle.PinnedCount == 0 && !tentacle.Suspended,
                "tentacle detaches anchors and enters ordinary throw flight");
            Check(visualRoot.parent == tentacle.transform && tentacle.transform.localScale == Vector3.one,
                "full-size procedural throw pose follows the tentacle body");
            Vector3 visualBefore = visualRoot.position; Vector3 bodyBefore = tentacle.transform.position;
            Call(thrower, "FixedUpdate");
            Check(Vector3.Distance(visualRoot.position - visualBefore, tentacle.transform.position - bodyBefore) < 0.001f,
                "tentacle visuals move by the thrown body's displacement");
            Call(tether, "Update"); Check(tether.CordCount == 0, "throw cannot create a bridge on the next tether update");
            Carry(tentacle); carrier.Release();
            Check(visualRoot.parent == null && tentacle.SimulationEnabled, "reclaiming flight restores normal procedural authoring");
            Carry(tentacle); Set(tether, "lastCarried", tentacle);
            tentacle.PinArm(root.transform.position + Vector3.left * 2f);
            tentacle.PinArm(root.transform.position + Vector3.right * 2f);
            Input(true, true, false, 0f); Input(false, false, true, 0.1f); Call(tether, "Update");
            Check(tether.CordCount == 1 && tentacle.Suspended, "tap putdown still creates the anchored tentacle bridge");
            tether.ReleaseCreature(tentacle); Carry(tentacle);
            var mountProperty = typeof(BoneRide).GetProperty("Mount");
            object previousMount = mountProperty.GetValue(null);
            try
            {
                mountProperty.GetSetMethod(true).Invoke(null, new object[] { new object() });
                Input(true, true, false, 0f); Input(false, true, false, 2f); Input(false, false, true, 0.1f);
                carrier.Drop(); thrower.Throw();
                Check(tentacle.IsCarried && !carrier.IsChargingThrow && carrier.TentacleLockedForRide,
                    "riding override reserves RMB and blocks putdown and throw");
            }
            finally { mountProperty.GetSetMethod(true).Invoke(null, new[] { previousMount }); }
            carrier.Release();
            Object.DestroyImmediate(tentacleRoot);

            string result = "PASS: " + checks + " charged carry/throw checks.";
            Debug.Log(result); return result;
        }
        finally
        {
            Time.timeScale = originalTimeScale;
            Object.DestroyImmediate(root);
            typeof(CreatureThrower).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { originalThrower });
            typeof(TentacleTether).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { originalTether });
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
