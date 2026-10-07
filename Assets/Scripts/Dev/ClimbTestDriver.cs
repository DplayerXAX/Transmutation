#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Automated climbing check. Only active when the editor is started with -climbTest
/// (see ClimbTestLauncher): finds cliffs in the generated world and the Hanging Spire,
/// climbs each one by holding "up", and writes what happened to a text file.
/// </summary>
public sealed class ClimbTestDriver : MonoBehaviour
{
    private struct Cliff
    {
        public Vector3 start;   // body centre, hanging at the wall
        public Vector3 feet;    // ground below the start, for reporting
        public Vector3 inward;
        public float topY;
        public string name;
    }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly StringBuilder report = new StringBuilder();
    private string outPath = "climbtest.txt";
    private string framesPath;
    private int frameIndex;
    private SmoothFirstPersonController controller;
    private HandClimber climber;
    private ProceduralWorld world;
    private Rigidbody body;
    private float halfHeight = 1f;
    private int passed, total;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-climbTest") < 0) return;
        var driver = new GameObject("Climb Test Driver").AddComponent<ClimbTestDriver>();
        int i = System.Array.IndexOf(args, "-climbTestOut");
        if (i >= 0 && i + 1 < args.Length) driver.outPath = args[i + 1];
        i = System.Array.IndexOf(args, "-climbTestFrames");
        if (i >= 0 && i + 1 < args.Length) driver.framesPath = args[i + 1];
    }

    private void Log(string line)
    {
        report.AppendLine(line);
        Debug.Log("[ClimbTest] " + line);
        File.WriteAllText(outPath, report.ToString());
    }

    private IEnumerator Start()
    {
        savedProgress = File.Exists(LandmarkProgress.FilePath) ? File.ReadAllText(LandmarkProgress.FilePath) : null;
        controller = FindFirstObjectByType<SmoothFirstPersonController>();
        climber = FindFirstObjectByType<HandClimber>();
        world = FindFirstObjectByType<ProceduralWorld>();
        if (controller == null || climber == null || world == null)
        {
            Log("Missing controller, climber or world.");
            Quit();
            yield break;
        }
        body = controller.GetComponent<Rigidbody>();
        var capsule = controller.GetComponent<CapsuleCollider>();
        if (capsule != null) halfHeight = capsule.height * 0.5f * controller.transform.lossyScale.y;
        climber.ScriptedInput = Vector2.zero;

        // Wait for every chunk to be meshed.
        FieldInfo generating = typeof(ProceduralWorld).GetField("generating", Private);
        FieldInfo built = typeof(ProceduralWorld).GetField("chunksBuilt", Private);
        float waitStart = Time.realtimeSinceStartup;
        int lastBuilt = -1;
        float stableSince = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - waitStart < 180f)
        {
            int now = (int)built.GetValue(world);
            if (now != lastBuilt)
            {
                lastBuilt = now;
                stableSince = Time.realtimeSinceStartup;
            }
            if (!(bool)generating.GetValue(world) && Time.realtimeSinceStartup - stableSince > 2f) break;
            yield return null;
        }
        Log($"World ready: {lastBuilt} chunks after {Time.realtimeSinceStartup - waitStart:0}s at {world.transform.position}. Player half height {halfHeight:0.00}.");
        Time.timeScale = 3f;

        bool spireOnly = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestSpireOnly") >= 0;
        List<Cliff> cliffs = spireOnly ? new List<Cliff>() : FindCliffs(10, 1.5f, 12f, 1, 2f);
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestTall") >= 0)
            cliffs.AddRange(FindCliffs(3, 10f, 30f, 2, 3f));
        Log($"Found {cliffs.Count} cliffs.");
        for (int i = 0; i < cliffs.Count; i++)
            yield return Climb(cliffs[i], fast: i % 2 == 1, timeout: i < 10 ? 40f : 80f, fromGround: i % 3 == 0);

        Cliff spire;
        yield return CheckSpireCreatures();
        int faces = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestCreaturesOnly") >= 0 ? 0 : 4;
        for (int face = 0; face < faces; face++)
            if (FindSpire(out spire, face * 90f)) yield return Climb(spire, fast: face % 2 == 1, timeout: 200f, fromGround: false, capture: face == 0);
        else Log("Spire: not found.");

        Log($"RESULT {passed}/{total} climbs reached the top.");
        Quit();
    }

    /// <summary>Tentacle creatures on the tower: where they are, then carry one and climb with it.</summary>
    private IEnumerator CheckSpireCreatures()
    {
        yield return new WaitForSeconds(4f);
        var crawlers = FindObjectsByType<SpireCrawler>(FindObjectsSortMode.None);
        var tentacles = FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None);
        Log($"Tentacle creatures: {tentacles.Length}, crawling on the tower: {crawlers.Length}");
        foreach (SpireCrawler crawler in crawlers)
        {
            float gap = crawler.wall != null ? Vector3.Distance(crawler.wall.ClosestPoint(crawler.transform.position), crawler.transform.position) : -1f;
            Log($"    {crawler.name} at height {crawler.transform.position.y - crawler.axisBase.y:0.0} m, {gap:0.00} m from the wall");
        }
        if (crawlers.Length > 0)
        {
            Vector3 before = crawlers[0].transform.position;
            yield return new WaitForSeconds(6f);
            Log($"    {crawlers[0].name} moved {Vector3.Distance(before, crawlers[0].transform.position):0.0} m in 6 s");
            if (framesPath != null)
            {
                // A look at it from 4 m out.
                Camera main = Camera.main;
                Transform crawlerTransform = crawlers[0].transform;
                Vector3 normal = crawlerTransform.up;
                if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
                captureCamera.CopyFrom(main);
                captureCamera.enabled = false;
                Vector3 from = crawlerTransform.position + normal * 4f + Vector3.up * 1f;
                captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(crawlerTransform.position - from));
                Directory.CreateDirectory(framesPath);
                Save(captureCamera, Path.Combine(framesPath, "crawler.png"));
            }

            // Carry it (as a right click would) and climb the tower with it on the hand.
            var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            body.isKinematic = false;
            body.position = crawlers[0].transform.position + Vector3.up * 0.5f;
            Physics.SyncTransforms();
            yield return null;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { crawlers[0].GetComponent<TentacleCreature>() });
            Log($"    carried: {carrier.CarriesTentacle}, sticky hands: {climber.StickyHands}");
        }
    }

    private static string savedProgress;

    private static void Quit()
    {
        // Put back the real save, so test climbs never count as collected seeds or glyphs.
        if (savedProgress != null) File.WriteAllText(LandmarkProgress.FilePath, savedProgress);
        else if (File.Exists(LandmarkProgress.FilePath)) File.Delete(LandmarkProgress.FilePath);
        UnityEditor.EditorApplication.Exit(0);
    }

    // ---------------- One climb ----------------

    private IEnumerator Climb(Cliff cliff, bool fast, float timeout, bool fromGround, bool capture = false)
    {
        total++;
        climber.ScriptedInput = Vector2.zero;
        // Start standing at the foot of the wall, or next to it in the air as if the player had jumped at it.
        fromGround &= cliff.start.y - cliff.feet.y < 2.5f;
        if (fromGround)
        {
            Vector3 stand = cliff.feet + Vector3.up * (halfHeight + 0.1f) - cliff.inward * 0.1f;
            Teleport(stand, cliff.inward);
            yield return new WaitForSeconds(0.5f);
        }
        else Teleport(cliff.start, cliff.inward);

        // Why a climb would not start: report what the climber sees from here.
        var findWall = typeof(HandClimber).GetMethod("FindWall", Private);
        var wallIsTall = typeof(HandClimber).GetMethod("WallIsTall", Private);
        object[] args = { null };
        bool sees = (bool)findWall.Invoke(climber, args);
        var seen = (RaycastHit)args[0];
        Vector3 forward = controller.Orientation != null ? controller.Orientation.forward : controller.transform.forward;
        Physics.SphereCast(body.position + Vector3.up * 0.3f, 0.25f, forward, out RaycastHit any, 3f, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore);
        Log($"  start {cliff.name}: grounded {controller.Grounded}, sees wall {sees} (tall {(sees && (bool)wallIsTall.Invoke(climber, new object[] { seen }))}), " +
            $"facing {Vector3.Angle(forward, cliff.inward):0} deg off, first hit {any.distance:0.00} m angle {Vector3.Angle(any.normal, Vector3.up):0} on {(any.collider != null ? any.collider.name : "-")}, " +
            $"{(fromGround ? "from the ground" : "from the air")}");

        climber.ScriptedFast = fast;
        climber.ScriptedInput = new Vector2(0f, 1f);
        var events = new List<string>();
        string lastSeen = climber.LastEvent;
        float start = Time.time, onTopSince = -1f, maxFeet = float.MinValue;
        bool started = false, success = false, stallReported = false;
        float nextCapture = 0f;
        float lastProgress = Time.time;
        int climbs = 0;
        bool wasActive = false;
        while (Time.time - start < timeout)
        {
            bool active = climber.IsClimbing || climber.IsMantling;
            if (active && !wasActive) climbs++;
            wasActive = active;
            started |= active;
            float feet = body.position.y - halfHeight;
            if (feet > maxFeet + 0.3f) lastProgress = Time.time;
            maxFeet = Mathf.Max(maxFeet, feet);
            if (active && !stallReported && Time.time - lastProgress > 5f)
            {
                stallReported = true;
                events.Add("    STALL " + DescribeClimber(cliff));
            }
            if (climber.LastEvent != lastSeen)
            {
                lastSeen = climber.LastEvent;
                events.Add($"    {Time.time - start,5:0.0}s  {lastSeen}  (feet {feet - cliff.topY:+0.0;-0.0} vs top)");
            }
            // Pictures from the player's eyes: a few reaches early on, the whole pull-up, and standing after it.
            if (capture && framesPath != null && Time.time >= nextCapture)
            {
                float since = Time.time - start;
                bool reachWindow = since > 3f && since < 4.2f;
                bool topWindow = climber.IsMantling || (!active && started && since < timeout);
                if (reachWindow || topWindow)
                {
                    Capture(cliff.name, climber.IsMantling ? "pullup" : active ? "climb" : "after");
                    nextCapture = Time.time + 0.12f;
                }
            }
            bool onTop = !active && controller.Grounded && feet > cliff.topY - 1f;
            if (onTop)
            {
                if (onTopSince < 0f) onTopSince = Time.time;
                if (Time.time - onTopSince > 0.6f)
                {
                    success = true;
                    break;
                }
            }
            else onTopSince = -1f;
            if (!started && Time.time - start > 4f) break;
            yield return null;
        }
        climber.ScriptedInput = Vector2.zero;
        if (success) passed++;
        float height = cliff.topY - cliff.feet.y;
        Log($"{(success ? "PASS" : "FAIL")} {cliff.name}  wall {height:0.0} m  {(fast ? "fast" : "normal")}  " +
            $"{(started ? $"{Time.time - start:0.0}s, {climbs} grab(s), best feet {maxFeet - cliff.topY:+0.0;-0.0} vs top" : "never started climbing")}");
        foreach (string e in events) Log(e);
        yield return new WaitForSeconds(0.3f);
    }

    private string DescribeClimber(Cliff cliff)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Vector3 normal = (Vector3)typeof(HandClimber).GetField("wallNormal", flags).GetValue(climber);
        System.Array hands = (System.Array)typeof(HandClimber).GetField("hands", flags).GetValue(climber);
        float stuck = (float)typeof(HandClimber).GetField("stuckTimer", flags).GetValue(climber);
        var text = new StringBuilder();
        text.Append($"feet {body.position.y - halfHeight - cliff.topY:+0.0;-0.0} vs top, wall normal {normal.ToString("0.00")} ({Vector3.Angle(normal, Vector3.up):0} deg), stuck {stuck:0.0}s, mantling {climber.IsMantling}");
        for (int i = 0; i < hands.Length; i++)
        {
            object hand = hands.GetValue(i);
            System.Type type = hand.GetType();
            Vector3 hold = (Vector3)type.GetField("hold").GetValue(hand);
            Vector3 handNormal = (Vector3)type.GetField("normal").GetValue(hand);
            bool moving = (bool)type.GetField("moving").GetValue(hand);
            text.Append($" | hand{i} {(hold - body.position).ToString("0.00")} from body, normal y {handNormal.y:0.00}{(moving ? " moving" : "")}");
        }
        // What is above the hands?
        Vector3 inward = -Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
        Vector3 anchor = body.position + Vector3.up * 0.6f;
        for (float h = 0.5f; h <= 2.01f; h += 0.5f)
        {
            bool hit = Physics.Raycast(anchor + Vector3.up * h - inward * 0.6f, inward, out RaycastHit ahead, 2.5f, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore);
            text.Append(hit ? $" | +{h:0.0}m: wall at {ahead.distance - 0.6f:0.00} angle {Vector3.Angle(ahead.normal, Vector3.up):0}" : $" | +{h:0.0}m: open");
        }
        return text.ToString();
    }

    private Camera captureCamera;

    /// <summary>Saves what the player sees, and a view of the player from the side.</summary>
    private void Capture(string climbName, string phase)
    {
        Camera main = Camera.main;
        if (main == null) return;
        if (captureCamera == null)
        {
            captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>();
            captureCamera.enabled = false;
        }
        captureCamera.CopyFrom(main);
        captureCamera.enabled = false;
        captureCamera.targetTexture = null;
        string folder = Path.Combine(framesPath, climbName.Replace(' ', '_').Replace('@', '_').Replace('(', '_').Replace(')', '_').Replace(',', '_'));
        Directory.CreateDirectory(folder);

        if (frameIndex % 4 == 0)
            Log($"    camera {main.name} at {main.transform.position} (body {body.position}), {DescribeHands(main)}");
        captureCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        Save(captureCamera, Path.Combine(folder, $"{frameIndex:000}_{phase}_eye.png"));

        Vector3 forward = Vector3.ProjectOnPlane(controller.Orientation != null ? controller.Orientation.forward : Vector3.forward, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        Vector3 from = body.position + side * 3.2f - forward * 1.6f + Vector3.up * 0.8f;
        captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(body.position + Vector3.up * 0.4f - from));
        captureCamera.fieldOfView = 60f;
        captureCamera.nearClipPlane = 0.05f;
        Save(captureCamera, Path.Combine(folder, $"{frameIndex:000}_{phase}_side.png"));
        frameIndex++;
    }

    private string DescribeHands(Camera cam)
    {
        var text = new StringBuilder();
        for (int i = 0; i < 2; i++)
        {
            if (!climber.GetHand(i, out Vector3 hold, out _, out bool gripping)) { text.Append($"hand{i} free; "); continue; }
            Vector3 view = cam.WorldToViewportPoint(hold);
            text.Append($"hand{i} at screen ({view.x:0.00},{view.y:0.00}) {view.z:0.00} m ahead{(gripping ? "" : " moving")}; ");
        }
        var limbs = GameObject.Find("First Person Body");
        if (limbs != null)
        {
            int visible = 0, total = 0;
            foreach (Renderer part in limbs.GetComponentsInChildren<Renderer>(true))
            {
                total++;
                if (part.enabled && part.gameObject.activeInHierarchy) visible++;
            }
            text.Append($"limb renderers {visible}/{total} on, layer {limbs.transform.GetChild(0).gameObject.layer}");
            foreach (Transform arm in limbs.transform)
            {
                Transform hand = arm.Find("Hand");
                if (hand == null) continue;
                Vector3 view = cam.WorldToViewportPoint(hand.position);
                text.Append($"; {arm.name} hand mesh at screen ({view.x:0.00},{view.y:0.00}) {view.z:0.00} m");
            }
            var fpb = FindFirstObjectByType<FirstPersonBody>();
            if (fpb != null)
            {
                var fpbCamera = typeof(FirstPersonBody).GetField("viewCamera", Private)?.GetValue(fpb) as Camera;
                var climbCamera = typeof(HandClimber).GetField("viewCamera", Private)?.GetValue(climber) as Camera;
                climber.GetClimbFrame(out Vector3 eye, out _, out _, out _);
                text.Append($"; body camera {(fpbCamera != null ? fpbCamera.name + " at " + fpbCamera.transform.position : "none")}, " +
                            $"climber camera {(climbCamera != null ? climbCamera.name + " at " + climbCamera.transform.position : "none")}, eye {eye}, " +
                            $"climbFrame {typeof(FirstPersonBody).GetField("climbFrame", Private)?.GetValue(fpb)}");
                object leftArm = typeof(FirstPersonBody).GetField("leftArm", Private)?.GetValue(fpb);
                if (leftArm != null)
                {
                    System.Type limbType = leftArm.GetType();
                    Vector3 current = (Vector3)limbType.GetField("current").GetValue(leftArm);
                    Transform upper = (Transform)limbType.GetField("upper").GetValue(leftArm);
                    Transform end = (Transform)limbType.GetField("end").GetValue(leftArm);
                    text.Append($"; left arm target {current - cam.transform.position} from camera, upper {upper.position - cam.transform.position}, end {end.position - cam.transform.position} ({end.name}), root {fpb.transform.position}");
                }
            }
        }
        else text.Append("no First Person Body");
        return text.ToString();
    }

    private static void Save(Camera cam, string path)
    {
        const int width = 640, height = 360;
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
        Object.Destroy(picture);
    }

    private void Teleport(Vector3 position, Vector3 inward)
    {
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.position = position;
        body.transform.position = position;
        float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
        typeof(SmoothFirstPersonController).GetField("yRotation", Private)?.SetValue(controller, yaw);
        typeof(SmoothFirstPersonController).GetField("xRotation", Private)?.SetValue(controller, 0f);
        if (controller.Orientation != null) controller.Orientation.rotation = Quaternion.Euler(0f, yaw, 0f);
        var holder = typeof(SmoothFirstPersonController).GetField("cameraHolder", Private)?.GetValue(controller) as Transform;
        if (holder != null) holder.rotation = Quaternion.Euler(0f, yaw, 0f);
        Physics.SyncTransforms();
    }

    // ---------------- Finding test walls ----------------

    private List<Cliff> FindCliffs(int wanted, float minRise, float maxRise, int cells, float minStep)
    {
        const float step = 3f;
        const int n = 86;
        Vector3 origin = world.transform.position + new Vector3(-n * step * 0.5f, 0f, -n * step * 0.5f);
        var heights = new float[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            Vector3 p = origin + new Vector3(i * step, 0f, j * step);
            heights[i, j] = TopHeight(p, out _);
        }

        var found = new List<Cliff>();
        int stageRise = 0, stageHit = 0, stageSteep = 0, stageGround = 0;
        var random = new System.Random(5);
        var order = new List<(int, int)>();
        for (int i = 2; i < n - 2; i++)
        for (int j = 2; j < n - 2; j++)
            order.Add((i, j));
        for (int k = order.Count - 1; k > 0; k--)
        {
            int r = random.Next(k + 1);
            (order[k], order[r]) = (order[r], order[k]);
        }

        foreach ((int i, int j) in order)
        {
            if (found.Count >= wanted) break;
            // Stay away from the rim cliffs at the edge of the world (falling past them respawns the player).
            if (Mathf.Abs(i - n * 0.5f) * step > 105f || Mathf.Abs(j - n * 0.5f) * step > 105f) continue;
            float h = heights[i, j];
            if (float.IsNaN(h)) continue;
            for (int d = 0; d < 8; d++)
            {
                int di, dj;
                switch (d)
                {
                    case 0: di = 1; dj = 0; break;
                    case 1: di = -1; dj = 0; break;
                    case 2: di = 0; dj = 1; break;
                    case 3: di = 0; dj = -1; break;
                    case 4: di = 1; dj = 1; break;
                    case 5: di = -1; dj = -1; break;
                    case 6: di = 1; dj = -1; break;
                    default: di = -1; dj = 1; break;
                }
                float far = heights[i + di * cells, j + dj * cells];
                if (float.IsNaN(far) || far - h < minStep) continue;
                stageRise++;

                Vector3 direction = new Vector3(di, 0f, dj).normalized;
                Vector3 start = origin + new Vector3(i * step, h + 1.2f, j * step);
                if (!Physics.Raycast(start, direction, out RaycastHit wall, 6f, 1 << 7, QueryTriggerInteraction.Ignore)) continue;
                if (!wall.collider.name.StartsWith("World Chunk")) continue;
                stageHit++;
                if (Vector3.Angle(wall.normal, Vector3.up) < 55f || Vector3.Dot(wall.normal, -direction) < 0.3f) continue;
                stageSteep++;

                Vector3 inward = -Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
                Vector3 hang = wall.point - inward * 0.55f;
                float ground = TopHeight(hang, out _, hang.y);
                if (float.IsNaN(ground)) continue;
                stageGround++;
                // The top: the first ground flat enough to stand on, going in from the wall.
                float top = float.NaN;
                for (float back = 0.5f; back <= 6f; back += 0.5f)
                {
                    float y = TopHeight(wall.point + inward * back, out Vector3 topNormal);
                    if (float.IsNaN(y) || y < wall.point.y || topNormal.y < 0.75f) continue;
                    top = y;
                    break;
                }
                if (float.IsNaN(top) || top - wall.point.y < minRise || top - wall.point.y > maxRise) continue;

                bool crowded = false;
                foreach (Cliff other in found) crowded |= Vector3.Distance(other.start, hang) < 20f;
                if (crowded) continue;
                found.Add(new Cliff { start = hang, feet = new Vector3(hang.x, ground, hang.z), inward = inward, topY = top, name = $"cliff@({hang.x:0},{hang.z:0})" });
                break;
            }
        }
        Log($"Cliff search: {stageRise} rises, {stageHit} wall hits, {stageSteep} steep, {stageGround} with ground below.");
        return found;
    }

    /// <summary>Height of the highest terrain surface at a column, or of the first one below fromY.</summary>
    private float TopHeight(Vector3 column, out Vector3 normal, float fromY = 400f)
    {
        normal = Vector3.up;
        Vector3 from = new Vector3(column.x, fromY, column.z);
        foreach (RaycastHit hit in SortedHits(from, fromY + 400f))
        {
            if (!hit.collider.name.StartsWith("World Chunk")) continue;
            normal = hit.normal;
            return hit.point.y;
        }
        return float.NaN;
    }

    private static RaycastHit[] SortedHits(Vector3 from, float distance)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, distance, 1 << 7, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    private bool FindSpire(out Cliff cliff, float startAngle)
    {
        cliff = default;
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (spire == null) return false;
        Transform tower = null;
        foreach (MeshCollider collider in spire.GetComponentsInChildren<MeshCollider>())
            if (collider.name == "Spire") tower = collider.transform;
        if (tower == null) return false;

        Vector3 root = tower.parent.position;
        Bounds bounds = tower.GetComponent<MeshCollider>().bounds;
        for (int a = 0; a < 12; a++)
        {
            float angle = startAngle + a * 30f + 15f;
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            float ground = TopHeight(root + outward * 25f, out _);
            if (float.IsNaN(ground)) continue;
            Vector3 from = root + outward * 40f;
            from.y = ground + 6f;
            if (!Physics.Raycast(from, -outward, out RaycastHit wall, 40f, 1 << 7, QueryTriggerInteraction.Ignore)) continue;
            if (wall.collider.transform != tower) continue;
            Vector3 inward = -Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
            Vector3 hang = wall.point - inward * 0.55f;
            cliff = new Cliff
            {
                start = hang,
                feet = new Vector3(hang.x, ground, hang.z),
                inward = inward,
                topY = bounds.max.y - 9f,
                name = $"Hanging Spire {startAngle:0}",
            };
            return true;
        }
        return false;
    }
}
#endif
