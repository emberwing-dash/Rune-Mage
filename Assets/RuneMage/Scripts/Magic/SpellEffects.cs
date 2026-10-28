using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    /// <summary>Spawns the visual + gameplay result of a released spell.</summary>
    public static class SpellEffects
    {
        public static void Perform(Game g, SpellCanvas.Release r)
        {
            var s = r.Spell;
            float dmg = s.Damage * r.Power;
            Vector3 head = g.Hands.Head.position;
            string sfx = "cast_" + (s.Primary == MagicType.Lightning ? "zap" : s.Primary == MagicType.Barrier ? "ward" : s.Primary.ToString().ToLower());
            if (s.Id == "tempest") sfx = "cast_fire";
            AudioSynth.PlayAt(sfx, r.Origin, 1f, Random.Range(0.95f, 1.05f));
            Fx.Burst(r.Origin, ElementColor(s), 26, 2.5f, 0.05f, 0.6f);

            switch (s.Kind)
            {
                case SpellKind.Projectile:
                    SpellProjectile.Launch(g, s, r.Origin, r.Direction, dmg);
                    break;
                case SpellKind.Bolt:
                    g.StartCoroutine(Lightning(g, s, r.Origin, r.Direction, dmg));
                    break;
                case SpellKind.Burst:
                    g.StartCoroutine(ConeBurst(g, s, r.Origin, r.Direction, dmg));
                    break;
                case SpellKind.Spike:
                    g.StartCoroutine(StoneSpike(g, s, r.Origin, r.Direction, dmg));
                    break;
                case SpellKind.Ward:
                {
                    Vector3 flat = Vector3.ProjectOnPlane(r.Direction, Vector3.up).normalized;
                    Vector3 center = new Vector3(head.x, head.y - 0.2f, head.z) + flat * 0.95f;
                    WardShield.Create(g, center, -flat, s.IsCombo);
                    g.Say(s.Name + " raised.", false);
                    break;
                }
                case SpellKind.Heal:
                    g.Ward.Heal(38f * r.Power);
                    HealGlow(g);
                    g.Say("Warmth floods back into your ward.", false);
                    break;
            }
        }

        public static Color ElementColor(SpellDef s) =>
            s.IsCombo ? Color.Lerp(Magic.Color(s.Primary), Magic.Color(s.Secondary), 0.5f) : Magic.Color(s.Primary);

        /// <summary>Which element of a (possibly combined) spell is most effective on this enemy.</summary>
        public static MagicType BestElement(SpellDef s, Enemy e)
        {
            if (!s.IsCombo) return s.Primary;
            return e.Def.Mult(s.Primary) >= e.Def.Mult(s.Secondary) ? s.Primary : s.Secondary;
        }

        public static float Scale(SpellDef s) => s.IsCombo ? 1.1f : 1f;

        // --- lightning ----------------------------------------------------------------------------------------

        static IEnumerator Lightning(Game g, SpellDef s, Vector3 from, Vector3 dir, float dmg)
        {
            var target = g.Combat.BestTarget(from, dir, 45f);
            var points = new List<Vector3> { from };
            var hit = new List<Enemy>();
            Vector3 end = target != null ? target.Center : from + dir * 4.5f;
            if (target != null) hit.Add(target);

            if (s.IsCombo)   // Storm Surge chains through water
            {
                Enemy last = target;
                for (int i = 0; i < 2 && last != null; i++)
                {
                    Enemy next = null; float bd = 2.2f;
                    foreach (var e in g.Combat.Enemies)
                    {
                        if (e == null || !e.Alive || hit.Contains(e)) continue;
                        float d = Vector3.Distance(e.Center, last.Center);
                        if (d < bd) { bd = d; next = e; }
                    }
                    if (next == null) break;
                    hit.Add(next); last = next;
                }
            }

            var chain = new List<Vector3> { from };
            foreach (var e in hit) chain.Add(e.Center);
            if (hit.Count == 0) chain.Add(end);

            var lr = Fx.NewLine(g.transform, "lightning", Magic.Color(MagicType.Lightning), 0.035f);
            var lr2 = Fx.NewLine(g.transform, "lightning_core", Color.white, 0.012f);
            float t = 0f;
            AudioSynth.PlayAt("cast_zap", end, 1f);
            bool applied = false;
            while (t < 0.28f)
            {
                t += Time.deltaTime;
                var pts = new List<Vector3>();
                for (int i = 0; i < chain.Count - 1; i++)
                {
                    Vector3 a = chain[i], b = chain[i + 1];
                    int seg = 14;
                    for (int k = 0; k < seg; k++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, k / (float)seg);
                        float amp = k == 0 ? 0f : 0.07f;
                        pts.Add(p + Random.insideUnitSphere * amp);
                    }
                }
                pts.Add(chain[chain.Count - 1]);
                lr.positionCount = lr2.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr2.SetPositions(pts.ToArray());
                lr.widthMultiplier = Mathf.Lerp(0.04f, 0.005f, t / 0.28f);
                if (!applied)
                {
                    applied = true;
                    foreach (var e in hit)
                        if (e != null && e.Alive) e.Hit(s, BestElement(s, e), dmg * (s.IsCombo ? 1f : 1f), e.Center, dir);
                    if (hit.Count == 0) Fx.Burst(end, Magic.Color(MagicType.Lightning), 20, 2f, 0.04f, 0.4f);
                }
                yield return null;
            }
            Object.Destroy(lr.gameObject);
            Object.Destroy(lr2.gameObject);
        }

        // --- wind / tempest ---------------------------------------------------------------------------------

        static IEnumerator ConeBurst(Game g, SpellDef s, Vector3 from, Vector3 dir, float dmg)
        {
            Color c = ElementColor(s);
            bool fire = s.Id == "tempest";
            var waves = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                var w = Fx.Billboard(g.transform, "wave", Fx.RingTex, c, 0.3f, 2.2f);
                Object.Destroy(w.GetComponent<FaceCamera>());
                w.position = from;
                w.rotation = Quaternion.LookRotation(dir, Vector3.up);
                waves.Add(w);
            }
            // fiery tempest shows the flame mesh flipbook rolling through the cone
            if (fire) FlameMeshFx.Spawn(g, from, dir, 1.4f, 0.9f);

            var already = new HashSet<Enemy>();
            float t = 0f;
            const float dur = 0.9f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                for (int i = 0; i < waves.Count; i++)
                {
                    float kk = Mathf.Clamp01(k - i * 0.1f);
                    waves[i].position = from + dir * (kk * 4.5f);
                    waves[i].localScale = Vector3.one * (0.3f + kk * 2.6f);
                    var m = waves[i].GetComponent<MeshRenderer>().sharedMaterial;
                    m.SetFloat("_Fade", 1f - kk);
                }
                float range = k * 4.6f;
                foreach (var e in g.Combat.Enemies)
                {
                    if (e == null || !e.Alive || already.Contains(e)) continue;
                    Vector3 to = e.Center - from;
                    if (to.magnitude > range) continue;
                    if (Vector3.Angle(dir, to) > 38f) continue;
                    already.Add(e);
                    e.Hit(s, BestElement(s, e), dmg, e.Center, dir);
                }
                yield return null;
            }
            foreach (var w in waves) Object.Destroy(w.gameObject);
        }

        // --- earth ----------------------------------------------------------------------------------------------

        static IEnumerator StoneSpike(Game g, SpellDef s, Vector3 from, Vector3 dir, float dmg)
        {
            var target = g.Combat.BestTarget(from, dir, 50f);
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            Vector3 pos = target != null ? target.transform.position : g.Hands.Head.position + flat * 2.6f;
            pos.y = g.Spatial.FloorY;

            var spikes = new List<Transform>();
            for (int i = 0; i < 5; i++)
            {
                var sp = SpikeMesh(i == 0 ? 0.28f : 0.16f, i == 0 ? 1.5f : 0.9f);
                sp.position = pos + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(i * 1.6f), 0, Mathf.Sin(i * 1.6f)) * 0.34f);
                sp.localScale = new Vector3(1, 0.01f, 1);
                spikes.Add(sp);
            }
            AudioSynth.PlayAt("cast_earth", pos, 1f);
            float t = 0f;
            bool hit = false;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                foreach (var sp in spikes) sp.localScale = new Vector3(1, Mathf.SmoothStep(0.01f, 1f, t / 0.25f), 1);
                yield return null;
            }
            Fx.Burst(pos + Vector3.up * 0.1f, Magic.Color(MagicType.Earth), 30, 2.4f, 0.06f, 0.7f, 1.5f);
            if (!hit)
            {
                hit = true;
                foreach (var e in g.Combat.Enemies)
                    if (e != null && e.Alive && Vector3.ProjectOnPlane(e.transform.position - pos, Vector3.up).magnitude < 0.95f)
                        e.Hit(s, MagicType.Earth, dmg, e.Center, Vector3.up);
            }
            yield return new WaitForSeconds(1.4f);
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                foreach (var sp in spikes) sp.localScale = new Vector3(1, Mathf.Lerp(1f, 0f, t / 0.5f), 1);
                yield return null;
            }
            foreach (var sp in spikes) Object.Destroy(sp.gameObject);
        }

        static Transform SpikeMesh(float radius, float height)
        {
            var go = new GameObject("spike");
            var mf = go.AddComponent<MeshFilter>();
            var mesh = new Mesh();
            const int n = 5;
            var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
            v.Add(Vector3.up * height); uv.Add(new Vector2(0.5f, 1));
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
                uv.Add(new Vector2(i / (float)n, 0));
            }
            for (int i = 0; i < n; i++) { tri.Add(0); tri.Add(1 + (i + 1) % n); tri.Add(1 + i); }
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Fx.Make(Fx.White, new Color(0.62f, 0.5f, 0.34f, 1f), false, 1.1f, 1, 1, 0, new Color(0.28f, 0.2f, 0.12f, 1f));
            mr.sharedMaterial.SetFloat("_ZWrite", 1f);
            return go.transform;
        }

        // --- heal ---------------------------------------------------------------------------------------------

        static void HealGlow(Game g)
        {
            var head = g.Hands.Head.position;
            for (int i = 0; i < 4; i++)
                Fx.Burst(head + Vector3.down * 0.4f + Random.insideUnitSphere * 0.3f, Magic.Color(MagicType.Heal), 14, 0.8f, 0.05f, 1.2f, -0.3f);
            AudioSynth.PlayAt("cast_heal", head, 0.8f);
        }
    }

    /// <summary>Roaring flame mesh made from the original Inferno-Overdrive spell model + the baked flame flipbooks.</summary>
    public static class FlameMeshFx
    {
        public static Transform Build(Transform parent, float length, bool upright = false)
        {
            var a = RuneMageAssets.Instance;
            if (a == null || a.FlameSpellModel == null || Fx.Glow == null) return null;

            var root = new GameObject("flameMesh").transform;
            root.SetParent(parent, false);
            var inst = Object.Instantiate(a.FlameSpellModel, root);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;

            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Object.Destroy(root.gameObject); return null; }
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // align the mesh's longest axis with +Z so it flies "forward"
            Vector3 sz = b.size;
            if (upright)
            {
                if (sz.x > sz.y && sz.x > sz.z) inst.transform.localRotation = Quaternion.Euler(0, 0, 90);
                else if (sz.z > sz.x && sz.z > sz.y) inst.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            }
            else if (sz.x > sz.y && sz.x > sz.z) inst.transform.localRotation = Quaternion.Euler(0, -90, 0);
            else if (sz.y > sz.x && sz.y > sz.z) inst.transform.localRotation = Quaternion.Euler(90, 0, 0);
            float longest = Mathf.Max(sz.x, sz.y, sz.z);
            inst.transform.localScale = Vector3.one * (length / Mathf.Max(longest, 0.01f));

            var tex = new[] { a.FlameOrange, a.FlameYellow, a.FlameRed };
            var tint = new[] { new Color(1f, 0.7f, 0.3f, 1f), new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.35f, 0.15f, 1f) };
            float phase = Random.value * 25f;
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var slots = r.sharedMaterials;
                var made = new Material[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                    made[i] = Fx.Make(tex[i % 3], tint[i % 3], true, 2.6f, 5, 5, 22, null, phase + i * 6f);
                r.sharedMaterials = made;
            }
            return root;
        }

        /// <summary>A short-lived blast of flame mesh (used by Inferno Tempest).</summary>
        public static void Spawn(Game g, Vector3 pos, Vector3 dir, float length, float life)
        {
            var root = Build(g.transform, length);
            if (root == null) return;
            root.position = pos;
            root.rotation = Quaternion.LookRotation(dir, Vector3.up);
            g.StartCoroutine(Move(root, dir, life));
        }

        static IEnumerator Move(Transform t, Vector3 dir, float life)
        {
            float k = 0f;
            Vector3 p0 = t.position;
            while (k < life && t != null)
            {
                k += Time.deltaTime;
                t.position = p0 + dir * (k / life * 3.5f);
                t.localScale = Vector3.one * (1f + 1.5f * k / life);
                yield return null;
            }
            if (t != null) Object.Destroy(t.gameObject);
        }
    }

    /// <summary>Elemental bolt that homes gently onto the enemy nearest the circle's aim line.</summary>
    public sealed class SpellProjectile : MonoBehaviour
    {
        Game game;
        SpellDef spell;
        float damage;
        Vector3 dir;
        float speed = 5.2f;
        Enemy target;
        float life;
        MagicType element;

        public static void Launch(Game g, SpellDef s, Vector3 from, Vector3 dir, float dmg)
        {
            var go = new GameObject("Spell_" + s.Id);
            go.transform.position = from;
            var p = go.AddComponent<SpellProjectile>();
            p.game = g; p.spell = s; p.damage = dmg; p.dir = dir.normalized; p.element = s.Primary;
            p.target = g.Combat.BestTarget(from, dir, 50f);
            p.BuildVisual();
        }

        void BuildVisual()
        {
            Color c = Magic.Color(element);
            Transform fm = null;
            if (element == MagicType.Fire) fm = FlameMeshFx.Build(transform, 0.55f);
            if (fm != null)
            {
                fm.localRotation = Quaternion.identity;
            }
            else
            {
                Fx.Billboard(transform, "orb", Fx.Radial, c, element == MagicType.Ice ? 0.2f : 0.3f, 2.4f);
                Fx.Billboard(transform, "core", Fx.Radial, Color.white, 0.12f, 2.6f);
            }
            if (element == MagicType.Ice)
            {
                // crystal: two crossed diamonds
                for (int i = 0; i < 2; i++)
                {
                    var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(d.GetComponent<Collider>());
                    d.transform.SetParent(transform, false);
                    d.transform.localScale = new Vector3(0.05f, 0.05f, 0.26f);
                    d.transform.localRotation = Quaternion.Euler(0, 0, 45 + i * 45);
                    d.GetComponent<MeshRenderer>().sharedMaterial = Fx.Make(Fx.White, new Color(0.7f, 0.95f, 1f, 0.9f), true, 2f);
                }
                speed = 6.2f;
            }
            if (element == MagicType.Water) speed = 5.8f;
            Fx.PointLight(transform, c, 1.6f, 2.4f);
            TrailFx(c);
        }

        void TrailFx(Color c)
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.5f;
            main.startSpeed = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 50;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.04f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var r = GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Make(Fx.Radial, Color.white, true, 1.6f, 1, 1, 0f, null, 0f, true);
        }

        void Update()
        {
            life += Time.deltaTime;
            if (life > 5f) { Destroy(gameObject); return; }

            if (target != null && target.Alive)
            {
                Vector3 want = (target.Center - transform.position).normalized;
                dir = Vector3.RotateTowards(dir, want, 2.8f * Time.deltaTime, 0f);
            }
            transform.position += dir * speed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(dir);

            foreach (var e in game.Combat.Enemies)
            {
                if (e == null || !e.Alive) continue;
                if (Vector3.Distance(e.Center, transform.position) < e.Radius + 0.1f)
                {
                    e.Hit(spell, element, damage, transform.position, dir);
                    Destroy(gameObject);
                    return;
                }
            }
        }
    }
}
