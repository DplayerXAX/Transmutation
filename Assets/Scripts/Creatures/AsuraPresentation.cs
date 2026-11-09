using UnityEngine;

/// <summary>Feedback observes combat; damage timing stays in CreatureCombat/CreatureAttack.</summary>
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
    private Transform[] feet;
    private Vector3[] footPositions;
    private Quaternion[] footRotations;
    private Transform organ;
    private Vector3 organPosition;
    private Vector3 basePosition, baseScale, lastPosition;
    private Quaternion baseRotation;
    private float stride, release;
    private CreatureCombatState state;

    private void Awake()
    {
        creature = GetComponent<Creature>(); combat = GetComponent<CreatureCombat>(); projectile = GetComponent<ProjectileCreature>();
        if (visual == null) visual = transform.Find("Visual");
        // Never animate a physics root as a visual fallback.
        if (visual == null) { enabled = false; return; }
        basePosition = visual.localPosition; baseRotation = visual.localRotation; baseScale = visual.localScale;
        lastPosition = transform.position;
        renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r.sharedMaterial != null && !r.sharedMaterial.name.Contains("Eyes"));
        properties = new MaterialPropertyBlock();
        feet = System.Array.FindAll(visual.GetComponentsInChildren<Transform>(), t => t.name == "Foot");
        footPositions = new Vector3[feet.Length]; footRotations = new Quaternion[feet.Length];
        for (int i = 0; i < feet.Length; i++) { footPositions[i] = feet[i].localPosition; footRotations[i] = feet[i].localRotation; }
        organ = transform.Find("Firing Pivot/Firing Organ");
        if (organ != null) organPosition = organ.localPosition;
    }
    private void OnEnable()
    {
        combat = GetComponent<CreatureCombat>();
        if (combat != null) combat.StateChanged += OnState;
    }
    private void OnDisable()
    {
        if (combat != null) combat.StateChanged -= OnState;
        if (visual != null) { visual.localPosition = basePosition; visual.localRotation = baseRotation; visual.localScale = baseScale; }
    }
    private void OnState(CreatureCombatState next)
    { state = next; if (next == CreatureCombatState.Release) release = 0.16f; }
    private void Update()
    {
        if (visual == null) return;
        float distance = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude;
        lastPosition = transform.position;
        bool moving = creature.SimulationEnabled && !creature.IsCarried && !creature.IsBubbleCaptured && !creature.IsDead;
        if (moving) stride += Mathf.Min(distance, 0.2f) * 10f / Mathf.Max(0.5f, transform.lossyScale.x);
        release = Mathf.Max(0f, release - Time.deltaTime);
        float jab = release / 0.16f;
        bool spent = projectile != null && projectile.FlightState == ProjectileFlightState.Spent;
        Color color = creature.Polarity == CreaturePolarity.Dark ? darkColor : creature.Polarity == CreaturePolarity.Light ? lightColor : new Color(1f, 0.4f, 0.08f);
        if (creature.IsDead || spent) color = Color.Lerp(color, Color.gray, 0.7f);
        if (creature is RollingAsura bomber && bomber.Armed) color = Color.Lerp(color, Color.white, Mathf.PingPong(Time.time * 12f, 1f));
        properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
        foreach (Renderer renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(properties);
        float swing = moving && distance > 0.0001f ? Mathf.Sin(stride) : 0f;
        for (int i = 0; i < feet.Length; i++)
        {
            float step = swing * (i % 2 == 0 ? 1f : -1f);
            feet[i].localRotation = footRotations[i] * Quaternion.Euler(step * 30f, 0f, 0f);
            feet[i].localPosition = footPositions[i] + Vector3.up * Mathf.Max(0f, step) * 0.06f;
        }
        float hurt = creature.TryGetComponent<CreatureHurtResponse>(out var response) ? response.Strength : 0f;
        Vector3 recoil = response != null ? response.LocalDirection : Vector3.back;
        float prepare = state == CreatureCombatState.Windup ? -4f : 0f;
        visual.localPosition = basePosition + Vector3.up * Mathf.Abs(swing) * 0.035f + recoil * hurt * 0.06f;
        visual.localRotation = baseRotation * Quaternion.Euler(prepare + jab * 10f - recoil.z * hurt * 9f, 0f, recoil.x * hurt * 9f);
        visual.localScale = Vector3.Scale(baseScale, new Vector3(1f + hurt * 0.025f, 1f - hurt * 0.05f, 1f + hurt * 0.025f));
        if (organ != null) organ.localPosition = organPosition - Vector3.forward * jab * 0.16f;
        if (creature is RollingAsura roller)
        {
            visual.localRotation = baseRotation * Quaternion.Euler(roller.RollDegrees, 0f, 0f);
            if (roller.Armed) visual.localScale = baseScale * (1f + Mathf.PingPong(Time.time * 4f, 0.15f));
        }
    }
    private void OnDrawGizmosSelected()
    {
        var current = GetComponent<CreatureCombat>();
        if (current == null || current.Target == null) return;
        Gizmos.color = Color.yellow; Gizmos.DrawLine(transform.position, current.Target.AimPosition);
    }
}
