using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuneMage.UI;

namespace RuneMage.EditorTools
{
    /// <summary>
    /// Builds every scene from the project's own SampleScene setup (Meta Building Blocks: Camera Rig + Interaction rig, Passthrough,
    /// Real Hands), so the Meta XR configuration you already made is what the game runs on:
    ///   Game.unity         = SampleScene copy minus the MR-template tutorial clutter + MR Utility Kit block + the RuneMage game root
    ///   MainMenu.unity, Achievements.unity, MagicList.unity = Game.unity copies with a MenuScene instead of the game root
    /// Installing a block can force a domain reload, so progress is kept in SessionState and resumed.
    /// </summary>
    public static class RuneMageBlocks
    {
        const string Dir = "Assets/Scenes/";
        const string SampleScene = Dir + "SampleScene.unity";
        const string GameScene = Dir + "Game.unity";
        const string MrukBlockId = "d4413f4f-542f-451f-92ee-4801f27d676a";
        static readonly string[] JunkRoots = { "MR Interaction Setup", "UI", "[BuildingBlock] Cube", "Cube" };

        static IEnumerator<bool> state;

        public static void BuildAllScenes()
        {
            RuneMageSetup.ConfigureProject();
            SessionState.SetInt("RM_SCN_STAGE", 0);
            SessionState.SetBool("RM_SCN_RUN", true);
            Start();
        }

        static void Start()
        {
            state = SessionState.GetString("RM_SCN_MODE", "") == "voice" ? RunVoice() : Run();
            EditorApplication.update += Pump;
        }

        const string DictationBlockId = "af49bc82-4174-46e0-b1e4-40f1c05b4908";    // Speech to Text (Dictation)
        const string SpeakTextBlockId = "ab791591-5ee2-431a-93e7-1749d1b5ad8f";    // Speak Text

        /// <summary>Installs Meta's Speech to Text and Speak Text building blocks into every scene that lacks them (the Voice SDK needs the voice_input Wit configuration).</summary>
        public static void InstallVoiceBlocks()
        {
            SessionState.SetString("RM_SCN_MODE", "voice");
            SessionState.SetBool("RM_SCN_RUN", true);
            Start();
        }

        static bool SceneHas(string typeName)
        {
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == typeName) return true;
            return false;
        }

