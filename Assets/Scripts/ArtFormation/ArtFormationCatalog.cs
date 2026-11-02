using System;
using UnityEngine;

/// <summary>The source styles, palette, and shared specimens used by the Art Formation board.</summary>
public sealed class ArtFormationCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Swatch
    {
        public string name;
        public string family;
        public Color color;
    }

    [Serializable]
    public sealed class Style
    {
        public string name;
        public string category;
        public string sourceShader;
        public string[] sourceMaterials;
        public Material[] colors;
    }

    public string sourceScene;
    public string[] specimenSources;
    public Mesh[] specimens;
    public Swatch[] palette;
    public Style[] styles;
}
