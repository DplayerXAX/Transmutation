using UnityEngine;

/// <summary>
/// Builds simple looping clips so the music system can be heard before real music exists.
/// Voice 0 is a pad for the base, voices 1+ are layers, 100+ are short blips for sounds.
/// All music voices share one loop length and chord progression so they stack in sync.
/// </summary>
public static class PlaceholderSynth
{
    private const int Rate = 44100;

    // Dm, Bb, F, C (MIDI notes)
    private static readonly int[][] Chords =
    {
        new[] { 50, 57, 62, 65 },
        new[] { 46, 53, 58, 62 },
        new[] { 53, 57, 60, 65 },
        new[] { 48, 55, 60, 64 },
    };

    public static AudioClip Build(string name, int voice, AudioEventBank bank)
    {
        if (voice >= 100) return Blip(name, voice - 100);

        float beat = 60f / bank.bpm;
        int beatsPerBar = bank.beatsPerBar;
        int totalBeats = bank.bars * beatsPerBar;
        int length = Mathf.RoundToInt(totalBeats * beat * Rate);
        var data = new float[length];
        var rng = new System.Random(voice * 7919 + 13);

        for (int bar = 0; bar < bank.bars; bar++)
        {
            int[] chord = Chords[bar % Chords.Length];
            float barStart = bar * beatsPerBar * beat;
            float barLength = beatsPerBar * beat;

            switch (voice)
            {
                case 0: // pad
                    foreach (int n in chord) Pad(data, barStart, barLength * 1.25f, Freq(n - 12), 0.18f);
                    break;
                case 1: // bells, eighth notes
                    for (int s = 0; s < beatsPerBar * 2; s++)
                        if (rng.NextDouble() < 0.6)
                            Bell(data, barStart + s * beat * 0.5f, Freq(Pick(chord, rng) + 12), 0.25f, 1.6f);
                    break;
                case 2: // low pulse on beats
                    for (int s = 0; s < beatsPerBar; s++)
                        Bell(data, barStart + s * beat, Freq(chord[0] - 12), 0.45f, 0.9f);
                    break;
                case 3: // sparse high shimmer
                    for (int s = 0; s < beatsPerBar * 4; s++)
                        if (rng.NextDouble() < 0.22)
                            Bell(data, barStart + s * beat * 0.25f, Freq(Pick(chord, rng) + 24), 0.12f, 2.5f);
                    break;
                case 4: // offbeat plucks
                    for (int s = 0; s < beatsPerBar; s++)
                        Bell(data, barStart + (s + 0.5f) * beat, Freq(Pick(chord, rng)), 0.3f, 0.35f);
                    break;
                default: // slow high voices
                    Pad(data, barStart, barLength * 1.1f, Freq(Pick(chord, rng) + 12 + (voice % 3) * 7), 0.14f);
                    break;
            }
        }

        Normalize(data, 0.5f);
        var clip = AudioClip.Create(name, length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip Blip(string name, int voice)
    {
        int length = Rate / 3;
        var data = new float[length];
        Bell(data, 0f, Freq(74 + (voice % 5) * 3), 0.6f, 0.25f, false);
        Normalize(data, 0.6f);
        var clip = AudioClip.Create(name, length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static int Pick(int[] chord, System.Random rng) => chord[rng.Next(chord.Length)];

    private static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

    // Decaying sine with one overtone. Writes wrap so the loop is seamless.
    private static void Bell(float[] data, float start, float freq, float gain, float decay, bool wrap = true)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int count = Mathf.RoundToInt(decay * 4f * Rate);
        for (int i = 0; i < count; i++)
        {
            int idx = s0 + i;
            if (wrap) idx %= data.Length;
            else if (idx >= data.Length) break;
            float t = i / (float)Rate;
            float env = Mathf.Exp(-t / decay) * Mathf.Min(1f, t * 200f);
            float w = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * freq * t);
            data[idx] += w * env * gain;
        }
    }

    // Soft detuned sine with slow attack and release.
    private static void Pad(float[] data, float start, float duration, float freq, float gain)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int count = Mathf.RoundToInt(duration * Rate);
        for (int i = 0; i < count; i++)
        {
            int idx = (s0 + i) % data.Length;
            float t = i / (float)Rate;
            float u = i / (float)count;
            float env = Mathf.Sin(Mathf.PI * u);
            env *= env;
            float w = Mathf.Sin(2f * Mathf.PI * freq * t) + Mathf.Sin(2f * Mathf.PI * freq * 1.003f * t);
            data[idx] += w * env * gain;
        }
    }

    private static void Normalize(float[] data, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
        float k = peak / max;
        for (int i = 0; i < data.Length; i++) data[i] *= k;
    }
}
