using System;
using HarmonyLib;
using Il2CppTLD.Gear;
using MelonLoader;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Inventory / crafting icon: the cooler model is photographed once with a temporary camera into a texture.
    /// Everything used for the shot (camera, lights, model) lives on an unused layer and is destroyed afterwards.
    /// </summary>
    internal static class ColdBoxIcon
    {
        const int Size = 256;
        const int ShotLayer = 31;
        static Texture2D itemIcon, listIcon;
        static bool itemFailed;

        /// <summary>Picture of the item (inventory grid, crafting detail): Resources\crafteo.png.</summary>
        internal static Texture2D Get()
        {
            if (itemIcon != null) return itemIcon;
            if (itemFailed) return null;
            itemIcon = LoadEmbedded("ColdBox.crafteo.png");   // hand-made colour picture; render the model only as a fallback
            if (itemIcon != null) return itemIcon;
            try { itemIcon = Render(); }
            catch (Exception e)
            {
                itemFailed = true;
                Melon<Core>.Logger.Error("Could not render the cooler icon: " + e);
            }
            return itemIcon;
        }

        /// <summary>White glyph used in the crafting list (Resources\iconoblanco.png).</summary>
        internal static Texture2D GetList()
        {
            if (listIcon == null) listIcon = LoadEmbedded("ColdBox.iconoblanco.png");
            return listIcon;
        }

        /// <summary>A hand-made image embedded in the DLL.</summary>
        internal static Texture2D LoadEmbedded(string resource)
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (s == null) return null;
                    var ms = new System.IO.MemoryStream();
                    s.CopyTo(ms);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(tex, (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)ms.ToArray())) return null;
                    tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    UnityEngine.Object.DontDestroyOnLoad(tex);
                    Melon<Core>.Logger.Msg("Cooler icon loaded: " + resource);
                    return tex;
                }
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Warning("Embedded icon failed, rendering one instead: " + e.Message);
                return null;
            }
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++) SetLayer(go.transform.GetChild(i).gameObject, layer);
        }

        static Texture2D Render()
        {
            var studio = new GameObject("ColdBox_IconStudio");
            studio.transform.position = new Vector3(0f, -5000f, 0f);
            RenderTexture rt = null;
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(studio.transform, false);
                if (!ColdBoxBuilder.AttachItemVisual(model.transform, 1f)) throw new InvalidOperationException("model resources not loaded");
                SetLayer(studio, ShotLayer);

                // key light with soft shadows (the box, the tarp and the jugs shade each other), plus a weak fill light
                var lightGo = new GameObject("Light"); lightGo.transform.SetParent(studio.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(48f, 25f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.85f;
                light.cullingMask = 1 << ShotLayer;
                var fillGo = new GameObject("Fill"); fillGo.transform.SetParent(studio.transform, false);
                fillGo.transform.rotation = Quaternion.Euler(25f, -120f, 0f);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.45f;
                fill.shadows = LightShadows.None;
                fill.cullingMask = 1 << ShotLayer;

                var camGo = new GameObject("Camera"); camGo.transform.SetParent(studio.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;                       // rendered by hand below
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << ShotLayer;
                cam.orthographic = true;
                cam.orthographicSize = 1.05f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 20f;
                // side view of the long face (faucet side, jugs on the left), slightly from above and from the left
                camGo.transform.localRotation = Quaternion.Euler(24f, -62f, 0f);
                var target = new Vector3(0.04f, 0.33f, -0.19f);   // center of the box + jugs
                camGo.transform.localPosition = target - camGo.transform.localRotation * Vector3.forward * 6f;

                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                UnityEngine.Object.DontDestroyOnLoad(tex);
                Melon<Core>.Logger.Msg("Cooler icon rendered.");
                return tex;
            }
            finally
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                UnityEngine.Object.Destroy(studio);
            }
        }
    }

    /// <summary>The crafting menu asks the blueprint for its icon; ours has no addressable texture, so answer with the rendered one.</summary>
    [HarmonyPatch(typeof(BlueprintData), nameof(BlueprintData.GetCraftingIcon))]
    internal static class CraftingIconPatch
    {
        static bool Prefix(BlueprintData __instance, ref Texture2D __result)
        {
            if (__instance == null || __instance.name != "BLUEPRINT_ColdBox") return true;
            var t = ColdBoxIcon.Get();
            if (t == null) return true;
            __result = t;
            return false;
        }
    }

    /// <summary>The crafting LIST shows a small white glyph (BlueprintDisplayItem.m_Icon), loaded from an addressable we do not have.</summary>
    [HarmonyPatch(typeof(Il2Cpp.BlueprintDisplayItem), nameof(Il2Cpp.BlueprintDisplayItem.Setup))]
    internal static class CraftingListIconPatch
    {
        static void Postfix(Il2Cpp.BlueprintDisplayItem __instance, BlueprintData bpi)
        {
            try
            {
                if (bpi == null || bpi.name != "BLUEPRINT_ColdBox" || __instance.m_Icon == null) return;
                var t = ColdBoxIcon.GetList();
                if (t == null) return;
                __instance.m_Icon.mainTexture = t;
                __instance.m_Icon.gameObject.SetActive(true);
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("List icon patch: " + e.Message); }
        }
    }

    /// <summary>The big picture of the selected blueprint (middle of the crafting screen).</summary>
    [HarmonyPatch(typeof(Il2Cpp.Panel_Crafting), nameof(Il2Cpp.Panel_Crafting.RefreshSelectedBlueprint))]
    internal static class CraftingDetailImagePatch
    {
        static void Postfix(Il2Cpp.Panel_Crafting __instance)
        {
            try
            {
                var bp = __instance.SelectedBPI;
                if (bp == null || bp.name != "BLUEPRINT_ColdBox" || __instance.m_SelectedImage == null) return;
                var t = ColdBoxIcon.Get();
                if (t == null) return;
                __instance.m_SelectedImage.mainTexture = t;
                __instance.m_SelectedImage.gameObject.SetActive(true);
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Detail image patch: " + e.Message); }
        }
    }
}
