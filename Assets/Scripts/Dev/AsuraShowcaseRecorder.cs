using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Explicit development capture after all hurt/pose updates have run.</summary>
[DefaultExecutionOrder(10000)]
public sealed class AsuraShowcaseRecorder : MonoBehaviour
{
    public string Report { get; private set; } = "Recording";
    private AsuraShowcase show;
    private MeleeAsura[] duel;
    private int contacts, frame;
    private float start, nextFrame;
    private readonly List<string> samples = new List<string>();
    private string folder;
    private RenderTexture rt;
    private Texture2D texture;
    private void Start()
    {
        show = FindFirstObjectByType<AsuraShowcase>(); show.Select(0);
        duel = FindObjectsByType<MeleeAsura>(FindObjectsSortMode.None);
        foreach (var c in duel) c.Damaged += (_, __, ___) => contacts++;
        folder = Path.GetFullPath("Temp/AsuraFeelCapture"); Directory.CreateDirectory(folder);
        rt = new RenderTexture(640, 360, 24); texture = new Texture2D(640, 360, TextureFormat.RGB24, false);
        start = Time.time; nextFrame = start + 0.08f;
    }
    private void LateUpdate()
    {
        if (frame >= 100 || Time.time < nextFrame) return;
        nextFrame += 0.08f;
        Camera camera = show.Observer;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(folder, frame.ToString("D3") + ".png"), texture.EncodeToPNG());
        camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
        if (duel.Length == 2) samples.Add($"{Time.time-start:F3},{duel[0].Health},{duel[1].Health},{Vector3.Distance(duel[0].transform.position,duel[1].transform.position):F3},{duel[0].transform.position.y:F3},{duel[1].transform.position.y:F3}");
        frame++;
        if (frame != 100) return;
        File.WriteAllLines(Path.Combine(folder, "duel.csv"), samples);
        Report = "Duel contacts: " + contacts + "; frames: 100; elapsed: " + (Time.time-start).ToString("F2");
        File.WriteAllText(Path.Combine(folder, "report.txt"), Report);
        rt.Release(); Destroy(rt); Destroy(texture);
    }
}
