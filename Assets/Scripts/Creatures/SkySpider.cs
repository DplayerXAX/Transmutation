using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TimeSpider: hunts the remaining Age of creatures trapped in its current web.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class SkySpider : Creature
{
    [Header("Web")]
    [SerializeField] private SkyWeb webPrefab;
    [Tooltip("Scale applied once to each new web. Overrides the web prefab's scale; existing webs keep their size.")]
    [Min(0.05f)] [SerializeField] private float webScale = 1f;
    [Min(0f)] [SerializeField] private float returnRange = 12f;

    [Header("Movement")]
    [Min(0f)] [SerializeField] private float moveSpeed = 1.2f;
    [Min(0f)] [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private Vector2 pauseTimeRange = new Vector2(0.4f, 1.6f);

    [Header("Age Hunting")]
    [Tooltip("Distance from the spider's Rigidbody to a captive's nearest solid collider surface required to drain all remaining Age.")]
    [Min(0f)] [SerializeField] private float biteRange = 0.75f;

    public SkyWeb CurrentWeb { get; private set; }
    public bool IsReturning { get; private set; }
    public int CreatedWebCount => webs.Count;
    public float BiteRange => Mathf.Max(0f, biteRange);
    public Creature HuntingTarget => CanHunt && IsEligiblePrey(huntCapture, out var prey) ? prey : null;

    private readonly List<SkyWeb> webs = new List<SkyWeb>();
    private Rigidbody body;
    private Vector3 destination;
    private float pauseTime;
    private bool wasCarried;
    private WebCapture huntCapture;
    private readonly List<Collider> preyColliders = new List<Collider>(8);

    private bool HasUsableWeb => CurrentWeb != null && CurrentWeb.isActiveAndEnabled && !CurrentWeb.IsDead &&
        CurrentWeb.gameObject.scene == gameObject.scene;
    private bool CanHunt => Application.isPlaying && isActiveAndEnabled && SimulationEnabled && !IsConsumptionPending &&
        !IsCarried && !IsBubbleCaptured && body != null && HasUsableWeb && !IsReturning;

    protected override void InitializeCreature()
    {
        // Age hunting accepts all captured creatures, independently of ordinary meal tags.
        SetFoodAndDiet(FoodTag.Meat, FoodTag.Everything);
        if (!Application.isPlaying || IsDead) return;
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        if (webPrefab == null)
        {
            Debug.LogError("SkySpider needs a SkyWeb prefab. Use Tools/Creatures/Create Sky Spider Assets.", this);
            SetSimulationEnabled(false);
            return;
        }
        CreateWeb(transform.position);
    }

    private void CreateWeb(Vector3 position)
    {
        SkyWeb web = Instantiate(webPrefab, position, Quaternion.identity);
        // Web lifetime and world position are independent of its spider and carrier.
        web.transform.SetParent(null);
        if (web.gameObject.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(web.gameObject, gameObject.scene);
        web.name = "Sky Web " + (webs.Count + 1);
        web.InitializeForSpawn(webScale);
        webs.Add(web);
        SetCurrentWeb(web, false);
    }

    private void SetCurrentWeb(SkyWeb web, bool returning)
    {
        huntCapture = null;
        if (CurrentWeb != null) CurrentWeb.SetBuilder(null);
        CurrentWeb = web;
        CurrentWeb.SetBuilder(this);
        IsReturning = returning;
        destination = returning ? web.transform.position : web.SamplePatrolPoint();
        pauseTime = 0f;
    }

    protected override void TickCreature(float deltaTime)
    {
        bool released = wasCarried && !IsCarried;
        wasCarried = IsCarried;
        if (IsCarried || IsBubbleCaptured) { huntCapture = null; return; }
        if (released) SelectWebAfterRelease();
        if (!HasUsableWeb && body != null) SelectWebAfterRelease();
    }

    private void SelectWebAfterRelease()
    {
        SkyWeb nearest = null;
        float nearestDistance = returnRange * returnRange;
        for (int i = webs.Count - 1; i >= 0; i--)
        {
            SkyWeb web = webs[i];
            if (web == null) { webs.RemoveAt(i); continue; }
            if (!web.isActiveAndEnabled || web.IsDead || web.gameObject.scene != gameObject.scene) continue;
            float distance = (web.transform.position - body.position).sqrMagnitude;
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            nearest = web;
        }
        if (nearest == null) CreateWeb(body.position);
        else SetCurrentWeb(nearest, true);
    }

    private void FixedUpdate()
    {
        if (!isActiveAndEnabled || !SimulationEnabled || IsConsumptionPending || IsCarried || IsBubbleCaptured || body == null || !HasUsableWeb)
        {
            huntCapture = null;
            return;
        }
        if (!IsReturning && HuntCapturedAge()) return;
        if (pauseTime > 0f) { pauseTime -= Time.fixedDeltaTime; return; }
        Vector3 next = MoveTowards(destination);
        if ((next - destination).sqrMagnitude > 0.01f) return;
        IsReturning = false;
        destination = CurrentWeb.SamplePatrolPoint();
        pauseTime = Random.Range(Mathf.Max(0f, pauseTimeRange.x), Mathf.Max(pauseTimeRange.x, pauseTimeRange.y));
    }

    private Vector3 MoveTowards(Vector3 target)
    {
        Vector3 position = body.position;
        Vector3 next = Vector3.MoveTowards(position, target, moveSpeed * Time.fixedDeltaTime);
        body.MovePosition(next);
        Vector3 direction = target - position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction, up), turnSpeed * Time.fixedDeltaTime));
        }
        return next;
    }

    private bool IsEligiblePrey(WebCapture capture, out Creature prey)
    {
        prey = null;
        if (capture == null || !capture.isActiveAndEnabled || capture.OwningWeb != CurrentWeb || capture.Body == null ||
            !capture.Body.gameObject.activeInHierarchy || capture.gameObject.scene != gameObject.scene) return false;
        prey = capture.Body.GetComponentInParent<Creature>();
        return prey != null && prey != this && prey.isActiveAndEnabled && prey.gameObject.scene == gameObject.scene &&
            !prey.IsDead && !prey.IsConsumptionPending && !prey.IsCarried && !prey.IsBubbleCaptured && prey.Age > 0f;
    }

    private bool TryGetPreyPoint(WebCapture capture, out Vector3 point, out float distanceSquared)
    {
        point = default;
        distanceSquared = float.PositiveInfinity;
        preyColliders.Clear();
        capture.Body.GetComponentsInChildren(false, preyColliders);
        for (int i = 0; i < preyColliders.Count; i++)
        {
            Collider collider = preyColliders[i];
            if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != capture.Body) continue;
            Vector3 candidate = collider.ClosestPoint(body.position);
            float distance = (candidate - body.position).sqrMagnitude;
            if (distance >= distanceSquared) continue;
            point = candidate;
            distanceSquared = distance;
        }
        return !float.IsPositiveInfinity(distanceSquared);
    }

    private bool HuntCapturedAge()
    {
        bool interrupted = huntCapture != null;
        if (!IsEligiblePrey(huntCapture, out _) || !TryGetPreyPoint(huntCapture, out _, out _))
        {
            huntCapture = null;
            float nearest = float.PositiveInfinity;
            IReadOnlyList<WebCapture> captures = CurrentWeb.Captures;
            for (int i = 0; i < captures.Count; i++)
            {
                WebCapture candidate = captures[i];
                if (!IsEligiblePrey(candidate, out _) || !TryGetPreyPoint(candidate, out _, out float distance)) continue;
                if (distance >= nearest) continue;
                nearest = distance;
                huntCapture = candidate;
            }
            // An interrupted hunt should resume patrol without waiting on its previous pause.
            if (huntCapture == null)
            {
                if (interrupted) { destination = CurrentWeb.SamplePatrolPoint(); pauseTime = 0f; }
                return false;
            }
        }
        pauseTime = 0f;
        if (TryDrainCapturedAge(huntCapture)) return true;
        if (!TryGetPreyPoint(huntCapture, out Vector3 target, out _)) return false;
        destination = target;
        MoveTowards(target);
        return true;
    }

    /// <summary>Spider-only feeding: spend a nearby captive's entire Age, gain it, and trigger age death.</summary>
    public bool TryDrainCapturedAge(WebCapture capture)
    {
        if (!CanHunt || !IsEligiblePrey(capture, out Creature prey) ||
            !TryGetPreyPoint(capture, out _, out float distanceSquared) || distanceSquared > BiteRange * BiteRange) return false;
        float remainingAge = prey.Age;
        // Credit before victim callbacks; its synchronous age death prevents another bite or transfer.
        SetRemainingAge(Age + remainingAge);
        // Also release captures on a child Rigidbody rather than only the Creature's root object.
        capture.Release();
        prey.SetRemainingAge(0f);
        if (this == null) return true;
        huntCapture = null;
        pauseTime = 0f;
        if (HasUsableWeb) destination = CurrentWeb.SamplePatrolPoint();
        return true;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        huntCapture = null;
        wasCarried = false;
    }

    private void OnDestroy()
    {
        if (CurrentWeb != null) CurrentWeb.SetBuilder(null);
    }

    protected override void OnDeath()
    {
        huntCapture = null;
        if (CurrentWeb != null) CurrentWeb.SetBuilder(null);
        CurrentWeb = null;
        IsReturning = false;
        wasCarried = false;
    }
}
