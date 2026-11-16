using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SquirrelForestMotionCheck
{
    const string Key = "SquirrelForest.MotionCheck";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static SquirrelForestMotionCheck() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/SquirrelForest.motion"))
        {
            File.Delete("Temp/SquirrelForest.motion"); SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".owned", !EditorApplication.isPlaying); SessionState.SetFloat(Key + ".next", 2);
            File.WriteAllText("ArtCaptures/SquirrelForest/Motion.txt", "Motion observation\n");
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < SessionState.GetFloat(Key + ".next", 2)) return;
        try
        {
            var log = new StringBuilder(); log.AppendLine($"TIME {Time.timeSinceLevelLoad:F1}");
            var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
            var manager = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            foreach (var s in manager.Squirrels)
            {
                var body = s.GetComponent<Rigidbody>(); var motor = s.GetComponent<SquirrelMotor>();
                var intent = (Vector3)typeof(SquirrelMotor).GetField("desiredVelocity", Private).GetValue(motor);
                string sweep = body.SweepTest(intent.normalized, out var hit, 1.5f, QueryTriggerInteraction.Ignore) ? $"{hit.collider.name}/{hit.normal}" : "none";
                string old = SessionState.GetString(Key + s.name, "");
                Vector3 last = old.Length == 0 ? body.position : JsonUtility.FromJson<Point>(old).position;
                float displacement = Vector3.ProjectOnPlane(body.position - last, Vector3.up).magnitude;
                SessionState.SetString(Key + s.name, JsonUtility.ToJson(new Point { position = body.position }));
                log.AppendLine($"{s.name} pos={body.position} delta={displacement:F3} velocity={body.linearVelocity} intent={intent} activity={typeof(Squirrel).GetField("activity",Private).GetValue(s)} sim={s.SimulationEnabled} auto={s.CanMoveAutonomously} kinematic={body.isKinematic} bypass={typeof(SquirrelMotor).GetField("hasBypassPoint", Private).GetValue(motor)} sweep={sweep}");
            }
            File.AppendAllText("ArtCaptures/SquirrelForest/Motion.txt", log.ToString()); SessionState.SetFloat(Key + ".next", Time.timeSinceLevelLoad + 5f);
            if (Time.timeSinceLevelLoad >= 47f) { SessionState.SetBool(Key, false); if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode(); }
        }
        catch (Exception e) { File.AppendAllText("ArtCaptures/SquirrelForest/Motion.txt", e.ToString()); SessionState.SetBool(Key, false); if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode(); }
    }
    [Serializable] private class Point { public Vector3 position; }
}
