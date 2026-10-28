using System.Collections.Generic;
using UnityEngine;

/// <summary>A biological light reservoir: a brief strong source becomes a long, portable, gentle lure.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class AfterglowPod : Creature
{
    [Header("Light storage")]
    [SerializeField, Min(0f)] private float detectionRadius = 12f;
    [SerializeField, Min(0f)] private float minimumSourceLuminosity = 300f;
    [SerializeField, Min(0f)] private float minimumChargeBrightness = 10f;
    [SerializeField, Min(0f)] private float releaseDelay = 1.2f;
    [SerializeField, Min(0.1f)] private float glowSeconds = 18f;
    [SerializeField, Min(0f)] private float storedLuminosity = 100f;
    [SerializeField, Min(0f)] private float restingLuminosity = 0.5f;
    private readonly List<LuminosityReading> readings = new List<LuminosityReading>();
    private float delayRemaining, glowRemaining, searchAt;
    public bool HasStoredLight => delayRemaining > 0f || glowRemaining > 0f;
    public bool IsReleasing => delayRemaining <= 0f && glowRemaining > 0f;
    public float GlowRemaining => glowRemaining;

    protected override void InitializeCreature()
    {
        SetCreatureName("Afterglow Pod");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.None);
        SetLuminosity(restingLuminosity);
        GetComponent<Rigidbody>().mass = 0.5f;
        GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
        GetComponent<SphereCollider>().radius = 0.3f;
        Transform core = transform.Find("Stored light");
        if (core != null && !core.TryGetComponent<GlowBeeLight>(out _))
            core.gameObject.AddComponent<GlowBeeLight>().Configure(core.GetComponent<Renderer>(), new Color(0.5f, 1f, 0.65f), 7f);
    }

    public void SenseCharge()
    {
        if (!SimulationEnabled || !IsAvailableForPerception || HasStoredLight) return;
        // Photon storage is a chemical reaction, not vision. Blindness must not stop charging.
        LuminosityQuery.Nearby(gameObject.scene, EmissionPosition, detectionRadius, minimumChargeBrightness,
            null, LuminosityQuery.DefaultObstructionMask, readings);
        foreach (var reading in readings)
        {
            if (ReferenceEquals(reading.Source, this) || reading.EmittedLuminosity < minimumSourceLuminosity) continue;
            delayRemaining = releaseDelay;
            glowRemaining = glowSeconds;
            return;
        }
    }

    protected override void TickCreature(float deltaTime)
    {
        AdvanceLight(deltaTime);
        if (Time.time >= searchAt) { searchAt = Time.time + 0.1f; SenseCharge(); }
    }

    public void AdvanceLight(float seconds)
    {
        if (!SimulationEnabled || !IsAvailableForPerception) return;
        float dt = Mathf.Max(0f, seconds);
        float waiting = Mathf.Min(delayRemaining, dt);
        delayRemaining -= waiting;
        dt -= waiting;
        if (delayRemaining <= 0f) glowRemaining = Mathf.Max(0f, glowRemaining - dt);
        float amount = IsReleasing ? storedLuminosity * Mathf.Lerp(0.2f, 1f, glowRemaining / Mathf.Max(0.1f, glowSeconds)) : restingLuminosity;
        SetLuminosity(amount);
    }

    protected override void OnDeath()
    {
        delayRemaining = glowRemaining = 0f;
        SetLuminosity(0f);
    }
}
