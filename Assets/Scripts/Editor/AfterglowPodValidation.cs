using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class AfterglowPodValidation
{
    public static IEnumerator Run()
    {
        Action<bool, string> check = FlashBloomValidation.Check;
        var podPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LightEcologySetup.PodPath);
        var pod = Object.Instantiate(podPrefab, new Vector3(200f, 3f, 0f), Quaternion.identity).GetComponent<AfterglowPod>();
        pod.GetComponent<Rigidbody>().isKinematic = true;
        var source = new GameObject("Pod test source").AddComponent<PlayerCreatureStats>();
        source.transform.position = new Vector3(200f, 3f, 3f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(200f, 3f, 1.5f);
        wall.transform.localScale = new Vector3(4f, 4f, 0.3f); wall.SetActive(false);
        var moth = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LightEcologySetup.MothPath),
            new Vector3(203f, 3f, 0f), Quaternion.identity).GetComponent<LightMoth>();
        AmbientMode oldMode = RenderSettings.ambientMode;
        Color oldAmbient = RenderSettings.ambientLight;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            AmbientLuminosity.Invalidate(); Physics.SyncTransforms();
            source.SetLuminosity(50f); pod.SenseCharge();
            check(!pod.HasStoredLight && pod.Luminosity == 0.5f, "Ordinary bee/honey-scale light does not charge the pod.");
            source.SetLuminosity(1800f); source.transform.position = new Vector3(200f, 3f, 20f);
            pod.SenseCharge(); check(!pod.HasStoredLight, "Strong sources outside the capture radius do not charge the pod.");
            source.transform.position = new Vector3(200f, 3f, 3f);
            wall.SetActive(true); Physics.SyncTransforms(); pod.SenseCharge();
            check(!pod.HasStoredLight, "A wall prevents photon capture.");
            wall.SetActive(false); Physics.SyncTransforms(); FlashBlindness.Apply(pod, 3f);
            pod.SenseCharge();
            check(pod.HasStoredLight && !pod.IsReleasing, "Strong visible light stores energy, even while its vision is dazzled.");
            source.SetLuminosity(0f);
            pod.AdvanceLight(0.5f);
            check(pod.Luminosity == 0.5f && !pod.IsReleasing, "The pod waits before releasing stored light.");
            pod.AdvanceLight(0.8f);
            check(pod.IsReleasing && pod.Luminosity > 90f, "A vanished source leaves a long-lived gentle afterglow.");
            moth.Sense();
            check(moth.TargetRoot == pod.transform, "The stored afterglow attracts the second creature.");
            float remaining = pod.GlowRemaining;
            pod.SenseCharge();
            check(pod.GlowRemaining == remaining, "An active reservoir cannot refresh itself indefinitely.");
            pod.AdvanceLight(20f); pod.SenseCharge();
            check(!pod.HasStoredLight && pod.Luminosity == 0.5f, "Spent light returns to idle and cannot recharge itself.");
            var flash = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlashBloomSetup.PrefabPath),
                new Vector3(200f, 3f, 2f), Quaternion.identity).GetComponent<FlashBloom>();
            flash.GetComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();
            check(flash.TryFlash(), "The first creature releases a real flash near the pod.");
            pod.SenseCharge();
            check(pod.HasStoredLight, "Actual Flash Bloom luminosity charges the third creature.");
            flash.AdvanceFlash(10f); Object.Destroy(flash.gameObject);
            pod.AdvanceLight(1.3f);
            yield return null;
            moth.GetComponent<FlashBlindness>()?.Advance(4f);
            moth.Sense();
            check(moth.TargetRoot == pod.transform, "After the flash, the recovered moth follows the pod's stored light.");
            pod.Die();
            check(pod.Luminosity == 0f && !pod.HasStoredLight, "Dead pods lose stored light.");
        }
        finally
        {
            RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
            Object.Destroy(pod.gameObject); Object.Destroy(source.gameObject); Object.Destroy(wall); Object.Destroy(moth.gameObject);
            AmbientLuminosity.Invalidate();
        }
    }
}
