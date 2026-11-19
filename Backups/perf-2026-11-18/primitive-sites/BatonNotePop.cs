using UnityEngine;

/// <summary>
/// The little note that pops off the conductor baton's tip when the player changes note: a glowing musical note in the
/// note's colour (one to four beamed heads, the same count as the crystals on that note's Tone Mesas) that springs up,
/// floats away and shrinks, with a thin ring opening round it. It moves with the view, so it stays readable while turning.
/// </summary>
public sealed class BatonNotePop : MonoBehaviour
{
    private const float Life = 0.75f;
    private const int RingPoints = 28;

    private Vector3 start;
    private float age;
    private Material glow, lineMaterial;
    private Transform glyph;
    private LineRenderer ring;
    private Color color;

    public static void Spawn(Transform view, Vector3 tipPosition, CaveNote note)
    {
        var go = new GameObject("Baton note pop");
        go.transform.SetParent(view, false);
        go.transform.position = tipPosition + view.up * 0.05f - view.right * 0.035f;
        go.transform.localScale = Vector3.one * 1.45f;
        go.AddComponent<BatonNotePop>().Build(note);
    }

    /// <summary>A larger note in the world over a creature of the chosen note, always facing the view.</summary>
    public static void SpawnOver(Transform view, Vector3 position, CaveNote note)
    {
        var go = new GameObject("Creature note pop");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 3f;
        var pop = go.AddComponent<BatonNotePop>();
        pop.face = view;
        pop.rise = 3f;
        pop.Build(note);
    }

    private Transform face;
    private float rise = 1f;

    private void Build(CaveNote note)
    {
        start = transform.localPosition;
        color = CaveGlowCreature.NoteColor(note);
        glow = new Material(Shader.Find("Capstone/Cave/Glow")) { name = "Note pop" };
        glow.SetColor("_Color", color * 1.25f);
        glow.SetFloat("_Core", 0.85f);
        glow.SetFloat("_Rim", 0.25f);

        glyph = new GameObject("Glyph").transform;
        glyph.SetParent(transform, false);
        int heads = (int)note + 1;
        const float gap = 0.045f, stem = 0.085f;
        float left = -(heads - 1) * gap * 0.5f;
        for (int i = 0; i < heads; i++)
        {
            float x = left + i * gap;
            var head = Part(PrimitiveType.Sphere, new Vector3(x, 0f, 0f), new Vector3(0.034f, 0.024f, 0.012f));
            head.localRotation = Quaternion.Euler(0f, 0f, 22f);
            Part(PrimitiveType.Cube, new Vector3(x + 0.015f, stem * 0.5f, 0f), new Vector3(0.005f, stem, 0.005f));
        }
        if (heads > 1)
            Part(PrimitiveType.Cube, new Vector3(left + (heads - 1) * gap * 0.5f + 0.015f, stem, 0f), new Vector3((heads - 1) * gap + 0.006f, 0.012f, 0.006f));
        else
        {
            var flag = Part(PrimitiveType.Cube, new Vector3(0.03f, stem - 0.012f, 0f), new Vector3(0.03f, 0.009f, 0.005f));
            flag.localRotation = Quaternion.Euler(0f, 0f, -35f);
        }

        lineMaterial = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Note pop ring" };
        var ringObject = new GameObject("Ring");
        ringObject.transform.SetParent(transform, false);
        ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = lineMaterial;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = 0.004f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.alignment = LineAlignment.TransformZ;
        Animate();
    }

    private Transform Part(PrimitiveType type, Vector3 local, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(glyph, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = glow;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        if (age >= Life) { Destroy(gameObject); return; }
        Animate();
    }

    private void Animate()
    {
        float t = age / Life;
        // Springs up past full size at once, then rises and shrinks away.
        float pop = t < 0.18f ? Mathf.Lerp(0.2f, 1.35f, t / 0.18f) : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01((t - 0.18f) / 0.15f));
        float end = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f);
        glyph.localScale = Vector3.one * pop * end;
        glyph.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 7f * (1f - t));
        transform.localPosition = start + new Vector3(-0.025f * t, 0.15f * (1f - (1f - t) * (1f - t)), 0.02f * t) * rise;
        if (face != null) transform.rotation = face.rotation;
        glow.SetFloat("_Glow", Mathf.Lerp(1.3f, 0.4f, t));

        float radius = Mathf.Lerp(0.03f, 0.13f, 1f - (1f - t) * (1f - t));
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i / (float)RingPoints * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius + 0.03f, 0f));
        }
        Color c = color * (1f - t);
        ring.startColor = ring.endColor = c;
    }

    private void OnDestroy()
    {
        if (glow != null) Destroy(glow);
        if (lineMaterial != null) Destroy(lineMaterial);
    }
}
