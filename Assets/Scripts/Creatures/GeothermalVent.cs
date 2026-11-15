using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Turns a FireSource into a geothermal vent: a glowing heat source that steam pours out of. Set on the ground,
/// the ground keeps refilling it, slowly breathing between a weak and a strong flow, so a region built round
/// vents never runs out of heat (it is the outside input every heat loop needs). It looks the same carried or
/// set down: the FireSource body with steam rising off it.
///   - Set down: steam rises thicker, faster and taller the hotter the vent, and erupts now and then when nearly
///     full. The plume lifts a player standing in it (higher the fuller the vent); an eruption throws them up.
///   - Carried: the ground stops refilling it. Holding left click jets steam along the view and drives the
///     player the other way.
///   - Put down or thrown, it falls and settles upright where it lands.
/// The FireSource still does the radiating. Built on the Geothermal Vent prefab by FlameCanyonSetup.
/// </summary>
[RequireComponent(typeof(FireSource))]
[DisallowMultipleComponent]
public sealed class GeothermalVent : MonoBehaviour, FireSource.ICarriedUse
{
    [Header("Ground Heat")]
    [Tooltip("The ground stops pushing heat in at this much.")]
    [Min(1f)] [SerializeField] private float maxHeat = 4000f;
    [Tooltip("Heat the ground adds per second at the weakest and strongest point of its breath.")]
    [SerializeField] private Vector2 groundFlow = new Vector2(12f, 40f);
    [Tooltip("Seconds for one slow rise and fall of the ground flow.")]
    [Min(1f)] [SerializeField] private float breathPeriod = 45f;

    [Header("Steam")]
    [Tooltip("Puffs per second when cold and when full.")]
    [SerializeField] private Vector2 steamRate = new Vector2(3f, 26f);
    [Tooltip("Rising speed of the steam when cold and when full (m/s).")]
    [SerializeField] private Vector2 steamSpeed = new Vector2(0.7f, 2.6f);
    [Tooltip("Heat share (of Max Heat) above which the vent erupts now and then.")]
    [Range(0f, 1f)] [SerializeField] private float eruptAbove = 0.85f;
    [Tooltip("Seconds between eruptions (random in this range).")]
    [SerializeField] private Vector2 eruptEvery = new Vector2(7f, 14f);

    [Header("Plume Lift (set down)")]
    [Tooltip("Width of the column that lifts the player.")]
    [Min(0.3f)] [SerializeField] private float liftRadius = 1.8f;
    [Tooltip("How high the plume carries the player when the vent is cold and when it is full (m).")]
    [SerializeField] private Vector2 liftHeight = new Vector2(4f, 22f);
    [Tooltip("Rising speed at the bottom of the plume (m/s); it fades to nothing at the top, so the player hovers there.")]
    [Min(0.1f)] [SerializeField] private float liftSpeed = 7f;
    [Tooltip("How quickly the plume brings the player up to its speed (m/s²).")]
    [Min(0.1f)] [SerializeField] private float liftAcceleration = 30f;
    [Tooltip("Upward speed an eruption throws a player in the plume to (m/s).")]
    [Min(0f)] [SerializeField] private float eruptionThrow = 16f;

    [Header("Steam Jet (carried)")]
    [Tooltip("How hard the jet drives the player away from where it points (m/s²).")]
    [Min(0f)] [SerializeField] private float jetThrust = 26f;
    [Tooltip("Heat the jet burns per second.")]
    [Min(0f)] [SerializeField] private float jetHeatPerSecond = 60f;
    [Tooltip("Speed of the jetted steam (m/s).")]
    [Min(0.1f)] [SerializeField] private float jetSpeed = 13f;

    [Header("Look")]
    [Tooltip("Width of the mouth the steam leaves from.")]
    [Min(0.3f)] [SerializeField] private float craterRadius = 1.4f;
    [Tooltip("Size of the glowing body while carried, as a share of its size set down (it is big; in the hand it would fill the view).")]
    [Range(0.1f, 1f)] [SerializeField] private float carriedScale = 0.15f;
    [Tooltip("How far the source's centre sits above the ground when set down.")]
    [Min(0f)] [SerializeField] private float restHeight = 0.5f;

    [Header("Runtime State (Read Only)")]
    [Range(0f, 1f)] [SerializeField] private float fill;
    [SerializeField] private float flow;
    [SerializeField] private VentState state = VentState.Set;

    private enum VentState { Set, Carried, Loose }

    private FireSource source;
    private Rigidbody body;
    private ParticleSystem steam;
    private float phase, nextEruption, looseSince;
    private bool jetting, erupted;
    private Vector3 jetDirection, visualScale;
    private Transform visual;
    private Rigidbody playerBody;
    private SmoothFirstPersonController player;

    private static Material steamMaterial;
    private static Texture2D puffTexture;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>0..1: how full the vent is.</summary>
    public float Fill => fill;
    /// <summary>Set down on the ground (not carried, not falling).</summary>
    public bool Settled => state == VentState.Set;
    public string Prompt => source != null && source.Heat > 0f ? "[LMB hold] Steam jet" : null;

