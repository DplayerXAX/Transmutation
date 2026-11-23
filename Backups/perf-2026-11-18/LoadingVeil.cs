using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wordless loading veil. Covers the screen while a scene's world is still being put together after Play starts
/// (terrain chunks, landmarks, flower tiles, objects waiting to settle on the ground), and shows progress as a ring
/// of dots that light one by one plus a thin line that fills. Fades out when everything is ready.
/// Boots itself in any scene that has a ProceduralWorld; nothing needs to be placed in the scene.
/// Note: the editor freeze before the first Play frame (script reload + scene reload) cannot be drawn over from
/// inside the game (see Scripts/README.md, UI).
/// </summary>
public sealed class LoadingVeil : MonoBehaviour
{
    private const int Dots = 24;          // ring size, like the 24 planet glyphs
    private const float MinShow = 0.6f;   // seconds, so a fast load still reads as a beat, not a flicker
    private const float Timeout = 30f;    // never hold the screen longer than this
    private const float FadeTime = 0.8f;

    private readonly List<ProceduralWorld> worlds = new List<ProceduralWorld>();
    private readonly List<HangingSpire> spires = new List<HangingSpire>();
    private readonly List<BoneKiln> kilns = new List<BoneKiln>();
    private readonly List<FloraPatch> flora = new List<FloraPatch>();
    private int settleAtStart;
    private float shown, target, started, fadeFrom = -1f, nextScan;
    private static Texture2D dot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        TryShow(SceneManager.GetActiveScene());
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) TryShow(scene); }

    private static void TryShow(Scene scene)
    {
        if (FindFirstObjectByType<LoadingVeil>() != null) return;
        var world = FindFirstObjectByType<ProceduralWorld>();
        if (world == null || world.gameObject.scene != scene) return;
        new GameObject("Loading veil").AddComponent<LoadingVeil>();
    }

    private void Start()
    {
        started = Time.realtimeSinceStartup;
        worlds.AddRange(FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None));
        spires.AddRange(FindObjectsByType<HangingSpire>(FindObjectsSortMode.None));
        kilns.AddRange(FindObjectsByType<BoneKiln>(FindObjectsSortMode.None));
        flora.AddRange(FindObjectsByType<FloraPatch>(FindObjectsSortMode.None));
        settleAtStart = FindObjectsByType<SettleWhenGroundReady>(FindObjectsSortMode.None).Length;
    }

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now >= nextScan) { nextScan = now + 0.2f; target = Mathf.Max(target, Progress()); }
        shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.5f);
        bool ready = target >= 0.999f && shown >= 0.999f && now - started >= MinShow;
        if (fadeFrom < 0f && (ready || now - started > Timeout))
        {
            fadeFrom = now;
#if UNITY_EDITOR
            Debug.Log($"Loading veil lifts after {now - started:0.0} s (progress {target:P0}{(ready ? "" : ", timed out")}).");
#endif
        }
        if (fadeFrom >= 0f && now - fadeFrom > FadeTime) Destroy(gameObject);
    }

    // Each part counts equally; parts the scene does not have are skipped.
    private float Progress()
    {
        float sum = 0f; int parts = 0;
        foreach (var w in worlds) { if (w == null) continue; sum += w.GenerationComplete ? 1f : w.LayoutReady ? w.LoadProgress : 0f; parts++; }
        if (spires.Count + kilns.Count > 0)
        {
            int built = 0, total = 0;
            foreach (var s in spires) if (s != null && s.isActiveAndEnabled) { total++; if (s.Built) built++; }
            foreach (var k in kilns) if (k != null && k.isActiveAndEnabled) { total++; if (k.Built) built++; }
            if (total > 0) { sum += (float)built / total; parts++; }
        }
        if (flora.Count > 0)
        {
            int grown = 0, total = 0;
            foreach (var f in flora) if (f != null && f.isActiveAndEnabled) { total++; if (f.Grown) grown++; }
            if (total > 0) { sum += (float)grown / total; parts++; }
        }
        if (settleAtStart > 0)
        {
            int left = FindObjectsByType<SettleWhenGroundReady>(FindObjectsSortMode.None).Length;
            // A few objects may never find ground (they fell off the map): 90% settled, or 15 s, counts as done.
            float settled = 1f - Mathf.Clamp01((float)left / settleAtStart);
            sum += Time.realtimeSinceStartup - started > 15f ? 1f : Mathf.Clamp01(settled / 0.9f); parts++;
        }
        return parts == 0 ? 1f : sum / parts;
    }

    private void OnGUI()
    {
        GUI.depth = -1000;
        float alpha = fadeFrom < 0f ? 1f : 1f - Mathf.Clamp01((Time.realtimeSinceStartup - fadeFrom) / FadeTime);
        if (dot == null) dot = MakeDot();
        var old = GUI.color;

        // Black ground of the line world.
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        float unit = Mathf.Min(Screen.width, Screen.height);
        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.47f);
        float radius = unit * 0.07f, size = Mathf.Max(4f, unit * 0.009f);
        float lit = shown * Dots;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4f);

        // Ring of dots, lit clockwise from the top; the next dot breathes.
        for (int i = 0; i < Dots; i++)
        {
            float a = i / (float)Dots * Mathf.PI * 2f;
            Vector2 p = centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
            float on = Mathf.Clamp01(lit - i);
            float grey = Mathf.Lerp(0.22f, 0.95f, on);
            if (on <= 0f && i == Mathf.FloorToInt(lit)) grey = Mathf.Lerp(0.22f, 0.5f, pulse);
            float s = size * Mathf.Lerp(0.7f, 1f, on);
            GUI.color = new Color(grey, grey, grey, alpha);
            GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), dot);
        }

        // Centre mark: one slowly turning stroke, so a stalled frame is visible as stillness.
        GUI.color = new Color(0.8f, 0.8f, 0.8f, alpha);
        var matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Time.realtimeSinceStartup * 40f, centre);
        GUI.DrawTexture(new Rect(centre.x - radius * 0.35f, centre.y - 0.75f, radius * 0.7f, 1.5f), Texture2D.whiteTexture);
        GUI.matrix = matrix;

        // Thin line below the ring that fills left to right.
        float width = unit * 0.32f, y = centre.y + radius + unit * 0.07f;
        GUI.color = new Color(0.25f, 0.25f, 0.25f, alpha);
        GUI.DrawTexture(new Rect(centre.x - width * 0.5f, y, width, 1f), Texture2D.whiteTexture);
        GUI.color = new Color(0.95f, 0.95f, 0.95f, alpha);
        GUI.DrawTexture(new Rect(centre.x - width * 0.5f, y - 0.5f, width * shown, 2f), Texture2D.whiteTexture);

        GUI.color = old;
    }

    private static Texture2D MakeDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 3f));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
