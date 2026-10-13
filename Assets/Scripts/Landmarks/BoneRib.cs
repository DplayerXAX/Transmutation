using UnityEngine;

/// <summary>
/// One rib of the Bone Kiln: a curved band of bone in three jointed pieces, hanging from a vertebra.
/// At rest the ribs lie side by side down to the ground, close enough to fence in what they cover.
/// BoneKiln sets how far each rib should be lifted; the rib eases after it, and every joint eases after
/// the one before it (lerp after lerp), so the root moves first and the tip follows late, like a whip.
/// A rib never comes down on something soft caught in its gap (a fuzzy creature, or the player). A fuzzy
/// creature it comes down on is pinned there and props it up until the player pets it or picks it up.
/// </summary>
public sealed class BoneRib : MonoBehaviour
{
    public Transform[] segments;
    [Tooltip("+1 = right of the spine, -1 = left.")]
    public float side = 1f;
    [Tooltip("Position along the spine, for the slow writhe.")]
    public float phase;
    [Tooltip("Degrees each joint turns up and back when fully lifted.")]
    public Vector3 liftAngles = new Vector3(38f, 22f, 26f);
    [Tooltip("Degrees of the slow writhe at each joint.")]
    public Vector3 writheAngles = new Vector3(2f, 4f, 7f);
    [Tooltip("Degrees each joint also swings forward and back as it writhes.")]
    public Vector3 twistAngles = new Vector3(2f, 4f, 6f);
    [Tooltip("How quickly the rib follows its target, then each joint follows the one before (per second).")]
    public Vector4 followRates = new Vector4(1.1f, 2.2f, 1.5f, 1.0f);
    [Tooltip("Colliders that the rib bends away from instead of passing through (the terrain).")]
    public LayerMask avoidLayers = 1 << 7;
    [Tooltip("The colossus itself: its own bones are not avoided.")]
    public Transform owner;

    [Header("Runtime (read only)")]
    [Range(0f, 1f)] public float open;
    public bool propped;
    [Tooltip("Set once the colossus wakes: the rib just moves, nothing props it.")]
    public bool free;

    // World points along the rib at rest: the gap it closes.
    [HideInInspector] public Vector3[] restPoints;
    [HideInInspector] public Vector3 tipLocal;

    private readonly Collider[] hits = new Collider[16];
    private FuzzCreature wedged;
    // Each joint's own, lagging copy of the lift, the writhe and the sway.
    private readonly float[] lift = new float[3];
    private readonly float[] writhe = new float[3];
    private readonly float[] sway = new float[3];
    // A fixed turn at the root joint, found once, that starts the rib clear of the ground.
    private float baseOffset;
    private bool placed;
    private bool wasClear = true;
    private readonly Quaternion[] lastRotations = new Quaternion[3];
    private readonly float[] lastLift = new float[3], lastWrithe = new float[3], lastSway = new float[3];

    /// <summary>Eases the lift towards target. A rib does not come down while its gap is occupied.</summary>
    public void Drive(float target, float deltaTime)
    {
        // A pinned fuzzy creature holds the rib up until the player pets it or picks it up.
        if (wedged != null)
        {
            if (wedged.IsCarried || wedged.IsPetted || !wedged.isActiveAndEnabled) Release();
            else target = Mathf.Max(target, open);
        }
        propped = false;
        if (!free && target < open - 0.001f && GapOccupied())
            propped = true;
        else
            open = Mathf.Lerp(open, target, 1f - Mathf.Exp(-followRates.x * deltaTime));
        Pose(Time.time, deltaTime);
    }

