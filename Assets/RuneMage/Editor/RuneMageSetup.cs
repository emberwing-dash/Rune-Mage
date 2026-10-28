using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuneMage.EditorTools
{
    /// <summary>
    /// One-shot project setup: Quest hand-tracking / passthrough / scene permissions, art import settings, the RuneMageAssets
    /// reference asset, and the RuneMage scene (Meta XR camera rig + passthrough + hands + MRUK + Game).
    /// Run from the menu (Tools > RuneMage > Setup All) or headless:
    ///   Unity.exe -batchmode -projectPath ... -executeMethod RuneMage.EditorTools.RuneMageSetup.RunAll -quit
    /// It is idempotent: running it twice rebuilds the same result.
    /// </summary>
    public static class RuneMageSetup
    {
        const string Art = "Assets/RuneMage/Art";
        const string ScenePath = "Assets/Scenes/RuneMage.unity";
        const string AssetsPath = "Assets/RuneMage/Resources/RuneMageAssets.asset";

        public static void RunAll()
        {
            ConfigureProject();
            ConfigureArt();
            BuildAssetsAsset();
            AssetDatabase.SaveAssets();
            Debug.Log("[RuneMage] Setup complete. Now run Tools > RuneMage > 4 Build Scene From Meta Building Blocks.");
        }

        // --- project / Quest settings ----------------------------------------------------------------------------

        public static void ConfigureProject()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Required;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            OVRProjectConfig.CommitProjectConfig(cfg);

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel32)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[RuneMage] Project configured: hand tracking (controllers+hands), passthrough required, scene support required.");
        }

        // --- art -----------------------------------------------------------------------------------------------

        public static void ConfigureArt()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Art, "*.fbx", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) continue;
                // Rig, humanoid bone mapping and clip settings come from the original projects' .meta files (copied next to each
                // FBX): re-deriving them here produced a mangled humanoid avatar. Only mobile-friendly mesh settings are applied.
                // (nothing to change: the original import settings are kept as-is)
                continue;

            }

            foreach (var path in Directory.GetFiles(Art, "*.png", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.sRGBTexture = true;
                ti.maxTextureSize = 2048;
                var android = ti.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 2048;
                android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android);
                ti.SaveAndReimport();
            }
        }

        // --- reference asset -------------------------------------------------------------------------------------

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        static AnimationClip Clip(string fbx, string contains = null)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            if (all.Count == 0) { Debug.LogWarning("[RuneMage] No clip in " + fbx); return null; }
            return contains == null ? all[0] : (all.FirstOrDefault(c => c.name.ToLower().Contains(contains)) ?? all[0]);
        }

        public static void BuildAssetsAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetsPath));
            var a = AssetDatabase.LoadAssetAtPath<RuneMageAssets>(AssetsPath);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<RuneMageAssets>();
                AssetDatabase.CreateAsset(a, AssetsPath);
            }
            a.GlowShader = Shader.Find("RuneMage/GlowFlipbook");
            if (a.GlowShader == null) Debug.LogError("[RuneMage] Shader RuneMage/GlowFlipbook not found.");

            string T(string n) => Art + "/Textures/" + n;
            a.FlameOrange = Load<Texture2D>(T("Flame_Orange_5x5.png"));
            a.FlameRed = Load<Texture2D>(T("Flame_Red_5x5.png"));
            a.FlameYellow = Load<Texture2D>(T("Flame_Yellow_5x5.png"));
            a.ZombieBody = Load<Texture2D>(T("ZombieBody_Orange_5x5.png"));
            a.ZombieMouth = Load<Texture2D>(T("ZombieMouth_Orange_5x5.png"));
            a.ZombieEyes = Load<Texture2D>(T("ZombieEyes_Orange_5x5.png"));
            a.ChandelureHead = Load<Texture2D>(T("ChandelureHead_5x5.png"));
            a.ChandelureHands = Load<Texture2D>(T("ChandelureHands_5x5.png"));
            a.ChandelureEyes = Load<Texture2D>(T("ChandelureEyes_5x5.png"));
            a.ChandelureBase = Load<Texture2D>(Art + "/Models/Chandelure/lanturnFace.png");
            a.ChandelureHead2 = Load<Texture2D>(T("ChandelureHead2_5x5.png"));
            a.ChandelureHands2 = Load<Texture2D>(T("ChandelureHands2_5x5.png"));
            a.ChandelureHands3 = Load<Texture2D>(T("ChandelureHands3_5x5.png"));
            a.ChandelureEyes2 = Load<Texture2D>(T("ChandelureEyes2_5x5.png"));
            a.TrailFlow = Load<Texture2D>(T("Trail_Flow.png"));
            a.EnvTrees = new[] { Load<GameObject>(Art + "/Models/Env/t1.fbx"), Load<GameObject>(Art + "/Models/Env/t2.fbx"), Load<GameObject>(Art + "/Models/Env/t3.fbx") };
            a.StandPlate = Load<GameObject>(Art + "/Models/Env/stonePlate.fbx");
            a.StandCandle = Load<GameObject>(Art + "/Models/Env/candle.fbx");
            a.MageEyes = Load<Texture2D>(T("MageGlowingEyes.png"));

            a.ZombieModel = Load<GameObject>(Art + "/Models/Zombie/Zombie.fbx");
            a.MageModel = Load<GameObject>(Art + "/Models/ZombieMage/ZombieMage.fbx");
            a.ChandelureModel = Load<GameObject>(Art + "/Models/Chandelure/Chandelure.fbx");
            a.FlameSpellModel = Load<GameObject>(Art + "/Models/FlameSpell/FlameSpell.fbx");

            a.ZombieWalk = Clip(Art + "/Models/Zombie/Zombie@walk.fbx");
            a.ZombieAttack = Clip(Art + "/Models/Zombie/Zombie@attack.fbx");
            a.ZombieDie = Clip(Art + "/Models/Zombie/Zombie@die.fbx");
            a.ZombieCrawl = Clip(Art + "/Models/Zombie/Zombie@crawl.fbx");
            a.MageCast = Clip(Art + "/Models/ZombieMage/ZombieMage@spellAttack.fbx");
            a.MageDead = Clip(Art + "/Models/ZombieMage/ZombieMage@deadLie.fbx");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();

            // make sure the shader survives stripping even if nothing in a scene references it directly
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == a.GlowShader) has = true;
            if (!has && a.GlowShader != null)
            {
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = a.GlowShader;
                so.ApplyModifiedProperties();
            }

            int missing = 0;
            foreach (var f in typeof(RuneMageAssets).GetFields())
                if (f.FieldType != typeof(RuneMageAssets) && !f.FieldType.IsArray && f.GetValue(a) == null) { Debug.LogWarning("[RuneMage] Missing reference: " + f.Name); missing++; }
            Debug.Log("[RuneMage] Assets reference built. Missing: " + missing);
        }

        // --- scene --------------------------------------------------------------------------------------------

        static void SetEnum(Object target, string prop, string enumName)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[RuneMage] No property " + prop + " on " + target.GetType().Name); return; }
            int idx = System.Array.IndexOf(p.enumNames, enumName);
            if (idx < 0) { Debug.LogWarning("[RuneMage] Enum " + enumName + " not in " + prop + ": " + string.Join(",", p.enumNames)); return; }
            p.enumValueIndex = idx;
            so.ApplyModifiedProperties();
        }

        static GameObject Prefab(string nameContains, string folder = "Packages")
        {
            var guids = AssetDatabase.FindAssets(nameContains + " t:Prefab", new[] { folder });
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == nameContains) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        // --- headless build -------------------------------------------------------------------------------------

        public static void BuildApk()
        {
            string outPath = "Builds/RuneMage.apk";
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[RuneMage] Build result: " + report.summary.result + " size=" + report.summary.totalSize);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
