using System.Collections.Generic;
using UnityEngine;

/// <summary>Carryable placeholder spider. Roams in its web volume and builds new territory after distant drops.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class SkySpider : Creature
{
    [Header("Web")]
    [SerializeField] private SkyWeb webPrefab;
    [Min(0f)] [SerializeField] private float returnRange = 12f;

    [Header("Movement")]
    [Min(0f)] [SerializeField] private float moveSpeed = 1.2f;
    [Min(0f)] [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private Vector2 pauseTimeRange = new Vector2(0.4f, 1.6f);

    public SkyWeb CurrentWeb { get; private set; }
    public bool IsReturning { get; private set; }
    public int CreatedWebCount => webs.Count;

    private readonly List<SkyWeb> webs = new List<SkyWeb>();
    private Rigidbody body;
    private Vector3 destination;
    private float pauseTime;
    private bool wasCarried;

    protected override void InitializeCreature()
    {
        if (!Application.isPlaying) return;
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
        web.name = "Sky Web " + (webs.Count + 1);
        web.InitializeGeometry();
        webs.Add(web);
        SetCurrentWeb(web, false);
    }

    private void SetCurrentWeb(SkyWeb web, bool returning)
    {
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
        if (IsCarried) return;
        if (released) SelectWebAfterRelease();
        if (CurrentWeb == null) CreateWeb(body.position);
    }

    private void SelectWebAfterRelease()
    {
        SkyWeb nearest = null;
        float nearestDistance = returnRange * returnRange;
        for (int i = webs.Count - 1; i >= 0; i--)
        {
            SkyWeb web = webs[i];
            if (web == null) { webs.RemoveAt(i); continue; }
            if (!web.isActiveAndEnabled) continue;
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
        if (!SimulationEnabled || IsCarried || body == null || CurrentWeb == null) return;
        if (pauseTime > 0f) { pauseTime -= Time.fixedDeltaTime; return; }
        Vector3 position = body.position;
        Vector3 next = Vector3.MoveTowards(position, destination, moveSpeed * Time.fixedDeltaTime);
        body.MovePosition(next);
        Vector3 direction = destination - position;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction, up), turnSpeed * Time.fixedDeltaTime));
        }
        if ((next - destination).sqrMagnitude > 0.01f) return;
        IsReturning = false;
        destination = CurrentWeb.SamplePatrolPoint();
        pauseTime = Random.Range(Mathf.Max(0f, pauseTimeRange.x), Mathf.Max(pauseTimeRange.x, pauseTimeRange.y));
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        wasCarried = false;
    }

    private void OnDestroy()
    {
        if (CurrentWeb != null) CurrentWeb.SetBuilder(null);
    }
}
