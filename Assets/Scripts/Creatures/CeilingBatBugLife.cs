using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class CeilingBatBugLife : MonoBehaviour
{
    [SerializeField] private CeilingBatBugHabitat habitat;
    [SerializeField] private Vector3 inheritedHome;
    [SerializeField] private bool initialized, laidEgg;
    [SerializeField] private float totalLife, elapsedLife, layingAge;
    private float nextAttempt;
    [SerializeField] private float corpseTime;
    private CeilingBatBug bug;
    private Collider[] overlaps = new Collider[64];
    private readonly HashSet<CeilingBatBugEgg> seen = new HashSet<CeilingBatBugEgg>();
    private readonly List<CeilingBatBugEgg> eggs = new List<CeilingBatBugEgg>();
    public CeilingBatBugHabitat Habitat => habitat;
    public Vector3 Home => inheritedHome;
    public bool HasLaidEgg => laidEgg;
    public float Elapsed => elapsedLife;
    public float TotalLifetime => totalLife;
    public bool Ready => habitat == null || habitat.Ready(transform.position);
    public void InitializeNewborn(CeilingBatBugHabitat data, Vector3 home, int seed)
    {
        float life = Mathf.Lerp(data.lifetime.x, data.lifetime.y, (float)new System.Random(seed).NextDouble());
        InitializeAdult(data, home, life, seed);
    }

    public void InitializeAdult(CeilingBatBugHabitat data, Vector3 home, float remaining, int seed)
    {
        habitat = data; inheritedHome = home;
        var random = new System.Random(seed);
        totalLife = Mathf.Max(remaining, Mathf.Lerp(data.lifetime.x, data.lifetime.y, (float)random.NextDouble()));
        elapsedLife = totalLife - remaining;
        layingAge = Mathf.Lerp(data.layingAge.x, data.layingAge.y, (float)random.NextDouble());
        initialized = true; laidEgg = false;
        bug = GetComponent<CeilingBatBug>();
        bug.InitializeNewIndividual(); bug.SetRemainingAge(remaining);
        GetComponent<CeilingBatBugSurfaceMotor>().ConfigureHome(home, data.roamRadius, data);
    }
    private void Awake()
    {
        bug = GetComponent<CeilingBatBug>();
        if (habitat != null && !initialized) InitializeAdult(habitat, habitat.Home, bug.Age, GetInstanceID());
        if (initialized && habitat != null) GetComponent<CeilingBatBugSurfaceMotor>().ConfigureHome(inheritedHome, habitat.roamRadius, habitat);
    }
    private void OnEnable() { if (bug == null) bug = GetComponent<CeilingBatBug>(); bug.Died += OnDied; }
    private void OnDisable() { if (bug != null) bug.Died -= OnDied; }
    private void Update()
    {
        if (bug == null || !bug.IsDead || habitat == null || habitat.corpseRetentionSeconds <= 0f || !Ready || bug.IsCarried ||
            (TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim)) return;
        corpseTime += Time.deltaTime;
        if (corpseTime >= habitat.corpseRetentionSeconds) Destroy(gameObject);
    }
    public void Advance(float dt)
    {
        if (!initialized || habitat == null) return;
        elapsedLife = Mathf.Max(elapsedLife, totalLife - bug.Age);
        if (!laidEgg && elapsedLife >= layingAge && Time.time >= nextAttempt && bug.CanSupportPlayer)
        { nextAttempt = Time.time + 1.5f; TryLayEgg(); }
    }
    public bool TryLayEgg()
    {
        if (laidEgg || habitat == null || habitat.eggPrefab == null || !bug.CanSupportPlayer || elapsedLife < layingAge) return false;
        var motor = GetComponent<CeilingBatBugSurfaceMotor>();
        Vector3 normal = motor.SurfaceNormal, side = Vector3.Cross(normal, transform.forward).normalized;
        for (int i = 0; i < 8; i++)
        {
            Vector3 from = transform.position + Quaternion.AngleAxis(i * 45f, normal) * side * 0.65f + normal * 0.3f;
            if (!motor.TrySurface(from, -normal, 1f, out RaycastHit hit)) continue;
            Vector3 position = hit.point + hit.normal * 0.15f;
            if (!habitat.Contains(position)) continue;
            int count = gameObject.scene.GetPhysicsScene().OverlapSphere(position, 0.14f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count != 0) continue;
            GameObject staging = new GameObject("Egg placement staging"); staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
            CeilingBatBugEgg egg = null;
            try
            {
                egg = Instantiate(habitat.eggPrefab, position, Quaternion.FromToRotation(Vector3.up, hit.normal), staging.transform);
                egg.InitializeLaid(habitat, inheritedHome, hit);
                laidEgg = true;
                egg.transform.SetParent(null, true);
                Physics.SyncTransforms();
                return true;
            }
            catch { laidEgg = false; if (egg != null) { egg.gameObject.SetActive(false); Destroy(egg.gameObject); } throw; }
            finally { Destroy(staging); }
        }
        return false;
    }
    private void OnDied(Creature dead)
    {
        if (habitat == null) return;
        int count;
        do
        {
            count = gameObject.scene.GetPhysicsScene().OverlapSphere(transform.position, habitat.sensingRadius + 0.25f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (overlaps.Length < 4096);
        seen.Clear(); eggs.Clear();
        for (int i = 0; i < count; i++)
        {
            var egg = overlaps[i].GetComponentInParent<CeilingBatBugEgg>();
            if (egg != null && egg.Habitat == habitat && egg.IsReady && seen.Add(egg) &&
                Vector3.Distance(egg.transform.position, transform.position) <= habitat.sensingRadius) eggs.Add(egg);
        }
        eggs.Sort((a, b) => { int c = b.ReadyWait.CompareTo(a.ReadyWait); return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID()); });
        foreach (var egg in eggs) if (egg != null && egg.TryHatch()) break;
    }
}
