using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The planet's script: every glyph is built from a few radicals that carry meaning.
/// Spine posture sets the class (upright = made thing, slanted = body, curved = growing, none = state/action),
/// ember dots mean heat (count = strength), a bar's height means place or number (see SpireAtHeight),
/// a glyph flipped upside down means its opposite, and a slash through a whole glyph means loss.
/// Strokes use the same box as LandmarkGlyphs: about 1 wide and 1.4 tall, base at y = 0.
/// "spire" and "kiln" stay in LandmarkGlyphs.Get; this holds the rest of the lexicon.
/// </summary>
public static class GlyphLexicon
{
    /// <summary>All lexicon ids, in chart order (spire and kiln live in LandmarkGlyphs).</summary>
    public static IReadOnlyList<string> Ids => ids;

    private static readonly string[] ids =
    {
        // Made things (upright spine).
        "stele", "door", "hearth",
        // Bodies (slanted spine; the wanderer is the one body that stands upright).
        "bone", "wanderer", "tentacle", "fuzz", "bellows",
        // Growing things (curved spine).
        "seed", "shroom", "flower", "grow",
        // States and actions (no spine).
        "heat", "cold", "draft", "fall", "carry", "hunt", "rest", "mirror", "lose",
    };

    private static Dictionary<string, List<Vector2[]>> table;

    public static bool TryGet(string id, out IReadOnlyList<Vector2[]> strokes)
    {
        if (table == null) Build();
        if (id != null && table.TryGetValue(id, out List<Vector2[]> found))
        {
            strokes = found;
            return true;
        }
        strokes = null;
        return false;
    }

    /// <summary>The grammar's opposite: the same strokes flipped upside down inside the glyph box.</summary>
    public static List<Vector2[]> Flip(IReadOnlyList<Vector2[]> strokes)
    {
        var result = new List<Vector2[]>(strokes.Count);
        foreach (Vector2[] stroke in strokes)
        {
            var flipped = new Vector2[stroke.Length];
            for (int i = 0; i < stroke.Length; i++) flipped[i] = new Vector2(stroke[i].x, 1.4f - stroke[i].y);
            result.Add(flipped);
        }
        return result;
    }

    private static Vector2 V(float x, float y) => new Vector2(x, y);
    private static Vector2[] Ring(float x, float y, float r) => LandmarkShapes.Ring(new Vector2(x, y), r, 12);

