using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTLD.Gear;
using Il2CppTLD.IntBackedUnit;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Turns the cooler into a real game container (Il2Cpp.Container): inventory UI, weight limit and item handling
    /// are all done by the game. Contents are saved by ColdBoxSave, not by the game's own serialization.
    /// </summary>
    internal static class ColdBoxContainer
    {
        /// <summary>Sets the container title in the current interface language (unknown IDs are shown as plain text).</summary>
        internal static void RefreshTitle(Container c)
        {
            if (c == null) return;
            var title = new LocalizedString();
            title.m_LocalizationID = ColdBoxLoc.T("Snow Cooler");
            c.m_LocalizedDisplayName = title;
        }

        /// <summary>Adds and configures the Container. Call it while the root object is still INACTIVE so Awake sees the final values.</summary>
        internal static Container Attach(GameObject root)
        {
            var c = root.AddComponent<Container>();

            RefreshTitle(c);

            c.m_StartInspected = true;       // skip the "searching" animation
            c.m_Inspected = true;
            c.m_NotPopulated = false;        // never generate loot
            c.m_ChanceEmpty = 1f;
            c.m_AllowDropAll = true;
            c.m_DisableSerialization = true; // the mod saves the contents itself (the game would need a scene GUID)
            c.m_DecayScalar = 1f;            // cold is applied by ColdBoxDecay, not by this field

            // the game's own inventory filter: only food is listed, so only food can be moved in
            c.m_OnlyShowSpecifiedGearType = true;
            c.m_ShowOnlyGearType = GearType.Food;
            c.m_DefaultFilter = InventoryFilterType.Food;
            c.m_FilterLocked = true;

            ApplyCapacity(c);
            EnsureSafeDefaults(c);
            return c;
        }

        /// <summary>
        /// A Container normally comes from a Unity prefab with every field filled in. Ours is built from code, so
        /// anything the game dereferences without a null check has to be filled in here. The log showed a
        /// NullReferenceException inside Container.BeginContainerClose (called from Panel_Container.OnDone)
        /// that made the inventory impossible to close.
        /// </summary>
        internal static void EnsureSafeDefaults(Container c)
        {
            if (c.m_ObjectAnims == null) c.m_ObjectAnims = new Il2CppReferenceArray<ObjectAnim>(0);
            if (c.m_DynamicMeshPieces == null) c.m_DynamicMeshPieces = new Il2CppSystem.Collections.Generic.List<GameObject>();
            if (c.m_GearToInstantiate == null) c.m_GearToInstantiate = new Il2CppSystem.Collections.Generic.List<GameObject>();
            if (c.m_DecorationItems == null) c.m_DecorationItems = new Il2CppSystem.Collections.Generic.List<DecorationItem>();
            if (c.m_ScriptedItemPrefabs == null) c.m_ScriptedItemPrefabs = new Il2CppReferenceArray<GameObject>(0);

            // audio event names: use the ones of a vanilla container (or empty strings, never null)
            GameBridge.GetVanillaContainerAudio(out string open, out string close, out string search);
            if (string.IsNullOrEmpty(c.m_OpenAudio)) c.m_OpenAudio = open;
            if (string.IsNullOrEmpty(c.m_CloseAudio)) c.m_CloseAudio = close;
            if (string.IsNullOrEmpty(c.m_SearchAudio)) c.m_SearchAudio = search;
        }

        internal static void ApplyCapacity(Container c)
        {
            c.m_Capacity = ItemWeight.FromKilograms(ColdBoxSettings.Instance.capacityKg);
        }

        internal static bool IsEmpty(Container c)
        {
            var list = new Il2CppSystem.Collections.Generic.List<GearItem>();
            c.GetItems(list);
            return list.Count == 0;
        }
    }
}
