using UnityEngine;

/// <summary>One anchored organism whose visible body is a carpet of GPU leaves.</summary>
[DisallowMultipleComponent]
public sealed class LeafGarden : Creature
{
    public enum GardenPhase { Resting, Gathering, Active, Settling }
    [Header("Garden")]
    public Vector2 Area = new Vector2(96, 96);
    public int Seed = 731;
    public bool Automatic = true;
    public LeafGardenSurface Surface;
    [Header("Natural variation")]
    public bool RandomizeEventSizes;
    public bool RandomizeFirstDelay;
    public Vector2 FirstDelaySeconds = new Vector2(60,1000);
    public Vector2 SmallHeightRange = new Vector2(8,18);
    public Vector2 MediumHeightRange = new Vector2(24,40);
    public Vector2 LargeHeightRange = new Vector2(55,80);
    [Range(0,1)] public float MediumChance = 0.13f;
    [Range(0,1)] public float LargeChance = 0.05f;
    [Header("Event")]
    [Range(0, 2)] public int SizePreset = 2;
    public Vector2 RestSeconds = new Vector2(25, 45);
    [Min(0.1f)] public float GatherSeconds = 5;
    [Min(0.1f)] public float ActiveSeconds = 12;
    [Min(0.1f)] public float SettleSeconds = 8;
    public GardenPhase Phase { get; private set; }
    public Vector3 TornadoCenter { get; private set; }
    public float Blend { get; private set; }
    public float Elapsed { get; private set; }
    public float EffectTime { get; private set; }
    public int EventNumber { get; private set; }
    private float eventHeight;
    public float Height => eventHeight > 0 ? eventHeight : SizePreset == 0 ? 15 : SizePreset == 1 ? 40 : 80;
    public float SourceRadius => Mathf.Lerp(3,12,Mathf.InverseLerp(8,80,Height));
    public float TopRadius => Mathf.Lerp(3.5f,20,Mathf.InverseLerp(8,80,Height));
    public bool LaunchActive => isActiveAndEnabled && SimulationEnabled && !IsDead &&
        (Phase == GardenPhase.Active || Phase == GardenPhase.Gathering && Blend > 0.4f);
    public override bool CanBeCarried => false;
    public override bool CanBeCaptured => false;
    public override bool CanBeConsumed => false;
    public override bool Invulnerable => true;
    public override string InteractionPrompt => null;
    private System.Random random;
    private float nextRest;
    protected override void InitializeCreature() => ResetGarden();
    public void ResetGarden()
    {
        random = new System.Random(Seed); Phase = GardenPhase.Resting;
        Elapsed = EffectTime = Blend = 0; EventNumber = 0;
        eventHeight = 0;
        TornadoCenter = transform.position; ChooseRest();
        if(RandomizeFirstDelay) nextRest=Mathf.Lerp(FirstDelaySeconds.x,FirstDelaySeconds.y,(float)random.NextDouble());
    }
    private void ChooseRest() => nextRest = Mathf.Lerp(Mathf.Max(0, RestSeconds.x),
        Mathf.Max(RestSeconds.x, RestSeconds.y), (float)random.NextDouble());
    private float RollAutomaticHeight()
    {
        float roll=(float)random.NextDouble();
        Vector2 range=roll<LargeChance?LargeHeightRange:roll<LargeChance+MediumChance?MediumHeightRange:SmallHeightRange;
        return Mathf.Lerp(range.x,range.y,(float)random.NextDouble());
    }
    public void TriggerTornado()
    {
        if(Phase!=GardenPhase.Resting) return;
        if(RandomizeEventSizes) eventHeight=RollAutomaticHeight();
        for(int attempt=0;attempt<64;attempt++)
        {
            Vector3 center=transform.position + new Vector3(
                ((float)random.NextDouble() - 0.5f) * Mathf.Max(0, Area.x - SourceRadius * 2 - 8), 0,
                ((float)random.NextDouble() - 0.5f) * Mathf.Max(0, Area.y - SourceRadius * 2 - 8));
            if(!HasLeafSupport(center)) continue;
            TriggerAt(center); return;
        }
        Elapsed=0;ChooseRest();
    }
    private bool HasLeafSupport(Vector3 center)
    {
        if(Surface==null) return true;
        return Surface.Sample(center).a>0.99f &&
            Surface.Sample(center+Vector3.right*SourceRadius).a>0.99f &&
            Surface.Sample(center-Vector3.right*SourceRadius).a>0.99f &&
            Surface.Sample(center+Vector3.forward*SourceRadius).a>0.99f &&
            Surface.Sample(center-Vector3.forward*SourceRadius).a>0.99f;
    }
    public void TriggerAt(Vector3 center)
    {
        if (Phase != GardenPhase.Resting || !SimulationEnabled || IsDead) return;
        Vector3 origin = transform.position;
        float x = Mathf.Max(0, Area.x * 0.5f - SourceRadius - 2);
        float z = Mathf.Max(0, Area.y * 0.5f - SourceRadius - 2);
        TornadoCenter = origin + new Vector3(Mathf.Clamp(center.x - origin.x, -x, x), 0,
            Mathf.Clamp(center.z - origin.z, -z, z));
        if(!HasLeafSupport(TornadoCenter)) return;
        if(Surface!=null) TornadoCenter=new Vector3(TornadoCenter.x,Surface.HeightAt(TornadoCenter),TornadoCenter.z);
        EventNumber++; Elapsed = Blend = 0; Phase = GardenPhase.Gathering;
    }
    public void SetPreset(int preset)
    {
        // Finish/reset explicitly rather than teleporting an existing tornado to a new shape.
        ResetGarden(); SizePreset = Mathf.Clamp(preset, 0, 2);
    }
    public float RadiusAt(float normalizedHeight) => Mathf.Lerp(SourceRadius * 0.22f,
        TopRadius, Mathf.Pow(Mathf.Clamp01(normalizedHeight), 1.25f));
    public bool Contains(Vector3 point)
    {
        if (!LaunchActive) return false;
        float height = Mathf.Max(0.1f, Height * Blend), y = point.y - TornadoCenter.y;
        if (y < -0.1f || y > height) return false;
        Vector2 sway = Sway(y / height, EffectTime) * Blend;
        Vector2 offset = new Vector2(point.x - TornadoCenter.x, point.z - TornadoCenter.z) - sway;
        float radius = RadiusAt(y / height) * Mathf.Lerp(0.25f, 1, Blend) * 0.88f;
        return offset.sqrMagnitude < radius * radius;
    }
    public static Vector2 Sway(float h, float time) => new Vector2(
        Mathf.Sin(time * 0.32f + h * 3.8f), Mathf.Cos(time * 0.27f + h * 4.1f)) * (h * h * 3);
    protected override void TickCreature(float dt)
    {
        EffectTime += dt; Elapsed += dt;
        switch (Phase)
        {
            case GardenPhase.Resting:
                Blend = 0; if (Automatic && Elapsed >= nextRest) TriggerTornado(); break;
            case GardenPhase.Gathering:
                Blend = Mathf.SmoothStep(0, 1, Elapsed / Mathf.Max(0.1f, GatherSeconds));
                if (Elapsed >= GatherSeconds) { Phase = GardenPhase.Active; Elapsed = 0; Blend = 1; }
                break;
            case GardenPhase.Active:
                Blend = 1;
                if (Elapsed >= ActiveSeconds) { Phase = GardenPhase.Settling; Elapsed = 0; }
                break;
            case GardenPhase.Settling:
                Blend = 1 - Mathf.SmoothStep(0, 1, Elapsed / Mathf.Max(0.1f, SettleSeconds));
                if (Elapsed >= SettleSeconds) { Phase = GardenPhase.Resting; Elapsed = Blend = 0; ChooseRest(); }
                break;
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0.65f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(transform.position, new Vector3(Area.x, 0.1f, Area.y));
        if (!Application.isPlaying) return;
        Gizmos.DrawWireSphere(TornadoCenter, SourceRadius);
    }
}
