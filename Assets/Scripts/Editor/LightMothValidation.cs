using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class LightMothValidation
{
    public static IEnumerator Run()
    {
        Action<bool, string> check = FlashBloomValidation.Check;
        AmbientMode oldMode = RenderSettings.ambientMode;
        Color oldAmbient = RenderSettings.ambientLight;
        var moth = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LightEcologySetup.MothPath),
            new Vector3(100f, 3f, 3f), Quaternion.identity).GetComponent<LightMoth>();
        var player = new GameObject("Moth test luminous player").AddComponent<PlayerCreatureStats>();
        player.transform.position = new Vector3(100f, 3f, 0f);
        var lamp = new GameObject("Moth test room light").AddComponent<Light>();
        lamp.type = LightType.Point; lamp.intensity = 10f; lamp.range = 20f;
        lamp.transform.position = moth.transform.position + Vector3.up * 2f; lamp.enabled = false;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(100f, 3f, 1.5f);
        wall.transform.localScale = new Vector3(4f, 4f, 0.3f); wall.SetActive(false);
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * 0.5f;
            var honey = HoneyLuminosityEffect.Apply(player, 50f, 60f);
            Physics.SyncTransforms(); AmbientLuminosity.Invalidate();
            moth.Sense();
            check(moth.TargetRoot == null, "Moth ignores honey glow in a bright environment.");
            RenderSettings.ambientLight = Color.black;
            moth.Sense();
            check(moth.TargetRoot == player.transform && moth.TargetContrast >= 3f,
                "The same luminous player attracts the moth in darkness.");
            lamp.enabled = true; AmbientLuminosity.Invalidate(); moth.Sense();
            check(moth.TargetRoot == null, "Turning on an ordinary room lamp removes sufficient light contrast.");
            lamp.enabled = false; AmbientLuminosity.Invalidate(); moth.Sense();
            check(moth.TargetRoot == player.transform, "Turning the lamp off restores attraction.");
            wall.SetActive(true); Physics.SyncTransforms(); moth.Sense();
            check(moth.TargetRoot == null, "Wall occlusion prevents attraction through scenery.");
            wall.SetActive(false); Physics.SyncTransforms(); moth.Sense();
            float before = Vector3.Distance(moth.transform.position, player.EmissionPosition);
            float until = Time.time + 0.9f;
            while (Time.time < until) yield return null;
            check(Vector3.Distance(moth.transform.position, player.EmissionPosition) < before - 0.3f,
                "Moth physically approaches and orbits the luminous player.");
            FlashBlindness.Apply(moth, 3f); moth.Sense();
            check(moth.State == LightMoth.Activity.Dazzled && moth.TargetRoot == null, "Dazzled moth loses its light target.");
            moth.GetComponent<FlashBlindness>().Advance(4f); moth.Sense();
            check(moth.TargetRoot == player.transform, "Moth resumes light detection after blindness expires.");
            honey.AdvanceTime(61f); moth.Sense();
            check(player.Luminosity == 0f && moth.TargetRoot == null, "Expired honey stops attraction without a player-specific exception.");
            var flash = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlashBloomSetup.PrefabPath),
                moth.transform.position + Vector3.right, Quaternion.identity).GetComponent<FlashBloom>();
            flash.GetComponent<Rigidbody>().isKinematic = true; Physics.SyncTransforms();
            check(flash.TryFlash() && FlashBlindness.IsBlinded(moth), "The first creature's actual flash blinds the second creature.");
            Object.Destroy(flash.gameObject);
        }
        finally
        {
            RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
            Object.Destroy(moth.gameObject); Object.Destroy(player.gameObject); Object.Destroy(lamp.gameObject); Object.Destroy(wall);
            AmbientLuminosity.Invalidate();
        }
    }
}
