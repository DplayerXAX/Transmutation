using UnityEngine;

/// <summary>A single Inspector color picker for the custom column; source materials are never edited.</summary>
[ExecuteAlways]
public sealed class ArtFormationColorTuner : MonoBehaviour
{
    [Header("Try a color on every surface style")]
    [Tooltip("Click this swatch to open Unity's color picker. The Custom column updates in Edit mode and Play mode.")]
    [ColorUsage(false)]
    public Color customColor = new Color(.753f,.545f,1f);

    [SerializeField, HideInInspector] private Renderer[] customColumn = new Renderer[0];
    [SerializeField, HideInInspector] private TextMesh columnLabel;
    private MaterialPropertyBlock properties;
    private Color applied;
    private bool refresh = true;
    private static readonly int PaletteId = Shader.PropertyToID("_AFPalette");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public void Configure(Renderer[] renderers, TextMesh label)
    {
        customColumn = renderers;
        columnLabel = label;
        Apply();
    }

    private void OnEnable() { refresh = true; Apply(); }
    private void OnValidate() { refresh = true; }
    private void Update()
    {
        if (refresh || applied != customColor) Apply();
    }

    private void Apply()
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        foreach (var sample in customColumn)
        {
            if (!sample) continue;
            sample.GetPropertyBlock(properties);
            properties.SetColor(PaletteId, customColor);
            properties.SetColor(BaseColorId, customColor);
            sample.SetPropertyBlock(properties);
        }
        if (columnLabel) columnLabel.text = "Custom\n#" + ColorUtility.ToHtmlStringRGB(customColor);
        applied = customColor;
        refresh = false;
    }

    private void OnDisable()
    {
        foreach (var sample in customColumn) if (sample) sample.SetPropertyBlock(null);
    }
}
