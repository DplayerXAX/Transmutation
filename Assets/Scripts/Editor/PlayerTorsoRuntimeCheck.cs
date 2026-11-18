using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerTorsoRuntimeCheck
{
    const string Key = "PlayerTorso.RuntimeCheck";
    static PlayerTorsoRuntimeCheck() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/PlayerTorso.check") && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete("Temp/PlayerTorso.check"); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < 3) return;
        SessionState.SetBool(Key, false);
        try
        {
            Directory.CreateDirectory("ArtCaptures/PlayerTorso");
            var torsos = Object.FindObjectsByType<PlayerTorsoAnatomy>(FindObjectsSortMode.None);
            if (torsos.Length == 0) throw new Exception("No torso was generated on the live player.");
            foreach (var torso in torsos)
            {
                var own = torso.GetComponentsInChildren<Renderer>().Where(torso.OwnsRenderer).ToArray();
                if (own.Length != 4 || torso.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("Torso layers or collider isolation failed.");
                if (torso.transform.localScale.y < .25f || torso.transform.localScale.y > 1.4f || Vector3.Dot(torso.transform.up, Vector3.up) < .6f)
                    throw new Exception("Torso is not aligned and scaled between the player's hips and shoulders.");
                if (own.Any(r => !r.enabled || r.sharedMaterial == null)) throw new Exception("Missing visible anatomical layer.");
            }
            var bodies=Object.FindObjectsByType<FirstPersonBody>(FindObjectsSortMode.None);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            foreach(var body in bodies)
            {
                var type=typeof(FirstPersonBody);
                var camera=(Camera)type.GetField("viewCamera",flags).GetValue(body);
                var torso=(Transform)type.GetField("torso",flags).GetValue(body);
                var controller=(SmoothFirstPersonController)type.GetField("controller",flags).GetValue(body);
                var pose=type.GetMethod("PoseTorso",flags);
                float hipHeight=(float)type.GetField("hipHeight",flags).GetValue(body);
                float hipWidth=(float)type.GetField("hipWidth",flags).GetValue(body);
                var orientation=controller.Orientation!=null?controller.Orientation:controller.transform;
                var rotation=camera.transform.rotation;
                try
                {
                    pose.Invoke(body,null);Vector3 before=torso.position;Quaternion beforeRotation=torso.rotation;Vector3 beforeScale=torso.localScale;
                    camera.transform.rotation=Quaternion.LookRotation(Vector3.down,orientation.forward);
                    // Simulate camera-space carry shoulders as well as looking straight down.
                    type.GetField("leftShoulder",flags).SetValue(body,camera.transform.TransformPoint(new Vector3(-.24f,-.3f,-.05f)));
                    type.GetField("rightShoulder",flags).SetValue(body,camera.transform.TransformPoint(new Vector3(.24f,-.3f,-.05f)));
                    pose.Invoke(body,null);
                    if(Vector3.Distance(before,torso.position)>.001f||Quaternion.Angle(beforeRotation,torso.rotation)>.1f||Vector3.Distance(beforeScale,torso.localScale)>.001f)
                        throw new Exception("Looking down or carry shoulders tilted the torso towards the camera.");
                    foreach(float side in new[]{-1f,1f})
                    {
                        var hip=controller.transform.position+Vector3.up*hipHeight+orientation.right*hipWidth*side;
                        var socket=torso.TransformPoint(new Vector3(.14f*side,0,0));
                        if(Vector3.Distance(hip,socket)>.035f)throw new Exception("Torso hip cuff is separated from thigh root.");
                    }
                }
                finally{camera.transform.rotation=rotation;pose.Invoke(body,null);}
            }
            File.WriteAllText("ArtCaptures/PlayerTorso/Runtime.txt", $"PASS: {torsos.Length} live player torso(s) generated in Play Mode; four render layers each, upright shoulder-to-hip pose and valid body scale; no added physics colliders.\nPASS: looking straight down with camera-space carry shoulders leaves torso pose stable; both hip cuffs overlap thigh roots (socket centres within 3.5 cm).\n");
        }
        catch (Exception e) { File.WriteAllText("ArtCaptures/PlayerTorso/Runtime.txt", e.ToString()); Debug.LogException(e); }
        finally { EditorApplication.ExitPlaymode(); }
    }
}
