using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    /// <summary>Material, texture and line factories shared by all visual effects.</summary>
    public static class Fx
    {
        static Texture2D radial, whitePixel, ringTex;
        static Material inkAdditive, glowAdditive;
        static readonly Dictionary<Color, Material> inkByColor = new Dictionary<Color, Material>();

        public static Shader Glow
        {
            get
            {
                var a = RuneMageAssets.Instance;
                if (a != null && a.GlowShader != null) return a.GlowShader;
                return Shader.Find("RuneMage/GlowFlipbook");
            }
        }

        public static Texture2D White
        {
            get
            {
                if (whitePixel == null)
                {
                    whitePixel = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "rm_white" };
                    whitePixel.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                    whitePixel.Apply(false, true);
                }
                return whitePixel;
            }
        }

        /// <summary>Soft radial gradient used for orbs, ring fills and lights.</summary>
        public static Texture2D Radial
        {
            get
            {
                if (radial == null)
                {
                    const int n = 64;
                    radial = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "rm_radial", wrapMode = TextureWrapMode.Clamp };
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(1f - d);
                            a = a * a * (3 - 2 * a);
                            radial.SetPixel(x, y, new Color(1, 1, 1, a));
                        }
                    radial.Apply(false, true);
                }
                return radial;
            }
        }

        /// <summary>Thin glowing annulus (for the seal's rim and pulse waves).</summary>
        public static Texture2D RingTex
        {
            get
            {
                if (ringTex == null)
                {
                    const int n = 128;
                    ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "rm_ring", wrapMode = TextureWrapMode.Clamp };
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.92f) / 0.07f);
                            a = a * a;
                            ringTex.SetPixel(x, y, new Color(1, 1, 1, a));
                        }
                    ringTex.Apply(false, true);
                }
                return ringTex;
            }
        }

        public static Material Make(Texture tex, Color tint, bool additive, float intensity = 1f, int cols = 1, int rows = 1, float fps = 0f, Color? baseColor = null, float phase = 0f, bool vertexColor = false)
        {
            var m = new Material(Glow) { name = "rm_fx" };
            m.mainTexture = tex != null ? tex : White;
            m.SetColor("_Tint", tint);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Cols", cols);
            m.SetFloat("_Rows", rows);
            m.SetFloat("_FPS", fps);
            m.SetFloat("_Phase", phase);
            m.SetColor("_BaseColor", baseColor ?? new Color(0, 0, 0, 0));
            m.SetFloat("_Fade", 1f);
            m.SetFloat("_UseVertexColor", vertexColor ? 1f : 0f);
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.renderQueue = 3000;
            return m;
        }

        public static Material Ink(Color c)
        {
            if (!inkByColor.TryGetValue(c, out var m) || m == null)
            {
                var tr = RuneMageAssets.Instance != null ? RuneMageAssets.Instance.TrailFlow : null;   // Trail VFX texture: flowing streaks along the stroke
                m = Make(tr != null ? tr : White, c, true, tr != null ? 3.0f : 2.2f, 1, 1, 0f, null, 0f, true);
                m.SetFloat("_Floor", tr != null ? 0.6f : 0f);
                inkByColor[c] = m;
            }
            return m;
        }

        public static Material Additive(Texture tex, Color c, float intensity = 1.5f) => Make(tex, c, true, intensity);

        // --- builders -----------------------------------------------------------------------------------------

        public static LineRenderer NewLine(Transform parent, string name, Color c, float width, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.numCapVertices = 6;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Ink(c);
            lr.startColor = lr.endColor = Color.white;
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            return lr;
        }

        public static void SetLineColor(LineRenderer lr, Color c, float intensity = 2.2f)
        {
            lr.sharedMaterial = Ink(c);
        }

        /// <summary>Camera-facing glow sprite. Returns the transform; scale it to size.</summary>
        public static Transform Billboard(Transform parent, string name, Texture tex, Color c, float size, float intensity = 1.4f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Make(tex, c, true, intensity);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<FaceCamera>();
            return go.transform;
        }

        public static Light PointLight(Transform parent, Color c, float intensity, float range)
        {
            var go = new GameObject("light");
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>One-shot particle burst built in code (no prefabs). Uses the glow shader so it matches everything else.</summary>
        public static void Burst(Vector3 pos, Color c, int count = 24, float speed = 2f, float size = 0.05f, float life = 0.7f, float gravity = 0f)
        {
            var go = new GameObject("burst");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = c;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 8;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.04f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Make(Radial, Color.white, true, 1.6f, 1, 1, 0f, null, 0f, true);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            Object.Destroy(go, life + 0.4f);
        }
    }

    /// <summary>Keeps a quad facing the player's eyes.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        Transform cam;
        void LateUpdate()
        {
            if (cam == null) { var c = Camera.main; if (c != null) cam = c.transform; else return; }
            transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);
        }
    }
}
