using UnityEngine;

/// <summary>
/// One 3D menu button: a floating shape with a label that loads a scene. When hovered it rises,
/// spins faster, grows a little and its accent pieces glow. MainMenuController sets Hovered.
/// </summary>
public class MainMenuButton : MonoBehaviour
{
    [Header("Target")]
    public string label = "Map";
    [Tooltip("Scene name as listed in Build Settings.")]
    public string sceneName = "Scene_Daniel";
    [Tooltip("Shown when the scene is not in Build Settings yet.")]
    public string unavailableMessage = "This place is not built yet.";

    [Header("Look")]
    [ColorUsage(false, true)] public Color accent = new Color(1f, 0.35f, 0.15f);
    [Tooltip("Part that spins (its children orbit with it).")]
    public Transform shape;
    [Tooltip("Renderers tinted with the accent colour.")]
    public Renderer[] glow;
    public TextMesh text;
    public float spinSpeed = 14f;
    public float hoverRise = 0.35f;

    public bool Hovered { get; set; }

    private float hover;
    private float phase;
    private Vector3 basePosition;
    private Vector3 baseScale = Vector3.one;
    private MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        basePosition = transform.localPosition;
        if (shape != null) baseScale = shape.localScale;
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
    }

    private void Update()
    {
        hover = Mathf.MoveTowards(hover, Hovered ? 1f : 0f, Time.deltaTime * 4f);
        float e = hover * hover * (3f - 2f * hover);

        float bob = Mathf.Sin(Time.time * 0.8f + phase) * 0.12f;
        transform.localPosition = basePosition + Vector3.up * (bob + e * hoverRise);

        if (shape != null)
        {
            shape.Rotate(0f, spinSpeed * (1f + e * 4f) * Time.deltaTime, 0f, Space.Self);
            shape.localScale = baseScale * (1f + e * 0.15f);
        }

        // Dim accent at rest, bright HDR accent on hover (bloom picks it up).
        Color c = Color.Lerp(accent * 0.45f, accent * 2.5f, e);
        c.a = 1f;
        if (glow != null)
        {
            foreach (Renderer r in glow)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }

        if (text != null)
        {
            Color plain = new Color(0.85f, 0.85f, 0.85f, 1f);
            Color lit = accent.maxColorComponent > 1f ? accent / accent.maxColorComponent : accent;
            lit.a = 1f;
            text.color = Color.Lerp(plain, lit, e);
        }
    }
}
