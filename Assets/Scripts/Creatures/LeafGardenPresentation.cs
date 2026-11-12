using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Persistent GPU leaf state, GPU visibility compaction, and one indirect mesh draw.</summary>
[RequireComponent(typeof(LeafGarden))]
public sealed class LeafGardenPresentation : MonoBehaviour
{
    public ComputeShader Simulation;
    public Material LeafMaterial;
    public Mesh LeafMesh;
    public Camera ViewCamera;
    [Range(25000, 400000)] public int LeafCount = 200000;
    [Range(0.15f, 1)] public float DistantDensity = 0.4f;
    [Min(5)] public float DetailDistance = 32;
    public bool DrawLeaves = true;
    public bool SimulateLeaves = true;
    [Tooltip("Organic gaps and thinner leaf coverage on mapped terrain.")]
    [Range(0.1f,1)] public float Coverage = 1;
    [Range(0f, 1f)] public float OrganicEdge;
    public int Capacity { get; private set; }
    public string Status { get; private set; } = "Not initialized";
    public ComputeBuffer StateBuffer => leaves;
    public ComputeBuffer DrawArguments => arguments;
    public int ActiveActors => actorCount;
    private LeafGarden garden;
    private ComputeShader compute;
    private Material material;
    private ComputeBuffer leaves, visible, arguments;
    private int init, step;
    private readonly Plane[] planes = new Plane[6];
    private readonly Vector4[] planeData = new Vector4[6];
    private readonly Vector4[] actorStarts = new Vector4[16], actorEnds = new Vector4[16];
    private readonly Collider[] actorHits = new Collider[128];
    private readonly Dictionary<Rigidbody, Vector3> actorPositions = new Dictionary<Rigidbody, Vector3>();
    private readonly List<Rigidbody> previousActors = new List<Rigidbody>(32);
    private readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();
    private readonly Vector3[] retained = new Vector3[16];
    private float actorTimer;
    private int actorCount;
    private Bounds bounds;

