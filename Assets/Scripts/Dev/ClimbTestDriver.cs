#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private PlayerGooCoating testGoo;
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
        LandmarkProgress.ResetAll(); // start with nothing collected (the real save is put back at the end)
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
        testGoo = controller.GetComponent<PlayerGooCoating>();
        if (testGoo == null) testGoo = controller.gameObject.AddComponent<PlayerGooCoating>();
        testGoo.TryApplyGoo();
        // Fixture supply supports long geometry regressions; finite supply is tested by GooCreatureValidation.
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
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestIcons") >= 0)
        {
            yield return CheckMouseIcon();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestTentacle") >= 0)
        {
            yield return CheckTentacleHolds();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestFlower") >= 0)
        {
            // A carried bubble flower still shoots: left click (the interactor) fires along the view; the thrower leaves it alone.
            foreach (BubbleFlower f in FindObjectsByType<BubbleFlower>(FindObjectsSortMode.None))
            {
                float under = Physics.Raycast(f.transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit g, 30f, ~0, QueryTriggerInteraction.Ignore) ? f.transform.position.y - g.point.y : float.NaN;
                Log($"Flower '{f.name}' at {f.transform.position}, {under:0.00} m above what is under it, awake {f.SimulationEnabled}");
            }
            Log($"Crosshair with empty hands: {FindFirstObjectByType<CrosshairHUD>()?.CurrentMode}");
            BubbleFlower flower = FindFirstObjectByType<BubbleFlower>();
            var flowerCarrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            var interactor = FindFirstObjectByType<PlayerCreatureInteractor>();
            Log($"Flower: {flower != null}, interactor {interactor != null}");
            if (flower != null && interactor != null)
            {
                Log($"    flower found at {flower.transform.position} ({flower.name}, parent {(flower.transform.parent != null ? flower.transform.parent.name : "-")}), player at {body.position}");
                // Bring it to the player's feet, as if walked up to.
                Vector3 feet = body.position + controller.Orientation.forward * 1.2f + Vector3.down * 0.9f;
                flower.transform.position = feet;
                var flowerBody = flower.GetComponent<Rigidbody>();
                if (flowerBody != null) { flowerBody.position = feet; flowerBody.linearVelocity = Vector3.zero; }
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.5f);
                typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(flowerCarrier, new object[] { flower });
                yield return new WaitForSeconds(0.5f);
                Log($"    carried {flowerCarrier.CarriedCreature == flower}, throwable {CreatureThrower.Instance?.HoldingThrowable}, crosshair {FindFirstObjectByType<CrosshairHUD>()?.CurrentMode}");
                for (int shot = 0; shot < 3; shot++)
                {
                    TrappingBubble before = flower.LastShot;
                    interactor.TryInteract();
                    if (flower.LastShot != null && flower.LastShot != before)
                    {
                        var rb = flower.LastShot.GetComponent<Rigidbody>();
                        for (int f = 0; f < 6; f++)
                        {
                            Log($"      frame {f}: bubble at {flower.LastShot.transform.position}, speed {(rb != null ? rb.linearVelocity.magnitude : -1f):0.0}, kinematic {rb?.isKinematic}, player at {body.position}");
                            yield return new WaitForFixedUpdate();
                        }
                    }
                    yield return new WaitForSeconds(0.6f);
                    TrappingBubble made = flower.LastShot;
                    Vector3 lookFlat = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                    Log($"    shot {shot + 1}: new bubble {(made != null && made != before)}, {(made != null ? Vector3.Distance(made.transform.position, body.position).ToString("0.0") + " m out, " + Vector3.Dot(made.transform.position - body.position, lookFlat).ToString("0.0") + " m ahead" : "-")}");
                }
            }
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestClip") >= 0)
        {
            yield return CheckTowerClipping();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestGroves") >= 0)
        {
            GameObject groves = GameObject.Find("06b Glowing groves in the flower field");
            Log($"Field groves: {(groves != null ? groves.transform.childCount : 0)}");
            if (groves != null && framesPath != null)
                for (int i = 0; i < Mathf.Min(3, groves.transform.childCount); i++)
                {
                    var r = groves.transform.GetChild(i).GetComponentInChildren<Renderer>();
                    if (r == null) continue;
                    Vector3 c = r.bounds.center;
                    Shot(c + new Vector3(9f, 3f, -9f), c, $"field_grove_{i}.png");
                }
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestCliff") >= 0)
        {
            yield return CheckCliffEcosystem();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestSky") >= 0)
        {
            yield return CheckSkyIslands();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestHold") >= 0)
        {
            yield return CheckHolding();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestBellows2") >= 0)
        {
            yield return CheckBellowsTwo();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestBellows") >= 0)
        {
            yield return CheckBellows();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestScar") >= 0)
        {
            yield return CheckScar();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestStuck") >= 0)
        {
            yield return CheckStuckSpots();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestBones") >= 0)
        {
            yield return CheckBoneRiding();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestRoute") >= 0)
        {
            yield return CheckTentacleRoute();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestPerf") >= 0)
        {
            yield return CheckPerformance();
            Quit();
            yield break;
        }
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestKilnOnly") >= 0)
        {
            yield return CheckKiln();
            Quit();
            yield break;
        }

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
        {
            // Faces 0 and 1 with the tentacle creature on the back, 2 and 3 bare-handed (the glazed band should stop those).
            if (face == 2)
            {
                var carrierNow = FindFirstObjectByType<PlayerCreatureCarrier>();
                if (carrierNow != null) carrierNow.Release();
                yield return new WaitForSeconds(0.5f);
                Log($"    released the creature: sticky hands {climber.StickyHands}");
            }
            if (FindSpire(out spire, face * 90f)) yield return Climb(spire, fast: face % 2 == 1, timeout: face >= 2 ? 90f : 200f, fromGround: false, capture: face == 0);
            else Log("Spire: not found.");
        }

        // The staged climb, bare-handed: the first tower from the ground, then each next stage from the landing
        // its bridge arrives at.
        var spireForShot = FindFirstObjectByType<HangingSpire>();
        if (FindSpire(out spire, 0f, "Stage Tower 1")) yield return Climb(spire, fast: false, timeout: 80f, fromGround: false, capture: true);
        else Log("Stage Tower 1: not found.");
        if (spireForShot != null)
            for (int l = 0; l < spireForShot.Landings.Count; l++)
            {
                var landing = spireForShot.Landings[l];
                Vector3 standAt = landing.top + Vector3.up * (halfHeight + 0.1f) - landing.inward * 0.3f;
                // Walk up to the wall, as a player would.
                if (Physics.Raycast(standAt, landing.inward, out RaycastHit wallAhead, 6f, 1 << 7, QueryTriggerInteraction.Ignore)) standAt = wallAhead.point - landing.inward * 0.6f;
                bool last = l == spireForShot.Landings.Count - 1;
                var cliff = new Cliff { start = standAt, feet = landing.top, inward = landing.inward, topY = last ? (spireForShot.CrownSeat(out Vector3 crownTop, out _) ? crownTop.y - 0.5f : spireForShot.GlazeRing.x + 1f) : spireForShot.StageTowers[1].GetComponent<MeshCollider>().bounds.max.y - 3.5f, name = last ? "From landing to the crown" : "Stage Tower 2 (from its landing)" };
                yield return Climb(cliff, fast: false, timeout: 80f, fromGround: true);
            }
        if (framesPath != null && spireForShot != null && spireForShot.StageTowers.Count == 2)
        {
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Vector3 mid = (spireForShot.StageTowers[0].position + spireForShot.StageTowers[1].position) * 0.5f;
            Vector3 spireBase = spireForShot.transform.GetChild(0).position;
            Vector3 away = (mid - spireBase); away.y = 0f;
            Vector3 from = mid + away.normalized * 55f + Vector3.up * 40f;
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(spireBase + Vector3.up * 30f - from));
            captureCamera.fieldOfView = 70f;
            Save(captureCamera, Path.Combine(framesPath, "stages.png"));
        }
        yield return CheckFuzzRope();
        yield return CheckGlazeRoutes();
        Log($"RESULT {passed}/{total} climbs reached the top.");
        Quit();
    }

    /// <summary>
    /// Climbs the first tower from several sides, bare-handed and with a tentacle creature, watching how far the
    /// player's body ever sinks into a tower (it should never pass through).
    /// </summary>
    private IEnumerator CheckTowerClipping()
    {
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        // The chest and head only (legs brushing a flared foot or a rim while pulling up are fine).
        var probeObject = new GameObject("Test Chest Probe") { layer = 2 };
        var capsule = probeObject.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.radius = 0.3f;
        capsule.height = 1.2f;
        capsule.center = new Vector3(0f, 0.35f, 0f);
        Physics.SyncTransforms();
        {
            MeshCollider t1 = null;
            foreach (MeshCollider m in spire.GetComponentsInChildren<MeshCollider>()) if (m.name == "Stage Tower 1") t1 = m;
            Vector3 inWall = t1.ClosestPointOnBounds(t1.bounds.center + Vector3.right * 100f) ;
            Physics.Raycast(t1.bounds.center + Vector3.right * 30f + Vector3.up * 2f, Vector3.left, out RaycastHit wallHit, 60f, 1 << 7);
            bool hitTrigger = Physics.ComputePenetration(capsule, wallHit.point + Vector3.left * 0.2f, Quaternion.identity, t1, t1.transform.position, t1.transform.rotation, out _, out float d1);
            capsule.isTrigger = false;
            bool hitSolid = Physics.ComputePenetration(capsule, wallHit.point + Vector3.left * 0.2f, Quaternion.identity, t1, t1.transform.position, t1.transform.rotation, out _, out float d2);
            capsule.isTrigger = true;
            var playerCapsule = controller.GetComponent<CapsuleCollider>();
            bool hitPlayer = Physics.ComputePenetration(playerCapsule, wallHit.point + Vector3.left * 0.2f, Quaternion.identity, t1, t1.transform.position, t1.transform.rotation, out _, out float d3);
            bool overlap = Physics.CheckCapsule(wallHit.point + Vector3.left * 0.2f + Vector3.up * 0.05f, wallHit.point + Vector3.left * 0.2f + Vector3.up * 0.65f, 0.3f, 1 << 7);
            Log($"Probe check at the wall of {wallHit.collider?.name} (point {wallHit.point}, normal {wallHit.normal}): trigger probe {hitTrigger} {d1:0.00}, solid probe {hitSolid} {d2:0.00}, player capsule {hitPlayer} {d3:0.00}, overlap {overlap}");
        }
        TentacleCreature tentacle = null;
        foreach (TentacleCreature t in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None)) { tentacle = t; if (t.GetComponent<SpireCrawler>() == null) break; }
        HandClimber.SweepBody = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noSweep") < 0;
        Log($"Clip test, body sweep {HandClimber.SweepBody}");
        for (int run = 0; run < 2; run++)
        {
            bool sticky = run == 1;
            if (sticky && tentacle != null)
            {
                var crawl = tentacle.GetComponent<SpireCrawler>();
                if (crawl != null) Destroy(crawl);
                body.isKinematic = false;
                body.position = tentacle.transform.position + Vector3.up * 0.5f;
                Physics.SyncTransforms();
                yield return null;
                typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
                yield return new WaitForSeconds(0.5f);
            }
            foreach (string towerName in new[] { "Stage Tower 1", "Spire" })
            for (int face = 0; face < 4; face++)
            {
                if (!FindSpire(out Cliff cliff, face * 90f + 20f, towerName)) continue;
                MeshCollider tower = null;
                foreach (MeshCollider m in spire.GetComponentsInChildren<MeshCollider>()) if (m.name == towerName) tower = m;
                // Watch penetration while the climb runs.
                float deepest = 0f;
                Vector3 deepestAt = Vector3.zero;
                bool watching = true;
                IEnumerator Watch()
                {
                    while (watching)
                    {
                        // Inside: the chest point is in the wall (a ray out from the middle of the tower meets the wall's
                        // outside face beyond it, or no wall at all from inside).
                        Vector3 chest = body.position + Vector3.up * 0.4f;
                        Vector3 outward = chest - tower.bounds.center; outward.y = 0f;
                        outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                        Vector3 outside = chest + outward * 12f;
                        if (tower.Raycast(new Ray(outside, -outward), out RaycastHit face, 12f) && face.distance < 12f - 0.15f)
                        { deepest += Time.deltaTime; deepestAt = body.position; }
                        yield return null;
                    }
                }
                Coroutine watcher = StartCoroutine(Watch());
                cliff.name = $"{towerName} face {face} {(sticky ? "with tentacle" : "bare hands")}";
                yield return Climb(cliff, fast: false, timeout: sticky ? 60f : 25f, fromGround: false);
                watching = false;
                StopCoroutine(watcher);
                Log($"    chest inside rock for {deepest:0.00} s (last at height {deepestAt.y - tower.transform.position.y:0.0}); ended {(Physics.CheckSphere(body.position, 0.2f, 1 << 7, QueryTriggerInteraction.Ignore) ? "INSIDE" : "clear")}");
            }
        }
    }

    /// <summary>
    /// Stands the player on top of bone mother pieces and other non-ground colliders, holds W, and reports whether the
    /// body counts as grounded and how far it walks. Run once with the any-layer ground check on and once with it off.
    /// </summary>
    private IEnumerator CheckStuckSpots()
    {
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        var spots = new List<Collider>();
        if (kiln != null)
            foreach (Collider c in kiln.GetComponentsInChildren<Collider>())
                if (!c.isTrigger && c.enabled && c.bounds.size.x > 0.6f && c.bounds.size.z > 0.6f) spots.Add(c);
        Log($"Stuck check: {spots.Count} bone colliders on layers {string.Join(",", spots.ConvertAll(c => c.gameObject.layer.ToString()).ToArray().Distinct())}");
        FieldInfo anyLayer = typeof(SmoothFirstPersonController).GetField("groundOnAnyLayer", Private);
        int step = Mathf.Max(1, spots.Count / 8);
        foreach (bool on in new[] { false, true })
        {
            anyLayer.SetValue(controller, on);
            int walked = 0, tried = 0;
            for (int i = 0; i < spots.Count; i += step)
            {
                Collider c = spots[i];
                Vector3 top = c.bounds.center + Vector3.up * (c.bounds.extents.y + 3f);
                if (!c.Raycast(new Ray(top, Vector3.down), out RaycastHit hit, 20f)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > 40f) continue;
                tried++;
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.position = hit.point + Vector3.up * (halfHeight + 0.05f);
                controller.transform.position = body.position;
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.4f);
                Vector3 start = body.position;
                bool groundedBefore = controller.Grounded;
                controller.ScriptedMove = new Vector2(0f, 1f);
                int groundedFrames = 0, frames = 0;
                float t = 0f;
                while (t < 1.5f) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; frames++; if (controller.Grounded) groundedFrames++; }
                controller.ScriptedMove = Vector2.zero;
                Vector3 d = body.position - start;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > 1.5f) walked++;
                Log($"  any-layer {on}: '{c.name}' layer {c.gameObject.layer}, grounded at start {groundedBefore}, grounded {groundedFrames}/{frames} frames, walked {flat:0.0} m, height change {d.y:+0.0;-0.0}");
            }
            Log($"  any-layer {on}: walked off {walked} of {tried} spots");
        }
        anyLayer.SetValue(controller, true);
    }

    /// <summary>
    /// The bone children and their mother: warm a child with a fire creature in its ribs, climb on with a tentacle
    /// creature carried (reins), fly it where the view looks; then wake the mother, climb onto her skull and steer her.
    /// </summary>
    private IEnumerator CheckBoneRiding()
    {
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        float wait = Time.time;
        while ((kiln == null || kiln.Root == null) && Time.time - wait < 30f) { yield return null; kiln = FindFirstObjectByType<BoneKiln>(); }
        Log($"Bones: kiln {(kiln != null && kiln.Root != null)}, children {BoneChild.All.Count}");
        if (BoneChild.All.Count == 0) yield break;
        for (int i = 0; i < BoneChild.All.Count; i++)
        {
            BoneChild c = BoneChild.All[i];
            float under = Physics.Raycast(c.Chest + Vector3.up * 3f, Vector3.down, out RaycastHit floorHit, 20f, 1 << 7) ? floorHit.point.y : float.NaN;
            Log($"    child {i} at {c.Chest}, ground below {under:0.00}, {Vector3.Distance(c.Chest, kiln.Root.position):0.0} m from the mouth, renderers {c.GetComponentsInChildren<Renderer>().Length}");
            if (framesPath != null) ShotOf(c.Chest, $"child_{i}_lying.png");
        }
        // Habits: crawling round the mother, going after fire bubbles from the nests.
        {
            var starts = new List<Vector3>();
            foreach (BoneChild c in BoneChild.All) starts.Add(c.Chest);
            Log($"    nests {FindObjectsByType<KilnBubbleNest>(FindObjectsSortMode.None).Length}");
            float maxLively = 0f;
            int chases = 0;
            bool shot = false;
            for (float t = 0f; t < 40f; t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                foreach (BoneChild c in BoneChild.All)
                {
                    maxLively = Mathf.Max(maxLively, c.Lively);
                    if (c.Chasing != null) chases++;
                    if (!shot && framesPath != null && c.Lively > 0.5f) { ShotOf(c.Chest, "child_chasing.png"); shot = true; }
                }
            }
            int bubbles = 0;
            foreach (FireBubble b in FindObjectsByType<FireBubble>(FindObjectsSortMode.None)) if (b.name.Contains("kiln nest")) bubbles++;
            for (int i = 0; i < BoneChild.All.Count; i++)
                Log($"    child {i} moved {Vector3.Distance(starts[i], BoneChild.All[i].Chest):0.0} m in 40 s, resting {BoneChild.All[i].Resting}, lively {BoneChild.All[i].Lively:0.00}, warmth {BoneChild.All[i].Warmth:0.00}");
            Log($"    nest bubbles alive {bubbles}; chasing seen {chases} times; most lively {maxLively:0.00}");
            foreach (FireBubble b in FindObjectsByType<FireBubble>(FindObjectsSortMode.None))
                if (b.name.Contains("kiln nest"))
                    Log($"      bubble {Vector3.Distance(b.transform.position, kiln.Root.position):0.0} m from her mouth, {Vector3.Distance(b.transform.position, kiln.HeartPoint):0.0} m from her heart, {b.transform.position.y - kiln.Root.position.y:+0.0;-0.0} m up");
            Log($"    mother warmth {kiln.Warmth:0.00}, awake {kiln.Awake}");
            if (framesPath != null) ShotOf(kiln.Root.position + Vector3.up * 3f, "bubbles_at_mother.png");
        }
        // The one furthest from its mother (her ribs are not in the way of a throw there).
        BoneChild child = BoneChild.All[0];
        foreach (BoneChild c in BoneChild.All)
            if (c.Warmth < 0.1f && Vector3.Distance(c.Chest, kiln.Root.position) > Vector3.Distance(child.Chest, kiln.Root.position)) child = c;
        Vector3 lying = child.Head.position;
        Log($"    child lies at {lying}, warmth {child.Warmth:0.00}");

        // Fire in its ribs.
        Creature fire = FindFirstObjectByType<FireBubble>();
        if (fire == null) foreach (Creature c in FindObjectsByType<Creature>(FindObjectsSortMode.None)) if (c.Heat > 0.5f) { fire = c; break; }
        if (fire == null) { Log("    no warm creature in the scene"); yield break; }
        fire.SetHeat(0f); // Regression: the authored FireBubble has zero Heat and must still be caught.
        Log($"    fire creature {fire.GetType().Name} heat {fire.Heat:0.0}");
        // As a player would: carry it over and let go of it over the child's ribs.
        var fireCarrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        body.isKinematic = false;
        body.position = fire.transform.position + Vector3.up * 0.5f;
        Physics.SyncTransforms();
        yield return null;
        typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(fireCarrier, new object[] { fire });
        yield return new WaitForSeconds(0.3f);
        // Stand a few metres off, look at its chest and throw (up to three tries: it keeps crawling about).
        for (int attempt = 0; attempt < 3 && child.Cradled != fire; attempt++)
        {
            if (fireCarrier.CarriedCreature != fire)
            {
                body.position = fire.transform.position + Vector3.up * 0.5f;
                Physics.SyncTransforms();
                yield return null;
                typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(fireCarrier, new object[] { fire });
                yield return new WaitForSeconds(0.3f);
            }
            // From the side away from the mother.
            Vector3 side = Vector3.ProjectOnPlane(child.Chest - kiln.Root.position, Vector3.up).normalized;
            Vector3 stand = child.Chest + side * 5f;
            if (Physics.Raycast(stand + Vector3.up * 10f, Vector3.down, out RaycastHit standGround, 30f, 1 << 7)) stand = standGround.point + Vector3.up * (halfHeight + 0.05f);
            body.position = stand;
            body.transform.position = stand;
            Physics.SyncTransforms();
            yield return null;
            Vector3 eye = Camera.main.transform.position;
            Vector3 toChest = child.Chest - eye;
            controller.SetLook(Mathf.Atan2(toChest.x, toChest.z) * Mathf.Rad2Deg, -Mathf.Atan2(toChest.y, new Vector2(toChest.x, toChest.z).magnitude) * Mathf.Rad2Deg + 4f);
            Log($"    try {attempt + 1}: carrying the fire {fireCarrier.CarriedCreature == fire}, {toChest.magnitude:0.0} m from the chest; throwing");
            CreatureThrower.Instance.Throw();
            for (int k = 0; k < 8; k++)
            {
                yield return new WaitForSeconds(0.2f);
                if (framesPath != null && (k == 0 || k == 2 || k == 5)) ShotOf(child.Chest, $"catch_{attempt}_{k}.png");
                Log($"      {0.2f * (k + 1):0.0}s after the throw: fire {Vector3.Distance(fire.transform.position, child.Chest):0.0} m from the chest, caught {child.Cradled == fire}, hit {CreatureThrower.Instance.LastHit}");
            }
        }
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSeconds(0.25f);
            Log($"      {i * 0.25f + 0.25f:0.00}s: fire {(fire == null ? "gone" : $"{Vector3.Distance(fire.transform.position, child.Chest):0.0} m from the chest, active {fire.isActiveAndEnabled}, heat {fire.Heat:0}")}, warmth {child.Warmth:0.00}");
        }
        yield return new WaitForSeconds(4.5f);
        float ground = Physics.Raycast(child.Head.position + Vector3.up, Vector3.down, out RaycastHit g, 50f, 1 << 7) ? g.point.y : float.NaN;
        Log($"    warmed: warmth {child.Warmth:0.00}, fire heat {fire.Heat:0.0}, head {child.Head.position.y - lying.y:+0.0;-0.0} m from where it lay");
        if (framesPath != null) ShotOf(child.Head.position, "child_warm.png");

        // Carry a tentacle creature and climb on.
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        TentacleCreature tentacle = null;
        foreach (TentacleCreature t in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None)) { tentacle = t; if (t.GetComponent<SpireCrawler>() == null) break; }
        if (tentacle != null)
        {
            var crawl = tentacle.GetComponent<SpireCrawler>();
            if (crawl != null) Destroy(crawl);
            body.isKinematic = false;
            body.position = tentacle.transform.position + Vector3.up * 0.5f;
            Physics.SyncTransforms();
            yield return null;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
            yield return new WaitForSeconds(0.5f);
        }
        body.position = child.Chest + Vector3.up * 1.2f;
        Physics.SyncTransforms();
        yield return null;
        BoneRide.ScriptedToggle = true;
        yield return new WaitForSeconds(0.6f);
        Log($"    climbed on: ridden {child.Ridden}, reins {BoneRide.Reins(child.transform)}, carrying a tentacle {carrier.CarriesTentacle}");

        // Pull the right rein and climb, independently of the view.
        yield return Steer(child.Head, "child", 5f);
        if (framesPath != null) ShotOf(child.Head.position, "child_ridden.png");
        BoneRide.ScriptedToggle = true;
        yield return new WaitForSeconds(0.3f);
        Log($"    got off: ridden {child.Ridden}, player kinematic {body.isKinematic}");

        // The mother: wake her, climb onto her skull, steer.
        kiln.Wake();
        {
            // The launch: straight up through the ground, then level off and roam. Logged every second.
            float startY = kiln.Skull.position.y, peak = startY, launchTime = 0f;
            int shots = 0;
            while (kiln.Emerging && launchTime < 30f)
            {
                yield return new WaitForSeconds(1f);
                launchTime += 1f;
                peak = Mathf.Max(peak, kiln.Skull.position.y);
                Log($"      {launchTime:0}s: launching {kiln.Launching}, skull {kiln.Skull.position.y - kiln.MouthPoint.y:+0.0;-0.0} m over the mouth, cutscene {BoneAwakeningCutscene.Playing}, camera {(Camera.main != null ? Vector3.Distance(Camera.main.transform.position, kiln.MouthPoint).ToString("0") : "-")} m from the mouth");
                if (framesPath != null && (launchTime == 2f || launchTime == 4f || launchTime == 6f) && Camera.main != null)
                    Save(Camera.main, Path.Combine(framesPath, $"launch_{shots++}.png"));
            }
            Log($"    launch: done after {launchTime:0} s (emerging {kiln.Emerging}), skull rose {peak - startY:0.0} m, now {kiln.Skull.position.y - kiln.MouthPoint.y:+0.0;-0.0} m over the mouth");
            Vector3 before = kiln.Skull.position;
            yield return new WaitForSeconds(4f);
            Log($"    after the launch: moved {Vector3.Distance(before, kiln.Skull.position):0.0} m in 4 s, cutscene {BoneAwakeningCutscene.Playing}");
            FindFirstObjectByType<BoneAwakeningCutscene>()?.Finish();
        }
        if (tentacle != null && !carrier.CarriesTentacle)
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
        body.isKinematic = false;
        body.position = kiln.Skull.position + Vector3.up * 3f;
        Physics.SyncTransforms();
        yield return null;
        BoneRide.ScriptedToggle = true;
        yield return new WaitForSeconds(0.6f);
        Log($"    mother: awake {kiln.Awake}, ridden {kiln.Ridden}, reins {BoneRide.Reins(kiln.Root)}");
        yield return Steer(kiln.Skull, "mother", 6f);
        if (framesPath != null) ShotOf(kiln.Skull.position, "mother_ridden.png");
        BoneRide.ScriptedToggle = true;
        yield return new WaitForSeconds(0.3f);
        Log($"    got off the mother: ridden {kiln.Ridden}");
        // Throwing a fuzzy creature.
        {
            FuzzCreature fuzz = FindFirstObjectByType<FuzzCreature>();
            var thrower = CreatureThrower.Instance;
            var fuzzCarrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            fuzzCarrier.Release();
            if (fuzz != null && thrower != null)
            {
                body.isKinematic = false;
                body.position = fuzz.transform.position + Vector3.up * 1f;
                Physics.SyncTransforms();
                yield return null;
                typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(fuzzCarrier, new object[] { fuzz });
                yield return new WaitForSeconds(0.5f);
                controller.SetLook(controller.LookYaw, -10f);
                yield return null;
                Vector3 from = fuzz.transform.position;
                thrower.Throw();
                yield return new WaitForSeconds(3f);
                Log($"    threw a fuzzy creature {Vector3.Distance(from, fuzz.transform.position):0.0} m, hit {thrower.LastHit}, carried {fuzz.IsCarried}, moving on its own again {fuzz.SimulationEnabled}");
            }
        }
        BoneRide.ScriptedMove = null;
        BoneRide.ScriptedRise = 0f;
    }

    /// <summary>Strafes right and climbs, then measures the mount's movement.</summary>
    private IEnumerator Steer(Transform headPart, string label, float seconds)
    {
        Vector3 start = headPart.position;
        float yaw = controller.LookYaw + 70f;
        controller.SetLook(yaw, -10f);
        BoneRide.ScriptedMove = Vector2.right;
        BoneRide.ScriptedRise = 1f;
        yield return new WaitForSeconds(seconds);
        BoneRide.ScriptedMove = null;
        BoneRide.ScriptedRise = 0f;
        Vector3 moved = headPart.position - start;
        Vector3 rider = body.position - headPart.position;
        Log($"    moved the {label} right and up for {seconds:0} s: moved {moved.magnitude:0.0} m, rose {moved.y:+0.0;-0.0} m; rider {rider.magnitude:0.0} m from its head");
    }

    /// <summary>The bone mother's carved scar: how deep it is, what grew in it, and whether bubbles rise out of it.</summary>
    private IEnumerator CheckScar()
    {
        BoneScar scar = FindFirstObjectByType<BoneScar>();
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        Log($"Scar: {(scar != null)}, carved {scar != null && scar.Carved}, kiln built {kiln != null && kiln.Root != null}");
        if (scar == null || !scar.Carved || kiln == null) yield break;
        float waited = 0f;
        while (scar.Dressing == null && waited < 40f) { waited += 0.5f; yield return new WaitForSeconds(0.5f); }
        Log($"    dressed after {waited:0.0} s: {(scar.Dressing != null)}, growths {scar.GrowthsPlaced}, mouth {kiln.Root.position}");
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if ((r.bounds.center - kiln.Root.position).magnitude > 60f) continue;
            foreach (Material m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name.Contains("InternalError") || !m.shader.isSupported)
                    Log($"      broken material on '{r.name}' (parent {(r.transform.parent != null ? r.transform.parent.name : "-")}): {(m == null ? "null" : m.name + " / " + (m.shader != null ? m.shader.name : "no shader"))}");
        }
        int n = scar.Line.Count;
        for (int k = 0; k <= 8; k++)
        {
            int i = Mathf.Clamp(k * (n - 1) / 8, 0, n - 1);
            Vector3 p = scar.Line[i];
            float floor = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 40f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
            Log($"    point {i}: lip {p.y:0.0}, floor {floor:0.0} ({p.y - floor:0.0} m deep)");
        }
        yield return new WaitForSeconds(1f);
        var vents = new List<KilnBubbleNest>();
        int surface = 0;
        foreach (KilnBubbleNest nest in FindObjectsByType<KilnBubbleNest>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (nest.name == "Crack Vent") vents.Add(nest);
            else if (nest.gameObject.activeInHierarchy && !nest.name.Contains("Heart")) surface++;
        }
        foreach (KilnBubbleNest v in vents)
        {
            Log($"    vent at {v.transform.position}, placed {v.Placed}, {scar.Distance(v.transform.position, out float w):0.0} m from the line (half width {w:0.0})");
            if (v.Placed) continue;
            Vector3 at = v.transform.position;
            var heights = new List<string>();
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 point = at + (sample == 0 ? Vector3.zero : Quaternion.Euler(0f, sample * 90f, 0f) * Vector3.forward * 1.1f);
                world.ColumnHeights(point, out float surfaceY, out _, out _);
                string h = "none";
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, surfaceY + 30f, point.z), Vector3.down, 45f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
                    if (world.OwnsCollider(hit.collider)) h = (h == "none" ? "" : h + "/") + $"{hit.point.y:0.0}(n{hit.normal.y:0.00})";
                heights.Add($"col {surfaceY:0.0}: {h}");
            }
            Log("      " + string.Join("; ", heights));
            foreach (Collider c in Physics.OverlapCapsule(at + Vector3.up, at + Vector3.up * 1.8f, 0.95f, ~0, QueryTriggerInteraction.Ignore))
                Log($"      in the way: {c.name} ({c.transform.root.name})");
        }
        Log($"    surface nests still on: {surface}");
        for (int t = 0; t < 4; t++)
        {
            yield return new WaitForSeconds(8f);
            int count = 0, risen = 0;
            foreach (FireBubble b in FindObjectsByType<FireBubble>(FindObjectsSortMode.None))
            {
                if (!b.name.Contains("crack vent")) continue;
                count++;
                if (t == 1 || t == 3) { var brb = b.GetComponent<Rigidbody>(); Log($"      bubble y {b.transform.position.y:0.0}, kinematic {brb?.isKinematic}, v {brb?.linearVelocity}, carried {b.IsCarried}, leashed {b.IsLeashed}, bone {b.IsBoundToBone}, cradled {BoneChild.IsCradled(b)}, sim {b.SimulationEnabled}"); }
                if (b.transform.position.y > kiln.Root.position.y) risen++;
            }
            Log($"    after {(t + 1) * 8} s: {count} vent bubbles, {risen} above the rim (scar counts {scar.VentBubbles} free, {scar.VentBubblesOut} out of the crack, {scar.VentBubblesReleased} released so far; vent bubbles above the lips now: {string.Join(" ", System.Array.ConvertAll(FindObjectsByType<FireBubble>(FindObjectsSortMode.None), f => f.name.Contains("crack vent") && !f.GetComponent<Rigidbody>().isKinematic ? (f.transform.position - kiln.Root.position).ToString("0") : ""))})");
        }
        if (framesPath != null)
        {
            Vector3 mid = scar.Line[n / 2];
            Vector3 across = Vector3.Cross(Vector3.up, (scar.Line[n - 1] - scar.Line[0]).normalized);
            Shot(mid + across * 30f + Vector3.up * 18f, mid + Vector3.down * 4f, "scar_overview.png");
            Vector3 q = scar.Line[n / 4];
            Shot(q + across * 6f + Vector3.up * 4f, q + Vector3.down * 6f, "scar_inside.png");
            Shot(q + Vector3.down * 2f + (scar.Line[n / 2] - q).normalized * -6f, scar.Line[n / 2] + Vector3.down * 5f, "scar_along.png");
        }
        // Standing on the floor of the crack and walking out.
        Vector3 floorAt = scar.Line[n / 3];
        if (Physics.Raycast(floorAt + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 40f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
        {
            Teleport(ground.point + Vector3.up * (halfHeight + 0.1f), (scar.Line[n - 1] - scar.Line[0]));
            yield return new WaitForSeconds(0.5f);
            Vector3 start = body.position;
            controller.ScriptedMove = new Vector2(0f, 1f);
            yield return new WaitForSeconds(3f);
            controller.ScriptedMove = Vector2.zero;
            Log($"    on the crack floor: grounded {controller.Grounded}, walked {Vector3.Distance(start, body.position):0.0} m, rose {body.position.y - start.y:+0.0;-0.0}");
        }
    }

    /// <summary>The Bellows: drinks heat, swells, pushes a box with its stream, floats when over-full, puffs when carried.</summary>
    private IEnumerator CheckBellows()
    {
        Vector3 flat = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
        Vector3 at = body.position + flat * 4f;
        if (Physics.Raycast(at + Vector3.up * 10f, Vector3.down, out RaycastHit ground, 40f, 1 << 7, QueryTriggerInteraction.Ignore)) at = ground.point;
        var go = new GameObject("Test Bellows");
        go.transform.SetPositionAndRotation(at + Vector3.up * 0.6f, Quaternion.LookRotation(flat));
        go.AddComponent<Rigidbody>();
        go.AddComponent<SphereCollider>();
        var bellows = go.AddComponent<BellowsCreature>();
        yield return new WaitForSeconds(1f);
        Log($"Bellows: radius {bellows.Radius:0.00}, charge {bellows.Charge:0.00}, at {go.transform.position}");

        // A fire bubble against it gives up its heat.
        FireFlower flowerSource = FindFirstObjectByType<FireFlower>();
        FireBubble prefab = flowerSource != null ? flowerSource.BubblePrefab : null;
        if (prefab != null)
        {
            FireBubble fire = Instantiate(prefab, go.transform.position + Vector3.up * 0.9f, Quaternion.identity);
            fire.gameObject.SetActive(true);
            fire.SetHeat(30f);
            float before = bellows.Charge;
            for (int k = 0; k < 6; k++)
            {
                if (fire == null) break;
                fire.transform.position = go.transform.position + Vector3.up * (bellows.Radius + 0.3f);
                var rb = fire.GetComponent<Rigidbody>(); if (rb != null) { rb.position = fire.transform.position; rb.linearVelocity = Vector3.zero; }
                yield return new WaitForSeconds(0.5f);
            }
            Log($"    fire bubble beside it for 3 s: charge {before:0.00} -> {bellows.Charge:0.00}, bubble heat {(fire != null ? fire.Heat : 0f):0.0}, radius {bellows.Radius:0.00}");
            if (fire != null) Destroy(fire.gameObject);
        }
        bellows.SetHeat(30f);
        yield return new WaitForSeconds(1.5f);
        if (framesPath != null) Shot(go.transform.position + go.transform.right * 3f + Vector3.up * 1.2f - flat * 1f, go.transform.position, "bellows_full.png");

        // A box in front of the spout.
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Test box";
        box.transform.position = bellows.Spout + flat * 2.5f;
        var boxBody = box.AddComponent<Rigidbody>();
        boxBody.mass = 2f;
        boxBody.useGravity = false; // floating in front of the spout: the ground here holds a resting box fast
        box.transform.localScale = Vector3.one * 0.6f;
        yield return new WaitForSeconds(0.8f);
        Log($"    box resting at {box.transform.position}: in front of it {(Physics.Raycast(box.transform.position, flat, out RaycastHit wall, 3f, ~0, QueryTriggerInteraction.Ignore) ? wall.collider.name + " at " + wall.distance.ToString("0.0") + " m" : "nothing")}");
        Vector3 boxStart = box.transform.position;
        float charge0 = bellows.Charge;
        bellows.ReceiveInteraction(new CreatureInteraction(controller.gameObject, bellows.transform.position, flat));
        float strongest = 0f;
        bool shot = false;
        for (float t = 0f; t < 4f; t += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            strongest = Mathf.Max(strongest, bellows.LastPush);
            if (Mathf.Abs(t % 0.5f) < Time.fixedDeltaTime) Log($"      t {t:0.0}: box at {box.transform.position}, speed {boxBody.linearVelocity}, sleeping {boxBody.IsSleeping()}, spout {bellows.Spout}, push {bellows.LastPush:0.0} on {bellows.LastPushed}, venting {bellows.Venting}");
            if (!shot && t > 0.4f && framesPath != null) { Shot(go.transform.position + go.transform.right * 4f + Vector3.up * 1.5f + flat * 2f, bellows.Spout + flat * 2f, "bellows_venting.png"); shot = true; }
        }
        Log($"    squeezed: charge {charge0:0.00} -> {bellows.Charge:0.00}, still venting {bellows.Venting}, box pushed {Vector3.Dot(box.transform.position - boxStart, flat):0.0} m along the spout, strongest push {strongest:0.0}");

        // A light bump and a hard bump.
        foreach (float speed in new[] { 2.5f, 8f })
        {
            bellows.SetHeat(30f);
            yield return new WaitForSeconds(0.5f);
            box.transform.position = bellows.Spout + flat * 2.5f;
            boxBody.position = box.transform.position; boxBody.linearVelocity = Vector3.zero;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.transform.localScale = Vector3.one * 0.5f;
            ball.transform.position = go.transform.position - flat * 2.5f + Vector3.up * 0.1f;
            var ballBody = ball.AddComponent<Rigidbody>();
            ballBody.useGravity = false;
            ballBody.linearVelocity = flat * speed;
            Vector3 b0 = box.transform.position;
            float c0 = bellows.Charge;
            yield return new WaitForSeconds(1.5f);
            Log($"    bumped at {speed:0.0} m/s: venting {bellows.Venting}, charge {c0:0.00} -> {bellows.Charge:0.00} in 1.5 s, box moved {Vector3.Dot(box.transform.position - b0, flat):0.0} m");
            Destroy(ball);
            yield return new WaitForSeconds(2f);
        }

        // Over-full: it floats.
        bellows.SetHeat(60f);
        float y0 = go.transform.position.y;
        yield return new WaitForSeconds(5f);
        bellows.SetHeat(60f);
        Log($"    over-full (charge 2): floating {bellows.Floating}, rose {go.transform.position.y - y0:0.0} m in 5 s");
        if (framesPath != null) Shot(go.transform.position + go.transform.right * 5f + Vector3.down * 0.5f - flat * 2f, go.transform.position, "bellows_floating.png");
        Vector3 floatStart = go.transform.position;
        bellows.ReceiveInteraction(new CreatureInteraction(controller.gameObject, bellows.transform.position, flat));
        yield return new WaitForSeconds(2f);
        Log($"    venting while floating: drove itself {Vector3.Dot(go.transform.position - floatStart, -flat):0.0} m backwards");

        // Carried: a puff along the view pushes the player back.
        bellows.SetHeat(30f);
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        body.isKinematic = false;
        Teleport(go.transform.position - flat * 1.5f + Vector3.up * 0.5f, flat);
        yield return new WaitForSeconds(0.6f);
        typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { bellows });
        yield return new WaitForSeconds(0.8f);
        Vector3 p0 = body.position;
        for (int k = 0; k < 3; k++)
        {
            bellows.ReceiveInteraction(new CreatureInteraction(controller.gameObject, bellows.transform.position, Camera.main.transform.forward));
            yield return new WaitForSeconds(0.7f);
        }
        Log($"    carried {bellows.IsCarried}: three puffs moved the player {Vector3.Dot(body.position - p0, -flat):0.0} m backwards, charge now {bellows.Charge:0.00}");
        Vector3 held = Camera.main.WorldToViewportPoint(bellows.transform.position);
        Log($"    held bellows at viewport ({held.x:0.00}, {held.y:0.00}), {held.z:0.0} m ahead");
        // Dropped while looking down at the ground: it must not end up inside it.
        controller.SetLook(controller.LookYaw, 70f);
        yield return new WaitForSeconds(0.5f);
        carrier.Release();
        yield return new WaitForSeconds(1f);
        bool inside = false;
        foreach (Collider c in Physics.OverlapSphere(bellows.transform.position, bellows.Radius * 0.8f, 1 << 7, QueryTriggerInteraction.Ignore)) inside |= !c.transform.IsChildOf(bellows.transform);
        Log($"    dropped looking down: inside the ground {inside}, at {bellows.transform.position}");

        // A carried flower sits in the bottom right.
        BubbleFlower flower = FindFirstObjectByType<BubbleFlower>();
        if (flower != null)
        {
            flower.transform.position = body.position + flat * 1.2f + Vector3.down * 0.9f;
            if (flower.TryGetComponent(out Rigidbody fb)) { fb.position = flower.transform.position; fb.linearVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.3f);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { flower });
            yield return new WaitForSeconds(1f);
            Vector3 view = Camera.main.WorldToViewportPoint(flower.transform.position);
            Log($"    carried flower base at viewport ({view.x:0.00}, {view.y:0.00}), {view.z:0.00} m ahead");
            if (framesPath != null) Save(Camera.main, Path.Combine(framesPath, "flower_held.png"));
            carrier.Release();
        }
    }

    /// <summary>Sky islands: gaps and rises between stones, standing on each, and the altar resting place on top.</summary>
    private IEnumerator CheckSkyIslands()
    {
        SkyIslands islands = FindFirstObjectByType<SkyIslands>();
        Log($"Sky islands: {(islands != null)}, stones {(islands != null ? islands.stones.Length : 0)}, built {(islands != null && islands.Built != null)}");
        if (islands == null || islands.Built == null) yield break;
        var path = new List<int>();
        for (int i = 0; i < islands.stones.Length; i++) if (i >= islands.scenery.Length || !islands.scenery[i]) path.Add(i);
        Log($"    path islands {path.Count}, drifting scenery rocks {islands.stones.Length - path.Count}");
        for (int n = 1; n < path.Count; n++)
        {
            Vector3 a = islands.stones[path[n - 1]], b = islands.stones[path[n]];
            float gap = new Vector2(b.x - a.x, b.z - a.z).magnitude - islands.radii[path[n - 1]] - islands.radii[path[n]];
            Log($"    island {n}: radius {islands.radii[path[n]]:0.0}, edge gap {gap:0.0} m, rise {b.y - a.y:+0.0}, {b.y - islands.stones[path[0]].y:0.0} m above the first");
        }
        var guide = FindFirstObjectByType<SkyIslandBubbleGuide>();
        var flowers = islands.GetComponentsInChildren<BubbleFlower>();
        Log($"    bubble flowers {flowers.Length}, guide lanes {(guide != null ? guide.lanes.Count : 0)}, cascade {(FindFirstObjectByType<BubbleCascade>() != null)}");
        yield return new WaitForSeconds(25f);
        if (guide != null)
        {
            int near = 0, shot = 0;
            foreach (var lane in guide.lanes)
            {
                if (lane.flower == null || lane.flower.LastShot == null) continue;
                shot++;
                Vector3 off = lane.flower.LastShot.transform.position - lane.target;
                float flat = new Vector2(off.x, off.z).magnitude;
                if (flat < 2f) near++;
                Log($"      lane: bubble {flat:0.0} m from its gap point, {lane.flower.LastShot.transform.position.y - lane.flower.transform.position.y:+0.0} m above its flower");
            }
            Log($"    after 25 s: {shot} lanes have a bubble out, {near} hanging within 2 m of their gap point");
        }
        int stood = 0;
        foreach (int i in path)
        {
            Teleport(islands.stones[i] + Vector3.up * (halfHeight + 0.4f), Vector3.forward);
            yield return new WaitForSeconds(0.6f);
            bool ok = controller.Grounded && Mathf.Abs(body.position.y - halfHeight - islands.stones[i].y) < 0.6f;
            if (ok) stood++;
            if (i == islands.stones.Length - 1 || !ok) Log($"    on stone {i}: grounded {controller.Grounded}, feet {body.position.y - halfHeight - islands.stones[i].y:+0.00} vs its top");
        }
        Log($"    stood on {stood} of {path.Count}");
        RestSpot altar = null;
        foreach (RestSpot spot in FindObjectsByType<RestSpot>(FindObjectsSortMode.None))
            if ((spot.transform.position - islands.AltarPoint).sqrMagnitude < 9f) altar = spot;
        Log($"    altar resting place: {(altar != null ? altar.kind + " at " + altar.transform.position : "none")}");
        // A jump between two stones: hold W and jump from stone 3 towards stone 4.
        if (framesPath != null)
        {
            Vector3 mid = islands.stones[islands.stones.Length / 2];
            Vector3 out_ = (mid - islands.transform.position); out_.y = 0f;
            Shot(mid + out_.normalized * 30f + Vector3.down * 8f, mid + Vector3.up * 2f, "sky_islands.png");
            Shot(islands.AltarPoint + new Vector3(8f, 4f, 8f), islands.AltarPoint, "sky_altar.png");
        }
    }

    /// <summary>The Leaf Map's west cliff ecosystem: water running down from the springs, and who lives there.</summary>
    private IEnumerator CheckCliffEcosystem()
    {
        GameObject group = GameObject.Find("03b Cliff ecosystem — springs, birds, webs");
        Log($"Cliff ecosystem: {(group != null)}");
        if (group == null) yield break;
        var fluid = group.GetComponentInChildren<ErosiveFluidSystem>();
        var springs = group.GetComponentsInChildren<ErosiveFluidSource>();
        Log($"    springs {springs.Length}, tentacles {group.GetComponentsInChildren<TentacleCreature>().Length}, goo {group.GetComponentsInChildren<GooCreature>().Length}, spiders {group.GetComponentsInChildren<SkySpider>().Length}");
        for (int t = 1; t <= 4; t++)
        {
            yield return new WaitForSeconds(10f);
            int below = 0;
            float lowest = float.MaxValue;
            foreach (var d in fluid.Droplets)
            {
                if (d == null) continue;
                float drop = float.MaxValue;
                foreach (var s in springs) drop = Mathf.Min(drop, s.transform.position.y - d.transform.position.y);
                if (drop > 6f) below++;
                lowest = Mathf.Min(lowest, d.transform.position.y);
            }
            Log($"    after {t * 10} s: {fluid.ActiveCount} droplets, {below} more than 6 m below their spring");
        }
        int birds = 0;
        var area = group.GetComponentInChildren<BirdSpawnArea>();
        foreach (BirdCreature b in FindObjectsByType<BirdCreature>(FindObjectsSortMode.None))
            if (area != null && (b.transform.position - area.transform.position).magnitude < 60f) birds++;
        Log($"    birds within 60 m of the cliff: {birds}");
        int alive = 0;
        foreach (var c in group.GetComponentsInChildren<Creature>()) if (!c.IsDead) alive++;
        Log($"    creatures in the group still alive: {alive}");
        if (framesPath != null && springs.Length > 0)
        {
            Vector3 s0 = springs[0].transform.position;
            Vector3 outward = springs[0].transform.forward; outward.y = 0f; outward.Normalize();
            // From the pool at the foot, looking up the fall; and from out in the meadow at the whole face.
            float footY = s0.y;
            foreach (var d in fluid.Droplets) if (d != null && (d.transform.position - s0).sqrMagnitude < 40f * 40f) footY = Mathf.Min(footY, d.transform.position.y);
            Vector3 foot = new Vector3(s0.x, footY, s0.z) + outward * 22f;
            Shot(foot + outward * 10f + Vector3.up * 2.5f, Vector3.Lerp(foot, s0, 0.5f), "cliff_falls.png");
            Shot(s0 + outward * 45f + Vector3.up * 2f + Vector3.Cross(Vector3.up, outward) * 25f, s0 + Vector3.down * 10f, "cliff_wide.png");
        }
        SkyIslands islands = FindFirstObjectByType<SkyIslands>();
        if (islands != null && framesPath != null)
        {
            Vector3 mid = islands.stones[islands.stones.Length / 2];
            Shot(islands.transform.position + new Vector3(45f, 5f, -45f), mid, "leaf_sky_islands.png");
        }
    }

    /// <summary>A full Bellows can be picked up; its stream pushes another Bellows and a bubble.</summary>
    private IEnumerator CheckBellowsTwo()
    {
        Vector3 flat = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
        Vector3 at = body.position + flat * 3.5f;
        if (Physics.Raycast(at + Vector3.up * 10f, Vector3.down, out RaycastHit ground, 40f, 1 << 7, QueryTriggerInteraction.Ignore)) at = ground.point;
        BellowsCreature Make(Vector3 p, string name)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(p + Vector3.up * 1.2f, Quaternion.LookRotation(flat));
            go.AddComponent<Rigidbody>(); go.AddComponent<SphereCollider>();
            return go.AddComponent<BellowsCreature>();
        }
        BellowsCreature a = Make(at, "Bellows A");
        yield return new WaitForSeconds(0.5f);
        a.SetHeat(36f); // full and swollen (1.2), not yet floating
        yield return new WaitForSeconds(2f);
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        Vector3 to = a.transform.position - Camera.main.transform.position;
        controller.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
        yield return new WaitForSeconds(0.3f);
        Log($"Full Bellows: radius {a.Radius:0.00}, floating {a.Floating}, {to.magnitude:0.0} m away, pickup target {(carrier.FindCreature() == a)}");
        // Another Bellows and a trapping bubble in front of A's spout.
        a.SetHeat(30f);
        var bodyA = a.GetComponent<Rigidbody>(); bodyA.isKinematic = true;
        BellowsCreature b = Make(a.Spout + flat * 3f - Vector3.up * 1.2f, "Bellows B");
        b.SetHeat(24f);
        TrappingBubble bubblePrefab = FindFirstObjectByType<BubbleFlower>() != null ? null : null;
        TrappingBubble bubble = null;
        BubbleFlower flower = FindFirstObjectByType<BubbleFlower>();
        if (flower != null)
        {
            flower.transform.position = a.Spout + flat * 2.5f + Vector3.Cross(Vector3.up, flat) * 1.5f + Vector3.down * 1.5f;
            if (flower.TryGetComponent(out Rigidbody fr)) { fr.position = flower.transform.position; fr.isKinematic = true; }
            Physics.SyncTransforms();
            flower.EmitGroundBubble();
            bubble = flower.LastShot;
        }
        yield return new WaitForSeconds(1.5f);
        Vector3 b0 = b.transform.position, bub0 = bubble != null ? bubble.transform.position : Vector3.zero;
        Log($"    B at {b.transform.position - a.Spout} from A's spout, A facing {a.transform.forward}");
        a.ReceiveInteraction(new CreatureInteraction(controller.gameObject, a.transform.position, flat));
        string pushedNames = "";
        for (int f = 0; f < 60; f++) { yield return new WaitForFixedUpdate(); if (a.LastPushed != null && !pushedNames.Contains(a.LastPushed)) pushedNames += a.LastPushed + "; "; }
        yield return new WaitForSeconds(1.3f);
        Log($"    pushed: {pushedNames}");
        Log($"    stream on Bellows B: moved {Vector3.Dot(b.transform.position - b0, flat):0.0} m along the stream");
        if (bubble != null) Log($"    stream on a bubble: moved {Vector3.Dot(bubble.transform.position - bub0, flat):0.0} m along the stream (sideways {Vector3.Dot(bubble.transform.position - bub0, Vector3.Cross(Vector3.up, flat)):0.0})");
        // Carried: hold the left button for a longer stream.
        bodyA.isKinematic = false;
        a.SetHeat(30f);
        typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { a });
        yield return new WaitForSeconds(0.8f);
        Vector3 held = Camera.main.WorldToViewportPoint(a.transform.position);
        Log($"    carried {a.IsCarried}, held at viewport ({held.x:0.00}, {held.y:0.00}), {held.z:0.0} m ahead");
        carrier.Release();
    }

    /// <summary>Each kind of carriable creature: where its visual middle sits on screen, how far from the right hand, and whether it goes into the ground when looking down.</summary>
    private IEnumerator CheckHolding()
    {
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        var kinds = new Dictionary<System.Type, Creature>();
        foreach (Creature c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
            if (c.CanBeCarried && !(c is TentacleCreature) && !(c is LeashBubble) && !(c is ErosiveFluidDroplet) && !kinds.ContainsKey(c.GetType())) kinds[c.GetType()] = c;
        {
            var go = new GameObject("Test Bellows");
            go.transform.position = body.position + Vector3.forward * 3f;
            go.AddComponent<Rigidbody>(); go.AddComponent<SphereCollider>();
            kinds[typeof(BellowsCreature)] = go.AddComponent<BellowsCreature>();
        }
        yield return new WaitForSeconds(1f);
        int index = 0;
        foreach (var pair in kinds)
        {
            Creature creature = pair.Value;
            if (creature == null) continue;
            controller.SetLook(controller.LookYaw, 0f);
            // Brought to arm's length first, as if walked up to.
            Vector3 near = body.position + controller.Orientation.forward * 1.6f;
            creature.transform.position = near;
            if (creature.TryGetComponent(out Rigidbody cb)) { cb.position = near; cb.linearVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { creature });
            yield return new WaitForSeconds(1.2f);
            if (carrier.CarriedCreature != creature) { Log($"{pair.Key.Name}: not carried"); continue; }
            CreatureBounds.TryMeasure(creature.transform, out Bounds b);
            Vector3 v = Camera.main.WorldToViewportPoint(b.center);
            float hand = carrier.HandAnchor != null ? Vector3.Distance(carrier.HandAnchor.position, b.ClosestPoint(carrier.HandAnchor.position)) : -1f;
            Log($"{pair.Key.Name}: size {b.size.magnitude:0.0}, middle at viewport ({v.x:0.00}, {v.y:0.00}) {v.z:0.0} m ahead, right hand {hand:0.00} m from its surface");
            if (framesPath != null && index < 6) { Directory.CreateDirectory(framesPath); Save(Camera.main, Path.Combine(framesPath, $"hold_{pair.Key.Name}.png")); }
            // Looking straight down at the ground.
            controller.SetLook(controller.LookYaw, 85f);
            yield return new WaitForSeconds(0.8f);
            bool inside = false;
            foreach (Collider c in Physics.OverlapSphere(b.center, 0.01f, 1 << 7, QueryTriggerInteraction.Ignore)) inside = true;
            CreatureBounds.TryMeasure(creature.transform, out Bounds down);
            float sunk = 0f;
            if (Physics.Raycast(down.center + Vector3.up * 5f, Vector3.down, out RaycastHit g, 20f, 1 << 7, QueryTriggerInteraction.Ignore)) sunk = Mathf.Max(0f, g.point.y - down.min.y);
            Log($"    looking down: its lowest point {sunk:0.00} m below the ground");
            carrier.Release();
            index++;
            yield return new WaitForSeconds(0.4f);
        }
        controller.SetLook(controller.LookYaw, 0f);
    }

    private void Shot(Vector3 from, Vector3 target, string file)
    {
        if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
        captureCamera.CopyFrom(Camera.main);
        captureCamera.enabled = false;
        captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
        captureCamera.fieldOfView = 65f;
        Directory.CreateDirectory(framesPath);
        Save(captureCamera, Path.Combine(framesPath, file));
    }

    private void ShotOf(Vector3 target, string file)
    {
        if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
        captureCamera.CopyFrom(Camera.main);
        captureCamera.enabled = false;
        Vector3 from = target + new Vector3(4f, 2.5f, 4f);
        captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
        captureCamera.fieldOfView = 60f;
        Directory.CreateDirectory(framesPath);
        Save(captureCamera, Path.Combine(framesPath, file));
    }

    /// <summary>The mouse icon under the crosshair in a few situations, and its drawn shapes saved as pictures.</summary>
    private IEnumerator CheckMouseIcon()
    {
        var icon = FindFirstObjectByType<MouseButtonIcon>();
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        Log($"Mouse icon: {(icon != null)}, text hidden {FindFirstObjectByType<InteractionPromptHUD>() != null}");
        if (icon == null) yield break;
        string State() => icon.Visible ? $"left {(icon.LeftLit ? "lit" : "dim")}, right {(icon.RightLit ? "lit" : "dim")}" : "hidden";
        yield return new WaitForSeconds(0.5f);
        Log($"    nothing to do: {State()}");
        // Looking at a fuzzy creature close by.
        FuzzCreature fuzz = FindFirstObjectByType<FuzzCreature>();
        if (fuzz != null)
        {
            Vector3 eyeSpot = fuzz.transform.position - Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized * 2.5f;
            Teleport(eyeSpot + Vector3.up * 0.6f, fuzz.transform.position - eyeSpot);
            Vector3 to = fuzz.transform.position - Camera.main.transform.position;
            controller.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.6f);
            Log($"    looking at a fuzzy creature: {State()} (pickup target {carrier.FindCreature()?.name}, {Vector3.Distance(Camera.main.transform.position, fuzz.transform.position):0.0} m)");
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { fuzz });
            yield return new WaitForSeconds(0.6f);
            Log($"    holding a fuzzy creature: {State()}");
            carrier.Release();
        }
        TentacleCreature tentacle = FindFirstObjectByType<TentacleCreature>();
        if (tentacle != null)
        {
            body.position = tentacle.transform.position + Vector3.up * 0.5f;
            Physics.SyncTransforms();
            yield return null;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
            yield return new WaitForSeconds(0.6f);
            Log($"    tentacle on the back: {State()}");
            carrier.Release();
            yield return new WaitForSeconds(0.3f);
        }
        if (RestSpotSystem.Instance != null && RestSpotSystem.Instance.Spots.Count > 0)
        {
            RestSpot spot = RestSpotSystem.Instance.Spots[0];
            Teleport(spot.Seat + spot.transform.forward * 1.2f + Vector3.up * 0.6f, -spot.transform.forward);
            controller.SetLook(controller.LookYaw, -60f);
            yield return new WaitForSeconds(0.6f);
            Log($"    next to a rest spot: {State()} (right click sits: {RestSpotSystem.Instance.RightClickSits})");
        }
        if (framesPath != null && MouseButtonIcon.Textures != null)
        {
            Directory.CreateDirectory(framesPath);
            // As it would look with the right button usable and the left not, on a dark background, three times larger.
            Texture2D[] t = MouseButtonIcon.Textures;
            int w = t[0].width, h = t[0].height, k = 3;
            var sheet = new Texture2D(w * k, h * k, TextureFormat.RGBA32, false);
            for (int y = 0; y < h * k; y++)
            for (int x = 0; x < w * k; x++)
            {
                Color c = new Color(0.1f, 0.11f, 0.12f, 1f);
                float o = t[0].GetPixel(x / k, y / k).a * 0.85f, l = t[1].GetPixel(x / k, y / k).a * 0.12f, r = t[2].GetPixel(x / k, y / k).a * 0.78f;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(o + l + r));
                sheet.SetPixel(x, y, c);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(framesPath, "mouse_icon.png"), sheet.EncodeToPNG());
        }
    }

    /// <summary>
    /// Stuck arms stay stuck: one arm stuck then put down (it waits there), picked up again (arms let go),
    /// two arms stuck while standing right against the wall then put down (it hangs, or waits, arms still stuck).
    /// Also: right click never throws, and the camera's field of view.
    /// </summary>
    private IEnumerator CheckTentacleHolds()
    {
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        TentacleTether tether = carrier != null ? carrier.GetComponent<TentacleTether>() : null;
        Log($"Tentacle holds: field of view {Camera.main.fieldOfView:0}, throw enabled {carrier?.ThrowEnabled}");
        TentacleCreature tentacle = null;
        foreach (TentacleCreature t in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None)) { tentacle = t; if (t.GetComponent<SpireCrawler>() == null) break; }
        if (tentacle == null || tether == null) { Log("    no tentacle creature or tether"); yield break; }
        var crawl = tentacle.GetComponent<SpireCrawler>();
        if (crawl != null) Destroy(crawl);
        if (!FindSpire(out Cliff cliff, 0f, "Stage Tower 1")) { Log("    no wall to use"); yield break; }

        IEnumerator Carry()
        {
            body.isKinematic = false;
            body.position = tentacle.transform.position + Vector3.up * 0.5f;
            Physics.SyncTransforms();
            yield return null;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
            yield return new WaitForSeconds(0.6f);
        }
        // Stand at the foot of the wall, facing it, close enough to touch it.
        IEnumerator StandAtWall()
        {
            Vector3 stand = cliff.feet + Vector3.up * (halfHeight + 0.1f) - cliff.inward * 0.9f;
            Teleport(stand, cliff.inward);
            yield return new WaitForSeconds(0.6f);
        }
        bool WallPoint(Vector3 sideways, float up, out Vector3 point, out Transform anchor)
        {
            point = default; anchor = null;
            Vector3 from = body.position + Vector3.up * up + sideways;
            if (!Physics.Raycast(from, cliff.inward, out RaycastHit hit, 6f, 1 << 7, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point + hit.normal * 0.05f; anchor = hit.collider.transform;
            return true;
        }
        Vector3 side = Vector3.Cross(Vector3.up, cliff.inward).normalized;

        // 1. One arm, then put down.
        yield return Carry();
        yield return StandAtWall();
        if (WallPoint(Vector3.zero, 1.2f, out Vector3 p1, out Transform a1)) tether.FireAt(p1, a1);
        yield return new WaitForSeconds(0.5f);
        Log($"    one arm shot: stuck {tentacle.PinnedCount}, problem '{tether.Problem}'");
        carrier.Drop();
        Vector3 putAt = tentacle.transform.position;
        yield return new WaitForSeconds(4f);
        Log($"    put down with one arm: stuck after 4 s {tentacle.PinnedCount}, waiting by its arm {tentacle.HeldByArms}, moved {Vector3.Distance(putAt, tentacle.transform.position):0.0} m");

        // 2. Picked up again: its arms let go.
        yield return Carry();
        Log($"    picked up again: stuck {tentacle.PinnedCount} (picking up lets the arms go)");

        // 3. Two arms, standing right against the wall, then put down.
        yield return StandAtWall();
        bool shotA = WallPoint(side * -2.5f, 1.5f, out Vector3 pa, out Transform aa);
        bool shotB = WallPoint(side * 2.5f, 1.5f, out Vector3 pb, out Transform ab);
        if (shotA) tether.FireAt(pa, aa);
        yield return new WaitForSeconds(0.3f);
        if (shotB) tether.FireAt(pb, ab);
        yield return new WaitForSeconds(0.5f);
        Log($"    two arms shot ({shotA}, {shotB}): stuck {tentacle.PinnedCount}, problem '{tether.Problem}'");
        carrier.Drop();
        {
            // Would the old rule (body and both arms clear where it was put down, or let go) have kept it?
            Vector3 at = tentacle.transform.position;
            bool bodyBlocked = false;
            foreach (Collider c in Physics.OverlapSphere(at, tentacle.TetherBodyRadius, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Creature>() == null && c.GetComponentInParent<SmoothFirstPersonController>() == null) bodyBlocked = true;
            bool armBlocked = false;
            foreach (Vector3 pin in tentacle.PinnedPoints())
            {
                Vector3 span = pin - at;
                foreach (RaycastHit h in Physics.SphereCastAll(at, 0.16f, span.normalized, Mathf.Max(0f, span.magnitude - 0.24f), ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.GetComponentInParent<Creature>() == null && h.collider.GetComponentInParent<SmoothFirstPersonController>() == null) armBlocked = true;
            }
            Log($"      where it was put down: body against rock {bodyBlocked}, an arm through rock {armBlocked} (either one made the old rule let both arms go)");
        }
        for (int k = 0; k < 4; k++)
        {
            yield return new WaitForSeconds(1.5f);
            Log($"      {1.5f * (k + 1):0.0} s after putting it down: stuck {tentacle.PinnedCount}, hung {tentacle.Suspended}, waiting by its arms {tentacle.HeldByArms}, cords {tether.CordCount}");
        }
        // 4. Walk off and come back: still stuck.
        body.position += -cliff.inward * 6f;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(3f);
        Log($"    after walking away: stuck {tentacle.PinnedCount}, hung {tentacle.Suspended}");

        // 5. A third shot replaces the oldest; tap R lets everything go.
        yield return Carry();
        yield return StandAtWall();
        for (int k = 0; k < 3; k++)
        {
            if (WallPoint(side * (k - 1) * 1.5f, 1.4f, out Vector3 pk, out Transform ak)) tether.FireAt(pk, ak);
            yield return new WaitForSeconds(0.3f);
        }
        Log($"    three shots: stuck {tentacle.PinnedCount} (two at most)");
        // Shooting at empty sky says why.
        tether.FireAt(body.position + Vector3.up * 200f, null);
        Log($"    after that: problem '{tether.Problem}'");
        // Mouse-only: with arms stuck, holding the right button reels in and does not put the creature down;
        // a quick right click puts it down.
        {
            if (!carrier.CarriesTentacle) yield return Carry();
            yield return StandAtWall();
            if (WallPoint(Vector3.zero, 3.5f, out Vector3 pr, out Transform ar)) tether.FireAt(pr, ar);
            body.position += -cliff.inward * 4f + Vector3.up * 0.3f;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.4f);
            var rmbReel = typeof(PlayerCreatureCarrier).GetMethod("UpdateRightMouse", Private);
            float startGap = Vector3.Distance(body.position, pr);
            bool everReeled = false;
            rmbReel.Invoke(carrier, new object[] { true, true, false, 0f });
            for (int k = 0; k < 60; k++)
            {
                yield return new WaitForFixedUpdate();
                // The carrier calls this every frame with the button state; frames here stand in for that.
                typeof(PlayerCreatureCarrier).GetMethod("UpdateRightMouse", Private).Invoke(carrier, new object[] { false, true, false, Time.fixedDeltaTime });
                everReeled |= tether.Reeling;
                if (k % 15 == 0) Log($"      reel frame {k}: steps {tether.ReelSteps}, busy {BoneRide.Busy}, carried {tentacle.IsCarried}, reeling {tether.Reeling}, velocity {body.linearVelocity.magnitude:0.0}, kinematic {body.isKinematic}, gap {Vector3.Distance(body.position, pr):0.0}");
            }
            rmbReel.Invoke(carrier, new object[] { false, false, true, 0f });
            yield return new WaitForSeconds(0.3f);
            Log($"    held right button with an arm stuck: reeled {everReeled}, gap {startGap:0.0} -> {Vector3.Distance(body.position, pr):0.0} m, still carried {tentacle.IsCarried}");
            // The same pull with the reel key, for comparison.
            float keyGap = Vector3.Distance(body.position, pr);
            tether.ScriptedReel = true;
            for (int k = 0; k < 60; k++)
            {
                yield return new WaitForFixedUpdate();
                if (k % 20 == 0) Log($"      key reel frame {k}: steps {tether.ReelSteps}, reeling {tether.Reeling}, velocity {body.linearVelocity.magnitude:0.0}, gap {Vector3.Distance(body.position, pr):0.0}");
            }
            tether.ScriptedReel = false;
            yield return null;
            Log($"    reel key: gap {keyGap:0.0} -> {Vector3.Distance(body.position, pr):0.0} m");
            rmbReel.Invoke(carrier, new object[] { true, true, false, 0f });
            rmbReel.Invoke(carrier, new object[] { false, false, true, 0.05f });
            yield return new WaitForSeconds(0.3f);
            Log($"    quick right click: carried {tentacle.IsCarried} (false = put down), stuck {tentacle.PinnedCount}");
        }
        // Boarding a bone child by shooting an arm into it: a resting one, then a flying one.
        for (int run = 0; run < 2; run++)
        {
            BoneChild child = null;
            foreach (BoneChild c in BoneChild.All) if (c != null) { child = c; break; }
            if (child == null) { Log("    no bone child"); break; }
            if (run == 1)
            {
                FireBubble fire = FindFirstObjectByType<FireBubble>();
                if (fire == null) { Log("    no fire bubble to warm a child"); break; }
                if (fire.Heat < 6f) fire.AddHeat(8f);
                child.Cradle(fire);
                yield return new WaitForSeconds(5f);
            }
            if (!carrier.CarriesTentacle) yield return Carry();
            Vector3 aimAt = child.RidePoint;
            Vector3 off = Vector3.ProjectOnPlane(Random.onUnitSphere, Vector3.up).normalized;
            body.isKinematic = false;
            body.position = aimAt + off * 9f + Vector3.up * 1f;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.3f);
            Transform bead = null; float near = float.MaxValue;
            foreach (Collider c in child.GetComponentsInChildren<Collider>())
            { float d = Vector3.Distance(c.bounds.center, child.RidePoint); if (d < near) { near = d; bead = c.transform; } }
            float start = Time.time;
            tether.FireAt(child.RidePoint, bead);
            bool approached = BoneRide.Approaching;
            while (Time.time - start < 7f && BoneRide.Mount == null && (BoneRide.Approaching || Time.time - start < 0.3f)) yield return null;
            Log($"    boarding a {(run == 1 ? $"flying (warmth {child.Warmth:0.00})" : "resting")} child from 9 m: started {approached}, mounted {BoneRide.Mount == (object)child} after {Time.time - start:0.0} s, ended: {BoneRide.LastApproachEnd}, problem '{tether.Problem}'");
            if (BoneRide.Mount != null)
            {
                // Steering on it: it flies on by itself the way the rider looks; A/D sideways, W/S up and down.
                yield return new WaitForSeconds(0.4f);
                {
                    Vector3 look = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                    Vector3 cruiseFrom = child.RidePoint;
                    yield return new WaitForSeconds(2f);
                    Vector3 cruised = child.RidePoint - cruiseFrom;
                    Log($"      no keys for 2 s: flew {cruised.magnitude:0.0} m, {Vector3.Dot(cruised, look):0.0} m along the view");
                }
                Log($"      on board: reins {BoneRide.Reins(child.transform)}, reined {child.Reined}, warmth {child.Warmth:0.00}");
                Vector3 before = child.RidePoint;
                Vector3 right = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized);
                BoneRide.ScriptedMove = new Vector2(1f, 0f);
                yield return new WaitForSeconds(1.5f);
                Vector3 moved = child.RidePoint - before;
                Log($"      D held 1.5 s: moved {moved.magnitude:0.0} m, {Vector3.Dot(moved, right):0.0} m to the right, {moved.y:+0.0;-0.0} m up");
                BoneRide.ScriptedMove = Vector2.zero;
                before = child.RidePoint;
                BoneRide.ScriptedRise = 1f;
                yield return new WaitForSeconds(1.5f);
                BoneRide.ScriptedRise = 0f;
                Log($"      W held 1.5 s: rose {child.RidePoint.y - before.y:+0.0;-0.0} m");
                BoneRide.ScriptedRise = -1f;
                before = child.RidePoint;
                yield return new WaitForSeconds(2f);
                BoneRide.ScriptedRise = 0f;
                Log($"      S held 2 s: rose {child.RidePoint.y - before.y:+0.0;-0.0} m");
                // Left click while riding: an arm shot at the ground pulls the rider off towards it.
                if (Physics.Raycast(body.position, Vector3.down + Camera.main.transform.forward, out RaycastHit spotHit, 40f, 1 << 7))
                {
                    tether.FireAt(spotHit.point + spotHit.normal * 0.05f, spotHit.collider.transform);
                    yield return new WaitForSeconds(0.3f);
                    Log($"      shot an arm from its back at {spotHit.collider.name} {spotHit.distance:0.0} m: still riding {BoneRide.Mount != null}, reeling {tether.Reeling}, carrying tentacle {carrier.CarriesTentacle}, stuck {tentacle.PinnedCount}, problem '{tether.Problem}'");
                }
                else Log("      nothing below to shoot at");
                BoneRide.ScriptedMove = null;
                BoneRide.GetOff(); yield return new WaitForSeconds(0.3f);
            }
        }
        // Trapped in a bubble with the tentacle on the back: an arm shot out and reeled in drags the whole bubble.
        {
            BubbleFlower flower = FindFirstObjectByType<BubbleFlower>();
            var prefab = flower != null ? typeof(BubbleFlower).GetField("bubblePrefab", Private)?.GetValue(flower) as TrappingBubble : null;
            if (prefab == null) Log("    no trapping bubble to test with");
            else
            {
                if (!carrier.CarriesTentacle) yield return Carry();
                tether.LetGoAll();
                yield return StandAtWall();
                body.position += -cliff.inward * 8f + Vector3.up * 5f; // in open air, so the bubble touches nothing
                // Fuzzy creatures following the player would touch the bubble and pop it: send them off.
                foreach (Creature other in FindObjectsByType<Creature>(FindObjectsSortMode.None))
                    if (other != tentacle && !(other is TrappingBubble) && Vector3.Distance(other.transform.position, body.position) < 15f)
                    { other.transform.position += Vector3.right * 60f; var ob = other.GetComponent<Rigidbody>(); if (ob != null) ob.position = other.transform.position; }
                Physics.SyncTransforms();
                yield return null;
                TrappingBubble bubble = Instantiate(prefab, body.position, Quaternion.identity);
                yield return new WaitForFixedUpdate();
                var capture = controller.GetComponent<BubbleCapture>() ?? controller.gameObject.AddComponent<BubbleCapture>();
                for (int w = 0; w < 30 && !capture.CanCapture; w++) yield return new WaitForSeconds(0.1f);
                CreatureBounds.TryMeasure(controller.transform, out Bounds measured);
                Log($"    before trapping: can capture {capture.CanCapture} (captured {capture.IsCaptured}, enabled {capture.isActiveAndEnabled}), player measures {CreatureBounds.LargestSide(measured):0.0} m");
                bool trapped = capture.IsCaptured || bubble.TryCapture(controller.gameObject);
                if (capture.IsCaptured && capture.OwningBubble != null) bubble = capture.OwningBubble;
                if (bubble == null) { Log("    the bubble popped before the test could use it"); yield break; }
                yield return new WaitForSeconds(0.5f);
                Vector3 target = cliff.feet + Vector3.up * 3f;
                if (bubble != null && Physics.Raycast(bubble.transform.position, (target - bubble.transform.position).normalized, out RaycastHit wallHit, 30f, 1 << 7, QueryTriggerInteraction.Ignore))
                {
                    tether.FireAt(wallHit.point + wallHit.normal * 0.05f, wallHit.collider.transform);
                    Vector3 start = bubble.transform.position;
                    float gap0 = Vector3.Distance(start, wallHit.point);
                    tether.ScriptedReel = true;
                    for (int q = 0; q < 4; q++)
                    {
                        yield return new WaitForSeconds(0.5f);
                        Log($"      {0.5f * (q + 1):0.0} s reeling: bubble {(bubble != null ? Vector3.Distance(bubble.transform.position, wallHit.point).ToString("0.0") + " m from the arm's end" : "gone")}, inside {capture.IsCaptured}, reel steps {tether.ReelSteps}");
                    }
                    tether.ScriptedReel = false;
                    Log($"    trapped in a bubble ({trapped}, captured {capture.IsCaptured}): reeled 2 s, bubble went from {gap0:0.0} to {Vector3.Distance(bubble != null ? bubble.transform.position : start, wallHit.point):0.0} m from the arm's end, player still inside {capture.IsCaptured}");
                }
                else Log("    trapped bubble: no wall to shoot");
                if (capture.IsCaptured) capture.Escape();
                if (bubble != null) Destroy(bubble.gameObject);
                yield return new WaitForSeconds(0.5f);
            }
        }
        // Right click on the tentacle: a long press still only puts it down.
        if (!carrier.CarriesTentacle) yield return Carry();
        var rmb = typeof(PlayerCreatureCarrier).GetMethod("UpdateRightMouse", Private);
        rmb.Invoke(carrier, new object[] { true, true, false, 0f });
        for (int k = 0; k < 30; k++) { rmb.Invoke(carrier, new object[] { false, true, false, 0.02f }); }
        rmb.Invoke(carrier, new object[] { false, false, true, 0.02f });
        yield return new WaitForSeconds(0.3f);
        Log($"    tentacle after a 0.6 s right click: carried {tentacle.IsCarried}, thrown {tentacle.ArmsKeptFromThrow}, stuck {tentacle.PinnedCount}");
        // A fuzzy creature: a 0.6 s right click charges and throws it.
        FuzzCreature fuzz = FindFirstObjectByType<FuzzCreature>();
        if (fuzz != null)
        {
            body.position = fuzz.transform.position + Vector3.up * 1f;
            Physics.SyncTransforms();
            yield return null;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { fuzz });
            yield return new WaitForSeconds(0.5f);
            Vector3 from = fuzz.transform.position;
            Log($"    fuzzy carried before the click: {carrier.CarriedCreature == fuzz} (carrying {carrier.CarriedCreature?.name}), throw enabled {carrier.ThrowEnabled}, allowThrow {typeof(PlayerCreatureCarrier).GetField("chargedThrow", Private)?.GetValue(carrier)}, carries tentacle {carrier.CarriesTentacle}");
            rmb.Invoke(carrier, new object[] { true, true, false, 0f });
            for (int k = 0; k < 30; k++) { rmb.Invoke(carrier, new object[] { false, true, false, 0.02f }); if (k == 20) Log($"    charging: {carrier.IsChargingThrow}, charge {carrier.ThrowCharge01:0.00}"); }
            rmb.Invoke(carrier, new object[] { false, false, true, 0.02f });
            yield return new WaitForSeconds(2f);
            Log($"    fuzzy after a 0.6 s right click: carried {fuzz.IsCarried}, flew {Vector3.Distance(from, fuzz.transform.position):0.0} m");
        }
        carrier.Release();
    }

    /// <summary>
    /// The towers without bridges: bare hands should slide off; with a tentacle creature carried the player climbs
    /// the first tower, shoots an arm at the second, reels across, climbs it, and does the same to the spire's crown.
    /// </summary>
    private IEnumerator CheckTentacleRoute()
    {
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        TentacleTether tether = carrier != null ? carrier.GetComponent<TentacleTether>() : null;
        if (spire == null || carrier == null || tether == null || spire.StageTowers.Count < 2) { Log("Route: missing spire, carrier or tether."); yield break; }
        int ribbons = 0;
        foreach (Transform t in spire.GetComponentsInChildren<Transform>()) if (t.name == "Bridge") ribbons++;
        Log($"Route: bridges left {ribbons}, landings {spire.Landings.Count}");

        // 1. Bare hands.
        carrier.Release();
        if (FindSpire(out Cliff bare, 0f, "Stage Tower 1")) yield return Climb(bare, fast: false, timeout: 30f, fromGround: false);

        // 2. Carry a tentacle creature and climb the first tower.
        TentacleCreature tentacle = null;
        foreach (TentacleCreature t in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None)) { tentacle = t; if (t.GetComponent<SpireCrawler>() == null) break; }
        if (tentacle == null) { Log("Route: no tentacle creature."); yield break; }
        var crawl = tentacle.GetComponent<SpireCrawler>();
        if (crawl != null) Destroy(crawl);
        body.isKinematic = false;
        body.position = tentacle.transform.position + Vector3.up * 0.5f;
        Physics.SyncTransforms();
        yield return null;
        typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { tentacle });
        yield return new WaitForSeconds(0.5f);
        Log($"Route: carrying a tentacle creature {carrier.CarriesTentacle}, sticky hands {climber.StickyHands}");
        if (FindSpire(out Cliff first, 0f, "Stage Tower 1")) yield return Climb(first, fast: false, timeout: 60f, fromGround: false, capture: true);

        // 3. and 4. Shoot an arm across, reel in, climb on.
        Transform second = spire.StageTowers[1];
        float secondTop = second.GetComponent<MeshCollider>().bounds.max.y - 3.5f;
        yield return Cross("Tower 1 -> Tower 2", spire.Landings[0], secondTop, tether);
        float crownTop = spire.CrownSeat(out Vector3 crown, out _) ? crown.y - 0.5f : float.NaN;
        if (spire.Landings.Count > 1) yield return Cross("Tower 2 -> Spire", spire.Landings[1], crownTop, tether);
    }

    private IEnumerator Cross(string label, (Vector3 top, Vector3 inward) landing, float topY, TentacleTether tether)
    {
        climber.ScriptedInput = Vector2.zero;
        yield return new WaitForSeconds(0.5f);
        Vector3 eye = Camera.main.transform.position;
        // A spot on the next tower's wall a little above where the bridge used to arrive.
        Vector3 goal = landing.top + landing.inward * 0.45f + Vector3.up * 2.5f;
        Vector3 toGoal = goal - eye;
        if (!Physics.Raycast(eye, toGoal.normalized, out RaycastHit hit, toGoal.magnitude + 3f, 1 << 7, QueryTriggerInteraction.Ignore))
        { Log($"Route {label}: nothing to shoot at ({toGoal.magnitude:0.0} m)"); yield break; }
        Log($"Route {label}: standing at feet {body.position.y - halfHeight:0.0}, grounded {controller.Grounded}; shooting {hit.distance:0.0} m at {hit.collider.name}");
        // The player looks where they shoot, so they face the wall they are hauled to.
        Vector3 facing = Vector3.ProjectOnPlane(hit.point - body.position, Vector3.up).normalized;
        float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        controller.SetLook(yaw, 0f);
        tether.FireAt(hit.point + hit.normal * 0.05f);
        yield return new WaitForSeconds(0.3f);
        tether.ScriptedReel = true;
        float start = Time.time;
        while (Time.time - start < 8f && Vector3.Distance(body.position, hit.point) > 1.0f) yield return null;
        yield return new WaitForSeconds(0.3f);
        Log($"Route {label}: reeled for {Time.time - start:0.0} s, now {Vector3.Distance(body.position, hit.point):0.0} m from the arm's end, reeling {tether.Reeling}");
        {
            object[] wallArgs = { null, false };
            bool sees = (bool)typeof(HandClimber).GetMethod("FindWall", Private).Invoke(climber, wallArgs);
            var seen = (RaycastHit)wallArgs[0];
            Vector3 fwd = controller.Orientation != null ? controller.Orientation.forward : controller.transform.forward;
            Log($"    at the wall: sees wall {sees} ({seen.distance:0.00} m, normal {seen.normal}), facing off by {Vector3.Angle(fwd, Vector3.ProjectOnPlane(hit.point - body.position, Vector3.up)):0} deg, grounded {controller.Grounded}, regrab {typeof(HandClimber).GetField("regrabTimer", Private)?.GetValue(climber)}, kinematic {body.isKinematic}");
        }
        climber.ScriptedInput = new Vector2(0f, 1f);
        yield return new WaitForSeconds(0.3f);
        tether.ScriptedReel = false;
        start = Time.time;
        bool grabbed = false;
        float best = float.MinValue;
        while (Time.time - start < 70f)
        {
            grabbed |= climber.IsClimbing;
            float feet = body.position.y - halfHeight;
            best = Mathf.Max(best, feet);
            if (!climber.IsClimbing && !climber.IsMantling && controller.Grounded && feet > topY - 1f && Time.time - start > 1f) break;
            if (!grabbed && Time.time - start > 4f) break;
            yield return null;
        }
        bool success = best > topY - 1f;
        total++;
        if (success) passed++;
        Log($"{(success ? "PASS" : "FAIL")} Route {label}: grabbed the wall {grabbed}, best feet {best - topY:+0.0;-0.0} vs top, {Time.time - start:0.0} s, last {climber.LastEvent}");
        climber.ScriptedInput = Vector2.zero;
    }

    /// <summary>
    /// Frame cost of each kind of creature: average frame time with everything running, then with each kind
    /// switched off in turn (its scripts disabled). Also counts renderers and colliders per kind.
    /// </summary>
    private IEnumerator CheckPerformance()
    {
        Time.timeScale = 1f;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        yield return new WaitForSeconds(3f);
        // The player's body seen from outside (front and side), to check the arms join a torso.
        if (framesPath != null)
        {
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            captureCamera.nearClipPlane = 0.02f;
            Transform o = controller.Orientation != null ? controller.Orientation : controller.transform;
            Directory.CreateDirectory(framesPath);
            Vector3[] spots = { o.forward * 1.6f + Vector3.up * 0.2f, o.right * 1.6f + o.forward * 0.4f + Vector3.up * 0.4f };
            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 from = body.position + spots[i];
                captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(body.position + Vector3.up * 0.2f - from));
                captureCamera.fieldOfView = 60f;
                Save(captureCamera, Path.Combine(framesPath, $"body_{i}.png"));
            }
        }
        var byType = new Dictionary<System.Type, List<MonoBehaviour>>();
        foreach (MonoBehaviour m in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (m == null || !m.isActiveAndEnabled) continue;
            string ns = m.GetType().Namespace ?? "";
            if (ns.StartsWith("Unity") || ns.StartsWith("AK") || m.GetType().Name.StartsWith("Ak") || m is ClimbTestDriver) continue;
            if (!byType.TryGetValue(m.GetType(), out var list)) byType[m.GetType()] = list = new List<MonoBehaviour>();
            list.Add(m);
        }
        // Each part of a creature's update, called by itself many times (milliseconds per call).
        void TimeCall(Object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, Private | BindingFlags.Public);
            if (info == null) { Log($"    {target.GetType().Name}.{method}: not found"); return; }
            info.Invoke(target, args);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) info.Invoke(target, args);
            Log($"    {target.GetType().Name}.{method}: {watch.Elapsed.TotalMilliseconds / 200.0:0.000} ms per call");
        }
        TentacleCreature tentacle = FindFirstObjectByType<TentacleCreature>();
        if (tentacle != null)
        {
            TimeCall(tentacle, "UpdateTentacles", 0.016f);
            TimeCall(tentacle, "UpdateMembrane");
            TimeCall(tentacle, "UpdateShaderState", 0.016f);
            TimeCall(tentacle, "UpdateHunt", 0.016f);
            TimeCall(tentacle, "Wander", 0.016f);
            TimeCall(tentacle, "UpdateBodyVisual", 0.016f);
        }
        FuzzCreature fuzz = FindFirstObjectByType<FuzzCreature>();
        if (fuzz != null) TimeCall(fuzz, "TickCreature", 0.016f);
        foreach (MarchingCubesVolume volume in FindObjectsByType<MarchingCubesVolume>(FindObjectsSortMode.None))
        {
            Log($"    volume '{volume.name}' under '{(volume.transform.parent != null ? volume.transform.parent.name : "-")}', {volume.NumPointsPerAxis} points per axis");
            TimeCall(volume, "DecayDensities", 0.05f);
            TimeCall(volume, "InjectFromOverlays", 0.05f);
            TimeCall(volume, "RebuildDirtyChunks");
            TimeCall(volume, "Update");
            {
                var decay = typeof(MarchingCubesVolume).GetMethod("DecayDensities", Private);
                var inject = typeof(MarchingCubesVolume).GetMethod("InjectFromOverlays", Private);
                var rebuild = typeof(MarchingCubesVolume).GetMethod("RebuildDirtyChunks", Private);
                double rebuildMs = 0;
                for (int i = 0; i < 20; i++)
                {
                    decay.Invoke(volume, new object[] { 0.05f });
                    inject.Invoke(volume, new object[] { 0.05f });
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    rebuild.Invoke(volume, null);
                    rebuildMs += w.Elapsed.TotalMilliseconds;
                }
                Log($"    MarchingCubesVolume rebuild after a real tick: {rebuildMs / 20.0:0.00} ms");
            }
        }
        BoneRib rib = FindFirstObjectByType<BoneRib>();
        if (rib != null) TimeCall(rib, "LateUpdate");
        if (rib != null) TimeCall(rib, "Update");
        // Every script's Update, LateUpdate and FixedUpdate, timed on one instance and scaled by how many there are.
        {
            var costs = new List<(string, double)>();
            foreach (var pair in byType)
            {
                MonoBehaviour one = pair.Value[0];
                if (one == null || one is ClimbTestDriver) continue;
                double total = 0;
                var parts = new List<string>();
                foreach (string name in new[] { "Update", "LateUpdate", "FixedUpdate" })
                {
                    MethodInfo info = null;
                    for (System.Type t = one.GetType(); t != null && info == null && t != typeof(MonoBehaviour); t = t.BaseType)
                        info = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, System.Type.EmptyTypes, null);
                    if (info == null) continue;
                    try
                    {
                        info.Invoke(one, null);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < 30; i++) info.Invoke(one, null);
                        double ms = watch.Elapsed.TotalMilliseconds / 30.0 * pair.Value.Count;
                        total += ms;
                        parts.Add($"{name} {ms:0.00}");
                    }
                    catch (System.Exception e) { parts.Add($"{name} failed ({e.InnerException?.GetType().Name})"); }
                }
                if (parts.Count > 0) costs.Add(($"{pair.Key.Name} x{pair.Value.Count}: {string.Join(", ", parts)}", total));
            }
            costs.Sort((x, y) => y.Item2.CompareTo(x.Item2));
            for (int i = 0; i < Mathf.Min(18, costs.Count); i++) Log($"    script cost {costs[i].Item2:0.00} ms  {costs[i].Item1}");
        }
        float baseline = 0f;
        yield return MeasureFrames(v => baseline = v);
        Log($"Perf: all running {baseline:0.00} ms per frame (batch mode, no rendering)");
    }

    private IEnumerator MeasureFrames(System.Action<float> result)
    {
        yield return null;
        yield return null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int frames = 120;
        for (int i = 0; i < frames; i++) yield return null;
        result((float)watch.Elapsed.TotalMilliseconds / frames);
    }

    /// <summary>Tentacle creatures on the tower: where they are, then carry one and climb with it.</summary>
    private IEnumerator CheckSpireCreatures()
    {
        yield return new WaitForSeconds(4f);
        var eruption = FindFirstObjectByType<SeedEruption>();
        var seedPickup = FindObjectsByType<LandmarkPickup>(FindObjectsSortMode.None);
        Log($"Seed eruption: {(eruption != null ? "yes" : "no")}, pickups: {seedPickup.Length}");
        if (eruption != null && framesPath != null)
        {
            // The crown seed: from the crown (a few moments, to catch it cracking) and from the ground 40 m out.
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Directory.CreateDirectory(framesPath);
            Transform seedPoint = seedPickup.Length > 0 ? System.Array.Find(seedPickup, p => p.kind == LandmarkPickup.Kind.Seed)?.transform : null;
            for (int i = 0; i < 10 && seedPoint != null; i++)
            {
                Vector3 centre = seedPoint.position;
                Vector3 from = centre + new Vector3(Mathf.Cos(i * 0.3f), 0f, Mathf.Sin(i * 0.3f)) * 14f + Vector3.down * 3f;
                captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(centre - from));
                Save(captureCamera, Path.Combine(framesPath, $"seed_{i}.png"));
                Log($"    seed at {centre.y:0.0}, shards out: {eruption.GetComponentsInChildren<Renderer>().Length}");
                yield return new WaitForSeconds(0.15f);
            }
            if (seedPoint != null)
            {
                Vector3 centre = seedPoint.position;
                Vector3 flat = new Vector3(centre.x, 0f, centre.z);
                Vector3 from = flat + Vector3.right * 60f;
                if (Physics.Raycast(from + Vector3.up * 200f, Vector3.down, out RaycastHit ground, 400f, 1 << 7)) from = ground.point + Vector3.up * 1.7f;
                captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(centre - from));
                captureCamera.fieldOfView = 70f;
                Save(captureCamera, Path.Combine(framesPath, "seed_from_ground.png"));
            }
        }
        // Resting places.
        {
            yield return new WaitForSeconds(1f);
            var rests = RestSpotSystem.Instance;
            if (rests == null) Log("Rest spots: no RestSpotSystem.");
            else
            {
                var kinds = new List<string>();
                foreach (RestSpot r in rests.Spots) kinds.Add(r.kind);
                Log($"Rest spots: {rests.Spots.Count} ({string.Join(", ", kinds)}); away from them the meditation gate says: {MeditationController.Gate?.Invoke() ?? "(allowed)"}");
                if (rests.Spots.Count > 0)
                {
                    RestSpot first = rests.Spots[0];
                    body.isKinematic = false;
                    body.position = first.Seat + first.transform.forward * 1.2f + Vector3.up * 0.6f;
                    body.linearVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(0.5f);
                    Log($"    beside the {first.kind}: gate says {MeditationController.Gate?.Invoke() ?? "(allowed)"}");
                    if (framesPath != null)
                    {
                        if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
                        captureCamera.CopyFrom(Camera.main);
                        captureCamera.enabled = false;
                        Vector3 from = first.transform.position + first.transform.forward * 4f + first.transform.right * 2f + Vector3.up * 2.2f;
                        captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(first.transform.position + Vector3.up * 0.6f - from));
                        Directory.CreateDirectory(framesPath);
                        Save(captureCamera, Path.Combine(framesPath, "rest_spot.png"));
                    }
                    // Every resting place has a scenic view; sitting locks the move keys.
                    for (int v = 0; v < rests.Spots.Count; v++)
                    {
                        RestSpot spot = rests.Spots[v];
                        var scenic = rests.ViewFor(spot);
                        Log($"    {spot.kind} view: {(scenic == null ? "none" : $"{Vector3.Distance(scenic.position, spot.Seat):0.0} m from the seat, looking {scenic.rotation.eulerAngles}")}");
                        if (scenic != null && framesPath != null)
                        {
                            spot.SetNew(false);
                            captureCamera.transform.SetPositionAndRotation(scenic.position, scenic.rotation);
                            captureCamera.fieldOfView = 60f;
                            Save(captureCamera, Path.Combine(framesPath, $"rest_view_{v}_{spot.kind}.png"));
                        }
                    }
                    var sit = typeof(RestSpotSystem).GetMethod("Sit", Private);
                    var stand = typeof(RestSpotSystem).GetMethod("Stand", Private);
                    sit.Invoke(rests, new object[] { first });
                    yield return new WaitForSeconds(0.3f);
                    Log($"    seated: Sitting {rests.Sitting}, move keys blocked {controller.BubbleMovementBlocked}, body kinematic {body.isKinematic}");
                    stand.Invoke(rests, null);
                    yield return null;
                }
            }
        }
        var crawlers = FindObjectsByType<SpireCrawler>(FindObjectsSortMode.None);
        var tentacles = FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None);
        // Predation: a tentacle creature on the ground and a fuzzy creature put near it.
        {
            TentacleCreature hunter = null;
            foreach (TentacleCreature t in tentacles) if (t.GetComponent<SpireCrawler>() == null && !t.IsCarried) { hunter = t; break; }
            FuzzCreature fuzz = FindFirstObjectByType<FuzzCreature>();
            if (hunter != null && fuzz != null)
            {
                Vector3 near = hunter.transform.position + hunter.transform.forward * 3f;
                var fb = fuzz.GetComponent<Rigidbody>();
                if (fb != null) fb.position = near;
                fuzz.transform.position = near;
                bool caught = false;
                for (float t = 0f; t < 10f && !caught; t += 0.5f) { yield return new WaitForSeconds(0.5f); caught = fuzz.Caught && hunter.Holding; }
                Log($"Predation: caught {caught}, distance {Vector3.Distance(fuzz.transform.position, hunter.transform.position):0.0} m");
                if (framesPath != null && caught)
                {
                    if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
                    captureCamera.CopyFrom(Camera.main);
                    captureCamera.enabled = false;
                    Vector3 from = hunter.transform.position + new Vector3(3f, 2.2f, 3f);
                    captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(fuzz.transform.position - from));
                    Directory.CreateDirectory(framesPath);
                    Save(captureCamera, Path.Combine(framesPath, "predation.png"));
                }
                for (float t = 0f; t < 14f && fuzz.Caught; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Log($"Predation: still held after the hold time: {fuzz.Caught}");
            }
            else Log("Predation: no ground tentacle creature or fuzzy creature.");
        }
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
            // How it looks from the eyes with the creature on the back.
            yield return new WaitForSeconds(2f);
            Log($"    still carried after 2 s: {carrier.CarriesTentacle}, creature {(crawlers.Length > 0 && crawlers[0] != null ? "-" : "on back")}");
            if (framesPath != null) { Capture("Carried", "back"); yield return new WaitForSeconds(0.7f); Capture("Carried", "back"); }
            yield return CheckTether();
        }
    }

    /// <summary>The two other ways past the glazed band: the heated shaft draft and the balcony bounce mushroom.</summary>
    private IEnumerator CheckGlazeRoutes()
    {
        var spireLandmark = FindFirstObjectByType<HangingSpire>();
        if (spireLandmark == null) yield break;
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null) carrier.Release();
        Vector3 probe = spireLandmark.HeatProbe;

        // Into a window from below, from inside the shaft and from outside.
        for (int side = 0; side < 2; side++)
        {
            bool inside = side == 0;
            if (!spireLandmark.FindWindow(inside, out Vector3 sill, out Vector3 radial)) { Log("Window: none found."); break; }
            Vector3 inward = inside ? radial : -radial;
            // Outside, the wall below the sill leans out, so start further out (the hands find it).
            Vector3 start = sill - inward * (inside ? 0.55f : 1.6f) + Vector3.down * 2.5f;
            Log($"Window from {(inside ? "inside" : "outside")}: sill {sill.y - probe.y:0.0} m up");
            yield return Climb(new Cliff { start = start, feet = start + Vector3.down, inward = inward, topY = sill.y, name = inside ? "Window (inside)" : "Window (outside)" },
                fast: false, timeout: 25f, fromGround: false);
        }

        // Draft: stand on the hearth, cold, then put a fire bubble on it.
        body.isKinematic = false;
        body.position = probe + Vector3.up * 1f;
        body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(3f);
        Log($"Draft cold: warmth {spireLandmark.Warmth:0.00}, player {body.position.y - probe.y:0.0} m above the base");
        Creature warm = null;
        var bubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Bubbles/Bubble.prefab");
        if (bubblePrefab != null) warm = Instantiate(bubblePrefab, probe, Quaternion.identity).GetComponent<Creature>();
        if (warm == null) { Log("Draft: no creature to warm."); }
        else
        {
            warm.transform.position = probe;
            var warmBody = warm.GetComponent<Rigidbody>();
            if (warmBody != null) { warmBody.isKinematic = true; warmBody.position = probe; }
            warm.SetHeat(100f);
            body.position = probe + Vector3.up * 1f;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            float best = 0f;
            for (float t = 0f; t < 14f; t += 0.5f)
            {
                warm.SetHeat(100f);
                warm.transform.position = probe;
                // Step off the hearth into the shaft once it is warm.
                if (t > 5f && t < 5.6f) body.position += ShaftOffset(spireLandmark);
                yield return new WaitForSeconds(0.5f);
                best = Mathf.Max(best, body.position.y - probe.y);
            }
            Log($"Draft warm ({warm.GetType().Name}): warmth {spireLandmark.Warmth:0.00}, player rose to {best:0.0} m above the base");
            if (framesPath != null) Capture("Draft", "rise");
            if (warmBody != null) warmBody.isKinematic = false;
            warm.SetHeat(0f);
        }

        // Bounce mushroom on the balcony.
        Transform shroomTransform = null;
        foreach (BounceShroom shroom in FindObjectsByType<BounceShroom>(FindObjectsSortMode.None))
            if (shroom.name.StartsWith("Balcony")) shroomTransform = shroom.transform;
        if (shroomTransform == null) { Log("Bounce: no balcony mushroom."); yield break; }
        Log($"Bounce: mushroom at {shroomTransform.position.y - probe.y:0.0} m above the base");
        if (framesPath != null)
        {
            // The glazed band and the balcony from 22 m out.
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Vector3 outward = shroomTransform.position - spireLandmark.transform.GetChild(0).position;
            outward.y = 0f;
            Vector3 from = shroomTransform.position + outward.normalized * 22f + Vector3.up * 2f;
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(shroomTransform.position + Vector3.up * 3f - from));
            captureCamera.fieldOfView = 60f;
            Directory.CreateDirectory(framesPath);
            Save(captureCamera, Path.Combine(framesPath, "glaze.png"));
            // The whole tower from 75 m out, to see the patches thicken towards the ring.
            Vector3 baseCentre = spireLandmark.transform.GetChild(0).position;
            Vector3 far = baseCentre + outward.normalized * 75f + Vector3.up * 30f;
            captureCamera.transform.SetPositionAndRotation(far, Quaternion.LookRotation(baseCentre + Vector3.up * 38f - far));
            captureCamera.fieldOfView = 65f;
            Save(captureCamera, Path.Combine(framesPath, "tower.png"));
        }
        var bounce = shroomTransform.GetComponent<BounceShroom>();
        bounce.ReceiveInteraction(new CreatureInteraction(body.gameObject, shroomTransform.position, Vector3.down));
        yield return new WaitForSeconds(1.2f);
        body.isKinematic = false;
        body.position = shroomTransform.position + Vector3.up * 2.5f;
        body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        float top = 0f;
        bool grabbed = false;
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            yield return null;
            top = Mathf.Max(top, body.position.y - probe.y);
            grabbed |= climber.IsClimbing;
        }
        Log($"Bounce: player rose to {top:0.0} m above the base (glaze ring {spireLandmark.GlazeRing.x:0.0}-{spireLandmark.GlazeRing.y:0.0}), grabbed the wall: {grabbed}");
    }

    // From the hearth towards the middle of the shaft (the hearth reaches 3.6 m in from the wall).
    private static Vector3 ShaftOffset(HangingSpire spireLandmark)
    {
        Vector3 toCentre = spireLandmark.transform.GetChild(0).position - spireLandmark.HeatProbe;
        toCentre.y = 0f;
        return toCentre.normalized * Mathf.Min(4f, toCentre.magnitude);
    }

    /// <summary>The Bone Kiln: the wave of lifting ribs, a fuzzy creature propping a rib, heat in the kiln heart.</summary>
    private IEnumerator CheckKiln()
    {
        BoneKiln kiln = null;
        for (float t = 0f; t < 20f && (kiln == null || kiln.Root == null); t += 0.5f)
        {
            kiln = FindFirstObjectByType<BoneKiln>();
            yield return new WaitForSeconds(0.5f);
        }
        if (kiln == null || kiln.Root == null) { Log("Kiln: not built."); yield break; }
        Transform rootT = kiln.Root;
        Log($"Kiln at {rootT.position}, {kiln.Ribs.Count} ribs, heart {kiln.HeartPoint}");
        body.isKinematic = true;
        body.position = rootT.position + rootT.right * 70f + Vector3.up * 40f;
        if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
        captureCamera.CopyFrom(Camera.main);
        captureCamera.enabled = false;
        captureCamera.fieldOfView = 65f;
        if (framesPath != null) Directory.CreateDirectory(framesPath);
        void Shot(string name, Vector3 localFrom, Vector3 localAt)
        {
            if (framesPath == null) return;
            Vector3 from = rootT.TransformPoint(localFrom), at = rootT.TransformPoint(localAt);
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            Save(captureCamera, Path.Combine(framesPath, name + ".png"));
        }
        for (int i = 0; i < 6; i++)
        {
            Vector2 lift = kiln.Lift;
            Log($"    cold t {Time.time:0.0}: average lift {lift.x:0.00}, ribs up {lift.y:0}");
            if (i % 2 == 0) Shot($"kiln_side_{i}", new Vector3(26f, 18f, 4f), new Vector3(0f, 2f, 6f));
            yield return new WaitForSeconds(1.5f);
        }
        Shot("kiln_tail", new Vector3(8f, 10f, 40f), new Vector3(0f, 3f, 4f));
        Shot("kiln_mouth_inside", new Vector3(0f, 3f, 9f), new Vector3(0f, -4f, -2f));
        Shot("kiln_skull_below", new Vector3(12f, -30f, 12f), new Vector3(0f, -28f, 0f));

        // A fuzzy creature held in a rib's gap: once that rib has lifted, it should stay up.
        FuzzCreature fuzz = null;
        foreach (FuzzCreature f in FindObjectsByType<FuzzCreature>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) { fuzz = f; break; }
        if (fuzz != null && kiln.Ribs.Count > 6)
        {
            BoneRib rib = kiln.Ribs[6];
            Vector3 spot = rib.restPoints[1];
            var fuzzBody = fuzz.GetComponent<Rigidbody>();
            float lowest = 1f; bool wasUp = false;
            for (float t = 0f; t < 40f; t += 0.25f)
            {
                if (fuzzBody != null) { fuzzBody.isKinematic = true; fuzzBody.position = spot; }
                fuzz.transform.position = spot;
                yield return new WaitForSeconds(0.25f);
                if (rib.open > 0.95f) wasUp = true;
                if (wasUp) lowest = Mathf.Min(lowest, rib.open);
            }
            Log($"    fuzz wedge: rib lifted {wasUp}, lowest after lifting {lowest:0.00} (propped {rib.propped})");
            var near = new List<string>();
            foreach (Collider c in Physics.OverlapSphere(spot, 1.5f, ~0, QueryTriggerInteraction.Collide)) near.Add(c.name + (c.GetComponentInParent<FuzzCreature>() != null ? "(fuzz)" : ""));
            Log($"    near the gap: {string.Join(", ", near)}; fuzz at {fuzz.transform.position}, colliders {fuzz.GetComponentsInChildren<Collider>().Length}");
            if (fuzzBody != null) fuzzBody.isKinematic = false;
            fuzz.transform.position = rootT.position + rootT.right * 40f + Vector3.up * 5f;
        }
        else Log("    fuzz wedge: no active FuzzCreature in the scene");

        var bubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Bubbles/Bubble.prefab");
        if (bubblePrefab != null)
        {
            Creature warm = Instantiate(bubblePrefab, kiln.HeartPoint, Quaternion.identity).GetComponent<Creature>();
            var warmBody = warm.GetComponent<Rigidbody>();
            for (float t = 0f; t < 14f; t += 0.5f)
            {
                warm.SetHeat(100f);
                if (warmBody != null) { warmBody.isKinematic = true; warmBody.position = kiln.HeartPoint; }
                warm.transform.position = kiln.HeartPoint;
                yield return new WaitForSeconds(0.5f);
            }
            Vector2 lift = kiln.Lift;
            Log($"    warm: warmth {kiln.Warmth:0.00}, average lift {lift.x:0.00}, ribs up {lift.y:0} of {kiln.Ribs.Count}");
            Shot("kiln_warm_side", new Vector3(26f, 18f, 4f), new Vector3(0f, 2f, 6f));
            // Keep it warm until it wakes, then follow it for a while.
            for (float t = 0f; t < 6f && !kiln.Awake; t += 0.5f)
            {
                warm.SetHeat(100f);
                yield return new WaitForSeconds(0.5f);
            }
            Log($"    awake: {kiln.Awake}");
            for (int i = 0; i < 10 && kiln.Awake; i++)
            {
                yield return new WaitForSeconds(4f);
                Vector3 h = kiln.Head;
                Log($"    t {Time.time:0}: head at {h}, {(kiln.Flying ? "flying" : "crawling")}, {h.y - rootT.position.y:0.0} m above the rim");
                if (framesPath != null && i % 2 == 1)
                {
                    Vector3 from = h + new Vector3(30f, 18f, 30f);
                    captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(h - from + Vector3.down * 4f));
                    Save(captureCamera, Path.Combine(framesPath, $"kiln_awake_{i}.png"));
                }
            }
        }
    }

    /// <summary>The carried tentacle fired into a cord: stand on it, then use it as a slingshot.</summary>
    private IEnumerator CheckTether()
    {
        var tether = FindFirstObjectByType<TentacleTether>();
        if (tether == null) { Log("Tether: no TentacleTether on the player."); yield break; }
        Vector3 spawnFlat = world.transform.position;
        Vector3 ground = new Vector3(spawnFlat.x, TopHeight(spawnFlat, out _), spawnFlat.z);
        Vector3 a = ground + Vector3.right * 5f + Vector3.up * 1.2f, b = ground - Vector3.right * 5f + Vector3.up * 1.2f;
        // Stand a little way off and shoot one arm at the first point: the creature on the back stretches it out.
        body.isKinematic = false;
        body.position = (a + b) * 0.5f + Vector3.forward * 6f + Vector3.up * 0.3f;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.5f);
        tether.FireAt(a);
        yield return new WaitForSeconds(0.6f);
        // Walk away from the stuck arm: it pulls back harder the further the player goes.
        {
            Vector3 away = (body.position - a); away.y = 0f; away.Normalize();
            Vector3 start = body.position;
            for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
            {
                body.AddForce(away * 25f, ForceMode.Acceleration);
                yield return new WaitForFixedUpdate();
            }
            Log($"Tether: walking away for 2.5 s with an arm stuck got {Vector3.Distance(start, body.position):0.0} m (it pulls back)");
            body.position = start;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.3f);
        }
        if (framesPath != null)
        {
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Vector3 from = body.position + new Vector3(4f, 2.5f, 4f);
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation((body.position + a) * 0.5f - from));
            Directory.CreateDirectory(framesPath);
            Save(captureCamera, Path.Combine(framesPath, "tether_arm.png"));
        }
        tether.FireAt(b);
        yield return new WaitForSeconds(0.4f);
        // Dragging a tentacle creature with the left hand (on a ground one).
        {
            TentacleCreature other = null;
            foreach (TentacleCreature t in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None))
                if (!t.IsCarried && t.GetComponent<SpireCrawler>() == null) { other = t; break; }
            if (other != null)
            {
                Vector3 from = other.transform.position;
                Vector3 hand = from + Vector3.up * 1.2f;
                other.Dragged = true;
                other.BeginPet(hand, Vector3.up);
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    Vector3 awayFromCord = other.transform.position - (a + b) * 0.5f; awayFromCord.y = 0f;
                    hand += awayFromCord.normalized * 3f * Time.deltaTime;
                    other.ContinuePet(hand, Vector3.up, Time.deltaTime);
                    yield return null;
                }
                other.EndPet();
                other.Dragged = false;
                Log($"Tether: dragged a tentacle creature by hand {Vector3.Distance(from, other.transform.position):0.0} m (hand moved 6 m)");
            }
        }
        // Set the creature down: it hangs between its two stuck arms.
        var carrierNow = FindFirstObjectByType<PlayerCreatureCarrier>();
        TentacleCreature hung = carrierNow.CarriedCreature as TentacleCreature;
        carrierNow.Release();
        yield return new WaitForSeconds(1f);
        Log($"Tether: cords {tether.CordCount}, creature hung {(hung != null && hung.Suspended)} with {(hung != null ? hung.PinnedCount : 0)} arms stuck, at {(hung != null ? (hung.transform.position - (a + b) * 0.5f).ToString() : "-")} from the middle");
        // Stand on it.
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.position = (a + b) * 0.5f + Vector3.up * (halfHeight + 0.6f);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(1.5f);
        Log($"Tether: standing on it, feet {body.position.y - halfHeight - (a.y):0.00} m above the cord (0 = on it), climbing {climber.IsClimbing}, last {climber.LastEvent}, kinematic {body.isKinematic}");
        // Walk along it from near one end to near the other: the feet should stay on the arms all the way.
        {
            Vector3 from = Vector3.Lerp(a, b, 0.15f), to = Vector3.Lerp(a, b, 0.85f);
            body.position = from + Vector3.up * (halfHeight + 0.5f);
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.5f);
            float lowest = float.MaxValue;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                Vector3 along = to - body.position; along.y = 0f;
                if (along.magnitude < 0.3f) break;
                body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f) + along.normalized * 2.5f;
                lowest = Mathf.Min(lowest, body.position.y - halfHeight - Mathf.Lerp(a.y, b.y, 0.5f));
                yield return null;
            }
            Log($"Tether: walked along the cord to {Vector3.Distance(body.position, to + Vector3.up * halfHeight):0.0} m from the far end, lowest feet {lowest:0.00} m against the pins' middle (fell off if far below 0)");
        }
        // Fuzzy creatures following the player would get in the way of this measurement: send them off.
        foreach (FuzzCreature f in FindObjectsByType<FuzzCreature>(FindObjectsSortMode.None)) { f.transform.position += Vector3.right * 40f; var fb = f.GetComponent<Rigidbody>(); if (fb != null) fb.position = f.transform.position; }
        Physics.SyncTransforms();
        // Walking up close: nothing should pull the player in.
        body.position = (a + b) * 0.5f + Vector3.forward * 1.6f;
        body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        yield return new WaitForSeconds(1f);
        Log($"Tether: stood next to the cord for 1 s, moved {Vector3.Distance(body.position, (a + b) * 0.5f + Vector3.forward * 1.6f):0.00} m (should stay put)");
        // Slingshot: grab the hung creature with the left hand, walk back 3 m drawing the cord, let go.
        float deepest = 0f, fling = 0f;
        if (hung != null)
        {
            hung.Dragged = true;
            hung.BeginPet(hung.transform.position, Vector3.forward);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                body.position = (a + b) * 0.5f + Vector3.forward * (1.6f + t * 2.5f);
                hung.ContinuePet(body.position + Vector3.back * 1.2f, Vector3.forward, Time.deltaTime);
                yield return null;
            }
            yield return new WaitForSeconds(0.2f);
            hung.EndPet();
            hung.Dragged = false;
            for (float t = 0f; t < 1.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                fling = Mathf.Max(fling, -body.linearVelocity.z);
                deepest = Mathf.Min(deepest, body.position.z - (a.z + b.z) * 0.5f);
            }
        }
        { var names = new List<string>(); foreach (Collider c in Physics.OverlapSphere(body.position + Vector3.back * 0.7f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) names.Add(c.name + "@" + c.gameObject.layer); Log("      blocking: " + string.Join(", ", names)); }
        Log($"Tether: drew the cord back about 3 m by hand and let go: shot over the line at {fling:0.0} m/s, reaching {-deepest:0.0} m past it (shots {tether.Flung})");
        if (framesPath != null)
        {
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Vector3 from = (a + b) * 0.5f + new Vector3(2f, 3f, 9f);
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation((a + b) * 0.5f - from));
            Directory.CreateDirectory(framesPath);
            Save(captureCamera, Path.Combine(framesPath, "tether.png"));
        }
    }

    /// <summary>A fuzz rope hung from the lip of a bridge landing, then climbed bare-handed from its bottom.</summary>
    private IEnumerator CheckFuzzRope()
    {
        var spireLandmark = FindFirstObjectByType<HangingSpire>();
        var fuzzes = FindObjectsByType<FuzzCreature>(FindObjectsSortMode.None);
        if (spireLandmark == null || spireLandmark.Landings.Count == 0 || fuzzes.Length < 2) { Log($"Fuzz rope: skipped ({fuzzes.Length} fuzz creatures)"); yield break; }
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null) carrier.Release();
        var landing = spireLandmark.Landings[0];
        // A rope hung 4.5 m above open ground near the spawn, reaching down to about head height.
        Vector3 spawnFlat = world.transform.position + Vector3.right * 6f;
        Vector3 lip = new Vector3(spawnFlat.x, TopHeight(spawnFlat, out _) + 4.5f, spawnFlat.z);
        landing = (lip, Vector3.forward);
        // Gather them round the player so they join the chain, then stick the first one at the lip.
        body.isKinematic = false;
        body.position = lip + Vector3.down * 6f;
        body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        for (int i = 1; i < fuzzes.Length; i++) { fuzzes[i].transform.position = body.position + Vector3.right * i; var fb = fuzzes[i].GetComponent<Rigidbody>(); if (fb != null) fb.position = fuzzes[i].transform.position; }
        yield return new WaitForSeconds(0.5f);
        fuzzes[0].transform.position = lip;
        var rb0 = fuzzes[0].GetComponent<Rigidbody>(); if (rb0 != null) rb0.position = lip;
        fuzzes[0].Anchor();
        yield return new WaitForSeconds(2f);
        float lowest = float.MaxValue;
        foreach (FuzzCreature f in fuzzes) if (f.InRope) lowest = Mathf.Min(lowest, f.transform.position.y);
        Log($"Fuzz rope: {FuzzCreature.RopeLength} creatures hang from the landing lip, down to {lip.y - lowest:0.0} m below it");
        if (framesPath != null)
        {
            if (captureCamera == null) { captureCamera = new GameObject("Climb Test Camera").AddComponent<Camera>(); captureCamera.enabled = false; }
            captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false;
            Vector3 from = lip + Vector3.Cross(Vector3.up, landing.inward) * 7f - landing.inward * 3f + Vector3.down * 2f;
            captureCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lip + Vector3.down * 2.5f - from));
            Directory.CreateDirectory(framesPath);
            Save(captureCamera, Path.Combine(framesPath, "fuzz_rope.png"));
        }
        // Climb it from the bottom, bare-handed, facing the rope.
        // Stand on the ground facing the rope and climb it: success = the feet get up near the top.
        Vector3 inward = Vector3.forward;
        float groundY = lip.y - 4.5f;
        var cliff = new Cliff { start = new Vector3(lip.x, groundY + halfHeight + 0.1f, lip.z) - inward * 0.75f, feet = new Vector3(lip.x, groundY, lip.z), inward = inward, topY = lip.y - 1.5f, name = "Fuzz rope (free hanging, 4.5 m)" };
        yield return Climb(cliff, fast: false, timeout: 30f, fromGround: true);
        FuzzCreature.DissolveRope();
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
        // Leftover from the previous test: let go before moving the player, or the next climb starts from there.
        climber.StopForBubbleCapture();
        yield return null;
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
        object[] args = { null, false };
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
        float start = Time.time, onTopSince = -1f, maxFeet = float.MinValue, idleSince = -1f;
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
            // Standing on a ledge part way up: walk up to the wall again, as a player would, and carry on.
            if (started && !active && !onTop && controller.Grounded)
            {
                if (idleSince < 0f) idleSince = Time.time;
                // The nearest tall wall round about (the climb may have wandered round the tower).
                RaycastHit ahead = default;
                Vector3 towards = Vector3.zero;
                float nearest = 6f;
                for (int d = 0; d < 16 && Time.time - idleSince > 1f; d++)
                {
                    Vector3 dir = Quaternion.Euler(0f, d * 22.5f, 0f) * cliff.inward;
                    if (Physics.Raycast(body.position + Vector3.up * 0.5f, dir, out RaycastHit hit, nearest, 1 << 7, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.4f)
                    { nearest = hit.distance; ahead = hit; towards = dir; }
                }
                if (towards != Vector3.zero)
                {
                    body.position = new Vector3(ahead.point.x, body.position.y, ahead.point.z) - towards * 0.55f;
                    Teleport(body.position, towards);
                    events.Add($"    {Time.time - start,5:0.0}s  walked {nearest - 0.55f:0.0} m to the wall");
                    idleSince = -1f;
                }
            }
            else idleSince = -1f;
            if (!started && Time.time - start > 4f) break;
            yield return null;
        }
        climber.ScriptedInput = Vector2.zero;
        if (success) passed++;
        float height = cliff.topY - cliff.feet.y;
        Log($"{(success ? "PASS" : "FAIL")} {cliff.name}  wall {height:0.0} m  {(fast ? "fast" : "normal")}  " +
            $"{(started ? $"{Time.time - start:0.0}s, {climbs} grab(s), best feet {maxFeet - cliff.topY:+0.0;-0.0} vs top" : "never started climbing")}");
        foreach (string e in events) Log(e);
        if (!success && started)
        {
            bool wallAhead = Physics.Raycast(body.position, cliff.inward, out RaycastHit ahead, 20f, 1 << 7, QueryTriggerInteraction.Ignore);
            bool floor = Physics.Raycast(body.position, Vector3.down, out RaycastHit under, 3f, 1 << 7, QueryTriggerInteraction.Ignore);
            if (framesPath != null && captureCamera != null)
            {
                captureCamera.CopyFrom(Camera.main);
                captureCamera.enabled = false;
                Vector3 eye = body.position + Vector3.up * 6f - cliff.inward * 8f;
                captureCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(body.position - eye));
                captureCamera.fieldOfView = 70f;
                Save(captureCamera, Path.Combine(framesPath, "ended_" + cliff.name.Replace(' ', '_') + ".png"));
            }
            Log($"    ended: grounded {controller.Grounded}, climbing {climber.IsClimbing}, mantling {climber.IsMantling}, wall ahead {(wallAhead ? $"{ahead.distance:0.0} m ({ahead.collider.name})" : "none")}, floor {(floor ? $"{under.distance:0.0} m ({under.collider.name}, normal y {under.normal.y:0.00})" : "none")}");
        }
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

    private void Update()
    {
        if (testGoo != null && testGoo.RemainingSeconds < testGoo.SecondsPerCreature)
            testGoo.TryApplyGoo();
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

    private bool FindSpire(out Cliff cliff, float startAngle, string towerName = "Spire")
    {
        cliff = default;
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (spire == null) return false;
        Transform tower = null;
        foreach (MeshCollider collider in spire.GetComponentsInChildren<MeshCollider>())
            if (collider.name == towerName) tower = collider.transform;
        if (tower == null) return false;

        Vector3 root = tower.position;
        bool main = towerName == "Spire";
        Bounds bounds = tower.GetComponent<MeshCollider>().bounds;
        for (int a = 0; a < 12; a++)
        {
            float angle = startAngle + a * 30f + 15f;
            Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            float ground = TopHeight(root + outward * (main ? 25f : 11f), out _);
            if (float.IsNaN(ground)) continue;
            Vector3 from = root + outward * (main ? 40f : 20f);
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
                topY = bounds.max.y - (main ? 9f : 3.5f),
                name = main ? $"Hanging Spire {startAngle:0}" : $"{towerName} {startAngle:0}",
            };
            return true;
        }
        return false;
    }
}
#endif
