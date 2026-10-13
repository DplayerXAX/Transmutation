using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A stationary Creature with bounded procedural silk and persistent physics captures.</summary>
public sealed class SkyWeb : Creature
{
    [Header("Shape")]
    [Min(0.2f)] [SerializeField] private float maximumRadius = 6f;
    [Tooltip("Uniform size multiplier for newly generated webs, including strands, patches, patrol and trapping. 0.5 = half size, 2 = double size.")]
    [Min(0.05f)] [SerializeField] private float webScale = 1f;
    [Range(1, 24)] [SerializeField] private int cellCount = 7;
    [Range(32, 1024)] [SerializeField] private int maximumStrands = 256;
    [Range(0, 24)] [SerializeField] private int maximumPatches = 6;
    [SerializeField] private int seed = 37;
    [Min(0.001f)] [SerializeField] private float supportThickness = 0.035f;
    [Min(0.001f)] [SerializeField] private float silkThickness = 0.008f;
    [Tooltip("Distance of outer D12 clusters from the web center, as a fraction of maximum radius.")]
    [Range(0.3f, 0.85f)] [SerializeField] private float clusterSpacing = 0.72f;
    [Tooltip("Spacing within each cluster, as a fraction of maximum radius.")]
    [Range(0.02f, 0.35f)] [SerializeField] private float clusterSpread = 0.18f;
    [Tooltip("D12 cage radius as a fraction of maximum radius, before Web Scale.")]
    [Range(0.06f, 0.3f)] [SerializeField] private float cellSize = 0.14f;
    [Tooltip("Long individual threads extending from each cage. Limited by the strand budget.")]
    [Range(0, 6)] [SerializeField] private int looseStrandsPerCell = 3;

    [Header("Growth")]
    [Min(0.01f)] [SerializeField] private float growthDuration = 45f;

    [Header("Rendering")]
    [SerializeField] private Material strandMaterial;
    [SerializeField] private Material patchMaterial;

    [Header("Trapping")]
    [SerializeField] private LayerMask trappingLayers = ~0;
    [Tooltip("Extra contact tolerance around a strand. Objects are approximated by their collider bounds.")]
    [Min(0f)] [SerializeField] private float contactPadding = 0.025f;

    public override bool CanBeCarried => false;
    public float MaximumRadius => geometry == null ? Mathf.Max(0.2f, maximumRadius) * Mathf.Max(0.05f, webScale) : geometry.MaximumRadius;
    public float Growth => geometry == null ? 0f : (float)geometry.RenderedStrandCount / geometry.StrandCount;
    public int StrandCount => geometry == null ? 0 : geometry.RenderedStrandCount;
    public int PatchCount => geometry == null ? 0 : geometry.RenderedPatchCount;
    public int MeshRevision => geometry == null ? 0 : geometry.MeshRevision;
    public int CaptureCount => captures.Count;
    public bool IsFullyGrown => geometry != null && StrandCount == geometry.StrandCount;
    public SkyWebGeometry Geometry => geometry;
    public event Action<WebCapture> Captured;

    private SkySpider builder;
    private SkyWebGeometry geometry;
    private Mesh strandMesh;
    private Mesh patchMesh;
    private float growthTime;
    private Collider[] overlapBuffer = new Collider[96];
    private readonly List<WebCapture> captures = new List<WebCapture>();
    private readonly Dictionary<Rigidbody, Bounds> bodyBounds = new Dictionary<Rigidbody, Bounds>(96);
    private Dictionary<Rigidbody, Vector3> previousCenters = new Dictionary<Rigidbody, Vector3>(96);
    private Dictionary<Rigidbody, Vector3> currentCenters = new Dictionary<Rigidbody, Vector3>(96);

    protected override void InitializeCreature()
    {
        // Prefabs are configured before their first play-mode initialization.
        if (!Application.isPlaying) return;
        InitializeGeometry();
    }

    public void InitializeGeometry()
    {
        if (geometry != null) return;
        maximumRadius = Mathf.Max(0.2f, maximumRadius);
        geometry = new SkyWebGeometry(seed, maximumRadius, cellCount, maximumStrands,
            maximumPatches, supportThickness, silkThickness, clusterSpacing, clusterSpread,
            cellSize, looseStrandsPerCell, webScale);
        strandMesh = CreateMesh("Sky Web Strands", strandMaterial);
        patchMesh = CreateMesh("Sky Web Patches", patchMaterial);
        geometry.Render(strandMesh, patchMesh, geometry.InitialStrandCount);
    }

    private Mesh CreateMesh(string meshName, Material material)
    {
        var child = new GameObject(meshName);
        child.transform.SetParent(transform, false);
        var mesh = new Mesh { name = meshName };
        mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return mesh;
    }

    public void SetBuilder(SkySpider spider)
    {
        builder = spider;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (builder == null || !builder.isActiveAndEnabled || !builder.SimulationEnabled ||
            builder.IsCarried || builder.CurrentWeb != this || builder.IsReturning) return;
        AdvanceGrowth(deltaTime);
    }

