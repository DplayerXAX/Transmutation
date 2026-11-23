using UnityEngine;

/// <summary>
/// One creature put away on a mount: a part of the player's body (BodyMounts) or a spot in the pack beast's fur
/// (FuzzPackBeast). It stays alive: it keeps its simulation (heat, ageing, its own look) and is owned by the
/// player's carrier like a held creature, so its own movement and AI stand still. Its solid colliders are off
/// (trigger ranges such as a fire's heat stay on), it shrinks to fit the mount and rides it with a slow,
/// phase-delayed swing. Restore() gives every one of those back.
/// A few creatures whose carried behaviour is a tool in the hand (the fishing pole) sleep instead.
/// </summary>
public sealed class StowedCreature
{
    public Creature Creature { get; }
    public int Slot { get; set; }
    /// <summary>Radius of its visible shape once shrunk to fit.</summary>
    public float Radius { get; private set; }
    /// <summary>Where its visible middle is now.</summary>
    public Vector3 Centre { get; private set; }

    private const float FlyTime = 0.4f;

    private readonly PlayerCreatureCarrier owner;
    private readonly bool sleeps, wasSimulating;
    private readonly Vector3 scale;
    private readonly Collider[] colliders;
    private readonly bool[] colliderWasOn;
    private readonly Renderer[] renderers;
    private readonly Rigidbody body;
    private readonly RigidbodyInterpolation bodyInterpolation;
    private readonly Vector3 centreLocal;  // pivot to visible middle, creature space, at full size
    private float fit;                     // scale factor on the mount
    private Quaternion restRotation;

    private Vector3 flyFrom;
    private Quaternion flyFromRotation;
    private float flyFromScale = 1f;
    private float flyT;
    private Quaternion swing;
    private float lagY;
    private bool started, hidden;
    private float currentScale = 1f;

    /// <summary>True once it has arrived at its mount.</summary>
    public bool Arrived => flyT >= 1f;

    /// <summary>Creatures that can go on a mount. A tentacle creature has its own back carry and is never stowed.</summary>
    public static bool CanStow(Creature creature) =>
        creature != null && creature.isActiveAndEnabled && !creature.IsCarried && !creature.IsBubbleCaptured &&
        creature.CanBeCarried && !(creature is TentacleCreature) && !(creature is FuzzCreature fuzz && fuzz.Grown);

    /// <summary>Puts the creature on a mount. It must already be out of the player's hand.</summary>
    public StowedCreature(Creature creature, PlayerCreatureCarrier carrier, int slot, float maxSize, Quaternion frame)
    {
        Creature = creature;
        owner = carrier;
        Slot = slot;
        Transform t = creature.transform;
        scale = t.localScale;

        float size = 0.3f;
        centreLocal = Vector3.zero;
        if (MeasureBody(creature, out Bounds seen))
        {
            size = Mathf.Max(0.05f, Mathf.Max(seen.size.x, Mathf.Max(seen.size.y, seen.size.z)));
            centreLocal = Quaternion.Inverse(t.rotation) * (seen.center - t.position);
        }
        naturalSize = size;
        Refit(maxSize, frame);
        Centre = t.position + t.rotation * centreLocal;

        // Owned like a held creature: its own movement stops, everything else about it keeps living.
        creature.TryBeginCarry(carrier, carrier.transform);
        body = creature.GetComponent<Rigidbody>();
        if (body != null) { bodyInterpolation = body.interpolation; body.interpolation = RigidbodyInterpolation.None; }

        sleeps = creature is FishingPoleCreature;
        wasSimulating = creature.SimulationEnabled;
        if (sleeps) creature.SetSimulationEnabled(false);

        colliders = creature.GetComponentsInChildren<Collider>(true);
        colliderWasOn = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderWasOn[i] = colliders[i].enabled;
            if (!colliders[i].isTrigger) colliders[i].enabled = false;
        }
        renderers = creature.GetComponentsInChildren<Renderer>(true);

