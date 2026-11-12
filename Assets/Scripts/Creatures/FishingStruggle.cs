using UnityEngine;

/// <summary>Numerical struggle, independent of input devices and presentation.</summary>
public sealed class FishingStruggle
{
    public float Progress { get; private set; }
    public float Required { get; private set; }
    public float EmptyTime { get; private set; }
    public Vector2Int CounterDirection { get; private set; }
    public bool Telegraphing => telegraph > 0f;
    public bool Won => Progress >= Required;
    public bool Lost => EmptyTime >= failureGrace;
    float telegraph, directionTime, failureGrace, drain, transition;
    Vector2 interval;
    public void Begin(float required, float drainRate, float grace, Vector2 directionInterval, float transitionTime)
    {
        Required = Mathf.Max(1f, required); Progress = Required * .25f; EmptyTime = 0;
        drain = Mathf.Max(0, drainRate); failureGrace = Mathf.Max(.01f, grace);
        interval = directionInterval; transition = transitionTime; ChangeDirection();
    }
    void ChangeDirection()
    {
        Vector2Int previous = CounterDirection;
        do { int i = Random.Range(0, 8); CounterDirection = new Vector2Int(Mathf.RoundToInt(Mathf.Cos(i * Mathf.PI / 4)), Mathf.RoundToInt(Mathf.Sin(i * Mathf.PI / 4))); }
        while (CounterDirection == previous);
        directionTime = Random.Range(interval.x, interval.y); telegraph = transition;
    }
    public void Tick(float dt)
    {
        if (Won || Lost || dt <= 0) return;
        if (telegraph > 0) { telegraph = Mathf.Max(0, telegraph - dt); return; }
        directionTime -= dt;
        if (directionTime <= 0) { ChangeDirection(); return; }
        Progress = Mathf.Max(0, Progress - drain * dt);
        EmptyTime = Progress <= 0 ? EmptyTime + dt : 0;
    }
    public bool Pull(Vector2Int input)
    {
        if (Telegraphing || Won || Lost || input != CounterDirection) return false;
        Progress = Mathf.Min(Required, Progress + 1); EmptyTime = 0; return true;
    }
}
