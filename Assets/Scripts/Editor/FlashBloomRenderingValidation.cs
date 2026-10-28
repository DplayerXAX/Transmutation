using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class FlashBloomRenderingValidation
{
    public static IEnumerator Run()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
        var bloom = Object.FindFirstObjectByType<FlashBloom>();
        FlashBloomValidation.Check(bloom.transform.Find("Glow sac").GetComponent<Renderer>().sharedMaterial.IsKeywordEnabled("_EMISSION"),
            "Flash sac material compiles its actual emission shader variant.");
        bloom.AdvanceFlash(30f);
        var camera = new GameObject("Flash brightness inspection").AddComponent<Camera>();
        camera.transform.position = bloom.EmissionPosition + new Vector3(0f, 1.8f, -1.2f);
        camera.transform.LookAt(bloom.EmissionPosition);
        camera.fieldOfView = 50f;
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
        var target = new RenderTexture(960, 720, 24);
        camera.targetTexture = target;
        yield return null;
        float Capture(string label)
        {
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0f, 0f, 960f, 720f), 0, 0); pixels.Apply();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../flash-bloom-" + label + ".png"));
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            float brightness = 0f;
            foreach (Color pixel in pixels.GetPixels()) brightness += pixel.grayscale / (960f * 720f);
            RenderTexture.active = previous; Object.Destroy(pixels);
            return brightness;
        }
        float before = Capture("before");
        FlashBloomValidation.Check(bloom.TryFlash(), "Rendering fixture releases a fresh flash.");
        yield return null;
        float after = Capture("after");
        FlashBloomValidation.Check(after > before + 0.005f, $"Actual rendered flash is visibly brighter than idle: {before:F4} -> {after:F4}.");
        camera.targetTexture = null; target.Release();
        Object.Destroy(target); Object.Destroy(camera.gameObject);
    }
}
