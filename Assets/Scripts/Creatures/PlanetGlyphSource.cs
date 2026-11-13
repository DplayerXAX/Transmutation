using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets a DropletChoir write the planet's own script (LandmarkGlyphs / GlyphLexicon).
/// A key is a phrase of space-separated tokens, written left to right:
///   flower, shroom, door, ...   a lexicon glyph (any id LandmarkGlyphs knows: spire, kiln, GlyphLexicon.Ids)
///   ~door                       the glyph flipped upside down (the grammar's opposite)
///   spire@0.9                   the spire sign with its height bar at 0..1 (0 = foot, 1 = top)
///   _                           place mark: a ground bar under the whole phrase (a stretch of land)
///   ^                           place mark above the phrase (a place under the ground)
///   U                           place mark as a bowl under the phrase (a hollow, a basin)
/// Place marks follow the grammar's "a bar's height means place". Unknown tokens make the key fail,
/// so the choir falls back to its test font rather than writing a made-up glyph.
/// </summary>
public sealed class PlanetGlyphSource : MonoBehaviour, IDropletGlyphSource
{
    private const float Advance = 1.3f;     // glyph box is about 1 wide, 1.4 tall
    private const float ToFontUnits = 6f / 1.4f; // the choir sizes glyphs by a 6-unit letter height

    /// <summary>Region titles used by RegionTitles (keys in this source's grammar).</summary>
    public const string FlowerField = "flower grow _";
    public const string Cave = "~door ^";
    public const string MushroomBasin = "shroom shroom U";
    public const string Tower = "spire@0.95 _";

    public bool TryGetStrokes(string key, List<Vector2[]> strokes) => TryBuild(key, strokes);

    /// <summary>Static form, so other code can use the phrase grammar without a component.</summary>
    public static bool TryBuild(string key, List<Vector2[]> strokes)
    {
        strokes.Clear();
        if (string.IsNullOrWhiteSpace(key)) return false;
        string[] tokens = key.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        var marks = new List<char>();
        float x = 0f;
        int glyphs = 0;
        foreach (string token in tokens)
        {
            if (token == "_" || token == "^" || token == "U") { marks.Add(token[0]); continue; }
            if (!TryGlyph(token, out IReadOnlyList<Vector2[]> glyph)) { strokes.Clear(); return false; }
            foreach (Vector2[] stroke in glyph)
            {
                var placed = new Vector2[stroke.Length];
                for (int i = 0; i < stroke.Length; i++) placed[i] = new Vector2(stroke[i].x + x, stroke[i].y) * ToFontUnits;
                strokes.Add(placed);
            }
            x += Advance;
            glyphs++;
        }
        if (glyphs == 0) return false;
        float left = -0.55f, right = (glyphs - 1) * Advance + 0.55f;
        foreach (char mark in marks)
        {
            switch (mark)
            {
                case '_':
                    strokes.Add(Scaled(new Vector2(left, -0.25f), new Vector2(right, -0.25f)));
                    break;
                case '^':
                    strokes.Add(Scaled(new Vector2(left, 1.65f), new Vector2(right, 1.65f)));
                    break;
                case 'U':
                {
                    var bowl = new Vector2[17];
                    for (int i = 0; i < bowl.Length; i++)
                    {
                        float t = i / (bowl.Length - 1f);
                        float a = Mathf.Lerp(Mathf.PI * 1.05f, Mathf.PI * 1.95f, t);
                        bowl[i] = new Vector2((left + right) * 0.5f + Mathf.Cos(a) * (right - left) * 0.52f,
                            0.2f + Mathf.Sin(a) * 0.55f) * ToFontUnits;
                    }
                    strokes.Add(bowl);
                    break;
                }
            }
        }
        return true;
    }

    private static bool TryGlyph(string token, out IReadOnlyList<Vector2[]> glyph)
    {
        glyph = null;
        bool flip = token.StartsWith("~");
        string id = flip ? token.Substring(1) : token;
        int at = id.IndexOf('@');
        if (at >= 0)
        {
            if (id.Substring(0, at) != "spire" ||
                !float.TryParse(id.Substring(at + 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float height)) return false;
            glyph = LandmarkGlyphs.SpireAtHeight(height);
        }
        else if (id == "spire" || id == "kiln") glyph = LandmarkGlyphs.Get(id);
        else if (!GlyphLexicon.TryGet(id, out glyph)) return false;
        if (flip) glyph = GlyphLexicon.Flip(glyph);
        return true;
    }

    private static Vector2[] Scaled(Vector2 a, Vector2 b) => new[] { a * ToFontUnits, b * ToFontUnits };
}
