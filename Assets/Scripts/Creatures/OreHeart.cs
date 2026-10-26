using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Ore Colossus's weak point: a crystal heart hanging on a stone chain under its belly. It swings as
/// the colossus walks and only erosive water wears it away (an ErosionTarget on its sphere collider).
/// It reports thresholds and breaking to its colossus, which acts on them in its own Tick.
/// Not a Creature: hits on it resolve to the colossus, which is invulnerable.
/// </summary>
[DisallowMultipleComponent]
public sealed class OreHeart : MonoBehaviour
{
    [Tooltip("Erosion the heart takes before it shatters.")]
    [Min(0.1f)] [SerializeField] private float maxIntegrity = 10f;
    [Tooltip("Shares of integrity left at which the colossus staggers.")]
    [SerializeField] private float[] staggerThresholds = { 0.75f, 0.5f, 0.25f };
    [Tooltip("Velocity damping of the swing, per second.")]
    [Min(0f)] [SerializeField] private float damping = 0.35f;
    [Tooltip("Largest swing away from hanging straight down, in degrees.")]
    [Range(5f, 60f)] [SerializeField] private float maxSwing = 25f;

    [Header("Runtime (Read Only)")]
    [SerializeField] private float integrity;
    [SerializeField] private bool broken;

    private OreColossus owner;
    private ErosionTarget target;
    private SphereCollider sphere;
    private Renderer crystal;
    private Transform chain;
    private readonly List<Transform> links = new List<Transform>();
    private float chainLength, radius, flash, wakeTimer;
    private int nextThreshold;
    private Vector3 position, velocity;
    private ParticleSystem chips;
    private MaterialPropertyBlock block;
    private Color baseEmission = Color.black, baseColor = Color.white;
    private static readonly Collider[] nearby = new Collider[32];
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public float Integrity => integrity;
    public float MaxIntegrity => maxIntegrity;
    public float Integrity01 => maxIntegrity > 0f ? Mathf.Clamp01(integrity / maxIntegrity) : 0f;
    public bool Broken => broken;
    public ErosionTarget Target => target;
    public SphereCollider Collider => sphere;
    public float Radius => radius;
    /// <summary>Raised with the amount taken each time water wears the heart.</summary>
    public event System.Action<OreHeart, float> Eroded;

