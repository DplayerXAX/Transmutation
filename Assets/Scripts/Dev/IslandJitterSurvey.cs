using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Opt-in Play Mode survey, created at runtime and never saved into scenes. For `seconds`, samples
/// every visible thing riding the first active MovingFrame after rendering positions are final:
/// frame members, children of rooted members (Recycle flowers), tentacle bones, and the main
/// camera. Jitter is each frame's miss from the previous frame's motion extrapolated over this frame's
/// length (a frame-time-aware second difference) of the position in the frame's drawn space:
/// smooth travel gives near zero, a pose drawn on a different clock from the deck gives roughly
/// speed x fixedDeltaTime. Also reports, per tentacle, how far its first bone strays from the body
/// (a detached skeleton). Read the result from <see cref="Result"/> or the console.
/// </summary>
public sealed class IslandJitterSurvey : MonoBehaviour
{
    public float seconds = 8;
    public int listed = 15;
    public static string Result { get; private set; } = "";
    public static bool Running { get; private set; }

    sealed class Track { public string name; public Vector3 a, b, axes; public float ta, tb; public int n; public double sum; public float max; }

    // The player's body on the physics timeline (sampled each step, relative to the frame's body pose at that
    // step). Large values here mean contact bounce; small values with large drawn jitter mean a timing mismatch.
    readonly Track playerPhysics = new() { name = "Player body (physics steps)" };
    MovingFrame surveyed; Rigidbody playerBody;
    void FixedUpdate()
    {
        if (!surveyed || !playerBody) return;
        // Runs after the frame planned this step: its body is still at the previous planned pose.
        Pose pose = surveyed.PreviousPhysicsPose;
        Accumulate(playerPhysics, Quaternion.Inverse(pose.rotation) * (playerBody.position - pose.position));
    }
    static float Accumulate(Track t, Vector3 local)
    {
        float jerk = 0;
        if (t.n >= 2)
        {
            // Extrapolate the last motion over this frame's own length: uneven frame times alone must not read as
            // a jump for something moving fast (a launched bubble at 25 m/s across a 6 ms then a 20 ms frame).
            float last = t.tb - t.ta, now = Time.time - t.tb;
            Vector3 d = local - (t.b + (t.b - t.a) * (last > 1e-5f ? now / last : 1));
            jerk = d.magnitude;
            t.sum += jerk * jerk; t.max = Mathf.Max(t.max, jerk);
            t.axes += Vector3.Scale(d, d);
        }
        t.a = t.b; t.b = local; t.ta = t.tb; t.tb = Time.time; t.n++;
        return jerk;
    }
    static string Line(Track t)
    {
        int k = Mathf.Max(1, t.n - 2);
        Vector3 axis = new(Mathf.Sqrt(t.axes.x / k), Mathf.Sqrt(t.axes.y / k), Mathf.Sqrt(t.axes.z / k));
        return $"  {t.max:0.0000} / {Mathf.Sqrt((float)(t.sum / k)):0.0000}  {t.name}  (RMS x/y/z {axis.x:0.0000}/{axis.y:0.0000}/{axis.z:0.0000})";
    }

