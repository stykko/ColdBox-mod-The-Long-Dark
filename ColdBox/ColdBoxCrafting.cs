using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using Il2CppTLD.Gear;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Adds the workbench recipe: 10 cedar wood + 5 scrap metal + 5 tarps + quality tools (tool is not consumed).
    /// The recipe is a copy of a vanilla workbench blueprint with the requirements replaced. Array element types are
    /// created through reflection so this file does not depend on how the game names its nested types.
    /// </summary>
    internal static class ColdBoxCrafting
    {
        const int WoodCount = 10;
        const int MetalCount = 5;
        const int TarpCount = 5;

        static bool done;
        static int attempts;
        static float nextTry;

        internal static void TryRegister()
        {
            if (done || Time.unscaledTime < nextTry) return;
            nextTry = Time.unscaledTime + 3f;

            var cfg = ColdBoxSettings.Instance;
            if (cfg == null || !cfg.enableCrafting) { done = true; return; }

            if (!GameBridge.InGame) return;   // gear data is only fully loaded once a game is running
            var bm = BlueprintManager.s_Instance;
            if (bm == null || !bm.m_IsInitialized || bm.m_AllBlueprints == null || bm.m_AllBlueprints.Count == 0) return;

            attempts++;
            try
            {
                if (Register(bm)) { done = true; return; }
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Error("Recipe registration failed: " + e);
            }
            if (attempts >= 10)
            {
                done = true;
                Melon<Core>.Logger.Error("The Snow Cooler recipe could not be registered (see messages above).");
            }
        }

        static bool Register(BlueprintManager bm)
        {
            DumpGearNames();   // written first so the file exists even if something below fails
            var kit = ColdBoxItem.GetPrefab();
            if (kit == null) { done = true; return false; }

            var wood = FindGear(new[] { "GEAR_Softwood" }, "cedar");   // cedar firewood ("cedro" in Spanish) is GEAR_Softwood
            var metal = FindGear(new[] { "GEAR_ScrapMetal" }, "scrapmetal");
            var tarp = FindGear(new[] { "GEAR_TarpSheet" }, "tarp");
            var tools = FindGear(new[] { "GEAR_HighQualityTools" }, "qualitytools");
            if (wood == null || metal == null || tarp == null || tools == null)
            {
                Melon<Core>.Logger.Error("Recipe ingredient not found: cedar wood=" + (wood != null) + ", scrap metal=" + (metal != null)
                    + ", tarp=" + (tarp != null) + ", quality tools=" + (tools != null) + ". See UserData\\ColdBox\\gear_names.txt.");
                return false;
            }
            var toolItem = tools.GetComponent<ToolsItem>();
            if (toolItem == null) { Melon<Core>.Logger.Error("'" + tools.name + "' has no ToolsItem component."); return false; }

            BlueprintData template = null;
            foreach (var b in bm.m_AllBlueprints)
            {
                if (b == null || b.m_CraftedResultGear == null) continue;
                if (b.m_RequiredCraftingLocation == CraftingLocation.Workbench && b.m_CraftingResultType == CraftingResult.StandardGear) { template = b; break; }
            }
            if (template == null) { Melon<Core>.Logger.Error("No vanilla workbench blueprint found to copy."); return false; }

            var bp = UnityEngine.Object.Instantiate(template);
            bp.name = "BLUEPRINT_ColdBox";
            UnityEngine.Object.DontDestroyOnLoad(bp);

            var t = typeof(BlueprintData);
            SetArray(bp, t, "m_RequiredGear", new[] { Req(t, wood, WoodCount), Req(t, metal, MetalCount), Req(t, tarp, TarpCount) });
            SetArray(bp, t, "m_RequiredPowder", new object[0]);
            SetArray(bp, t, "m_RequiredLiquid", new object[0]);
            bp.m_RequiredTool = toolItem;
            bp.m_OptionalTools = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ToolsItem>(0);
            bp.m_FilteredOptionalTools = new Il2CppSystem.Collections.Generic.List<ToolsItem>();
            bp.m_CraftedResultGear = kit;
            bp.m_CraftedResultCount = 1;
            bp.m_CraftingResultType = CraftingResult.StandardGear;
            bp.m_RequiredCraftingLocation = CraftingLocation.Workbench;
            bp.m_DurationMinutes = Mathf.RoundToInt(ColdBoxSettings.Instance.craftMinutes);
            bp.m_XPModesToDisable = new Il2CppSystem.Collections.Generic.List<ExperienceModeType>();
            bp.m_AppearsInStoryOnly = false;
            bp.m_AppearsInSurvivalOnly = false;
            bp.m_Locked = false;
            bp.m_IgnoreLockInSurvival = true;
            bp.m_UsesPhoto = false;

            bm.m_AllBlueprints.Add(bp);
            Melon<Core>.Logger.Msg($"Recipe added: {WoodCount}x {wood.name} + {MetalCount}x {metal.name} + {TarpCount}x {tarp.name} + {tools.name} at the workbench.");
            return true;
        }

        // ---------- reflection helpers ----------
        static object Req(Type bpType, GearItem item, int count)
        {
            var arrType = bpType.GetProperty("m_RequiredGear").PropertyType;
            var elemType = arrType.GetGenericArguments()[0];
            var e = Activator.CreateInstance(elemType);
            elemType.GetProperty("m_Item").SetValue(e, item);
            elemType.GetProperty("m_Count").SetValue(e, count);
            var units = elemType.GetProperty("m_Units");
            if (units != null)
            {
                try { units.SetValue(e, Enum.Parse(units.PropertyType, "Count")); } catch (Exception) { /* keep default */ }
            }
            return e;
        }

        static void SetArray(object target, Type type, string prop, object[] elements)
        {
            var p = type.GetProperty(prop);
            var arr = Activator.CreateInstance(p.PropertyType, new object[] { (long)elements.Length });
            var indexer = p.PropertyType.GetProperty("Item", new[] { typeof(int) });
            for (int i = 0; i < elements.Length; i++) indexer.SetValue(arr, elements[i], new object[] { i });
            p.SetValue(target, arr);
        }

        // ---------- finding vanilla items ----------
        static GearItem FindGear(string[] exactNames, string keyword)
        {
            foreach (var n in exactNames)
            {
                var g = ColdBoxItem.LoadVanillaPrefab(n);
                if (g != null) return g;
            }
            string kw = keyword.ToLowerInvariant();
            foreach (var name in AllGearNames())
            {
                if (!name.StartsWith("GEAR_")) continue;
                string lower = name.ToLowerInvariant();
                string display = GearDisplay(name).Replace(" ", "").ToLowerInvariant();
                if (lower.Contains(kw) || display.Contains(kw))
                {
                    var g = ColdBoxItem.LoadVanillaPrefab(name);
                    if (g != null) { Melon<Core>.Logger.Msg("Ingredient '" + keyword + "' = " + name); return g; }
                }
            }
            return null;
        }

        static string GearDisplay(string prefabName)
        {
            try { return GearItem.GetGearDisplayName(prefabName) ?? ""; } catch (Exception) { return ""; }
        }

        static List<string> AllGearNames()
        {
            var set = new HashSet<string>();
            foreach (var d in Resources.FindObjectsOfTypeAll<GearItemData>()) if (d != null && !string.IsNullOrEmpty(d.name)) set.Add(d.name);
            foreach (var g in Resources.FindObjectsOfTypeAll<GearItem>()) if (g != null && !string.IsNullOrEmpty(g.name)) set.Add(g.name.Replace("(Clone)", "").Trim());
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static bool dumped;
        static void DumpGearNames()
        {
            if (dumped) return;
            try
            {
                var names = AllGearNames();
                dumped = names.Count > 100;   // an early, partial list is rewritten on the next attempt
                var sb = new StringBuilder();
                foreach (var n in names) sb.AppendLine(n + "\t" + GearDisplay(n));
                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "ColdBox");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "gear_names.txt"), sb.ToString());
            }
            catch (Exception) { /* diagnostics only */ }
        }
    }
}
