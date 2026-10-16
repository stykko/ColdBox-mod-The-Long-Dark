using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ColdBox
{
    // ---------------- save file model (plain JSON, one file per game save slot) ----------------
    internal class ItemData
    {
        public string Prefab { get; set; }
        public string Data { get; set; }   // GearItem.SerializeToString()
    }

    internal class BoxData
    {
        public string Id { get; set; }
        public float Px { get; set; }
        public float Py { get; set; }
        public float Pz { get; set; }
        public float Rx { get; set; }
        public float Ry { get; set; }
        public float Rz { get; set; }
        public float Rw { get; set; }
        public float SnowHours { get; set; }
        public float SnowTotalHours { get; set; }
        public float LoadLiters { get; set; }
        public float WaterPending { get; set; }
        public float SavedHours { get; set; }   // game hours when saved (snow keeps melting while you are away)
        public List<ItemData> Items { get; set; } = new List<ItemData>();
    }

    internal class SceneData
    {
        public string Scene { get; set; }
        public List<BoxData> Boxes { get; set; } = new List<BoxData>();
    }

    internal class SaveFile
    {
        public int Version { get; set; } = 1;
        public List<SceneData> Scenes { get; set; } = new List<SceneData>();
    }

    /// <summary>
    /// Persistence: the mod writes its own JSON file in UserData\ColdBox\ every time the game saves, and rebuilds the
    /// coolers (position, snow, water and every stored item) after the scene of the loaded save is ready.
    /// </summary>
    internal static class ColdBoxSave
    {
        static bool restorePending;
        static float readySince = -1f;
        static readonly HashSet<string> restoredScenes = new HashSet<string>();

        // ---------------- paths ----------------
        static string FilePath(string slot)
        {
            string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "ColdBox");
            Directory.CreateDirectory(dir);
            foreach (char c in Path.GetInvalidFileNameChars()) slot = slot.Replace(c, '_');
            return Path.Combine(dir, slot + ".json");
        }

        static SaveFile ReadFile(string path)
        {
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(path)) ?? new SaveFile();
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Could not read the ColdBox save file, starting empty: " + e.Message); }
            return new SaveFile();
        }

        // ---------------- scene changes ----------------
        internal static void MarkSceneChanged()
        {
            restorePending = true;
            readySince = -1f;
            restoredScenes.Clear();
        }

        // ---------------- save ----------------
        internal static void Save()
        {
            if (restorePending || !GameBridge.InGame) return;   // never overwrite while the scene is still being restored
            string slot = SaveGameSystem.GetCurrentSaveName();
            if (string.IsNullOrEmpty(slot)) return;

            string path = FilePath(slot);
            var file = ReadFile(path);

            var byScene = new Dictionary<string, List<BoxData>>();
            foreach (string s in restoredScenes) byScene[s] = new List<BoxData>();   // restored scenes with no cooler left are saved empty
            float now = GameBridge.GetGameHoursNow();

            foreach (var box in ColdBoxManager.Boxes)
            {
                if (box == null) continue;
                string scene = box.GetSceneKey();
                if (!byScene.ContainsKey(scene)) byScene[scene] = new List<BoxData>();
                byScene[scene].Add(Capture(box, now));
            }

            foreach (var kv in byScene)
            {
                file.Scenes.RemoveAll(sd => sd.Scene == kv.Key);
                if (kv.Value.Count > 0) file.Scenes.Add(new SceneData { Scene = kv.Key, Boxes = kv.Value });
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static BoxData Capture(ColdBoxBehaviour box, float nowHours)
        {
            var t = box.transform;
            var data = new BoxData
            {
                Id = box.GetId(),
                Px = t.position.x, Py = t.position.y, Pz = t.position.z,
                Rx = t.rotation.x, Ry = t.rotation.y, Rz = t.rotation.z, Rw = t.rotation.w,
                SnowHours = box.GetSnowHoursLeft(),
                SnowTotalHours = box.GetSnowTotalHours(),
                LoadLiters = box.GetLoadLiters(),
                WaterPending = box.GetWaterPending(),
                SavedHours = nowHours
            };

            var container = box.GetContainer();
            if (container != null)
            {
                var items = new Il2CppSystem.Collections.Generic.List<GearItem>();
                container.GetItems(items);
                for (int i = 0; i < items.Count; i++)
                {
                    var gi = items[i];
                    if (gi == null) continue;
                    data.Items.Add(new ItemData { Prefab = gi.name.Replace("(Clone)", "").Trim(), Data = gi.SerializeToString() });
                }
            }
            return data;
        }

        // ---------------- restore ----------------
        internal static void TryRestore()
        {
            if (!restorePending) return;
            if (!GameBridge.InGame) return;
            if (SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress()) { readySince = -1f; return; }
            if (GameManager.GetPlayerTransform() == null || GameManager.GetTimeOfDayComponent() == null) return;

            if (readySince < 0f) readySince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - readySince < 2f) return;   // let the game finish placing its own objects first

            restorePending = false;
            try { RestoreScene(); }
            catch (Exception e) { Melon<Core>.Logger.Error("Restoring the coolers failed: " + e); }
        }

        static void RestoreScene()
        {
            string scene = GameManager.m_ActiveScene;
            restoredScenes.Add(scene);

            string slot = SaveGameSystem.GetCurrentSaveName();
            if (string.IsNullOrEmpty(slot)) return;
            var file = ReadFile(FilePath(slot));
            var sd = file.Scenes.Find(s => s.Scene == scene);
            if (sd == null) return;

            float now = GameBridge.GetGameHoursNow();
            int restored = 0;
            foreach (var bd in sd.Boxes)
            {
                if (ColdBoxManager.FindById(bd.Id) != null) continue;   // never duplicate

                var pos = new Vector3(bd.Px, bd.Py, bd.Pz);
                var rot = new Quaternion(bd.Rx, bd.Ry, bd.Rz, bd.Rw);
                var box = ColdBoxManager.CreateBox(pos, rot, bd.Id, scene);
                if (box == null) continue;

                box.RestoreState(bd.SnowHours, bd.SnowTotalHours, bd.LoadLiters, bd.WaterPending);
                float away = now - bd.SavedHours;
                if (away > 0f) box.AdvanceMelt(away);

                RestoreItems(box, bd);
                restored++;
            }
            if (restored > 0) Melon<Core>.Logger.Msg("Restored " + restored + " cooler(s) in " + scene + ".");
        }

        static void RestoreItems(ColdBoxBehaviour box, BoxData bd)
        {
            var container = box.GetContainer();
            if (container == null) return;

            foreach (var item in bd.Items)
            {
                if (string.IsNullOrEmpty(item.Prefab) || string.IsNullOrEmpty(item.Data)) continue;
                try
                {
                    var gi = GearItem.InstantiateAndDeserializeGearItem(item.Prefab, item.Data, box.transform, false, true);
                    if (gi == null) { Melon<Core>.Logger.Warning("Could not recreate item " + item.Prefab); continue; }

                    // the game may already register the item in the container while deserializing; do not add it twice
                    var current = new Il2CppSystem.Collections.Generic.List<GearItem>();
                    container.GetItems(current);
                    bool present = false;
                    for (int i = 0; i < current.Count; i++)
                        if (current[i] != null && current[i].Pointer == gi.Pointer) { present = true; break; }
                    if (!present) container.AddGear(gi);
                }
                catch (Exception e)
                {
                    Melon<Core>.Logger.Warning("Failed to restore item " + item.Prefab + ": " + e.Message);
                }
            }
        }
    }

    // ---------------- hooks into the game's own saving ----------------
    [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.SaveGame), new Type[] { typeof(string), typeof(string) })]
    internal static class SaveGamePatch
    {
        static void Postfix()
        {
            try { ColdBoxSave.Save(); }
            catch (Exception e) { Melon<Core>.Logger.Error("Saving the coolers failed: " + e); }
        }
    }

    // Save right before leaving a scene, while its coolers still exist.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadScene), new Type[] { typeof(string), typeof(string) })]
    internal static class LoadScenePatch
    {
        static void Prefix()
        {
            try { ColdBoxSave.Save(); }
            catch (Exception e) { Melon<Core>.Logger.Error("Saving the coolers before the scene change failed: " + e); }
        }
    }
}
