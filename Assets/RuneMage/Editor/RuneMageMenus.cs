using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuneMage.UI;

namespace RuneMage.EditorTools
{
    /// <summary>
    /// Writes the menu UI into the menu scenes as ordinary saved objects, so they show up in the Scene view and can be edited by hand.
    /// Also drops the template's VR sky/floor ("Environment") from every game scene: this is a passthrough game, and that mesh hides the real room.
    /// Run headless: -executeMethod RuneMage.EditorTools.RuneMageMenus.BuildAndSave
    /// </summary>
    public static class RuneMageMenus
    {
        const string SpriteDir = "Assets/RuneMage/Resources/UI";

        public static void BuildAndSave()
        {
            EnsureSprites();
            foreach (var scene in new[] { "MainMenu", "Achievements", "MagicList" })
            {
                var sc = EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity", OpenSceneMode.Single);
                RemoveVrEnvironment();
                var menu = Object.FindFirstObjectByType<MenuScene>();
                if (menu == null) { Debug.LogError("No MenuScene in " + scene); continue; }
                menu.BuildAll();
                EditorSceneManager.MarkSceneDirty(sc);
                EditorSceneManager.SaveScene(sc);
                Debug.Log("Menu UI saved into " + scene);
            }
            var game = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            RemoveVrEnvironment();
            BuildProps();
            BuildGameUi();
            EditorSceneManager.MarkSceneDirty(game);
            EditorSceneManager.SaveScene(game);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The dead trees and the candle stand, saved under "Props" so they are visible in the Scene view before Play.</summary>
        static void BuildProps()
        {
            var env = Object.FindFirstObjectByType<RuneMage.Environment>();
            if (env == null) env = new GameObject("Props").AddComponent<RuneMage.Environment>();
            env.gameObject.name = "Props";
            env.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            env.BuildPreview(PropMaterial("Prop_Tree", new Color(0.2f, 0.15f, 0.12f)), PropMaterial("Prop_StonePlate", new Color(0.22f, 0.21f, 0.23f)), PropMaterial("Prop_Candle", new Color(0.86f, 0.8f, 0.66f)));
        }

        /// <summary>The HUD, the grimoire and the inventory panel, saved as components with their UI on the Game object.</summary>
        static void BuildGameUi()
        {
            var game = Object.FindFirstObjectByType<Game>();
            if (game == null) { Debug.LogError("No Game in the scene"); return; }
            var go = game.gameObject;
            UiKit.Ensure<HudUi>(go).BuildAll();
            UiKit.Ensure<Grimoire>(go).BuildBook(CoverMaterial());
            UiKit.Ensure<Inventory>(go).BuildPanel();
        }

        static Material CoverMaterial()
        {
            const string dir = "Assets/RuneMage/Art/Materials";
            Directory.CreateDirectory(dir);
            const string path = dir + "/Grimoire_Cover.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = Fx.Make(Fx.White, new Color(0.35f, 0.2f, 0.12f, 1f), false, 1f, 1, 1, 0, new Color(0.2f, 0.1f, 0.06f, 1f));
            m.SetFloat("_ZWrite", 1f); m.renderQueue = 2000;
            m.mainTexture = null;
            m.name = "Grimoire_Cover";
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material PropMaterial(string name, Color tint)
        {
            const string dir = "Assets/RuneMage/Art/Materials";
            Directory.CreateDirectory(dir);
            string path = dir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = Fx.Make(Fx.White, tint, false, 1f, 1, 1, 0, tint);
            m.SetFloat("_ZWrite", 1f); m.SetFloat("_Cull", 2f); m.renderQueue = 2000;
            m.mainTexture = null;                                   // the shader's default white; keeps the asset free of in-memory textures
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void RemoveVrEnvironment()
        {
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == "Environment" && r.GetComponentInChildren<MeshRenderer>() != null && r.GetComponentInChildren<MeshRenderer>().GetComponent("FadeMaterial") != null)
                    Object.DestroyImmediate(r);
        }

        static void EnsureSprites()
        {
            Directory.CreateDirectory(SpriteDir);
            Make("ui_rounded", 22f, new Vector4(24, 24, 24, 24));
            Make("ui_circle", 32f, Vector4.zero);
        }

        static void Make(string name, float radius, Vector4 border)
        {
            string path = SpriteDir + "/" + name + ".png";
            if (!File.Exists(path)) File.WriteAllBytes(path, UiKit.RoundedTexture(64, radius).EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.spriteBorder = border;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
    }
}
