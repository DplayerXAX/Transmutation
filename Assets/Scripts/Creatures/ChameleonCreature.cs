using UnityEngine;

/// <summary>A portable material sampler. Sources are untouched; only the player can borrow its appearance.</summary>
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class ChameleonCreature : Creature
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Renderer[] skinRenderers;
    [Min(0.1f)] [SerializeField] private float sampleRadius = 5f;
    [SerializeField] private Vector2 sampleInterval = new Vector2(60f, 120f);
    private readonly SurfaceMaterialResolver resolver = new SurfaceMaterialResolver();
    private Material currentMaterial;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private float sampleRemaining;

    public Material CurrentMaterial => currentMaterial != null ? currentMaterial :
        skinRenderers != null && skinRenderers.Length > 0 && skinRenderers[0] != null ? skinRenderers[0].sharedMaterial : null;
    public override bool CanBeCarried => !IsDead;
    public override bool CanUseWhileCarried => !IsDead;
    public override bool HeldInView => true;
    public override string InteractionPrompt => IsDead ? null : "Borrow Appearance";
    public override string CarriedPrompt => IsDead ? null : "[LMB] Copy Aimed Material";

    protected override void InitializeCreature()
    {
        SetCreatureName("Chameleon");
        if (visualRoot != null) { restPosition = visualRoot.localPosition; restRotation = visualRoot.localRotation; }
        ResetSampleTimer();
    }

    private void Start()
    {
        if (SimulationEnabled && !IsCarried && !IsBubbleCaptured) SampleNearby();
    }

    private void ResetSampleTimer()
    {
        float minimum = Mathf.Max(0.1f, sampleInterval.x);
        sampleRemaining = Random.Range(minimum, Mathf.Max(minimum, sampleInterval.y));
    }

    protected override void OnCarryStarted() => ResetSampleTimer();
    protected override void OnCaptureStarted() => ResetSampleTimer();

    protected override void TickCreature(float deltaTime)
    {
        if (visualRoot != null)
        {
            float phase = Time.time * 1.7f + GetInstanceID();
            visualRoot.localPosition = restPosition + Vector3.up * (Mathf.Sin(phase) * 0.006f);
            visualRoot.localRotation = restRotation * Quaternion.Euler(0f, Mathf.Sin(phase * 0.55f) * 2f, 0f);
        }
        if (IsCarried || IsBubbleCaptured) return;
        sampleRemaining -= deltaTime;
        if (sampleRemaining > 0f) return;
        SampleNearby();
        ResetSampleTimer();
    }

    private void SampleNearby()
    {
        if (resolver.TryNearby(gameObject.scene, transform.position, sampleRadius, this, out Material source))
            CopyMaterial(source);
    }

    private void CopyMaterial(Material source)
    {
        if (source == null || skinRenderers == null || skinRenderers.Length == 0) return;
        var snapshot = new Material(source) { name = source.name + " (Chameleon Mimic)", hideFlags = HideFlags.DontSave };
        foreach (Renderer part in skinRenderers)
        {
            if (part == null) continue;
            part.sharedMaterial = snapshot;
            part.SetPropertyBlock(null);
        }
        if (currentMaterial != null) Destroy(currentMaterial);
        currentMaterial = snapshot;
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead || IsConsumptionPending || IsBubbleCaptured || !isActiveAndEnabled || !SimulationEnabled ||
            interaction.Source == null || interaction.Source.scene != gameObject.scene) return;
        var interactor = interaction.Source.GetComponent<PlayerCreatureInteractor>();
        if (interactor == null) interactor = interaction.Source.GetComponentInParent<PlayerCreatureInteractor>();
        if (interactor == null) interactor = interaction.Source.transform.root.GetComponentInChildren<PlayerCreatureInteractor>();
        if (IsCarried)
        {
            if (interactor == null || Carrier == null ||
                interactor.transform.root != Carrier.transform.root) return;
            if (interactor.TryGetSurfaceHit(out RaycastHit hit) && resolver.TryFromHit(hit, out Material source))
                CopyMaterial(source);
            return;
        }
        FirstPersonBody playerBody = interaction.Source.transform.root.GetComponentInChildren<FirstPersonBody>();
        if (playerBody != null) playerBody.ApplyMimicMaterial(CurrentMaterial);
    }

    private void OnDestroy()
    {
        if (currentMaterial != null) Destroy(currentMaterial);
    }
}
