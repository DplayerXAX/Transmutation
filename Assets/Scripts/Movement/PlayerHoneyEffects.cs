using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Session-local honey effects. Owns honey duration and personal night vision; luminosity visuals are shared.</summary>
[DisallowMultipleComponent]
public sealed class PlayerHoneyEffects : MonoBehaviour
{
    [Min(1f)] [SerializeField] private float sightDuration = 45f;
    [Min(1f)] [SerializeField] private float coatingDuration = 60f;
    [Tooltip("Luminosity added by the temporary honey effect. Refreshing honey does not stack it.")]
    [Min(0f)] [SerializeField] private float coatingLuminosity = 50f;
    [Min(1f)] [SerializeField] private float nestReadDistance = 5f;
    private float sightRemaining, nextScan;
    private HoneyLuminosityEffect coating;
    private PlayerCreatureCarrier carrier;
    private PlayerCreatureStats stats;
    private GlowBeeColony colony;
    private GlowBeeNest nearbyNest;
    private Light sightLight;
    private Volume sightVolume;
    private VolumeProfile profile;
    private GUIStyle panelStyle;
    public bool HasHoneySight => isActiveAndEnabled && sightRemaining > 0f;
    public bool IsScented => isActiveAndEnabled && coating != null && coating.IsActive;
    public float SightRemaining => sightRemaining;
    public float CoatingRemaining => coating != null ? coating.Remaining : 0f;
    public GlowBeeNest NearbyNest => nearbyNest;
    public string NestReadout => nearbyNest == null ? null :
        $"GLOW BEE NEST\nHoney: {nearbyNest.HoneyCount}/{nearbyNest.HoneyCapacity}    Nectar: {nearbyNest.StoredNectar:0.#}\n" +
        (nearbyNest.StoredNectar >= nearbyNest.NectarPerHoney && nearbyNest.HoneyCount < nearbyNest.HoneyCapacity
            ? $"Fermenting: {nearbyNest.FermentationProgress:P0}" : nearbyNest.HoneyCount >= nearbyNest.HoneyCapacity
                ? "Honey storage full" : "Bees are gathering nectar");

    public static PlayerHoneyEffects Ensure(PlayerCreatureCarrier playerCarrier)
    {
        if (playerCarrier == null) return null;
        GameObject owner = playerCarrier.PlayerController != null ? playerCarrier.PlayerController.gameObject : playerCarrier.gameObject;
        var effects = owner.GetComponent<PlayerHoneyEffects>();
        if (effects == null) effects = owner.AddComponent<PlayerHoneyEffects>();
        effects.carrier = playerCarrier;
        effects.stats = owner.GetComponent<PlayerCreatureStats>();
        if (effects.stats == null) effects.stats = owner.AddComponent<PlayerCreatureStats>();
        if (owner.GetComponent<PlayerLuminosityVisuals>() == null) owner.AddComponent<PlayerLuminosityVisuals>();
        return effects;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        bool visible = HasHoneySight && carrier != null && camera == carrier.CarryCamera &&
            (carrier.PlayerController == null || carrier.PlayerController.enabled);
        if (sightLight != null) sightLight.enabled = visible;
        if (sightVolume != null) sightVolume.weight = visible ? 1f : 0f;
    }

    private void EndCamera(ScriptableRenderContext context, Camera camera) => HideSight();

    private void HideSight()
    {
        if (sightLight != null) sightLight.enabled = false;
        if (sightVolume != null) sightVolume.weight = 0f;
    }

    public bool TryApply(GlowHoney.Use use)
    {
        if (!isActiveAndEnabled) return false;
        if (use == GlowHoney.Use.Drink) sightRemaining = Mathf.Max(sightRemaining, sightDuration);
        else if (use == GlowHoney.Use.Coat)
        {
            if (stats == null || !stats.isActiveAndEnabled) return false;
            coating = HoneyLuminosityEffect.Apply(stats, coatingLuminosity, coatingDuration);
        }
        else return false;
        EnsureVisuals();
        RefreshTargets();
        RefreshVisuals();
        return true;
    }

