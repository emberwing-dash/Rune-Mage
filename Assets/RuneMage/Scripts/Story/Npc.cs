using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RuneMage.Voice;

namespace RuneMage
{
    /// <summary>A floating spirit companion. Mirel is calm and formal; Pip is a mischievous cinder-sprite.</summary>
    public sealed class Npc : MonoBehaviour
    {
        public string DisplayName;
        public Color Tint;
        public Vector3 Offset;                 // preferred position relative to the player's view (right, up, forward)
        public float Size = 0.16f;
        public float Pitch = 1f;
        public bool Follow = true;
        public Dictionary<string, string> Knowledge = new Dictionary<string, string>();   // keywords (|-separated) -> answer
        public string Fallback = "I am not sure.";

        Game game;
        Transform orb, aura, ring;
        Vector3 vel;
        float talkUntil;

        public static Npc Create(Game g, string name, Color tint, Vector3 offset, float size, float pitch)
        {
            var go = new GameObject("NPC_" + name);
            var n = go.AddComponent<Npc>();
            n.game = g; n.DisplayName = name; n.Tint = tint; n.Offset = offset; n.Size = size; n.Pitch = pitch;
            n.Build();
            return n;
        }

        void Build()
        {
            aura = Fx.Billboard(transform, "aura", Fx.Radial, new Color(Tint.r, Tint.g, Tint.b, 0.5f), Size * 5f, 1.2f);
            orb = Fx.Billboard(transform, "orb", Fx.Radial, Color.Lerp(Tint, Color.white, 0.5f), Size * 1.6f, 2.6f);
            ring = Fx.Billboard(transform, "ring", Fx.RingTex, Tint, Size * 3.2f, 2.2f);
            Fx.PointLight(transform, Tint, 1.2f, 2.5f);

            var label = new GameObject("name");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = Vector3.up * (Size * 2.6f);
            var t = label.AddComponent<TextMeshPro>();
            t.text = DisplayName.ToUpperInvariant();
            t.fontSize = 0.5f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(Tint.r * 0.5f + 0.5f, Tint.g * 0.5f + 0.5f, Tint.b * 0.5f + 0.5f, 0.9f);
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.sizeDelta = new Vector2(2f, 0.4f);
            label.AddComponent<FaceCamera>();

            Place(true);
        }

        void Place(bool snap)
        {
            var head = game.Hands.Head;
            if (head == null) return;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 target = head.position + right * Offset.x + Vector3.up * Offset.y + flat * Offset.z;
            if (snap) transform.position = target;
            else transform.position = Vector3.SmoothDamp(transform.position, target, ref vel, 0.9f);
        }

        public void Talk(float seconds) => talkUntil = Time.time + seconds;

        void Update()
        {
            if (game == null || game.Hands.Head == null) return;
            if (Follow)
            {
                // only drift when the player turns a lot; otherwise hold still near the preferred spot
                Vector3 flat = Vector3.ProjectOnPlane(game.Hands.Head.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, flat);
                Vector3 target = game.Hands.Head.position + right * Offset.x + Vector3.up * Offset.y + flat * Offset.z;
                if ((target - transform.position).magnitude > 0.5f) Place(false);
            }
            float bob = Mathf.Sin(Time.time * 1.7f + Size * 10f) * 0.02f;
            transform.position += Vector3.up * bob * Time.deltaTime * 6f;
            bool talking = Time.time < talkUntil;
            float pulse = talking ? 1f + 0.25f * Mathf.Sin(Time.time * 16f) : 1f + 0.06f * Mathf.Sin(Time.time * 2f);
            orb.localScale = Vector3.one * Size * 1.6f * pulse;
            aura.localScale = Vector3.one * Size * 5f * (talking ? 1.15f : 1f);
            if (ring != null) ring.localScale = Vector3.one * Size * 3.2f * (1f + 0.1f * Mathf.Sin(Time.time * 3f));
        }

        /// <summary>Free-form question answering by keyword (the player says "ask Mirel about the rift").</summary>
        public string Answer(string topic)
        {
            string t = TextMatch.Normalize(topic);
            string best = null; float bs = 0.74f;
            foreach (var kv in Knowledge)
                foreach (var key in kv.Key.Split('|'))
                {
                    float s = Mathf.Max(TextMatch.Similarity(t, key), TextMatch.ContainsWord(t, key, 0.84f) ? 0.9f : 0f);
                    if (s > bs) { bs = s; best = kv.Value; }
                }
            return best ?? Fallback;
        }
    }
}
