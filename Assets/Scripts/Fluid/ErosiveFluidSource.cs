using UnityEngine;

[DisallowMultipleComponent]
public sealed class ErosiveFluidSource : MonoBehaviour
{
    [SerializeField] private ErosiveFluidSystem fluid;
    [SerializeField] private bool emitting = true;
    [Min(0f)] [SerializeField] private float dropletsPerSecond = 8f;
    [Min(0.01f)] [SerializeField] private float lifetime = 15f;
    [Min(0.05f)] [SerializeField] private float dropletRadius = 0.25f;
    [Min(0f)] [SerializeField] private float spread = 0.12f;
    [SerializeField] private Vector3 initialVelocity = new Vector3(0f, 0f, 0.5f);
    [Min(0f)] [SerializeField] private float erosionRate = 0.5f;
    [Min(0.01f)] [SerializeField] private float erosionRadius = 1f;
    [Range(0f, 1f)] [SerializeField] private float stationaryMultiplier = 0.15f;
    [Min(1f)] [SerializeField] private float movingMultiplier = 2f;
    [Min(0.01f)] [SerializeField] private float fullFlowSpeed = 4f;
    [Tooltip("Heat at which a droplet from this source evaporates.")]
    [Min(0.01f)] [SerializeField] private float evaporationHeat = 3f;
    [Tooltip("Heat a droplet loses per second, so only sustained heating evaporates it.")]
    [Min(0f)] [SerializeField] private float heatLossPerSecond = 0.5f;
    private float accumulator;
    private bool driven;
    public bool Emitting => emitting;
    public ErosiveFluidSystem Fluid => fluid;
    public float Lifetime => lifetime;
    public float Radius => dropletRadius;
    public float ErosionRadius => erosionRadius;
    public float Rate => erosionRate;
    public float EvaporationHeat => evaporationHeat;
    public float HeatLossPerSecond => heatLossPerSecond;
    public void SetEmitting(bool value) { emitting = value; accumulator = 0f; }
    public void SetFluid(ErosiveFluidSystem value) => fluid = value;
    /// <summary>A driven source only supplies droplet settings; its owner (e.g. rain) spawns the drops.</summary>
    internal void SetDriven(bool value) => driven = value;
    /// <summary>Small, short-lived, gentler drops so rain shares the pool with ground sources.</summary>
    internal void ConfigureForRain()
    {
        emitting = true; lifetime = 5f; dropletRadius = 0.12f; spread = 0f;
        erosionRate = 0.3f; erosionRadius = 0.6f;
    }
    public float MotionMultiplier(float tangentialSpeed) => Mathf.Lerp(stationaryMultiplier, movingMultiplier, Mathf.Clamp01(tangentialSpeed / fullFlowSpeed));
    internal void Emit(float dt)
    {
        if (driven || !isActiveAndEnabled || !emitting || fluid == null || fluid.World == null ||
            !fluid.World.IsRegionReady(transform.position, dropletRadius + 0.5f)) { accumulator = 0f; return; }
        accumulator += dropletsPerSecond * dt;
        int count = Mathf.Min(16, Mathf.FloorToInt(accumulator));
        accumulator -= Mathf.Floor(accumulator);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * spread;
            fluid.Spawn(this, transform.position + transform.right * offset.x + transform.forward * offset.y, transform.TransformDirection(initialVelocity));
        }
    }
    private void OnDrawGizmosSelected()
    { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(transform.position, dropletRadius); Gizmos.DrawLine(transform.position, transform.position + transform.TransformDirection(initialVelocity)); }
}