    /// <summary>Builds the heart and its chain. The heart object must still be inactive.</summary>
    internal void Initialize(OreColossus colossus, Transform chainParent, float length, float heartRadius, float integrityMax,
        float sensitivity, Mesh heartMesh, Mesh linkMesh, Material heartMaterial, Material chainMaterial)
    {
        owner = colossus; chainLength = length; radius = heartRadius;
        maxIntegrity = integrityMax; integrity = maxIntegrity; nextThreshold = 0; broken = false;
        gameObject.AddComponent<MeshFilter>().sharedMesh = heartMesh;
        crystal = gameObject.AddComponent<MeshRenderer>();
        crystal.sharedMaterials = Materials(heartMesh, heartMaterial, heartMaterial);
        sphere = gameObject.AddComponent<SphereCollider>();
        sphere.radius = radius;
        // A swinging heart must not shove the player around; water and creatures still meet it.
        sphere.excludeLayers = 1 << 6;
        target = gameObject.AddComponent<ErosionTarget>();
        target.Configure(new Collider[] { sphere }, sensitivity, 1f, 0.9f, 0.15f);
        target.Eroded += OnTargetEroded;
        if (heartMaterial != null)
        {
            if (heartMaterial.HasProperty(EmissionId)) baseEmission = heartMaterial.GetColor(EmissionId);
            if (heartMaterial.HasProperty(BaseColorId)) baseColor = heartMaterial.GetColor(BaseColorId);
        }
        block = new MaterialPropertyBlock();

        chain = chainParent;
        int count = Mathf.Max(2, Mathf.CeilToInt(chainLength / 0.55f));
        for (int i = 0; i < count; i++)
        {
            var link = new GameObject("Chain Link " + i).transform;
            link.SetParent(chain, false);
            link.gameObject.AddComponent<MeshFilter>().sharedMesh = linkMesh;
            var renderer = link.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = chainMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            links.Add(link);
        }

        var particles = new GameObject("Heart Chips").transform;
        particles.SetParent(transform, false);
        chips = particles.gameObject.AddComponent<ParticleSystem>();
        var main = chips.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96; main.startLifetime = 0.8f; main.startSize = 0.07f; main.startSpeed = 1.2f; main.gravityModifier = 1f;
        main.startColor = baseEmission.maxColorComponent > 0.01f ? (Color)baseEmission : new Color(0.55f, 0.9f, 0.85f);
        var emission = chips.emission; emission.enabled = false;
        var shape = chips.shape; shape.enabled = false;
        var particleRenderer = chips.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = heartMaterial;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material[] Materials(Mesh mesh, Material a, Material b) => mesh != null && mesh.subMeshCount > 1 ? new[] { a, b } : new[] { a };

    /// <summary>Places the heart hanging still below the anchor.</summary>
    internal void Hang(Vector3 anchor, float groundY)
    {
        position = anchor + Vector3.down * chainLength;
        position.y = Mathf.Max(position.y, groundY + radius);
        velocity = Vector3.zero;
        Apply(anchor);
    }

    /// <summary>One frame of the swing under a moving anchor, kept off the ground.</summary>
    internal void Simulate(float deltaTime, Vector3 anchor, float groundY)
    {
        if (broken || deltaTime <= 0f) return;
        int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * 60f), 1, 8);
        float h = deltaTime / steps;
        float cosLimit = Mathf.Cos(maxSwing * Mathf.Deg2Rad);
        for (int s = 0; s < steps; s++)
        {
            velocity += Physics.gravity * h;
            velocity *= Mathf.Max(0f, 1f - damping * h);
            position += velocity * h;
            Vector3 offset = position - anchor;
            float distance = offset.magnitude;
            if (distance > chainLength && distance > 1e-4f)
            {
                Vector3 dir = offset / distance;
                position = anchor + dir * chainLength;
                float outward = Vector3.Dot(velocity, dir);
                if (outward > 0f) velocity -= dir * outward;
                offset = position - anchor; distance = chainLength;
            }
            if (distance > 1e-4f && Vector3.Dot(offset / distance, Vector3.down) < cosLimit)
            {
                Vector3 clamped = Vector3.RotateTowards(Vector3.down, offset / distance, maxSwing * Mathf.Deg2Rad, 0f);
                position = anchor + clamped * distance;
                velocity *= 0.5f;
            }
            // Resting on the ground the chain goes slack.
            if (position.y < groundY + radius) { position.y = groundY + radius; if (velocity.y < 0f) velocity.y = 0f; velocity *= 1f - Mathf.Min(1f, 4f * h); }
        }
        Apply(anchor);
        flash = Mathf.MoveTowards(flash, 0f, deltaTime * 3f);
        UpdateGlow();
        // Teleported kinematic contacts do not wake sleeping bodies; water resting on the heart must keep eroding.
        wakeTimer -= deltaTime;
        if (wakeTimer <= 0f)
        {
            wakeTimer = 0.1f;
            int count = gameObject.scene.GetPhysicsScene().OverlapSphere(position, radius + 0.6f, nearby, 1 << 4, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (nearby[i].attachedRigidbody != null && nearby[i].attachedRigidbody.IsSleeping()) nearby[i].attachedRigidbody.WakeUp();
        }
    }

    /// <summary>A sideways shove, e.g. when a foot lands.</summary>
    internal void Kick(Vector3 deltaVelocity) { if (!broken) velocity += deltaVelocity; }