    private void Awake()
    {
        source = GetComponent<FireSource>();
        body = GetComponent<Rigidbody>();
        source.CarriedUse = this;
        phase = Random.value;
        nextEruption = Time.time + Random.Range(eruptEvery.x, eruptEvery.y);
        visual = transform.Find("Visual");
        if (visual != null) visualScale = visual.localScale;
        steam = BuildSteam();
        // Rest on the ground under it (vents placed for the old crater sat a little higher).
        Settle(GroundBelow(out RaycastHit ground) && ground.distance < 2f ? ground.point : transform.position - Vector3.up * restHeight);
    }

    private void Update()
    {
        if (source == null || source.IsDead) return;
        float dt = Time.deltaTime;
        UpdateState();

        if (state == VentState.Set)
        {
            // The ground breathes: a slow swell of heat, then a lull.
            phase += dt / breathPeriod;
            float breath = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            flow = Mathf.Lerp(groundFlow.x, groundFlow.y, breath);
            float room = maxHeat - source.Heat;
            if (room > 0f) source.AddHeat(Mathf.Min(room, flow * dt));
        }
        else flow = 0f;
        fill = Mathf.Clamp01(source.Heat / maxHeat);

        UpdateSteam();
        if (state == VentState.Set && fill >= eruptAbove && Time.time >= nextEruption)
        {
            nextEruption = Time.time + Random.Range(eruptEvery.x, eruptEvery.y);
            Erupt();
        }
        UpdateJet(dt);
        if (visual != null)
        {
            Vector3 target = visualScale * (source.IsCarried ? carriedScale : 1f);
            visual.localScale = Vector3.Lerp(visual.localScale, target, 1f - Mathf.Exp(-12f * dt));
        }
    }

    private void FixedUpdate()
    {
        if (source == null || source.IsDead) return;
        if (state == VentState.Loose) TrySettle();
        FindPlayer();
        if (playerBody == null || playerBody.isKinematic) return;

        if (jetting && source.IsCarried)
        {
            // The jet pushes back on the hands holding it.
            player.Fling(playerBody.linearVelocity - jetDirection * jetThrust * Time.fixedDeltaTime, 0.25f);
        }
        else if (state == VentState.Set) LiftPlayer(Time.fixedDeltaTime);
        erupted = false;
    }

    // ---------------- Being carried, put down and set ----------------

    private void UpdateState()
    {
        if (source.IsCarried)
        {
            state = VentState.Carried;
            return;
        }
        if (state == VentState.Carried)
        {
            // Let go: it drops (or flies, if thrown) and settles where it lands.
            state = VentState.Loose;
            looseSince = Time.time;
        }
        if (state == VentState.Loose && !CreatureThrower.IsFlying(source) && body != null && body.isKinematic)
        {
            source.BeginFalling(Vector3.zero);
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void TrySettle()
    {
        if (body == null || CreatureThrower.IsFlying(source) || body.isKinematic) return;
        if (Time.time - looseSince < 0.2f || body.linearVelocity.sqrMagnitude > 1.5f) return;
        if (!GroundBelow(out RaycastHit hit) || hit.distance > restHeight + 0.6f) return;
        Settle(hit.point);
    }

    private void Settle(Vector3 ground)
    {
        state = VentState.Set;
        if (body != null)
        {
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
            body.useGravity = false;
        }
        transform.SetPositionAndRotation(ground + Vector3.up * restHeight, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
    }

    private bool GroundBelow(out RaycastHit ground)
    {
        ground = default;
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 0.2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform) || (playerBody != null && hit.rigidbody == playerBody)) continue;
            if (hit.distance < best) { best = hit.distance; ground = hit; }
        }
        if (best == float.MaxValue) return false;
        ground.distance -= 0.2f;
        return true;
    }

    // ---------------- Player ----------------

    private void FindPlayer()
    {
        if (player != null) return;
        player = FindFirstObjectByType<SmoothFirstPersonController>();
        if (player != null) playerBody = player.GetComponent<Rigidbody>();
    }

