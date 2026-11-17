using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public sealed class FurnaceCreature : Creature, IRideSurface
{
    public FurnaceRecipe recipe;
    public Collider ember;
    public Renderer globe, emberSkin;
    public Transform shelf;
    public AudioSource breath;

    const float Radius = 5f, Rim = 4.4f, Depth = 2f, Capacity = 100f;
    readonly List<Piece> batch = new List<Piece>();
    readonly List<Creature> candidates = new List<Creature>();
    readonly List<Vector3> positions = new List<Vector3>();
    MaterialPropertyBlock properties;
    Rigidbody body;
    Vector3 home;
    float phase, peak, lastBoarded;
    bool firing;
    int feedFrame = -5;

    sealed class Piece
    {
        public Creature item;
        public Transform display;
    }

    public override bool CanBeCarried => false;
    public override bool CanBeCaptured => false;
    public override bool CanBeConsumed => false;
    public override bool Invulnerable => true;

    protected override void InitializeCreature()
    {
        SetCreatureName("Furnace Creature");
        SetResolvedPolarity(CreaturePolarity.None);
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        home = body.position;
        OnHeatChanged();
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public bool Stoke(Creature pending)
    {
        if (!isActiveAndEnabled || recipe == null || recipe.output == null || IsDead) return false;
        if (!firing)
        {
            Capture(pending);
            firing = true;
            peak = 0;
        }
        Feed(14f);
        Breathe(1.1f);
        return true;
    }

    public void Sustain(float dt)
    {
        if (firing) Feed(32f * dt);
    }

    void Feed(float amount)
    {
        feedFrame = Time.frameCount;
        SetHeat(Mathf.Min(Capacity, Heat + amount));
        peak = Mathf.Max(peak, Heat / Capacity);
    }

    void Capture(Creature pending)
    {
        CreatureRegistry.SnapshotFood(gameObject.scene, true, candidates, positions);
        for (int i = 0; i < candidates.Count; i++)
        {
            Creature item = candidates[i];
            if (!recipe.Accepts(item.IngredientKind) || item.IsCarried || item == pending ||
                !item.IsInPhysicalWorld || item.IsBubbleCaptured || BoneChild.IsCradled(item) ||
                ReflectionSpotlight.IsFrozen(item) ||
                (item.TryGetComponent<WebCapture>(out var web) && web.IsCaptured) ||
                (item.TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim)) continue;
            Vector3 local = transform.InverseTransformPoint(item.AimPosition);
            float radial = (local.x * local.x + local.z * local.z) / (Radius * Radius);
            if (radial >= 1 || local.y > Rim || local.y < Rim - Depth * Mathf.Sqrt(1 - radial) - 0.05f) continue;
            CreatureThrower.ReleaseFlight(item);
            // Retain the original, inactive, so no pickup, feeding or lifespan system can take it.
            // Only its visible meshes participate in the inhale.
            Transform display = new GameObject("Firing " + item.CreatureName).transform;
            display.SetParent(transform, false);
            display.SetPositionAndRotation(item.transform.position, item.transform.rotation);
            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.TryGetComponent<MeshRenderer>(out var source) || !source.enabled) continue;
                var part = new GameObject(filter.name);
                part.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                part.transform.localScale = filter.transform.lossyScale;
                part.transform.SetParent(display, true);
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            }
            item.gameObject.SetActive(false);
            batch.Add(new Piece { item = item, display = display });
        }
    }

    protected override void TickCreature(float dt)
    {
        if (!firing) return;
        if (Time.frameCount - feedFrame > 1) SetHeat(Mathf.Max(0, Heat - 14f * dt));
        for (int i = 0; i < batch.Count; i++)
        {
            float angle = i * 2.4f;
            float radius = Mathf.Min(3f, 0.48f * Mathf.Sqrt(i));
            float twitch = peak >= recipe.requiredPeak ? 0.12f * Mathf.Abs(Mathf.Sin(Time.time * 8 + i)) : 0;
            Vector3 nest = new Vector3(Mathf.Cos(angle) * radius, Rim - Depth * Mathf.Sqrt(1-radius*radius/25) + 0.18f + twitch, Mathf.Sin(angle) * radius);
            batch[i].display.localPosition = Vector3.MoveTowards(batch[i].display.localPosition, nest, dt * 3.5f);
        }
        if (Heat <= 0) Finish();
    }

    protected override void OnHeatChanged()
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        properties.SetFloat("_Fill", Mathf.Clamp01(Heat / Capacity));
        if (globe != null) globe.SetPropertyBlock(properties);
        if (emberSkin != null)
        {
            properties.SetFloat("_VisibleHeight", 1f);
            emberSkin.SetPropertyBlock(properties);
            properties.Clear();
        }
    }

    void Breathe(float pitch)
    {
        if (breath == null || breath.clip == null) return;
        breath.pitch = pitch;
        breath.Play();
    }

    void Finish()
    {
        firing = false;
        var counts = new Dictionary<FurnaceIngredient, int>();
        foreach (Piece part in batch)
            if (part.item != null) counts[part.item.IngredientKind] = counts.GetValueOrDefault(part.item.IngredientKind) + 1;
        int sets = recipe.CompleteSets(counts);
        bool react = peak >= recipe.requiredPeak && recipe.output != null;
        var exits = new List<Vector3>();
        int outputs = react ? sets * recipe.outputPerSet : 0;
        // A player or late ingredient can occupy a launch point. Search the rest of
        // the opening before giving the intact batch back on a fully obstructed exit.
        for (int slot = 0; exits.Count < outputs && slot < outputs + 75; slot++)
        {
            Vector3 local = new Vector3(slot % 5 - 2, Rim + 0.9f + slot / 25, slot / 5 % 5 - 2);
            Vector3 pos = transform.TransformPoint(local);
            if (gameObject.scene.GetPhysicsScene().OverlapSphere(pos, 0.48f, clearance, ~0, QueryTriggerInteraction.Ignore) == 0)
                exits.Add(pos);
        }
        react &= exits.Count == outputs;
        if (react)
            foreach (Vector3 pos in exits)
            {
                Creature bird = Instantiate(recipe.output, pos, transform.rotation, transform);
                bird.transform.SetParent(null, true);
            }
        counts.Clear();
        int returned = 0;
        foreach (Piece part in batch)
        {
            if (part.item != null)
            {
                FurnaceIngredient kind = part.item.IngredientKind;
                int used = counts.GetValueOrDefault(kind);
                if (react && used < recipe.ConsumptionLimit(kind, sets))
                { Destroy(part.item.gameObject); counts[kind] = used + 1; }
                else
                {
                    Vector3 offset = new Vector3((returned % 3 - 1) * 0.5f, 0.35f + returned / 6 * 0.55f, returned / 3 % 2 * 0.45f);
                    Restore(part.item, shelf.TransformPoint(offset));
                    returned++;
                }
            }
            Destroy(part.display.gameObject);
        }
        batch.Clear();
        Breathe(0.68f);
    }

    readonly Collider[] clearance = new Collider[1];
    static void Restore(Creature item, Vector3 pos)
    {
        item.transform.position = pos;
        item.gameObject.SetActive(true);
        if (item.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
        { rb.position = pos; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
    }

    protected override void OnDisable()
    {
        firing = false;
        foreach (Piece part in batch)
        {
            if (part.item != null) Restore(part.item, part.item.transform.position);
            if (part.display != null) Destroy(part.display.gameObject);
        }
        batch.Clear();
        SetHeat(0);
        base.OnDisable();
    }

    public bool Supports(Collider surface) => surface != null && surface.attachedRigidbody == body && surface != ember;

    public Vector3 VelocityAt(Vector3 pointWS)
    {
        // The controller also probes when its stationary rigidbody has gone to sleep.
        lastBoarded = Time.fixedTime;
        return Vector3.zero;
    }

    void OnCollisionStay(Collision contact) => CheckBoarding(contact);
    void OnCollisionEnter(Collision contact) => CheckBoarding(contact);
    void CheckBoarding(Collision contact)
    {
        if (contact.collider.GetComponentInParent<SmoothFirstPersonController>() != null)
            lastBoarded = Time.fixedTime;
    }

    void FixedUpdate()
    {
        if (!SimulationEnabled || firing || Time.fixedTime - lastBoarded < 0.35f) return;
        phase += Time.fixedDeltaTime * 0.095f;
        body.MovePosition(home + transform.right * (2.4f * Mathf.Sin(phase)) +
            transform.forward * (0.7f * (1 - Mathf.Cos(phase))));
    }
}
