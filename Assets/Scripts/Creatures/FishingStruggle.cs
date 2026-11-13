using UnityEngine;

/// <summary>Numerical catch progress; direction is supplied by world-space geometry.</summary>
public sealed class FishingStruggle
{
    public float Progress { get; private set; }
    public float Required { get; private set; }
    public float EmptyTime { get; private set; }
    public Vector2Int CounterDirection { get; private set; }
    public bool Won => Progress >= Required;
    public bool Lost => EmptyTime >= failureGrace;
    float failureGrace, drain;

    public void Begin(float required, float drainRate, float grace)
    {
        Required = Mathf.Max(1, required); Progress = Required * .25f; EmptyTime = 0;
        CounterDirection = Vector2Int.zero;
        drain = Mathf.Max(0, drainRate); failureGrace = Mathf.Max(.01f, grace);
    }
    public void SetCounterDirection(Vector2Int direction) { CounterDirection = direction; }
    public void Tick(float dt)
    {
        if (Won || Lost || dt <= 0) return;
        float before = Progress;
        Progress = Mathf.Max(0, Progress - drain * dt);
        EmptyTime = Progress > 0 ? 0 : EmptyTime + (before > 0 && drain > 0 ? Mathf.Max(0, dt - before / drain) : dt);
    }
    public bool Accepts(Vector2Int input)
    {
        if (Mathf.Abs(input.x) > 1 || Mathf.Abs(input.y) > 1) return false;
        if (CounterDirection == Vector2Int.zero) return input == Vector2Int.zero;
        return input != Vector2Int.zero && Vector2.Dot(((Vector2)input).normalized, ((Vector2)CounterDirection).normalized) >= .5f;
    }
    public bool Pull(Vector2Int input, float load = 0, float gain = 1.5f, float loadBonus = .2f)
    {
        if (Won || Lost || !Accepts(input)) return false;
        Progress = Mathf.Min(Required, Progress + Mathf.Max(0, gain) * (1 + Mathf.Clamp01(load) * Mathf.Clamp01(loadBonus)));
        EmptyTime = 0; return true;
    }
}

/// <summary>Samples an independent virtual tip, never the animated pole or its recoil.</summary>
public sealed class FishingLoad
{
    public float Normalized { get; private set; }
    Vector3 previousBody, previousRest, previousSteering, previousFish, previousForward;
    bool initialized;
    public void Reset() { initialized = false; Normalized = 0; }
    public void Sample(Vector3 body, Vector3 restTip, Vector3 steeringOffset, Vector3 fishBeforeSwimming,
        Vector3 naturalTravel, Vector3 forward, float dt, float smoothing = .15f, float fullLoadSpeed = 2)
    {
        if (dt <= 0) return;
        Vector3 bodyTravel = body - previousBody;
        bool discontinuity = !initialized || dt > .2f || bodyTravel.magnitude > 1 ||
            (fishBeforeSwimming - previousFish).magnitude > 2 || Vector3.Angle(previousForward, forward) > 45;
        if (discontinuity) Normalized = 0;
        else
        {
            Vector3 line = (fishBeforeSwimming - restTip - steeringOffset).normalized;
            // Natural travel is sampled within this frame, before the tether acts; body
            // translation and externally repositioning the fish never earn extra load.
            Vector3 fishVelocity = naturalTravel / dt;
            Vector3 rotatedSteering = Quaternion.FromToRotation(previousForward, forward) * previousSteering;
            Vector3 cameraVelocity = (restTip - previousRest - bodyTravel + rotatedSteering - previousSteering) / dt;
            Vector3 steeringVelocity = (steeringOffset - rotatedSteering) / dt;
            float cameraSeparation = Mathf.Clamp(-Vector3.Dot(cameraVelocity, line), -fullLoadSpeed, fullLoadSpeed);
            if (Vector3.Dot(forward, (fishBeforeSwimming - body).normalized) < Mathf.Cos(70 * Mathf.Deg2Rad)) cameraSeparation = 0;
            float separation = Vector3.Dot(fishVelocity - steeringVelocity, line) + cameraSeparation;
            if (Mathf.Abs(separation) < .001f) separation = 0;
            float target = Mathf.Clamp01(separation / Mathf.Max(.01f, fullLoadSpeed));
            Normalized = Mathf.Lerp(Normalized, target, 1 - Mathf.Exp(-dt / Mathf.Max(.01f, smoothing)));
        }
        previousBody = body; previousRest = restTip; previousSteering = steeringOffset;
        previousForward = forward; previousFish = fishBeforeSwimming + naturalTravel; initialized = true;
    }
    // Exclude the numerical tether correction from the next frame's swimming velocity.
    public void CommitFishPosition(Vector3 correctedPosition) { previousFish = correctedPosition; }
}

public static class FishingCounterPull
{
    public static Vector2Int Calculate(Vector3 playerPosition, Vector3 fishPosition, Vector3 viewRight, Vector3 viewUp,
        Vector2Int previous, float neutralDistance = .25f, float activeDistance = .4f)
    {
        Vector3 offset = fishPosition - playerPosition;
        return new Vector2Int(Axis(Vector3.Dot(offset, viewRight), previous.x, neutralDistance, activeDistance),
            Axis(Vector3.Dot(offset, viewUp), previous.y, neutralDistance, activeDistance));
    }
    static int Axis(float offset, int previous, float neutral, float active)
    {
        if (Mathf.Abs(offset) <= neutral) return 0;
        int required = offset > 0 ? -1 : 1;
        return required == previous || Mathf.Abs(offset) >= active ? required : 0;
    }
}
