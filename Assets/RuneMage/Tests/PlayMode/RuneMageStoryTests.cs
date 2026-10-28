using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using RuneMage.Recognition;

namespace RuneMage.Tests
{
    /// <summary>Plays the opening of the story with a scripted player that only uses voice phrases and hand strokes.</summary>
    public class RuneMageStoryTests
    {
        Game g;

        IEnumerator Stroke(List<Vector3> pts)
        {
            g.Hands.ScriptedTip = pts[0]; g.Hands.ScriptedPinch = true;
            yield return null; yield return null;
            foreach (var p in pts) { g.Hands.ScriptedTip = p; yield return null; }
            g.Hands.ScriptedPinch = false;
            yield return null; yield return null;
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator Story_FirstLesson_IsCompletableByVoiceAndGestureOnly()
        {
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.Reset();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null; yield return null;
            g = Game.I;
            SaveSystem.Data.VoiceOverEnabled = false;
            g.Hands.Scripted = true;
            g.Hands.ScriptedTip = g.Hands.Head.position + g.Hands.Head.forward * 0.6f;
            Time.timeScale = 4f;

            float t0 = Time.realtimeSinceStartup;
            int lastStep = -1; float lastBegin = -10f;
            bool learnedSaid = false, drawing = false;
            var log = new System.Text.StringBuilder();
            while (g.Quests.StateOf("first_ink") != QuestState.Complete && Time.realtimeSinceStartup - t0 < 300f)
            {
                int step = g.Quests.StepOf("first_ink");
                if (step != lastStep) { lastStep = step; log.AppendLine("step " + step + " at " + (Time.realtimeSinceStartup - t0).ToString("0") + "s"); }

                if (g.Voice.AwaitingYesNo) g.Voice.Route("yes");
                else if (g.Voice.ActiveChoices != null && g.Voice.ActiveChoices.Count > 0) g.Voice.Route("two");
                else if (g.Quests.StateOf("first_ink") == QuestState.Unavailable && Time.unscaledTime - lastBegin > 1f) { lastBegin = Time.unscaledTime; g.Voice.Route("begin"); }

                if (step == 0 && !learnedSaid && SaveSystem.IsDiscovered("fireball") && g.Quests.StateOf("first_ink") == QuestState.Active)
                {
                    learnedSaid = true;
                    g.Voice.Route("learn fire magic");
                }

                // follow whatever guide is on screen
                if (!drawing && g.Guide.Visible && g.Canvas.HasPlane && !g.Canvas.Busy)
                {
                    if (g.Guide.Current == "ring" && !g.Canvas.RingPresent)
                    {
                        drawing = true;
                        var c = g.Canvas.PlaneOrigin; var r = g.Canvas.PlaneRight;
                        var pts = new List<Vector3>();
                        for (int i = 0; i <= 70; i++) { float a = Mathf.PI / 2f - i / 70f * Mathf.PI * 2f * 1.02f; pts.Add(c + r * Mathf.Cos(a) * 0.17f + Vector3.up * Mathf.Sin(a) * 0.17f); }
                        yield return Stroke(pts);
                        yield return new WaitForSeconds(1.5f);
                        drawing = false;
                    }
                    else if (g.Guide.Current == "fire" && g.Canvas.RingPresent && g.Canvas.Glyphs.Count == 0)
                    {
                        drawing = true;
                        var pts = new List<Vector3>();
                        foreach (var p in RuneShapes.Densify(RuneShapes.Get("fire")[0], 10))
                            pts.Add(g.Canvas.FromPlane(g.Canvas.RingCenter + new Vector2(p.X - 0.5f, p.Y - 0.5f) * (g.Canvas.RingRadius * 1.15f)));
                        yield return Stroke(pts);
                        yield return new WaitForSeconds(2f);
                        drawing = false;
                    }
                }
                if (g.Canvas.Armed != null && step >= 3) g.Voice.Route("ignis");
                yield return null;
            }
            Time.timeScale = 1f;
            Debug.Log("[StoryTest] " + log);
            Assert.AreEqual(QuestState.Complete, g.Quests.StateOf("first_ink"), "tutorial did not finish. progress:\n" + log);
            Assert.IsTrue(SaveSystem.IsLearned("fireball"));
            Assert.IsTrue(SaveSystem.Data.TutorialDone);
            Assert.AreEqual(QuestState.Active, g.Quests.StateOf("cinder_court"));
        }

        [TearDown] public void Reset() { Time.timeScale = 1f; }
    }
}
