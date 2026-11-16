using UnityEngine;

public static class AsuraBloodPalette
{
    public static readonly Color Light = new Color(0.96f, 0.78f, 0.3f);
    public static readonly Color Dark = new Color(0.33f, 0.12f, 0.65f);
    public static Color For(CreaturePolarity polarity) => polarity == CreaturePolarity.Dark ? Dark : Light;
}
