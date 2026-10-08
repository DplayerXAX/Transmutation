using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sticky, furry blob. When the player comes close it joins a single-file chain behind them,
/// like the children in "eagle catches chicks": the first one follows the player, every other one
/// follows the one in front. Neighbours in the chain stay joined by a sagging strand of goo.
///
/// The body is a lumpy procedural blob; fur is drawn as stacked shells by the StickyFur shader,
/// which droops the hair and drags it behind the motion.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class FuzzCreature : Creature
{
    private static readonly List<FuzzCreature> Chain = new List<FuzzCreature>();

    [Header("Following")]
    [Tooltip("The player joins a creature into the chain when closer than this.")]
    [Min(0f)] [SerializeField] private float noticeRadius = 6f;
    [Tooltip("A creature leaves the chain when the player is further than this.")]
    [Min(0f)] [SerializeField] private float loseRadius = 30f;
    [Tooltip("Gap kept to whoever is in front.")]
    [Min(0.2f)] [SerializeField] private float spacing = 1.3f;
    [Min(0f)] [SerializeField] private float maxSpeed = 6.5f;
    [Min(0f)] [SerializeField] private float wanderRadius = 3f;

    [Header("Body")]
    [Min(0.05f)] [SerializeField] private float radius = 0.38f;
    [SerializeField] private int seed = 3;
    [Tooltip("How far the bottom of the blob sits off the ground.")]
    [SerializeField] private float groundClearance = 0.02f;
    [Min(0f)] [SerializeField] private float hopHeight = 0.18f;
    [Min(0.1f)] [SerializeField] private float hopRate = 2.2f;
    [SerializeField] private LayerMask groundLayers = (1 << 0) | (1 << 7) | (1 << 8);
    [Tooltip("Height of the happy hop when petted.")]
    [Min(0f)] [SerializeField] private float petHopHeight = 0.4f;
    [Min(0.1f)] [SerializeField] private float petHopTime = 0.55f;

    // Petting: time into the current happy hop (negative = not hopping) and time until the next one.
    private float petHop = -1f;
    private float nextPetHop;

    [Header("Look")]
    [SerializeField] private Material furMaterial;
    [SerializeField] private Material gooMaterial;
    [Range(1, 24)] [SerializeField] private int furShells = 12;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool following;
    [SerializeField] private int chainIndex = -1;

    private static readonly int ShellId = Shader.PropertyToID("_Shell");
    private static readonly int VelocityId = Shader.PropertyToID("_Velocity");
    private static readonly RaycastHit[] Hits = new RaycastHit[8];

    private Transform player;
    private Vector3 velocity;
    private Vector3 home;
    private Vector3 wanderTarget;
    private float wanderTimer;
    private float hopPhase;
    private float squash = 1f;

    private Mesh blobMesh;
    private Vector3[] restDirections;
    private Vector3[] blobVertices;
    private float[] lumps;
    private Transform visual;
    private MeshRenderer[] shells;
    private MaterialPropertyBlock block;

    private Mesh gooMesh;
    private Vector3[] gooVertices;
    private MeshRenderer gooRenderer;
    private const int GooRings = 10, GooSides = 6;

    public bool IsFollowing => following;

    protected override void InitializeCreature()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller != null) player = controller.transform;
        home = transform.position;
        wanderTarget = home;
        hopPhase = seed * 1.3f;
        BuildBlob();
        BuildGoo();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        LeaveChain();
    }

    private void OnDestroy()
    {
        LeaveChain();
        if (visual != null) Destroy(visual.gameObject);
        if (blobMesh != null) Destroy(blobMesh);
        if (gooMesh != null) Destroy(gooMesh);
        if (gooRenderer != null) Destroy(gooRenderer.gameObject);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        UpdateMembership();

        Vector3 position = transform.position;
        Vector3 desired = Vector3.zero;

        if (IsCarried)
        {
            velocity = Vector3.zero;
        }
        else if (IsPetted)
        {
            // Stays under the hand and turns towards it.
            Vector3 toHand = Vector3.ProjectOnPlane(PetPoint - position, Vector3.up);
            if (toHand.sqrMagnitude > 1e-4f)
                FaceCreature(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toHand.normalized, Vector3.up), 1f - Mathf.Exp(-4f * deltaTime)));
        }
        else if (following)
        {
            // Rope-like follow: stay `spacing` behind whoever is ahead, on the line towards them.
            Vector3 leader = LeaderPosition();
            Vector3 toLeader = Vector3.ProjectOnPlane(leader - position, Vector3.up);
            float gap = toLeader.magnitude - spacing;
            if (gap > 0.05f) desired = toLeader.normalized * Mathf.Min(maxSpeed, gap * 3f);
        }
        else
        {
            wanderTimer -= deltaTime;
            if (wanderTimer <= 0f)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                wanderTarget = home + new Vector3(offset.x, 0f, offset.y);
                wanderTimer = Random.Range(2f, 5f);
            }
            Vector3 toTarget = Vector3.ProjectOnPlane(wanderTarget - position, Vector3.up);
            if (toTarget.magnitude > 0.3f) desired = toTarget.normalized * 0.8f;
        }

        if (!IsCarried)
        {
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-5f * deltaTime));
            MoveCreature(velocity * deltaTime);
            if (velocity.sqrMagnitude > 0.01f)
                FaceCreature(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity.normalized, Vector3.up), 1f - Mathf.Exp(-6f * deltaTime)));
            StickToGround(deltaTime);
        }

        UpdateBlob(deltaTime);
        UpdateGoo();
    }

    // ---------------- Petting ----------------

    // A hop straight away when the hand first touches it, and a last one when the hand lets go.
    protected override void OnPetStart()
    {
        petHop = 0f;
        nextPetHop = 1.2f;
    }

    protected override void OnPetEnd()
    {
        if (petHop < 0f) petHop = 0f;
    }

    // ---------------- Chain ----------------

    private void UpdateMembership()
    {
        if (player == null) return;
        float distance = Vector3.Distance(transform.position, player.position);
        if (IsCarried)
        {
            LeaveChain();
            return;
        }
        if (!following && distance < noticeRadius)
        {
            Chain.Add(this);
            following = true;
        }
        else if (following && distance > loseRadius)
        {
            LeaveChain();
            home = transform.position;
        }
        chainIndex = following ? Chain.IndexOf(this) : -1;
    }

    private void LeaveChain()
    {
        if (!following) return;
        Chain.Remove(this);
        following = false;
        chainIndex = -1;
    }

    private Vector3 LeaderPosition()
    {
        int index = Chain.IndexOf(this);
        if (index <= 0 || Chain[index - 1] == null) return player.position;
        return Chain[index - 1].transform.position;
    }

    // ---------------- Ground ----------------

    private void StickToGround(float deltaTime)
    {
        Vector3 origin = transform.position + Vector3.up * 3f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 10f, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 ground = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            if (Hits[i].collider.GetComponentInParent<Creature>() != null) continue;
            if (player != null && Hits[i].collider.transform.IsChildOf(player.root)) continue;
            if (Hits[i].distance < best)
            {
                best = Hits[i].distance;
                ground = Hits[i].point;
            }
        }

        float moving = Mathf.Clamp01(velocity.magnitude / 2f);
        hopPhase += deltaTime * hopRate * Mathf.Lerp(0.6f, 2.2f, moving) * Mathf.PI;
        float hop = Mathf.Abs(Mathf.Sin(hopPhase)) * hopHeight * moving;
        // Squash on landing, stretch in the air.
        float squashTarget = 1f - 0.25f * (1f - Mathf.Abs(Mathf.Sin(hopPhase))) * moving + 0.08f * Mathf.Sin(Time.time * 2f + seed);

        // Petted: it squishes down under each stroke of the hand and now and then hops up happily,
        // more often the more it likes the player.
        if (IsPetted)
        {
            squashTarget = 0.86f + 0.12f * Mathf.Sin(Time.time * 4.2f);
            nextPetHop -= deltaTime;
            if (petHop < 0f && nextPetHop <= 0f)
            {
                petHop = 0f;
                nextPetHop = Mathf.Lerp(2.2f, 0.9f, Affection) + Random.Range(0f, 0.6f);
            }
        }
        if (petHop >= 0f)
        {
            float u = petHop / petHopTime;
            petHop += deltaTime;
            if (u >= 1f) petHop = -1f;
            else if (u < 0.18f) squashTarget = 0.62f;                       // crouch to jump
            else if (u < 0.85f)
            {
                hop += Mathf.Sin((u - 0.18f) / 0.67f * Mathf.PI) * petHopHeight; // up and down
                squashTarget = 1.25f;                                         // stretched in the air
            }
            else squashTarget = 0.7f;                                         // squash on landing
        }
        squash = Mathf.Lerp(squash, squashTarget, 1f - Mathf.Exp(-(petHop >= 0f ? 22f : 14f) * deltaTime));

        if (best < float.MaxValue)
        {
            float targetY = ground.y + radius * squash + groundClearance + hop;
            float y = Mathf.Lerp(transform.position.y, targetY, 1f - Mathf.Exp(-(petHop >= 0f ? 30f : 18f) * deltaTime));
            MoveCreature(Vector3.up * (y - transform.position.y));
        }
        else
        {
            MoveCreature(Vector3.down * (6f * deltaTime));
        }
    }

    // ---------------- Body ----------------

    private void BuildBlob()
    {
        var random = new System.Random(seed);
        const int latitudes = 12, longitudes = 18;
        var directions = new List<Vector3> { Vector3.up };
        for (int i = 1; i < latitudes; i++)
        {
            float theta = Mathf.PI * i / latitudes;
            for (int j = 0; j < longitudes; j++)
            {
                float phi = 2f * Mathf.PI * j / longitudes;
                directions.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
            }
        }
        directions.Add(Vector3.down);
        restDirections = directions.ToArray();
        blobVertices = new Vector3[restDirections.Length];

        // A few soft bulges so no two blobs match.
        lumps = new float[restDirections.Length];
        var bulges = new Vector3[4];
        for (int i = 0; i < bulges.Length; i++)
            bulges[i] = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble(), (float)random.NextDouble() * 2f - 1f).normalized;
        for (int v = 0; v < restDirections.Length; v++)
        {
            float lump = 0f;
            foreach (Vector3 bulge in bulges)
                lump += 0.22f * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(restDirections[v], bulge)), 4f);
            lumps[v] = lump;
        }

        var triangles = new List<int>();
        int bottom = restDirections.Length - 1;
        for (int j = 0; j < longitudes; j++)
        {
            int next = (j + 1) % longitudes;
            AddOutward(triangles, 0, 1 + j, 1 + next);
            for (int i = 0; i < latitudes - 2; i++)
            {
                int a = 1 + i * longitudes + j, b = 1 + i * longitudes + next;
                AddOutward(triangles, a, a + longitudes, b);
                AddOutward(triangles, b, a + longitudes, b + longitudes);
            }
            int last = 1 + (latitudes - 2) * longitudes;
            AddOutward(triangles, bottom, last + next, last + j);
        }

        blobMesh = new Mesh { name = "Fuzz Blob", hideFlags = HideFlags.DontSave };
        blobMesh.MarkDynamic();
        blobMesh.vertices = restDirections;
        blobMesh.SetTriangles(triangles, 0);
        blobMesh.SetUVs(0, new List<Vector3>(restDirections)); // Stable fur coordinates.
        blobMesh.RecalculateNormals();

        // Each shell is one layer of fur; shell 0 is the solid skin.
        visual = new GameObject(name + " Fur").transform;
        visual.SetParent(transform, false);
        shells = new MeshRenderer[furShells + 1];
        for (int s = 0; s <= furShells; s++)
        {
            var shell = new GameObject("Shell " + s, typeof(MeshFilter), typeof(MeshRenderer));
            shell.transform.SetParent(visual, false);
            shell.GetComponent<MeshFilter>().sharedMesh = blobMesh;
            var renderer = shell.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = furMaterial;
            renderer.shadowCastingMode = s == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            shells[s] = renderer;
        }
        block = new MaterialPropertyBlock();
    }

    private void AddOutward(List<int> triangles, int a, int b, int c)
    {
        Vector3 pa = restDirections[a], pb = restDirections[b], pc = restDirections[c];
        bool outward = Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), pa + pb + pc) > 0f;
        triangles.Add(a);
        triangles.Add(outward ? b : c);
        triangles.Add(outward ? c : b);
    }

    private void UpdateBlob(float deltaTime)
    {
        float time = Time.time;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        for (int v = 0; v < restDirections.Length; v++)
        {
            Vector3 d = restDirections[v];
            float wobble = 0.05f * Mathf.Sin(d.x * 5f + time * 3.1f + seed) + 0.04f * Mathf.Sin(d.z * 6f - time * 2.3f);
            float r = radius * (1f + lumps[v] + wobble);
            // Sag: the underside spreads and flattens like something wet.
            Vector3 p = d * r;
            p.y *= squash * (d.y < 0f ? 0.7f : 1f);
            float spread = 1f / Mathf.Sqrt(Mathf.Max(squash, 0.3f));
            p.x *= spread * (d.y < 0f ? 1.12f : 1f);
            p.z *= spread * (d.y < 0f ? 1.12f : 1f);
            // The back half lags behind motion.
            p -= localVelocity * (0.03f * Mathf.Max(0f, -Vector3.Dot(d, localVelocity.normalized)));
            blobVertices[v] = p;
        }
        blobMesh.vertices = blobVertices;
        blobMesh.RecalculateNormals();
        blobMesh.RecalculateBounds();
        blobMesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 4f); // Room for fur.

        Vector3 drag = -velocity;
        for (int s = 0; s < shells.Length; s++)
        {
            shells[s].GetPropertyBlock(block);
            block.SetFloat(ShellId, s / (float)Mathf.Max(1, furShells));
            block.SetVector(VelocityId, drag);
            shells[s].SetPropertyBlock(block);
        }
    }

    // ---------------- Goo strand to the one ahead ----------------

    private void BuildGoo()
    {
        if (gooMaterial == null) return;
        var triangles = new List<int>();
        for (int r = 0; r < GooRings - 1; r++)
        for (int s = 0; s < GooSides; s++)
        {
            int a = r * GooSides + s, b = r * GooSides + (s + 1) % GooSides;
            int c = a + GooSides, d = b + GooSides;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(b); triangles.Add(d); triangles.Add(c);
        }
        gooVertices = new Vector3[GooRings * GooSides];
        gooMesh = new Mesh { name = "Fuzz Goo", hideFlags = HideFlags.DontSave };
        gooMesh.MarkDynamic();
        gooMesh.vertices = gooVertices;
        gooMesh.SetTriangles(triangles, 0);

        var goo = new GameObject(name + " Goo", typeof(MeshFilter), typeof(MeshRenderer));
        goo.GetComponent<MeshFilter>().sharedMesh = gooMesh;
        gooRenderer = goo.GetComponent<MeshRenderer>();
        gooRenderer.sharedMaterial = gooMaterial;
        gooRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gooRenderer.enabled = false;
    }

    private void UpdateGoo()
    {
        if (gooMesh == null) return;
        int index = following ? Chain.IndexOf(this) : -1;
        bool linked = index > 0 && Chain[index - 1] != null;
        gooRenderer.enabled = linked;
        if (!linked) return;

        Transform ahead = Chain[index - 1].transform;
        Vector3 from = transform.position + transform.forward * radius * 0.7f;
        Vector3 to = ahead.position - ahead.forward * radius * 0.7f;
        float length = Vector3.Distance(from, to);
        // Thins as it stretches, sags more when slack.
        float thickness = Mathf.Lerp(0.05f, 0.012f, Mathf.InverseLerp(spacing * 0.8f, spacing * 2.5f, length));
        float sag = Mathf.Lerp(0.35f, 0.05f, Mathf.InverseLerp(spacing * 0.8f, spacing * 2.5f, length));
        Vector3 along = (to - from).normalized;
        Vector3 side = Vector3.Cross(along, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(along, side); // (side, up, along) winds the tube outward.

        for (int r = 0; r < GooRings; r++)
        {
            float t = r / (float)(GooRings - 1);
            Vector3 centre = Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
            // Fat where it sticks to each body, thin in the middle.
            float radiusHere = thickness * (1f + 1.5f * Mathf.Pow(Mathf.Abs(t - 0.5f) * 2f, 3f));
            for (int s = 0; s < GooSides; s++)
            {
                float angle = 2f * Mathf.PI * s / GooSides;
                gooVertices[r * GooSides + s] = centre + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radiusHere;
            }
        }
        gooMesh.vertices = gooVertices;
        gooMesh.RecalculateNormals();
        gooMesh.RecalculateBounds();
    }
}
