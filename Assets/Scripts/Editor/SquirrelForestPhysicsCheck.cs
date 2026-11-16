using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SquirrelForestPhysicsCheck
{
    const string Request = "Temp/SquirrelForest.physics";
    const string Key = "SquirrelForest.PhysicsCheck";
    static SquirrelForestPhysicsCheck() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(Request))
        {
            File.Delete(Request);
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".owned", !EditorApplication.isPlaying);
            SessionState.SetInt(Key + ".phase", 0);
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (Time.timeSinceLevelLoad < 2f) return;
        int phase = SessionState.GetInt(Key + ".phase", 0);
        if (phase == 1 && Time.timeSinceLevelLoad < 12f) return;
        try
        {
            var log = new StringBuilder(); log.AppendLine("PLAY snapshot at " + Time.timeSinceLevelLoad);
            var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
            var roots = scene.GetRootGameObjects();
            foreach (var ground in roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>(true)).Where(c => c.gameObject.layer == 7))
                log.AppendLine($"GROUND {ground.name}: enabled={ground.enabled}, active={ground.gameObject.activeInHierarchy}, bounds={ground.bounds}");
            var manager = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            log.AppendLine($"Manager initialized={manager.IsInitialized}, squirrels={manager.Squirrels.Count}, nests={manager.Nests.Count}");
            foreach (var nest in manager.Nests)
            {
                var body = nest.GetComponent<Rigidbody>(); var shape = nest.GetComponent<Collider>();
                float floor = float.NaN;
                if (scene.GetPhysicsScene().Raycast(nest.transform.position + Vector3.up * 100f, Vector3.down, out var hit, 300f, 1 << 7, QueryTriggerInteraction.Ignore)) floor = hit.point.y;
                var box = nest.GetComponent<BoxCollider>();
                Vector3 support = nest.transform.TransformPoint(box.center - Vector3.up * box.size.y * 0.5f);
                if (!scene.GetPhysicsScene().Raycast(support + Vector3.up * 5f, Vector3.down, out var supportHit, 10f, 1 << 7, QueryTriggerInteraction.Ignore))
                    throw new Exception("Nest has no terrain beneath it: " + nest.name);
                float gap = support.y - supportHit.point.y;
                log.AppendLine($"  support gap={gap:F3}, terrain alignment={Vector3.Dot(nest.transform.up, supportHit.normal):F3}");
                if (!nest.IsMoving && !nest.IsCarried && Mathf.Abs(gap) > 0.1f) throw new Exception($"Nest floats above terrain: {nest.name}, gap={gap:F3}");
                if (phase == 0)
                {
                    var owner = nest.Owner; var ownerBody = owner.GetComponent<Rigidbody>();
                    Vector3 ownerPosition = ownerBody.position; Vector3 groundPoint = supportHit.point;
                    if (!nest.BeginMove(owner)) throw new Exception("Nest cannot begin owner transport.");
                    Vector3 before = nest.transform.position;
                    owner.transform.position = ownerPosition + Vector3.right * 0.25f;
                    ownerBody.position = owner.transform.position; Physics.SyncTransforms();
                    if (Vector3.Distance(nest.transform.position, before) < 0.2f) throw new Exception("Carried nest does not follow owner.");
                    owner.transform.position = ownerPosition; ownerBody.position = ownerPosition;
                    nest.EndMove(groundPoint);
                    if (nest.IsMoving || !shape.enabled || body.isKinematic || !body.useGravity) throw new Exception("Nest remains frozen after transport.");
                    log.AppendLine("  PASS: owner transport follows movement and restores grounded dynamic physics.");
                }
                log.AppendLine($"NEST {nest.name} pos={nest.transform.position} body={body.position} bottom={shape.bounds.min.y:F3} floor={floor:F3} kinematic={body.isKinematic} gravity={body.useGravity} moving={nest.IsMoving} carried={nest.IsCarried} enabled={shape.enabled} sleeping={body.IsSleeping()} constraints={body.constraints}");
                foreach (var r in nest.GetComponentsInChildren<Renderer>()) log.AppendLine($"  renderer {r.name}: {r.bounds}");
            }
            foreach (var squirrel in manager.Squirrels)
                log.AppendLine($"SQUIRREL {squirrel.name}: pos={squirrel.transform.position} simulation={squirrel.SimulationEnabled} autonomous={squirrel.CanMoveAutonomously} velocity={squirrel.GetComponent<Rigidbody>().linearVelocity} activity={typeof(Squirrel).GetField("activity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(squirrel)}");
            Directory.CreateDirectory("ArtCaptures/SquirrelForest");
            if (phase == 0) File.WriteAllText("ArtCaptures/SquirrelForest/Physics.txt", log.ToString());
            else File.AppendAllText("ArtCaptures/SquirrelForest/Physics.txt", log.ToString());
            if (phase == 0) SessionState.SetInt(Key + ".phase", 1);
            else { SessionState.SetBool(Key, false); if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode(); }
        }
        catch (Exception e)
        {
            File.WriteAllText("ArtCaptures/SquirrelForest/Physics.txt", e.ToString()); SessionState.SetBool(Key, false);
            if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode();
        }
    }
}
