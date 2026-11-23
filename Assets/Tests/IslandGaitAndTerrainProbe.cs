using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Opt-in regression for intentional gait and the sloped deck patch.</summary>
public sealed class IslandGaitAndTerrainProbe : MonoBehaviour
{
    public static string Result = "Not run";
    Keyboard keyboard;
    void OnDestroy() { if (keyboard != null) InputSystem.RemoveDevice(keyboard); }
    IEnumerator Start()
    {
        Result = "Running";
        keyboard = InputSystem.AddDevice<Keyboard>();
        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        var body = FindFirstObjectByType<FirstPersonBody>();
        var frame = FindFirstObjectByType<MovingFrame>();
        yield return new WaitForSeconds(6);
        float phase = body.WalkPhase;
        yield return new WaitForSeconds(2);
        Check(!controller.WalkingAnimationActive && Mathf.Abs(body.WalkPhase-phase)<.001f, "idle aboard has no walking animation");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
        yield return new WaitForSeconds(.7f);
        Check(controller.WalkingAnimationActive && body.WalkPhase>phase+.1f,"walking state plus input advances gait");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        yield return new WaitForSeconds(.5f);
        phase=body.WalkPhase;
        yield return new WaitForSeconds(1);
        Check(!controller.WalkingAnimationActive && Mathf.Abs(body.WalkPhase-phase)<.001f,"stopping input stops walking animation");
        var terrain=frame.terrainSurfaces[0];
        Check(terrain is MeshCollider && SurfaceGeometry.IsTerrain(terrain),"baked patch supports terrain ecology");
        Check(terrain.Raycast(new Ray(frame.transform.TransformPoint(new Vector3(-20,10,14)),Vector3.down),out var hit,20),"hill collision ray");
        Check(frame.transform.InverseTransformPoint(hit.point).y>.2f && frame.Supports(terrain),"nonflat hill is rideable support");
        controller.Body.position=hit.point+Vector3.up*1.3f;
        controller.Body.linearVelocity=frame.VelocityAt(controller.Body.position);
        yield return new WaitForSeconds(1);
        Check(controller.Grounded,"player grounds on curved mesh patch");
        Check(FindObjectsByType<Creature>(FindObjectsSortMode.None).Length>=30 && FindObjectsByType<BoneChild>(FindObjectsSortMode.None).Length>0,"test population and bone dragon spawn");
        Result="PASS: idle gait suppressed, walking input animates, released input stops gait, baked hill collision/ecology/grounding, test creatures and bone dragon";
    }
    static void Check(bool value,string label)
    { if(value)return; Result="FAIL: "+label; throw new InvalidOperationException(Result); }
}
