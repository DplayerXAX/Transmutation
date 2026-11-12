using UnityEngine;

/// <summary>All catch feedback lives on the held organism; no screen overlay.</summary>
[DefaultExecutionOrder(1100)]
public sealed class FishingPoleVisuals : MonoBehaviour
{
    public Transform tip, vein, compass;
    public Renderer veinRenderer;
    FishingPoleCreature pole;
    LineRenderer line;
    Transform lure;
    Material glow;
    void Awake()
    {
        pole = GetComponent<FishingPoleCreature>();
        glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")); glow.color = Color.cyan;
        line = new GameObject("Lure Filament").AddComponent<LineRenderer>(); line.transform.SetParent(transform, false);
        line.sharedMaterial = glow; line.positionCount = 12; line.startWidth = .014f; line.endWidth = .006f;
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "Floating Lure";
        Destroy(orb.GetComponent<Collider>()); lure = orb.transform; lure.SetParent(transform, false);
        orb.GetComponent<Renderer>().sharedMaterial = glow;
        veinRenderer.sharedMaterial = glow;
        foreach (var r in compass.GetComponentsInChildren<Renderer>()) r.sharedMaterial = glow;
    }
    void LateUpdate()
    {
        bool charging = pole.State == FishingState.Charging;
        bool active = pole.State != FishingState.Idle && !charging;
        line.enabled = active; lure.gameObject.SetActive(active);
        if (active)
        {
            Vector3 end = pole.LurePosition;
            lure.position = end;
            lure.localScale = Vector3.one * (.12f + (pole.State == FishingState.Reeling ? .035f * Mathf.Sin(Time.time * 12) : 0));
            for (int i = 0; i < 12; i++) { float t = i / 11f; line.SetPosition(i, Vector3.Lerp(tip.position, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * .12f); }
        }
        bool struggle = pole.State == FishingState.Reeling;
        compass.gameObject.SetActive(struggle);
        float progress = charging ? pole.CastCharge01 : struggle ? pole.Struggle.Progress / pole.Struggle.Required : (pole.State == FishingState.Landing ? 1 : .05f);
        vein.localScale = new Vector3(.065f, .045f, .05f + progress * .55f);
        Color color = new Color(.25f, .65f, .8f);
        if (charging) color = Color.Lerp(new Color(.4f, .3f, .1f), new Color(1, .8f, .25f), pole.CastCharge01);
        if (struggle)
        {
            var cam = pole.Carrier != null ? pole.Carrier.CarryCamera : Camera.main;
            Vector2Int direction = pole.Struggle.CounterDirection;
            if (cam != null) compass.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.right * direction.x + cam.transform.up * direction.y);
            color = pole.Struggle.Telegraphing ? new Color(1, .65f, .15f) : Color.cyan;
            if (pole.Struggle.EmptyTime > 0) color = Color.Lerp(new Color(.2f, 0, 0), Color.red, .5f + .5f * Mathf.Sin(Time.time * 14));
            if (Time.time - pole.LastPullAt < .12f) color = Color.white;
        }
        glow.color = color;
    }
    void OnDestroy() { if (glow != null) Destroy(glow); }
}

