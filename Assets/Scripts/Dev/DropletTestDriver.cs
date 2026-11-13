#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Automated Droplet Choir check, only active when the editor is started with -dropletTest
/// (see DropletSceneBuilder.BuildAndTest). Asks the nearest choir for several test glyphs, checks
/// that the droplets reach their places, pushes one out of place and watches it return, and
/// measures how smooth the motion is (top speed and acceleration of every droplet every frame).
/// Writes a report (-dropletTestOut) and pictures (-dropletTestFrames), then quits.
/// </summary>
public sealed class DropletTestDriver : MonoBehaviour
{
    private readonly StringBuilder report = new StringBuilder();
    private string outPath = "droplettest.txt";
    private string framesPath;
    private DropletChoir choir;
    private Vector3[] lastPosition, lastVelocity;
    private readonly List<float> accelerations = new List<float>();
    private float maxSpeed, maxAcceleration;
    private readonly List<float> surfaceMs = new List<float>();
    private int surfaceVerts;
    private bool measuring;
    private bool regionMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        bool regions = System.Array.IndexOf(args, "-regionTitleTest") >= 0;
        if (System.Array.IndexOf(args, "-dropletTest") < 0 && !regions) return;
        var driver = new GameObject("Droplet Test Driver").AddComponent<DropletTestDriver>();
        driver.regionMode = regions;
        int i = System.Array.IndexOf(args, "-dropletTestOut");
        if (i >= 0 && i + 1 < args.Length) driver.outPath = args[i + 1];
        i = System.Array.IndexOf(args, "-dropletTestFrames");
        if (i >= 0 && i + 1 < args.Length) driver.framesPath = args[i + 1];
    }

    private void Log(string line)
    {
        report.AppendLine(line);
        Debug.Log("[DropletTest] " + line);
        File.WriteAllText(outPath, report.ToString());
    }

    private IEnumerator Start()
    {
        if (regionMode) { yield return RegionTest(); yield break; }
        Time.captureFramerate = 60;
        yield return null;
        yield return null;
        Camera cam = Camera.main;
        choir = null;
        float best = float.MaxValue;
        foreach (DropletChoir c in FindObjectsByType<DropletChoir>(FindObjectsSortMode.None))
        {
            float d = cam != null ? Vector3.Distance(cam.transform.position, c.Anchor) : 0f;
            if (d < best) { best = d; choir = c; }
        }
        if (choir == null) { Log("FAIL no DropletChoir in the scene"); Quit(); yield break; }
        Log($"choir '{choir.name}' with {choir.DropletCount} droplets, {best:F1} m from the camera");
        lastPosition = new Vector3[choir.DropletCount];
        lastVelocity = new Vector3[choir.DropletCount];
        for (int i = 0; i < choir.DropletCount; i++) lastPosition[i] = choir.GetDroplet(i).transform.position;
        yield return Wait(1f);
        measuring = true;

        int passed = 0, total = 0;
        string[] keys = { "HELLO", "@spiral", "RAIN 42?", "SKY\nWELL", "@eye" };
        foreach (string key in keys)
        {
            total++;
            string label = key.Replace("\n", "/");
            if (!choir.Show(key)) { Log($"FAIL '{label}': choir refused it"); continue; }
            float start = Time.time;
            while (choir.CurrentPhase == DropletChoir.Phase.Gather && Time.time - start < 30f) yield return null;
            yield return Wait(2f);
            float error = choir.MeanPlaceError();
            bool ok = choir.CurrentPhase == DropletChoir.Phase.Hold && error < 0.05f;
            if (ok) passed++;
            Log($"{(ok ? "PASS" : "FAIL")} '{label}': {choir.GlyphDropletCount} droplets placed, formed after {Time.time - start - 2f:F1} s, mean place error {error * 100f:F1} cm");
            if (cam != null && framesPath != null) Save(cam, Path.Combine(framesPath, "glyph_" + total + ".png"));
            if (key == "HELLO" && framesPath != null) CloseUp("closeup_glyph.png", choir.Anchor + choir.GlyphRotation * new Vector3(-3.2f, 0.2f, 0f));
            surfaceVerts = Mathf.Max(surfaceVerts, choir.SurfaceVertexCount);

            if (key == "HELLO")
            {
                // Push one droplet a metre and a half out of the word; it should glide back.
                total++;
                GlyphDroplet pushed = null;
                for (int i = 0; i < choir.DropletCount && pushed == null; i++)
                    if (Vector3.Distance(choir.GetDroplet(i).transform.position, choir.Anchor) < 3f) pushed = choir.GetDroplet(i);
                Vector3 home = pushed.transform.position;
                pushed.transform.position += new Vector3(0.9f, 1.2f, 0f);
                ResetMotion(pushed);
                yield return Wait(4f);
                float back = Vector3.Distance(pushed.transform.position, home);
                if (back < 0.08f) passed++;
                Log($"{(back < 0.08f ? "PASS" : "FAIL")} pushed droplet back within {back * 100f:F1} cm of its place after 4 s");
            }
            choir.Release();
            start = Time.time;
            float nextNote = Time.time;
            while (choir.CurrentPhase != DropletChoir.Phase.Drift && Time.time - start < 30f)
            {
                if (Time.time >= nextNote)
                {
                    nextNote += 1f;
                    Debug.Log($"[DropletTest] dispersing {Time.time - start:F0} s: surface {choir.LastSurfaceMilliseconds:F2} ms, {choir.SurfaceVertexCount} vertices, real {Time.realtimeSinceStartup:F1}");
                }
                yield return null;
            }
            if (key == "HELLO" && cam != null && framesPath != null)
            {
                yield return Wait(2f);
                Save(cam, Path.Combine(framesPath, "cloud.png"));
                CloseUp("closeup_cloud.png", ClosestPair());
            }
        }

        // Left alone, the choir should gather by itself (the player stands close, so within ~5 s).
        total++;
        float waitStart = Time.time;
        while (choir.CurrentPhase == DropletChoir.Phase.Drift && Time.time - waitStart < 30f) yield return null;
        bool selfStarted = choir.CurrentPhase != DropletChoir.Phase.Drift;
        if (selfStarted) passed++;
        Log($"{(selfStarted ? "PASS" : "FAIL")} gathered on its own after {Time.time - waitStart:F1} s into '{choir.CurrentGlyph}'");
        // Catch it half written, for the picture.
        yield return Wait(3.5f);
        if (cam != null && framesPath != null) Save(cam, Path.Combine(framesPath, "writing.png"));

        measuring = false;
        accelerations.Sort();
        float p99 = accelerations.Count > 0 ? accelerations[(int)(accelerations.Count * 0.99f)] : 0f;
        total++;
        bool smooth = maxSpeed < 2.5f && p99 < 6f;
        if (smooth) passed++;
        Log($"{(smooth ? "PASS" : "FAIL")} smooth motion: top speed {maxSpeed:F2} m/s, 99th-percentile acceleration {p99:F2} m/s2, max {maxAcceleration:F2} m/s2 (the pushed droplet's teleport frames excluded)");
        surfaceMs.Sort();
        float p95 = surfaceMs.Count > 0 ? surfaceMs[(int)(surfaceMs.Count * 0.95f)] : 0f;
        float worstMs = surfaceMs.Count > 0 ? surfaceMs[surfaceMs.Count - 1] : 0f;
        total++;
        bool merged = surfaceVerts > 0 && p95 < 6f;
        if (merged) passed++;
        Log($"{(merged ? "PASS" : "FAIL")} merged surface: up to {surfaceVerts} vertices, rebuild 95th percentile {p95:F2} ms, worst {worstMs:F2} ms per frame");

        // Region titles in the planet's script: summon each, as RegionTitles does on entering a region.
        foreach (DropletChoir c in FindObjectsByType<DropletChoir>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
        var source = gameObject.AddComponent<PlanetGlyphSource>();
        string[] titles = { PlanetGlyphSource.FlowerField, PlanetGlyphSource.Cave, PlanetGlyphSource.MushroomBasin, PlanetGlyphSource.Tower };
        string[] names = { "flower_field", "cave", "mushroom_basin", "tower" };
        Vector3 ahead = cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 10f;
        ahead.y = 0f;
        for (int k = 0; k < titles.Length; k++)
        {
            total++;
            DropletChoir title = DropletChoir.SummonTitle(cam.transform, titles[k], source, choir.DropletMaterial);
            float begin = Time.time;
            while (title != null && title.CurrentPhase != DropletChoir.Phase.Hold && Time.time - begin < 20f) yield return null;
            yield return Wait(2f);
            float placeError = title != null ? title.MeanPlaceError() : -1f;
            int placed = title != null ? title.GlyphDropletCount : 0;
            if (framesPath != null) Save(cam, Path.Combine(framesPath, "title_" + names[k] + ".png"));
            while (title != null && Time.time - begin < 60f) yield return null;
            bool ok = placed > 20 && placeError >= 0f && placeError < 0.08f && title == null;
            if (ok) passed++;
            Log($"{(ok ? "PASS" : "FAIL")} title '{titles[k]}': {placed} creatures, place error {placeError * 100f:F1} cm, gone again after {Time.time - begin:F0} s");
        }
        Log($"RESULT {passed}/{total}");
        Quit();
    }

    /// <summary>
    /// Scene_World check: walks (teleports) the player into each RegionTitles region and confirms that its
    /// title is summoned, written in place and pictured.
    /// </summary>
    private IEnumerator RegionTest()
    {
        var titles = FindFirstObjectByType<RegionTitles>();
        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (titles == null || controller == null) { Log("FAIL no RegionTitles or player in the scene"); Quit(); yield break; }
        var body = controller.GetComponent<Rigidbody>();
        float readyBy = Time.realtimeSinceStartup + 90f;
        Log("waiting for the world to stream in");
        while (Time.realtimeSinceStartup < readyBy) yield return null;
        Time.captureFramerate = 60;
        int passed = 0, total = 0;
        foreach (RegionTitles.Region region in titles.Regions)
        {
            total++;
            if (!FindFloor(region, out Vector3 floor)) { Log($"FAIL {region.name}: no floor found inside the region"); continue; }
            Vector3 toCentre = region.centre - floor;
            toCentre.y = 0f;
            Vector3 facing = toCentre.sqrMagnitude > 4f ? toCentre.normalized : Vector3.forward;
            Teleport(controller, body, floor + Vector3.up * 1.2f, facing);
            float begin = Time.time;
            while ((titles.Active == null || !titles.Active.name.Contains(region.name)) && Time.time - begin < 8f) yield return null;
            DropletChoir title = titles.Active;
            if (title == null || !title.name.Contains(region.name))
            {
                Log($"FAIL {region.name}: no title appeared (player at {floor}, current region {(titles.Current != null ? titles.Current.name : "none")})");
                continue;
            }
            while (title != null && title.CurrentPhase != DropletChoir.Phase.Hold && Time.time - begin < 25f) yield return null;
            yield return Wait(2f);
            float error = title != null ? title.MeanPlaceError() : -1f;
            if (framesPath != null && Camera.main != null) Save(Camera.main, Path.Combine(framesPath, "world_" + region.name.Replace(' ', '_') + ".png"));
            // Turn the head 50 degrees: a screen title must stay put on the screen, every creature in place.
            Vector3 viewBefore = Camera.main.WorldToViewportPoint(title.Anchor);
            Teleport(controller, body, body.position, Quaternion.Euler(0f, 50f, 0f) * facing);
            yield return Wait(0.3f);
            float turnedError = title != null ? title.MeanPlaceError() : -1f;
            Vector3 viewAfter = title != null ? Camera.main.WorldToViewportPoint(title.Anchor) : Vector3.zero;
            float top = 0f;
            for (int i = 0; title != null && i < title.DropletCount; i++)
            {
                GlyphDroplet d = title.GetDroplet(i);
                if (d != null && d.Presence > 0.5f) top = Mathf.Max(top, Camera.main.WorldToViewportPoint(d.transform.position).y);
            }
            Log($"  topmost creature at {top:F3} of the screen height (top border = 1)");
            bool ok = title != null && error >= 0f && error < 0.08f && turnedError < 0.08f && Vector2.Distance(viewBefore, viewAfter) < 0.02f
                && top > 0.9f && top < 0.99f;
            if (ok) passed++;
            Log($"{(ok ? "PASS" : "FAIL")} {region.name}: '{region.title}' appeared {Time.time - begin:F1} s after arriving at {floor}, {title?.GlyphDropletCount} creatures, place error {error * 100f:F1} cm (after a 50 deg turn {turnedError * 100f:F1} cm), screen spot {(Vector2)viewBefore} -> {(Vector2)viewAfter}, surface {title?.LastSurfaceMilliseconds:F2} ms");
        }
        Log($"RESULT {passed}/{total}");
        Quit();
    }

    private static bool FindFloor(RegionTitles.Region region, out Vector3 floor)
    {
        for (int ring = 0; ring < 3; ring++)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f, r = region.radius * (0.15f + ring * 0.2f);
                Vector3 start = region.centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                start.y = Mathf.Min(region.heightRange.y - 0.5f, 60f);
                foreach (RaycastHit hit in Physics.RaycastAll(start, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y < region.heightRange.x + 0.5f || hit.point.y > region.heightRange.y - 2.5f) continue;
                    if (hit.normal.y < 0.6f || hit.collider.attachedRigidbody != null) continue;
                    // Need head room: nothing solid in the 2.2 m above the floor.
                    if (Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.up, 2.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    floor = hit.point;
                    return true;
                }
            }
        floor = default;
        return false;
    }

    private static void Teleport(SmoothFirstPersonController controller, Rigidbody body, Vector3 position, Vector3 facing)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.position = position;
        body.transform.position = position;
        float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        typeof(SmoothFirstPersonController).GetField("yRotation", Private)?.SetValue(controller, yaw);
        typeof(SmoothFirstPersonController).GetField("xRotation", Private)?.SetValue(controller, 0f);
        if (controller.Orientation != null) controller.Orientation.rotation = Quaternion.Euler(0f, yaw, 0f);
        var holder = typeof(SmoothFirstPersonController).GetField("cameraHolder", Private)?.GetValue(controller) as Transform;
        if (holder != null) holder.rotation = Quaternion.Euler(0f, yaw, 0f);
        Physics.SyncTransforms();
    }

    private void LateUpdate()
    {
        if (choir == null || lastPosition == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < choir.DropletCount; i++)
        {
            Vector3 p = choir.GetDroplet(i).transform.position;
            Vector3 v = (p - lastPosition[i]) / dt;
            if (measuring && !float.IsNaN(lastVelocity[i].x))
            {
                float a = (v - lastVelocity[i]).magnitude / dt;
                maxSpeed = Mathf.Max(maxSpeed, v.magnitude);
                maxAcceleration = Mathf.Max(maxAcceleration, a);
                accelerations.Add(a);
            }
            lastPosition[i] = p;
            if (i == 0 && measuring) surfaceMs.Add(choir.LastSurfaceMilliseconds);
            lastVelocity[i] = float.IsNaN(lastVelocity[i].x) ? Vector3.zero : v;
        }
    }

    /// <summary>The middle of the two droplets closest together (where merging shows best).</summary>
    private Vector3 ClosestPair()
    {
        float best = float.MaxValue;
        Vector3 at = choir.Anchor;
        for (int i = 0; i < choir.DropletCount; i++)
            for (int j = i + 1; j < choir.DropletCount; j++)
            {
                Vector3 a = choir.GetDroplet(i).transform.position, b = choir.GetDroplet(j).transform.position;
                float d = (a - b).sqrMagnitude;
                if (d < best) { best = d; at = (a + b) * 0.5f; }
            }
        Log($"closest pair in the cloud: {Mathf.Sqrt(best) * 100f:F1} cm apart");
        return at;
    }

    /// <summary>A picture from a temporary camera 0.9 m in front of a point, looking at it.</summary>
    private void CloseUp(string file, Vector3 target)
    {
        var go = new GameObject("Close-up camera");
        var cam = go.AddComponent<Camera>();
        cam.nearClipPlane = 0.02f;
        cam.fieldOfView = 40f;
        Vector3 back = choir.GlyphRotation * Vector3.back;
        go.transform.position = target + back * 2.2f + Vector3.up * 0.2f;
        go.transform.LookAt(target);
        Save(cam, Path.Combine(framesPath, file));
        Destroy(go);
    }

    private void ResetMotion(GlyphDroplet droplet)
    {
        for (int i = 0; i < choir.DropletCount; i++)
            if (choir.GetDroplet(i) == droplet)
            {
                lastPosition[i] = droplet.transform.position;
                lastVelocity[i] = new Vector3(float.NaN, 0f, 0f); // skip the teleport frame
            }
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }

    private static void Save(Camera cam, string path)
    {
        const int width = 960, height = 540;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        picture.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(target);
        File.WriteAllBytes(path, picture.EncodeToPNG());
        Destroy(picture);
    }

    private static void Quit() => UnityEditor.EditorApplication.Exit(0);
}
#endif
