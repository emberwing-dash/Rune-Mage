using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuneMage.Tests
{
    /// <summary>Prints model bounds/material slots and renders per-enemy close-ups to diagnose art integration.</summary>
    public class RuneMageDiagnostics
    {
        [UnityTest]
        public IEnumerator EnemyCloseUps()
        {
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.Reset();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null; yield return null;
            var g = Game.I;
            g.Story.StopAllCoroutines();
            var cam = g.Hands.Head.GetComponent<Camera>();
            g.CenterEye.root.position = new Vector3(0, 1.3f, 0);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            cam.fieldOfView = 60f;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots"));
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            var A = RuneMageAssets.Instance;
            foreach (var c in new[] { A.ZombieWalk, A.ZombieAttack, A.ZombieDie, A.ZombieCrawl, A.MageCast, A.MageDead })
                sb.AppendLine("clip " + (c == null ? "NULL" : c.name + " len=" + c.length + " human=" + c.isHumanMotion + " legacy=" + c.legacy + " loop=" + c.isLooping + " fr=" + c.frameRate));
            var allZ = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/RuneMage/Art/Models/Zombie/Zombie@walk.fbx");
            foreach (var o in allZ) sb.AppendLine("walk.fbx asset: " + o.GetType().Name + " " + o.name);

            foreach (var kind in new[] { EnemyKind.Zombie, EnemyKind.ZombieMage, EnemyKind.Chandelure })
            {
                g.Combat.ClearAll();
                var e = g.Combat.Spawn(kind, new Vector3(0, 0, 2.6f));
                e.Def.Speed = 0f;
                yield return new WaitForSeconds(2.4f);
                sb.AppendLine("== " + kind + " pos=" + e.transform.position + " scale=" + e.transform.localScale);
                var model = e.transform.Find("model");
                if (model != null)
                {
                    sb.AppendLine("  model localScale=" + model.localScale + " localPos=" + model.localPosition + " eul=" + model.localEulerAngles);
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                    {
                        var names = new System.Text.StringBuilder();
                        foreach (var m in r.sharedMaterials) names.Append((m != null ? m.name : "null") + ",");
                        sb.AppendLine("  " + r.GetType().Name + " " + r.name + " bounds center=" + r.bounds.center + " size=" + r.bounds.size + " mats=" + names);
                    }
                    var anim = model.GetComponentInChildren<Animator>();
                    sb.AppendLine("  animator=" + (anim != null) + " avatar=" + (anim != null && anim.avatar != null ? anim.avatar.name + " human=" + anim.avatar.isHuman : "none"));
                }
                else sb.AppendLine("  NO MODEL (placeholder)");
                if (kind == EnemyKind.Zombie) { var an = e.transform.Find("model").GetComponentInChildren<Animator>(); sb.AppendLine("  zombie animator enabled=" + an.enabled + " cullingMode=" + an.cullingMode + " rootScale=" + an.transform.lossyScale); }
                // aim the camera at the enemy
                Vector3 target = e.transform.position + Vector3.up * (e.Def.Height * 0.55f + e.Def.HoverHeight);
                g.CenterEye.position = new Vector3(0, target.y, 0);
                var rt = new RenderTexture(900, 700, 24);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, "close_" + kind + ".png"), tex.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(outDir, "diag.txt"), sb.ToString());
            Assert.Pass();
        }
    }
}
