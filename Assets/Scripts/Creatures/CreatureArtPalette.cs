using UnityEngine;

/// <summary>Visual vocabulary only; colors never determine statistics.</summary>
public static class CreatureArtPalette
{
    public static readonly Color Organic = new Color(.78f,.12f,.22f);
    public static readonly Color OrganicLight = new Color(1,.48f,.43f);
    public static readonly Color Time = new Color(.26f,.85f,.43f);
    public static readonly Color Heat = new Color(.12f,.37f,1);
    public static readonly Color Pearl = new Color(.9f,.91f,.98f);
    public static readonly Color Ink = new Color(.035f,.025f,.065f);
    public static readonly Color LightAsura = new Color(1,.68f,.8f);
    public static readonly Color DarkAsura = new Color(.13f,.035f,.22f);
    public static Color Rotate(Color c) => new Color(c.g,c.b,c.r,c.a);
    public static Color Polarity(CreaturePolarity p) => p == CreaturePolarity.Dark ? DarkAsura : LightAsura;
}