    private void Update()
    {
        // Meditation temporarily disables the ordinary controller; its world should stay undisturbed.
        bool paused = carrier != null && carrier.PlayerController != null && !carrier.PlayerController.enabled;
        if (!paused) AdvanceSightTime(Time.deltaTime);
        RefreshVisuals();
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            RefreshTargets();
        }
        RefreshNearbyNest();
    }

    internal void AdvanceTime(float seconds)
    {
        AdvanceSightTime(seconds);
        if (coating != null) coating.AdvanceTime(seconds);
        RefreshVisuals();
    }

    private void AdvanceSightTime(float seconds)
    {
        bool hadSight = HasHoneySight;
        sightRemaining = Mathf.Max(0f, sightRemaining - Mathf.Max(0f, seconds));
        if (hadSight != HasHoneySight) RefreshTargets();
    }

    private void RefreshTargets()
    {
        foreach (Creature creature in FindObjectsByType<Creature>(FindObjectsSortMode.None))
        {
            if (creature.gameObject.scene != gameObject.scene) continue;
            var visibility = creature.GetComponent<HoneyHiddenCreature>();
            bool tagged = HoneyHiddenCreature.HasTaggedVisual(creature);
            if (visibility == null && tagged) visibility = creature.gameObject.AddComponent<HoneyHiddenCreature>();
            if (visibility != null) visibility.SetSight(HasHoneySight);
        }
    }

    private void RefreshNearbyNest()
    {
        if (colony == null)
            foreach (GlowBeeColony candidate in FindObjectsByType<GlowBeeColony>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { colony = candidate; break; }
        nearbyNest = null;
        if (colony == null) return;
        float nearest = nestReadDistance * nestReadDistance;
        foreach (GlowBeeNest nest in colony.Nests)
        {
            if (nest == null || !nest.isActiveAndEnabled || nest.IsDead) continue;
            float distance = (nest.transform.position - transform.position).sqrMagnitude;
            if (distance > nearest) continue;
            nearest = distance;
            nearbyNest = nest;
        }
    }

    private void EnsureVisuals()
    {
        if (sightLight != null) return;
        var sight = new GameObject("Honey night vision");
        sight.transform.SetParent(transform, false);
        sight.transform.localPosition = Vector3.up * 1.4f;
        sightLight = sight.AddComponent<Light>();
        sightLight.type = LightType.Point;
        sightLight.color = new Color(0.68f, 1f, 0.72f);
        sightLight.range = 28f;
        sightLight.intensity = 1.6f;
        sightLight.shadows = LightShadows.None;
        sightLight.enabled = false;
        sightVolume = sight.AddComponent<Volume>();
        sightVolume.isGlobal = true;
        sightVolume.priority = 100f;
        sightVolume.weight = 0f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        sightVolume.sharedProfile = profile;
        var adjustment = profile.Add<ColorAdjustments>(true);
        adjustment.postExposure.Override(1.25f);
        adjustment.colorFilter.Override(new Color(0.78f, 1f, 0.83f));

    }

    private void RefreshVisuals()
    {
        HideSight();
        GetComponent<PlayerLuminosityVisuals>()?.Refresh();
    }

    private void OnGUI()
    {
        if (BoneAwakeningCutscene.Playing || (carrier != null && carrier.PlayerController != null && !carrier.PlayerController.enabled)) return;
        if (panelStyle == null) panelStyle = new GUIStyle(GUI.skin.box)
        { fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 8, 8), wordWrap = true };
        if (nearbyNest != null) GUI.Box(new Rect(Screen.width * 0.5f - 210f, Screen.height * 0.67f, 420f, 88f), NestReadout, panelStyle);
        string status = HasHoneySight ? $"Honey sight  {Mathf.CeilToInt(sightRemaining)}s" : "";
        if (IsScented) status += (status.Length > 0 ? "\n" : "") + $"Glowing + sweet scent  {Mathf.CeilToInt(CoatingRemaining)}s";
        if (status.Length > 0) GUI.Box(new Rect(20f, Screen.height - 95f, 295f, 72f), status, panelStyle);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        if (coating != null) coating.Cancel();
        sightRemaining = 0f;
        RefreshTargets();
        RefreshVisuals();
        nearbyNest = null;
    }

    private void OnDestroy()
    {
        if (profile != null)
        {
            foreach (var component in profile.components) Destroy(component);
            Destroy(profile);
        }
    }
}