    public void AdvanceGrowth(float deltaTime)
    {
        InitializeGeometry();
        if (!SimulationEnabled || IsFullyGrown || deltaTime <= 0f) return;
        growthTime = Mathf.Min(growthDuration, growthTime + deltaTime);
        int visible = geometry.InitialStrandCount + Mathf.FloorToInt(
            (geometry.StrandCount - geometry.InitialStrandCount) * Mathf.Clamp01(growthTime / Mathf.Max(0.01f, growthDuration)));
        geometry.Render(strandMesh, patchMesh, visible);
    }

    public Vector3 SamplePatrolPoint()
    {
        InitializeGeometry();
        return transform.position + geometry.SamplePatrolPoint(UnityEngine.Random.value, UnityEngine.Random.insideUnitSphere);
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled || geometry == null || StrandCount == 0) return;
        ScanForCaptures();
    }

    private void ScanForCaptures()
    {
        Vector3 center = transform.position;
        int count = Physics.OverlapSphereNonAlloc(center, MaximumRadius + 2f, overlapBuffer,
            trappingLayers, QueryTriggerInteraction.Ignore);
        if (count == overlapBuffer.Length)
        {
            // Expand on exceptional dense frames; do not allocate continuously at the same saturation.
            Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
            count = Physics.OverlapSphereNonAlloc(center, MaximumRadius + 2f, overlapBuffer,
                trappingLayers, QueryTriggerInteraction.Ignore);
        }
        bodyBounds.Clear();
        currentCenters.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlapBuffer[i];
            Rigidbody body = candidate.attachedRigidbody;
            if (!IsEligible(body)) continue;
            if (bodyBounds.TryGetValue(body, out Bounds bounds))
            {
                bounds.Encapsulate(candidate.bounds);
                bodyBounds[body] = bounds;
            }
            else bodyBounds.Add(body, candidate.bounds);
        }
        foreach (KeyValuePair<Rigidbody, Bounds> entry in bodyBounds)
        {
            Rigidbody body = entry.Key;
            Bounds bounds = entry.Value;
            Vector3 current = bounds.center;
            Vector3 previous = previousCenters.TryGetValue(body, out Vector3 stored)
                ? stored : current - body.linearVelocity * Time.fixedDeltaTime;
            currentCenters[body] = current;
            // Circumscribed sphere is deliberately conservative for arbitrary/compound colliders.
            float bodyRadius = bounds.extents.magnitude;
            for (int strandIndex = 0; strandIndex < StrandCount; strandIndex++)
            {
                SkyWebGeometry.Strand strand = geometry.GetStrand(strandIndex);
                float contactRadius = bodyRadius + strand.Radius + contactPadding;
                float distance = SkyWebGeometry.SegmentDistanceSquared(previous, current,
                    center + strand.Start, center + strand.End, out Vector3 bodyPoint, out Vector3 silkPoint);
                if (distance > contactRadius * contactRadius) continue;
                // Stop on the contact side of the sweep rather than attaching a fast object far past the silk.
                body.position += bodyPoint - current;
                TryCapture(body, silkPoint);
                break;
            }
        }
        Dictionary<Rigidbody, Vector3> swap = previousCenters;
        previousCenters = currentCenters;
        currentCenters = swap;
    }

    public bool IsEligible(Rigidbody body)
    {
        if (body == null || !body.gameObject.activeInHierarchy || body.isKinematic) return false;
        if (body.GetComponentInParent<SkySpider>() != null || body.GetComponentInParent<SkyWeb>() != null) return false;
        if (body.GetComponentInParent<SmoothFirstPersonController>() != null ||
            body.GetComponentInParent<PlayerCreatureCarrier>() != null ||
            body.GetComponentInParent<AdvancedFirstPersonTraversal>() != null ||
            body.gameObject.layer == LayerMask.NameToLayer("Player")) return false;
        Creature creature = body.GetComponentInParent<Creature>();
        if (creature != null && creature.IsCarried) return false;
        WebCapture capture = body.GetComponent<WebCapture>();
        return capture == null || capture.CanCapture;
    }

    public bool TryCapture(Rigidbody body, Vector3 contactPoint)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || !IsEligible(body)) return false;
        WebCapture capture = body.GetComponent<WebCapture>();
        if (capture == null) capture = body.gameObject.AddComponent<WebCapture>();
        if (!capture.Attach(this, body, contactPoint)) return false;
        captures.Add(capture);
        Captured?.Invoke(capture);
        return true;
    }

    public void ForgetCapture(WebCapture capture)
    {
        captures.Remove(capture);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        while (captures.Count > 0)
        {
            WebCapture capture = captures[captures.Count - 1];
            if (capture == null) captures.RemoveAt(captures.Count - 1);
            else capture.Release();
        }
        previousCenters.Clear(); currentCenters.Clear();
    }

    private void OnDestroy()
    {
        if (strandMesh != null) Destroy(strandMesh);
        if (patchMesh != null) Destroy(patchMesh);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.65f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, MaximumRadius);
    }
}
