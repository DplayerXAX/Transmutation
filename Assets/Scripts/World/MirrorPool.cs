using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A still mirror pool in the dark caves (Daniel's reference: a cave lake so flat that the lit rock and stalactites
/// above seem to hang upside down in it). A flat irregular disc of water on the floor (shader Capstone/Cave/MirrorPool)
/// with stalagmites round it, a faint glow of its own (a reveal light, so the rock round it and its mirror image show
/// from afar), and a planar reflection: a second camera renders the view mirrored in the water plane.
/// Only the nearest visible pool within `mirrorRange` is mirrored each frame (one extra half-resolution render); the
/// others show their dark tint. DarkTunnels places them through StrangeWater.Build.
/// Attach gives the same mirror to a HangingLake, which lies on the rock: looking up, the floor lies upside down in the
/// water, and each drop that leaves sends rings across it.
/// </summary>
[DefaultExecutionOrder(2000)] // after the camera has moved this frame
public sealed class MirrorPool : MonoBehaviour
{
    // Only a pool this close (metres from its edge) mirrors; further off the water shows its plain look.
    private const float MirrorRange = 25f;
    // The mirror camera stops drawing here; the dark caves hide anything further anyway.
    private const float MirrorFarClip = 60f;
    private static readonly Plane[] ViewPlanes = new Plane[6];
    private static readonly List<MirrorPool> All = new List<MirrorPool>();
    private static Camera mirrorCamera;
    private static RenderTexture mirrorTexture;
    private static MirrorPool mirrored;
    private static int lastFrame = -1;
    private static readonly int MirrorTexId = Shader.PropertyToID("_MirrorTex");
    private const int WaterLayer = 4;

    private static readonly Plane[] frustum = new Plane[6];
    // Test hooks (Editor/CaveBatonCheck).
    public static bool DebugSupported;
    public static int DebugRenders, DebugTicks, DebugNoBest;
    public static float DebugNearest;
    public static bool DebugNearestSeen;
    public static string DebugCamera;
    public static RenderTexture DebugTexture => mirrorTexture;
    private Material material;
    private Renderer surface;
    private float radius;
    private WaterDrips drips;
    private readonly Vector4[] tipData = new Vector4[6];
    public float Radius => radius;
    private Vector3 centre, planePoint, planeNormal = Vector3.up;

    /// <summary>Makes a pool on the floor round a point: radius in metres, glow colour for its reveal light.</summary>
    public static MirrorPool Create(Transform parent, Vector3 floorPoint, float radius, Color glow, int stalagmites, int mask, Material rock)
    {
        // The water level: most of the floor round it under water, the highest bumps poking out as islands.
        var heights = new List<float>();
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f, r = radius * (i % 2 == 0 ? 0.45f : 0.85f);
            Vector3 p = floorPoint + new Vector3(Mathf.Cos(a) * r, 4f, Mathf.Sin(a) * r);
            if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 10f, mask)) heights.Add(hit.point.y);
        }
        heights.Sort();
        float level = heights.Count > 0 ? heights[Mathf.Clamp(Mathf.RoundToInt(heights.Count * 0.65f), 0, heights.Count - 1)] + 0.05f : floorPoint.y + 0.1f;

        var go = new GameObject("Mirror pool");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(floorPoint.x, level, floorPoint.z);
        go.layer = WaterLayer;
        var pool = go.AddComponent<MirrorPool>();
        pool.radius = radius;
        pool.centre = pool.planePoint = go.transform.position;
        go.AddComponent<MeshFilter>().sharedMesh = PoolMesh(radius, Random.Range(0f, 50f));
        pool.surface = go.AddComponent<MeshRenderer>();
        pool.surface.shadowCastingMode = ShadowCastingMode.Off;
        pool.surface.receiveShadows = false;
        pool.material = new Material(Shader.Find("Capstone/Cave/MirrorPool")) { name = "Mirror pool" };
        pool.surface.sharedMaterial = pool.material;

        // Its own faint glow: the rock round it comes out of the dark, so there is something to mirror.
        var glowAt = new GameObject("Pool glow").transform;
        glowAt.SetParent(go.transform, false);
        glowAt.localPosition = Vector3.up * 1.5f;
        CaveReveal.Add(glowAt, radius * 1.8f, glow, () => 0.55f);

        // Stalagmites round the rim (their mirror images hang down in the water), in the terrain's own material.
        for (int i = 0; i < stalagmites; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = radius * Random.Range(0.75f, 1.25f);
            Vector3 p = go.transform.position + new Vector3(Mathf.Cos(a) * r, 5f, Mathf.Sin(a) * r);
            if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 12f, mask)) continue;
            Stalagmite(go.transform, hit.point - Vector3.up * 0.3f, Random.Range(1.5f, 6f), Random.Range(0.3f, 0.9f), rock);
        }
        return pool;
    }

    /// <summary>Gives a hanging lake its mirror: the lake's own mesh and water, reflecting in a level plane through it.</summary>
    public static MirrorPool Attach(GameObject lake, Renderer renderer, Material water, float planeY, Vector3 centre, float radius, WaterDrips drips)
    {
        lake.layer = WaterLayer;
        var pool = lake.AddComponent<MirrorPool>();
        pool.surface = renderer;
        pool.material = water;
        pool.radius = radius;
        pool.drips = drips;
        pool.centre = centre;
        pool.planePoint = new Vector3(centre.x, planeY, centre.z);
        pool.planeNormal = Vector3.down;
        return pool;
    }

    private void Update()
    {
        if (drips == null) return;
        int n = Mathf.Min(drips.TipCount, tipData.Length);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = drips.TipPoint(i);
            tipData[i] = new Vector4(p.x, p.z, drips.SinceDrop(i), 0f);
        }
        material.SetVectorArray("_Tips", tipData);
        material.SetInt("_TipCount", n);
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() { All.Remove(this); if (mirrored == this) mirrored = null; }

    private void LateUpdate()
    {
        // One pool per frame does the mirror render: the nearest visible one in range.
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        DebugTicks++;
        DebugNearest = float.MaxValue;
        Camera main = Camera.main;
        MirrorPool best = null;
        float bestD = MirrorRange;
        if (main != null)
        {
            GeometryUtility.CalculateFrustumPlanes(main, ViewPlanes);
            foreach (MirrorPool p in All)
            {
                float d = Vector3.Distance(main.transform.position, p.centre) - p.radius;
                // In front of the view (a wide cone, so a pool at the edge of the screen still counts), or underfoot.
                Vector3 to = p.centre - main.transform.position;
                bool seen = p.surface != null && (to.magnitude < p.radius + 2f ||
                    GeometryUtility.TestPlanesAABB(ViewPlanes, new Bounds(p.centre, new Vector3(p.radius * 2f, 2f, p.radius * 2f))));
                if (d < DebugNearest) { DebugNearest = d; DebugNearestSeen = seen; DebugCamera = main.name + " " + main.transform.position; }
                if (!seen) continue;
                if (d < bestD) { bestD = d; best = p; }
            }
        }
        foreach (MirrorPool p in All) p.material.SetFloat("_MirrorOn", p == best ? 1f : 0f);
        mirrored = best;
        if (best != null) best.Render(main);
        else DebugNoBest++;
    }

    private void Render(Camera main)
    {
        int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
        if (mirrorTexture == null || mirrorTexture.width != w || mirrorTexture.height != h)
        {
            if (mirrorTexture != null) mirrorTexture.Release();
            mirrorTexture = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "Mirror pool" };
        }
        if (mirrorCamera == null)
        {
            var go = new GameObject("Mirror pool camera") { hideFlags = HideFlags.HideAndDontSave };
            mirrorCamera = go.AddComponent<Camera>();
            mirrorCamera.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
        }
        Camera cam = mirrorCamera;
        cam.CopyFrom(main);
        cam.enabled = false;
        cam.cullingMask = main.cullingMask & ~(1 << WaterLayer);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.targetTexture = mirrorTexture;
        cam.farClipPlane = Mathf.Min(main.farClipPlane, MirrorFarClip);

        // Mirror the view in the water plane, and clip everything below the water (oblique near plane).
        Vector3 n = planeNormal, p0 = planePoint;
        float d = -Vector3.Dot(n, p0);
        Matrix4x4 reflect = Reflection(new Vector4(n.x, n.y, n.z, d));
        cam.worldToCameraMatrix = main.worldToCameraMatrix * reflect;
        Vector3 eye = reflect.MultiplyPoint(main.transform.position);
        cam.transform.position = eye;
        Vector4 clip = CameraSpacePlane(cam, p0, n);
        cam.ResetProjectionMatrix();
        cam.projectionMatrix = cam.CalculateObliqueMatrix(clip);

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = mirrorTexture };
        bool invert = GL.invertCulling;
        GL.invertCulling = true;
        DebugSupported = RenderPipeline.SupportsRenderRequest(cam, request);
        if (DebugSupported) RenderPipeline.SubmitRenderRequest(cam, request);
        DebugRenders++;
        GL.invertCulling = invert;
        Shader.SetGlobalTexture(MirrorTexId, mirrorTexture);
    }

    private static Matrix4x4 Reflection(Vector4 plane)
    {
        var m = Matrix4x4.identity;
        m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
        m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
        m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
        return m;
    }

    private static Vector4 CameraSpacePlane(Camera cam, Vector3 point, Vector3 normal)
    {
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cp = m.MultiplyPoint(point + normal * 0.02f);
        Vector3 cn = m.MultiplyVector(normal).normalized;
        return new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn));
    }

    // A flat irregular disc; uv 0..1 across it, centred, for the edge fade.
    private static Mesh PoolMesh(float radius, float seed)
    {
        const int around = 48;
        var v = new Vector3[around + 1];
        var uv = new Vector2[around + 1];
        var t = new int[around * 3];
        v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < around; i++)
        {
            float a = i / (float)around * Mathf.PI * 2f;
            float r = radius * (0.85f + 0.3f * Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + seed, Mathf.Sin(a) * 1.3f + seed));
            v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
            t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % around + 1; t[i * 3 + 2] = i + 1;
        }
        var m = new Mesh { name = "Mirror pool", vertices = v, uv = uv, triangles = t };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A rough cone of rock standing on the floor (or, turned over, hanging from a ceiling).</summary>
    public static void Stalagmite(Transform parent, Vector3 foot, float tall, float baseRadius, Material rock, bool hanging = false)
    {
        const int around = 9, rings = 7;
        var v = new List<Vector3>();
        var t = new List<int>();
        float seed = Random.Range(0f, 50f);
        for (int j = 0; j <= rings; j++)
        {
            float u = j / (float)rings;
            float r = baseRadius * Mathf.Pow(1f - u, 1.4f) * (1f + 0.25f * Mathf.Sin(u * 11f + seed));
            for (int i = 0; i < around; i++)
            {
                float a = i / (float)around * Mathf.PI * 2f;
                float lump = 1f + (Mathf.PerlinNoise(a + seed, u * 3f) - 0.5f) * 0.5f;
                v.Add(new Vector3(Mathf.Cos(a) * r * lump, u * tall, Mathf.Sin(a) * r * lump));
            }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < around; i++)
            {
                int a0 = j * around + i, a1 = j * around + (i + 1) % around, b0 = a0 + around, b1 = a1 + around;
                t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
            }
        var mesh = new Mesh { name = "Stalagmite" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        var go = new GameObject(hanging ? "Stalactite" : "Stalagmite");
        go.transform.SetParent(parent, true);
        go.transform.position = foot;
        go.transform.rotation = hanging ? Quaternion.Euler(180f, Random.Range(0f, 360f), 0f) : Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = rock;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.layer = 7;
    }
}
