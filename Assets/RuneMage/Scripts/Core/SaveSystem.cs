using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuneMage
{
    [Serializable]
    public sealed class QuestProgress
    {
        public string Id;
        public int State;        // QuestState as int: 0 unavailable, 1 offered, 2 active, 3 complete
        public int Step;
        public int Counter;
    }

    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;
        public bool TutorialDone;
        public int Chapter;                                  // 0 = prologue
        public List<string> Discovered = new List<string>(); // spells the player has read about
        public List<string> Learned = new List<string>();
        public List<string> Mastered = new List<string>();
        public List<string> CastCountKeys = new List<string>();
        public List<int> CastCountValues = new List<int>();
        public List<QuestProgress> Quests = new List<QuestProgress>();
        public List<string> Shards = new List<string>();     // collected artefact ids
        public bool GuidesEnabled = true;
        public bool VoiceOverEnabled = true;
        public float BestWard = 0;
        public List<string> InvIds = new List<string>();
        public List<int> InvCounts = new List<int>();
        public List<string> Unlocked = new List<string>();          // achievement ids
        public List<string> CounterKeys = new List<string>();
        public List<int> CounterValues = new List<int>();

        public int ItemCount(string id) { int i = InvIds.IndexOf(id); return i >= 0 ? InvCounts[i] : 0; }
        public void AddItem(string id, int n)
        {
            int i = InvIds.IndexOf(id);
            if (i < 0) { InvIds.Add(id); InvCounts.Add(Mathf.Min(99, n)); } else InvCounts[i] = Mathf.Min(99, InvCounts[i] + n);
        }
        public bool TakeItem(string id)
        {
            int i = InvIds.IndexOf(id);
            if (i < 0 || InvCounts[i] <= 0) return false;
            if (--InvCounts[i] == 0) { InvIds.RemoveAt(i); InvCounts.RemoveAt(i); }
            return true;
        }
        public int Counter(string key) { int i = CounterKeys.IndexOf(key); return i >= 0 ? CounterValues[i] : 0; }
        public int AddCounter(string key, int n = 1)
        {
            int i = CounterKeys.IndexOf(key);
            if (i < 0) { CounterKeys.Add(key); CounterValues.Add(n); return n; }
            return CounterValues[i] += n;
        }

        public int CastCount(string id)
        {
            int i = CastCountKeys.IndexOf(id);
            return i >= 0 ? CastCountValues[i] : 0;
        }

        public int AddCast(string id)
        {
            int i = CastCountKeys.IndexOf(id);
            if (i < 0) { CastCountKeys.Add(id); CastCountValues.Add(1); return 1; }
            return ++CastCountValues[i];
        }
    }

    /// <summary>JSON save in persistentDataPath. Writes go through a temp file so a crash cannot corrupt progress.</summary>
    public static class SaveSystem
    {
        static string PathFile => System.IO.Path.Combine(Application.persistentDataPath, "runemage_save.json");
        public static SaveData Data { get; private set; } = new SaveData();
        public static bool LoadedExisting { get; private set; }

        public static void Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFile)) ?? new SaveData();
                    LoadedExisting = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RuneMage] Save load failed, starting fresh: " + e.Message);
                Data = new SaveData();
            }
        }

        public static void Save()
        {
            try
            {
                string tmp = PathFile + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(PathFile)) File.Delete(PathFile);
                File.Move(tmp, PathFile);
            }
            catch (Exception e) { Debug.LogWarning("[RuneMage] Save failed: " + e.Message); }
        }

        public static void Reset()
        {
            Data = new SaveData();
            LoadedExisting = false;
            try { if (File.Exists(PathFile)) File.Delete(PathFile); } catch { }
        }

        public static bool IsLearned(string spellId) => Data.Learned.Contains(spellId);
        public static bool IsDiscovered(string spellId) => Data.Discovered.Contains(spellId) || Data.Learned.Contains(spellId);

        public static void Discover(string spellId)
        {
            if (!Data.Discovered.Contains(spellId)) { Data.Discovered.Add(spellId); Save(); }
        }

        public static void Learn(string spellId)
        {
            if (!Data.Learned.Contains(spellId)) { Data.Learned.Add(spellId); Save(); }
        }
    }
}