    private void Start() => Allocate();
    public void Rebuild(int count)
    {
        LeafCount = Mathf.Clamp(count, 25000, 400000);
        if (isActiveAndEnabled && Application.isPlaying) Allocate();
    }
    private void Allocate()
    {
        Release(); garden = GetComponent<LeafGarden>();
        if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing ||
            Simulation == null || LeafMaterial == null || LeafMesh == null)
        { Status = "Leaf Garden needs compute/instancing support and its three rendering assets."; Debug.LogError(Status, this); return; }
        try
        {
            Capacity = Mathf.Clamp(LeafCount, 25000, 400000);
            compute = Instantiate(Simulation); material = new Material(LeafMaterial);
            leaves = new ComputeBuffer(Capacity, 64);
            visible = new ComputeBuffer(Capacity, sizeof(uint), ComputeBufferType.Append);
            arguments = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            arguments.SetData(new uint[] { LeafMesh.GetIndexCount(0), 0, LeafMesh.GetIndexStart(0), (uint)LeafMesh.GetBaseVertex(0), 0 });
            init = compute.FindKernel("Initialize"); step = compute.FindKernel("Simulate");
            compute.SetInt("_Count", Capacity); compute.SetInt("_Seed", garden.Seed);
            compute.SetVector("_Garden", new Vector4(garden.transform.position.x, garden.transform.position.y, garden.transform.position.z, 0));
            compute.SetVector("_Area", new Vector4(garden.Area.x, garden.Area.y, Mathf.CeilToInt(Mathf.Sqrt(Capacity)), 0));
            Texture2D surface=garden.Surface!=null && garden.Surface.Map!=null?garden.Surface.Map:Texture2D.whiteTexture;
            Vector4 rect=garden.Surface!=null?garden.Surface.WorldRect:new Vector4(-1,-1,2,2);
            compute.SetTexture(init,"_Surface",surface); compute.SetTexture(step,"_Surface",surface);
            compute.SetVector("_SurfaceRect",rect); compute.SetFloat("_Mapped",garden.Surface!=null?1:0); compute.SetFloat("_Coverage",Coverage);
            compute.SetFloat("_OrganicEdge", OrganicEdge);
            material.SetTexture("_Surface",surface); material.SetVector("_SurfaceRect",rect); material.SetFloat("_Mapped",garden.Surface!=null?1:0);
            compute.SetBuffer(init, "_Leaves", leaves); compute.Dispatch(init, (Capacity + 127) / 128, 1, 1);
            compute.SetBuffer(step, "_Leaves", leaves); compute.SetBuffer(step, "_Visible", visible);
            material.SetBuffer("_Leaves", leaves); material.SetBuffer("_Visible", visible);
            actorPositions.Clear(); previousActors.Clear(); actorCount = 0; actorTimer = 0;
            Status = $"{Capacity:N0} persistent GPU leaves";
        }
        catch (Exception e) { Release(); Status = e.Message; Debug.LogException(e, this); }
    }

    private void CollectActors(float dt)
    {
        // Refresh actor discovery at 10 Hz, but update their swept paths every rendered frame.
        actorTimer -= dt;
        if (actorTimer <= 0)
        {
            actorTimer = 0.1f; previousActors.Clear(); seen.Clear();
            int count = ReflectionPhysics.OverlapSphereNonAlloc(garden.transform.position,
                Mathf.Max(garden.Area.x, garden.Area.y) * 0.75f, actorHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count && previousActors.Count < 16; i++)
            {
                Rigidbody body = actorHits[i].attachedRigidbody;
                if (body != null && !body.isKinematic && body != garden.GetComponent<Rigidbody>() && seen.Add(body))
                    previousActors.Add(body);
            }
            // Retain only selected actors: bounded memory even as actors spawn/despawn.
            for (int i = 0; i < previousActors.Count; i++) retained[i] = actorPositions.TryGetValue(previousActors[i], out var p) ? p : previousActors[i].position;
            actorPositions.Clear();
            for (int i = 0; i < previousActors.Count; i++) actorPositions[previousActors[i]] = retained[i];
        }
        actorCount = 0;
        foreach (Rigidbody body in previousActors)
        {
            if (body == null || body.isKinematic) continue;
            Vector3 now = body.position, previous = actorPositions.TryGetValue(body, out var p) ? p : now;
            actorPositions[body] = now;
            Vector3 travel = now - previous;
            if (travel.sqrMagnitude > 400) previous = now; // teleport/reset
            float speed = Mathf.Min(30, new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude);
            if (speed < 0.15f) continue;
            actorStarts[actorCount] = new Vector4(previous.x, previous.y, previous.z, Mathf.Clamp(speed * 0.14f, 0.15f, 3));
            actorEnds[actorCount] = new Vector4(now.x, now.y, now.z, 0.75f + speed * 0.035f);
            actorCount++;
        }
    }
    private void LateUpdate()
    {
        if (leaves == null) return;
        Camera cam = ViewCamera != null && ViewCamera.isActiveAndEnabled ? ViewCamera : Camera.main;
        if (cam == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        CollectActors(Time.deltaTime);
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        for (int i = 0; i < 6; i++) planeData[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
        visible.SetCounterValue(0);
        compute.SetVectorArray("_Planes", planeData);
        compute.SetVectorArray("_ActorStarts", actorStarts); compute.SetVectorArray("_ActorEnds", actorEnds);
        compute.SetInt("_ActorCount", actorCount);
        compute.SetFloat("_Delta", SimulateLeaves && garden.SimulationEnabled && !garden.IsDead ? dt : 0);
        compute.SetFloat("_EffectTime", garden.EffectTime);
        compute.SetFloat("_Blend", garden.Blend);
        compute.SetFloat("_Height", garden.Height); compute.SetFloat("_SourceRadius", garden.SourceRadius); compute.SetFloat("_TopRadius", garden.TopRadius);
        compute.SetVector("_Center", garden.TornadoCenter);
        compute.SetVector("_Camera", cam.transform.position);
        compute.SetVector("_LOD", new Vector4(DetailDistance, DistantDensity, 0, 0));
        compute.Dispatch(step, (Capacity + 127) / 128, 1, 1);
        ComputeBuffer.CopyCount(visible, arguments, sizeof(uint));
        // Full vertical extent protects the landmark even when its source tile is off-screen.
        float low=garden.Surface!=null?garden.Surface.HeightRange.x:garden.transform.position.y;
        float high=garden.Surface!=null?garden.Surface.HeightRange.y:garden.transform.position.y;
        bounds = new Bounds(new Vector3(garden.transform.position.x,(low+high+105)*0.5f,garden.transform.position.z), new Vector3(garden.Area.x + 60, high-low+120, garden.Area.y + 60));
        if (DrawLeaves) Graphics.DrawMeshInstancedIndirect(LeafMesh, 0, material, bounds, arguments,
            0, null, ShadowCastingMode.Off, false, gameObject.layer, cam);
    }
    private void OnDisable() => Release();
    private void OnEnable() { if (Application.isPlaying && garden != null) Allocate(); }
    private void Release()
    {
        leaves?.Release(); visible?.Release(); arguments?.Release(); leaves = visible = arguments = null; Capacity = 0;
        if (material != null) Destroy(material); if (compute != null) Destroy(compute);
    }
}
