using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;
using MelonLoader;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// The craftable "Snow Cooler" inventory item. There is no Unity project, so the gear prefab is built at runtime
    /// by cloning a small vanilla gear item and swapping its visuals / data. The game finds gear prefabs by name
    /// (also when a save is loaded), so the lookups for <see cref="ItemName"/> are answered by Harmony patches below.
    /// </summary>
    internal static class ColdBoxItem
    {
        internal const string ItemName = "GEAR_ColdBoxKit";
        const float VisualScale = 1f;   // same size as the placed cooler

        // vanilla items used as the clone source (first one that loads wins)
        static readonly string[] BaseCandidates = { "GEAR_Hinge", "GEAR_ScrapMetal", "GEAR_Rope", "GEAR_Charcoal" };

        static GameObject prefabGo;
        static GearItem prefabGear;
        static bool failed;
        static GameObject holder;

        internal static bool IsKit(GearItem gi)
        {
            return gi != null && gi.gameObject != null && gi.gameObject.name.StartsWith(ItemName);
        }

        /// <summary>Gets (building it on first use) the prefab of the cooler item. Null if it could not be built.</summary>
        internal static GearItem GetPrefab()
        {
            if (prefabGear != null) return prefabGear;
            if (failed) return null;
            try
            {
                Build();
            }
            catch (Exception e)
            {
                failed = true;
                Melon<Core>.Logger.Error("Could not build the cooler item: " + e);
            }
            return prefabGear;
        }

        static GearItem LoadBase(out string usedName)
        {
            foreach (var n in BaseCandidates)
            {
                try
                {
                    var g = RealLoadPrefab(n);
                    if (g != null) { usedName = n; return g; }
                }
                catch (Exception) { /* try the next candidate */ }
            }
            usedName = null;
            return null;
        }

        // bypasses our own patch
        internal static bool BypassPatch;
        static GearItem RealLoadPrefab(string name)
        {
            BypassPatch = true;
            try { return GearItem.LoadGearItemPrefab(name); }
            finally { BypassPatch = false; }
        }

        internal static GearItem LoadVanillaPrefab(string name)
        {
            try { return RealLoadPrefab(name); }
            catch (Exception) { return null; }
        }

        static string step = "";
        static void Step(string name) { step = name; Melon<Core>.Logger.Msg("Cooler item: " + name); }

        /// <summary>The game fills GearItem.m_GearItemData in Awake, which never runs on our inactive clone; find the data any way we can.</summary>
        static GearItemData FindSourceData(GearItem baseGear)
        {
            GearItemData src = null;
            try { src = baseGear.GearItemData; } catch (Exception) { }
            if (src == null) { try { src = baseGear.m_GearItemData; } catch (Exception) { } }
            if (src != null) return src;

            // last resort: wake up a temporary copy (Awake loads the data), read it, throw the copy away
            var tmp = UnityEngine.Object.Instantiate(baseGear.gameObject);
            try
            {
                var tg = tmp.GetComponent<GearItem>();
                try { src = tg.m_GearItemData; } catch (Exception) { }
                if (src == null) { try { src = tg.GearItemData; } catch (Exception) { } }
            }
            finally { UnityEngine.Object.Destroy(tmp); }
            return src;
        }

        static void Build()
        {
            Step("loading a base prefab");
            var baseGear = LoadBase(out string baseName);
            if (baseGear == null)
            {
                failed = true;
                Melon<Core>.Logger.Error("Cooler item: no base gear prefab could be loaded; the item is unavailable.");
                return;
            }

            try
            {
                Step("cloning " + baseName);
                holder = new GameObject("ColdBox_ItemHolder");
                holder.SetActive(false);   // inactive parent: the clone never wakes up (no Awake, nothing registered in the scene)
                UnityEngine.Object.DontDestroyOnLoad(holder);

                Step("reading the item data of " + baseName);
                var src = FindSourceData(baseGear);
                Melon<Core>.Logger.Msg("Cooler item: source data " + (src != null ? "found" : "NOT found (a new one is created)"));

                var go = UnityEngine.Object.Instantiate(baseGear.gameObject, holder.transform);
                go.name = ItemName;
                go.transform.localScale = Vector3.one;

                Step("removing the vanilla behaviours");
                // keep only what a gear item needs; remove behaviours of the vanilla item (stacking, food, tool data...)
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    // GetComponents<Component>() returns wrappers typed as Component, so the real type is checked with TryCast
                    if (c.TryCast<Transform>() != null || c.TryCast<GearItem>() != null || c.TryCast<ObjectGuid>() != null || c.TryCast<Rigidbody>() != null) continue;
                    UnityEngine.Object.DestroyImmediate(c);
                }
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);

                Step("adding the cooler model");
                ColdBoxBuilder.AttachItemVisual(go.transform, VisualScale);
                ColdBoxBuilder.GetColliderBounds(out Vector3 size, out Vector3 center);
                var box = go.AddComponent<BoxCollider>();
                box.size = size * VisualScale;
                box.center = center * VisualScale;

                var gear = go.GetComponent<GearItem>();
                if (gear == null) throw new InvalidOperationException("the clone lost its GearItem component");

                Step("fixing the gear item references");
                // GearItem keeps lists of its renderers / rigidbody from the prefab it was cloned from. We destroyed
                // those objects, so the game would hit a NullReferenceException (GearItem.DisableTextureStreaming) when
                // adding the item to the inventory or destroying it. Point them at the new model instead.
                var rends = go.GetComponentsInChildren<MeshRenderer>(true);
                var rendArr = new Il2CppReferenceArray<MeshRenderer>(rends.Length);
                for (int i = 0; i < rends.Length; i++) rendArr[i] = rends[i];
                gear.m_MeshRenderers = rendArr;
                gear.m_SkinnedMeshRenderers = new Il2CppReferenceArray<SkinnedMeshRenderer>(0);
                var body = go.GetComponent<Rigidbody>();
                if (body == null) body = go.AddComponent<Rigidbody>();
                gear.m_RigidBody = body;

                Step("creating the item data");
                GearItemData data = src != null ? UnityEngine.Object.Instantiate(src) : ScriptableObject.CreateInstance<GearItemData>();
                data.name = ItemName;
                ApplyTexts(data);
                data.m_BaseWeight = ItemWeight.FromKilograms(10f);
                data.m_IsPlaceable = true;
                var tex = ColdBoxIcon.Get();   // rendered icon for the inventory grid
                if (tex != null)
                {
                    var iconData = ScriptableObject.CreateInstance<GearItemInventoryIconSimpleData>();
                    iconData.m_Icon = tex;
                    UnityEngine.Object.DontDestroyOnLoad(iconData);
                    data.m_IconData = iconData;
                }
                data.m_MaxHP = 100f;
                data.m_DailyHPDecay = 0f;
                UnityEngine.Object.DontDestroyOnLoad(data);
                gear.m_GearItemData = data;

                prefabGo = go;
                prefabGear = gear;
                Melon<Core>.Logger.Msg("Cooler item ready (cloned from " + baseName + ").");
            }
            catch (Exception e)
            {
                failed = true;
                Melon<Core>.Logger.Error("Cooler item failed at step '" + step + "': " + e);
            }
        }

        internal const string DescriptionKey = "A cooler box that is filled with snow to keep food cold for days. Place it, load it with snow, then store food inside.";

        /// <summary>Writes the name and description in the current interface language (new LocalizedString objects, so nothing stays cached).</summary>
        static void ApplyTexts(GearItemData data)
        {
            var title = new LocalizedString(); title.m_LocalizationID = ColdBoxLoc.T("Snow Cooler");
            var desc = new LocalizedString(); desc.m_LocalizationID = ColdBoxLoc.T(DescriptionKey);
            data.m_LocalizedName = title;
            data.m_LocalizedDescription = desc;
        }

        /// <summary>Re-applies the item name/description after the interface language changed (workbench list, inventory, tooltips).</summary>
        internal static void RefreshTexts()
        {
            try
            {
                if (prefabGear != null && prefabGear.m_GearItemData != null) ApplyTexts(prefabGear.m_GearItemData);
                foreach (var g in UnityEngine.Object.FindObjectsOfType<GearItem>())
                    if (g != null && g.m_GearItemData != null && g.m_GearItemData.name == ItemName) ApplyTexts(g.m_GearItemData);
            }
            catch (System.Exception e) { Melon<Core>.Logger.Warning("Could not refresh the item texts: " + e.Message); }
        }

        /// <summary>Creates a live copy of the cooler item in the world. Null if the item is unavailable.</summary>
        internal static GearItem Spawn(Transform parent = null)
        {
            if (GetPrefab() == null) return null;
            var inst = UnityEngine.Object.Instantiate(prefabGo);   // not under the inactive holder -> Awake runs now
            inst.name = ItemName;
            if (parent != null) inst.transform.SetParent(parent, false);
            inst.SetActive(true);
            var gi = inst.GetComponent<GearItem>();
            try { gi.ManualStart(); } catch (Exception) { /* some versions do it in Awake */ }
            return gi;
        }

        /// <summary>Puts one cooler item in the player's inventory.</summary>
        internal static bool GiveToPlayer()
        {
            try
            {
                var gi = Spawn();
                if (gi == null) return false;
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm != null) pm.AddItemToPlayerInventory(gi, false, true);
                else GameManager.GetInventoryComponent().AddGear(gi, true);
                return true;
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Error("Could not add the cooler item to the inventory: " + e);
                return false;
            }
        }

        internal static int CountInInventory()
        {
            var inv = GameManager.GetInventoryComponent();
            if (inv == null) return 0;
            var list = new Il2CppSystem.Collections.Generic.List<GearItem>();
            inv.GetGearItems((Il2CppSystem.Predicate<GearItem>)(g => IsKit(g)), list);
            return list.Count;
        }

        /// <summary>Inventory "Place" button pressed on a cooler item: close the inventory and start the placement mode.</summary>
        internal static void PlaceFromInventory(GearItem item)
        {
            try
            {
                var inv = InterfaceManager.GetPanel<Panel_Inventory>();
                if (inv != null) inv.Enable(false);
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Could not close the inventory: " + e.Message); }

            ColdBoxManager.Defer(() => ColdBoxManager.RequestNewPlacement(placed =>
            {
                if (!placed || item == null) return;
                try { GameManager.GetInventoryComponent().DestroyGear(item); }   // the item turned into the placed cooler
                catch (Exception e) { Melon<Core>.Logger.Error("Could not consume the cooler item: " + e); }
            }));
        }
    }

    // ---- the game looks gear prefabs up by name (crafting result, loading a save); answer for our item ----

    [HarmonyPatch(typeof(GearItem), nameof(GearItem.LoadGearItemPrefab))]
    internal static class LoadGearItemPrefabPatch
    {
        static bool Prefix(string name, ref GearItem __result)
        {
            if (ColdBoxItem.BypassPatch || name != ColdBoxItem.ItemName) return true;
            __result = ColdBoxItem.GetPrefab();
            return __result == null;   // if our item is unavailable let the game run (it will report a missing prefab)
        }
    }

    [HarmonyPatch(typeof(GearItem), nameof(GearItem.InstantiateGearItem), new[] { typeof(string) })]
    internal static class InstantiateGearItemPatch
    {
        static bool Prefix(string prefabName, ref GearItem __result)
        {
            if (prefabName != ColdBoxItem.ItemName) return true;
            __result = ColdBoxItem.Spawn();
            return __result == null;
        }
    }

    [HarmonyPatch(typeof(GearItem), nameof(GearItem.InstantiateAndDeserializeGearItem),
        new[] { typeof(string), typeof(string), typeof(Transform), typeof(bool), typeof(bool) })]
    internal static class InstantiateAndDeserializePatch
    {
        static bool Prefix(string prefabName, Transform parent, ref GearItem __result)
        {
            if (prefabName != ColdBoxItem.ItemName) return true;
            __result = ColdBoxItem.Spawn(parent);   // the item has no state worth restoring
            return __result == null;
        }
    }

    internal static class InventoryPlacePatch
    {
        internal static bool Handle(Panel_Inventory __instance)
        {
            try
            {
                var sel = __instance.GetCurrentlySelectedItem();
                var gi = sel != null ? sel.m_GearItem : null;
                if (!ColdBoxItem.IsKit(gi)) return true;
                ColdBoxItem.PlaceFromInventory(gi);
                return false;
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Warning("Place patch: " + e.Message);
                return true;
            }
        }
    }

    // the inventory shows "Use" for this item; make it start the placement too
    [HarmonyPatch(typeof(Panel_Inventory), nameof(Panel_Inventory.OnEquip))]
    internal static class InventoryEquipPatch
    {
        static bool Prefix(Panel_Inventory __instance) { return InventoryPlacePatch.Handle(__instance); }
    }

    [HarmonyPatch(typeof(Panel_Inventory), nameof(Panel_Inventory.OnPlace))]
    internal static class InventoryPlacePatch2
    {
        static bool Prefix(Panel_Inventory __instance) { return InventoryPlacePatch.Handle(__instance); }
    }

    // Crafting normally puts the result in the inventory by itself. If that did not happen for our recipe
    // (the game could not instantiate the runtime-built prefab), the item is added here as a safety net.
    [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.OnCraftingSuccess))]
    internal static class CraftingSuccessPatch
    {
        static void Prefix(Panel_Crafting __instance)
        {
            try
            {
                var bp = __instance.SelectedBPI;
                if (bp == null || bp.name != "BLUEPRINT_ColdBox") return;
                int before = ColdBoxItem.CountInInventory();
                Melon<Core>.Logger.Msg("Crafting finished; coolers in the inventory before: " + before);
                ColdBoxManager.DeferSeconds(1.0f, () =>
                {
                    int after = ColdBoxItem.CountInInventory();
                    Melon<Core>.Logger.Msg("Coolers in the inventory after crafting: " + after);
                    if (after > before) return;
                    if (ColdBoxItem.GiveToPlayer()) GameBridge.ShowMessage("Snow Cooler added to your inventory.");
                    else Melon<Core>.Logger.Error("The crafted cooler could not be added to the inventory.");
                });
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Crafting patch: " + e.Message); }
        }
    }
}
