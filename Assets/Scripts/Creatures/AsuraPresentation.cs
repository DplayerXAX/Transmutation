using UnityEngine;

/// <summary>Event-driven feedback only; damage and attack timing belong to simulation.</summary>
public sealed class AsuraPresentation : MonoBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField] private Color lightColor = new Color(0.96f, 0.78f, 0.3f);
    [SerializeField] private Color darkColor = new Color(0.33f, 0.12f, 0.65f);
    private Creature creature;
    private CreatureCombat combat;
    private ProjectileCreature projectile;
    private Renderer[] renderers;
    private MaterialPropertyBlock properties;
    private Vector3 baseScale;
    private float flash;
    private CreatureCombatState state;

    private void Awake()
    {
        creature = GetComponent<Creature>(); combat = GetComponent<CreatureCombat>(); projectile = GetComponent<ProjectileCreature>();
        if (visual == null) visual = transform.Find("Visual");
        if (visual == null) visual = transform;
        baseScale = visual.localScale;
        renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), renderer =>
            renderer.sharedMaterial != null && !renderer.sharedMaterial.name.Contains("Eyes"));
        properties = new MaterialPropertyBlock();
    }
    private void OnEnable()
    {
        creature.Damaged += OnDamaged; creature.Died += OnDied;
        if (combat != null) combat.StateChanged += OnState;
        if (projectile != null) projectile.FlightChanged += OnFlight;
    }
    private void OnDisable()
    {
        creature.Damaged -= OnDamaged; creature.Died -= OnDied;
        if (combat != null) combat.StateChanged -= OnState;
        if (projectile != null) projectile.FlightChanged -= OnFlight;
    }
    private void OnDamaged(Creature damaged, float amount, Creature source) => flash = 0.15f;
    private void OnDied(Creature dead) { state = CreatureCombatState.Idle; flash = 0.12f; }
    private void OnState(CreatureCombatState next)
    { state = next; if (next == CreatureCombatState.Release) flash = 0.12f; }
    private void OnFlight(ProjectileFlightState next) { flash = 0.12f; }
    private void Update()
    {
        flash = Mathf.Max(0f, flash - Time.deltaTime);
        bool spent = projectile != null && projectile.FlightState == ProjectileFlightState.Spent;
        Color color = creature.Polarity == CreaturePolarity.Dark ? darkColor :
            creature.Polarity == CreaturePolarity.Light ? lightColor : new Color(1f, 0.4f, 0.08f);
        if (creature.IsDead || spent) color = Color.Lerp(color, Color.gray, 0.7f);
        if (flash > 0f) color = Color.Lerp(color, Color.white, flash / 0.15f);
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.SetPropertyBlock(properties);
        float squash = state == CreatureCombatState.Windup ? 0.9f + 0.04f * Mathf.Sin(Time.time * 35f) : 1f;
        visual.localScale = Vector3.Scale(baseScale, new Vector3(1f / squash, squash, 1f / squash));
    }
    private void OnDrawGizmosSelected()
    {
        CreatureCombat current = GetComponent<CreatureCombat>();
        if (current == null || current.Target == null) return;
        Gizmos.color = Color.yellow; Gizmos.DrawLine(transform.position, current.Target.AimPosition);
    }
}
