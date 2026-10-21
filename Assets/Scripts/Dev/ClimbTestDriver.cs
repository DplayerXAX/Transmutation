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
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTestFlower") >= 0)
        {
            // A carried bubble flower still shoots: left click (the interactor) fires along the view; the thrower leaves it alone.
            foreach (BubbleFlower f in FindObjectsByType<BubbleFlower>(FindObjectsSortMode.None))
            {
                float under = Physics.Raycast(f.transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit g, 30f, ~0, QueryTriggerInteraction.Ignore) ? f.transform.position.y - g.point.y : float.NaN;
                Log($"Flower '{f.name}' at {f.transform.position}, {under:0.00} m above what is under it, awake {f.SimulationEnabled}");
            }
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
                Log($"    carried {flowerCarrier.CarriedCreature == flower}, throwable {CreatureThrower.Instance?.HoldingThrowable}");
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
                    Log($"    shot {shot + 1}: new bubble {(made != null && made != before)}, {(made != null ? Vector3.Distance(made.transform.position, body.position).ToString("0.0") + " m out" : "-")}");
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
        yield return new WaitForSeconds(10f);
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
        BoneRide.ScriptedTurn = null;
        BoneRide.ScriptedRise = 0f;
    }

    /// <summary>Pulls the right rein and climbs while looking elsewhere, then measures the mount's movement.</summary>
    private IEnumerator Steer(Transform headPart, string label, float seconds)
    {
        Vector3 start = headPart.position;
        float yaw = controller.LookYaw + 70f;
        controller.SetLook(yaw, -10f);
        BoneRide.ScriptedTurn = 1f;
        BoneRide.ScriptedRise = 1f;
        yield return new WaitForSeconds(seconds);
        BoneRide.ScriptedTurn = null;
        BoneRide.ScriptedRise = 0f;
        Vector3 moved = headPart.position - start;
        Vector3 rider = body.position - headPart.position;
        Log($"    steered the {label} with the right rein for {seconds:0} s: moved {moved.magnitude:0.0} m, rose {moved.y:+0.0;-0.0} m; rider {rider.magnitude:0.0} m from its head");
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
            object[] wallArgs = { null };
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
        var bubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Bubble.prefab");
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

        var bubblePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Bubble.prefab");
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
