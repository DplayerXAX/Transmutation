using UnityEngine;

/// <summary>
/// A plug of living rock in a tunnel, marked with the three role glyphs. It stays shut until the player comes near with
/// melody, accompaniment and drums all following (CreatureSong.FullChord); then it sinks into the floor and stays open.
/// </summary>
public sealed class ChordDoor : MonoBehaviour
{
    private const float HearRange = 18f;

    public bool Open { get; private set; }
    private float sink;
    private Vector3 shut;
    private Collider solid;
    private LineRenderer[] marks;

    public static ChordDoor Create(Transform parent, Vector3 centre, Vector3 facing, float radius, Material rock)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Chord door";
        go.layer = 7;
        go.transform.SetParent(parent, true);
        go.transform.position = centre;
        go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        go.transform.localScale = new Vector3(radius * 2.2f, radius * 2.2f, radius * 1.3f);
        go.GetComponent<MeshRenderer>().sharedMaterial = rock;
        var door = go.AddComponent<ChordDoor>();
        door.shut = centre;
        door.solid = go.GetComponent<Collider>();
        door.Mark(radius);
        // A faint light of its own, so the shut door shows in the dark.
        var glow = new GameObject("Chord door light").transform;
        glow.SetParent(parent, true);
        glow.position = centre + facing.normalized * radius * 1.1f;
        CaveReveal.Add(glow, radius * 1.3f, new Color(0.8f, 0.8f, 0.9f), () => door.Open ? 0f : 0.7f);
        return door;
    }

    private void Mark(float radius)
    {
        var line = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Chord door glyphs" };
        var list = new System.Collections.Generic.List<LineRenderer>();
        for (int r = 0; r < 3; r++)
        {
            Color c = CreatureSong.RoleColor((SongRole)r);
            foreach (Vector2[] stroke in SongGlyphs.Role((SongRole)r))
            {
                var go = new GameObject("Glyph stroke");
                go.transform.SetParent(transform, true);
                go.transform.SetPositionAndRotation(transform.position, transform.rotation);
                var l = go.AddComponent<LineRenderer>();
                l.sharedMaterial = line;
                l.useWorldSpace = false;
                l.alignment = LineAlignment.TransformZ;
                l.widthMultiplier = 0.06f;
                l.positionCount = stroke.Length;
                l.startColor = l.endColor = c;
                float size = radius * 0.2f;
                Vector3 scale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);
                go.transform.localScale = scale;
                for (int k = 0; k < stroke.Length; k++)
                    // The face looks along +z, so x runs right to left for whoever stands before it.
                    l.SetPosition(k, new Vector3(-((r - 1) * radius * 0.3f + stroke[k].x * size), (stroke[k].y - 0.7f) * size, radius * 0.68f));
                list.Add(l);
            }
        }
        marks = list.ToArray();
    }

    private void Update()
    {
        Camera cam = Camera.main;
        if (!Open && cam != null && CreatureSong.FullChord && Vector3.Distance(cam.transform.position, shut) < HearRange)
        {
            Open = true;
            AudioManager.PostEvent("Play_Crate_Thump", gameObject);
        }
        if (!Open || sink >= 1f) return;
        sink = Mathf.MoveTowards(sink, 1f, Time.deltaTime / 2.5f);
        transform.position = shut + Vector3.down * Mathf.SmoothStep(0f, 1f, sink) * transform.lossyScale.y * 1.1f;
        if (sink >= 1f)
        {
            solid.enabled = false;
            foreach (LineRenderer l in marks) l.enabled = false;
            GetComponent<Renderer>().enabled = false;
        }
    }
}
