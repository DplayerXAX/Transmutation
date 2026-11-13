using UnityEngine;

/// <summary>All catch feedback lives on the held organism; no screen overlay.</summary>
[DefaultExecutionOrder(1100)]
public sealed class FishingPoleVisuals : MonoBehaviour
{
    public Transform tip, vein, bendPivot;
    [SerializeField] float sidewaysBend = 28, verticalBend = 20, bendResponse = 14;
    [SerializeField] float loadBend = 8, recoilBend = 6, recoilAttack = .05f, recoilRecovery = .18f;
    [SerializeField] float successFlash = .25f, pulseDuration = .15f;
    [SerializeField] float catchFlickBend = 32, catchFlickAttack = .1f, catchFlickRecovery = .45f;
    public Renderer veinRenderer;
    FishingPoleCreature pole;
    LineRenderer line;
    Transform lure, pulse;
    Material glow, lineGlow, pulseGlow;
    Quaternion steeringBend = Quaternion.identity;
    float usefulSteering;
    ParticleSystem catchMist;
    Material mistMaterial;
    Texture2D mistTexture;
    public float SteeringFeedback => usefulSteering;
    void Awake()
    {
        pole = GetComponent<FishingPoleCreature>();
        glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")); glow.color = Color.cyan;
        line = new GameObject("Lure Filament").AddComponent<LineRenderer>(); line.transform.SetParent(transform, false);
        lineGlow = new Material(glow); pulseGlow = new Material(glow); pulseGlow.color = Color.white;
        line.sharedMaterial = lineGlow; line.positionCount = 24; line.startWidth = .014f; line.endWidth = .006f;
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "Floating Lure";
        Destroy(orb.GetComponent<Collider>()); lure = orb.transform; lure.SetParent(transform, false);
        orb.GetComponent<Renderer>().sharedMaterial = lineGlow;
        veinRenderer.sharedMaterial = glow;
        var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere); bead.name = "Pull Pulse";
        Destroy(bead.GetComponent<Collider>()); pulse = bead.transform; pulse.SetParent(transform, false);
        bead.GetComponent<Renderer>().sharedMaterial = pulseGlow; pulse.localScale = Vector3.one * .07f;
        pulse.gameObject.SetActive(false);
        CreateCatchMist();
        pole.CatchSucceeded += OnCatchSucceeded;
    }
    void LateUpdate()
    {
        bool charging = pole.State == FishingState.Charging;
        bool active = pole.State != FishingState.Idle && !charging;
        bool struggle = pole.State == FishingState.Reeling;
        bool catching = struggle || pole.State == FishingState.Landing;
        float pullAge = Time.time - pole.LastPullAt;
        float badAge = Time.time - pole.LastRejectedPullAt;
        usefulSteering = Mathf.MoveTowards(usefulSteering, pole.SteeringAccepted ? 1 : 0, Time.deltaTime / .1f);
        float recoil = catching ? Envelope(pullAge, recoilAttack, recoilRecovery) * (1 + pole.LastPullLoad * .2f) : 0;
        float twitch = struggle ? Envelope(badAge, .04f, .1f) : 0;
        float catchAge = Time.time - pole.CatchCommittedAt;
        float catchFlick = pole.State == FishingState.Landing ? Envelope(catchAge, catchFlickAttack, catchFlickRecovery) : 0;
        if (bendPivot != null)
        {
            var cam = pole.Carrier != null ? pole.Carrier.CarryCamera : Camera.main;
            Vector2Int input = struggle ? pole.SteeringInput : Vector2Int.zero;
            Vector3 worldPull = cam != null ? cam.transform.right * input.x + cam.transform.up * input.y : Vector3.zero;
            Vector3 localPull = transform.InverseTransformDirection(worldPull);
            Quaternion bend = Quaternion.Euler(-localPull.y * verticalBend, localPull.x * sidewaysBend, 0);
            steeringBend = Quaternion.Slerp(steeringBend, bend, 1 - Mathf.Exp(-bendResponse * Time.deltaTime));
            Vector3 loadDirection = struggle ? transform.InverseTransformDirection((pole.LurePosition - tip.position).normalized) : Vector3.zero;
            Vector2 loadAxes = Vector2.ClampMagnitude(new Vector2(-loadDirection.y, loadDirection.x), 1);
            float tension = struggle ? Mathf.Max(pole.LineTautness * .4f, pole.NormalizedLoad) : 0;
            bendPivot.localRotation = steeringBend * Quaternion.Euler(loadAxes.x * loadBend * tension - recoilBend * recoil - catchFlickBend * catchFlick + twitch * 1.5f,
                loadAxes.y * loadBend * tension, twitch * 1.5f);
        }
        // Build the line after bending so it remains attached to the animated tip.
        line.enabled = active; lure.gameObject.SetActive(active);
        pulse.gameObject.SetActive(active && catching && pullAge >= 0 && pullAge < pulseDuration);
        if (active)
        {
            Vector3 end = pole.LurePosition;
            lure.position = end;
            lure.localScale = Vector3.one * (.12f + (struggle ? .015f * Mathf.Sin(Time.time * 12) : 0));
            float sag = struggle ? Mathf.Lerp(.3f, .015f, pole.LineTautness) : .12f;
            Vector3 side = Vector3.Cross((end - tip.position).normalized, Vector3.up).normalized;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)(line.positionCount - 1);
                float vibration = struggle ? Mathf.Sin(t * 30 - Time.time * 40) * .012f * pole.NormalizedLoad : 0;
                line.SetPosition(i, Vector3.Lerp(tip.position, end, t) + (Vector3.up * -sag + side * vibration) * Mathf.Sin(t * Mathf.PI));
            }
            if (pulse.gameObject.activeSelf)
            {
                float index = Mathf.Clamp01(pullAge / pulseDuration) * (line.positionCount - 1);
                int low = Mathf.FloorToInt(index);
                pulse.position = Vector3.Lerp(line.GetPosition(low), line.GetPosition(Mathf.Min(low + 1, line.positionCount - 1)), index - low);
            }
            line.startWidth = .014f + usefulSteering * .006f;
        }
        float progress = charging ? pole.CastCharge01 : struggle ? pole.Struggle.Progress / pole.Struggle.Required : (pole.State == FishingState.Landing ? 1 : .05f);
        float thickness = 1 + usefulSteering * .2f;
        vein.localScale = new Vector3(.065f * thickness, .045f * thickness, .05f + progress * .55f);
        Color color = new Color(.25f, .65f, .8f);
        if (charging) color = Color.Lerp(new Color(.4f, .3f, .1f), new Color(1, .8f, .25f), pole.CastCharge01);
        if (struggle)
        {
            color = Color.Lerp(new Color(.06f, .23f, .28f), new Color(.25f, 1, 1), usefulSteering);
            if (pole.Struggle.EmptyTime > 0) color = Color.Lerp(new Color(.2f, 0, 0), Color.red, .5f + .5f * Mathf.Sin(Time.time * 14));
        }
        if (catching && pullAge >= 0 && pullAge < successFlash) color = Color.Lerp(color, Color.white, 1 - pullAge / successFlash);
        if (pole.State == FishingState.Landing && catchAge < .6f) color = Color.Lerp(Color.white, Color.cyan, catchAge / .6f);
        else if (struggle && twitch > 0) color *= 1 - twitch * .35f;
        glow.color = color;
        lineGlow.color = Color.Lerp(new Color(.12f, .4f, .48f), Color.cyan, Mathf.Max(usefulSteering, pole.NormalizedLoad));
    }
    void CreateCatchMist()
    {
        mistTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
            mistTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2)));
        }
        mistTexture.Apply();
        mistMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mistMaterial.SetTexture("_BaseMap", mistTexture); mistMaterial.SetFloat("_Surface", 1);
        mistMaterial.SetFloat("_SrcBlend", 5); mistMaterial.SetFloat("_DstBlend", 1); mistMaterial.SetFloat("_ZWrite", 0);
        mistMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mistMaterial.renderQueue = 3000;
        var root = new GameObject("Catch Dimensional Mist"); root.transform.SetParent(transform, false);
        catchMist = root.AddComponent<ParticleSystem>(); catchMist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = catchMist.main; main.loop = false; main.playOnAwake = false; main.duration = .7f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .65f); main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .75f);
        main.startSize = new ParticleSystem.MinMaxCurve(.08f, .18f); main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = catchMist.emission; emission.enabled = false;
        var shape = catchMist.shape; shape.shapeType = ParticleSystemShapeType.Sphere;
        var size = catchMist.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        catchMist.GetComponent<ParticleSystemRenderer>().sharedMaterial = mistMaterial;
    }
    void OnCatchSucceeded(SeaAngelCreature fish)
    {
        catchMist.transform.position = fish.transform.position;
        var shape = catchMist.shape; shape.radius = fish.BodyLength * .4f;
        var skin = fish.transform.Find("Visual").GetComponentInChildren<Renderer>();
        Color tint = skin.material.GetColor("_BaseColor");
        mistMaterial.SetColor("_BaseColor", Color.Lerp(tint, Color.white, .5f));
        catchMist.Emit(18);
    }
    static float Envelope(float age, float attack, float recovery)
    {
        if (age < 0) return 0;
        return age < attack ? age / Mathf.Max(.001f, attack) : Mathf.Clamp01(1 - (age - attack) / Mathf.Max(.001f, recovery));
    }
    void OnDestroy()
    {
        if (pole != null) pole.CatchSucceeded -= OnCatchSucceeded;
        if (mistMaterial != null) Destroy(mistMaterial);
        if (mistTexture != null) Destroy(mistTexture);
        if (glow != null) Destroy(glow);
        if (lineGlow != null) Destroy(lineGlow);
        if (pulseGlow != null) Destroy(pulseGlow);
    }
}