    IEnumerator Start()
    {
        Running = true; Result = ""; events.Clear();
        var frame = MovingFrame.Active.Count > 0 ? MovingFrame.Active[0] : null;
        if (!frame) { Finish("No MovingFrame active."); yield break; }
        var tracks = new Dictionary<Transform, Track>();
        var detach = new Dictionary<TentacleCreature, float>();
        surveyed = frame;
        StartCoroutine(AfterSimulation(frame));
        var player = FindFirstObjectByType<SmoothFirstPersonController>();
        if (player) { playerBody = player.GetComponent<Rigidbody>(); contacts = player.gameObject.AddComponent<PlayerContactRecorder>(); contacts.hideFlags = HideFlags.DontSave;
            stepRecorder = gameObject.AddComponent<PlayerStepRecorder>(); stepRecorder.frame = frame; stepRecorder.body = playerBody; }
        float end = Time.time + seconds;
        float minY = float.MaxValue, maxY = float.MinValue;
        while (Time.time < end)
        {
            yield return new WaitForEndOfFrame();
            Transform deck = frame.transform;
            minY = Mathf.Min(minY, deck.position.y); maxY = Mathf.Max(maxY, deck.position.y);
            foreach (var member in frame.Members)
            {
                if (!member || member.ExternalMotion) continue;
                Sample(tracks, deck, member.transform, member.name);
                if (member.TryGetComponent<CompostCreature>(out _))
                    foreach (Transform child in member.transform)
                        if (child.GetComponent<RecycleFlower>()) Sample(tracks, deck, child, member.name + "/" + child.name);
                if (member.TryGetComponent<TentacleCreature>(out var tentacle)) SampleTentacle(tracks, detach, deck, tentacle);
            }
            if (Camera.main) Sample(tracks, deck, Camera.main.transform, "Main Camera");
        }
        var report = new StringBuilder();
        report.AppendLine($"[IslandJitter] {seconds:0}s, island speed {frame.Speed:0.00} m/s, height range {maxY - minY:0.00} m, fixed dt {Time.fixedDeltaTime:0.000}s, {tracks.Count} tracks");
        report.AppendLine("Worst per-frame jitter in the deck's drawn space (max / RMS, metres):");
        foreach (var t in tracks.Values.Where(t => t.n >= 3).OrderByDescending(t => t.max).Take(listed))
            report.AppendLine(Line(t));
        if (playerPhysics.n >= 3) report.AppendLine(Line(playerPhysics));
        if (contacts) report.AppendLine(ContactReport());
        if (stepRecorder) report.AppendLine(StepReport());
        report.AppendLine(PostReport());
        foreach (var kv in detach)
            if (kv.Key) report.AppendLine($"Tentacle {kv.Key.name} (dead={kv.Key.IsDead}): first bone up to {kv.Value:0.000} m from body centre");
        if (events.Count > 0) { report.AppendLine($"Jumps over {eventThreshold} m (first {events.Count}):"); foreach (var e in events) report.AppendLine(e); }
        Finish(report.ToString());
    }

    void Finish(string result)
    {
        Result = result; Running = false;
        Debug.Log(result);
        if (contacts) Destroy(contacts);
        Destroy(gameObject);
    }

    PlayerContactRecorder contacts;
    /// <summary>Records the player's ground contacts: tilted normals on a flat deck kick a frictionless body sideways.</summary>
    internal sealed class PlayerContactRecorder : MonoBehaviour
    {
        public int steps, tilted; public float steepest = 1, sideImpulse; public string worstWith = "-";
        public readonly HashSet<string> surfaces = new();
        void OnCollisionStay(Collision c) => Record(c);
        void OnCollisionEnter(Collision c) => Record(c);
        void Record(Collision c)
        {
            steps++;
            surfaces.Add(c.collider.name + "(" + c.collider.GetType().Name + ")");
            Vector3 impulse = c.impulse;
            sideImpulse = Mathf.Max(sideImpulse, new Vector2(impulse.x, impulse.z).magnitude);
            for (int i = 0; i < c.contactCount; i++)
            {
                float up = c.GetContact(i).normal.y;
                if (up < .98f && up > .3f) { tilted++; if (up < steepest) { steepest = up; worstWith = c.collider.name; } }
            }
        }
    }
    /// <summary>
    /// Runs after every gameplay FixedUpdate (just before the frame's velocity commit and the simulation):
    /// the player's velocity relative to the deck point under it, as scripts left it for this step, and
    /// how much the simulation then changed it.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    internal sealed class PlayerStepRecorder : MonoBehaviour
    {
        public MovingFrame frame; public Rigidbody body;
        public int steps; public double preSum, simSum; public float preMax, simMax;
        Vector3 lastSet; bool haveLast;
        SmoothFirstPersonController controller;
        public float vsRideMax, rideErrMax; public double vsRideSum, rideErrSum;
        static readonly System.Reflection.FieldInfo RideField = typeof(SmoothFirstPersonController).GetField("rideVelocity",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        void FixedUpdate()
        {
            if (!frame || !body) return;
            // Change made by the previous simulation step to the velocity scripts had set.
            if (haveLast) { float sim = (body.linearVelocity - lastSet).magnitude; simSum += sim * sim; simMax = Mathf.Max(simMax, sim); }
            float pre = (body.linearVelocity - frame.VelocityAt(body.position)).magnitude;
            preSum += pre * pre; preMax = Mathf.Max(preMax, pre); steps++;
            if (controller == null) controller = body.GetComponent<SmoothFirstPersonController>();
            if (controller != null)
            {
                var ride = (Vector3)RideField.GetValue(controller);
                float vsRide = (body.linearVelocity - ride).magnitude, rideErr = (ride - frame.VelocityAt(body.position)).magnitude;
                vsRideMax = Mathf.Max(vsRideMax, vsRide); rideErrMax = Mathf.Max(rideErrMax, rideErr);
                vsRideSum += vsRide * vsRide; rideErrSum += rideErr * rideErr;
            }
            lastSet = body.linearVelocity; haveLast = true;
        }
    }
    PlayerStepRecorder stepRecorder;
    // Horizontal player velocity relative to the deck, read right after each simulation step.
    int postSteps; double postSum; float postMax; readonly List<string> postSeries = new();
    IEnumerator AfterSimulation(MovingFrame frame)
    {
        var wait = new WaitForFixedUpdate();
        int lastFrame = -1, inFrame = 0;
        while (true)
        {
            yield return wait;
            if (!playerBody) continue;
            if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; inFrame = 0; } else inFrame++;
            // The frame's plan for the step just simulated is still current.
            Vector3 rel = playerBody.linearVelocity - frame.VelocityAt(playerBody.position - playerBody.linearVelocity * Time.fixedDeltaTime);
            float side = new Vector2(rel.x, rel.z).magnitude;
            postSteps++; postSum += side * side; postMax = Mathf.Max(postMax, side);
            if (postSeries.Count < 30) postSeries.Add($"{Vector3.Dot(rel, frame.transform.right):0.00}@{inFrame}");
        }
    }
    string PostReport() => postSteps == 0 ? "" :
        $"After simulation, horizontal player velocity vs deck: max {postMax:0.000} m/s, RMS {Mathf.Sqrt((float)(postSum / postSteps)):0.000}; first lateral values (value@step within render frame): {string.Join(" ", postSeries)}";
    string StepReport() => stepRecorder && stepRecorder.steps > 0
        ? $"Player velocity vs deck before each step: max {stepRecorder.preMax:0.000} m/s, RMS {Mathf.Sqrt((float)(stepRecorder.preSum / stepRecorder.steps)):0.000}. " +
          $"Body vs controller rideVelocity: max {stepRecorder.vsRideMax:0.000}, RMS {Mathf.Sqrt((float)(stepRecorder.vsRideSum / stepRecorder.steps)):0.000}. " +
          $"rideVelocity vs deck: max {stepRecorder.rideErrMax:0.000}, RMS {Mathf.Sqrt((float)(stepRecorder.rideErrSum / stepRecorder.steps)):0.000}"
        : "";
    string ContactReport() => contacts ? $"Player contacts: {contacts.steps} callbacks, {contacts.tilted} tilted floor normals (steepest normal.y {contacts.steepest:0.000} on {contacts.worstWith}), max sideways impulse {contacts.sideImpulse:0.000} N·s, surfaces: {string.Join(", ", contacts.surfaces)}" : "";

