using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Picks icons for the game's action picker. Sprite names live in the game's UI atlas and are not part of any DLL,
    /// so they are discovered at runtime by keyword. The full list is written to UserData\ColdBox\sprite_names.txt
    /// so the keywords can be tuned without guessing.
    /// </summary>
    internal static class ColdBoxIcons
    {
        static List<string> names;

        static void Build()
        {
            if (names != null) return;
            names = new List<string>();
            try
            {
                UIAtlas atlas = null;

                // best source: the atlas the picker's own icons use
                var picker = InterfaceManager.GetPanel<Panel_ActionPicker>();
                var items = picker == null ? null : picker.m_ActionPickerItemList;
                if (items != null)
                    for (int i = 0; i < items.Length && atlas == null; i++)
                        if (items[i] != null && items[i].m_Sprite != null) atlas = items[i].m_Sprite.atlas;

                // fallback: the atlas with the most "ico..." sprites
                if (atlas == null)
                {
                    int best = -1;
                    foreach (var a in Resources.FindObjectsOfTypeAll<UIAtlas>())
                    {
                        if (a == null || a.spriteList == null) continue;
                        int count = 0;
                        for (int i = 0; i < a.spriteList.Count; i++)
                            if (a.spriteList[i].name != null && a.spriteList[i].name.StartsWith("ico", StringComparison.OrdinalIgnoreCase)) count++;
                        if (count > best) { best = count; atlas = a; }
                    }
                }

                if (atlas == null || atlas.spriteList == null) { Melon<Core>.Logger.Warning("No UI atlas found for action picker icons."); return; }
                for (int i = 0; i < atlas.spriteList.Count; i++)
                    if (!string.IsNullOrEmpty(atlas.spriteList[i].name)) names.Add(atlas.spriteList[i].name);

                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "ColdBox");
                Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "sprite_names.txt"), names);
                Melon<Core>.Logger.Msg("Action picker atlas has " + names.Count + " sprites (list saved to UserData\\ColdBox\\sprite_names.txt).");
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Could not read the UI atlas: " + e.Message); }
        }

        // ---------------- custom pictures for some picker entries ----------------
        // The picker only knows atlas sprites, so our own pictures are drawn as an NGUI UITexture on top of the entry's
        // sprite (which is hidden meanwhile). Overlays are dropped as soon as the picker shows something else.

        sealed class Overlay
        {
            public UISprite sprite;
            public GameObject go;
            public string expectedName;
            public ActionPickerItem item;
        }

        static readonly List<Overlay> overlays = new List<Overlay>();
        static readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();

        static string PictureFor(string label)
        {
            if (label.StartsWith("Cover")) return "ColdBox.icon_snow.png";
            if (label.StartsWith("Close")) return "ColdBox.icon_close.png";
            return null;
        }

        /// <summary>Call shortly after the picker was shown with <paramref name="labels"/> / <paramref name="spriteNames"/> (same order).</summary>
        internal static void ApplyCustomPictures(List<string> labels, List<string> spriteNames)
        {
            ClearOverlays();
            try
            {
                var picker = InterfaceManager.GetPanel<Panel_ActionPicker>();
                var items = picker == null ? null : picker.m_ActionPickerItemList;
                if (items == null) return;
                for (int i = 0; i < labels.Count && i < items.Length; i++)
                {
                    string res = PictureFor(labels[i]);
                    var item = items[i];
                    if (res == null || item == null || item.m_Sprite == null) continue;
                    if (!pictures.TryGetValue(res, out var tex)) pictures[res] = tex = ColdBoxIcon.LoadEmbedded(res);
                    if (tex == null) continue;

                    var spr = item.m_Sprite;
                    var go = new GameObject("ColdBox_Picture");
                    try
                    {
                        go.layer = spr.gameObject.layer;
                        go.transform.SetParent(spr.transform.parent, false);
                        go.transform.localPosition = spr.transform.localPosition;
                        go.transform.localRotation = spr.transform.localRotation;
                        go.transform.localScale = spr.transform.localScale;
                        var ut = go.AddComponent<UITexture>();
                        ut.mainTexture = tex;
                        ut.width = spr.width;
                        ut.height = spr.height;
                        ut.depth = spr.depth + 1;
                        spr.enabled = false;
                        overlays.Add(new Overlay { sprite = spr, go = go, item = item, expectedName = spriteNames[i] });
                    }
                    catch
                    {
                        UnityEngine.Object.Destroy(go);
                        spr.enabled = true;
                        throw;
                    }
                }
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Custom menu pictures failed: " + e.Message); }
        }

        /// <summary>Every frame: drop the overlays when the picker closed or now shows other entries.</summary>
        internal static void ValidateOverlays()
        {
            if (overlays.Count == 0) return;
            bool stale = false;
            foreach (var o in overlays)
            {
                if (o.item == null || o.sprite == null || o.go == null || !o.item.gameObject.activeInHierarchy || o.sprite.spriteName != o.expectedName) { stale = true; break; }
            }
            if (stale) ClearOverlays();
        }

        static void ClearOverlays()
        {
            foreach (var o in overlays)
            {
                try
                {
                    if (o.go != null) UnityEngine.Object.Destroy(o.go);
                    if (o.sprite != null) o.sprite.enabled = true;
                }
                catch { }
            }
            overlays.Clear();
        }

        // ---------------- frozen-food badge inside the cooler ----------------
        // The game marks frozen food with blue icicles. Inside the Snow Cooler panel they are covered with our picture.
        // The game's own widget is only made transparent (never disabled or destroyed) so its show/hide logic keeps working.

        sealed class Badge
        {
            public UIWidget widget;
            public GameObject go;
            public float alpha;
        }

        static readonly Dictionary<int, Badge> badges = new Dictionary<int, Badge>();
        const float BadgeScale = 0.75f;   // size of our picture relative to the game's frozen icon
        static float nextBadgeScan;
        static bool badgeDumped;

        static bool IsFrozenIcon(UIWidget w)
        {
            if (w.gameObject.name != null && w.gameObject.name.StartsWith("ColdBox_")) return false;   // never our own pictures
            var sp = w.TryCast<UISprite>();
            if (sp != null && string.Equals(sp.spriteName, "ico_inv_ItemFrozen", StringComparison.OrdinalIgnoreCase)) return true;
            if (sp == null && w.TryCast<UITexture>() == null) return false;   // labels and the like never count
            if (w.width > 64 || w.height > 64) return false;
            string own = w.gameObject.name ?? "";
            string par = w.transform.parent != null ? (w.transform.parent.name ?? "") : "";
            if (own.IndexOf("frozen", StringComparison.OrdinalIgnoreCase) >= 0 || par.IndexOf("frozen", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // the icicles are bright cyan: a small cyan sprite inside the item grid is treated as the frozen icon
            var c = w.color;
            return w.width <= 48 && w.height <= 48 && c.r < 0.35f && c.g > 0.55f && c.b > 0.8f && c.a > 0.2f;
        }

        static string PathOf(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; t != null && i < 9; i++, t = t.parent) sb.Insert(0, "/" + t.name);
            return sb.ToString();
        }

        /// <summary>Call every frame. <paramref name="active"/> = the Snow Cooler's storage panel is the one that is open.</summary>
        internal static void UpdateFrozenBadges(bool active)
        {
            try
            {
                var panel = active ? InterfaceManager.GetPanel<Panel_Container>() : null;
                if (panel == null || !panel.gameObject.activeInHierarchy)
                {
                    if (badges.Count > 0) ClearBadges();
                    badgeDumped = false;
                    return;
                }

                // keep the game's icicles invisible (it may re-colour them) and show our badge only while the game shows its own
                foreach (var b in badges.Values)
                {
                    if (b.widget == null || b.go == null) continue;
                    bool shown = b.widget.enabled && b.widget.gameObject.activeInHierarchy;
                    if (b.go.activeSelf != shown) b.go.SetActive(shown);
                    if (shown && b.widget.alpha > 0.001f) b.widget.alpha = 0f;
                }

                if (Time.unscaledTime < nextBadgeScan) return;
                nextBadgeScan = Time.unscaledTime + 0.25f;

                var dump = badgeDumped ? null : new List<string>();
                foreach (var w in UnityEngine.Object.FindObjectsOfType<UIWidget>())
                {
                    if (w == null || !w.gameObject.activeInHierarchy) continue;
                    if (dump != null && w.transform.IsChildOf(panel.transform))
                    {
                        var sp = w.TryCast<UISprite>();
                        string what = sp != null ? "sprite:" + sp.spriteName : (w.TryCast<UITexture>() != null ? "texture" : "other");
                        if (w.width <= 64 && w.height <= 64 && !w.gameObject.name.StartsWith("ColdBox_"))
                        {
                            var c = w.color;
                            dump.Add(PathOf(w.transform) + " | " + what + " | " + w.width + "x" + w.height + " | " + (w.enabled ? "on" : "off")
                                + " | rgba " + c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + "," + c.a.ToString("0.00"));
                        }
                    }
                    if (!w.enabled) continue;
                    int id = w.GetInstanceID();
                    if (badges.ContainsKey(id) || !IsFrozenIcon(w)) continue;
                    MakeBadge(w, id);
                }

                if (dump != null)
                {
                    badgeDumped = true;
                    string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "ColdBox");
                    Directory.CreateDirectory(dir);
                    File.WriteAllLines(Path.Combine(dir, "container_widgets.txt"), dump);
                    // also into the log, so Latest.log alone is enough to diagnose
                    Melon<Core>.Logger.Msg("Container panel: " + dump.Count + " small widgets");
                    for (int i = 0; i < dump.Count && i < 120; i++) Melon<Core>.Logger.Msg("W " + dump[i]);
                }
            }
            catch (Exception e) { Melon<Core>.Logger.Warning("Frozen badge update failed: " + e.Message); }
        }

        static void MakeBadge(UIWidget w, int id)
        {
            const string res = "ColdBox.icon_frozen.png";
            if (!pictures.TryGetValue(res, out var tex)) pictures[res] = tex = ColdBoxIcon.LoadEmbedded(res);
            if (tex == null) return;

            var go = new GameObject("ColdBox_FrozenBadge");
            try
            {
                go.layer = w.gameObject.layer;
                go.transform.SetParent(w.transform.parent, false);
                go.transform.localPosition = w.transform.localPosition;
                go.transform.localRotation = w.transform.localRotation;
                go.transform.localScale = w.transform.localScale;
                var ut = go.AddComponent<UITexture>();
                ut.mainTexture = tex;
                ut.width = Mathf.Max(8, Mathf.RoundToInt(w.width * BadgeScale));   // same centre, smaller than the game's icon
                ut.height = Mathf.Max(8, Mathf.RoundToInt(w.height * BadgeScale));
                ut.depth = w.depth + 5;
                var badge = new Badge { widget = w, go = go, alpha = w.alpha };
                w.alpha = 0f;
                badges[id] = badge;
                Melon<Core>.Logger.Msg("Frozen badge attached to " + PathOf(w.transform) + " (" + w.width + "x" + w.height + ", depth " + w.depth + ").");
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
                throw;
            }
        }

        static void ClearBadges()
        {
            foreach (var b in badges.Values)
            {
                try
                {
                    if (b.go != null) UnityEngine.Object.Destroy(b.go);
                    if (b.widget != null) b.widget.alpha = b.alpha;
                }
                catch { }
            }
            badges.Clear();
        }

        /// <summary>Returns the first sprite whose name contains one of the keywords (in priority order). Never returns an empty name if the atlas has any icon.</summary>
        internal static string Pick(params string[] keywords)
        {
            Build();
            foreach (string kw in keywords)
            {
                string found = null;
                foreach (string n in names)
                {
                    if (n.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool isIcon = n.StartsWith("ico", StringComparison.OrdinalIgnoreCase);
                    if (found == null || (isIcon && !found.StartsWith("ico", StringComparison.OrdinalIgnoreCase)) || (isIcon == found.StartsWith("ico", StringComparison.OrdinalIgnoreCase) && n.Length < found.Length)) found = n;
                }
                if (found != null) return found;
            }
            foreach (string n in names) if (n.StartsWith("ico", StringComparison.OrdinalIgnoreCase)) return n;   // any icon beats a black square
            return "";
        }
    }
}