    private void Apply(Vector3 anchor)
    {
        Vector3 offset = position - anchor;
        Vector3 down = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.down;
        transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.down, down) * Quaternion.Euler(0f, Time.time * 9f, 0f));
        float distance = offset.magnitude;
        float slack = Mathf.Max(0f, chainLength - distance);
        Vector3 end = position - down * radius * 0.85f;
        // Taut it hangs straight; slack it sags into a curve.
        Vector3 control = (anchor + end) * 0.5f + Vector3.down * slack * 0.6f;
        for (int i = 0; i < links.Count; i++)
        {
            float t = (i + 0.5f) / links.Count;
            Vector3 p = Bezier(anchor, control, end, t);
            Vector3 tangent = Bezier(anchor, control, end, Mathf.Min(1f, t + 0.02f)) - Bezier(anchor, control, end, Mathf.Max(0f, t - 0.02f));
            if (tangent.sqrMagnitude < 1e-8f) tangent = down;
            links[i].SetPositionAndRotation(p, Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f));
        }
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

    private void OnTargetEroded(ErosionTarget source, Vector3 point, float amount)
    {
        if (broken || amount <= 0f) return;
        integrity = Mathf.Max(0f, integrity - amount);
        flash = Mathf.Min(1f, flash + amount * 2f + 0.15f);
        if (chips != null)
        {
            Vector3 normal = (point - transform.position).sqrMagnitude > 1e-6f ? (point - transform.position).normalized : Vector3.up;
            var emit = new ParticleSystem.EmitParams { position = point, velocity = normal * 1.2f + Random.insideUnitSphere * 0.5f };
            if (!chips.isPlaying) chips.Play();
            chips.Emit(emit, Mathf.Clamp(Mathf.CeilToInt(amount * 12f), 1, 6));
        }
        Eroded?.Invoke(this, amount);
        // Erosion arrives inside the fluid system's physics loop: only report, the colossus acts in its Tick.
        while (nextThreshold < staggerThresholds.Length && Integrity01 <= staggerThresholds[nextThreshold])
        {
            nextThreshold++;
            if (integrity > 0f && owner != null) owner.OnHeartThreshold();
        }
        if (integrity <= 0f && owner != null) owner.OnHeartBroken();
    }

    /// <summary>Debug and validation: wears the heart down to nothing.</summary>
    internal void ForceBreak()
    {
        if (broken) return;
        integrity = 0f;
        nextThreshold = staggerThresholds.Length;
        if (owner != null) owner.OnHeartBroken();
    }

    private void UpdateGlow()
    {
        if (crystal == null) return;
        float health = Integrity01;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 1.5f, health));
        crystal.GetPropertyBlock(block);
        block.SetColor(EmissionId, baseEmission * (Mathf.Lerp(0.25f, 1f, health) * pulse + flash * 2.5f));
        block.SetColor(BaseColorId, Color.Lerp(baseColor * 0.35f, baseColor, health));
        crystal.SetPropertyBlock(block);
    }

    /// <summary>Bursts into shards and drops the broken chain; the heart stops taking water.</summary>
    internal void Shatter(Mesh shardMesh, Material shardMaterial, Transform belly)
    {
        if (broken) return;
        broken = true;
        integrity = 0f;
        Vector3 centre = transform.position;
        var random = new System.Random(GetInstanceID());
        for (int i = 0; i < 9; i++)
        {
            var shard = new GameObject("Heart Shard");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shard, gameObject.scene);
            Vector3 dir = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 1.2f, (float)random.NextDouble() * 2f - 1f).normalized;
            shard.transform.SetPositionAndRotation(centre + dir * radius * 0.5f, Quaternion.LookRotation(dir));
            shard.transform.localScale = Vector3.one * (0.35f + (float)random.NextDouble() * 0.3f);
            shard.AddComponent<MeshFilter>().sharedMesh = shardMesh;
            shard.AddComponent<MeshRenderer>().sharedMaterial = shardMaterial;
            var collider = shard.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.4f, 0.4f, 0.8f);
            collider.excludeLayers = 1 << 6;
            var body = shard.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.linearVelocity = dir * (3f + (float)random.NextDouble() * 3f) + Vector3.up * 2f;
            body.angularVelocity = Random.insideUnitSphere * 6f;
            Destroy(shard, 7f);
        }
        if (chips != null)
        {
            chips.transform.SetParent(null, true);
            chips.transform.position = centre;
            if (!chips.isPlaying) chips.Play();
            chips.Emit(40);
            Destroy(chips.gameObject, 2f);
        }
        // Two links stay dangling from the belly and move with it.
        for (int i = 0; i < links.Count; i++)
        {
            if (i < 2 && belly != null) links[i].SetParent(belly, true);
            else links[i].gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }

    private void OnDestroy() { if (target != null) target.Eroded -= OnTargetEroded; }
}
