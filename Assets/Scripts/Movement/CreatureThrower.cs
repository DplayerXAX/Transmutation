using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Throwing what the player carries (a fuzzy creature, a fire bubble...; not a tentacle creature, whose left
/// click shoots its arms instead, nor a creature that is used with left click while carried): hold the left mouse button to aim, a dotted arc shows where it will fly and
/// a ring where it lands; let go to throw it.
/// The creature flies the arc by itself (its own movement is paused), and stops at the first thing it touches.
/// A fire bubble that hits a bone child is caught by it (BoneChild.Catch).
/// Added to the player by itself.
/// </summary>
public sealed class CreatureThrower : MonoBehaviour
{
    [Tooltip("Launch speed (m/s); the arc steepens or flattens with where the view points.")]
    [Min(1f)] [SerializeField] private float throwSpeed = 13f;
    [Tooltip("Gravity on a thrown creature (m/s²), gentler than the world's so throws carry.")]
    [Min(1f)] [SerializeField] private float throwGravity = 14f;
    [Tooltip("Longest a throw is followed before the creature is let go wherever it is.")]
    [Min(0.5f)] [SerializeField] private float maxFlightTime = 4f;

    public static CreatureThrower Instance { get; private set; }
    public bool Aiming => aiming;
    /// <summary>What the last thrown creature hit (for tests).</summary>
    public string LastHit { get; private set; }

    private sealed class Flight
    {
        public Creature creature;
        public Vector3 position, velocity;
        public float time;
        public bool previousSimulation, previousKinematic;
    }

    private PlayerCreatureCarrier carrier;
    private Camera view;
    private bool aiming;
    private LineRenderer arc;
    private Transform marker;
    private readonly List<Flight> flights = new List<Flight>();
    private readonly List<Vector3> points = new List<Vector3>();
    private const int Solid = ~(1 << 2);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AddToPlayer()
    {
        PlayerCreatureCarrier carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null && carrier.GetComponent<CreatureThrower>() == null) carrier.gameObject.AddComponent<CreatureThrower>();
    }

    private void Awake()
    {
        Instance = this;
        carrier = GetComponent<PlayerCreatureCarrier>();
    }

    /// <summary>True when the player holds something that left click throws.</summary>
    public bool HoldingThrowable => carrier != null && carrier.CarriedCreature != null && !carrier.CarriesTentacle && !carrier.CarriedCreature.CanUseWhileCarried;

    private void Update()
    {
        if (view == null) view = Camera.main;
        Mouse mouse = Mouse.current;
        bool pressed = HoldingThrowable && mouse != null && mouse.leftButton.isPressed && Application.isFocused;
        if (pressed) PlanArc(StartPoint(), LaunchVelocity());
        ShowArc(pressed);
        if (aiming && !pressed && HoldingThrowable && mouse != null && !mouse.leftButton.isPressed) Throw();
        aiming = pressed;
    }

    private Vector3 StartPoint()
    {
        Creature held = carrier.CarriedCreature;
        Vector3 inFront = view.transform.position + view.transform.forward * 0.8f;
        // From the hand; if the held creature has not caught up with the hand yet, from just in front of the eyes.
        return held != null && Vector3.Distance(held.transform.position, view.transform.position) < 2.5f ? held.transform.position : inFront;
    }

    private Vector3 LaunchVelocity()
    {
        // A little above where the view points, so a level look still lobs it.
        Vector3 dir = (view.transform.forward + Vector3.up * 0.18f).normalized;
        return dir * throwSpeed;
    }

    /// <summary>Throws what is carried along the arc (also used by tests).</summary>
    public void Throw()
    {
        if (!HoldingThrowable || view == null) return;
        Creature creature = carrier.CarriedCreature;
        Vector3 start = StartPoint();
        Vector3 velocity = LaunchVelocity();
        carrier.Release();
        bool previousSimulation = creature.SimulationEnabled;
        // It flies by itself: its own walking, floating and physics pause until it lands.
        creature.SetSimulationEnabled(false);
        var body = creature.GetComponent<Rigidbody>();
        bool previousKinematic = body != null && body.isKinematic;
        if (body != null) body.isKinematic = true;
        flights.Add(new Flight { creature = creature, position = start, velocity = velocity,
            previousSimulation = previousSimulation, previousKinematic = previousKinematic });
    }

    /// <summary>Return temporary throw ownership before carry, capture, or death takes over.</summary>
    internal static void ReleaseFlight(Creature creature)
    {
        if (Instance == null || creature == null) return;
        for (int i = Instance.flights.Count - 1; i >= 0; i--)
        {
            Flight flight = Instance.flights[i];
            if (flight.creature != creature) continue;
            Instance.flights.RemoveAt(i);
            var body = creature.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = flight.previousKinematic;
            if (!creature.IsConsumptionPending) creature.SetSimulationEnabled(flight.previousSimulation);
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            Flight f = flights[i];
            if (f.creature == null) { flights.RemoveAt(i); continue; }
            if (f.creature.IsConsumptionPending) { flights.RemoveAt(i); continue; }
            // Snatched out of the air by a bone child passing close: the child has it now.
            if (BoneChild.IsCradled(f.creature)) { f.creature.SetSimulationEnabled(true); flights.RemoveAt(i); continue; }
            f.time += dt;
            Vector3 next = f.position + f.velocity * dt + Vector3.down * (0.5f * throwGravity * dt * dt);
            f.velocity += Vector3.down * throwGravity * dt;
            Vector3 step = next - f.position;
            bool landed = f.time > maxFlightTime;
            bool hitSolid = FindFlightHit(f.position, next, f.creature, out RaycastHit hit);
            // Stop the catch sweep at the first obstacle, so it cannot jump through a wall.
            Vector3 end = hitSolid ? f.position + step.normalized * hit.distance : next;
            if (BoneChild.CatchAlongPath(f.creature, f.position, end))
            {
                LastHit = "Bone child caught the throw";
                f.creature.SetSimulationEnabled(true);
                flights.RemoveAt(i);
                continue;
            }
            if (hitSolid)
            {
                next = hit.point + hit.normal * 0.3f;
                landed = true;
                LastHit = hit.collider.name + (hit.collider.transform.parent != null ? " in " + hit.collider.transform.parent.name : "");
                // A fire bubble thrown at a bone child is caught in its ribs.
                BoneChild child = hit.collider.GetComponentInParent<BoneChild>();
                if (child != null && child.Catch(f.creature)) { Land(f, next, caught: true); flights.RemoveAt(i); continue; }
            }
            f.position = next;
            f.creature.transform.position = next;
            var body = f.creature.GetComponent<Rigidbody>();
            if (body != null) body.position = next;
            if (landed) { Land(f, next, caught: false); flights.RemoveAt(i); }
        }
    }

    private static void Land(Flight f, Vector3 at, bool caught)
    {
        f.creature.transform.position = at;
        var body = f.creature.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = at;
            // A caught bubble stays held still by the child; anything else is a physics body again.
            if (!caught)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
            }
        }
        f.creature.SetSimulationEnabled(f.previousSimulation);
    }

    private bool IsPlayer(Collider c)
    {
        return carrier != null && carrier.PlayerBody != null && c.attachedRigidbody == carrier.PlayerBody;
    }

    private readonly RaycastHit[] flightHits = new RaycastHit[32];

    private bool FindFlightHit(Vector3 from, Vector3 to, Creature thrown, out RaycastHit nearest)
    {
        nearest = default;
        Vector3 step = to - from;
        if (step.sqrMagnitude < 1e-8f) return false;
        int count = Physics.SphereCastNonAlloc(from, 0.25f, step.normalized, flightHits, step.magnitude, Solid, QueryTriggerInteraction.Ignore);
        RaycastHit[] results = flightHits;
        if (count == flightHits.Length)
        {
            results = Physics.SphereCastAll(from, 0.25f, step.normalized, step.magnitude, Solid, QueryTriggerInteraction.Ignore);
            count = results.Length;
        }
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = results[i];
            if (candidate.distance >= distance || IsPlayer(candidate.collider) ||
                (thrown != null && candidate.collider.transform.IsChildOf(thrown.transform))) continue;
            nearest = candidate;
            distance = candidate.distance;
        }
        return distance < float.PositiveInfinity;
    }

    /// <summary>The flight path from start, stopping at the first thing it would hit.</summary>
    private void PlanArc(Vector3 start, Vector3 velocity)
    {
        points.Clear();
        points.Add(start);
        Vector3 p = start, v = velocity;
        const float step = 0.05f;
        for (int i = 0; i < 80; i++)
        {
            Vector3 next = p + v * step + Vector3.down * (0.5f * throwGravity * step * step);
            v += Vector3.down * throwGravity * step;
            if (FindFlightHit(p, next, carrier.CarriedCreature, out RaycastHit hit))
            {
                points.Add(hit.point);
                landing = hit.point;
                landingNormal = hit.normal;
                landingFound = true;
                return;
            }
            points.Add(next);
            p = next;
        }
        landingFound = false;
    }

    private Vector3 landing, landingNormal;
    private bool landingFound;

    private void ShowArc(bool on)
    {
        if (arc == null)
        {
            var go = new GameObject("Throw Arc");
            arc = go.AddComponent<LineRenderer>();
            arc.widthMultiplier = 0.03f;
            arc.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            arc.material.SetColor("_BaseColor", new Color(1f, 0.92f, 0.75f));
            arc.shadowCastingMode = ShadowCastingMode.Off;
            arc.textureMode = LineTextureMode.Tile;
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Throw Marker";
            ring.GetComponent<Renderer>().sharedMaterial = arc.material;
            marker = ring.transform;
        }
        arc.gameObject.SetActive(on);
        marker.gameObject.SetActive(on && landingFound);
        if (!on) return;
        arc.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) arc.SetPosition(i, points[i]);
        if (landingFound)
        {
            marker.position = landing + landingNormal * 0.02f;
            marker.rotation = Quaternion.FromToRotation(Vector3.up, landingNormal);
            marker.localScale = new Vector3(0.45f, 0.01f, 0.45f) * (1f + 0.12f * Mathf.Sin(Time.time * 8f));
        }
    }
}
