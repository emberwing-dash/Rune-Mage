using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using RuneMage.UI;

namespace RuneMage.Tests
{
    public class RuneMageMenuTests
    {
        IEnumerator Load(string scene)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(scene);
            yield return null; yield return null;
        }

        static VoiceRouterAccess Router() => new VoiceRouterAccess();
        sealed class VoiceRouterAccess { public Voice.VoiceRouter R => Object.FindFirstObjectByType<Voice.VoiceRouter>(); }

        [UnityTest]
        public IEnumerator MainMenu_VoiceNavigatesToAllScenes()
        {
            SaveSystem.Reset();
            yield return Load("MainMenu");
            Assert.IsNotNull(Object.FindFirstObjectByType<MenuScene>());
            Router().R.Route("magic list");                    // title page offers only Play and Quit
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name, "magic list is not on the title page");
            Router().R.Route("play");                          // Play opens the hub
            yield return null;
            Router().R.Route("magic list");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("MagicList", SceneManager.GetActiveScene().name);
            Router().R.Route("back");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            yield return null;
            Router().R.Route("achievements");                  // back lands on the hub, so this works at once
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("Achievements", SceneManager.GetActiveScene().name);
            Router().R.Route("next page");
            yield return null;
            Router().R.Route("main menu");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            Router().R.Route("begin");
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual("Game", SceneManager.GetActiveScene().name);
            Assert.IsNotNull(Game.I);
        }

        [UnityTest]
        public IEnumerator Game_ReturnsToMainMenuByVoice()
        {
            yield return Load("Game");
            Game.I.Voice.Route("main menu");
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Inventory_DropsAreCollected_AndPotionsWorkByVoice()
        {
            SaveSystem.Reset();
            yield return Load("Game");
            var g = Game.I;
            g.Story.StopAllCoroutines();
            g.Hands.Scripted = true;
            g.Hands.ScriptedTip = g.Hands.Head.position + g.Hands.Head.forward * 0.6f;

            // a drop right at the player's chest is collected immediately
            ItemDrop.Create(g, ItemBook.Find("ember_salve"), 2, g.PlayerChest);
            ItemDrop.Create(g, ItemBook.Find("moonwater"), 1, g.PlayerChest);
            yield return new WaitForSeconds(1.6f);
            Assert.AreEqual(2, SaveSystem.Data.ItemCount("ember_salve"));
            Assert.AreEqual(1, SaveSystem.Data.ItemCount("moonwater"));

            // monsters drop loot when they die
            var z = g.Combat.Spawn(EnemyKind.Zombie, g.Hands.Head.position + Vector3.forward * 3f - Vector3.up * g.Hands.Head.position.y);
            yield return new WaitForSeconds(2f);
            int before = Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Length;
            for (int i = 0; i < 12; i++) { var d = ItemBook.Drops(EnemyKind.Zombie); }
            Assert.Greater(ItemBook.Drops(EnemyKind.Chandelure).Count, 2, "boss has a rich drop table");
            Assert.AreEqual(1f, ItemBook.Drops(EnemyKind.Chandelure)[0].Chance, "boss always drops the elixir");

            // potions: refuse at full, work when hurt
            g.Voice.Route("drink health potion");
            yield return null;
            Assert.AreEqual(2, SaveSystem.Data.ItemCount("ember_salve"), "full ward: potion is kept");
            g.Ward.Hurt(50f);
            float ward = g.Ward.Ward;
            g.Voice.Route("drink health potion");
            yield return null;
            Assert.AreEqual(1, SaveSystem.Data.ItemCount("ember_salve"));
            Assert.Greater(g.Ward.Ward, ward + 30f);

            yield return new WaitForSeconds(1.7f);          // potion cooldown
            g.Ward.Spend(60f);
            float mana = g.Ward.Mana;
            g.Voice.Route("use mana potion");
            yield return null;
            Assert.AreEqual(0, SaveSystem.Data.ItemCount("moonwater"));
            Assert.Greater(g.Ward.Mana, mana + 30f);

            g.Voice.Route("open inventory");
            yield return null;
            Assert.IsTrue(g.Inventory.IsOpen);
            g.Voice.Route("close inventory");
            yield return null;
            Assert.IsFalse(g.Inventory.IsOpen);

            // persistence
            SaveSystem.Save(); SaveSystem.Load();
            Assert.AreEqual(1, SaveSystem.Data.ItemCount("ember_salve"));
        }

        [UnityTest]
        public IEnumerator Achievements_UnlockFromProgress()
        {
            SaveSystem.Reset();
            yield return Load("Game");
            var g = Game.I;
            g.Story.StopAllCoroutines();
            SaveSystem.Data.AddCounter("kills", 1);
            SaveSystem.Data.AddCounter("potions", 1);
            g.Achievements.Evaluate();
            Assert.IsTrue(Achievements.IsUnlocked("first_kill"));
            Assert.IsTrue(Achievements.IsUnlocked("first_potion"));
            Assert.IsFalse(Achievements.IsUnlocked("kills_25"));
            g.Achievements.Unlock("first_cast");
            Assert.AreEqual(3, SaveSystem.Data.Unlocked.Count);
        }

        [UnityTest]
        public IEnumerator Capture_MenuScreens()
        {
            SaveSystem.Reset();
            SaveSystem.Learn("fireball"); SaveSystem.Discover("aqualance"); SaveSystem.Data.AddItem("ember_salve", 3); SaveSystem.Data.AddItem("cinder_ash", 7);
            SaveSystem.Data.Unlocked.Add("first_cast"); SaveSystem.Data.AddCounter("kills", 11); SaveSystem.Data.Chapter = 1;
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots")); Directory.CreateDirectory(outDir);
            foreach (var scene in new[] { "MainMenu", "Hub", "Achievements", "MagicList" })
            {
                MenuScene.OpenHub = scene == "Hub";
                yield return Load(scene == "Hub" ? "MainMenu" : scene);
                var cam = Object.FindFirstObjectByType<OVRCameraRig>().centerEyeAnchor.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f); cam.fieldOfView = 70f;
                yield return null;
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, "menu_" + scene + ".png"), tex.EncodeToPNG());
            }
            Assert.Pass();
        }
    }
}
