using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneMage.UI
{
    /// <summary>Procedural UI building blocks (rounded glass panels, pills, bars). No sprite assets, no layout files.</summary>
    public static class UiKit
    {
        public static readonly Color Glass = new Color(0.04f, 0.05f, 0.11f, 0.84f);
        public static readonly Color GlassLight = new Color(0.12f, 0.14f, 0.26f, 0.88f);
        public static readonly Color Gold = new Color(1f, 0.83f, 0.42f, 1f);
        public static readonly Color Cyan = new Color(0.5f, 0.9f, 1f, 1f);
        public static readonly Color Text = new Color(0.94f, 0.95f, 1f, 1f);
        public static readonly Color Dim = new Color(0.66f, 0.7f, 0.85f, 1f);
        public static readonly Color Good = new Color(0.45f, 1f, 0.6f, 1f);
        public static readonly Color Bad = new Color(1f, 0.4f, 0.35f, 1f);

        static Sprite rounded;
        static Sprite circle;

        public static Sprite Rounded
        {
            get
            {
                if (rounded == null) rounded = Resources.Load<Sprite>("UI/ui_rounded");    // saved asset, so menus built in the editor keep their sprites
                if (rounded == null) rounded = MakeRounded(64, 22, new Vector4(24, 24, 24, 24));
                return rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = Resources.Load<Sprite>("UI/ui_circle");
                if (circle == null) circle = MakeRounded(64, 32, Vector4.zero);
                return circle;
            }
        }

        static Sprite MakeRounded(int n, float radius, Vector4 border)
        {
            var t = RoundedTexture(n, radius);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        public static Texture2D RoundedTexture(int n, float radius)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = Mathf.Min(x + 0.5f, n - x - 0.5f);
                    float py = Mathf.Min(y + 0.5f, n - y - 0.5f);
                    float dx = Mathf.Max(0, radius - px), dy = Mathf.Max(0, radius - py);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(false, false);
            return t;
        }

        // --- get-or-create -----------------------------------------------------------------------------------------------------
        // The HUD, grimoire and inventory panel are saved in the scene so they can be seen and edited before Play. They are built by the
        // same code in both places: between BeginBuild and EndBuild an object that already exists (same parent, same name, in order) is
        // reused untouched, so hand edits survive and the code only re-finds its references. Outside a build everything is created fresh.

        static readonly HashSet<Transform> claimed = new HashSet<Transform>();
        public static bool Reusing { get; private set; }

        public static void BeginBuild() { claimed.Clear(); Reusing = true; }
        public static void EndBuild() { claimed.Clear(); Reusing = false; }

        public static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>A plain child object (canvas roots, groups): reused during a build, otherwise new. <paramref name="created"/> says which.</summary>
        public static Transform Child(Transform parent, string name, out bool created)
        {
            created = false;
            if (Reusing && parent != null)
                foreach (Transform c in parent)
                    if (c.name == name && !claimed.Contains(c)) { claimed.Add(c); return c; }
            created = true;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (Reusing) claimed.Add(go.transform);
            return go.transform;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2? anchor = null) => Rect(parent, name, pos, size, anchor, out _);

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2? anchor, out bool created)
        {
            var t = Child(parent, name, out created);
            var r = t as RectTransform;
            if (r == null) { r = t.gameObject.AddComponent<RectTransform>(); created = true; }
            if (created)
            {
                Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
                r.anchorMin = r.anchorMax = a;
                r.pivot = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = pos;
                r.sizeDelta = size;
            }
            return r;
        }

        public static Image Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color color, Vector2? anchor = null, bool circle = false)
        {
            var r = Rect(parent, name, pos, size, anchor, out bool created);
            var img = r.GetComponent<Image>();
            if (img == null) { img = r.gameObject.AddComponent<Image>(); created = true; }
            if (created)
            {
                img.sprite = circle ? Circle : Rounded;
                img.type = circle ? Image.Type.Simple : Image.Type.Sliced;
                img.color = color;
                img.raycastTarget = false;
            }
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal, Vector2? anchor = null, string name = "text")
        {
            var r = Rect(parent, name, pos, size, anchor, out bool created);
            var t = r.GetComponent<TextMeshProUGUI>();
            if (t == null) { t = r.gameObject.AddComponent<TextMeshProUGUI>(); created = true; }
            if (created)
            {
                t.text = text;
                t.fontSize = fontSize;
                t.color = color;
                t.alignment = align;
                t.fontStyle = style;
                t.enableWordWrapping = true;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.raycastTarget = false;
            }
            return t;
        }

        /// <summary>A straight line drawn as a thin rotated panel. Always new (used for runtime diagrams, never saved), in the parent's pixel units.</summary>
        public static Image Segment(Transform parent, Vector2 a, Vector2 b, float thickness, Color c)
        {
            var go = new GameObject("seg", typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            Vector2 d = b - a;
            r.sizeDelta = new Vector2(d.magnitude + thickness * 0.6f, thickness);
            r.anchoredPosition = (a + b) * 0.5f;
            r.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var img = go.AddComponent<Image>();
            img.sprite = Rounded; img.type = Image.Type.Sliced; img.color = c; img.raycastTarget = false;
            return img;
        }

        public sealed class Bar
        {
            public Image Fill;
            public RectTransform FillRect;
            public float Width;
            public void Set(float f)
            {
                f = Mathf.Clamp01(f);
                FillRect.sizeDelta = new Vector2(Mathf.Max(8f, Width * f), FillRect.sizeDelta.y);
            }
        }

        public static Bar MakeBar(Transform parent, Vector2 pos, float width, float height, Color fill)
        {
            var back = Panel(parent, "bar", pos, new Vector2(width, height), new Color(1, 1, 1, 0.12f));
            var f = Panel(back.transform, "fill", Vector2.zero, new Vector2(width, height - 6), fill);
            f.rectTransform.anchorMin = f.rectTransform.anchorMax = new Vector2(0, 0.5f);
            f.rectTransform.pivot = new Vector2(0, 0.5f);
            f.rectTransform.anchoredPosition = new Vector2(3, 0);
            return new Bar { Fill = f, FillRect = f.rectTransform, Width = width - 6 };
        }

        public static void FadeGroup(CanvasGroup g, float target, float speed)
        {
            g.alpha = Mathf.MoveTowards(g.alpha, target, Time.unscaledDeltaTime * speed);
        }
    }
}
