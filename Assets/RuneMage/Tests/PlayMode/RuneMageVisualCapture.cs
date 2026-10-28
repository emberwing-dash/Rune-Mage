using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using RuneMage.Recognition;

namespace RuneMage.Tests
{
    /// <summary>Renders key moments to PNG so the art integration (flame flipbooks, ink, HUD) can be eyeballed without a headset.</summary>
    public class RuneMageVisualCapture
    {
        Game g;
        Camera cam;
        string outDir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.Reset();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null; yield return null;
            g = Game.I;
            g.Story.StopAllCoroutines();
            g.Hands.Scripted = true;
            cam = g.Hands.Head.GetComponent<Camera>();
            g.CenterEye.root.position = new Vector3(0, 1.55f, 0);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            cam.fieldOfView = 75f;
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots"));
            Directory.CreateDirectory(outDir);
            yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(rt); Object.Destroy(tex);
        }

        Vector3 Fwd() { var f = Vector3.ProjectOnPlane(g.Hands.Head.forward, Vector3.up); return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized; }
        Vector3 Right() => Vector3.Cross(Vector3.up, Fwd());
        Vector3 Center() => g.Hands.Head.position + Fwd() * 0.62f + Vector3.down * 0.12f;
        Vector3 At(float u, float v) => Center() + Right() * u + Vector3.up * v;

        IEnumerator Stroke(List<Vector3> pts)
        {
            g.Hands.ScriptedTip = pts[0]; g.Hands.ScriptedPinch = true;
            yield return null; yield return null;
            foreach (var p in pts) { g.Hands.ScriptedTip = p; yield return null; }
            g.Hands.ScriptedPinch = false;
            yield return null; yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureKeyMoments()
        {
            SaveSystem.Learn("fireball");
            var head = g.Hands.Head.position;
            // enemies
            g.Combat.Spawn(EnemyKind.Zombie, new Vector3(-1.4f, 0, 2.8f));
            g.Combat.Spawn(EnemyKind.ZombieMage, new Vector3(0.2f, 0, 3.4f));
            g.Combat.Spawn(EnemyKind.Chandelure, new Vector3(1.7f, 0, 3.0f));
            yield return new WaitForSeconds(5f);
            g.Hud.ShowSubtitle("Mirel", "Fire only feeds them! Quick: say, learn water magic.", 0f);
            g.Hud.SetObjective("The Cinder Court: Learn water magic");
            g.Hud.SetHints("learn water magic", "open my grimoire", "what can I say");
            yield return Shot("1_enemies");
            g.CenterEye.root.rotation = Quaternion.Euler(0, 55, 0); yield return null; yield return Shot("1b_env_right");
            g.CenterEye.root.rotation = Quaternion.Euler(0, -45, 0); yield return null; yield return Shot("1c_env_left");
            g.CenterEye.root.rotation = Quaternion.identity; yield return null;

            // seal + rune
            g.Combat.ClearAll();
            yield return null;
            g.Hud.SetHints();
            var ring = new List<Vector3>();
            for (int i = 0; i <= 70; i++) { float a = Mathf.PI / 2f - i / 70f * Mathf.PI * 2f * 1.02f; ring.Add(At(Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f)); }
            yield return Stroke(ring);
            var tri = new List<Vector3>();
            foreach (var p in RuneShapes.Densify(RuneShapes.Get("fire")[0], 10)) tri.Add(At((p.X - 0.5f) * 0.13f, (p.Y - 0.5f) * 0.13f));
            g.Guide.ShowRune("fire");
            yield return Shot("2_guide_rune");
            yield return Stroke(tri);
            yield return new WaitForSeconds(1.3f);
            yield return Shot("3_seal_ready");

            // cast at a zombie so the flame projectile is visible
            var z = g.Combat.Spawn(EnemyKind.Zombie, new Vector3(0.3f, 0, 3.2f));
            yield return new WaitForSeconds(2f);
            g.Hands.TriggerPush();
            yield return new WaitForSeconds(0.35f);
            yield return Shot("4_fireball");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("5_impact");

            // grimoire
            g.Combat.ClearAll();
            SaveSystem.Discover("aqualance"); SaveSystem.Discover("aegis");
            g.Grimoire.Open();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("6_grimoire_intro");
            g.Grimoire.Turn(1);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("7_grimoire_fire");
            g.Grimoire.Close();

            // dialogue choices
            g.Voice.SetChoices(new List<Voice.VoiceChoice>
            {
                new Voice.VoiceChoice { Label = "Who are you, and what is the Ember Court?", Keywords = new[] { "who are you" } },
                new Voice.VoiceChoice { Label = "Yes, teach me.", Keywords = new[] { "teach me" } },
                new Voice.VoiceChoice { Label = "I am not sure I want this.", Keywords = new[] { "not sure" } },
            });
            g.Hud.ShowSubtitle("Mirel", "Your room is thin tonight. Shall I teach you to defend it?", 0f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("8_dialogue");
            Assert.Pass();
        }
    }
}