        static IEnumerator<bool> RunVoice()
        {
            foreach (var n in new[] { "MainMenu", "Achievements", "MagicList", "Game" })
            {
                string path = Dir + n + ".unity";
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (!SceneHas("AppDictationExperience"))
                {
                    var install = InstallBlock(DictationBlockId, "Speech to Text");
                    while (install.MoveNext()) yield return true;
                }
                if (!SceneHas("TTSSpeaker"))
                {
                    var install = InstallBlock(SpeakTextBlockId, "Speak Text");
                    while (install.MoveNext()) yield return true;
                }
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t != null && t.name == "Stats" && t.parent != null && t.parent.name == "Page_Hub") UnityEngine.Object.DestroyImmediate(t.gameObject);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
                Debug.Log("[RuneMage] voice blocks ready in " + n + ": dictation=" + SceneHas("AppDictationExperience") + " speak=" + SceneHas("TTSSpeaker"));
                yield return true;
            }
            SessionState.SetString("RM_SCN_MODE", "");
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (!SessionState.GetBool("RM_SCN_RUN", false)) return;
            EditorApplication.delayCall += () => { if (state == null) Start(); };
        }

        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { if (state != null && state.MoveNext()) return; }
            catch (Exception e) { Debug.LogError("[RuneMage] Scene build failed: " + e); }
            EditorApplication.update -= Pump;
            state = null;
            SessionState.SetBool("RM_SCN_RUN", false);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static IEnumerator<bool> Run()
        {
            int stage = SessionState.GetInt("RM_SCN_STAGE", 0);

            if (stage == 0)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) != null) AssetDatabase.DeleteAsset(GameScene);
                if (!AssetDatabase.CopyAsset(SampleScene, GameScene)) { Debug.LogError("[RuneMage] Could not duplicate SampleScene"); yield break; }
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
                Clean();
                AddSkeletons();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), GameScene);
                SessionState.SetInt("RM_SCN_STAGE", 1);
                Debug.Log("[RuneMage] Game.unity duplicated from SampleScene");
                yield return true;
                stage = 1;
            }

            if (stage == 1)
            {
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
                if (UnityEngine.Object.FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>() == null)
                {
                    SessionState.SetInt("RM_SCN_STAGE", 2);          // a reload may happen inside the install: do not repeat it
                    var install = InstallBlock(MrukBlockId, "MR Utility Kit");
                    while (install.MoveNext()) yield return true;
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), GameScene);
                }
                SessionState.SetInt("RM_SCN_STAGE", 2);
                yield return true;
                stage = 2;
            }

            if (stage == 2)
            {
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
                if (UnityEngine.Object.FindFirstObjectByType<Game>() == null) AddGameRoot();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), GameScene);
                BuildMenuScene("MainMenu", MenuKind.Main);
                BuildMenuScene("Achievements", MenuKind.Achievements);
                BuildMenuScene("MagicList", MenuKind.MagicList);

                var list = new List<EditorBuildSettingsScene>();
                foreach (var n in new[] { "MainMenu", "Game", "Achievements", "MagicList" }) list.Add(new EditorBuildSettingsScene(Dir + n + ".unity", true));
                list.Add(new EditorBuildSettingsScene(SampleScene, false));
                EditorBuildSettings.scenes = list.ToArray();
                Debug.Log("[RuneMage] All scenes built: MainMenu, Game, Achievements, MagicList");
            }
        }

        // --- scene surgery ----------------------------------------------------------------------------------------------

        static void Clean()
        {
            foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
                if (JunkRoots.Contains(r.name)) { Debug.Log("[RuneMage] removing template object: " + r.name); UnityEngine.Object.DestroyImmediate(r); }
            // only the rig's centre eye is the main camera
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.tag = cam.name == "CenterEyeAnchor" ? "MainCamera" : "Untagged";
            var rig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                var c = rig.centerEyeAnchor.GetComponent<Camera>();
                c.nearClipPlane = 0.05f; c.farClipPlane = 60f;
                var mgr = rig.GetComponent<OVRManager>();
                if (mgr != null) { mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel; mgr.isInsightPassthroughEnabled = true; }
            }
        }

        /// <summary>The interaction rig exposes OVRHand data sources only; add a skeleton next to each so fingertips can be read.</summary>
        static void AddSkeletons()
        {
            foreach (var hand in UnityEngine.Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand.GetComponent<OVRSkeleton>() != null) continue;
                var p = new SerializedObject(hand).FindProperty("HandType");
                string side = p.enumNames[p.enumValueIndex];
                var skel = hand.gameObject.AddComponent<OVRSkeleton>();
                var so = new SerializedObject(skel);
                var sp = so.FindProperty("_skeletonType");
                int idx = Array.IndexOf(sp.enumNames, side);
                if (idx >= 0) { sp.enumValueIndex = idx; so.ApplyModifiedProperties(); }
                Debug.Log("[RuneMage] OVRSkeleton added to " + hand.name + " as " + side);
            }
        }

        static void WireHands(out Transform eye, out OVRHand lh, out OVRHand rh, out OVRSkeleton ls, out OVRSkeleton rs)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            eye = rig != null ? rig.centerEyeAnchor : null;
            lh = rh = null; ls = rs = null;
            foreach (var hand in UnityEngine.Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var p = new SerializedObject(hand).FindProperty("HandType");
                string n = p != null ? p.enumNames[p.enumValueIndex] : "";
                if (n == "HandLeft") { lh = hand; ls = hand.GetComponent<OVRSkeleton>(); }
                else if (n == "HandRight") { rh = hand; rs = hand.GetComponent<OVRSkeleton>(); }
            }
        }

        static void AddGameRoot()
        {
            WireHands(out var eye, out var lh, out var rh, out var ls, out var rs);
            var go = new GameObject("RuneMage");
            var game = go.AddComponent<Game>();
            game.CenterEye = eye; game.LeftOvrHand = lh; game.RightOvrHand = rh; game.LeftSkeleton = ls; game.RightSkeleton = rs;
            Debug.Log("[RuneMage] Game root wired: eye=" + (eye != null) + " L=" + (lh != null) + " R=" + (rh != null) + " skelL=" + (ls != null) + " skelR=" + (rs != null));
        }

        static void BuildMenuScene(string name, MenuKind kind)
        {
            string path = Dir + name + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(GameScene, path);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var g in UnityEngine.Object.FindObjectsByType<Game>(FindObjectsInactive.Include, FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(g.gameObject);
            var mruk = UnityEngine.Object.FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>();
            if (mruk != null) UnityEngine.Object.DestroyImmediate(mruk.gameObject);         // menus do not need room data
            WireHands(out var eye, out var lh, out var rh, out var ls, out var rs);
            var go = new GameObject("Menu_" + kind);
            var m = go.AddComponent<MenuScene>();
            m.Kind = kind; m.CenterEye = eye; m.LeftOvrHand = lh; m.RightOvrHand = rh; m.LeftSkeleton = ls; m.RightSkeleton = rs;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
        }

        // --- Building Block installation (same call the Building Blocks window makes) ------------------------------------

        static IEnumerator<bool> InstallBlock(string blockId, string label)
        {
            Type baseType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Meta.XR.BuildingBlocks.Editor.BlockData")).FirstOrDefault(t => t != null);
            if (baseType == null) { Debug.LogError("[RuneMage] Meta Building Blocks editor assembly not found"); yield break; }
            ScriptableObject data = null;
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Packages" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("BuildingBlocks") || !path.EndsWith(".asset")) continue;
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null || !baseType.IsAssignableFrom(so.GetType())) continue;
                var id = so.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(so) as string;
                if (id == blockId) { data = so; break; }
            }
            if (data == null) { Debug.LogWarning("[RuneMage] Block not found: " + label); yield break; }
            var add = data.GetType().GetMethod("InstallWithDependencies", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(GameObject) }, null);
            Debug.Log("[RuneMage] Installing block: " + label);
            Task task = null;
            try { task = add.Invoke(data, new object[] { null }) as Task; }
            catch (Exception e) { Debug.LogWarning("[RuneMage] " + label + " install threw: " + (e.InnerException ?? e).Message); }
            double t0 = EditorApplication.timeSinceStartup;
            while (task != null && !task.IsCompleted && EditorApplication.timeSinceStartup - t0 < 60) yield return true;
            Debug.Log("[RuneMage] Block ready: " + label);
        }
    }
}