    /// <summary>Standing in the plume lifts the player, slower towards its top, so they hover there.</summary>
    private void LiftPlayer(float dt)
    {
        Vector3 offset = playerBody.position - transform.position;
        float height = offset.y;
        offset.y = 0f;
        float top = Mathf.Lerp(liftHeight.x, liftHeight.y, fill);
        if (offset.sqrMagnitude > liftRadius * liftRadius || height < -1.5f || height > top) return;
        Vector3 velocity = playerBody.linearVelocity;
        if (erupted)
        {
            player.Fling(new Vector3(velocity.x, Mathf.Max(velocity.y, eruptionThrow), velocity.z), 0.5f);
            return;
        }
        float target = liftSpeed * Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Max(0f, height) / top));
        if (velocity.y >= target) return;
        // A kick first, like a jump: on the ground (and on the slope of the pit) the walking controller holds
        // the body down, so a gentle push never gets it off.
        velocity.y = Mathf.Max(velocity.y, Mathf.Min(target, 4f));
        velocity.y = Mathf.MoveTowards(velocity.y, target, liftAcceleration * dt);
        player.Fling(velocity, 0.15f);
    }

    // ---------------- Steam ----------------

    private void UpdateSteam()
    {
        if (steam == null) return;
        var emission = steam.emission;
        emission.rateOverTime = state == VentState.Set ? Mathf.Lerp(steamRate.x, steamRate.y, fill) : 0f;
        var main = steam.main;
        float speed = Mathf.Lerp(steamSpeed.x, steamSpeed.y, fill);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed * 1.2f);
    }

    /// <summary>Carried, left click held: a hard stream of steam along the view, paid for with heat.</summary>
    private void UpdateJet(float dt)
    {
        jetting = false;
        if (!source.IsCarried || source.Heat <= 0f || Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
        Camera view = Camera.main;
        jetDirection = view != null ? view.transform.forward : transform.forward;
        source.DrainHeat(jetHeatPerSecond * dt);
        jetting = true;
        if (steam == null) return;
        var parameters = new ParticleSystem.EmitParams();
        int count = Mathf.Max(1, Mathf.RoundToInt(90f * dt));
        for (int i = 0; i < count; i++)
        {
            Vector3 spread = Random.insideUnitSphere * 0.12f;
            parameters.position = transform.position + jetDirection * 0.7f + spread;
            parameters.velocity = (jetDirection + spread * 1.5f).normalized * jetSpeed * Random.Range(0.8f, 1.15f);
            parameters.startSize = Random.Range(0.35f, 0.7f);
            parameters.startLifetime = Random.Range(0.7f, 1.2f);
            steam.Emit(parameters, 1);
        }
    }

    /// <summary>A short, fast burst straight up out of the vent.</summary>
    private void Erupt()
    {
        erupted = true;
        if (steam == null) return;
        var parameters = new ParticleSystem.EmitParams();
        for (int i = 0; i < 45; i++)
        {
            Vector2 spread = Random.insideUnitCircle * 0.6f;
            parameters.position = transform.position + new Vector3(spread.x * craterRadius * 0.4f, 0.2f, spread.y * craterRadius * 0.4f);
            parameters.velocity = new Vector3(spread.x * 1.2f, Random.Range(6f, 11f), spread.y * 1.2f);
            parameters.startSize = Random.Range(0.8f, 1.6f);
            parameters.startLifetime = Random.Range(2.2f, 3.6f);
            steam.Emit(parameters, 1);
        }
    }

    private ParticleSystem BuildSteam()
    {
        var go = new GameObject("Steam");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.4f; // off the top of the body
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.04f; // warm steam keeps rising
        main.prewarm = true;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = craterRadius * 0.45f;

        // Drifts and slows as it cools, so the plume leans and spreads at the top.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.45f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.3f);
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1.2f;
        limit.dampen = 0.08f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.25f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1.6f), new Keyframe(1f, 3.2f)));

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        // Lit orange by the vent at the bottom, then pale grey steam that fades out.
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.5f, 0.22f), 0f), new GradientColorKey(new Color(0.95f, 0.82f, 0.72f), 0.18f),
                    new GradientColorKey(new Color(0.86f, 0.84f, 0.82f), 0.6f), new GradientColorKey(new Color(0.75f, 0.74f, 0.74f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.08f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = SteamMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 3f;
        ps.Play();
        return ps;
    }

    /// <summary>Soft round puffs, alpha-blended, shared by every vent.</summary>
    internal static Material SteamMaterial()
    {
        if (steamMaterial != null) return steamMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;
        if (puffTexture == null)
        {
            const int size = 64;
            puffTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Steam Puff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(t * t * (3f - 2f * t) * 255f));
            }
            puffTexture.SetPixels32(pixels);
            puffTexture.Apply();
        }
        steamMaterial = new Material(shader) { name = "Geothermal Steam (runtime)" };
        steamMaterial.SetTexture("_BaseMap", puffTexture);
        steamMaterial.SetColor(BaseColorId, Color.white);
        steamMaterial.SetFloat("_Surface", 1f);
        steamMaterial.SetFloat("_Blend", 0f);
        steamMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        steamMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        steamMaterial.SetFloat("_ZWrite", 0f);
        steamMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        steamMaterial.renderQueue = (int)RenderQueue.Transparent;
        return steamMaterial;
    }

    private void OnValidate()
    {
        groundFlow = new Vector2(Mathf.Max(0f, groundFlow.x), Mathf.Max(groundFlow.x, groundFlow.y));
        steamRate = new Vector2(Mathf.Max(0f, steamRate.x), Mathf.Max(steamRate.x, steamRate.y));
        steamSpeed = new Vector2(Mathf.Max(0f, steamSpeed.x), Mathf.Max(steamSpeed.x, steamSpeed.y));
        eruptEvery = new Vector2(Mathf.Max(1f, eruptEvery.x), Mathf.Max(eruptEvery.x, eruptEvery.y));
        liftHeight = new Vector2(Mathf.Max(0.5f, liftHeight.x), Mathf.Max(liftHeight.x, liftHeight.y));
    }
}
