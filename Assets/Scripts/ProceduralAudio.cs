using UnityEngine;

/// <summary>
/// Generates looping ambience clips at runtime so the disasters have sound without shipping
/// audio assets: a low earthquake rumble, steady rain, and gusting wind. Every clip is a few
/// seconds long and crossfaded onto itself so it loops without a click.
/// </summary>
public static class ProceduralAudio
{
    const int SampleRate = 44100;

    /// <summary>Deep brown-noise rumble. Pitch it down further on the AudioSource for the main shock.</summary>
    public static AudioClip Rumble(float seconds = 6f, int seed = 11)
    {
        var rng = new System.Random(seed);
        int n = Mathf.RoundToInt(seconds * SampleRate);
        var data = new float[n];

        // Two cascaded one-pole low-passes turn white noise into a deep, rolling rumble.
        float a = 0f, b = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            a += (white - a) * 0.012f;
            b += (a - b) * 0.05f;
            data[i] = b;
        }

        Finish(data, "Rumble");
        return Make(data, "ProceduralRumble");
    }

    /// <summary>Steady rain: hissing high-passed noise with scattered heavier drops.</summary>
    public static AudioClip Rain(float seconds = 6f, int seed = 23)
    {
        var rng = new System.Random(seed);
        int n = Mathf.RoundToInt(seconds * SampleRate);
        var data = new float[n];

        float lp = 0f, lp2 = 0f;
        float drop = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.35f;         // remove the harshest top end
            lp2 += (lp - lp2) * 0.02f;          // rumble to subtract, leaving the hiss
            float hiss = (lp - lp2) * 0.6f;

            // Occasional individual drops: a short burst that decays quickly.
            if (rng.NextDouble() < 0.0006) drop = 0.9f;
            drop *= 0.985f;
            float d = drop * (float)(rng.NextDouble() * 2.0 - 1.0);

            data[i] = hiss + d * 0.5f;
        }

        Finish(data, "Rain");
        return Make(data, "ProceduralRain");
    }

    /// <summary>Wind: filtered noise whose loudness and tone swell with slow, irregular gusts.</summary>
    public static AudioClip Wind(float seconds = 8f, int seed = 37)
    {
        var rng = new System.Random(seed);
        int n = Mathf.RoundToInt(seconds * SampleRate);
        var data = new float[n];

        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            // Two sine gusts whose periods divide the clip length, so the loop point is seamless.
            float gust = 0.55f + 0.30f * Mathf.Sin(t * Mathf.PI * 2f * 2f) + 0.15f * Mathf.Sin(t * Mathf.PI * 2f * 5f + 1.3f);

            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            // Gusts open the filter: stronger wind is brighter as well as louder.
            float cutoff = Mathf.Lerp(0.02f, 0.09f, gust);
            lp += (white - lp) * cutoff;
            lp2 += (lp - lp2) * 0.004f;         // very slow component to subtract: keeps it from droning
            data[i] = (lp - lp2) * gust;
        }

        Finish(data, "Wind");
        return Make(data, "ProceduralWind");
    }

    /// <summary>A single thunder clap: a sharp crack decaying into a long rumble. Not looped.</summary>
    public static AudioClip Thunder(float seconds = 4f, int seed = 5)
    {
        var rng = new System.Random(seed);
        int n = Mathf.RoundToInt(seconds * SampleRate);
        var data = new float[n];

        float a = 0f, b = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            a += (white - a) * 0.02f;
            b += (a - b) * 0.08f;

            float crack = Mathf.Exp(-t * 18f);                  // initial snap
            float roll = Mathf.Exp(-t * 0.9f) * (0.6f + 0.4f * Mathf.Sin(t * 7f));
            data[i] = white * crack * 0.5f + b * roll * 4f;
        }

        Normalize(data, 0.9f);
        return Make(data, "ProceduralThunder");
    }

    private static void Finish(float[] data, string _)
    {
        Normalize(data, 0.8f);
        Crossfade(data, Mathf.RoundToInt(0.5f * SampleRate));
    }

    private static void Normalize(float[] data, float peak)
    {
        float max = 1e-5f;
        foreach (var s in data) max = Mathf.Max(max, Mathf.Abs(s));
        float k = peak / max;
        for (int i = 0; i < data.Length; i++) data[i] *= k;
    }

    /// <summary>Blend the tail into the head so the clip loops without a click.</summary>
    private static void Crossfade(float[] data, int fadeSamples)
    {
        int n = data.Length;
        fadeSamples = Mathf.Min(fadeSamples, n / 4);
        for (int i = 0; i < fadeSamples; i++)
        {
            float t = (float)i / fadeSamples;
            int tail = n - fadeSamples + i;
            data[tail] = Mathf.Lerp(data[tail], data[i], t);
        }
    }

    private static AudioClip Make(float[] data, string name)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Attach a looping 2D ambience source, silent until its volume is raised.</summary>
    public static AudioSource AddLoop(GameObject host, AudioClip clip, float volume = 0f)
    {
        var src = host.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = volume;
        return src;
    }
}
