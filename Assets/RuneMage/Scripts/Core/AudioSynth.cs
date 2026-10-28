using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    /// <summary>
    /// Generates every sound effect procedurally at startup (no audio assets, no licensing): chimes, whooshes, zaps,
    /// error buzzes. Short mono clips at 22.05 kHz to stay light on standalone hardware.
    /// </summary>
    public static class AudioSynth
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string name)
        {
            if (cache.TryGetValue(name, out var c) && c != null) return c;
            c = Build(name);
            cache[name] = c;
            return c;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(f(t, seconds), -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Env(float t, float dur, float attack = 0.01f)
        {
            float a = Mathf.Clamp01(t / attack);
            float r = Mathf.Clamp01((dur - t) / (dur * 0.6f));
            return a * r * r;
        }

        static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        static readonly System.Random rng = new System.Random(1234);
        static float Noise() => (float)(rng.NextDouble() * 2 - 1);

        static AudioClip Build(string name)
        {
            switch (name)
            {
                case "ink":      // soft shimmer when a stroke begins
                    return Make(name, 0.18f, (t, d) => (Sine(1320, t) * 0.25f + Sine(1980, t) * 0.12f) * Env(t, d, 0.005f));
                case "ring":     // seal closes: rising chord
                    return Make(name, 0.9f, (t, d) => (Sine(440, t) + Sine(660, t) * 0.8f + Sine(880 + 220 * t, t) * 0.6f) * 0.22f * Env(t, d, 0.02f));
                case "glyph":    // rune accepted: bright two-note bell
                    return Make(name, 0.7f, (t, d) => ((t < 0.18f ? Sine(784, t) : Sine(1175, t)) + Sine(2349, t) * 0.25f) * 0.3f * Env(t, d, 0.004f));
                case "ready":    // circle fully charged
                    return Make(name, 0.8f, (t, d) => (Sine(523, t) + Sine(659, t) + Sine(784, t) + Sine(1046, t) * 0.7f) * 0.17f * Env(t, d, 0.03f));
                case "error":    // invalid rune / open circle
                    return Make(name, 0.35f, (t, d) => (Sine(180 - 60 * t, t) * 0.6f + Noise() * 0.12f) * Env(t, d, 0.004f));
                case "cast_fire":
                    return Make(name, 0.9f, (t, d) => (Noise() * 0.5f * Mathf.Clamp01(1f - t / d) + Sine(110 + 260 * t, t) * 0.4f) * Env(t, d, 0.02f));
                case "cast_water":
                    return Make(name, 0.7f, (t, d) => (Noise() * 0.28f + Sine(300 + 500 * t, t) * 0.25f) * Env(t, d, 0.03f));
                case "cast_wind":
                    return Make(name, 1.0f, (t, d) => Noise() * 0.5f * Mathf.Sin(Mathf.PI * t / d) * Env(t, d, 0.05f));
                case "cast_zap":
                    return Make(name, 0.5f, (t, d) => (Noise() * 0.7f * (Mathf.Sin(t * 180f) > 0 ? 1 : 0.25f) + Sine(90, t) * 0.5f) * Env(t, d, 0.002f));
                case "cast_earth":
                    return Make(name, 0.7f, (t, d) => (Sine(60 - 20 * t, t) * 0.9f + Noise() * 0.25f * Mathf.Clamp01(1f - t * 4f)) * Env(t, d, 0.004f));
                case "cast_ice":
                    return Make(name, 0.6f, (t, d) => (Sine(2093, t) * 0.25f + Sine(3136, t) * 0.18f + Noise() * 0.1f) * Env(t, d, 0.002f));
                case "cast_ward":
                    return Make(name, 1.0f, (t, d) => (Sine(330, t) + Sine(495, t) * 0.7f + Sine(660, t) * 0.4f) * 0.22f * Env(t, d, 0.08f));
                case "cast_heal":
                    return Make(name, 1.2f, (t, d) => (Sine(523 + 130 * t, t) + Sine(784 + 190 * t, t) * 0.6f) * 0.2f * Env(t, d, 0.1f));
                case "hit":
                    return Make(name, 0.25f, (t, d) => (Noise() * 0.5f + Sine(140, t) * 0.5f) * Env(t, d, 0.002f));
                case "enemy_die":
                    return Make(name, 0.8f, (t, d) => (Sine(220 - 170 * t, t) * 0.5f + Noise() * 0.25f * (1f - t / d)) * Env(t, d, 0.005f));
                case "portal":
                    return Make(name, 1.4f, (t, d) => (Sine(80 + 140 * t, t) * 0.5f + Sine(160 + 280 * t, t) * 0.25f + Noise() * 0.08f) * Env(t, d, 0.3f));
                case "listen":   // mic opened (very quiet tick)
                    return Make(name, 0.12f, (t, d) => Sine(1500, t) * 0.12f * Env(t, d, 0.002f));
                case "quest":
                    return Make(name, 1.1f, (t, d) => ((t < 0.25f ? Sine(523, t) : t < 0.5f ? Sine(659, t) : Sine(784, t)) + Sine(1568, t) * 0.2f) * 0.3f * Env(t, d, 0.005f));
                case "ember":    // enemy bolt launch
                    return Make(name, 0.5f, (t, d) => (Noise() * 0.3f + Sine(200 - 100 * t, t) * 0.4f) * Env(t, d, 0.01f));
                default:
                    return Make(name, 0.2f, (t, d) => Sine(600, t) * 0.2f * Env(t, d));
            }
        }

        /// <summary>Plays a clip as a 3D one-shot at a world position.</summary>
        public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            var go = new GameObject("sfx_" + name);
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = Get(name);
            a.spatialBlend = 1f;
            a.minDistance = 0.6f;
            a.maxDistance = 12f;
            a.volume = volume;
            a.pitch = pitch;
            a.Play();
            Object.Destroy(go, a.clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }
    }
}