    private static void Build()
    {
        table = new Dictionary<string, List<Vector2[]>>();

        var bone = new List<Vector2[]>
        {
            new[] { V(-0.45f, 0.1f), V(0.42f, 1.32f) },
            new[] { V(-0.16f, 0.5f), V(-0.4f, 0.38f), V(-0.44f, 0.12f) },
            new[] { V(0.04f, 0.78f), V(-0.18f, 0.62f), V(-0.2f, 0.36f) },
            new[] { V(0.22f, 1.04f), V(0.38f, 0.86f), V(0.4f, 0.6f) },
        };
        var heat = new List<Vector2[]>
        {
            new[] { V(-0.35f, 0.15f), V(0.35f, 0.15f) },
            new[] { V(-0.15f, 0.5f) },
            new[] { V(0.05f, 0.85f) },
            new[] { V(0.2f, 1.2f) },
        };
        var draft = new List<Vector2[]>
        {
            new[] { V(-0.32f, 0.1f), V(-0.08f, 1.02f) },
            new[] { V(0.06f, 0.1f), V(0.3f, 1.02f) },
            new[] { V(-0.26f, 1.04f), V(0.05f, 1.33f), V(0.38f, 1.06f) },
        };

        // Made things.
        table["stele"] = new List<Vector2[]>
        {
            new[] { V(0f, 0.05f), V(0f, 1.35f) },
            new[] { V(-0.3f, 0.05f), V(0.3f, 0.05f) },
            new[] { V(0f, 0.45f), V(0.3f, 0.45f) },
            new[] { V(0f, 0.75f), V(0.3f, 0.75f) },
            new[] { V(0f, 1.05f), V(0.3f, 1.05f) },
        };
        table["door"] = new List<Vector2[]>
        {
            new[] { V(-0.25f, 0.05f), V(-0.25f, 1.0f) },
            new[] { V(0.25f, 0.05f), V(0.25f, 1.0f) },
            new[] { V(-0.38f, 1.0f), V(0.38f, 1.0f) },
            new[] { V(-0.18f, 1.12f), V(0f, 1.32f), V(0.18f, 1.12f) },
        };
        table["hearth"] = new List<Vector2[]>
        {
            new[] { V(-0.45f, 0.35f), V(0.45f, 0.35f) },
            new[] { V(0f, 0.05f), V(0f, 0.35f) },
            new[] { V(-0.18f, 0.65f) },
            new[] { V(0.18f, 0.65f) },
            new[] { V(0f, 0.95f) },
        };

        // Bodies.
        table["bone"] = bone;
        table["wanderer"] = new List<Vector2[]>
        {
            new[] { V(0f, 0.05f), V(0f, 0.85f) },
            Ring(0f, 1.08f, 0.17f),
            new[] { V(-0.28f, 0.52f), V(0f, 0.74f), V(0.28f, 0.52f) },
        };
        table["tentacle"] = new List<Vector2[]>
        {
            new[] { V(-0.42f, 0.12f), V(0.3f, 1.28f) },
            new[] { V(-0.13f, 0.58f), V(-0.4f, 0.52f), V(-0.44f, 0.3f), V(-0.3f, 0.26f) },
            new[] { V(0.05f, 0.87f), V(0.36f, 0.74f), V(0.42f, 0.5f), V(0.28f, 0.44f) },
            new[] { V(0.21f, 1.14f), V(-0.08f, 1.2f), V(-0.2f, 1.36f) },
        };
        table["fuzz"] = new List<Vector2[]>
        {
            new[] { V(-0.3f, 0.1f), V(0.08f, 0.7f) },
            Ring(0.15f, 0.95f, 0.22f),
            new[] { V(0.15f, 1.22f), V(0.15f, 1.34f) },
            new[] { V(0.42f, 1.0f), V(0.52f, 1.04f) },
            new[] { V(-0.12f, 1.05f), V(-0.22f, 1.12f) },
            new[] { V(0.38f, 0.76f), V(0.47f, 0.68f) },
        };
        table["bellows"] = new List<Vector2[]>
        {
            new[] { V(-0.42f, 0.1f), V(0.42f, 1.32f) },
            Ring(0f, 0.71f, 0.22f),
            new[] { V(0.28f, 0.52f), V(0.5f, 0.42f) },
            new[] { V(0.3f, 0.68f), V(0.52f, 0.64f) },
        };

        // Growing things.
        table["seed"] = new List<Vector2[]>
        {
            Ring(0f, 0.5f, 0.22f),
            new[] { V(0f, 0.5f) },
            new[] { V(0f, 0.72f), V(0.1f, 0.95f), V(0.02f, 1.15f), V(-0.06f, 1.3f) },
        };
        table["shroom"] = new List<Vector2[]>
        {
            new[] { V(0f, 0.05f), V(0.08f, 0.35f), V(0.04f, 0.65f), V(-0.02f, 0.92f) },
            new[] { V(-0.42f, 0.92f), V(0f, 1.22f), V(0.42f, 0.92f) },
            new[] { V(-0.2f, 0.92f), V(0.2f, 0.92f) },
        };
        table["flower"] = new List<Vector2[]>
        {
            new[] { V(0f, 0.05f), V(0.1f, 0.4f), V(0.04f, 0.75f), V(-0.04f, 1.0f) },
            new[] { V(0.07f, 0.45f), V(0.3f, 0.55f), V(0.36f, 0.75f) },
            new[] { V(-0.22f, 1.12f) },
            new[] { V(0f, 1.3f) },
            new[] { V(0.22f, 1.12f) },
        };
        table["grow"] = new List<Vector2[]>
        {
            new[] { V(0f, 0.1f), V(0.08f, 0.5f), V(0f, 0.9f) },
            new[] { V(-0.25f, 0.75f), V(0f, 1.0f), V(0.25f, 0.75f) },
            new[] { V(-0.15f, 1.1f), V(0f, 1.3f), V(0.15f, 1.1f) },
        };

        // States and actions.
        table["heat"] = heat;
        table["cold"] = Flip(heat);
        table["draft"] = draft;
        table["fall"] = Flip(draft);
        table["carry"] = new List<Vector2[]>
        {
            Ring(0.05f, 0.45f, 0.18f),
            new[] { V(-0.35f, 0.3f), V(-0.35f, 0.9f), V(-0.05f, 1.12f), V(0.3f, 0.95f), V(0.32f, 0.75f) },
        };
        table["hunt"] = new List<Vector2[]>
        {
            Ring(0f, 0.7f, 0.16f),
            new[] { V(-0.42f, 0.2f), V(-0.3f, 0.7f), V(-0.42f, 1.1f), V(-0.22f, 1.26f) },
            new[] { V(0.42f, 0.2f), V(0.3f, 0.7f), V(0.42f, 1.1f), V(0.22f, 1.26f) },
        };
        table["rest"] = new List<Vector2[]>
        {
            new[] { V(-0.42f, 0.35f), V(0.42f, 0.35f) },
            new[] { V(0f, 0.68f) },
            new[] { V(-0.2f, 1.0f), V(0.2f, 1.0f) },
        };
        table["mirror"] = new List<Vector2[]>
        {
            new[] { V(-0.3f, 1.0f), V(0f, 1.3f), V(0.3f, 1.0f) },
            new[] { V(-0.42f, 0.7f), V(-0.1f, 0.7f) },
            new[] { V(0.1f, 0.7f), V(0.42f, 0.7f) },
            new[] { V(-0.3f, 0.4f), V(0f, 0.1f), V(0.3f, 0.4f) },
        };
        var lose = new List<Vector2[]>(heat) { new[] { V(-0.42f, 1.22f), V(0.42f, 0.28f) } };
        table["lose"] = lose;
    }

    // Domain reload may be off in the editor.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => table = null;
}
