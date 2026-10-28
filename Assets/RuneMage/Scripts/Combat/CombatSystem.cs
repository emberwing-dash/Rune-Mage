using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RuneMage
{
    /// <summary>Player vitals: Ward (health) and Mana. No hit-flash on the screen edges: gentle, comfortable feedback only.</summary>
    public sealed class PlayerWard : MonoBehaviour
    {
        public float MaxWard = 100f, MaxMana = 100f;
        public float Ward { get; private set; } = 100f;
        public float Mana { get; private set; } = 100f;
        public float ManaRegen = 7f;
        public bool Invulnerable;
        public event Action Depleted;
        public event Action<float> Damaged;

        float lastHit;

        void Update()
        {
            Mana = Mathf.Min(MaxMana, Mana + ManaRegen * Time.deltaTime);
            if (Time.time - lastHit > 8f && Ward < MaxWard) Ward = Mathf.Min(MaxWard, Ward + 1.2f * Time.deltaTime);
        }

        public bool Spend(float mana)
        {
            if (Mana < mana) return false;
            Mana -= mana;
            return true;
        }

        public void RestoreMana(float v) => Mana = Mathf.Min(MaxMana, Mana + v);
        public void Heal(float v) => Ward = Mathf.Min(MaxWard, Ward + v);
        public void FullRestore() { Ward = MaxWard; Mana = MaxMana; }

        public void Hurt(float v)
        {
            if (Invulnerable || Ward <= 0f) return;
            Ward = Mathf.Max(0f, Ward - v);
            lastHit = Time.time;
            Damaged?.Invoke(v);
            if (Ward <= 0f) Depleted?.Invoke();
        }
    }

    /// <summary>Active Aegis ward: a glowing disc between the player and the enemies that eats hostile bolts.</summary>
    public sealed class WardShield : MonoBehaviour
    {
        public float Radius = 0.6f;
        public float Life = 6f;
        public bool Reflect;           // Crystal Aegis
        public float ReflectDamage;
        float t;
        Material mat;
        Game game;

        public static WardShield Create(Game g, Vector3 center, Vector3 facingPlayer, bool crystal)
        {
            var go = new GameObject("AegisWard");
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(-facingPlayer, Vector3.up);
            var s = go.AddComponent<WardShield>();
            s.game = g;
            s.Reflect = crystal;
            s.ReflectDamage = crystal ? 10f : 0f;
            s.Life = crystal ? 9f : 6f;
            s.Radius = crystal ? 0.75f : 0.6f;
            Color c = crystal ? new Color(0.7f, 0.95f, 1f) : Magic.Color(MagicType.Barrier);
            var q = Fx.Billboard(go.transform, "disc", Fx.Radial, new Color(c.r, c.g, c.b, 0.35f), s.Radius * 2.1f, 1.0f);
            UnityEngine.Object.Destroy(q.GetComponent<FaceCamera>());
            q.localRotation = Quaternion.identity;
            var rim = Fx.Billboard(go.transform, "rim", Fx.RingTex, c, s.Radius * 2.1f, 2.2f);
            UnityEngine.Object.Destroy(rim.GetComponent<FaceCamera>());
            rim.localRotation = Quaternion.identity;
            s.mat = rim.GetComponent<MeshRenderer>().sharedMaterial;
            g.Combat.Shields.Add(s);
            return s;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (mat != null) mat.SetFloat("_Fade", Mathf.Clamp01((Life - t) / 1f));
            if (t >= Life) Destroy(gameObject);
        }

        /// <summary>True if a bolt at this position is stopped by the ward.</summary>
        public bool Blocks(Vector3 pos)
        {
            Vector3 n = transform.forward;
            float d = Vector3.Dot(pos - transform.position, n);
            if (Mathf.Abs(d) > 0.28f) return false;
            Vector3 lateral = (pos - transform.position) - n * d;
            return lateral.magnitude < Radius * 1.05f;
        }

        void OnDestroy() => game?.Combat.Shields.Remove(this);
    }

    /// <summary>Slow, readable ember projectile. Hits the player's ward unless it is stopped by an Aegis.</summary>
    public sealed class EnemyBolt : MonoBehaviour
    {
        public float Damage = 8f;
        public float Speed = 2.6f;
        public Enemy Owner;
        public bool Big;
        Vector3 vel;
        Game game;
        float life;
        Light light;

        public static EnemyBolt Create(Game g, Vector3 from, Vector3 to, float damage, Enemy owner, float scale, bool big)
        {
            var go = new GameObject("EmberBolt");
            go.transform.position = from;
            var b = go.AddComponent<EnemyBolt>();
            b.game = g; b.Damage = damage; b.Owner = owner; b.Big = big;
            b.Speed = big ? 1.9f : 2.6f;
            b.vel = (to - from).normalized * b.Speed;
            Color c = big ? new Color(1f, 0.8f, 0.4f) : new Color(1f, 0.4f, 0.12f);
            var orb = Fx.Billboard(go.transform, "orb", Fx.Radial, c, 0.28f * scale, 2.4f);
            var core = Fx.Billboard(go.transform, "core", Fx.Radial, new Color(1f, 0.95f, 0.7f), 0.12f * scale, 2.5f);
            b.light = Fx.PointLight(go.transform, c, 1.5f, 2f);
            AudioSynth.PlayAt("ember", from, 0.7f);
            return b;
        }

        void Update()
        {
            life += Time.deltaTime;
            transform.position += vel * Time.deltaTime;
            if (life > 9f) { Destroy(gameObject); return; }

            foreach (var s in game.Combat.Shields)
            {
                if (s != null && s.Blocks(transform.position))
                {
                    Fx.Burst(transform.position, Magic.Color(MagicType.Barrier), 22, 2f, 0.04f, 0.5f);
                    AudioSynth.PlayAt("hit", transform.position, 0.8f, 1.4f);
                    if (s.Reflect && Owner != null && Owner.Alive)
                        Owner.Hit(null, MagicType.Ice, s.ReflectDamage, Owner.Center, -vel.normalized);
                    game.Combat.FloatText(transform.position, "Blocked", Magic.Color(MagicType.Barrier));
                    game.Combat.NotifyBlocked();
                    Destroy(gameObject);
                    return;
                }
            }

            if (Vector3.Distance(transform.position, game.PlayerChest) < 0.4f)
            {
                game.Ward.Hurt(Damage);
                Fx.Burst(transform.position, new Color(1f, 0.5f, 0.2f), 20, 1.5f, 0.04f, 0.5f);
                AudioSynth.PlayAt("hit", transform.position, 0.6f, 0.8f);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>The portal an enemy walks out of. Rendered as a glowing ring on the floor / wall.</summary>
    public sealed class Rift : MonoBehaviour
    {
        public static Rift Create(Vector3 pos, Vector3 normal, float seconds)
        {
            var go = new GameObject("Rift");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(normal == Vector3.zero ? Vector3.up : normal, Vector3.forward == normal ? Vector3.right : Vector3.forward);
            var r = go.AddComponent<Rift>();
            var c = new Color(0.7f, 0.3f, 1f);
            var a = Fx.Billboard(go.transform, "ring", Fx.RingTex, c, 1.5f, 2.2f);
            UnityEngine.Object.Destroy(a.GetComponent<FaceCamera>());
            a.localRotation = Quaternion.identity;
            var b = Fx.Billboard(go.transform, "fill", Fx.Radial, new Color(0.3f, 0.1f, 0.5f, 0.8f), 1.4f, 1.5f);
            UnityEngine.Object.Destroy(b.GetComponent<FaceCamera>());
            b.localRotation = Quaternion.identity;
            var l = Fx.PointLight(go.transform, c, 2f, 3f);
            l.transform.localPosition = Vector3.forward * 0.3f;
            AudioSynth.PlayAt("portal", pos, 1f);
            r.StartCoroutine(r.Life(go.transform, a, seconds));
            return r;
        }

        IEnumerator Life(Transform root, Transform ring, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = t < 0.8f ? t / 0.8f : (t > seconds - 0.8f ? (seconds - t) / 0.8f : 1f);
                root.localScale = Vector3.one * Mathf.SmoothStep(0, 1, k);
                ring.Rotate(0, 0, 90f * Time.deltaTime, Space.Self);
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    public sealed class CombatSystem : MonoBehaviour
    {
        Game game;
        public readonly List<Enemy> Enemies = new List<Enemy>();
        public readonly List<WardShield> Shields = new List<WardShield>();

        public event Action<Enemy> EnemyDied;
        public event Action<Enemy, string> EnemyBark;
        public event Action<Enemy, MagicType, string> Reaction;
        public event Action Blocked;

        public int AliveCount { get { int n = 0; foreach (var e in Enemies) if (e != null && e.Alive) n++; return n; } }

        public void Init(Game g) => game = g;

        public void NotifyReaction(Enemy e, MagicType el, string r) => Reaction?.Invoke(e, el, r);
        public void NotifyBlocked() => Blocked?.Invoke();

        // --- spawning ------------------------------------------------------------------------------------------

        public Enemy Spawn(EnemyKind kind, Vector3? position = null)
        {
            Vector3 pos, normal;
            if (position.HasValue) { pos = position.Value; normal = Vector3.up; }
            else game.Spatial.FindSpawnPoint(out pos, out normal);

            Rift.Create(pos + Vector3.up * 0.02f, Vector3.up, 4f);
            var e = Enemy.Spawn(game, kind, pos);
            Enemies.Add(e);
            e.Died += OnDied;
            e.Barked += (en, line) => EnemyBark?.Invoke(en, line);
            return e;
        }

        public void SpawnWisps(Enemy boss, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 p = boss.transform.position + new Vector3(UnityEngine.Random.Range(-1.4f, 1.4f), 0, UnityEngine.Random.Range(-0.4f, 0.6f));
                p.y = game.Spatial.FloorY;
                Spawn(EnemyKind.Wisp, p);
            }
        }

        void OnDied(Enemy e)
        {
            game.Ward.RestoreMana(14f);
            EnemyDied?.Invoke(e);
        }

        public void FireEnemyBolt(Vector3 from, Vector3 to, float damage, Enemy owner, float scale = 1f, bool big = false)
        {
            EnemyBolt.Create(game, from, to, damage, owner, scale, big);
        }

        public void ClearAll()
        {
            foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
            Enemies.Clear();
            foreach (var b in FindObjectsByType<EnemyBolt>(FindObjectsSortMode.None)) Destroy(b.gameObject);
        }

        // --- queries -------------------------------------------------------------------------------------------

        public Enemy BestTarget(Vector3 origin, Vector3 dir, float maxAngle, float maxRange = 12f)
        {
            Enemy best = null; float bestAngle = maxAngle;
            foreach (var e in Enemies)
            {
                if (e == null || !e.Alive) continue;
                Vector3 to = e.Center - origin;
                if (to.magnitude > maxRange) continue;
                float a = Vector3.Angle(dir, to);
                if (a < bestAngle) { bestAngle = a; best = e; }
            }
            return best;
        }

        public Enemy Nearest(Vector3 p)
        {
            Enemy best = null; float bd = float.MaxValue;
            foreach (var e in Enemies)
            {
                if (e == null || !e.Alive) continue;
                float d = (e.Center - p).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        public void Cleanup() => Enemies.RemoveAll(e => e == null);

        void Update() { if (Time.frameCount % 120 == 0) Cleanup(); }

        // --- floating text -------------------------------------------------------------------------------------

        public void FloatText(Vector3 pos, string text, Color c)
        {
            var go = new GameObject("floatText");
            go.transform.position = pos + Vector3.up * 0.15f;
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = 1.2f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = c;
            t.fontStyle = FontStyles.Bold;
            t.enableWordWrapping = false;
            t.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
            go.AddComponent<FaceCamera>();
            StartCoroutine(Float(go, t));
        }

        IEnumerator Float(GameObject go, TextMeshPro t)
        {
            float k = 0f;
            Vector3 p0 = go.transform.position;
            while (k < 1f && go != null)
            {
                k += Time.deltaTime / 1.1f;
                go.transform.position = p0 + Vector3.up * (0.35f * k);
                var c = t.color; c.a = 1f - k * k; t.color = c;
                yield return null;
            }
            if (go != null) Destroy(go);
        }
    }
}
