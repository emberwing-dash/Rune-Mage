using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    /// <summary>
    /// Dresses the passthrough room: a few dead trees around the edge of the play space and a stone stand with a lit candle,
    /// using the Pumpkin SoulDew models. Props are placed on the MRUK floor (or an arc when there is no room data), outside the
    /// space where enemies fight, and are lit with unlit dark materials so they sit naturally against the real room.
    /// </summary>
    public sealed class Environment : MonoBehaviour
    {
        Game game;
        float placedAt = -1f;
        bool built;

        public void Init(Game g) { game = g; }

        void Update()
        {
            if (built || game == null || game.Hands.Head == null) return;
            // wait a moment for MRUK to load the room (or give up and use the fallback layout)
            if (placedAt < 0f) placedAt = Time.time;
            if (game.Spatial.HasRoom || Time.time - placedAt > 4f) Build();
        }

        void Build()
        {
            built = true;
            if (transform.childCount > 0) { Adapt(); return; }          // props are authored in the scene: only fit them to the room
            var a = RuneMageAssets.Instance;
            
            if (a == null) return;
            var head = game.Hands.Head;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
            float floor = game.Spatial.FloorY;
            var root = new GameObject("Environment").transform;

            // dead trees on the far side of the room, spread across a 200 degree arc
            float[] angles = { -80f, -35f, 25f, 75f };
            float[] heights = { 2.6f, 3.2f, 2.3f, 2.9f };
            for (int i = 0; i < angles.Length; i++)
            {
                var src = a.EnvTrees != null && a.EnvTrees.Length > 0 ? a.EnvTrees[i % a.EnvTrees.Length] : null;
                if (src == null) continue;
                Vector3 dir = Quaternion.AngleAxis(angles[i], Vector3.up) * fwd;
                Vector3 pos = head.position + dir * (4.4f + 0.4f * (i % 2));
                pos.y = floor;
                if (!game.Spatial.IsInsideRoom(new Vector3(pos.x, floor + 0.5f, pos.z)))
                    pos = head.position + dir * 2.9f; pos.y = floor;
                Place(src, root, pos, heights[i], new Color(0.2f, 0.15f, 0.12f), Quaternion.Euler(0, Random.Range(0, 360f), 0));
            }

            // the stand: a stone plate with a candle, to the player's left-front
            Vector3 sdir = Quaternion.AngleAxis(-28f, Vector3.up) * fwd;
            Vector3 sp = head.position + sdir * 2.2f; sp.y = floor;
            if (!game.Spatial.IsInsideRoom(new Vector3(sp.x, floor + 0.5f, sp.z))) { sp = head.position + sdir * 1.6f; sp.y = floor; }
            var plate = Place(a.StandPlate, root, sp, 0.95f, new Color(0.22f, 0.21f, 0.23f), Quaternion.LookRotation(-sdir, Vector3.up), true);
            if (plate != null)
            {
                var candle = Place(a.StandCandle, root, sp + Vector3.up * 0.7f, 0.42f, new Color(0.86f, 0.8f, 0.66f), Quaternion.identity);
                Vector3 tip = sp + Vector3.up * 1.18f;
                var flame = Fx.Billboard(root, "standFlame", Fx.Radial, new Color(1f, 0.6f, 0.2f), 0.26f, 2.4f);
                flame.position = tip;
                Fx.Billboard(root, "standCore", Fx.Radial, Color.white, 0.09f, 2.6f).position = tip;
                var l = Fx.PointLight(root, new Color(1f, 0.6f, 0.25f), 1.1f, 3.5f);
                l.transform.position = tip;
                var fl = root.gameObject.AddComponent<Flicker>(); fl.Light = l; fl.Sprite = flame;
            }
        }

        // --- authored layout ---------------------------------------------------------------------------------------------------

        const float TreeRing = 4.4f;

        /// <summary>
        /// Editor time: lays the trees and the stand out under this object as saved scene objects (+Z is "ahead of the player"), so they can be
        /// seen and moved in the Scene view. Materials are saved assets passed in by the editor script.
        /// </summary>
        public void BuildPreview(Material treeMat, Material plateMat, Material candleMat)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            var a = RuneMageAssets.Instance;
            if (a == null) return;
            Vector3 fwd = Vector3.forward;
            float[] angles = { -80f, -35f, 25f, 75f };
            float[] heights = { 2.6f, 3.2f, 2.3f, 2.9f };
            for (int i = 0; i < angles.Length; i++)
            {
                var src = a.EnvTrees != null && a.EnvTrees.Length > 0 ? a.EnvTrees[i % a.EnvTrees.Length] : null;
                if (src == null) continue;
                Vector3 pos = Quaternion.AngleAxis(angles[i], Vector3.up) * fwd * (TreeRing + 0.4f * (i % 2));
                var t = Place(src, transform, transform.TransformPoint(pos), heights[i], Color.white, Quaternion.Euler(0, 40f * i + 15f, 0), false, treeMat);
                if (t != null) t.name = "Tree_" + (i + 1);
            }
            Vector3 sdir = Quaternion.AngleAxis(-28f, Vector3.up) * fwd;
            Vector3 sp = sdir * 2.2f;
            var plate = Place(a.StandPlate, transform, transform.TransformPoint(sp), 0.95f, Color.white, Quaternion.LookRotation(-sdir, Vector3.up), true, plateMat);
            if (plate != null)
            {
                plate.name = "Stand_Plate";
                var candle = Place(a.StandCandle, transform, transform.TransformPoint(sp + Vector3.up * 0.7f), 0.42f, Color.white, Quaternion.identity, false, candleMat);
                if (candle != null) candle.name = "Stand_Candle";
                var tip = new GameObject("CandleTip").transform;
                tip.SetParent(transform, false);
                tip.localPosition = sp + Vector3.up * 1.18f;
            }
        }

        /// <summary>Runtime: turn the authored layout so "ahead" is where the player is looking, stand it on the floor, and pull anything outside the room back inside.</summary>
        void Adapt()
        {
            var head = game.Hands.Head;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
            float floor = game.Spatial.FloorY;
            transform.SetPositionAndRotation(new Vector3(head.position.x, floor, head.position.z), Quaternion.LookRotation(fwd, Vector3.up));
            foreach (Transform c in transform)
            {
                if (!c.name.StartsWith("Tree_") && c.name != "Stand_Plate" && c.name != "Stand_Candle") continue;
                Vector3 p = c.position;
                if (game.Spatial.IsInsideRoom(new Vector3(p.x, floor + 0.5f, p.z))) continue;
                Vector3 flat = new Vector3(p.x - head.position.x, 0, p.z - head.position.z);
                float keep = c.name == "Stand_Plate" || c.name == "Stand_Candle" ? 1.6f : 2.9f;
                if (flat.magnitude > keep) c.position = new Vector3(head.position.x, p.y, head.position.z) + flat.normalized * keep;
            }
            var tipT = transform.Find("CandleTip");
            if (tipT == null) return;
            Vector3 tip = tipT.position;
            var flame = Fx.Billboard(transform, "standFlame", Fx.Radial, new Color(1f, 0.6f, 0.2f), 0.26f, 2.4f);
            flame.position = tip;
            Fx.Billboard(transform, "standCore", Fx.Radial, Color.white, 0.09f, 2.6f).position = tip;
            var l = Fx.PointLight(transform, new Color(1f, 0.6f, 0.25f), 1.1f, 3.5f);
            l.transform.position = tip;
            var fl = gameObject.AddComponent<Flicker>(); fl.Light = l; fl.Sprite = flame;
        }

        /// <summary>Instantiate a model scaled to a target size (largest bounds axis stands upright), dark unlit-style tint.</summary>
        Transform Place(GameObject src, Transform parent, Vector3 pos, float targetHeight, Color tint, Quaternion yaw, bool useWidth = false, Material saved = null)
        {
            if (src == null) return null;
            var go = Instantiate(src, parent);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            Material mat = saved;
            if (mat == null)
            {
                mat = Fx.Make(Fx.White, tint, false, 1f, 1, 1, 0, tint);
                mat.SetFloat("_ZWrite", 1f); mat.SetFloat("_Cull", 2f); mat.renderQueue = 2000;
            }
            var rends = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < m.Length; i++) m[i] = mat;
                r.sharedMaterials = m;
            }
            // Blender Z-up props: if the model is taller along Z than Y, stand it up
            Bounds b = Combined(rends);
            if (b.size.z > b.size.y * 1.4f && !useWidth) { go.transform.rotation = Quaternion.Euler(-90, 0, 0); b = Combined(rends); }
            float size = useWidth ? Mathf.Max(b.size.x, b.size.z) : b.size.y;
            float s = targetHeight / Mathf.Max(size, 0.001f);
            go.transform.localScale *= s;
            b = Combined(rends);
            go.transform.rotation = yaw * go.transform.rotation;
            go.transform.position = pos + Vector3.up * (pos.y - b.min.y - pos.y) + new Vector3(0, 0, 0);
            b = Combined(rends);
            go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
            
            return go.transform;
        }

        /// <summary>World bounds from the meshes themselves (Renderer.bounds lags behind freshly moved objects).</summary>
        static Bounds Combined(Renderer[] rends)
        {
            bool any = false; Bounds b = new Bounds();
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds; var m = r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            return any ? b : rends[0].bounds;
        }
    }

    public sealed class Flicker : MonoBehaviour
    {
        public Light Light; public Transform Sprite;
        float baseI;
        void Start() { baseI = Light != null ? Light.intensity : 1f; }
        void Update()
        {
            float n = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 7f, 3f);
            if (Light != null) Light.intensity = baseI * n;
            if (Sprite != null) Sprite.localScale = Vector3.one * 0.26f * n;
        }
    }
}
