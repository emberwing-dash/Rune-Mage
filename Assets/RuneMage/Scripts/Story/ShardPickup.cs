using System;
using UnityEngine;

namespace RuneMage
{
    /// <summary>A glowing rune shard hidden in the room. Touching it with a fingertip awakens it.</summary>
    public sealed class ShardPickup : MonoBehaviour
    {
        public string SpellId;
        public MagicType Element;
        public event Action<ShardPickup> Collected;

        Game game;
        Transform orb, ring;
        float t, hum;
        float touch;

        public static ShardPickup Create(Game g, Vector3 pos, string spellId, string elementName)
        {
            var go = new GameObject("Shard_" + elementName);
            go.transform.position = pos;
            var s = go.AddComponent<ShardPickup>();
            s.game = g;
            s.SpellId = spellId;
            s.Element = SpellBook.Find(spellId).Primary;
            Color c = Magic.Color(s.Element);
            s.orb = Fx.Billboard(go.transform, "glow", Fx.Radial, c, 0.22f, 2f);
            s.ring = Fx.Billboard(go.transform, "ring", Fx.RingTex, Color.white, 0.32f, 1.6f);
            Fx.PointLight(go.transform, c, 0.8f, 1.5f);
            return s;
        }

        void Update()
        {
            t += Time.deltaTime;
            hum -= Time.deltaTime;
            float s = 1f + 0.18f * Mathf.Sin(t * 3f);
            orb.localScale = Vector3.one * 0.22f * s;
            ring.localScale = Vector3.one * (0.3f + 0.12f * Mathf.Repeat(t, 1.6f));
            transform.position += Vector3.up * Mathf.Sin(t * 1.3f) * 0.0004f;

            var head = game.Hands.Head;
            if (head == null) return;
            float d = Vector3.Distance(head.position, transform.position);
            if (hum <= 0f && d < 2.2f)
            {
                hum = Mathf.Lerp(0.5f, 2.2f, d / 2.2f);          // beeps faster as the player gets close
                AudioSynth.PlayAt("listen", transform.position, 0.9f, 0.6f + (1f - d / 2.2f));
            }

            // fingertip touch
            foreach (var h in new[] { game.Hands.Right, game.Hands.Left })
            {
                if (!h.Tracked) continue;
                if (Vector3.Distance(h.IndexTip, transform.position) < 0.13f) touch += Time.deltaTime;
            }
            if (touch > 0.35f)
            {
                Fx.Burst(transform.position, Magic.Color(Element), 40, 2.5f, 0.05f, 0.9f);
                Collected?.Invoke(this);
                Destroy(gameObject);
            }
            else touch = Mathf.Max(0f, touch - Time.deltaTime * 0.5f);
        }
    }
}