        flyFrom = Centre;
        flyFromRotation = t.rotation;
        swing = frame;
    }

    private readonly float naturalSize;

    /// <summary>
    /// Size of the creature's body: its solid colliders when it has any (renderer bounds can be padded, the fuzz's fur
    /// mesh reserves room four times its radius), otherwise what is drawn.
    /// </summary>
    private static bool MeasureBody(Creature creature, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Collider c in creature.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger || !CreatureBounds.BelongsTo(c.transform, creature.transform)) continue;
            if (!found) { bounds = c.bounds; found = true; }
            else bounds.Encapsulate(c.bounds);
        }
        return found || CreatureBounds.TryMeasure(creature.transform, out bounds);
    }

    /// <summary>Fits it to a (new) mount size and heading.</summary>
    private void Refit(float maxSize, Quaternion frame)
    {
        fit = Mathf.Min(1f, maxSize / naturalSize);
        Radius = naturalSize * fit * 0.5f;
        Transform t = Creature.transform;
        Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
        restRotation = Quaternion.Inverse(frame) * Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat : Vector3.forward, Vector3.up);
    }

    /// <summary>Still owned by the player? False once it died, was eaten or something else took it.</summary>
    public bool Valid => Creature != null && Creature.isActiveAndEnabled && Creature.Carrier == owner &&
                         (owner == null || owner.CarriedCreature != Creature);

    /// <summary>Hides it (out of the inventory view, it is "in" the body) or shows it again.</summary>
    public void SetHidden(bool value)
    {
        if (hidden == value) return;
        hidden = value;
        foreach (Renderer r in renderers) if (r != null) r.forceRenderingOff = value;
    }

    /// <summary>
    /// Moves it to its mount: origin + (delayed frame) * offset. The frame turns with a lag (swingRate, lower = slower),
    /// so it sways behind turns without ever changing its distance from the origin. verticalLag above zero lets it
    /// bob a little behind up-down motion (0 = none). extraScale grows it a little (hovered in the view).
    /// </summary>
    public void Ride(Vector3 origin, Quaternion frame, Vector3 offset, Quaternion tilt, float swingRate, float verticalLag,
        float deltaTime, float extraScale = 1f)
    {
        if (Creature == null) return;
        if (!started) { swing = frame; lagY = origin.y; started = true; }
        swing = Quaternion.Slerp(swing, frame, 1f - Mathf.Exp(-swingRate * deltaTime));
        lagY = verticalLag > 0f ? Mathf.Lerp(lagY, origin.y, 1f - Mathf.Exp(-verticalLag * deltaTime)) : origin.y;
        Vector3 target = new Vector3(origin.x, lagY, origin.z) + swing * offset;
        Quaternion targetRotation = swing * tilt * restRotation;

        flyT = Mathf.Min(1f, flyT + deltaTime / FlyTime);
        float k = flyT * flyT * (3f - 2f * flyT);
        Vector3 centre = Vector3.Lerp(flyFrom, target, k);
        Quaternion rotation = Quaternion.Slerp(flyFromRotation, targetRotation, k);
        currentScale = Mathf.Lerp(currentScale, fit * extraScale, 1f - Mathf.Exp(-14f * deltaTime));
        float f = Mathf.Lerp(flyFromScale, currentScale, k);

        Transform t = Creature.transform;
        t.localScale = scale * f;
        Vector3 position = centre - rotation * (centreLocal * f);
        t.SetPositionAndRotation(position, rotation);
        if (body != null) body.position = position;
        Centre = centre;
    }

    /// <summary>Starts a short flight to a new mount (another body part, or the beast) from where it is now.</summary>
    public void MoveTo(int slot, float maxSize, Quaternion frame)
    {
        if (Creature == null) return;
        flyFrom = Centre;
        flyFromRotation = Creature.transform.rotation;
        flyFromScale = Creature.transform.localScale.x / Mathf.Max(1e-4f, scale.x);
        currentScale = flyFromScale;
        flyT = 0f;
        Slot = slot;
        Refit(maxSize, frame);
    }

    /// <summary>Gives it back at full size where it is: carry ownership, colliders, look and sleep as they were.</summary>
    public void Restore()
    {
        if (Creature == null) return;
        SetHidden(false);
        Transform t = Creature.transform;
        // Grow back round the same middle.
        Vector3 centre = Centre;
        t.localScale = scale;
        t.position = centre - t.rotation * centreLocal;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderWasOn[i];
        if (body != null) { body.position = t.position; body.interpolation = bodyInterpolation; }
        if (owner != null && Creature.Carrier == owner && owner.CarriedCreature != Creature) Creature.EndCarry(owner);
        if (sleeps) Creature.SetSimulationEnabled(wasSimulating);
    }
}
