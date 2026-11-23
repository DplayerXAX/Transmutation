using UnityEngine;

/// <summary>
/// Note Seed: what the player gathers in the caves under the crack. A shut stone pod with a faint speck of its note's
/// colour showing through. It sleeps until a following creature of its role comes near (with the baton: its note): then its petals open, the seed
/// rises and lights the rock round it. Walk up to an open pod and the seed flies to you and is counted under its role's
/// glyph at the bottom of the view (no menu or text). Drum seeds make the drums stronger: the heavy Tone Mesas only
/// follow once you hold enough of them, which opens the next places, where more seeds lie.
/// Kept for this play session only, for now.
/// </summary>
public sealed class NoteSeed : MonoBehaviour
{
    private const float HearRange = 14f, TakeRange = 1.8f;

    private CaveNote note;
    public CaveNote Note => note;
    public float Openness => open;
    private readonly Transform[] petals = new Transform[5];
    private Transform seed, revealAt;
    private Material glow;
    private CaveReveal.Light reveal;
    private float open, flight = -1f, phase;
    private bool awake;
    private Vector3 flightFrom;

    public static NoteSeed Create(Transform parent, Vector3 point, Vector3 normal, CaveNote note)
    {
        var go = new GameObject("Note Seed (" + note + ")");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, 0.6f)));
        var s = go.AddComponent<NoteSeed>();
        s.Build(note);
        return s;
    }

    private void Build(CaveNote tone)
    {
        note = tone;
        phase = Random.value * 10f;
        var stone = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Seed pod" };
        stone.SetColor("_BaseColor", new Color(0.1f, 0.095f, 0.11f));
        stone.SetFloat("_Smoothness", 0.3f);
        Color c = CaveGlowCreature.NoteColor(tone);
        glow = new Material(Shader.Find("Capstone/Cave/Glow")) { name = "Seed" };
        glow.SetColor("_Color", c * 2.4f);

        Part(PrimitiveType.Sphere, transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.14f, 0.42f), stone);
        seed = Part(PrimitiveType.Sphere, transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.13f, 0.17f, 0.13f), glow).transform;
        for (int i = 0; i < petals.Length; i++)
        {
            var pivot = new GameObject("Petal").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = Quaternion.Euler(0f, i * 72f, 0f) * new Vector3(0f, 0.07f, 0.13f);
            pivot.localRotation = Quaternion.Euler(0f, i * 72f, 0f);
            Part(PrimitiveType.Sphere, pivot, new Vector3(0f, 0.2f, 0f), new Vector3(0.16f, 0.42f, 0.07f), stone);
            petals[i] = pivot;
        }
        revealAt = new GameObject("Seed light").transform;
        revealAt.SetParent(transform, false);
        revealAt.localPosition = Vector3.up * 0.6f;
        reveal = CaveReveal.Add(revealAt, 3.5f, c, () => open);
        Animate();
    }

    private static GameObject Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Material m)
    {
        var go = VisualPrimitive.Create(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    // Melody seeds answer melody creatures, accompaniment seeds accompaniment, the rest drums.
    private SongRole Role => note == CaveNote.E ? SongRole.Melody : note == CaveNote.G ? SongRole.Accompaniment : SongRole.Drums;

    private void Update()
    {
        ConductorBaton baton = ConductorBaton.Instance;
        CreatureSong song = CreatureSong.Active ? CreatureSong.Instance : null;
        if (baton == null && song == null) return;
        Camera cam = Camera.main;
        if (flight >= 0f) { Fly(baton, song, cam); return; }
        if (cam == null) return;
        float distance = Vector3.Distance(cam.transform.position, transform.position);
        if (!awake)
            awake = song != null ? song.FollowerNear(Role, transform.position, 9f) : baton.Playing && baton.CurrentNote == note && distance < HearRange;
        open = Mathf.MoveTowards(open, awake ? 1f : 0f, Time.deltaTime * 1.8f);
        Animate();
        if (awake && open > 0.8f && (song != null || baton.gameObject.activeInHierarchy) && distance < TakeRange + 1.6f && Horizontal(cam.transform.position) < TakeRange)
        {
            flight = 0f;
            flightFrom = seed.position;
            seed.SetParent(null, true);
        }
    }

    private float Horizontal(Vector3 p)
    {
        p -= transform.position;
        p.y = 0f;
        return p.magnitude;
    }

    private void Fly(ConductorBaton baton, CreatureSong song, Camera cam)
    {
        flight += Time.deltaTime / 0.45f;
        open = Mathf.MoveTowards(open, 0f, Time.deltaTime * 0.6f);
        Vector3 to = baton != null && baton.gameObject.activeInHierarchy ? baton.transform.position : cam != null ? cam.transform.position : flightFrom;
        float t = Mathf.Clamp01(flight);
        seed.position = Vector3.Lerp(flightFrom, to, t * t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.35f;
        seed.localScale = new Vector3(0.13f, 0.17f, 0.13f) * (1f - t * 0.7f);
        if (t < 1f) return;
        if (song != null) song.AddSeed(Role); else baton.AddSeed(note);
        Destroy(seed.gameObject);
        enabled = false;
        CaveReveal.Remove(reveal);
        reveal = null;
    }

    private void Animate()
    {
        float e = Mathf.SmoothStep(0f, 1f, open);
        for (int i = 0; i < petals.Length; i++)
            petals[i].localRotation = Quaternion.Euler(0f, i * 72f, 0f) * Quaternion.Euler(Mathf.Lerp(-16f, 68f, e), 0f, 0f);
        seed.localPosition = new Vector3(0f, Mathf.Lerp(0.2f, 0.62f + Mathf.Sin(Time.time * 2f + phase) * 0.05f, e), 0f);
        glow.SetFloat("_Glow", Mathf.Lerp(0.3f, 1.2f, e));
    }

    private void OnDestroy()
    {
        if (reveal != null) CaveReveal.Remove(reveal);
        if (seed != null && seed.parent == null) Destroy(seed.gameObject);
    }
}
