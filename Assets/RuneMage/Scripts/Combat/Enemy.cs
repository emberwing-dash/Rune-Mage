using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RuneMage
{
    public enum EnemyKind { Zombie, ZombieMage, Chandelure, Wisp }

    public sealed class EnemyDef
    {
        public EnemyKind Kind;
        public string Name;
        public float MaxHp;
        public float Speed;
        public float PreferredDistance;     // never approaches closer than this (safety)
        public float AttackInterval;
        public float BoltDamage;
        public float Height;                // normalised model height in metres
        public float HoverHeight;           // 0 = on the floor
        public Dictionary<MagicType, float> Multipliers = new Dictionary<MagicType, float>();
        public string WeaknessHint;
        public string[] Barks;

        public float Mult(MagicType t) => Multipliers.TryGetValue(t, out var m) ? m : 1f;

        public static EnemyDef Get(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Zombie:
                    return new EnemyDef
                    {
                        Kind = k, Name = "Cinder Walker", MaxHp = 70, Speed = 0.42f, PreferredDistance = 2.1f, AttackInterval = 4.2f, BoltDamage = 8,
                        Height = 1.5f,
                        Multipliers = { { MagicType.Fire, 0.3f }, { MagicType.Water, 1.8f }, { MagicType.Ice, 1.6f }, { MagicType.Wind, 1.2f }, { MagicType.Earth, 1.0f }, { MagicType.Lightning, 1.0f } },
                        WeaknessHint = "Flames feed it. Try water or ice."
                    };
                case EnemyKind.ZombieMage:
                    return new EnemyDef
                    {
                        Kind = k, Name = "Ash Warlock", MaxHp = 90, Speed = 0.3f, PreferredDistance = 3.2f, AttackInterval = 3.4f, BoltDamage = 11,
                        Height = 1.7f,
                        Multipliers = { { MagicType.Fire, 0.25f }, { MagicType.Water, 1.3f }, { MagicType.Ice, 1.2f }, { MagicType.Wind, 1.6f }, { MagicType.Lightning, 1.8f }, { MagicType.Earth, 0.9f } },
                        WeaknessHint = "Its fire bolts hit hard. Raise an Aegis ward, then strike with lightning or wind."
                    };
                case EnemyKind.Chandelure:
                    return new EnemyDef
                    {
                        Kind = k, Name = "The Candle Queen", MaxHp = 420, Speed = 0.25f, PreferredDistance = 3.4f, AttackInterval = 3.0f, BoltDamage = 12,
                        Height = 1.3f, HoverHeight = 0.95f,
                        Multipliers = { { MagicType.Fire, 0.15f }, { MagicType.Water, 1.6f }, { MagicType.Ice, 1.3f }, { MagicType.Wind, 1.7f }, { MagicType.Lightning, 1.1f }, { MagicType.Earth, 0.5f } },
                        WeaknessHint = "Her flame shield only breaks to water, ice or wind."
                    };
                default:
                    return new EnemyDef
                    {
                        Kind = EnemyKind.Wisp, Name = "Ember Wisp", MaxHp = 16, Speed = 0.6f, PreferredDistance = 2.6f, AttackInterval = 5f, BoltDamage = 5,
                        Height = 0.35f, HoverHeight = 1.3f,
                        Multipliers = { { MagicType.Fire, 0f }, { MagicType.Water, 2f }, { MagicType.Ice, 2f }, { MagicType.Lightning, 1.5f }, { MagicType.Wind, 1.5f } },
                        WeaknessHint = "A spark of flame. Anything but fire puts it out."
                    };
            }
        }
    }

    /// <summary>A hostile creature from the Ember Court. Walks in through a rift, keeps a safe distance, and throws slow embers.</summary>
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>
        /// Inferno.fbx (the original "Zombie") imports in Unity 6 with its 28-bone body meshes collapsed (mesh bounds ~1 cm against a
        /// 1.8 m skeleton), so by default the Cinder Walker reuses the ZombieMage rig (which imports correctly) minus the cape, driven by
        /// the original Zombie walk / attack / die animations. Set this true if the Inferno.fbx import is fixed.
        /// </summary>
        public static bool UseOriginalZombieMesh = true;

        public EnemyDef Def { get; private set; }
        public float Hp { get; private set; }
        public bool Alive { get; private set; } = true;
        public bool IsBoss => Def.Kind == EnemyKind.Chandelure;
        public float Radius = 0.55f;
        public bool ShieldUp { get; private set; }
        public float ShieldHp { get; private set; }
        public int Phase { get; private set; } = 1;
        public Vector3 Center => transform.position + Vector3.up * (Def.HoverHeight > 0 ? 0f : Def.Height * 0.55f);
        public float HpFraction => Hp / Def.MaxHp;

        public event Action<Enemy> Died;
        public event Action<Enemy, string> Barked;

        Game game;
        Transform model;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] clipPlayables;
        int currentClip = -1;
        readonly List<Material> mats = new List<Material>();
        float attackTimer, slowUntil, stunUntil, nextAbility, spawnT;
        float chargeT = -1f;
        bool spawning = true;
        Light glow;
        Transform bodyFlame;
        Transform shieldVis;
        int strafeDir = 1;
        Vector3 knock;
        MaterialPropertyBlock mpb;

        enum Clip { Walk, Attack, Die, Idle }
        AnimationClip[] clips;

        public static Enemy Spawn(Game g, EnemyKind kind, Vector3 pos)
        {
            var go = new GameObject("Enemy_" + kind);
            go.transform.position = pos;
            var e = go.AddComponent<Enemy>();
            e.Init(g, EnemyDef.Get(kind));
            return e;
        }

        void Init(Game g, EnemyDef def)
        {
            game = g;
            Def = def;
            Hp = def.MaxHp;
            attackTimer = UnityEngine.Random.Range(2.5f, def.AttackInterval);
            nextAbility = Time.time + 8f;
            strafeDir = UnityEngine.Random.value > 0.5f ? 1 : -1;
            BuildModel();
            if (IsBoss) { ShieldUp = true; ShieldHp = 80f; BuildShield(); }
            Radius = Mathf.Max(0.35f, def.Height * 0.38f);
            StartCoroutine(SpawnRoutine());
        }

        // --- visuals --------------------------------------------------------------------------------------------

        void BuildModel()
        {
            var assets = RuneMageAssets.Instance;
            GameObject prefab = null;
            switch (Def.Kind)
            {
                case EnemyKind.Zombie: prefab = UseOriginalZombieMesh ? assets?.ZombieModel : assets?.MageModel; break;
                case EnemyKind.ZombieMage: prefab = assets?.MageModel; break;
                case EnemyKind.Chandelure: prefab = assets?.ChandelureModel; break;
            }

            if (Def.Kind == EnemyKind.Wisp)
            {
                // a drifting spark: layered glow sprites (no mesh needed)
                var w = new GameObject("model").transform;
                w.SetParent(transform, false);
                model = w;
                Fx.Billboard(w, "halo", Fx.Radial, new Color(1f, 0.45f, 0.1f, 0.8f), 0.7f, 1.6f).localPosition = Vector3.up * 0.15f;
                Fx.Billboard(w, "flame", Fx.Radial, new Color(1f, 0.8f, 0.3f), 0.38f, 2.6f).localPosition = Vector3.up * 0.15f;
                Fx.Billboard(w, "core", Fx.Radial, Color.white, 0.16f, 2.8f).localPosition = Vector3.up * 0.15f;
            }
            else if (prefab != null)
            {
                var inst = Instantiate(prefab, transform);
                inst.name = "model";
                model = inst.transform;
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                NormaliseHeight(model, Def.Height);
                if (Def.Kind == EnemyKind.Zombie && !UseOriginalZombieMesh)
                    foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                        if (r.name == "Plane" || r.name == "Circle") r.gameObject.SetActive(false);   // cape and halo: keep walkers distinct from the Warlock
                SkinFlames(model);
            }
            else
            {
                // art missing: readable placeholder so the game remains playable
                var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(transform, false);
                p.transform.localScale = new Vector3(0.5f, Def.Height * 0.5f, 0.5f);
                p.transform.localPosition = Vector3.up * Def.Height * 0.5f;
                p.GetComponent<MeshRenderer>().sharedMaterial = Fx.Make(Fx.Radial, new Color(1f, 0.4f, 0.1f), false, 1.2f);
                model = p.transform;
            }

            if (false)
            {
                // The rigged flame body is authored at an unusual scale; guarantee a readable burning figure with the original
                // flame mesh + flipbook textures, standing under the (smaller) rigged head.
                var body = FlameMeshFx.Build(transform, Def.Height * (Def.Kind == EnemyKind.Wisp ? 1.2f : 0.95f), true);
                if (body != null)
                {
                    body.localPosition = Vector3.up * Def.Height * 0.5f;
                    bodyFlame = body;
                    // flame mesh origin is its centre; keep the base on the floor
                }
            }
            if (Def.Kind != EnemyKind.Wisp) BuildAura();
            glow = Fx.PointLight(transform, new Color(1f, 0.5f, 0.15f), IsBoss ? 0.6f : 1.4f, 3.2f);
            glow.transform.localPosition = Vector3.up * Def.Height * 0.7f;

            var a = assets;
            if (a != null)
            {
                switch (Def.Kind)
                {
                    case EnemyKind.Zombie: clips = new[] { a.ZombieWalk, a.ZombieAttack, a.ZombieDie, a.ZombieWalk }; break;
                    case EnemyKind.ZombieMage: clips = new[] { a.MageCast, a.MageCast, a.MageDead, a.MageCast }; break;
                    default: clips = null; break;
                }
            }
            SetupAnimation();
            // (FitSkinnedModel is experimental and unused)
        }

        /// <summary>
        /// SkinnedMeshRenderer.bounds is stale before the first animation frame (and the Mixamo-rigged flame meshes are authored at 0.01 scale),
        /// so measure the real posed vertices with BakeMesh after a few frames and rescale the model to the intended height.
        /// </summary>
        IEnumerator FitSkinnedModel(Transform m, float targetHeight)
        {
            var rends = new List<Renderer>(m.GetComponentsInChildren<Renderer>());
            foreach (var r in rends) r.enabled = false;
            for (int i = 0; i < 4; i++) yield return null;
            var baked = new Mesh();
            bool any = false;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.gameObject.activeInHierarchy) continue;
                if (smr.name.ToLower().Contains("eye") || smr.name.ToLower().Contains("mouth")) continue;   // thin detail meshes
                smr.BakeMesh(baked, false);
                var verts = baked.vertices;
                var l2w = smr.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    Vector3 w = l2w.MultiplyPoint3x4(v);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                    any = true;
                }
            }
            Destroy(baked);
            if (any)
            {
                float h = Mathf.Max(max.y - min.y, 0.05f);
                float s = Mathf.Clamp(targetHeight / h, 0.2f, 60f);
                m.localScale *= s;
                // stand on the floor: bring the lowest vertex to the enemy root
                float lowest = (min.y - m.position.y) * s + m.position.y;
                m.position += Vector3.up * (transform.position.y - lowest) * (Def.HoverHeight > 0 ? 0f : 1f);
            }
            foreach (var r in rends) if (r != null) r.enabled = true;
        }

        static void NormaliseHeight(Transform t, float targetHeight)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            // skinned meshes may report bind-pose bounds; use the larger of height/width for robustness
            float h = Mathf.Max(b.size.y, 0.01f);
            float s = targetHeight / h;
            if (h < 0.2f || h > 6f) s = targetHeight / Mathf.Max(b.size.x, b.size.z, h);
            t.localScale *= s;
            // stand on the floor: lift so the lowest point sits at y = 0
            rends = t.GetComponentsInChildren<Renderer>();
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            t.position += Vector3.up * (t.parent.position.y - b.min.y);
        }

        void SkinFlames(Transform root)
        {
            var a = RuneMageAssets.Instance;
            if (a == null || Fx.Glow == null) return;
            mpb = new MaterialPropertyBlock();
            float phase = UnityEngine.Random.value * 25f;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;   // imported localBounds are wrong for these rigs
                var slots = r.sharedMaterials;
                var made = new Material[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    string n = (slots[i] != null ? slots[i].name : "").ToLowerInvariant();
                    made[i] = MakeSkin(n, phase + i * 3.7f);
                    if (Def.Kind == EnemyKind.Chandelure) 
                    mats.Add(made[i]);
                }
                r.sharedMaterials = made;
            }
        }

        Material MakeSkin(string slotName, float phase)
        {
            var a = RuneMageAssets.Instance;
            bool eye = slotName.Contains("eye");
            bool mouth = slotName.Contains("mouth");
            Color charred = new Color(0.07f, 0.035f, 0.025f, 1f);
            switch (Def.Kind)
            {
                case EnemyKind.Chandelure:
                {
                    // Mirrors the original Humpty Bumpy materials: chanEye1/2, chanHands1-3, chanHead1/2 are opaque emissive flame
                    // frames; base and flameMouth use the lantern-face texture.
                    int v = slotName.Length > 0 && char.IsDigit(slotName[slotName.Length - 1]) ? slotName[slotName.Length - 1] - '0' : 1;
                    Texture2D tex = null; float intensity = 1.5f;
                    if (eye) tex = v == 2 ? a.ChandelureEyes2 : a.ChandelureEyes;
                    else if (slotName.Contains("blue")) tex = v == 2 ? a.ChandelureHands2 : v == 3 ? a.ChandelureHands3 : a.ChandelureHands;
                    else if (slotName.Contains("orangeflame")) tex = v == 2 ? a.ChandelureHead2 : a.ChandelureHead;
                    else if (slotName == "black" || slotName == "baseblack" || slotName == "frame")
                    {
                        float g = slotName == "frame" ? 0.2f : 0.05f;      // dark iron cage, near-black arms (white texture * tint)
                        var dark = new Color(g, g, g * 1.15f, 1f);
                        return Fx.Make(Fx.White, dark, false, 1f, 1, 1, 0, dark, phase);
                    }
                    else if (slotName.Contains("hand")) tex = v == 2 ? a.ChandelureHands2 : v == 3 ? a.ChandelureHands3 : a.ChandelureHands;
                    else if (slotName.Contains("head")) tex = v == 2 ? a.ChandelureHead2 : a.ChandelureHead;
                    if (tex != null)
                    {
                        // frames are ~80% transparent flame cut-outs and must stay that way: natural texture colours, no fill
                        var fm = Fx.Make(tex, Color.white, false, 1.0f, 5, 5, 16, new Color(0f, 0f, 0f, 0f), phase);
                        fm.SetFloat("_ZWrite", 1f);
                        return fm;
                    }
                    return Fx.Make(a.ChandelureBase != null ? a.ChandelureBase : Fx.White, Color.white, false, 1.1f, 1, 1, 0, new Color(0f, 0f, 0f, 1f), phase);
                }
                default:
                    if (eye) return Fx.Make(a.ZombieEyes, new Color(1f, 0.9f, 0.4f, 1f), true, 2.6f, 5, 5, 16, null, phase);
                    if (mouth) return Fx.Make(a.ZombieMouth, new Color(1f, 0.6f, 0.2f, 1f), true, 2.2f, 5, 5, 16, null, phase);
                    var tint = Def.Kind == EnemyKind.ZombieMage ? new Color(1f, 0.35f, 0.55f, 1f) : new Color(1f, 0.7f, 0.2f, 1f);
                    return Fx.Make(a.ZombieBody, tint, false, 2.4f, 5, 5, 16, charred, phase);
            }
        }

        /// <summary>A soft burning aura and rising embers so every creature reads as "made of fire" even from across the room.</summary>
        void BuildAura()
        {
            Color c = Def.Kind == EnemyKind.Chandelure ? new Color(1f, 0.6f, 0.25f, 0.12f) : new Color(1f, 0.4f, 0.1f, 0.5f);
            var aura = Fx.Billboard(transform, "aura", Fx.Radial, c, 1f, 1.2f);
            aura.localScale = new Vector3(Def.Height * 0.85f, Def.Height * 1.2f, 1f);
            aura.localPosition = Vector3.up * Def.Height * 0.55f;

            var go = new GameObject("embers");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * Def.Height * 0.4f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.3f; main.startSpeed = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(1f, 0.65f, 0.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.12f;
            main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 16;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.5f, Def.Height * 0.7f, 0.3f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Make(Fx.Radial, Color.white, true, 1.8f, 1, 1, 0f, null, 0f, true);
        }

        void BuildShield()
        {
            shieldVis = Fx.Billboard(transform, "flameShield", Fx.Radial, new Color(1f, 0.55f, 0.2f, 0.5f), 2.1f, 1.2f);
            shieldVis.localPosition = Vector3.up * 0.5f;
        }

        // --- animation -----------------------------------------------------------------------------------------

        void SetupAnimation()
        {
            if (clips == null || model == null) return;
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            var valid = new List<AnimationClip>();
            foreach (var c in clips) valid.Add(c);
            graph = PlayableGraph.Create("enemy_" + Def.Kind);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            clipPlayables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                clipPlayables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                graph.Connect(clipPlayables[i], 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            PlayClip(Clip.Walk);
        }

        float[] clipTarget;
        void PlayClip(Clip c)
        {
            if (!graph.IsValid() || clips == null) return;
            int i = (int)c;
            if (i >= clips.Length || clips[i] == null) return;
            if (currentClip == i) { return; }
            currentClip = i;
            if (clipTarget == null) clipTarget = new float[clips.Length];
            for (int k = 0; k < clipTarget.Length; k++) clipTarget[k] = k == i ? 1f : 0f;
            clipPlayables[i].SetTime(0);
            if (c == Clip.Die) clipPlayables[i].SetDuration(clips[i].length);
        }

        void BlendClips()
        {
            if (!graph.IsValid() || clipTarget == null) return;
            for (int k = 0; k < clipTarget.Length; k++)
            {
                float w = Mathf.MoveTowards(mixer.GetInputWeight(k), clipTarget[k], Time.deltaTime * 4f);
                mixer.SetInputWeight(k, w);
            }
        }

        // --- life cycle ----------------------------------------------------------------------------------------

        IEnumerator SpawnRoutine()
        {
            // rise out of the rift
            Vector3 target = transform.position;
            float baseY = target.y;
            Vector3 hidden = target + Vector3.down * 1.6f;
            float t = 0f;
            transform.localScale = Vector3.one * 0.01f;
            while (t < 1.6f)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, t / 1.6f);
                transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, k);
                transform.position = Vector3.Lerp(hidden, target, k);
                yield return null;
            }
            transform.position = target;
            spawning = false;
            Barked?.Invoke(this, null);
        }

        void Update()
        {
            if (!Alive) return;
            BlendClips();
            if (spawning) return;

            var head = game.Hands.Head;
            if (head == null) return;

            float dt = Time.deltaTime;
            Vector3 toPlayer = head.position - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
            float dist = flat.magnitude;
            Vector3 dir = dist > 0.01f ? flat / dist : transform.forward;

            // face the player
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * 4f);

            bool stunned = Time.time < stunUntil;
            float speed = Def.Speed * (Time.time < slowUntil ? 0.35f : 1f) * (stunned ? 0f : 1f);
            Vector3 move = Vector3.zero;
            if (dist > Def.PreferredDistance + 0.15f) move = dir * speed;
            else if (dist < Def.PreferredDistance - 0.25f) move = -dir * Mathf.Max(speed, 0.35f);   // back away: never crowd the player
            else if (IsBoss || Def.Kind == EnemyKind.Wisp) move = Vector3.Cross(Vector3.up, dir) * strafeDir * speed * 0.8f;

            move += knock;
            knock = Vector3.Lerp(knock, Vector3.zero, dt * 4f);

            Vector3 next = transform.position + move * dt;
            if (game.Spatial.IsInsideRoom(new Vector3(next.x, game.Spatial.FloorY + 0.5f, next.z)) && !stunned)
                transform.position = new Vector3(next.x, transform.position.y, next.z);
            if (Def.HoverHeight > 0f)
            {
                float hy = game.Spatial.FloorY + Def.HoverHeight + 0.08f * Mathf.Sin(Time.time * 1.6f + GetInstanceID());
                transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, hy, dt * 3f), transform.position.z);
            }

            if (clips != null) PlayClip(move.sqrMagnitude > 0.0004f && !stunned ? Clip.Walk : (clips.Length > 3 ? Clip.Idle : Clip.Walk));

            // attacks (ranged, slow, telegraphed)
            if (!stunned && chargeT < 0f)
            {
                attackTimer -= dt;
                if (attackTimer <= 0f)
                {
                    attackTimer = Def.AttackInterval * (Phase >= 2 ? 0.75f : 1f) * UnityEngine.Random.Range(0.85f, 1.2f);
                    StartCoroutine(AttackRoutine());
                }
            }
            if (IsBoss) BossLogic();
            if (chargeT >= 0f) ChargeNova(dt);
        }

        IEnumerator AttackRoutine()
        {
            if (!Alive) yield break;
            if (clips != null && clips.Length > 1) PlayClip(Clip.Attack);
            glow.intensity = 4f;
            yield return new WaitForSeconds(0.5f);
            if (!Alive) yield break;
            glow.intensity = 1.4f;
            int shots = Phase >= 2 && IsBoss ? 3 : 1;
            for (int i = 0; i < shots; i++)
            {
                Vector3 from = Center + Vector3.up * 0.25f;
                Vector3 jitter = new Vector3((i - (shots - 1) * 0.5f) * 0.55f, 0, 0);
                game.Combat.FireEnemyBolt(from, game.PlayerChest + jitter, Def.BoltDamage, this);
                yield return new WaitForSeconds(0.25f);
            }
            if (clips != null) currentClip = -1;
        }

        // --- boss ------------------------------------------------------------------------------------------------

        void BossLogic()
        {
            float f = HpFraction;
            int newPhase = f <= 0.33f ? 3 : (f <= 0.66f ? 2 : 1);
            if (newPhase != Phase)
            {
                Phase = newPhase;
                glow.color = Phase == 3 ? new Color(0.5f, 0.7f, 1f) : new Color(1f, 0.4f, 0.1f);
                Barked?.Invoke(this, Phase == 2 ? "The Candle Queen roars: her flames burn hotter! Raise an Aegis ward when she volleys!" :
                                                   "She gathers all her fire. Interrupt the nova with big damage, or hide behind an Aegis!");
                if (Phase >= 2) { ShieldUp = true; ShieldHp = 60f; if (shieldVis == null) BuildShield(); shieldVis.gameObject.SetActive(true); }
            }
            if (Time.time >= nextAbility && !Alive) return;
            if (Time.time >= nextAbility)
            {
                if (Phase == 1 || Phase == 2)
                {
                    nextAbility = Time.time + (Phase == 1 ? 14f : 11f);
                    game.Combat.SpawnWisps(this, Phase == 1 ? 2 : 3);
                }
                else
                {
                    nextAbility = Time.time + 16f;
                    chargeT = 0f;
                    nova = 0f;
                    Barked?.Invoke(this, "Candlelight Nova charging!");
                }
            }
        }

        float nova;
        void ChargeNova(float dt)
        {
            chargeT += dt;
            nova += dt;
            glow.intensity = 1.4f + chargeT * 3f;
            glow.range = 3.2f + chargeT * 1.2f;
            if (shieldVis != null) shieldVis.localScale = Vector3.one * (2.1f + chargeT * 0.5f);
            if (chargeT >= 4f)
            {
                chargeT = -1f;
                glow.intensity = 1.4f; glow.range = 3.2f;
                if (shieldVis != null) shieldVis.localScale = Vector3.one * 2.1f;
                game.Combat.FireEnemyBolt(Center, game.PlayerChest, 30f, this, 2.2f, true);
            }
        }

        // --- damage ----------------------------------------------------------------------------------------------

        public float NovaDamageTaken;

        public void Hit(SpellDef spell, MagicType element, float baseDamage, Vector3 point, Vector3 dir)
        {
            if (!Alive || spawning) return;
            float mult = Def.Mult(element);
            float dmg = baseDamage * mult;

            string reaction = null;
            if (mult >= 1.5f) reaction = "weak";
            else if (mult <= 0.4f) reaction = "resist";

            if (ShieldUp)
            {
                // the flame shield only breaks to water / ice / wind (anything strong against fire)
                bool breaks = element == MagicType.Water || element == MagicType.Ice || element == MagicType.Wind;
                float s = breaks ? dmg : dmg * 0.1f;
                ShieldHp -= s;
                game.Combat.FloatText(point, breaks ? "Shield cracks!" : "Shield holds", breaks ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.6f, 0.3f));
                AudioSynth.PlayAt("hit", point, 0.8f, breaks ? 1.3f : 0.7f);
                Fx.Burst(point, breaks ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.5f, 0.2f), 16, 1.6f, 0.04f, 0.5f);
                if (ShieldHp <= 0f)
                {
                    ShieldUp = false;
                    if (shieldVis != null) shieldVis.gameObject.SetActive(false);
                    Barked?.Invoke(this, "The flame shield shatters! Strike now!");
                }
                if (chargeT >= 0f && breaks) { NovaDamageTaken += s; }
                return;
            }

            Hp -= dmg;
            if (chargeT >= 0f)
            {
                NovaDamageTaken += dmg;
                if (NovaDamageTaken >= 70f)
                {
                    chargeT = -1f; NovaDamageTaken = 0; glow.intensity = 1.4f; glow.range = 3.2f;
                    stunUntil = Time.time + 2.5f;
                    game.Combat.FloatText(Center + Vector3.up * 0.5f, "Nova interrupted!", Color.white);
                }
            }

            if (element == MagicType.Ice) slowUntil = Time.time + 3.5f;
            if (element == MagicType.Earth) stunUntil = Time.time + 1.2f;
            if (element == MagicType.Wind) knock += Vector3.ProjectOnPlane(dir, Vector3.up).normalized * 1.6f;
            if (element == MagicType.Lightning) stunUntil = Mathf.Max(stunUntil, Time.time + 0.5f);

            Color c = Magic.Color(element);
            Fx.Burst(point, c, 22, 2.2f, 0.045f, 0.6f);
            AudioSynth.PlayAt("hit", point, 1f, reaction == "weak" ? 1.25f : reaction == "resist" ? 0.7f : 1f);
            game.Combat.FloatText(point, reaction == "weak" ? Mathf.RoundToInt(dmg) + "!" : reaction == "resist" ? "resisted" : Mathf.RoundToInt(dmg).ToString(), reaction == "weak" ? new Color(1f, 0.95f, 0.4f) : Color.white);
            if (reaction != null) game.Combat.NotifyReaction(this, element, reaction);

            StartCoroutine(Flash());
            if (Hp <= 0f) Die();
        }

        IEnumerator Flash()
        {
            foreach (var m in mats) if (m != null) m.SetFloat("_Intensity", 5f);
            yield return new WaitForSeconds(0.08f);
            foreach (var m in mats) if (m != null) m.SetFloat("_Intensity", 2.4f);
        }

        public void ForceKill()
        {
            if (Alive) Die();
        }

        void Die()
        {
            Alive = false;
            if (graph.IsValid() && clips != null && clips.Length > 2 && clips[2] != null) PlayClip(Clip.Die);
            AudioSynth.PlayAt("enemy_die", Center, 1f);
            Fx.Burst(Center, new Color(1f, 0.5f, 0.15f), 40, 3f, 0.06f, 0.9f);
            Died?.Invoke(this);
            StartCoroutine(DieRoutine());
        }

        IEnumerator DieRoutine()
        {
            float t = 0f;
            while (t < 1.8f)
            {
                t += Time.deltaTime;
                BlendClips();
                float f = Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.8f));
                foreach (var m in mats) if (m != null) m.SetFloat("_Fade", f);
                if (glow != null) glow.intensity = 1.4f * f;
                yield return null;
            }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
            foreach (var m in mats) if (m != null) Destroy(m);
        }
    }
}