    private void Pose(float time, float deltaTime)
    {
        if (segments == null) return;
        if (!placed) Place();

        // Remember the current pose: if the next one would reach into the ground, the rib just stops here.
        for (int k = 0; k < 3 && k < segments.Length; k++)
        {
            lastRotations[k] = segments[k].localRotation;
            lastLift[k] = lift[k]; lastWrithe[k] = writhe[k]; lastSway[k] = sway[k];
        }

        // The root joint follows the rib's lift and its own slow writhe; each further joint follows the one
        // before it a little later, so motion runs out along the rib in layers.
        float leadWrithe = Mathf.Sin(time * 0.35f - phase * 0.45f);
        float leadSway = Mathf.Sin(time * 0.22f - phase * 0.4f + 1.7f);
        for (int k = 0; k < segments.Length && k < 3; k++)
        {
            float rate = k == 0 ? followRates.y : k == 1 ? followRates.z : followRates.w;
            float follow = 1f - Mathf.Exp(-rate * deltaTime);
            float liftTarget = k == 0 ? open : lift[k - 1];
            float writheTarget = k == 0 ? leadWrithe : writhe[k - 1];
            float swayTarget = k == 0 ? leadSway : sway[k - 1];
            lift[k] = Mathf.Lerp(lift[k], liftTarget, follow);
            writhe[k] = Mathf.Lerp(writhe[k], writheTarget, follow);
            sway[k] = Mathf.Lerp(sway[k], swayTarget, follow);
            Apply(k, k == 0 ? baseOffset : 0f);
        }

        // Stop rather than pass through the ground: keep the last clear pose until the rib's own motion
        // carries it somewhere clear again. (If it is already touching, as when the body crawls into a
        // slope, it keeps moving, since moving is the only way out.)
        bool clear = ClearOfGround();
        if (!clear && wasClear)
        {
            for (int k = 0; k < 3 && k < segments.Length; k++)
            {
                segments[k].localRotation = lastRotations[k];
                lift[k] = lastLift[k]; writhe[k] = lastWrithe[k]; sway[k] = lastSway[k];
            }
            return;
        }
        wasClear = clear;
    }

    private void Apply(int k, float extra)
    {
        float eased = lift[k] * lift[k] * (3f - 2f * lift[k]);
        float angle = liftAngles[k] * eased + writheAngles[k] * writhe[k] + extra;
        // Positive turns lift the right ribs; the left ones mirror it. The sway twists the rib.
        segments[k].localRotation = Quaternion.AngleAxis(side * angle, Vector3.forward) * Quaternion.AngleAxis(twistAngles[k] * sway[k], Vector3.up);
    }

    /// <summary>Once, at the start: turn the rib at its root until its resting pose is out of the ground.</summary>
    private void Place()
    {
        placed = true;
        float[] tries = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 80f, -80f, 100f, -100f, 130f, -130f };
        foreach (float offset in tries)
        {
            for (int k = 0; k < 3 && k < segments.Length; k++) Apply(k, k == 0 ? offset : 0f);
            if (!ClearOfGround()) continue;
            baseOffset = offset;
            return;
        }
        baseOffset = 0f;
    }

    /// <summary>True when no piece of the rib reaches into the terrain (the colossus's own bones do not count).</summary>
    private bool ClearOfGround()
    {
        for (int k = 0; k < segments.Length; k++)
        {
            Vector3 a = segments[k].position;
            Vector3 b = k + 1 < segments.Length ? segments[k + 1].position : segments[k].TransformPoint(tipLocal);
            if (Physics.Linecast(a, b, out RaycastHit hit, avoidLayers, QueryTriggerInteraction.Ignore) && (owner == null || !hit.transform.IsChildOf(owner)))
                return false;
        }
        return true;
    }

    private void Release()
    {
        if (wedged != null && wedged.isActiveAndEnabled) wedged.SetSimulationEnabled(true);
        wedged = null;
    }

    private void OnDisable() => Release();

    /// <summary>True when a fuzzy creature or the player stands in the gap this rib would close.</summary>
    private bool GapOccupied()
    {
        if (restPoints == null) return false;
        foreach (Vector3 point in restPoints)
            if (Holds(point, 1.0f)) return true;
        return false;
    }

    private bool Holds(Vector3 point, float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(point, radius, hits, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider hit = hits[i];
            FuzzCreature fuzz = hit.GetComponentInParent<FuzzCreature>();
            if (fuzz != null)
            {
                // Caught: it stays wedged in the gap.
                if (wedged == null && !fuzz.IsCarried)
                {
                    wedged = fuzz;
                    fuzz.SetSimulationEnabled(false);
                    Rigidbody fuzzBody = fuzz.GetComponent<Rigidbody>();
                    if (fuzzBody != null && !fuzzBody.isKinematic) fuzzBody.linearVelocity = Vector3.zero;
                }
                return true;
            }
            Rigidbody body = hit.attachedRigidbody;
            if (body != null && body.GetComponent<SmoothFirstPersonController>() != null) return true;
        }
        return false;
    }

    /// <summary>A point a share of the way down the rib (0 = at the spine, 1 = the tip), on its current pose.</summary>
    public Vector3 Along(float share)
    {
        share = Mathf.Clamp01(share);
        float scaled = share * segments.Length;
        int index = Mathf.Min(segments.Length - 1, Mathf.FloorToInt(scaled));
        Vector3 start = segments[index].position;
        Vector3 end = index + 1 < segments.Length ? segments[index + 1].position : segments[index].TransformPoint(tipLocal);
        return Vector3.Lerp(start, end, scaled - index);
    }
}