    /// <summary>Jumps larger than this are listed individually with the time and state.</summary>
    public static float eventThreshold = .3f;
    static readonly List<string> events = new();
    static string context = "";

    static float Sample(Dictionary<Transform, Track> tracks, Transform deck, Transform target, string name)
    {
        if (!target) return 0;
        Vector3 local = deck.InverseTransformPoint(target.position);
        if (!tracks.TryGetValue(target, out var t)) tracks[target] = t = new Track { name = name };
        float jerk = Accumulate(t, local);
        if (jerk > eventThreshold && events.Count < 40)
            events.Add($"  t={Time.time:0.000} f={Time.frameCount} {jerk:0.000} m {name} local={local:F2} {context}");
        return jerk;
    }

    static void SampleTentacle(Dictionary<Transform, Track> tracks, Dictionary<TentacleCreature, float> detach, Transform deck, TentacleCreature tentacle)
    {
        var root = FindRoot(tentacle);
        if (!root) return;
        var body = tentacle.GetComponent<Rigidbody>();
        context = $"[dead={tentacle.IsDead} sim={tentacle.SimulationEnabled} kin={(body && body.isKinematic)} interp={(body ? body.interpolation.ToString() : "-")} rootParented={root.parent == tentacle.transform}]";
        float worst = detach.TryGetValue(tentacle, out var w) ? w : 0;
        foreach (Transform arm in root)
        {
            Transform first = null, last = null;
            foreach (Transform part in arm) if (part.name == "Joint") { if (!first) first = part; last = part; }
            if (!first) continue;
            worst = Mathf.Max(worst, Vector3.Distance(first.position, tentacle.transform.position));
            Sample(tracks, deck, first, tentacle.name + "/" + arm.name + " root bone");
            Sample(tracks, deck, last, tentacle.name + "/" + arm.name + " tip bone");
        }
        detach[tentacle] = worst;
        context = "";
    }

    static Transform FindRoot(TentacleCreature tentacle)
    {
        string name = tentacle.name + " Tentacles";
        var child = tentacle.transform.Find(name);
        if (child) return child; // a corpse carries its bones
        var found = GameObject.Find(name);
        return found ? found.transform : null;
    }
}
