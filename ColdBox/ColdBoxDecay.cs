using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Slows down the decay of every item stored in a cooler that still has snow.
    /// Items are identified by GearItem.m_InstanceID; the set is refreshed a few times per second.
    /// </summary>
    internal static class ColdBoxDecay
    {
        internal static readonly HashSet<int> ColdItemIds = new HashSet<int>();
        static float nextRefresh;

        internal static void Refresh()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;

            ColdItemIds.Clear();
            var items = new Il2CppSystem.Collections.Generic.List<GearItem>();
            foreach (var box in ColdBoxManager.Boxes)
            {
                if (box == null || !box.IsCold()) continue;
                var container = box.GetContainer();
                if (container == null) continue;
                items.Clear();
                container.GetItems(items);
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null) ColdItemIds.Add(items[i].m_InstanceID);
            }
        }
    }

    [HarmonyPatch(typeof(GearItem), nameof(GearItem.DecayOverTODHours))]
    internal static class GearItemDecayPatch
    {
        static void Prefix(GearItem __instance, ref float scale)
        {
            if (ColdBoxDecay.ColdItemIds.Count == 0 || __instance == null) return;
            if (ColdBoxDecay.ColdItemIds.Contains(__instance.m_InstanceID))
                scale *= ColdBoxSettings.Instance.decayMultiplier;
        }
    }
}
