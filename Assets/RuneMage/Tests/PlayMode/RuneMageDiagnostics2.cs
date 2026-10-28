using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuneMage.Tests
{
    public class RuneMageDiagnostics2
    {
        [UnityTest]
        public IEnumerator ZombiePoses()
        {
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.Reset();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null; yield return null;
            var g = Game.I;
            g.Story.StopAllCoroutines();
            var cam = g.Hands.Head.GetComponent<Camera>();
            g.CenterEye.root.position = new Vector3(0, 1.0f, 0);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            cam.fieldOfView = 60f;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots"));
            var A = RuneMageAssets.Instance;
            var go = Object.Instantiate(System.Environment.GetEnvironmentVariable("RM_MODEL") == "mage" ? A.MageModel : A.ZombieModel, new Vector3(0, 0, 2.5f), Quaternion.identity);
            var solid = Fx.Make(Fx.White, new Color(1f, 0.55f, 0.2f), false, 1f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { var m = new Material[r.sharedMaterials.Length]; for (int i = 0; i < m.Length; i++) m[i] = solid; r.sharedMaterials = m; }
            var an = go.GetComponentInChildren<Animator>(); an.enabled = false;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("root scale=" + go.transform.localScale + " children=" + go.transform.childCount);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var smr = r as SkinnedMeshRenderer;
                sb.AppendLine(r.name + " type=" + r.GetType().Name + " bounds size=" + r.bounds.size + " center=" + r.bounds.center +
                    (smr != null ? " verts=" + smr.sharedMesh.vertexCount + " meshBounds=" + smr.sharedMesh.bounds.size + " bones=" + smr.bones.Length + " root=" + (smr.rootBone != null ? smr.rootBone.name : "null") + " lossyRootScale=" + (smr.rootBone != null ? smr.rootBone.lossyScale.ToString() : "") : ""));
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var bm = new Mesh(); smr.BakeMesh(bm, false);
                var l2w = smr.transform.localToWorldMatrix; Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
                foreach (var v in bm.vertices) { var w = l2w.MultiplyPoint3x4(v); mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w); }
                sb.AppendLine("  baked " + smr.name + " min=" + mn + " max=" + mx);
            }
            File.WriteAllText(Path.Combine(outDir, "zdiag.txt"), sb.ToString());
            // look at the model from the side, bind pose, solid colour, one renderer at a time + all
            var all = go.GetComponentsInChildren<Renderer>();
            g.CenterEye.root.position = new Vector3(2.5f, 1.0f, 2.5f);
            g.CenterEye.root.rotation = Quaternion.Euler(0, -90, 0);
            yield return Cap(cam, Path.Combine(outDir, "z_side_all.png"));
            int idx = 0;
            foreach (var only in all)
            {
                foreach (var r in all) r.enabled = (r == only);
                g.CenterEye.root.position = new Vector3(0, 1.0f, 0); g.CenterEye.root.rotation = Quaternion.identity;
                yield return Cap(cam, Path.Combine(outDir, "z_only_" + (idx++) + "_" + only.name + ".png"));
            }
            Assert.Pass();
        }

        IEnumerator Cap(Camera cam, string path)
        {
            yield return null;
            var rt = new RenderTexture(700, 700, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(700, 700, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 700, 700), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
    }
}
